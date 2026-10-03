#include "Modules/ModuleManager.h"
#include "IOpenXRExtensionPlugin.h"
#include "IOpenXRHMDModule.h"
#include "OpenXRCore.h"
#include "HapbeatDemoSessionHandMenu.h"
#include "HapbeatDemoSessionLog.h"
#include "HapbeatDemoSessionPause.h"

namespace
{
    bool GLeftMenuPressed=false;
}

bool HapbeatDemoSessionHandMenu::IsLeftMenuPressed() {return GLeftMenuPressed;}

/**
 * Pause mode A (SystemMenu): Quest reports the left hand's menu gesture (palm toward the face, pinch) through
 * XR_FB_hand_tracking_aim, as XR_HAND_TRACKING_AIM_MENU_PRESSED_BIT_FB of the left hand (the flag Unity's
 * XRHandTrackingAim MenuPressed reads). UE's OpenXRHandTracking locates the joints without that extension's struct,
 * so this plugin keeps its own left hand tracker and reads the flag each game frame (UpdateDeviceLocations runs on
 * the game thread, OnStartGameFrame). Requires XR_EXT_hand_tracking (the OpenXRHandTracking plugin); without the pause,
 * in PalmPinchHold mode or on a runtime without the extensions nothing is created and the flag stays false. The hand
 * joints are read independently (FHapbeatPauseDetector), so the pause opens without the flag too.
 */
class FHapbeatDemoSessionModule : public IModuleInterface, public IOpenXRExtensionPlugin
{
public:
    virtual void StartupModule() override {RegisterOpenXRExtensionModularFeature();}
    virtual void ShutdownModule() override {UnregisterOpenXRExtensionModularFeature();}
    virtual FString GetDisplayName() override {return TEXT("HapbeatDemoSession");}
    virtual bool GetOptionalExtensions(TArray<const ANSICHAR*>& OutExtensions) override
    {
        if(!WantsHandMenu()) return false;
        OutExtensions.Add(XR_FB_HAND_TRACKING_AIM_EXTENSION_NAME);
        return true;
    }
    virtual void PostCreateInstance(XrInstance InInstance) override
    {
        bAvailable=false;
        IOpenXRHMDModule& OpenXR=IOpenXRHMDModule::Get();
        if(!WantsHandMenu()||!OpenXR.IsExtensionEnabled(XR_EXT_HAND_TRACKING_EXTENSION_NAME)||!OpenXR.IsExtensionEnabled(XR_FB_HAND_TRACKING_AIM_EXTENSION_NAME)) {
            UE_LOG(LogHapbeatDemoSession,Log,TEXT("DEMO_SESSION_HAND_MENU off (pause SystemMenu=%d, hand tracking=%d, hand tracking aim=%d)"),WantsHandMenu(),
                OpenXR.IsExtensionEnabled(XR_EXT_HAND_TRACKING_EXTENSION_NAME),OpenXR.IsExtensionEnabled(XR_FB_HAND_TRACKING_AIM_EXTENSION_NAME));
            return;
        }
        bAvailable=XR_SUCCEEDED(xrGetInstanceProcAddr(InInstance,"xrCreateHandTrackerEXT",(PFN_xrVoidFunction*)&CreateHandTracker))
            &&XR_SUCCEEDED(xrGetInstanceProcAddr(InInstance,"xrDestroyHandTrackerEXT",(PFN_xrVoidFunction*)&DestroyHandTracker))
            &&XR_SUCCEEDED(xrGetInstanceProcAddr(InInstance,"xrLocateHandJointsEXT",(PFN_xrVoidFunction*)&LocateHandJoints));
    }
    virtual const void* OnBeginSession(XrSession InSession,const void* InNext) override
    {
        if(bAvailable&&Tracker==XR_NULL_HANDLE) {
            XrHandTrackerCreateInfoEXT Info{XR_TYPE_HAND_TRACKER_CREATE_INFO_EXT};
            Info.hand=XR_HAND_LEFT_EXT;Info.handJointSet=XR_HAND_JOINT_SET_DEFAULT_EXT;
            // A runtime without hand tracking answers XR_ERROR_FEATURE_UNSUPPORTED: the flag then stays false.
            const XrResult Result=CreateHandTracker(InSession,&Info,&Tracker);
            if(XR_FAILED(Result)) Tracker=XR_NULL_HANDLE;
            UE_LOG(LogHapbeatDemoSession,Log,TEXT("DEMO_SESSION_HAND_MENU tracker=%d result=%d"),Tracker!=XR_NULL_HANDLE,int32(Result));
        }
        return InNext;
    }
    virtual void OnDestroySession(XrSession InSession) override
    {
        if(Tracker!=XR_NULL_HANDLE) {DestroyHandTracker(Tracker);Tracker=XR_NULL_HANDLE;}
        GLeftMenuPressed=false;
    }
    virtual void UpdateDeviceLocations(XrSession InSession,XrTime DisplayTime,XrSpace TrackingSpace) override
    {
        GLeftMenuPressed=false;
        if(Tracker==XR_NULL_HANDLE) return;
        XrHandTrackingAimStateFB Aim{XR_TYPE_HAND_TRACKING_AIM_STATE_FB};
        XrHandJointLocationEXT Joints[XR_HAND_JOINT_COUNT_EXT];
        XrHandJointLocationsEXT Locations{XR_TYPE_HAND_JOINT_LOCATIONS_EXT,&Aim};
        Locations.jointCount=XR_HAND_JOINT_COUNT_EXT;Locations.jointLocations=Joints;
        XrHandJointsLocateInfoEXT Info{XR_TYPE_HAND_JOINTS_LOCATE_INFO_EXT};
        Info.baseSpace=TrackingSpace;Info.time=DisplayTime;
        GLeftMenuPressed=XR_SUCCEEDED(LocateHandJoints(Tracker,&Info,&Locations))&&Locations.isActive==XR_TRUE
            &&(Aim.status&XR_HAND_TRACKING_AIM_MENU_PRESSED_BIT_FB)!=0;
    }
private:
    static bool WantsHandMenu()
    {
        const FHapbeatPauseSettings Settings=FHapbeatPauseSettings::Load();
        return Settings.bEnabled&&Settings.Gesture==EHapbeatPauseGesture::SystemMenu;
    }
    bool bAvailable=false;
    XrHandTrackerEXT Tracker=XR_NULL_HANDLE;
    PFN_xrCreateHandTrackerEXT CreateHandTracker=nullptr;
    PFN_xrDestroyHandTrackerEXT DestroyHandTracker=nullptr;
    PFN_xrLocateHandJointsEXT LocateHandJoints=nullptr;
};

IMPLEMENT_MODULE(FHapbeatDemoSessionModule,HapbeatDemoSession)
