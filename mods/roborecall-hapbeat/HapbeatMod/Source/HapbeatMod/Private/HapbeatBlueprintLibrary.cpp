#include "HapbeatBlueprintLibrary.h"

#include "HapbeatModConfig.h"
#include "HapbeatModLog.h"
#include "HapbeatSender.h"

#include "HAL/PlatformTime.h"

namespace
{
	// One sender per process, created on first use. A raw pointer rather than a
	// smart pointer so teardown is explicit and happens exactly once, from
	// ShutdownSender() (module shutdown) — a static destructor would run at an
	// unspecified point during engine exit, after the socket subsystem may
	// already be gone.
	FHapbeatSender* GSender = nullptr;

	/// FPlatformTime::Seconds() of the last FAILED open, or -1 when the last
	/// attempt succeeded / none has happened yet.
	///
	/// A failed open is retried, not remembered forever: the first shot of a
	/// session can easily land while the Windows firewall prompt is still up or
	/// the adapter is not ready, and a permanent give-up would silently kill
	/// haptics for the whole exhibition session. The cooldown is what keeps a
	/// genuinely broken network from re-attempting (and re-logging) on every shot.
	double GLastOpenFailureSeconds = -1.0;

	/// enabled=false in the settings file is a deliberate user choice, not a
	/// failure — never retried, only cleared by Set Enabled / Reload Settings.
	bool GDisabledByConfig = false;

	const double kOpenRetryCooldownSeconds = 10.0;
}

FHapbeatSender* UHapbeatBlueprintLibrary::GetSender()
{
	if (GSender != nullptr && GSender->IsOpen())
	{
		return GSender;
	}
	if (GDisabledByConfig)
	{
		return nullptr;
	}

	const double Now = FPlatformTime::Seconds();
	if (GLastOpenFailureSeconds >= 0.0 && (Now - GLastOpenFailureSeconds) < kOpenRetryCooldownSeconds)
	{
		return nullptr;
	}

	const FHapbeatModConfig Config = FHapbeatModConfig::LoadOrCreate(FHapbeatModConfig::GetConfigFilePath());
	if (!Config.bEnabled)
	{
		GDisabledByConfig = true;
		UE_LOG(LogHapbeatMod, Log,
			TEXT("Hapbeat is disabled in the settings file (enabled=false); no socket will be opened. ")
			TEXT("Use the Hapbeat Set Enabled or Hapbeat Reload Settings node to turn it back on."));
		return nullptr;
	}

	if (GSender == nullptr)
	{
		GSender = new FHapbeatSender();
	}

	if (!GSender->Open(Config))
	{
		GLastOpenFailureSeconds = Now;
		UE_LOG(LogHapbeatMod, Warning,
			TEXT("Could not open the Hapbeat UDP socket; haptics are OFF. Retrying on the next event ")
			TEXT("after %.0f s. Check the Windows firewall prompt for UE4Editor.exe and the network ")
			TEXT("adapter. The 'Hapbeat Is Socket Open' node reports false until this succeeds."),
			kOpenRetryCooldownSeconds);
		return nullptr;
	}

	GLastOpenFailureSeconds = -1.0;
	return GSender;
}

void UHapbeatBlueprintLibrary::ShutdownSender()
{
	if (GSender != nullptr)
	{
		GSender->Close();
		delete GSender;
		GSender = nullptr;
	}
	GLastOpenFailureSeconds = -1.0;
	GDisabledByConfig = false;
}

void UHapbeatBlueprintLibrary::HapbeatSendFire()
{
	if (FHapbeatSender* Sender = GetSender())
	{
		Sender->Fire(TEXT("fire"));
	}
}

void UHapbeatBlueprintLibrary::HapbeatSendKill()
{
	if (FHapbeatSender* Sender = GetSender())
	{
		Sender->Fire(TEXT("kill"));
	}
}

void UHapbeatBlueprintLibrary::HapbeatSendPlayerHit()
{
	if (FHapbeatSender* Sender = GetSender())
	{
		Sender->Fire(TEXT("player_hit"));
	}
}

void UHapbeatBlueprintLibrary::HapbeatSendCustom(const FString& LogicalEvent)
{
	if (FHapbeatSender* Sender = GetSender())
	{
		Sender->Fire(LogicalEvent);
	}
}

void UHapbeatBlueprintLibrary::HapbeatStopEvent(const FString& LogicalEvent)
{
	if (FHapbeatSender* Sender = GetSender())
	{
		Sender->FireStop(LogicalEvent);
	}
}

void UHapbeatBlueprintLibrary::HapbeatStopAll()
{
	if (FHapbeatSender* Sender = GetSender())
	{
		Sender->StopAll();
	}
}

void UHapbeatBlueprintLibrary::HapbeatSetEnabled(bool bEnabled)
{
	if (!bEnabled)
	{
		// Closing sends CONNECT_STATUS(connected=false) so devices clear their
		// display right away instead of waiting for a presence timeout.
		if (GSender != nullptr)
		{
			GSender->StopAll();
			GSender->Close();
		}
		return;
	}

	// Re-enable: clear both "do not retry" latches (an explicit Blueprint request
	// overrides a previous failure and a stale enabled=false), reload settings and
	// reopen. GetSender handles the very first creation.
	GLastOpenFailureSeconds = -1.0;
	GDisabledByConfig = false;
	if (GSender == nullptr)
	{
		GetSender();
		return;
	}

	FHapbeatModConfig Config = FHapbeatModConfig::LoadOrCreate(FHapbeatModConfig::GetConfigFilePath());
	Config.bEnabled = true; // explicit Blueprint request wins over a stale file
	if (!GSender->Open(Config))
	{
		// Same cooldown as GetSender: a failed re-enable is retried on the next
		// event rather than latching haptics off for the session.
		GLastOpenFailureSeconds = FPlatformTime::Seconds();
		UE_LOG(LogHapbeatMod, Warning,
			TEXT("Hapbeat Set Enabled(true) could not open the socket; will retry on a later event."));
	}
}

int32 UHapbeatBlueprintLibrary::HapbeatGetAliveDeviceCount()
{
	return (GSender != nullptr) ? GSender->GetAliveDeviceCount() : 0;
}

bool UHapbeatBlueprintLibrary::HapbeatIsSocketOpen()
{
	return GSender != nullptr && GSender->IsOpen();
}

void UHapbeatBlueprintLibrary::HapbeatReloadSettings()
{
	const FHapbeatModConfig Config = FHapbeatModConfig::LoadOrCreate(FHapbeatModConfig::GetConfigFilePath());

	// The file on disk is the new source of truth for both latches: a user who
	// just fixed the network (or flipped enabled= back on) gets an immediate
	// attempt rather than waiting out the cooldown.
	GLastOpenFailureSeconds = -1.0;
	GDisabledByConfig = false;

	if (GSender == nullptr)
	{
		GetSender();
		return;
	}
	GSender->ApplyConfig(Config);
	if (Config.bEnabled && !GSender->IsOpen())
	{
		GLastOpenFailureSeconds = FPlatformTime::Seconds();
		UE_LOG(LogHapbeatMod, Warning,
			TEXT("Settings reloaded, but the Hapbeat socket is still not open; will retry on a later event."));
		return;
	}
	UE_LOG(LogHapbeatMod, Log, TEXT("Settings reloaded from disk."));
}
