#pragma once
#include "CoreMinimal.h"

struct FXRHandTrackingState;

/** How the shared pause is opened with a tracked hand. The controller's menu button (≡) opens it in both modes. */
enum class EHapbeatPauseGesture : uint8
{
    /**
     * A: Quest's own menu gesture (left palm toward the face, pinch). Read from the hand joints with a short hold,
     * and from the runtime's menu flag (XR_FB_hand_tracking_aim) where it is available.
     */
    SystemMenu,
    /** B: left palm toward the face, thumb and index pinched, held for 2 s (read from the hand joints). */
    PalmPinchHold,
};

/**
 * Shared pause settings, the counterpart of the Unity package's settings asset. Off unless the project opts in
 * in its checked-in Config/DefaultGame.ini (demos with their own menu leave it off):
 *   [HapbeatDemoSession.Pause]
 *   Enabled=True
 *   Gesture=SystemMenu      ; or PalmPinchHold
 *   HoldSeconds=0.3         ; optional: how long the hand shape is held (default 0.3 in A, 2 in B)
 */
struct HAPBEATDEMOSESSION_API FHapbeatPauseSettings
{
    static constexpr float SystemMenuHoldSeconds=.3f;
    static constexpr float PalmPinchHoldSeconds=2.f;
    bool bEnabled=false;
    EHapbeatPauseGesture Gesture=EHapbeatPauseGesture::SystemMenu;
    float HoldSeconds=SystemMenuHoldSeconds;
    static FHapbeatPauseSettings Load();
    /** "SystemMenu" / "PalmPinchHold" (case-insensitive); false leaves Out unchanged. */
    static bool ParseGesture(const FString& Text,EHapbeatPauseGesture& Out);
};

/** One frame of pause input: the controller's left menu button, the runtime's hand menu flag and the left hand's joints. */
struct FHapbeatPauseInput
{
    /** The Touch controller's ≡. */
    bool bMenuButton=false;
    /** The left hand's menu gesture as the runtime reports it (XR_FB_hand_tracking_aim MENU_PRESSED; SystemMenu only). */
    bool bHandMenu=false;
    /** Left hand joints (world space), valid only with bLeftHand. */
    bool bLeftHand=false;
    TArray<FVector> LeftHand;
    FVector Eye=FVector::ZeroVector;
    /** LeftHand from an OpenXR hand tracking state (false when it is not tracked). */
    bool SetLeftHand(const FXRHandTrackingState& State);
};

/**
 * The single place that decides "open / close the pause". Rising edge of the controller's menu button in both
 * modes, and the left palm facing the eye with thumb and index pinched for HoldSeconds (0.3 s in A, 2 s in B, the
 * T-Rex development pause). In A the runtime's hand menu flag fires at once as well; it and the held shape are one
 * gesture, so one of them fires and the hand must let go (shape and flag both off) before the next.
 */
struct HAPBEATDEMOSESSION_API FHapbeatPauseDetector
{
    EHapbeatPauseGesture Gesture=EHapbeatPauseGesture::SystemMenu;
    float HoldSeconds=FHapbeatPauseSettings::SystemMenuHoldSeconds;
    /** True on the frame the pause should toggle. */
    bool Update(const FHapbeatPauseInput& In,float Dt);
    /** Drops a hold in progress; the hand has to let go before the next one. */
    void Reset();
    float GetHoldSeconds() const {return Hold;}
    /** Left palm toward the eye and thumb tip to index tip within PinchCm. */
    static bool IsPalmPinch(TConstArrayView<FVector> LeftHand,const FVector& Eye,float PinchCm);
private:
    bool bMenuDown=false, bHandMenuDown=false, bPinching=false, bArmed=true;
    float Hold=0;
};
