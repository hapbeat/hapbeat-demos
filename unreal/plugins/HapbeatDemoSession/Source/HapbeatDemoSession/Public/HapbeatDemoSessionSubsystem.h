#pragma once
#include "CoreMinimal.h"
#include "Subsystems/GameInstanceSubsystem.h"
#include "Tickable.h"
#include "HapbeatDemoSessionTicket.h"
#include "HapbeatDemoSwitchReceiver.h"
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

    /** Session mode only: shows the completion panel and pauses game input (OnGameplayPausedChanged). */
    void ShowCompletion();
    void HideCompletion();
    bool IsCompletionShown() const {return bCompletionShown;}
    /** True while the completion panel is up: the demo ignores its own game input. */
    bool IsGameplayPaused() const {return bCompletionShown;}
    /**
     * Starts steps[index+1] (or the finish runtime) with the next ticket. On success: haptics off, sounds and the
     * 7710 listener stopped, then this task finishes. On failure the panel shows an error and the demo stays.
     * Off Android nothing is launched (logged only).
     */
    void LaunchNextOrFinish();

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
    /** The completion panel opened (true) or closed (false). */
    FHapbeatDemoSessionFlag OnGameplayPausedChanged;

    /** The ticket LaunchNextOrFinish hands over (index + 1, current haptics_ui). */
    FString MakeNextTicketJson() const;
private:
    void Load(const FString& DescriptorJson,bool bDescriptorRead,const FString& TicketJson,bool bTicketPresent);
    /** hapbeat-device.json -> UHapbeatSubsystem::SetAddressOverride(persist: false). Nothing without a file. */
    void ApplyDeviceAddress();
    bool IsControlAllowed(const FString& Action) const;
    bool ExecuteControl(const FString& Action);
    FHapbeatSessionPointerInput ReadPointers(APlayerController* PlayerController) const;
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
    bool bHasDescriptor=false, bSessionActive=false, bCompletionShown=false, bHapticsEnabled=true, bHapticsUiVisible=false;
    bool bInitialized=false, bExiting=false;
};
