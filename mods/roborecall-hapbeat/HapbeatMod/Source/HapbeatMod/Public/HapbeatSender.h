// The UDP side of the mod: one broadcast-capable socket, a PONG receiver, a
// keep-alive ticker, and the unicast routing rules shared with every other
// Hapbeat SDK (see mods/shared/README.md > 送信の挙動).
//
// Nothing here knows about Robo Recall. Gameplay code (Blueprint, via
// UHapbeatBlueprintLibrary) only ever names a LOGICAL event.

#pragma once

#include "CoreMinimal.h"
#include "HapbeatModConfig.h"
#include "HapbeatWire.h"

// VERIFY-4.16: these are the 4.16 paths for the socket/networking types.
#include "Containers/Ticker.h"
#include "Common/UdpSocketReceiver.h"
#include "Interfaces/IPv4/IPv4Endpoint.h"
#include "Misc/ScopeLock.h"

class FSocket;

/// Whether a command actually went out unicast or fell back to broadcast.
/// Surfaced only for logging/diagnostics.
enum class EHapbeatSendRoute : uint8
{
	Broadcast,
	Unicast,
};

class FHapbeatSender
{
public:
	FHapbeatSender();
	~FHapbeatSender();

	/// Open the socket and start the receive + keep-alive machinery. Safe to call
	/// again (re-opens). Returns false when disabled by config or the socket
	/// could not be created.
	bool Open(const FHapbeatModConfig& InConfig);

	/// Send a final CONNECT_STATUS(connected=false), then tear everything down.
	void Close();

	bool IsOpen() const { return Socket != nullptr; }

	/// Fire a logical event ("fire", "kill", "player_hit", ...). Returns false
	/// when the event is unknown, disabled, or suppressed by the rate limit.
	/// Callable from the game thread (which is where Blueprint runs).
	bool Fire(const FString& LogicalEvent);

	/// Stop the clip bound to a logical event — for looping clips such as the
	/// low-health heartbeat. Never rate limited: a stop must not be dropped.
	bool FireStop(const FString& LogicalEvent);

	/// STOP_ALL: silence everything this app started.
	void StopAll();

	/// Devices that have answered a PING within the known-device TTL.
	int32 GetAliveDeviceCount() const;

	/// Replace the live settings (used by HapbeatReloadSettings). Re-opens the
	/// socket when the port or the enabled flag changed.
	void ApplyConfig(const FHapbeatModConfig& InConfig);

	const FHapbeatModConfig& GetConfig() const { return Config; }

private:
	bool TickKeepAlive(float DeltaTime);
	void HandleReceived(const FArrayReaderPtr& Reader, const FIPv4Endpoint& Sender);

	void SendPing();
	void SendConnectStatus(bool bConnected);

	/// PLAY/STOP/STOP_ALL routing:
	///  - unicast disabled -> broadcast
	///  - no device has PONGed within the TTL -> broadcast (fail open)
	///  - device with an unknown address -> unicast anyway (fail open)
	///  - device whose reported address matches the target -> unicast
	///  - at least one unicast went out -> stop (never also broadcast, or a
	///    known-and-matching device would receive the same PLAY twice)
	///  - nothing went out -> broadcast. The firmware re-applies addressMatch()
	///    to everything it receives, so a broadcast can never actuate a device
	///    the target did not address; skipping would only risk losing a command
	///    to a stale cached address — and for STOP that means a looping clip
	///    never stops.
	EHapbeatSendRoute SendCommand(const hapbeat::Bytes& Packet, const std::string& ResolvedTarget);

	/// Broadcast a raw packet (PING / CONNECT_STATUS / command fallback).
	void SendBroadcast(const hapbeat::Bytes& Packet);

	bool SendTo(const hapbeat::Bytes& Packet, const FString& IpString);

	uint16 NextSeq();
	std::string ResolvedTargetForSend() const;

	FSocket* Socket;
	FUdpSocketReceiver* Receiver;
	FDelegateHandle KeepAliveHandle;

	FHapbeatModConfig Config;

	uint16 Seq;

	/// Device IP -> FPlatformTime::Seconds() of its most recent PONG. Written on
	/// the receiver thread, read on the game thread: guarded by DeviceLock.
	TMap<FString, double> DevicePongTimes;
	/// Device IP -> the address string it reported. Absent = unknown = fail open.
	TMap<FString, FString> DeviceAddresses;
	mutable FCriticalSection DeviceLock;

	/// Logical event -> FPlatformTime::Seconds() of its last accepted fire.
	TMap<FString, double> LastFireTimes;

	/// How long a device stays a unicast destination after its last PONG.
	/// Without expiry the set only grows: a powered-off device would keep
	/// absorbing datagrams AND keep the set non-empty, permanently suppressing
	/// the broadcast fallback on a LAN with no live device left.
	static const double KnownDeviceTtlSeconds;

	/// Keep-alive period. Sending PING is mandatory, not optional: a PONG only
	/// ever answers a PING, so without it the known-device table ages out and
	/// every command falls back to broadcast forever.
	static const float KeepAliveIntervalSeconds;
};
