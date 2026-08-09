#include "HapbeatSender.h"

#include "HapbeatModLog.h"

// VERIFY-4.16: 4.16 header locations for the socket stack.
#include "Common/UdpSocketBuilder.h"
#include "Containers/StringConv.h"   // TCHAR_TO_UTF8 / UTF8_TO_TCHAR
#include "HAL/PlatformProcess.h"     // FPlatformProcess::ComputerName
#include "HAL/PlatformTime.h"
#include "Interfaces/IPv4/IPv4Address.h"
#include "Sockets.h"
#include "SocketSubsystem.h"

// ---------------------------------------------------------------------------
// VERIFY-4.16: FUdpSocketReceiver::Start()
//
// In current engines the receiver's worker thread is started by an explicit
// Start() call; in the UE4.16 generation the CONSTRUCTOR started it and there was
// no Start(). If the build fails with "class FUdpSocketReceiver has no member
// named 'Start'", set this to 0 — the thread is then already running and nothing
// else changes.
// ---------------------------------------------------------------------------
#ifndef HAPBEAT_UDP_RECEIVER_HAS_START
	#define HAPBEAT_UDP_RECEIVER_HAS_START 1
#endif

const double FHapbeatSender::KnownDeviceTtlSeconds = 15.0;
const float  FHapbeatSender::KeepAliveIntervalSeconds = 2.0f;

namespace
{
	std::string ToStd(const FString& S)
	{
		return std::string(TCHAR_TO_UTF8(*S));
	}

	FString ToFString(const std::string& S)
	{
		return FString(UTF8_TO_TCHAR(S.c_str()));
	}

	int64 NowMicros()
	{
		return static_cast<int64>(FPlatformTime::Seconds() * 1000000.0);
	}
}

FHapbeatSender::FHapbeatSender()
	: Socket(nullptr)
	, Receiver(nullptr)
	, Seq(0)
{
}

FHapbeatSender::~FHapbeatSender()
{
	Close();
}

bool FHapbeatSender::Open(const FHapbeatModConfig& InConfig)
{
	Close();
	Config = InConfig;

	if (!Config.bEnabled)
	{
		UE_LOG(LogHapbeatMod, Log, TEXT("Hapbeat is disabled in settings; no socket opened."));
		return false;
	}

	// Bind to an OS-assigned local port so the device's PONG (which it unicasts
	// back to the source addr/port of our PING) lands on THIS socket.
	Socket = FUdpSocketBuilder(TEXT("HapbeatModUDP"))
		.AsReusable()
		.AsNonBlocking()
		.WithBroadcast()
		.BoundToAddress(FIPv4Address::Any)
		.BoundToPort(0)
		.WithReceiveBufferSize(64 * 1024)
		.Build();

	if (Socket == nullptr)
	{
		UE_LOG(LogHapbeatMod, Warning, TEXT("Failed to create the Hapbeat UDP socket."));
		return false;
	}

	// Device knowledge is per-connection: after a reconnect the previously-seen
	// IPs may belong to a different network entirely, and a stale non-empty set
	// would suppress the broadcast fallback (total silence on the new network).
	{
		FScopeLock Lock(&DeviceLock);
		DevicePongTimes.Empty();
		DeviceAddresses.Empty();
	}
	LastFireTimes.Empty();

	// The receiver polls with a 100 ms wait and simply continues when a RecvFrom
	// fails, so ICMP "port unreachable" feedback from a powered-off device (which
	// on Windows surfaces as an error on the NEXT receive) cannot kill the thread.
	// That is the same failure the other SDKs suppress with SIO_UDP_CONNRESET;
	// FSocket exposes no ioctl, so we rely on the receiver's non-fatal loop.
	Receiver = new FUdpSocketReceiver(Socket, FTimespan::FromMilliseconds(100.0), TEXT("HapbeatModReceiver"));
	Receiver->OnDataReceived().BindRaw(this, &FHapbeatSender::HandleReceived);
#if HAPBEAT_UDP_RECEIVER_HAS_START
	Receiver->Start();
#endif

	KeepAliveHandle = FTicker::GetCoreTicker().AddTicker(
		FTickerDelegate::CreateRaw(this, &FHapbeatSender::TickKeepAlive),
		KeepAliveIntervalSeconds);

	// Announce immediately rather than waiting a full keep-alive tick: until a
	// device PONGs it is invisible to the unicast routing, so anything fired in
	// that window would broadcast while later commands unicast.
	SendPing();
	SendConnectStatus(true);

	UE_LOG(LogHapbeatMod, Log, TEXT("Hapbeat sender open (port %d, app '%s', unicast %s)."),
		Config.Port, *Config.AppName, Config.bCommandUnicast ? TEXT("on") : TEXT("off"));
	return true;
}

void FHapbeatSender::Close()
{
	if (Socket == nullptr)
	{
		return;
	}

	// Let the device clear its display immediately instead of waiting for a
	// presence timeout. Fire-and-forget: teardown can race with shutdown.
	SendConnectStatus(false);

	if (KeepAliveHandle.IsValid())
	{
		FTicker::GetCoreTicker().RemoveTicker(KeepAliveHandle);
		KeepAliveHandle.Reset();
	}

	// Tear the receiver down BEFORE the socket: its destructor stops and joins the
	// worker thread, which is what guarantees no callback touches Socket (or this
	// object) afterwards.
	if (Receiver != nullptr)
	{
		Receiver->Stop(); // explicit; do not rely on the dtor joining across engine versions
		delete Receiver;
		Receiver = nullptr;
	}

	ISocketSubsystem* SocketSubsystem = ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM);
	if (SocketSubsystem != nullptr)
	{
		Socket->Close();
		SocketSubsystem->DestroySocket(Socket);
	}
	Socket = nullptr;

	{
		FScopeLock Lock(&DeviceLock);
		DevicePongTimes.Empty();
		DeviceAddresses.Empty();
	}

	UE_LOG(LogHapbeatMod, Log, TEXT("Hapbeat sender closed."));
}

void FHapbeatSender::ApplyConfig(const FHapbeatModConfig& InConfig)
{
	const bool bNeedsReopen =
		(InConfig.Port != Config.Port) ||
		(InConfig.bEnabled != Config.bEnabled) ||
		(Socket == nullptr && InConfig.bEnabled);

	if (bNeedsReopen)
	{
		Open(InConfig);
		return;
	}

	Config = InConfig;
	UE_LOG(LogHapbeatMod, Log, TEXT("Hapbeat settings reloaded (%d event bindings)."), Config.Events.Num());
}

uint16 FHapbeatSender::NextSeq()
{
	// Game thread only (Blueprint calls and the core ticker both run there), so
	// no lock is needed. The receiver thread never allocates a sequence number.
	return Seq++;
}

std::string FHapbeatSender::ResolvedTargetForSend() const
{
	// This mod never addresses individual body positions, so the base target is
	// empty ("everything") and only the player/group override shapes it.
	return hapbeat::ResolveTarget(std::string(),
		hapbeat::NormalizeOverride(Config.Player),
		hapbeat::NormalizeOverride(Config.Group));
}

bool FHapbeatSender::Fire(const FString& LogicalEvent)
{
	if (Socket == nullptr || LogicalEvent.IsEmpty())
	{
		return false;
	}

	const FHapbeatEventBinding* Binding = Config.FindEvent(LogicalEvent);
	if (Binding == nullptr)
	{
		UE_LOG(LogHapbeatMod, Warning,
			TEXT("Unknown logical event '%s' (not in the settings file)."), *LogicalEvent);
		return false;
	}
	if (!Binding->bEnabled)
	{
		return false;
	}

	if (Config.MinIntervalMs > 0)
	{
		const double Now = FPlatformTime::Seconds();
		const double MinGap = Config.MinIntervalMs / 1000.0;
		if (const double* Last = LastFireTimes.Find(LogicalEvent))
		{
			if (Now - *Last < MinGap)
			{
				return false; // rate limited
			}
		}
		LastFireTimes.Add(LogicalEvent, Now);
	}

	const std::string Target = ResolvedTargetForSend();
	// targetTime 0 = play immediately (message-format.md §8).
	const hapbeat::Bytes Payload = hapbeat::BuildPlayPayload(
		ToStd(Binding->EventId), 0, Config.MasterGain * Binding->Gain, Target);
	const hapbeat::Bytes Packet = hapbeat::BuildPacket(hapbeat::kCmdPlay, NextSeq(), Payload);
	if (Packet.empty())
	{
		UE_LOG(LogHapbeatMod, Warning, TEXT("PLAY packet for '%s' exceeds the 512-byte limit; dropped."),
			*Binding->EventId);
		return false;
	}

	SendCommand(Packet, Target);
	return true;
}

bool FHapbeatSender::FireStop(const FString& LogicalEvent)
{
	if (Socket == nullptr || LogicalEvent.IsEmpty())
	{
		return false;
	}

	const FHapbeatEventBinding* Binding = Config.FindEvent(LogicalEvent);
	if (Binding == nullptr)
	{
		return false;
	}

	const std::string Target = ResolvedTargetForSend();
	const hapbeat::Bytes Packet = hapbeat::BuildPacket(hapbeat::kCmdStop, NextSeq(),
		hapbeat::BuildStopPayload(ToStd(Binding->EventId), Target));
	if (Packet.empty())
	{
		return false;
	}

	SendCommand(Packet, Target);
	return true;
}

void FHapbeatSender::StopAll()
{
	if (Socket == nullptr)
	{
		return;
	}

	const std::string Target = ResolvedTargetForSend();
	const hapbeat::Bytes Packet = hapbeat::BuildPacket(hapbeat::kCmdStopAll, NextSeq(),
		hapbeat::BuildStopAllPayload(Target));
	if (!Packet.empty())
	{
		SendCommand(Packet, Target);
	}
}

int32 FHapbeatSender::GetAliveDeviceCount() const
{
	const double Now = FPlatformTime::Seconds();
	int32 Count = 0;

	FScopeLock Lock(&DeviceLock);
	for (TMap<FString, double>::TConstIterator It(DevicePongTimes); It; ++It)
	{
		if (Now - It.Value() <= KnownDeviceTtlSeconds)
		{
			++Count;
		}
	}
	return Count;
}

bool FHapbeatSender::TickKeepAlive(float /*DeltaTime*/)
{
	if (Socket != nullptr)
	{
		SendPing();
		SendConnectStatus(true);
	}
	return true; // keep ticking
}

void FHapbeatSender::SendPing()
{
	const hapbeat::Bytes Packet = hapbeat::BuildPacket(hapbeat::kCmdPing, NextSeq(),
		hapbeat::BuildPingPayload(NowMicros()));
	SendBroadcast(Packet); // always broadcast: this is what discovers devices
}

void FHapbeatSender::SendConnectStatus(bool bConnected)
{
	// Legacy display-only field: the active override group when set, else 0.
	const uint8 GroupByte = (Config.Group >= 1 && Config.Group <= 255)
		? static_cast<uint8>(Config.Group) : 0;

	const hapbeat::Bytes Packet = hapbeat::BuildPacket(hapbeat::kCmdConnectStatus, NextSeq(),
		hapbeat::BuildConnectStatusPayload(bConnected, GroupByte,
			ToStd(Config.AppName), ToStd(FString(FPlatformProcess::ComputerName()))));
	SendBroadcast(Packet); // presence, not an addressed playback command
}

EHapbeatSendRoute FHapbeatSender::SendCommand(const hapbeat::Bytes& Packet, const std::string& ResolvedTarget)
{
	if (Socket == nullptr)
	{
		return EHapbeatSendRoute::Broadcast;
	}

	if (!Config.bCommandUnicast)
	{
		SendBroadcast(Packet);
		return EHapbeatSendRoute::Broadcast;
	}

	// Snapshot under the lock, then send outside it: SendTo can block on the
	// socket subsystem and the receiver thread must not wait on us.
	TArray<FString> Destinations;
	{
		const double Now = FPlatformTime::Seconds();
		FScopeLock Lock(&DeviceLock);
		for (TMap<FString, double>::TConstIterator It(DevicePongTimes); It; ++It)
		{
			// Stopped answering PINGs (powered off, left the network, rebooting):
			// skip it so we stop aiming datagrams at a dead host and so the live
			// set can empty out and let the broadcast fallback take over.
			if (Now - It.Value() > KnownDeviceTtlSeconds)
			{
				continue;
			}

			// Fail open: unknown address => send anyway. Known => must match.
			if (const FString* KnownAddress = DeviceAddresses.Find(It.Key()))
			{
				if (!hapbeat::AddressMatches(ResolvedTarget, ToStd(*KnownAddress)))
				{
					continue;
				}
			}

			Destinations.Add(It.Key());
		}
	}

	bool bSentAny = false;
	for (int32 i = 0; i < Destinations.Num(); ++i)
	{
		// One unreachable target must not block the rest.
		if (SendTo(Packet, Destinations[i]))
		{
			bSentAny = true;
		}
	}

	if (!bSentAny)
	{
		SendBroadcast(Packet);
		return EHapbeatSendRoute::Broadcast;
	}

	return EHapbeatSendRoute::Unicast;
}

void FHapbeatSender::SendBroadcast(const hapbeat::Bytes& Packet)
{
	SendTo(Packet, TEXT("255.255.255.255"));
}

bool FHapbeatSender::SendTo(const hapbeat::Bytes& Packet, const FString& IpString)
{
	if (Socket == nullptr || Packet.empty())
	{
		return false;
	}

	ISocketSubsystem* SocketSubsystem = ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM);
	if (SocketSubsystem == nullptr)
	{
		return false;
	}

	TSharedPtr<FInternetAddr> Addr = SocketSubsystem->CreateInternetAddr();
	bool bIsValid = false;
	Addr->SetIp(*IpString, bIsValid);
	if (!bIsValid)
	{
		return false;
	}
	Addr->SetPort(Config.Port);

	int32 BytesSent = 0;
	const bool bOk = Socket->SendTo(Packet.data(), static_cast<int32>(Packet.size()), BytesSent, *Addr);
	if (!bOk)
	{
		UE_LOG(LogHapbeatMod, Verbose, TEXT("UDP send to %s failed; continuing."), *IpString);
	}
	return bOk;
}

void FHapbeatSender::HandleReceived(const FArrayReaderPtr& Reader, const FIPv4Endpoint& Sender)
{
	// ---- receiver worker thread ----
	if (!Reader.IsValid())
	{
		return;
	}

	hapbeat::ParsedPacket Pkt;
	if (!hapbeat::ParsePacket(Reader->GetData(), Reader->Num(), Pkt))
	{
		return; // not ours / malformed — silently ignore
	}
	if (Pkt.Cmd != hapbeat::kCmdPong)
	{
		return; // ERROR frames carry nothing this mod can act on
	}

	hapbeat::PongInfo Pong;
	if (!hapbeat::ParsePong(Pkt.Payload, Pong))
	{
		return;
	}

	const FString SenderIp = Sender.Address.ToString(); // "a.b.c.d", no port

	// Recorded whether or not the PONG reported an address: the routing needs the
	// full set of live devices, and an address-unknown device still gets commands
	// (fail open). The timestamp is what lets the routing expire a device that
	// stopped answering.
	FScopeLock Lock(&DeviceLock);
	DevicePongTimes.Add(SenderIp, FPlatformTime::Seconds());
	if (Pong.bHasAddress)
	{
		DeviceAddresses.Add(SenderIp, ToFString(Pong.Address));
	}
}
