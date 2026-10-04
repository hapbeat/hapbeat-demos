#pragma once
#include "CoreMinimal.h"
#include "HapbeatDemoSwitchProtocol.h"

class FSocket;
class FInternetAddr;

/**
 * Demo Switch UDP 7710 receiver for the foreground runtime: DISCOVER/HERE, QUERY/STATE and CONTROL
 * (ACK -> operation on the game thread -> READY). SWITCH is answered FAILED/not_allowed.
 *
 * Settings: Saved/Config/HapbeatDemoSession.json ({"enabled", "shared_secret", "allow_unsigned",
 * "isolated_lan"}). Without that file the receiver runs in isolated-LAN unsigned mode, the same as the
 * Unity demos' exhibition settings. Replay state (highest sequence per controller) persists in
 * Saved/HapbeatDemoSession/Sequences.json; an unreadable state disables the receiver.
 * -HapbeatDemoSwitchTest binds loopback 17710 instead, with -HapbeatDemoSwitchConfig=<file> and a separate state file.
 */
class HAPBEATDEMOSESSION_API FHapbeatDemoSwitchReceiver
{
public:
    /** Whether the runtime accepts this CONTROL action right now (unsupported -> FAILED/not_allowed). */
    TFunction<bool(const FString& Action)> IsAllowed;
    /** Runs an accepted CONTROL action; false -> FAILED/launch_failed. */
    TFunction<bool(const FString& Action)> Execute;
    /** The runtime's current state for a STATE answer (QUERY is not answered without it). */
    TFunction<FHapbeatDemoSwitchState()> GetState;

    ~FHapbeatDemoSwitchReceiver() {Stop();}
    /** Reads the settings and replay state. False when the receiver stays off. */
    bool Configure(const FString& InDemoId);
    /** Polls the socket; binds while the runtime is foreground and closes it otherwise. */
    void Tick();
    /** Closes the socket and stays off (before handing the device to the next runtime). */
    void Stop();
    bool IsEnabled() const {return bEnabled;}
    bool IsListening() const {return Socket!=nullptr;}
private:
    void Close();
    bool LoadReplayState();
    bool Reserve(const FString& ControllerId,int64 Sequence);
    void Send(const FString& Payload,const FInternetAddr& Destination);
    FSocket* Socket=nullptr;
    FString DemoId,Secret,StateFile;
    TMap<FString,int64> Sequences;
    bool bEnabled=false,bUnsigned=false,bTest=false,bBindFailed=false,bWasForeground=false;
};
