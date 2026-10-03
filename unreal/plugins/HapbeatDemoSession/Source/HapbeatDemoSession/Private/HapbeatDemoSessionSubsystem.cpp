#include "HapbeatDemoSessionSubsystem.h"
#include "HapbeatDemoSessionLog.h"
#include "HapbeatDemoSessionDeviceAddress.h"
#include "HapbeatDemoSessionHandMenu.h"
#include "HapbeatDemoSessionPlatform.h"
#include "HapbeatDemoSessionUi.h"
#include "HapbeatDemoSessionPanelAnchor.h"
#include "HapbeatSubsystem.h"
#include "AudioDevice.h"
#include "Camera/PlayerCameraManager.h"
#include "Engine/Engine.h"
#include "Engine/GameInstance.h"
#include "Engine/World.h"
#include "GameFramework/PlayerController.h"
#include "HeadMountedDisplayFunctionLibrary.h"
#include "HeadMountedDisplayTypes.h"
#include "IXRTrackingSystem.h"
#include "InputCoreTypes.h"
#include "Misc/App.h"
#include "Misc/CoreDelegates.h"

DEFINE_LOG_CATEGORY(LogHapbeatDemoSession);

UHapbeatDemoSessionSubsystem* UHapbeatDemoSessionSubsystem::Get(const UObject* WorldContext)
{
    const UWorld* World=GEngine?GEngine->GetWorldFromContextObject(WorldContext,EGetWorldErrorMode::ReturnNull):nullptr;
    const UGameInstance* GameInstance=World?World->GetGameInstance():nullptr;
    return GameInstance?GameInstance->GetSubsystem<UHapbeatDemoSessionSubsystem>():nullptr;
}

void UHapbeatDemoSessionSubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
    Super::Initialize(Collection);
    // The address file is applied on top of what the SDK restored at its own start (saved override, pinned axes).
    Collection.InitializeDependency<UHapbeatSubsystem>();
    ApplyDeviceAddress();
    FString DescriptorJson,TicketJson;
    const bool bDescriptorRead=HapbeatDemoSessionPlatform::ReadDescriptor(DescriptorJson);
    const bool bTicketPresent=HapbeatDemoSessionPlatform::TakeTicket(TicketJson);
    Load(DescriptorJson,bDescriptorRead,TicketJson,bTicketPresent);
    // The pause works with or without a descriptor / ticket (a demo started from the Quest library too).
    PauseSettings=FHapbeatPauseSettings::Load();
    PauseDetector.Gesture=PauseSettings.Gesture;
    PauseDetector.HoldSeconds=PauseSettings.HoldSeconds;
    bHubInstalled=HapbeatDemoSessionPlatform::IsInstalled(HubComponent().Package);
    if(PauseSettings.bEnabled) UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_PAUSE enabled gesture=%s hold=%.2f hub=%d"),
        PauseSettings.Gesture==EHapbeatPauseGesture::SystemMenu?TEXT("SystemMenu"):TEXT("PalmPinchHold"),PauseSettings.HoldSeconds,bHubInstalled);
    // On Android the activity's pause is broadcast on the game thread, which is suspended right after: the
    // hand-over has to end the task from inside this callback (LaunchAndroid.cpp SuspendApp_EventThread).
    DeactivateHandle=FCoreDelegates::ApplicationWillDeactivateDelegate.AddUObject(this,&UHapbeatDemoSessionSubsystem::OnApplicationDeactivated);
    RecenterHandle=FCoreDelegates::VRHeadsetRecenter.AddUObject(this,&UHapbeatDemoSessionSubsystem::OnRecenter);
    bInitialized=true;
}

void UHapbeatDemoSessionSubsystem::Load(const FString& DescriptorJson,bool bDescriptorRead,const FString& TicketJson,bool bTicketPresent)
{
    Receiver.Stop();
    bHasDescriptor=bSessionActive=bCompletionShown=false;Descriptor=FHapbeatDemoSessionDescriptor();Ticket=FHapbeatDemoSessionTicket();
    FString Error;
    if(!bDescriptorRead) {UE_LOG(LogHapbeatDemoSession,Warning,TEXT("DEMO_SESSION_NO_DESCRIPTOR hapbeat-demo-session.json not found: session tickets and Demo Switch are off"));}
    else if(!FHapbeatDemoSessionDescriptor::Parse(DescriptorJson,Descriptor,Error)) {UE_LOG(LogHapbeatDemoSession,Error,TEXT("DEMO_SESSION_BAD_DESCRIPTOR %s"),*Error);}
    else bHasDescriptor=true;
    if(bTicketPresent) {
        // A rejected ticket is the same as no ticket: the demo runs normally (spec: 受け取り 1).
        if(!bHasDescriptor) {UE_LOG(LogHapbeatDemoSession,Warning,TEXT("DEMO_SESSION_TICKET_REJECTED no descriptor"));}
        else if(!HapbeatDemoSession::AcceptTicket(TicketJson,Descriptor.DemoId,Ticket,Error)) {UE_LOG(LogHapbeatDemoSession,Warning,TEXT("DEMO_SESSION_TICKET_REJECTED %s"),*Error);}
        else bSessionActive=true;
    }
    TArray<FString> Warnings;
    Options=bHasDescriptor?HapbeatDemoSession::ResolveOptions(Descriptor,bSessionActive?Ticket.Steps[Ticket.Index].Options:TArray<TPair<FString,FString>>(),bSessionActive?&Warnings:nullptr):TMap<FString,FString>();
    for(const FString& W:Warnings) UE_LOG(LogHapbeatDemoSession,Warning,TEXT("DEMO_SESSION_OPTION %s"),*W);
    // Every step starts with haptics on; the button's visibility carries over the session.
    bHapticsEnabled=true;
    bHapticsUiVisible=bSessionActive&&Ticket.bHapticsUi;
    if(bSessionActive) UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_ACTIVE demo=%s session=%s step=%d/%d haptics_ui=%d"),
        *Descriptor.DemoId,*Ticket.SessionId,Ticket.Index+1,Ticket.Steps.Num(),bHapticsUiVisible);
    if(bHasDescriptor) {
        Receiver.IsAllowed=[this](const FString& Action){return IsControlAllowed(Action);};
        Receiver.Execute=[this](const FString& Action){return ExecuteControl(Action);};
        Receiver.Configure(Descriptor.DemoId);
    }
}

void UHapbeatDemoSessionSubsystem::ApplyDeviceAddress()
{
    FString Json,Source,Error;
    if(!HapbeatDemoSessionPlatform::ReadDeviceAddress(Json,Source)) return;
    FHapbeatDeviceAddress Address;
    if(!FHapbeatDeviceAddress::Parse(Json,Address,Error)) {UE_LOG(LogHapbeatDemoSession,Warning,TEXT("HAPBEAT_DEVICE_ADDRESS_REJECTED %s source=%s"),*Error,*Source);return;}
    UHapbeatSubsystem* Hapbeat=GetGameInstance()?GetGameInstance()->GetSubsystem<UHapbeatSubsystem>():nullptr;
    if(!Hapbeat) {UE_LOG(LogHapbeatDemoSession,Warning,TEXT("HAPBEAT_DEVICE_ADDRESS_REJECTED no Hapbeat subsystem source=%s"),*Source);return;}
    // -1 in SetAddressOverride clears that axis, so an unchanged axis passes the current override. Build-pinned
    // axes (UHapbeatConfig::ForcedOverride*) are kept by the SDK itself.
    Hapbeat->SetAddressOverride(FHapbeatDeviceAddress::ResolveAxis(Address.Player,Hapbeat->GetOverridePlayer()),
        FHapbeatDeviceAddress::ResolveAxis(Address.Group,Hapbeat->GetOverrideGroup()),false);
    UE_LOG(LogHapbeatDemoSession,Display,TEXT("HAPBEAT_DEVICE_ADDRESS player=%d group=%d source=%s"),Hapbeat->GetOverridePlayer(),Hapbeat->GetOverrideGroup(),*Source);
}

void UHapbeatDemoSessionSubsystem::Deinitialize()
{
    bInitialized=false;Receiver.Stop();Controls.Reset();
    FCoreDelegates::ApplicationWillDeactivateDelegate.Remove(DeactivateHandle);
    FCoreDelegates::VRHeadsetRecenter.Remove(RecenterHandle);
    if(Ui.IsValid()) Ui->Destroy();
    Super::Deinitialize();
}

TStatId UHapbeatDemoSessionSubsystem::GetStatId() const
{
    RETURN_QUICK_DECLARE_CYCLE_STAT(UHapbeatDemoSessionSubsystem,STATGROUP_Tickables);
}

UWorld* UHapbeatDemoSessionSubsystem::GetTickableGameObjectWorld() const
{
    return GetGameInstance()?GetGameInstance()->GetWorld():nullptr;
}

void UHapbeatDemoSessionSubsystem::RegisterControl(const FString& Action,const UObject* Owner,TFunction<bool()> Handler)
{
    Controls.RemoveAll([&](const FControl& C){return C.Action==Action;});
    Controls.Add({Action,Owner,MoveTemp(Handler)});
}

void UHapbeatDemoSessionSubsystem::UnregisterControls(const UObject* Owner)
{
    Controls.RemoveAll([&](const FControl& C){return !C.Owner.IsValid()||C.Owner.Get()==Owner;});
}

bool UHapbeatDemoSessionSubsystem::IsControlAllowed(const FString& Action) const
{
    if(Action.StartsWith(TEXT("haptics_"))) return Descriptor.bHapticsToggle
        &&(Action==TEXT("haptics_on")||Action==TEXT("haptics_off")||Action==TEXT("haptics_ui_show")||Action==TEXT("haptics_ui_hide"));
    return Controls.ContainsByPredicate([&](const FControl& C){return C.Action==Action&&C.Owner.IsValid();});
}

bool UHapbeatDemoSessionSubsystem::ExecuteControl(const FString& Action)
{
    if(Action==TEXT("haptics_on")||Action==TEXT("haptics_off")) {SetHapticsEnabled(Action==TEXT("haptics_on"));return true;}
    if(Action==TEXT("haptics_ui_show")||Action==TEXT("haptics_ui_hide")) {SetHapticsUiVisible(Action==TEXT("haptics_ui_show"));return true;}
    const FControl* Control=Controls.FindByPredicate([&](const FControl& C){return C.Action==Action&&C.Owner.IsValid();});
    if(!Control||!Control->Handler()) return false;
    // restart reloads the experience: an open completion or pause panel belongs to the previous run.
    if(Action==TEXT("restart")) {HideCompletion();HidePause();}
    return true;
}

void UHapbeatDemoSessionSubsystem::SetHapticsEnabled(bool bEnabled)
{
    if(bHapticsEnabled==bEnabled) return;
    bHapticsEnabled=bEnabled;
    UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_HAPTICS on=%d"),bHapticsEnabled);
    OnHapticsChanged.Broadcast(bHapticsEnabled);
}

void UHapbeatDemoSessionSubsystem::SetHapticsUiVisible(bool bVisible)
{
    bHapticsUiVisible=bVisible;
    UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_HAPTICS_UI visible=%d"),bHapticsUiVisible);
}

const FHapbeatDemoSessionComponent& UHapbeatDemoSessionSubsystem::HubComponent()
{
    static const FHapbeatDemoSessionComponent Hub{TEXT("jp.hapbeat.demohub"),TEXT("com.unity3d.player.UnityPlayerGameActivity")};
    return Hub;
}

void UHapbeatDemoSessionSubsystem::ShowCompletion()
{
    if(!bSessionActive||bCompletionShown||IsLeaving()) return;
    if(bPauseShown) SetPauseShown(false);
    bCompletionShown=true;
    UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_COMPLETION_SHOWN step=%d/%d retry=%d next=%s"),Ticket.Index+1,Ticket.Steps.Num(),
        Ticket.Steps[Ticket.Index].bRetry,Ticket.HasNextStep()?*Ticket.Steps[Ticket.Index+1].DemoId:TEXT("finish"));
    OnGameplayPausedChanged.Broadcast(true);
}

void UHapbeatDemoSessionSubsystem::HideCompletion()
{
    if(!bCompletionShown) return;
    bCompletionShown=false;
    if(Ui.IsValid()) Ui->HideCompletion();
    UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_COMPLETION_HIDDEN"));
    OnGameplayPausedChanged.Broadcast(false);
}

void UHapbeatDemoSessionSubsystem::ShowPause()
{
    if(!PauseSettings.bEnabled||bPauseShown||bCompletionShown||IsLeaving()) return;
    SetPauseShown(true);
}

void UHapbeatDemoSessionSubsystem::HidePause()
{
    if(bPauseShown) SetPauseShown(false);
}

void UHapbeatDemoSessionSubsystem::SetPauseShown(bool bShown)
{
    bPauseShown=bShown;
    if(!bShown&&Ui.IsValid()) Ui->HidePause();
    UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_PAUSE %s"),bShown?TEXT("shown"):TEXT("hidden"));
    OnPauseChanged.Broadcast(bShown);
    OnGameplayPausedChanged.Broadcast(bShown);
}

void UHapbeatDemoSessionSubsystem::ReturnToHub()
{
    if(!bHubInstalled||IsLeaving()) return;
    const FHapbeatDemoSessionComponent& Hub=HubComponent();
    UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_RETURN_TO_HUB target=%s/%s"),*Hub.Package,*Hub.Activity);
    if(!HapbeatDemoSessionPlatform::Launch(Hub,FString())) {
        UE_LOG(LogHapbeatDemoSession,Error,TEXT("DEMO_SESSION_LAUNCH_FAILED %s/%s"),*Hub.Package,*Hub.Activity);
        ShowLaunchError(TEXT("Hub を起動できませんでした。\nスタッフにお知らせください。"));
        return;
    }
    StartHandoff();
}

FString UHapbeatDemoSessionSubsystem::MakeNextTicketJson() const
{
    return bSessionActive?Ticket.MakeNext(bHapticsUiVisible).ToJson():FString();
}

void UHapbeatDemoSessionSubsystem::LaunchNextOrFinish()
{
    if(!bSessionActive||IsLeaving()) return;
    const FHapbeatDemoSessionComponent& Target=Ticket.NextTarget();
    const FString Json=MakeNextTicketJson();
    UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_LAUNCH target=%s/%s index=%d haptics_ui=%d"),*Target.Package,*Target.Activity,Ticket.Index+1,bHapticsUiVisible);
    if(!HapbeatDemoSessionPlatform::CanLaunch()) {
        UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_LAUNCH_SKIPPED not Android; ticket=%s"),*Json);
        return;
    }
    if(!HapbeatDemoSessionPlatform::Launch(Target,Json)) {
        UE_LOG(LogHapbeatDemoSession,Error,TEXT("DEMO_SESSION_LAUNCH_FAILED %s/%s"),*Target.Package,*Target.Activity);
        ShowLaunchError(TEXT("次のデモを起動できませんでした。\nスタッフにお知らせください。"));
        return;
    }
    StartHandoff();
}

FString UHapbeatDemoSessionSubsystem::NextLabel() const
{
    if(!bSessionActive) return FString();
    return Ticket.HasNextStep()?FString::Printf(TEXT("次へ：%s"),*Ticket.Steps[Ticket.Index+1].Title):FString(TEXT("デモを終了"));
}

void UHapbeatDemoSessionSubsystem::ShowLaunchError(const FString& Text)
{
    if(!Ui.IsValid()) return;
    if(bPauseShown) Ui->SetPauseError(Text); else Ui->SetCompletionError(Text);
}

void UHapbeatDemoSessionSubsystem::StartHandoff()
{
    // Do not end this task yet: Quest would switch to its home environment and send the starting runtime to the
    // background. Wait until this application itself has gone to the background (UpdateHandoff).
    bHandoffHadVRFocus=FApp::UseVRFocus()&&FApp::HasVRFocus();
    Handoff.Start();
    UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_LAUNCH_WAITING background within %.0f s (vr_focus=%d)"),FHapbeatLaunchHandoff::TimeoutSeconds,bHandoffHadVRFocus);
}

void UHapbeatDemoSessionSubsystem::UpdateHandoff(bool bBackgrounded,float Dt)
{
    switch(Handoff.Update(bBackgrounded,Dt)) {
    case FHapbeatLaunchHandoff::EStep::Exit:
        UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_BACKGROUNDED ending this demo"));
        ExitAfterLaunch();
        break;
    case FHapbeatLaunchHandoff::EStep::Failed:
        UE_LOG(LogHapbeatDemoSession,Error,TEXT("DEMO_SESSION_LAUNCH_FAILED still in front after %.0f s"),FHapbeatLaunchHandoff::TimeoutSeconds);
        ShowLaunchError(TEXT("起動を確認できませんでした。\nスタッフにお知らせください。"));
        break;
    default:
        break;
    }
}

void UHapbeatDemoSessionSubsystem::OnApplicationDeactivated()
{
    if(Handoff.IsWaiting()) UpdateHandoff(true,0);
}

void UHapbeatDemoSessionSubsystem::OnRecenter()
{
    // The tracking origin moves at the event's changeTime (and a demo may move its pawn in response, e.g. the
    // Safety Mill stand spot): keep putting the panels in front of the head for a short while.
    RecenterSeconds=.3f;
    UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_RECENTER"));
}

void UHapbeatDemoSessionSubsystem::ExitAfterLaunch()
{
    // This application is in the background behind the next runtime: stop haptics, sound and the 7710 listener,
    // then end this task. Only once.
    if(bExiting) return;
    bExiting=true;
    SetHapticsEnabled(false);
    if(UWorld* World=GetTickableGameObjectWorld()) {
        FAudioDeviceHandle Audio=World->GetAudioDevice();
        if(Audio.IsValid()) Audio->StopAllSounds(true);
    }
    Receiver.Stop();
    HapbeatDemoSessionPlatform::FinishTask();
    FPlatformMisc::RequestExit(false);
}

FHapbeatPauseInput UHapbeatDemoSessionSubsystem::ReadPauseInput(APlayerController* PC,const FVector& Eye) const
{
    FHapbeatPauseInput In;In.Eye=Eye;
    if(!GEngine||!GEngine->XRSystem.IsValid()) return In;
    bool bUseFocus=false,bFocus=true;
    UHeadMountedDisplayFunctionLibrary::GetVRFocusState(bUseFocus,bFocus);
    if(bUseFocus&&!bFocus) return In;
    // The controller's ≡ button, the left hand's joints and, in SystemMenu mode, the runtime's hand menu flag
    // (XR_FB_hand_tracking_aim, FHapbeatDemoSessionModule).
    In.bMenuButton=PC->IsInputKeyDown(EKeys::OculusTouch_Left_Menu_Click);
    In.bHandMenu=PauseDetector.Gesture==EHapbeatPauseGesture::SystemMenu&&HapbeatDemoSessionHandMenu::IsLeftMenuPressed();
    FXRHandTrackingState S;
    UHeadMountedDisplayFunctionLibrary::GetHandTrackingState(PC,EXRSpaceType::UnrealWorldSpace,EControllerHand::Left,S);
    In.SetLeftHand(S);
    return In;
}

FHapbeatSessionPointerInput UHapbeatDemoSessionSubsystem::ReadPointers(APlayerController* PC) const
{
    FHapbeatSessionPointerInput In;
    if(!GEngine||!GEngine->XRSystem.IsValid()) return In;
    bool bUseFocus=false,bFocus=true;
    UHeadMountedDisplayFunctionLibrary::GetVRFocusState(bUseFocus,bFocus);
    if(bUseFocus&&!bFocus) return In;
    static const FKey Triggers[2]={EKeys::OculusTouch_Left_Trigger_Click,EKeys::OculusTouch_Right_Trigger_Click};
    for(int32 H=0;H<2;++H) {
        const EControllerHand Hand=H==0?EControllerHand::Left:EControllerHand::Right;
        FXRHandTrackingState S;
        UHeadMountedDisplayFunctionLibrary::GetHandTrackingState(PC,EXRSpaceType::UnrealWorldSpace,Hand,S);
        if(S.bValid&&S.TrackingStatus==ETrackingStatus::Tracked&&S.HandKeyLocations.Num()==EHandKeypointCount) {
            const FVector Tip=S.HandKeyLocations[int32(EHandKeypoint::IndexTip)];
            if(!Tip.ContainsNaN()) {In.bFinger[H]=true;In.Finger[H]=Tip;}
            continue;
        }
        // No tracked hand on this side: a held controller points with its aim pose and presses with the trigger.
        FXRMotionControllerState C;
        UHeadMountedDisplayFunctionLibrary::GetMotionControllerState(PC,EXRSpaceType::UnrealWorldSpace,Hand,EXRControllerPoseType::Aim,C);
        if(C.bValid&&C.TrackingStatus!=ETrackingStatus::NotTracked&&!C.ControllerLocation.ContainsNaN()&&!C.ControllerRotation.ContainsNaN()&&C.ControllerRotation.IsNormalized()) {
            In.bRay[H]=true;In.RayOrigin[H]=C.ControllerLocation;In.RayDirection[H]=C.ControllerRotation.GetForwardVector();
            In.bTrigger[H]=PC->IsInputKeyDown(Triggers[H]);
        }
    }
    return In;
}

AHapbeatDemoSessionUi* UHapbeatDemoSessionSubsystem::EnsureUi(UWorld* World)
{
    if(Ui.IsValid()&&Ui->GetWorld()==World) return Ui.Get();
    FActorSpawnParameters Params;Params.ObjectFlags|=RF_Transient;
    Params.SpawnCollisionHandlingOverride=ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    Ui=World->SpawnActor<AHapbeatDemoSessionUi>(Params);
    return Ui.Get();
}

void UHapbeatDemoSessionSubsystem::Tick(float Dt)
{
    if(!bInitialized) return;
    if(Handoff.IsWaiting()) UpdateHandoff(bHandoffHadVRFocus&&!FApp::HasVRFocus(),Dt);
    Receiver.Tick();
    UWorld* World=GetTickableGameObjectWorld();
    if(!World||!World->IsGameWorld()||World->bIsTearingDown) return;
    APlayerController* PC=World->GetFirstPlayerController();
    if(!PC||!PC->PlayerCameraManager) return;
    const FVector Eye=PC->PlayerCameraManager->GetCameraLocation();
    const FRotator Rotation=PC->PlayerCameraManager->GetCameraRotation();
    if(PauseSettings.bEnabled&&!bCompletionShown&&!IsLeaving()&&PauseDetector.Update(ReadPauseInput(PC,Eye),Dt)) {
        // The same gesture / button closes it again (= 再開).
        if(bPauseShown) HidePause(); else ShowPause();
    }
    // After a system recenter: open panels (the completion panel too, anchor or not) go in front of the head again.
    const bool bRecentering=RecenterSeconds>=0;
    if(bRecentering) RecenterSeconds-=Dt;
    const bool bHapticsButton=bHasDescriptor&&Descriptor.bHapticsToggle&&bHapticsUiVisible&&!bExiting;
    if(!bCompletionShown&&!bPauseShown&&!bHapticsButton&&!Ui.IsValid()) return;
    AHapbeatDemoSessionUi* View=EnsureUi(World);
    if(!View) return;
    if(bCompletionShown&&!View->IsCompletionShown()) {
        FHapbeatSessionCompletionView Model;
        Model.StepNumber=Ticket.Index+1;Model.StepCount=Ticket.Steps.Num();Model.bRetry=Ticket.Steps[Ticket.Index].bRetry;
        Model.NextLabel=NextLabel();
        View->ShowCompletion(Model,AHapbeatDemoSessionUi::PlacePanel(Eye,Rotation,UHapbeatDemoSessionPanelAnchor::Find(World)));
    } else if(!bCompletionShown&&View->IsCompletionShown()) View->HideCompletion();
    if(bPauseShown&&!View->IsPauseShown()) View->ShowPause(bHubInstalled,NextLabel(),AHapbeatDemoSessionUi::PlacePanel(Eye,Rotation));
    else if(!bPauseShown&&View->IsPauseShown()) View->HidePause();
    if(bRecentering) View->Reposition(AHapbeatDemoSessionUi::PlacePanel(Eye,Rotation));
    View->SetHapticsButton(bHapticsButton,bHapticsEnabled);
    const AHapbeatDemoSessionUi::FEvents E=View->Step(ReadPointers(PC),Eye,Rotation,Dt);
    // While a launch waits for the background the panels stay up, but nothing else is started from them.
    if(IsLeaving()) return;
    if(E.bToggleHaptics) SetHapticsEnabled(!bHapticsEnabled);
    if(E.bRetry) {
        UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_RETRY step=%d"),Ticket.Index+1);
        HideCompletion();OnRestartRequested.Broadcast();
    }
    if(E.bNext||E.bPauseNext) {
        if(E.bPauseNext) UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_PAUSE_NEXT"));
        LaunchNextOrFinish();
    }
    if(E.bResume||E.bRestart) {
        PauseDetector.Reset();
        HidePause();
        if(E.bRestart) {UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SESSION_PAUSE_RESTART"));OnRestartRequested.Broadcast();}
    }
    if(E.bHub) ReturnToHub();
}
