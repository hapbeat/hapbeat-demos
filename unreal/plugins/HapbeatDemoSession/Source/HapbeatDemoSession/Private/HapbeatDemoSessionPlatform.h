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
    /** Whether another runtime can be started from here (Android only). */
    bool CanLaunch();
    bool Launch(const FHapbeatDemoSessionComponent& Target,const FString& TicketJson);
    /** finishAndRemoveTask(). */
    void FinishTask();
}
