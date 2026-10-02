#include "HapbeatDemoSessionTicket.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "Policies/CondensedJsonPrintPolicy.h"

namespace
{
    using FObject=TSharedPtr<FJsonObject>;
    int32 CodePoints(const FString& S)
    {
        int32 N=0;
        for(int32 I=0;I<S.Len();++I) if(!(S[I]>=0xDC00&&S[I]<=0xDFFF)) ++N; // a surrogate pair counts once
        return N;
    }
    bool OnlyKeys(const FObject& O,std::initializer_list<const TCHAR*> Allowed)
    {
        for(const auto& Pair:O->Values) {
            bool Known=false;
            for(const TCHAR* Name:Allowed) Known|=Pair.Key==Name;
            if(!Known) return false;
        }
        return true;
    }
    bool HasKeys(const FObject& O,std::initializer_list<const TCHAR*> Required)
    {
        for(const TCHAR* Name:Required) if(!O->Values.Contains(Name)) return false;
        return true;
    }
    bool GetString(const FObject& O,const TCHAR* Name,FString& Out)
    {
        const TSharedPtr<FJsonValue>* V=O->Values.Find(Name);
        if(!V||!V->IsValid()||(*V)->Type!=EJson::String) return false;
        Out=(*V)->AsString();return true;
    }
    bool GetBool(const FObject& O,const TCHAR* Name,bool& Out)
    {
        const TSharedPtr<FJsonValue>* V=O->Values.Find(Name);
        if(!V||!V->IsValid()||(*V)->Type!=EJson::Boolean) return false;
        Out=(*V)->AsBool();return true;
    }
    bool GetNumber(const FObject& O,const TCHAR* Name,double& Out)
    {
        const TSharedPtr<FJsonValue>* V=O->Values.Find(Name);
        if(!V||!V->IsValid()||(*V)->Type!=EJson::Number) return false;
        Out=(*V)->AsNumber();return FMath::IsFinite(Out);
    }
    bool GetObject(const FObject& O,const TCHAR* Name,FObject& Out)
    {
        const TSharedPtr<FJsonValue>* V=O->Values.Find(Name);
        if(!V||!V->IsValid()||(*V)->Type!=EJson::Object) return false;
        Out=(*V)->AsObject();return Out.IsValid();
    }
    bool GetArray(const FObject& O,const TCHAR* Name,const TArray<TSharedPtr<FJsonValue>>*& Out)
    {
        const TSharedPtr<FJsonValue>* V=O->Values.Find(Name);
        if(!V||!V->IsValid()||(*V)->Type!=EJson::Array) return false;
        Out=&(*V)->AsArray();return true;
    }
    /** ^[a-z0-9][a-z0-9._-]{0,MaxTail}$ */
    bool IsToken(const FString& V,int32 MaxTail)
    {
        if(V.IsEmpty()||V.Len()>MaxTail+1) return false;
        for(int32 I=0;I<V.Len();++I) {
            const TCHAR C=V[I];
            if((C>='a'&&C<='z')||(C>='0'&&C<='9')) continue;
            if(I>0&&(C=='.'||C=='_'||C=='-')) continue;
            return false;
        }
        return true;
    }
    bool IsValue(const FString& V) {return IsToken(V,31);}
    /** ^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+$, at most 200 characters. */
    bool IsJavaName(const FString& V)
    {
        if(V.IsEmpty()||V.Len()>200) return false;
        int32 Segments=0;bool SegmentStart=true;
        for(int32 I=0;I<V.Len();++I) {
            const TCHAR C=V[I];
            const bool Letter=(C>='a'&&C<='z')||(C>='A'&&C<='Z')||C=='_';
            if(C=='.') {if(SegmentStart) return false;SegmentStart=true;continue;}
            if(SegmentStart) {if(!Letter) return false;++Segments;SegmentStart=false;continue;}
            if(!Letter&&!(C>='0'&&C<='9')) return false;
        }
        return !SegmentStart&&Segments>=2;
    }
    bool WithinBytes(const FString& Json)
    {
        return FTCHARToUTF8(*Json).Length()<=HapbeatDemoSession::MaxJsonBytes;
    }
    bool ParseObject(const FString& Json,FObject& Out)
    {
        return FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Json),Out)&&Out.IsValid();
    }
    bool ParseLabel(const FObject& O,const TCHAR* Name,FHapbeatDemoSessionLabel& Out)
    {
        FObject L;
        if(!GetObject(O,Name,L)||!OnlyKeys(L,{TEXT("ja"),TEXT("en")})||!GetString(L,TEXT("ja"),Out.Ja)) return false;
        if(CodePoints(Out.Ja)<1||CodePoints(Out.Ja)>40) return false;
        if(L->Values.Contains(TEXT("en"))&&(!GetString(L,TEXT("en"),Out.En)||CodePoints(Out.En)<1||CodePoints(Out.En)>60)) return false;
        return true;
    }
    bool ParseComponent(const FObject& O,FHapbeatDemoSessionComponent& Out)
    {
        return OnlyKeys(O,{TEXT("package"),TEXT("activity")})&&GetString(O,TEXT("package"),Out.Package)&&GetString(O,TEXT("activity"),Out.Activity)
            &&IsJavaName(Out.Package)&&IsJavaName(Out.Activity);
    }
    bool Fail(FString& Error,const TCHAR* Why) {Error=Why;return false;}
}

bool HapbeatDemoSession::IsIdentifier(const FString& Value) {return IsToken(Value,63);}

bool FHapbeatDemoSessionDescriptor::Parse(const FString& Json,FHapbeatDemoSessionDescriptor& Out,FString& Error)
{
    if(!WithinBytes(Json)) return Fail(Error,TEXT("descriptor exceeds 16384 bytes"));
    FObject O;
    if(!ParseObject(Json,O)) return Fail(Error,TEXT("descriptor is not a JSON object"));
    if(!OnlyKeys(O,{TEXT("version"),TEXT("demo_id"),TEXT("title"),TEXT("minutes"),TEXT("supports"),TEXT("options")})
        ||!HasKeys(O,{TEXT("version"),TEXT("demo_id"),TEXT("title"),TEXT("options"),TEXT("supports")})) return Fail(Error,TEXT("descriptor fields"));
    FHapbeatDemoSessionDescriptor D;double Version=0;
    if(!GetNumber(O,TEXT("version"),Version)||Version!=1) return Fail(Error,TEXT("descriptor version"));
    if(!GetString(O,TEXT("demo_id"),D.DemoId)||!HapbeatDemoSession::IsIdentifier(D.DemoId)) return Fail(Error,TEXT("descriptor demo_id"));
    if(!ParseLabel(O,TEXT("title"),D.Title)) return Fail(Error,TEXT("descriptor title"));
    if(O->Values.Contains(TEXT("minutes"))&&(!GetNumber(O,TEXT("minutes"),D.Minutes)||D.Minutes<=0||D.Minutes>60)) return Fail(Error,TEXT("descriptor minutes"));
    FObject Supports;
    if(!GetObject(O,TEXT("supports"),Supports)||!OnlyKeys(Supports,{TEXT("haptics_toggle")})||!GetBool(Supports,TEXT("haptics_toggle"),D.bHapticsToggle)) return Fail(Error,TEXT("descriptor supports"));
    const TArray<TSharedPtr<FJsonValue>>* Options=nullptr;
    if(!GetArray(O,TEXT("options"),Options)||Options->Num()>8) return Fail(Error,TEXT("descriptor options"));
    for(const TSharedPtr<FJsonValue>& V:*Options) {
        const FObject Option=V.IsValid()&&V->Type==EJson::Object?V->AsObject():nullptr;
        FHapbeatDemoSessionOption Opt;
        if(!Option.IsValid()||!OnlyKeys(Option,{TEXT("id"),TEXT("label"),TEXT("values"),TEXT("default"),TEXT("when")})
            ||!GetString(Option,TEXT("id"),Opt.Id)||!HapbeatDemoSession::IsIdentifier(Opt.Id)||!ParseLabel(Option,TEXT("label"),Opt.Label)
            ||!GetString(Option,TEXT("default"),Opt.Default)||!IsValue(Opt.Default)) return Fail(Error,TEXT("descriptor option"));
        const TArray<TSharedPtr<FJsonValue>>* Values=nullptr;
        if(!GetArray(Option,TEXT("values"),Values)||Values->Num()<1||Values->Num()>8) return Fail(Error,TEXT("descriptor option values"));
        for(const TSharedPtr<FJsonValue>& Item:*Values) {
            const FObject Entry=Item.IsValid()&&Item->Type==EJson::Object?Item->AsObject():nullptr;
            FString Value;FHapbeatDemoSessionLabel Label;
            if(!Entry.IsValid()||!OnlyKeys(Entry,{TEXT("value"),TEXT("label")})||!GetString(Entry,TEXT("value"),Value)||!IsValue(Value)||!ParseLabel(Entry,TEXT("label"),Label))
                return Fail(Error,TEXT("descriptor option value"));
            Opt.Values.Add(Value);
        }
        if(Option->Values.Contains(TEXT("when"))) {
            FObject When;
            if(!GetObject(Option,TEXT("when"),When)||When->Values.Num()!=1) return Fail(Error,TEXT("descriptor option when"));
            for(const auto& Pair:When->Values) {
                Opt.WhenOption=Pair.Key;
                if(!HapbeatDemoSession::IsIdentifier(Pair.Key)||!Pair.Value.IsValid()||Pair.Value->Type!=EJson::Array||Pair.Value->AsArray().IsEmpty()) return Fail(Error,TEXT("descriptor option when"));
                for(const TSharedPtr<FJsonValue>& W:Pair.Value->AsArray()) {
                    if(!W.IsValid()||W->Type!=EJson::String||!IsValue(W->AsString())) return Fail(Error,TEXT("descriptor option when"));
                    Opt.WhenValues.Add(W->AsString());
                }
            }
        }
        D.Options.Add(MoveTemp(Opt));
    }
    Out=MoveTemp(D);return true;
}

bool FHapbeatDemoSessionTicket::Parse(const FString& Json,FHapbeatDemoSessionTicket& Out,FString& Error)
{
    if(!WithinBytes(Json)) return Fail(Error,TEXT("ticket exceeds 16384 bytes"));
    FObject O;
    if(!ParseObject(Json,O)) return Fail(Error,TEXT("ticket is not a JSON object"));
    if(!OnlyKeys(O,{TEXT("version"),TEXT("session_id"),TEXT("index"),TEXT("haptics_ui"),TEXT("steps"),TEXT("finish")})
        ||!HasKeys(O,{TEXT("version"),TEXT("session_id"),TEXT("index"),TEXT("haptics_ui"),TEXT("steps"),TEXT("finish")})) return Fail(Error,TEXT("ticket fields"));
    FHapbeatDemoSessionTicket T;double Version=0,Index=-1;
    if(!GetNumber(O,TEXT("version"),Version)||Version!=1) return Fail(Error,TEXT("ticket version"));
    if(!GetString(O,TEXT("session_id"),T.SessionId)||T.SessionId.Len()!=16) return Fail(Error,TEXT("ticket session_id"));
    for(TCHAR C:T.SessionId) if(!((C>='0'&&C<='9')||(C>='a'&&C<='f'))) return Fail(Error,TEXT("ticket session_id"));
    if(!GetNumber(O,TEXT("index"),Index)||Index<0||Index>32||FMath::FloorToDouble(Index)!=Index) return Fail(Error,TEXT("ticket index"));
    T.Index=static_cast<int32>(Index);
    if(!GetBool(O,TEXT("haptics_ui"),T.bHapticsUi)) return Fail(Error,TEXT("ticket haptics_ui"));
    FObject Finish;
    if(!GetObject(O,TEXT("finish"),Finish)||!ParseComponent(Finish,T.Finish)) return Fail(Error,TEXT("ticket finish"));
    const TArray<TSharedPtr<FJsonValue>>* Steps=nullptr;
    if(!GetArray(O,TEXT("steps"),Steps)||Steps->Num()<1||Steps->Num()>32) return Fail(Error,TEXT("ticket steps"));
    for(const TSharedPtr<FJsonValue>& V:*Steps) {
        const FObject S=V.IsValid()&&V->Type==EJson::Object?V->AsObject():nullptr;
        FHapbeatDemoSessionStep Step;FObject Options;
        if(!S.IsValid()||!OnlyKeys(S,{TEXT("demo_id"),TEXT("title"),TEXT("package"),TEXT("activity"),TEXT("options"),TEXT("retry")})
            ||!HasKeys(S,{TEXT("demo_id"),TEXT("title"),TEXT("package"),TEXT("activity"),TEXT("options"),TEXT("retry")})) return Fail(Error,TEXT("ticket step fields"));
        if(!GetString(S,TEXT("demo_id"),Step.DemoId)||!HapbeatDemoSession::IsIdentifier(Step.DemoId)) return Fail(Error,TEXT("ticket step demo_id"));
        if(!GetString(S,TEXT("title"),Step.Title)||CodePoints(Step.Title)<1||CodePoints(Step.Title)>40) return Fail(Error,TEXT("ticket step title"));
        if(!GetString(S,TEXT("package"),Step.Target.Package)||!GetString(S,TEXT("activity"),Step.Target.Activity)
            ||!IsJavaName(Step.Target.Package)||!IsJavaName(Step.Target.Activity)) return Fail(Error,TEXT("ticket step component"));
        if(!GetBool(S,TEXT("retry"),Step.bRetry)) return Fail(Error,TEXT("ticket step retry"));
        if(!GetObject(S,TEXT("options"),Options)||Options->Values.Num()>8) return Fail(Error,TEXT("ticket step options"));
        for(const auto& Pair:Options->Values) {
            if(!HapbeatDemoSession::IsIdentifier(Pair.Key)||!Pair.Value.IsValid()||Pair.Value->Type!=EJson::String||!IsValue(Pair.Value->AsString()))
                return Fail(Error,TEXT("ticket step option"));
            Step.Options.Emplace(Pair.Key,Pair.Value->AsString());
        }
        T.Steps.Add(MoveTemp(Step));
    }
    Out=MoveTemp(T);return true;
}

FHapbeatDemoSessionTicket FHapbeatDemoSessionTicket::MakeNext(bool bInHapticsUi) const
{
    FHapbeatDemoSessionTicket Next=*this;
    Next.Index=Index+1;Next.bHapticsUi=bInHapticsUi;
    return Next;
}

FString FHapbeatDemoSessionTicket::ToJson() const
{
    FString Json;
    auto W=TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json);
    auto Component=[&](const FHapbeatDemoSessionComponent& C) {W->WriteValue(TEXT("package"),C.Package);W->WriteValue(TEXT("activity"),C.Activity);};
    W->WriteObjectStart();
    W->WriteValue(TEXT("version"),1);W->WriteValue(TEXT("session_id"),SessionId);W->WriteValue(TEXT("index"),Index);W->WriteValue(TEXT("haptics_ui"),bHapticsUi);
    W->WriteArrayStart(TEXT("steps"));
    for(const FHapbeatDemoSessionStep& S:Steps) {
        W->WriteObjectStart();
        W->WriteValue(TEXT("demo_id"),S.DemoId);W->WriteValue(TEXT("title"),S.Title);Component(S.Target);
        W->WriteObjectStart(TEXT("options"));
        for(const auto& Pair:S.Options) W->WriteValue(Pair.Key,Pair.Value);
        W->WriteObjectEnd();
        W->WriteValue(TEXT("retry"),S.bRetry);
        W->WriteObjectEnd();
    }
    W->WriteArrayEnd();
    W->WriteObjectStart(TEXT("finish"));Component(Finish);W->WriteObjectEnd();
    W->WriteObjectEnd();W->Close();
    return Json;
}

bool HapbeatDemoSession::AcceptTicket(const FString& Json,const FString& DemoId,FHapbeatDemoSessionTicket& Out,FString& Error)
{
    FHapbeatDemoSessionTicket T;
    if(!FHapbeatDemoSessionTicket::Parse(Json,T,Error)) return false;
    // index == len(steps) is the "all steps done" ticket that only the finish runtime (Hub) receives.
    if(T.Index>=T.Steps.Num()) return Fail(Error,TEXT("ticket index is past the last step"));
    if(T.Steps[T.Index].DemoId!=DemoId) {Error=FString::Printf(TEXT("ticket step is for %s, this runtime is %s"),*T.Steps[T.Index].DemoId,*DemoId);return false;}
    Out=MoveTemp(T);return true;
}

TMap<FString,FString> HapbeatDemoSession::ResolveOptions(const FHapbeatDemoSessionDescriptor& D,const TArray<TPair<FString,FString>>& Requested,TArray<FString>* Warnings)
{
    auto Warn=[Warnings](const FString& Text){if(Warnings) Warnings->Add(Text);};
    TMap<FString,FString> Asked;
    for(const auto& Pair:Requested) Asked.Add(Pair.Key,Pair.Value);
    // First every option's value (request or default), then drop those whose `when` does not hold.
    TMap<FString,FString> Value;
    for(const FHapbeatDemoSessionOption& O:D.Options) {
        const FString* V=Asked.Find(O.Id);
        Value.Add(O.Id,V&&O.Values.Contains(*V)?*V:O.Default);
    }
    TMap<FString,FString> Result;
    for(const FHapbeatDemoSessionOption& O:D.Options) {
        const bool Active=O.WhenOption.IsEmpty()||O.WhenValues.Contains(Value.FindRef(O.WhenOption));
        const FString* V=Asked.Find(O.Id);
        if(!Active) {
            if(V) Warn(FString::Printf(TEXT("option %s is inactive here and is ignored"),*O.Id));
            continue;
        }
        if(!V) Warn(FString::Printf(TEXT("option %s missing: default %s"),*O.Id,*O.Default));
        else if(!O.Values.Contains(*V)) Warn(FString::Printf(TEXT("option %s has unknown value %s: default %s"),*O.Id,**V,*O.Default));
        Result.Add(O.Id,Value[O.Id]);
    }
    for(const auto& Pair:Requested)
        if(!D.Options.ContainsByPredicate([&](const FHapbeatDemoSessionOption& O){return O.Id==Pair.Key;}))
            Warn(FString::Printf(TEXT("unknown option %s is ignored"),*Pair.Key));
    return Result;
}
