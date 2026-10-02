#pragma once
#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "HapbeatDemoSessionUi.generated.h"

class UWidgetComponent;
class UStaticMeshComponent;

/** One frame of pointer input, world space: tracked index fingertips and (for hands without tracking) controller aim rays. Index 0 = left, 1 = right. */
struct FHapbeatSessionPointerInput
{
    bool bFinger[2]={false,false};
    FVector Finger[2]={FVector::ZeroVector,FVector::ZeroVector};
    bool bRay[2]={false,false};
    FVector RayOrigin[2]={FVector::ZeroVector,FVector::ZeroVector};
    FVector RayDirection[2]={FVector::ForwardVector,FVector::ForwardVector};
    bool bTrigger[2]={false,false};
};

/**
 * Presses on a flat panel: an index-fingertip poke (approach from the front, then push through the face;
 * the same depth window and hysteresis as Safety Mill's FSafetyMillPoke) or a controller ray + trigger
 * press (rising edge while the ray is on the button).
 */
struct HAPBEATDEMOSESSION_API FHapbeatSessionPressTracker
{
    /**
     * Plane: panel centre, +X toward the user (unscaled). Buttons: widget pixels, origin top left.
     * Returns the pressed button, or INDEX_NONE. While !bAccepting nothing is pressed and pokes must re-approach.
     * OutHover: button under a finger or ray. InOutRayHit: per hand, shortened to the panel hit distance (cm).
     */
    int32 Update(const FTransform& Plane,float CmPerPixel,const FVector2D& Size,TConstArrayView<FBox2D> Buttons,
        const FHapbeatSessionPointerInput& Input,bool bAccepting,int32* OutHover=nullptr,float* InOutRayHit=nullptr);
    void Reset();
private:
    int32 Armed[2]={INDEX_NONE,INDEX_NONE};
    bool bTriggerDown[2]={false,false};
};

/** What the completion panel shows. */
struct FHapbeatSessionCompletionView
{
    int32 StepNumber=1, StepCount=1;
    bool bRetry=false;
    /** "次へ：<title>" or "デモを終了". */
    FString NextLabel;
};

/**
 * World-space Demo Session UI, driven by UHapbeatDemoSessionSubsystem every frame: the completion panel
 * (in front of the user, below eye level) and the in-view haptics ON/OFF button (lower left of the view),
 * plus a thin ray from each tracked controller while the UI is visible.
 */
UCLASS(NotPlaceable,Transient)
class HAPBEATDEMOSESSION_API AHapbeatDemoSessionUi : public AActor
{
    GENERATED_BODY()
public:
    AHapbeatDemoSessionUi();
    struct FEvents
    {
        bool bRetry=false, bNext=false, bToggleHaptics=false;
    };
    /** Buttons ignore input for this long after the panel appears. */
    static constexpr float CompletionInputDelay=1.f;
    void ShowCompletion(const FHapbeatSessionCompletionView& View,const FVector& Eye,const FRotator& ViewRotation);
    void HideCompletion();
    bool IsCompletionShown() const {return bCompletionShown;}
    bool IsCompletionAccepting() const {return bCompletionShown&&CompletionSeconds>=CompletionInputDelay;}
    /** Error line of the panel (fixed-height area); empty clears it. */
    void SetCompletionError(const FString& Text) {CompletionError=Text;}
    void SetHapticsButton(bool bVisible,bool bOn);
    FEvents Step(const FHapbeatSessionPointerInput& Input,const FVector& Eye,const FRotator& ViewRotation,float Dt);
private:
    void BuildCompletionWidget();
    void BuildHapticsWidget();
    void PlaceHapticsButton(const FVector& Eye,const FRotator& ViewRotation,float Dt);
    FLinearColor ButtonColor(int32 Button,bool bPrimary) const;
    UPROPERTY() TObjectPtr<UWidgetComponent> Completion;
    UPROPERTY() TObjectPtr<UWidgetComponent> HapticsButton;
    UPROPERTY() TObjectPtr<UStaticMeshComponent> Rays[2];
    FHapbeatSessionPressTracker CompletionPress, HapticsPress;
    FHapbeatSessionCompletionView CompletionView;
    TArray<FBox2D> CompletionButtons;
    int32 RetryButton=INDEX_NONE, NextButton=INDEX_NONE;
    int32 CompletionHover=INDEX_NONE;
    bool bCompletionShown=false, bCompletionBuilt=false, bHapticsBuilt=false;
    float CompletionSeconds=0;
    FString CompletionError;
    bool bHapticsOn=true, bHapticsHover=false, bHapticsPlaced=false;
    float HapticsYaw=0;
    FVector HapticsLocation=FVector::ZeroVector;
};
