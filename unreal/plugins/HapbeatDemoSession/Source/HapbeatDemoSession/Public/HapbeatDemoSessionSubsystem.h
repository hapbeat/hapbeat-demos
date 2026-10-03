#pragma once
#include "CoreMinimal.h"
#include "Subsystems/GameInstanceSubsystem.h"
#include "Tickable.h"
#include "HapbeatDemoSessionTicket.h"
#include "HapbeatDemoSwitchReceiver.h"
#include "HapbeatDemoSessionPause.h"
#include "HapbeatDemoSessionHandoff.h"
#include "HapbeatDemoSessionSubsystem.generated.h"

class AHapbeatDemoSessionUi;
class APlayerController;
struct FHapbeatSessionPointerInput;

DECLARE_MULTICAST_DELEGATE_OneParam(FHapbeatDemoSessionFlag,bool);

/**
 * Demo Session runtime (repos-core/hapbeat-contracts/specs/demo-session.md) and the Demo Switch receiver.
 *
 * At start it reads this APK's descriptor and the launching Intent's session ticket. A valid ticket whose
 * steps[index] is this demo enables session mode: the demo calls ShowCompletion() on its own completion
 * event, the completion panel then offers "もう一度" (OnRestartRequested) and "次へ" / "デモを終了", which
 * starts the next runtime and ends this one. Without a ticket the demo runs as before.
 *
 * Haptics ON/OFF (OnHapticsChanged) starts ON. With descriptor supports.haptics_toggle, the in-view button
 * shows while IsHapticsUiVisible() (ticket haptics_ui, or Demo Switch CONTROL haptics_ui_show/hide).
 *
 * The demo registers its CONTROL actions (restart / menu_open / menu_close / recenter) with RegisterControl.
 *
 * Shared pause (demos without a menu of their own, [HapbeatDemoSession.Pause] in DefaultGame.ini): the left menu
 * gesture / controller menu button (FHapbeatPauseDetector) opens a panel in front of the head with 再開 /
 * 最初からやり直す / 次へ (or デモを終了; session only) / Hub に戻る, with or without a session. The demo stops its
 * game, navigation voice and haptics on OnPauseChanged.
 *
 * Starting the next runtime (or the Hub) ends this demo only once it has gone to the background
 * (FHapbeatLaunchHandoff); a system recenter (FCoreDelegates::VRHeadsetRecenter) puts open panels back in front
 * of the head.
 *
 * After the Hapbeat SDK subsystem has initialized, the per-device address file (hapbeat-device.json in the app's
 * external files directory) is read once and its axes are set as the SDK's address override (not persisted).
 */
UCLASS()
class HAPBEATDEMOSESSION_API UHapbeatDemoSessionSubsystem : public UGameInstanceSubsystem, public FTickableGameObject
{
    GENERATED_BODY()
public:
    static UHapbeatDemoSessionSubsystem* Get(const UObject* WorldContext);

    virtual void Initialize(FSubsystemCollectionBase& Collection) override;
    virtual void Deinitialize() override;
    virtual void Tick(float DeltaTime) override;
    virtual TStatId GetStatId() const override;
    virtual ETickableTickType GetTickableTickType() const override {return IsTemplate()?ETickableTickType::Never:ETickableTickType::Always;}
    virtual bool IsTickableWhenPaused() const override {return true;}
    virtual UWorld* GetTickableGameObjectWorld() const override;

    bool HasDescriptor() const {return bHasDescriptor;}
    const FHapbeatDemoSessionDescriptor& GetDescriptor() const {return Descriptor;}
    bool IsSessionActive() const {return bSessionActive;}
    /** Effective value of an active option of this demo (ticket value or descriptor default); empty when unknown. */
    FString GetOption(const FString& Id) const {return Options.FindRef(Id);}
    int32 GetStepIndex() const {return bSessionActive?Ticket.Index:0;}
    int32 GetStepCount() const {return bSessionActive?Ticket.Steps.Num():0;}

    /**
     * Session mode only: shows the completion panel (at the world's UHapbeatDemoSessionPanelAnchor, else in front
     * of the head) and pauses game input (OnGameplayPausedChanged). An open pause panel closes first.
     */
    void ShowCompletion();
    void HideCompletion();
    bool IsCompletionShown() const {return bCompletionShown;}
    /** True while the completion or the pause panel is up: the demo ignores its own game input. */
    bool IsGameplayPaused() const {return bCompletionShown||bPauseShown;}
    /** The shared pause is enabled for this project ([HapbeatDemoSession.Pause] Enabled=True). */
    bool IsPauseEnabled() const {return PauseSettings.bEnabled;}
    EHapbeatPauseGesture GetPauseGesture() const {return PauseDetector.Gesture;}
    /** Opens the pause panel (only when enabled, and not over the completion panel or while leaving). */
    void ShowPause();
    /** Closes the pause panel (再開). */
    void HidePause();
    bool IsPauseShown() const {return bPauseShown;}
    /**
     * Starts the Demo Hub without a ticket and ends this demo the same way as LaunchNextOrFinish. Only when the
     * Hub is installed (IsHubInstalled). On failure the pause panel shows an error and the demo stays.
     */
    void ReturnToHub();
    bool IsHubInstalled() const {return bHubInstalled;}
    /**
     * Starts steps[index+1] (or the finish runtime) with the next ticket (completion panel, or the pause panel's
     * 次へ / デモを終了). Once this application has gone to the background: haptics off, sounds and the 7710
     * listener stopped, then this task finishes, once. When the launch fails, or this application is still in
     * front after FHapbeatLaunchHandoff::TimeoutSeconds, the open panel shows an error and the demo stays.
     * Off Android nothing is launched (logged only).
     */
    void LaunchNextOrFinish();
    /** A launch is waiting for this application to go to the background, or this demo is ending. */
    bool IsLeaving() const {return bExiting||Handoff.IsWaiting();}

    bool IsHapticsEnabled() const {return bHapticsEnabled;}
    void SetHapticsEnabled(bool bEnabled);
    bool IsHapticsUiVisible() const {return bHapticsUiVisible;}
    void SetHapticsUiVisible(bool bVisible);

    /** Registers a Demo Switch CONTROL action handled by the demo; Handler returns false when it could not run. */
    void RegisterControl(const FString& Action,const UObject* Owner,TFunction<bool()> Handler);
    void UnregisterControls(const UObject* Owner);

    /** "もう一度": restart the same step inside the demo (the panel is already closed). */
    FSimpleMulticastDelegate OnRestartRequested;
    /** Hapbeat output on/off. Off also stops loops and streams that are playing. */
    FHapbeatDemoSessionFlag OnHapticsChanged;
    /** IsGameplayPaused() changed: the completion or the pause panel opened (true) or closed (false). */
    FHapbeatDemoSessionFlag OnGameplayPausedChanged;
    /**
     * The pause panel opened (true) or closed (false): stop / resume the game's progress, the navigation voice
     * and haptics (the haptics on/off switch itself is unchanged). 最初からやり直す closes the pause, then
     * broadcasts OnRestartRequested.
     */
    FHapbeatDemoSessionFlag OnPauseChanged;
    /** The Demo Hub (the component Unity's Demo Switch settings also trust as demo_hub). */
    static const FHapbeatDemoSessionComponent& HubComponent();

    /** The ticket LaunchNextOrFinish hands over (index + 1, current haptics_ui). */
    FString MakeNextTicketJson() const;
private:
    void Load(const FString& DescriptorJson,bool bDescriptorRead,const FString& TicketJson,bool bTicketPresent);
    /** hapbeat-device.json -> UHapbeatSubsystem::SetAddressOverride(persist: false). Nothing without a file. */
    void ApplyDeviceAddress();
    bool IsControlAllowed(const FString& Action) const;
    bool ExecuteControl(const FString& Action);
    FHapbeatSessionPointerInput ReadPointers(APlayerController* PlayerController) const;
    /** One frame of the pause gesture input; nothing while the application has no focus (system menu open). */
    FHapbeatPauseInput ReadPauseInput(APlayerController* PlayerController,const FVector& Eye) const;
    void SetPauseShown(bool bShown);
    /** "次へ：<title>" or "デモを終了" (session mode). */
    FString NextLabel() const;
    /** The next runtime (or the Hub) was started: wait for the background (FHapbeatLaunchHandoff). */
    void StartHandoff();
    void UpdateHandoff(bool bBackgrounded,float Dt);
    /** Error line of the panel the launch came from (the pause panel when it is open, else the completion panel). */
    void ShowLaunchError(const FString& Text);
    /** FCoreDelegates::ApplicationWillDeactivateDelegate (Android: the activity is pausing; the game thread stops right after). */
    void OnApplicationDeactivated();
    /** FCoreDelegates::VRHeadsetRecenter (OpenXR XrEventDataReferenceSpaceChangePending). */
    void OnRecenter();
    /** This application is in the background: haptics off, sounds and the 7710 listener stopped, task finished (once). */
    void ExitAfterLaunch();
    AHapbeatDemoSessionUi* EnsureUi(UWorld* World);
    struct FControl
    {
        FString Action;
        TWeakObjectPtr<const UObject> Owner;
        TFunction<bool()> Handler;
    };
    TArray<FControl> Controls;
    FHapbeatDemoSessionDescriptor Descriptor;
    FHapbeatDemoSessionTicket Ticket;
    TMap<FString,FString> Options;
    FHapbeatDemoSwitchReceiver Receiver;
    TWeakObjectPtr<AHapbeatDemoSessionUi> Ui;
    FHapbeatPauseSettings PauseSettings;
    FHapbeatPauseDetector PauseDetector;
    FHapbeatLaunchHandoff Handoff;
    FDelegateHandle DeactivateHandle, RecenterHandle;
    /** Seconds left in which open panels are put back in front of the head after a recenter (-1: none). */
    float RecenterSeconds=-1;
    bool bHasDescriptor=false, bSessionActive=false, bCompletionShown=false, bHapticsEnabled=true, bHapticsUiVisible=false;
    bool bPauseShown=false, bHubInstalled=false;
    bool bInitialized=false, bExiting=false;
    /** The launch started with VR focus (OpenXR): losing it counts as going to the background. */
    bool bHandoffHadVRFocus=false;
};
