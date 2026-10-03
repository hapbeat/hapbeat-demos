#pragma once
#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "HapbeatDemoSessionUi.generated.h"

class UWidgetComponent;
class UStaticMeshComponent;
class UHapbeatDemoSessionPanelAnchor;
class SCanvas;

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
 * (in front of the user below eye level, or at the demo's UHapbeatDemoSessionPanelAnchor), the pause panel
 * (in front of the user), the in-view haptics ON/OFF button (lower left of the view), plus a thin ray from
 * each tracked controller while the UI is visible. Panels stay where they appeared.
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
        /** Pause panel: 再開 / 最初からやり直す / Hub に戻る. */
        bool bResume=false, bRestart=false, bHub=false;
    };
    /** Panel buttons ignore input for this long after the panel appears. */
    static constexpr float CompletionInputDelay=1.f;
    /**
     * Where a panel appears. Without an anchor: 55 cm ahead of the eye (head yaw only), 12 cm below it, facing the
     * eye. With one: at the anchor, its yaw turned toward the eye (or the anchor's own rotation with bUseRotation,
     * turned round when the eye is behind it). +X of the result is the panel's face.
     */
    static FTransform PlacePanel(const FVector& Eye,const FRotator& ViewRotation,const UHapbeatDemoSessionPanelAnchor* Anchor=nullptr);
    void ShowCompletion(const FHapbeatSessionCompletionView& View,const FTransform& At);
    void HideCompletion();
    bool IsCompletionShown() const {return Completion.bShown;}
    bool IsCompletionAccepting() const {return Completion.IsAccepting();}
    /** Error line of the panel (fixed-height area); empty clears it. */
    void SetCompletionError(const FString& Text) {Completion.Error=Text;}
    /** bHub: show 「Hub に戻る」 (only when the Hub is installed). */
    void ShowPause(bool bHub,const FTransform& At);
    void HidePause();
    bool IsPauseShown() const {return Pause.bShown;}
    bool IsPauseAccepting() const {return Pause.IsAccepting();}
    void SetPauseError(const FString& Text) {Pause.Error=Text;}
    void SetHapticsButton(bool bVisible,bool bOn);
    FEvents Step(const FHapbeatSessionPointerInput& Input,const FVector& Eye,const FRotator& ViewRotation,float Dt);
private:
    /** One floating panel's input state; its widget is CompletionPanel / PausePanel. */
    struct FPanel
    {
        FHapbeatSessionPressTracker Press;
        TArray<FBox2D> Buttons;
        int32 Hover=INDEX_NONE;
        bool bShown=false;
        float Seconds=0;
        FString Error;
        bool IsAccepting() const {return bShown&&Seconds>=CompletionInputDelay;}
    };
    void BuildCompletionWidget();
    void BuildPauseWidget(bool bHub);
    void BuildHapticsWidget();
    /** Title, sub line and the reserved error line; the buttons are added by the caller. */
    TSharedRef<SCanvas> MakePanelCanvas(const FPanel& Panel,const FString& Title,TFunction<FText()> SubLine);
    void AddPanelButton(SCanvas& Canvas,const FPanel& Panel,int32 Index,bool bPrimary,TFunction<FText()> Label,int32 FontSize=28);
    void ShowPanel(UWidgetComponent* Widget,FPanel& Panel,const FTransform& At);
    void HidePanel(UWidgetComponent* Widget,FPanel& Panel);
    int32 StepPanel(UWidgetComponent* Widget,FPanel& Panel,const FHapbeatSessionPointerInput& Input,float Dt,float* RayHit);
    void PlaceHapticsButton(const FVector& Eye,const FRotator& ViewRotation,float Dt);
    FLinearColor ButtonColor(const FPanel& Panel,int32 Button,bool bPrimary) const;
    UPROPERTY() TObjectPtr<UWidgetComponent> CompletionPanel;
    UPROPERTY() TObjectPtr<UWidgetComponent> PausePanel;
    UPROPERTY() TObjectPtr<UWidgetComponent> HapticsButton;
    UPROPERTY() TObjectPtr<UStaticMeshComponent> Rays[2];
    FPanel Completion, Pause;
    FHapbeatSessionPressTracker HapticsPress;
    FHapbeatSessionCompletionView CompletionView;
    int32 RetryButton=INDEX_NONE, NextButton=INDEX_NONE;
    int32 ResumeButton=INDEX_NONE, RestartButton=INDEX_NONE, HubButton=INDEX_NONE;
    bool bHapticsBuilt=false;
    bool bHapticsOn=true, bHapticsHover=false, bHapticsPlaced=false;
    float HapticsYaw=0;
    FVector HapticsLocation=FVector::ZeroVector;
};
