#pragma once
#include "CoreMinimal.h"
#include "HapbeatDemoSwitchProtocol.h"

class FSocket;
class FInternetAddr;

/** How an accepted CONTROL action ended in FHapbeatDemoSwitchReceiver::Execute. */
enum class EHapbeatControlResult : uint8
{
    /** Done: READY. */
    Ready,
    /** Could not run: FAILED/launch_failed. */
    Failed,
    /** Started a hand-over that ends later with FHapbeatDemoSwitchReceiver::FinishPending (session_next). */
    Pending,
};

/**
 * Demo Switch UDP 7710 receiver: DISCOVER/HERE, QUERY/STATE and CONTROL (ACK -> operation on the game thread ->
 * READY, or for a launch READY naming the started demo once this runtime has left the foreground). One operation at
 * a time: CONTROL is answered FAILED/not_allowed while a launch is pending. SWITCH is answered FAILED/not_allowed. The socket stays bound while the process runs, also while the
 * runtime is not foreground (no VR focus: boundary setup, system menu); then STATE carries foreground=false and
 * CONTROL is answered FAILED/not_allowed "not in foreground". A failed bind is retried for a few seconds. On
 * Android a Wi-Fi MulticastLock is held while bound, so a broadcast DISCOVER is not filtered.
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
    /** Runs an accepted CONTROL action (after ACK). */
    TFunction<EHapbeatControlResult(const FString& Action)> Execute;
    /** The runtime's current state for a STATE answer (QUERY is not answered without it). */
    TFunction<FHapbeatDemoSwitchState()> GetState;

    ~FHapbeatDemoSwitchReceiver() {Stop();}
    /** Reads the settings and replay state. False when the receiver stays off. */
    bool Configure(const FString& InDemoId);
    /** Binds (retrying a failed bind for a few seconds) and polls the socket. */
    void Tick();
    /** Closes the socket and stays off (before handing the device to the next runtime). A pending operation is dropped. */
    void Stop();
    /**
     * Ends the operation Execute left Pending: READY with current_demo_id = LaunchedDemoId (call it before Stop, while
     * 7710 is still bound), or FAILED/launch_failed with Text. Nothing when none is pending.
     */
    void FinishPending(bool bSuccess,const FString& LaunchedDemoId,const FString& Text=FString());
    bool HasPending() const {return PendingSource.IsValid();}
    bool IsEnabled() const {return bEnabled;}
    bool IsListening() const {return Socket!=nullptr;}
private:
    void Close();
    bool LoadReplayState();
    bool Reserve(const FString& ControllerId,int64 Sequence);
    void Send(const FString& Payload,const FInternetAddr& Destination);
    FSocket* Socket=nullptr;
    /** The CONTROL whose launch is pending (Execute returned Pending) and where its status goes. */
    FHapbeatDemoSwitchMessage PendingMessage;
    TSharedPtr<FInternetAddr> PendingSource;
    FString DemoId,Secret,StateFile;
    TMap<FString,int64> Sequences;
    /** First failed bind attempt and the next retry (FPlatformTime::Seconds), 0 before any failure. */
    double BindFailedAt=0,NextBindAt=0;
    bool bEnabled=false,bUnsigned=false,bTest=false,bBindFailed=false,bForeground=true,bMulticastLock=false;
};
