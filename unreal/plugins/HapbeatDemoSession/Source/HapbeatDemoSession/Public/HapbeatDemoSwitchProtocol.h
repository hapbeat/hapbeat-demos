#pragma once
#include "CoreMinimal.h"

/** Demo Switch v1 subset (repos-core/hapbeat-contracts/specs/demo-switch-control.md). No packet value is ever used as a path or executable. */
struct FHapbeatDemoSwitchMessage
{
    FString Type, ControllerId, DemoId, Action, SceneId, Nonce, Auth;
    int64 Sequence=0;
};
namespace HapbeatDemoSwitchProtocol
{
    HAPBEATDEMOSESSION_API bool Parse(const TArray<uint8>& Bytes,FHapbeatDemoSwitchMessage& Out);
    HAPBEATDEMOSESSION_API bool IsIdentifier(const FString& Value);
    HAPBEATDEMOSESSION_API bool Authenticate(const FHapbeatDemoSwitchMessage& Message,const FString& Secret,bool bAllowUnsigned);
    HAPBEATDEMOSESSION_API FString Here(const FHapbeatDemoSwitchMessage& Message,const FString& CurrentDemoId,const FString& Secret);
    HAPBEATDEMOSESSION_API FString Status(const FHapbeatDemoSwitchMessage& Message,const FString& CurrentDemoId,const FString& Type,const FString& Code,const FString& Secret);
}
