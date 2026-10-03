#include "HapbeatDemoSessionPause.h"
#include "HapbeatDemoSessionLog.h"
#include "HeadMountedDisplayTypes.h"
#include "Misc/ConfigCacheIni.h"

namespace
{
    // T-Rex development pause (TrexXRInteraction ProcessDebugPause): palm within 60 deg of the eye direction,
    // pinch closes at 1.5 cm and stays closed up to 3 cm.
    constexpr float FacingDot=.5f, PinchStart=1.5f, PinchKeep=3.f;
}

bool FHapbeatPauseSettings::ParseGesture(const FString& Text,EHapbeatPauseGesture& Out)
{
    if(Text.Equals(TEXT("SystemMenu"),ESearchCase::IgnoreCase)) {Out=EHapbeatPauseGesture::SystemMenu;return true;}
    if(Text.Equals(TEXT("PalmPinchHold"),ESearchCase::IgnoreCase)) {Out=EHapbeatPauseGesture::PalmPinchHold;return true;}
    return false;
}

FHapbeatPauseSettings FHapbeatPauseSettings::Load()
{
    FHapbeatPauseSettings S;
    if(!GConfig) return S;
    GConfig->GetBool(TEXT("HapbeatDemoSession.Pause"),TEXT("Enabled"),S.bEnabled,GGameIni);
    FString Gesture;
    if(GConfig->GetString(TEXT("HapbeatDemoSession.Pause"),TEXT("Gesture"),Gesture,GGameIni)&&!ParseGesture(Gesture,S.Gesture))
        UE_LOG(LogHapbeatDemoSession,Warning,TEXT("DEMO_SESSION_PAUSE unknown Gesture=%s (SystemMenu is used)"),*Gesture);
    return S;
}

bool FHapbeatPauseInput::SetLeftHand(const FXRHandTrackingState& S)
{
    bLeftHand=S.bValid&&S.TrackingStatus==ETrackingStatus::Tracked&&S.HandKeyLocations.Num()==EHandKeypointCount;
    LeftHand=bLeftHand?S.HandKeyLocations:TArray<FVector>();
    return bLeftHand;
}

bool FHapbeatPauseDetector::IsPalmPinch(TConstArrayView<FVector> K,const FVector& Eye,float PinchCm)
{
    if(K.Num()!=EHandKeypointCount) return false;
    auto At=[&](EHandKeypoint Joint){return K[int32(Joint)];};
    // Left hand: forward x index-side points out of the back of the hand, so the palm normal is its negative.
    const FVector Palm=-FVector::CrossProduct(At(EHandKeypoint::MiddleProximal)-At(EHandKeypoint::Wrist),
        At(EHandKeypoint::IndexProximal)-At(EHandKeypoint::LittleProximal)).GetSafeNormal();
    const bool bFacing=FVector::DotProduct(Palm,(Eye-At(EHandKeypoint::Palm)).GetSafeNormal())>FacingDot;
    return bFacing&&FVector::Distance(At(EHandKeypoint::ThumbTip),At(EHandKeypoint::IndexTip))<PinchCm;
}

bool FHapbeatPauseDetector::Update(const FHapbeatPauseInput& In,float Dt)
{
    const bool bPressed=In.bMenuButton&&!bMenuDown;
    bMenuDown=In.bMenuButton;
    if(Gesture==EHapbeatPauseGesture::PalmPinchHold) {
        if(!In.bLeftHand||!IsPalmPinch(In.LeftHand,In.Eye,Hold>0?PinchKeep:PinchStart)) {Hold=0;bArmed=true;}
        else if(bArmed) {
            Hold+=Dt;
            if(Hold>=HoldSeconds) {Hold=0;bArmed=false;return true;}
        }
    }
    return bPressed;
}

void FHapbeatPauseDetector::Reset()
{
    // A button still held or a pinch still made does not fire again until it is released.
    bArmed=false;Hold=0;
}
