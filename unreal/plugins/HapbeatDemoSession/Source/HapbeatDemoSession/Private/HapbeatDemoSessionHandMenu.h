#pragma once
#include "CoreMinimal.h"

/**
 * The left hand's menu gesture as the OpenXR runtime reports it: XR_FB_hand_tracking_aim's MENU_PRESSED on a hand
 * tracker of this plugin (FHapbeatDemoSessionModule), made only with the shared pause in SystemMenu mode. Read on the
 * game thread.
 */
namespace HapbeatDemoSessionHandMenu
{
    /** False when the extension or hand tracking is not available, or the hand is not tracked. */
    bool IsLeftMenuPressed();
}
