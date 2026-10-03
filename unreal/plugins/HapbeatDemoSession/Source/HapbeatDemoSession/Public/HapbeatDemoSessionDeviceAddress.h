#pragma once
#include "CoreMinimal.h"

/**
 * Per-device Hapbeat address file hapbeat-device.json (repos-core/hapbeat-contracts/specs/demo-session.md
 * "端末ごとの Hapbeat 宛先", schemas/demo-device-address.schema.json). Parsing applies the schema's
 * constraints; nothing here reads platform state or touches the SDK.
 */
struct HAPBEATDEMOSESSION_API FHapbeatDeviceAddress
{
    /** An axis the file leaves alone. */
    static constexpr int32 Unchanged=-1;
    /** UTF-8 byte limit of the file. */
    static constexpr int32 MaxJsonBytes=1024;
    /** 1..99 or Unchanged. */
    int32 Player=Unchanged, Group=Unchanged;
    /** {"version":1,"player":<-1|1..99>,"group":<-1|1..99>}, no other fields, at most MaxJsonBytes. */
    static bool Parse(const FString& Json,FHapbeatDeviceAddress& Out,FString& OutError);
    /**
     * The value to hand to UHapbeatSubsystem::SetAddressOverride for one axis. There -1 disables the axis'
     * override, so an Unchanged axis passes the currently effective override instead and stays as it is.
     */
    static int32 ResolveAxis(int32 FileValue,int32 CurrentOverride) {return FileValue==Unchanged?CurrentOverride:FileValue;}
};
