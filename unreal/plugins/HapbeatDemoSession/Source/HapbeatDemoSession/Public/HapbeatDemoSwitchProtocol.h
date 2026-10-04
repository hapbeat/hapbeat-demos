#pragma once
#include "CoreMinimal.h"

/** Demo Switch v1 subset (repos-core/hapbeat-contracts/specs/demo-switch-control.md). No packet value is ever used as a path or executable. */
struct FHapbeatDemoSwitchMessage
{
    FString Type, ControllerId, DemoId, Action, SceneId, Nonce, Auth;
    int64 Sequence=0;
};
/** What a STATE answers to a QUERY (specs/demo-switch-control.md, State query). */
struct FHapbeatDemoSwitchState
{
    bool bHapticsOn=true, bHapticsUi=false, bRecenterUi=false, bPaused=false;
    /** Demo Session step (0-based) and step count; -1 / 0 outside a session. */
    int32 StepIndex=-1, StepCount=0;
};
namespace HapbeatDemoSwitchProtocol
{
    HAPBEATDEMOSESSION_API bool Parse(const TArray<uint8>& Bytes,FHapbeatDemoSwitchMessage& Out);
    HAPBEATDEMOSESSION_API bool IsIdentifier(const FString& Value);
    HAPBEATDEMOSESSION_API bool Authenticate(const FHapbeatDemoSwitchMessage& Message,const FString& Secret,bool bAllowUnsigned);
    HAPBEATDEMOSESSION_API FString Here(const FHapbeatDemoSwitchMessage& Message,const FString& CurrentDemoId,const FString& Secret);
    /** STATE answering a QUERY (same controller_id and nonce). */
    HAPBEATDEMOSESSION_API FString State(const FHapbeatDemoSwitchMessage& Message,const FString& CurrentDemoId,const FHapbeatDemoSwitchState& State,const FString& Secret);
    HAPBEATDEMOSESSION_API FString Status(const FHapbeatDemoSwitchMessage& Message,const FString& CurrentDemoId,const FString& Type,const FString& Code,const FString& Secret);
}
