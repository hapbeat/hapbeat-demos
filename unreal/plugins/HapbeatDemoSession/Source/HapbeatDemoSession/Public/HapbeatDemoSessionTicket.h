#pragma once
#include "CoreMinimal.h"

/**
 * Demo Session descriptor and session ticket (repos-core/hapbeat-contracts/specs/demo-session.md,
 * schemas/demo-session-{descriptor,ticket}.schema.json). Parsing applies the schema's constraints;
 * nothing here launches or reads platform state.
 */
struct FHapbeatDemoSessionLabel
{
    FString Ja, En;
};
struct FHapbeatDemoSessionOption
{
    FString Id;
    FHapbeatDemoSessionLabel Label;
    TArray<FString> Values;
    FString Default;
    /** Optional `when`: active only while option WhenOption has one of WhenValues. Empty = always active. */
    FString WhenOption;
    TArray<FString> WhenValues;
};
struct HAPBEATDEMOSESSION_API FHapbeatDemoSessionDescriptor
{
    FString DemoId;
    FHapbeatDemoSessionLabel Title;
    double Minutes=0;
    bool bHapticsToggle=false;
    TArray<FHapbeatDemoSessionOption> Options;
    static bool Parse(const FString& Json,FHapbeatDemoSessionDescriptor& Out,FString& OutError);
};
struct FHapbeatDemoSessionComponent
{
    FString Package, Activity;
};
struct FHapbeatDemoSessionStep
{
    FString DemoId, Title;
    FHapbeatDemoSessionComponent Target;
    /** As written in the ticket (order kept). */
    TArray<TPair<FString,FString>> Options;
    bool bRetry=false;
};
struct HAPBEATDEMOSESSION_API FHapbeatDemoSessionTicket
{
    FString SessionId;
    int32 Index=0;
    bool bHapticsUi=false;
    /** Optional `recenter_ui` (視線をリセット button shown); omitted = false. */
    bool bRecenterUi=false;
    /** Optional `hand_style` ("ghost" / "skin"), empty when omitted. This runtime draws its own hands and only passes it on. */
    FString HandStyle;
    TArray<FHapbeatDemoSessionStep> Steps;
    FHapbeatDemoSessionComponent Finish;
    /** Schema validation only (does not check the demo_id of steps[index]). */
    static bool Parse(const FString& Json,FHapbeatDemoSessionTicket& Out,FString& OutError);
    bool HasNextStep() const {return Index+1<Steps.Num();}
    /** steps[index+1], or finish (the Hub) after the last step. */
    const FHapbeatDemoSessionComponent& NextTarget() const {return HasNextStep()?Steps[Index+1].Target:Finish;}
    /** The ticket handed to the next runtime: index + 1, the current haptics_ui and recenter_ui. Other fields unchanged. */
    FHapbeatDemoSessionTicket MakeNext(bool bInHapticsUi,bool bInRecenterUi) const;
    FString ToJson() const;
};
namespace HapbeatDemoSession
{
    /** Intent String extra that carries the ticket. */
    inline const TCHAR* TicketExtra=TEXT("com.hapbeat.demo_session.ticket");
    /** UTF-8 byte limit of a ticket and of a descriptor. */
    constexpr int32 MaxJsonBytes=16384;
    HAPBEATDEMOSESSION_API bool IsIdentifier(const FString& Value);
    /** Parses the ticket and accepts it only for this runtime: index < len(steps) and steps[index].demo_id == DemoId. */
    HAPBEATDEMOSESSION_API bool AcceptTicket(const FString& Json,const FString& DemoId,FHapbeatDemoSessionTicket& Out,FString& OutError);
    /**
     * Effective option values: every active descriptor option, from the request when valid, else the
     * descriptor default. Unknown options, unknown values, missing and inactive options add a warning.
     */
    HAPBEATDEMOSESSION_API TMap<FString,FString> ResolveOptions(const FHapbeatDemoSessionDescriptor& Descriptor,const TArray<TPair<FString,FString>>& Requested,TArray<FString>* OutWarnings=nullptr);
}
