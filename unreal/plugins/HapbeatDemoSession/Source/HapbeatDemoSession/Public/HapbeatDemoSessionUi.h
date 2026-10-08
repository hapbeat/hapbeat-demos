#pragma once
#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "HapbeatDemoSessionUi.generated.h"

class UWidgetComponent;
class UStaticMeshComponent;
class UMaterialInterface;
class USoundBase;
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
 * Widget pixels of a panel (the Unity package's DemoSessionPanel layout: its millimetres x 2): the size, the
 * title / sub line / reserved error line and the buttons, top to bottom.
 */
struct FHapbeatSessionPanelLayout
{
    FVector2D Size=FVector2D::ZeroVector;
    FBox2D Title=FBox2D(ForceInit), SubLine=FBox2D(ForceInit), Error=FBox2D(ForceInit);
    TArray<FBox2D> Buttons;
};

/**
 * World-space Demo Session UI, driven by UHapbeatDemoSessionSubsystem every frame: the completion panel
 * (in front of the user below eye level, or at the demo's UHapbeatDemoSessionPanelAnchor), the pause panel
 * (in front of the user), the in-view haptics ON/OFF and 視線をリセット buttons (lower left of the view, side by side), plus a
 * thin ray from each tracked controller while the UI is visible. Panels stay where they appeared.
 *
 * Looks like the Unity package's panels (DemoSessionUi.cs): the same colours, Noto Sans CJK JP, sizes and vertical
 * button stacks, and the same short click on a press. Every panel draws on top of the scene (no depth test,
 * PanelSortPriority); hands drawn with HandSortPriority or above stay in front of the panels.
 */
UCLASS(NotPlaceable,Transient)
class HAPBEATDEMOSESSION_API AHapbeatDemoSessionUi : public AActor
{
    GENERATED_BODY()
public:
    AHapbeatDemoSessionUi();
    virtual void PostInitializeComponents() override;
    struct FEvents
    {
        bool bRetry=false, bNext=false, bToggleHaptics=false, bRecenter=false;
        /** Pause panel: 再開 / 最初からやり直す / 次へ or デモを終了 (bPauseNext) / Hub に戻る. */
        bool bResume=false, bRestart=false, bPauseNext=false, bHub=false;
    };
    /** Panel buttons ignore input for this long after the panel appears. */
    static constexpr float CompletionInputDelay=1.f;
    /**
     * Translucency sort priority of every panel. Panels are drawn after the scene without a depth test (on top of
     * the models); a demo's translucent hand layers use HandSortPriority or above so that a hand in front of a panel
     * is drawn over it (the Unity package's canvas sorting order 500 and hand renderers 501).
     */
    static constexpr int32 PanelSortPriority=500;
    static constexpr int32 HandSortPriority=PanelSortPriority+1;
    /** Widget scale: 2 px per Unity millimetre. */
    static constexpr float PanelCmPerPixel=.05f;
    static FHapbeatSessionPanelLayout CompletionLayout(int32 ButtonCount);
    static FHapbeatSessionPanelLayout PauseLayout(int32 ButtonCount);
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
    /**
     * bHub: show 「Hub に戻る」 (only when the Hub is installed). NextLabel: "次へ：<title>" / "デモを終了" in a
     * session (the completion panel's next button), empty for none.
     */
    void ShowPause(bool bHub,const FString& NextLabel,const FTransform& At);
    void HidePause();
    bool IsPauseShown() const {return Pause.bShown;}
    bool IsPauseAccepting() const {return Pause.IsAccepting();}
    void SetPauseError(const FString& Text) {Pause.Error=Text;}
    /** Moves the open panels to At (a recenter) without restarting their input delay; the in-view buttons snap to the view again. */
    void Reposition(const FTransform& At);
    void SetHapticsButton(bool bVisible,bool bOn);
    void SetRecenterButton(bool bVisible);
    /**
     * Centre of an in-view button for the head's heading (world yaw), as in Unity's DemoSessionCornerButtons: 45 cm
     * from the eye, 30 deg below the horizon, 視線をリセット (bRecenter) 19.5 deg and haptics 5 deg left of the heading;
     * 88 x 64 mm each, about 10 mm apart.
     */
    static FVector InViewButtonLocation(const FVector& Eye,float HeadingYaw,bool bRecenter);
    FEvents Step(const FHapbeatSessionPointerInput& Input,const FVector& Eye,const FRotator& ViewRotation,float Dt);
private:
    /** One floating panel's input and look state; its widget is CompletionPanel / PausePanel / HapticsButton / RecenterButton. */
    struct FPanel
    {
        FHapbeatSessionPressTracker Press;
        FVector2D Size=FVector2D::ZeroVector;
        TArray<FBox2D> Buttons;
        int32 Hover=INDEX_NONE;
        /** Pressed button and how long it still shows the pressed colour. */
        int32 Flash=INDEX_NONE;
        float FlashSeconds=0;
        bool bShown=false;
        float Seconds=0;
        FString Error;
        bool IsAccepting() const {return bShown&&Seconds>=CompletionInputDelay;}
    };
    /** A head-following button (haptics, 視線をリセット) and where it is drawn. */
    struct FInViewButton
    {
        FPanel Face;
        FVector Location=FVector::ZeroVector;
        FQuat Rotation=FQuat::Identity;
        bool bPlaced=false;
    };
    void BuildCompletionWidget();
    void BuildPauseWidget(bool bHub,const FString& NextLabel);
    void BuildInViewWidget(UWidgetComponent* Widget,FInViewButton& Button,TFunction<FText()> Label,TFunction<bool()> Highlighted);
    /** Background, title, sub line and the reserved error line; the buttons are added by the caller. */
    TSharedRef<SCanvas> MakePanelCanvas(const FPanel& Panel,const FHapbeatSessionPanelLayout& Layout,const FString& Title,TFunction<FText()> SubLine);
    void AddPanelButton(SCanvas& Canvas,const FPanel& Panel,int32 Index,TFunction<FText()> Label,TFunction<bool()> Highlighted=nullptr);
    void ShowPanel(UWidgetComponent* Widget,FPanel& Panel,const FTransform& At);
    void HidePanel(UWidgetComponent* Widget,FPanel& Panel);
    int32 StepPanel(UWidgetComponent* Widget,FPanel& Panel,const FHapbeatSessionPointerInput& Input,float InputDelay,float Dt,float* RayHit);
    void SetInViewVisible(UWidgetComponent* Widget,FInViewButton& Button,bool bVisible);
    void PlaceInViewButtons(const FVector& Eye,const FRotator& ViewRotation,float Dt);
    FLinearColor ButtonColor(const FPanel& Panel,int32 Button,bool bHighlighted) const;
    /** The short click of a pressed button (game worlds only; plays while the world is paused). */
    void PlayClick();
    UPROPERTY() TObjectPtr<UWidgetComponent> CompletionPanel;
    UPROPERTY() TObjectPtr<UWidgetComponent> PausePanel;
    UPROPERTY() TObjectPtr<UWidgetComponent> HapticsButton;
    UPROPERTY() TObjectPtr<UWidgetComponent> RecenterButton;
    UPROPERTY() TObjectPtr<UStaticMeshComponent> Rays[2];
    /** Plugin content (Scripts/create_content.py): the widget material without depth test, the ray material, the click. */
    UPROPERTY() TObjectPtr<UMaterialInterface> PanelMaterial;
    UPROPERTY() TObjectPtr<USoundBase> ClickSound;
    FPanel Completion, Pause;
    FInViewButton Haptics, Recenter;
    FHapbeatSessionCompletionView CompletionView;
    int32 RetryButton=INDEX_NONE, NextButton=INDEX_NONE;
    int32 ResumeButton=INDEX_NONE, RestartButton=INDEX_NONE, PauseNextButton=INDEX_NONE, HubButton=INDEX_NONE;
    FString PauseNextLabel;
    bool bHapticsOn=true;
};
