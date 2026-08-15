// Settings for the Hapbeat mod, read from a plain key=value text file so it can
// be retuned without rebuilding the plugin (and without pulling in UE's Json
// module, which would be another dependency for a mod that has to compile
// against a 2017-era engine fork).
//
// Location: <this plugin>/Config/HapbeatMod.txt
// The file is written with defaults on first run when it is missing; if the
// directory is read-only (a Program Files install), defaults are used silently.

#pragma once

#include "CoreMinimal.h"

/// One logical event's binding. The mod's hook code only ever names the logical
/// event ("fire", "kill", "player_hit"); which kit clip that plays, at what gain,
/// and whether it plays at all, live here.
struct FHapbeatEventBinding
{
	FString EventId;
	float   Gain;
	bool    bEnabled;

	FHapbeatEventBinding()
		: Gain(1.0f), bEnabled(true) {}

	FHapbeatEventBinding(const FString& InEventId, float InGain, bool bInEnabled)
		: EventId(InEventId), Gain(InGain), bEnabled(bInEnabled) {}
};

struct FHapbeatModConfig
{
	/// Master on/off. False = no socket is opened at all.
	bool bEnabled;

	/// Shown on the device OLED. Truncated to 16 characters on the wire (DEC-029).
	FString AppName;

	/// Forced group / player number (1..99) applied to every outgoing target,
	/// or -1 to leave targets untouched. Same semantics as the Unity SDK's
	/// Address Override.
	int32 Group;
	int32 Player;

	/// Multiplied with each event's own gain.
	float MasterGain;

	/// Minimum interval between two fires of the SAME logical event. Stops a
	/// rapid-fire weapon from turning into a packet storm. 0 disables.
	int32 MinIntervalMs;

	/// Prefer unicast to devices that have answered a PING (falling back to
	/// broadcast when none is live). Set false to always broadcast.
	bool bCommandUnicast;

	/// Device UDP port (contracts specs/ports.md).
	int32 Port;

	/// Where discovery packets go when nothing has answered yet. Empty (the
	/// default) means "work it out": one subnet-directed address per local
	/// adapter, plus 255.255.255.255 as a catch-all.
	///
	/// Set it by hand when automatic detection guesses wrong. 255.255.255.255
	/// alone leaves a multi-homed host through the single adapter with the
	/// lowest metric — on a PC running Hyper-V / WSL2 / Docker that is often a
	/// virtual switch with no device behind it, and no Ethernet cable has to be
	/// plugged in for that to happen. A subnet-directed address (192.168.0.255)
	/// resolves through the directly-connected route instead, so the metric
	/// never applies.
	///
	/// Automatic detection assumes a /24 subnet, which is what home and office
	/// routers hand out. On a /16 or /25 network, put the real broadcast address
	/// here (192.168.255.255, 192.168.0.127, …).
	FString BroadcastAddress;

	/// Logical event name -> binding. Keys are compared case-insensitively.
	TMap<FString, FHapbeatEventBinding> Events;

	FHapbeatModConfig();

	/// Defaults bound to the standard vr-shooter-kit clips.
	static FHapbeatModConfig CreateDefault();

	/// Absolute path of this plugin's Config/HapbeatMod.txt. Empty when the
	/// plugin cannot be located (should not happen at runtime).
	static FString GetConfigFilePath();

	/// Load from `Path`, creating it with defaults when absent. Never fails: a
	/// missing or unreadable file yields defaults, and unparsable lines are
	/// skipped with a warning so one typo cannot silence the whole mod.
	static FHapbeatModConfig LoadOrCreate(const FString& Path);

	/// Serialize to the key=value text format (with explanatory comments).
	FString ToText() const;

	/// Binding for `LogicalEvent`, or nullptr when not configured.
	const FHapbeatEventBinding* FindEvent(const FString& LogicalEvent) const;
};
