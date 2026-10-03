#pragma once
#include "CoreMinimal.h"

struct FXRHandTrackingState;

/** How the shared pause is opened with a tracked hand. The controller's menu button (≡) opens it in both modes. */
enum class EHapbeatPauseGesture : uint8
{
    /** A: Quest's own menu gesture (left palm toward the face, pinch), reported as the left menu button. */
    SystemMenu,
    /** B: left palm toward the face, thumb and index pinched, held for HoldSeconds (read from the hand joints). */
    PalmPinchHold,
};

/**
 * Shared pause settings, the counterpart of the Unity package's settings asset. Off unless the project opts in
 * in its checked-in Config/DefaultGame.ini (demos with their own menu leave it off):
 *   [HapbeatDemoSession.Pause]
 *   Enabled=True
 *   Gesture=SystemMenu      ; or PalmPinchHold
 */
struct HAPBEATDEMOSESSION_API FHapbeatPauseSettings
{
    bool bEnabled=false;
    EHapbeatPauseGesture Gesture=EHapbeatPauseGesture::SystemMenu;
    static FHapbeatPauseSettings Load();
    /** "SystemMenu" / "PalmPinchHold" (case-insensitive); false leaves Out unchanged. */
    static bool ParseGesture(const FString& Text,EHapbeatPauseGesture& Out);
};

/** One frame of pause input: the left menu button (controller ≡, or the system menu gesture in mode A) and the left hand's joints. */
struct FHapbeatPauseInput
{
    bool bMenuButton=false;
    /** Left hand joints (world space), valid only with bLeftHand. */
    bool bLeftHand=false;
    TArray<FVector> LeftHand;
    FVector Eye=FVector::ZeroVector;
    /** LeftHand from an OpenXR hand tracking state (false when it is not tracked). */
    bool SetLeftHand(const FXRHandTrackingState& State);
};

/**
 * The single place that decides "open / close the pause". Rising edge of the menu button in both modes; in
 * PalmPinchHold also the left palm facing the eye with thumb and index pinched for HoldSeconds (the T-Rex
 * development pause). After a hold has fired the hand must let go before it fires again.
 */
struct HAPBEATDEMOSESSION_API FHapbeatPauseDetector
{
    static constexpr float HoldSeconds=2.f;
    EHapbeatPauseGesture Gesture=EHapbeatPauseGesture::SystemMenu;
    /** True on the frame the pause should toggle. */
    bool Update(const FHapbeatPauseInput& In,float Dt);
    /** Drops a hold in progress; the hand has to let go before the next one. */
    void Reset();
    float GetHoldSeconds() const {return Hold;}
    /** Left palm toward the eye and thumb tip to index tip within PinchCm. */
    static bool IsPalmPinch(TConstArrayView<FVector> LeftHand,const FVector& Eye,float PinchCm);
private:
    bool bMenuDown=false, bArmed=true;
    float Hold=0;
};
