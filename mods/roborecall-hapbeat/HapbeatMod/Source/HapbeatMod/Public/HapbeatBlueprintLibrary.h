// The only surface Blueprint sees. Drop one of these nodes into a weapon / bot /
// player Blueprint's event graph at the point the game already does the thing
// (fire, die, take damage) — see README.md > エディタ内 Blueprint 配線手順.
//
// Everything is a static function on a UBlueprintFunctionLibrary, so the nodes
// need no target pin and no actor to live on.

#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "HapbeatBlueprintLibrary.generated.h"

class FHapbeatSender;

UCLASS()
class UHapbeatBlueprintLibrary : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	/// Weapon fired. Plays the "fire" logical event (vr-shooter-kit.shot_recoil).
	UFUNCTION(BlueprintCallable, Category = "Hapbeat", meta = (DisplayName = "Hapbeat Send Fire"))
	static void HapbeatSendFire();

	/// A bot was destroyed. Plays "kill" (vr-shooter-kit.kill_confirm).
	UFUNCTION(BlueprintCallable, Category = "Hapbeat", meta = (DisplayName = "Hapbeat Send Kill"))
	static void HapbeatSendKill();

	/// The player took damage. Plays "player_hit" (vr-shooter-kit.hit_heavy).
	UFUNCTION(BlueprintCallable, Category = "Hapbeat", meta = (DisplayName = "Hapbeat Send Player Hit"))
	static void HapbeatSendPlayerHit();

	/// Play any logical event defined in Config/HapbeatMod.txt — use this to add
	/// a new haptic without touching C++ (add the binding to the settings file,
	/// then name it here).
	UFUNCTION(BlueprintCallable, Category = "Hapbeat", meta = (DisplayName = "Hapbeat Send Custom"))
	static void HapbeatSendCustom(const FString& LogicalEvent);

	/// Stop a looping logical event (e.g. a low-health heartbeat).
	UFUNCTION(BlueprintCallable, Category = "Hapbeat", meta = (DisplayName = "Hapbeat Stop Event"))
	static void HapbeatStopEvent(const FString& LogicalEvent);

	/// Silence everything this mod started. Wire to end-of-wave / pause / EndPlay.
	UFUNCTION(BlueprintCallable, Category = "Hapbeat", meta = (DisplayName = "Hapbeat Stop All"))
	static void HapbeatStopAll();

	/// Turn haptics off (closes the socket, telling devices we disconnected) or
	/// back on, at runtime.
	UFUNCTION(BlueprintCallable, Category = "Hapbeat", meta = (DisplayName = "Hapbeat Set Enabled"))
	static void HapbeatSetEnabled(bool bEnabled);

	/// Devices that answered a PING in the last 15 s. 0 means nothing is
	/// listening — the fastest way to tell "no haptics" from "wrong wiring".
	UFUNCTION(BlueprintPure, Category = "Hapbeat", meta = (DisplayName = "Hapbeat Alive Device Count"))
	static int32 HapbeatGetAliveDeviceCount();

	/// Whether the UDP socket is actually open. false means nothing was ever sent
	/// — the socket could not be created (firewall prompt still up, adapter not
	/// ready) or haptics are switched off in the settings file. Distinguishes
	/// "the mod never got on the network" from "packets go out but no device
	/// answers", which Hapbeat Alive Device Count alone cannot tell apart.
	/// A failed open is retried automatically on a later event.
	UFUNCTION(BlueprintPure, Category = "Hapbeat", meta = (DisplayName = "Hapbeat Is Socket Open"))
	static bool HapbeatIsSocketOpen();

	/// Re-read Config/HapbeatMod.txt without restarting the game. Retune gains
	/// and event ids between PIE runs.
	UFUNCTION(BlueprintCallable, Category = "Hapbeat", meta = (DisplayName = "Hapbeat Reload Settings"))
	static void HapbeatReloadSettings();

	/// Tear the sender down. Called by the module on shutdown; not a Blueprint node.
	static void ShutdownSender();

private:
	/// Lazily creates and opens the sender on first use, so nothing touches the
	/// network until a Blueprint actually fires an event.
	static FHapbeatSender* GetSender();
};
