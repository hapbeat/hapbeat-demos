#include "Modules/ModuleManager.h"
#include "IOpenXRExtensionPlugin.h"
#include "HapbeatDemoSessionPause.h"

/**
 * Pause mode A (SystemMenu): runtimes that emulate a controller for tracked hands do so through the
 * khr/simple_controller profile, where the palm-facing pinch (on Quest the left hand's menu gesture) is
 * /user/hand/left/input/menu/click (common mapping; not yet confirmed on a Quest). UE's legacy input has no
 * simple-controller keys, so nothing would bind it. The key override below binds OculusTouch_Left_Menu_Click to that path as
 * well as to the Touch controller's ≡, so UHapbeatDemoSessionSubsystem reads one key for both. Overrides replace
 * UE's own binding of the key, hence the Touch path is listed again. Without the pause (or in PalmPinchHold mode)
 * nothing is overridden.
 */
class FHapbeatDemoSessionModule : public IModuleInterface, public IOpenXRExtensionPlugin
{
public:
    virtual void StartupModule() override {RegisterOpenXRExtensionModularFeature();}
    virtual void ShutdownModule() override {UnregisterOpenXRExtensionModularFeature();}
    virtual FString GetDisplayName() override {return TEXT("HapbeatDemoSession");}
    virtual bool GetInputKeyOverrides(TArray<FInputKeyOpenXRProperties>& OutOverrides) override
    {
        const FHapbeatPauseSettings Settings=FHapbeatPauseSettings::Load();
        if(!Settings.bEnabled||Settings.Gesture!=EHapbeatPauseGesture::SystemMenu) return false;
        const FString Key=TEXT("OculusTouch_Left_Menu_Click"), Path=TEXT("/user/hand/left/input/menu/click");
        OutOverrides.Add({Key,TEXT("OculusTouch"),Path});
        OutOverrides.Add({Key,TEXT("SimpleController"),Path});
        return true;
    }
};

IMPLEMENT_MODULE(FHapbeatDemoSessionModule,HapbeatDemoSession)
