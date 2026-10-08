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
    /** False while the runtime is not the focused foreground app (SWITCH / CONTROL are then refused). */
    bool bForeground=true;
    bool bHapticsOn=true, bHapticsUi=false, bRecenterUi=false, bPaused=false;
    /** Demo Session step (0-based) and step count; -1 / 0 outside a session. */
    int32 StepIndex=-1, StepCount=0;
    // Optional fields, sent and signed in this order only when present (a controller treats a missing one as unknown).
    /** The OS device model (NormalizeDeviceModel); empty = absent. */
    FString DeviceModel;
    /** Whether this runs inside the editor (Play In Editor); unset = absent. */
    TOptional<bool> bEditor;
    /** "main" or "completion" ("manage" is the Hub's); empty = absent. */
    FString Screen;
    /** "ghost" / "skin", the look of the shared hands; empty = absent (a runtime that does not draw them). */
    FString HandStyle;
};
namespace HapbeatDemoSwitchProtocol
{
    HAPBEATDEMOSESSION_API bool Parse(const TArray<uint8>& Bytes,FHapbeatDemoSwitchMessage& Out);
    HAPBEATDEMOSESSION_API bool IsIdentifier(const FString& Value);
    /** STATE device_model: control characters (U+0000–U+001F, U+007F–U+009F) removed, cut to 64 code points; empty = leave it out. */
    HAPBEATDEMOSESSION_API FString NormalizeDeviceModel(const FString& Value);
    HAPBEATDEMOSESSION_API bool Authenticate(const FHapbeatDemoSwitchMessage& Message,const FString& Secret,bool bAllowUnsigned);
    HAPBEATDEMOSESSION_API FString Here(const FHapbeatDemoSwitchMessage& Message,const FString& CurrentDemoId,const FString& Secret);
    /** STATE answering a QUERY (same controller_id and nonce). */
    HAPBEATDEMOSESSION_API FString State(const FHapbeatDemoSwitchMessage& Message,const FString& CurrentDemoId,const FHapbeatDemoSwitchState& State,const FString& Secret);
    /** ACK / READY / FAILED; Text is the diagnostic message (at most 256 UTF-8 bytes). */
    HAPBEATDEMOSESSION_API FString Status(const FHapbeatDemoSwitchMessage& Message,const FString& CurrentDemoId,const FString& Type,const FString& Code,const FString& Secret,const FString& Text=FString());
}
