#pragma once
#include "CoreMinimal.h"

struct FHapbeatDemoSessionComponent;

/** Platform side of Demo Session: Android GameActivity thunks (HapbeatDemoSession_APL.xml), file fallbacks elsewhere. */
namespace HapbeatDemoSessionPlatform
{
    /** Android: assets/hapbeat-demo-session.json. Elsewhere: <project>/Config/HapbeatDemoSession/hapbeat-demo-session.json. */
    bool ReadDescriptor(FString& OutJson);
    /**
     * Android: the launching Intent's ticket extra, removed from the Intent. Non-Shipping elsewhere:
     * -HapbeatSessionTicketFile=<path>. True when a ticket was present.
     */
    bool TakeTicket(FString& OutJson);
    /**
     * Android: getExternalFilesDir(null)/hapbeat-device.json (absent when missing or over 1024 bytes; the Java
     * side logs the latter). Non-Shipping elsewhere: -HapbeatDeviceAddressFile=<path>. True when a file was
     * present; OutSource is its path for the log.
     */
    bool ReadDeviceAddress(FString& OutJson,FString& OutSource);
    /** Whether another runtime can be started from here (Android only). */
    bool CanLaunch();
    /** Starts Target (explicit Intent, NEW_TASK | CLEAR_TASK); an empty TicketJson starts it without a ticket. */
    bool Launch(const FHapbeatDemoSessionComponent& Target,const FString& TicketJson);
    /** Android: whether Package is installed (the manifest declares it in <queries>). False elsewhere. */
    bool IsInstalled(const FString& Package);
    /**
     * Android: acquires (true) or releases (false) the Wi-Fi MulticastLock the Demo Switch receiver holds while
     * bound, so a broadcast DISCOVER is not filtered. True when the lock is now held. No-op returning false elsewhere.
     */
    bool SetMulticastLock(bool bHeld);
    /** finishAndRemoveTask(). */
    void FinishTask();
    /**
     * The device model as the OS reports it, like Unity's SystemInfo.deviceModel: Android "<Build.MANUFACTURER>
     * <Build.MODEL>" (e.g. "Oculus Quest 3"); elsewhere FPlatformMisc::GetDeviceMakeAndModel() ("<vendor> <brand>").
     */
    FString DeviceModel();
}
