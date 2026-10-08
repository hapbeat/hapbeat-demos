#include "HapbeatDemoSwitchProtocol.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Policies/CondensedJsonPrintPolicy.h"
THIRD_PARTY_INCLUDES_START
// OpenSSL's unrelated UI typedef conflicts with UE's UHT metadata namespace.
#define UI OPENSSL_UI
#include <openssl/hmac.h>
#include <openssl/crypto.h>
#undef UI
THIRD_PARTY_INCLUDES_END

namespace
{
    bool Hex(const FString& S,int32 Length)
    {
        if(S.Len()!=Length) return false;
        for(TCHAR C:S) if(!((C>='0'&&C<='9')||(C>='a'&&C<='f'))) return false;
        return true;
    }
    FString Field(const TCHAR* Name,const FString& Value)
    {
        const FTCHARToUTF8 Utf8(*Value);
        return FString::Printf(TEXT("%s=%d:%s\n"),Name,Utf8.Length(),*Value);
    }
    FString Signature(const FString& Text,const FString& Secret)
    {
        const FTCHARToUTF8 Key(*Secret),Data(*Text);
        uint8 Digest[EVP_MAX_MD_SIZE];unsigned int Length=0;
        if(!HMAC(EVP_sha256(),Key.Get(),Key.Length(),reinterpret_cast<const uint8*>(Data.Get()),Data.Length(),Digest,&Length) || Length!=32) return FString();
        return BytesToHex(Digest,Length).ToLower();
    }
    FString Header(const TCHAR* Kind) {return FString(TEXT("HAPBEAT-DEMO-SWITCH/1\n"))+Kind+TEXT("\n");}
    /** DISCOVER and QUERY carry a nonce and no sequence. */
    bool IsNonceRequest(const FString& Type) {return Type==TEXT("DISCOVER")||Type==TEXT("QUERY");}
    FString Command(const FHapbeatDemoSwitchMessage& M)
    {
        FString C=Header(IsNonceRequest(M.Type)?*M.Type:TEXT("COMMAND"))
            +Field(TEXT("version"),TEXT("1"))+Field(TEXT("type"),M.Type)+Field(TEXT("controller_id"),M.ControllerId);
        if(IsNonceRequest(M.Type)) return C+Field(TEXT("nonce"),M.Nonce);
        C+=Field(TEXT("seq"),LexToString(M.Sequence))+Field(TEXT("demo_id"),M.DemoId);
        if(M.Type==TEXT("CONTROL")) C+=Field(TEXT("action"),M.Action)+Field(TEXT("scene_id"),M.SceneId);
        return C;
    }
    FString Encode(TSharedRef<FJsonObject> O,const FString& Canonical,const FString& Secret)
    {
        if(!Secret.IsEmpty()) {
            const FString Auth=Signature(Canonical,Secret);
            if(Auth.IsEmpty()) return FString();
            O->SetStringField(TEXT("auth"),Auth);
        }
        FString Json;
        auto Writer=TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json);
        FJsonSerializer::Serialize(O,Writer);
        return Json;
    }
}
bool HapbeatDemoSwitchProtocol::IsIdentifier(const FString& V)
{
    if(V.IsEmpty() || V.Len()>64) return false;
    for(int32 I=0;I<V.Len();++I) {
        const TCHAR C=V[I];
        if((C>='a'&&C<='z')||(C>='0'&&C<='9')) continue;
        if(I>0&&(C=='.'||C=='_'||C=='-')) continue;
        return false;
    }
    return true;
}
FString HapbeatDemoSwitchProtocol::NormalizeDeviceModel(const FString& Value)
{
    FString Out;
    for(int32 I=0,Count=0;I<Value.Len()&&Count<64;++I) {
        const TCHAR C=Value[I];
        if(C<=0x1F||(C>=0x7F&&C<=0x9F)) continue;
        Out.AppendChar(C);
        // A UTF-16 surrogate pair is one code point.
        if(C>=0xD800&&C<=0xDBFF&&I+1<Value.Len()&&Value[I+1]>=0xDC00&&Value[I+1]<=0xDFFF) Out.AppendChar(Value[++I]);
        ++Count;
    }
    return Out;
}
bool HapbeatDemoSwitchProtocol::Parse(const TArray<uint8>& Bytes,FHapbeatDemoSwitchMessage& Out)
{
    if(Bytes.IsEmpty()||Bytes.Num()>1024) return false;
    // Every permitted field value is ASCII. Escaped ASCII is still decoded below.
    // Reject embedded NUL and non-ASCII bytes before creating a terminated string.
    for(uint8 C:Bytes) if((C<32&&C!=9&&C!=10&&C!=13)||C>126) return false;
    const FUTF8ToTCHAR Utf8(reinterpret_cast<const ANSICHAR*>(Bytes.GetData()),Bytes.Num());
    auto Reader=TJsonReaderFactory<>::Create(FString(Utf8.Length(),Utf8.Get()));
    TMap<FString,FString> Fields;
    TSet<FString> Numbers;
    EJsonNotation N;
    if(!Reader->ReadNext(N)||N!=EJsonNotation::ObjectStart) return false;
    bool Ended=false;
    while(Reader->ReadNext(N)) {
        if(N==EJsonNotation::ObjectEnd) {Ended=true;break;}
        const FString Name=Reader->GetIdentifier();
        if(Name.IsEmpty()||Fields.Contains(Name)) return false;
        if(N==EJsonNotation::String) Fields.Add(Name,Reader->GetValueAsString());
        else if(N==EJsonNotation::Number) {
            const FString Raw=Reader->GetValueAsNumberString();
            if(Raw.IsEmpty()||Raw.Len()>16) return false;
            for(TCHAR C:Raw) if(C<'0'||C>'9') return false;
            Fields.Add(Name,Raw);Numbers.Add(Name);
        } else return false;
    }
    if(!Ended || Reader->ReadNext(N) || !Reader->GetErrorMessage().IsEmpty()) return false;
    const FString Type=Fields.FindRef(TEXT("type"));
    TSet<FString> Required={TEXT("version"),TEXT("type"),TEXT("controller_id")};
    if(IsNonceRequest(Type)) Required.Add(TEXT("nonce"));
    else if(Type==TEXT("CONTROL")||Type==TEXT("SWITCH")) {
        Required.Add(TEXT("seq"));Required.Add(TEXT("demo_id"));
        if(Type==TEXT("CONTROL")) {Required.Add(TEXT("action"));Required.Add(TEXT("scene_id"));}
    } else return false;
    for(const auto& Pair:Fields) if(!Required.Contains(Pair.Key)&&Pair.Key!=TEXT("auth")) return false;
    for(const auto& Name:Required) {
        if(!Fields.Contains(Name)) return false;
        if(Numbers.Contains(Name)!=(Name==TEXT("version")||Name==TEXT("seq"))) return false;
    }
    if(Fields.FindRef(TEXT("version"))!=TEXT("1")||!IsIdentifier(Fields.FindRef(TEXT("controller_id")))) return false;
    if(Fields.Contains(TEXT("auth")) && (Numbers.Contains(TEXT("auth"))||!Hex(Fields[TEXT("auth")],64))) return false;
    FHapbeatDemoSwitchMessage M;
    M.Type=Type;M.ControllerId=Fields[TEXT("controller_id")];M.Auth=Fields.FindRef(TEXT("auth"));
    if(IsNonceRequest(Type)) {M.Nonce=Fields[TEXT("nonce")];if(!Hex(M.Nonce,16)) return false;}
    else {
        M.Sequence=FCString::Atoi64(*Fields[TEXT("seq")]);M.DemoId=Fields[TEXT("demo_id")];
        if(M.Sequence<1||M.Sequence>9007199254740991LL||!IsIdentifier(M.DemoId)) return false;
        if(Type==TEXT("CONTROL")) {
            M.Action=Fields[TEXT("action")];M.SceneId=Fields[TEXT("scene_id")];
            if(M.Action==TEXT("scene")) {if(!IsIdentifier(M.SceneId)) return false;}
            else if(!M.SceneId.IsEmpty()) return false;
        }
    }
    Out=MoveTemp(M);return true;
}
bool HapbeatDemoSwitchProtocol::Authenticate(const FHapbeatDemoSwitchMessage& M,const FString& Secret,bool bAllowUnsigned)
{
    if(Secret.IsEmpty()) return bAllowUnsigned && M.Auth.IsEmpty();
    if(!Hex(M.Auth,64)) return false;
    const FString Expected=Signature(Command(M),Secret);
    if(Expected.Len()!=64) return false;
    const FTCHARToUTF8 A(*Expected),B(*M.Auth);
    return CRYPTO_memcmp(A.Get(),B.Get(),64)==0;
}
FString HapbeatDemoSwitchProtocol::Here(const FHapbeatDemoSwitchMessage& M,const FString& Demo,const FString& Secret)
{
    auto O=MakeShared<FJsonObject>();O->SetNumberField(TEXT("version"),1);O->SetStringField(TEXT("type"),TEXT("HERE"));
    O->SetStringField(TEXT("controller_id"),M.ControllerId);O->SetStringField(TEXT("nonce"),M.Nonce);O->SetStringField(TEXT("current_demo_id"),Demo);
    const FString C=Header(TEXT("HERE"))+Field(TEXT("version"),TEXT("1"))+Field(TEXT("type"),TEXT("HERE"))
        +Field(TEXT("controller_id"),M.ControllerId)+Field(TEXT("nonce"),M.Nonce)+Field(TEXT("current_demo_id"),Demo);
    return Encode(O,C,Secret);
}
FString HapbeatDemoSwitchProtocol::State(const FHapbeatDemoSwitchMessage& M,const FString& Demo,const FHapbeatDemoSwitchState& S,const FString& Secret)
{
    auto O=MakeShared<FJsonObject>();O->SetNumberField(TEXT("version"),1);O->SetStringField(TEXT("type"),TEXT("STATE"));
    O->SetStringField(TEXT("controller_id"),M.ControllerId);O->SetStringField(TEXT("nonce"),M.Nonce);O->SetStringField(TEXT("current_demo_id"),Demo);
    O->SetBoolField(TEXT("foreground"),S.bForeground);O->SetBoolField(TEXT("haptics_on"),S.bHapticsOn);O->SetBoolField(TEXT("haptics_ui"),S.bHapticsUi);O->SetBoolField(TEXT("recenter_ui"),S.bRecenterUi);
    O->SetBoolField(TEXT("paused"),S.bPaused);O->SetNumberField(TEXT("step_index"),S.StepIndex);O->SetNumberField(TEXT("step_count"),S.StepCount);
    // Booleans are signed as true / false, integers in base 10.
    auto Bool=[](bool B){return FString(B?TEXT("true"):TEXT("false"));};
    FString C=Header(TEXT("STATE"))+Field(TEXT("version"),TEXT("1"))+Field(TEXT("type"),TEXT("STATE"))
        +Field(TEXT("controller_id"),M.ControllerId)+Field(TEXT("nonce"),M.Nonce)+Field(TEXT("current_demo_id"),Demo)
        +Field(TEXT("foreground"),Bool(S.bForeground))+Field(TEXT("haptics_on"),Bool(S.bHapticsOn))+Field(TEXT("haptics_ui"),Bool(S.bHapticsUi))+Field(TEXT("recenter_ui"),Bool(S.bRecenterUi))
        +Field(TEXT("paused"),Bool(S.bPaused))+Field(TEXT("step_index"),LexToString(S.StepIndex))+Field(TEXT("step_count"),LexToString(S.StepCount));
    // The optional fields follow in the contract's order, each signed only when present.
    auto Optional=[&](const TCHAR* Name,const FString& Value){if(!Value.IsEmpty()) {O->SetStringField(Name,Value);C+=Field(Name,Value);}};
    Optional(TEXT("device_model"),S.DeviceModel);
    if(S.bEditor.IsSet()) {O->SetBoolField(TEXT("editor"),S.bEditor.GetValue());C+=Field(TEXT("editor"),Bool(S.bEditor.GetValue()));}
    Optional(TEXT("screen"),S.Screen);
    Optional(TEXT("hand_style"),S.HandStyle);
    return Encode(O,C,Secret);
}
FString HapbeatDemoSwitchProtocol::Status(const FHapbeatDemoSwitchMessage& M,const FString& Demo,const FString& Type,const FString& Code,const FString& Secret,const FString& Text)
{
    auto O=MakeShared<FJsonObject>();O->SetNumberField(TEXT("version"),1);O->SetStringField(TEXT("type"),Type);
    O->SetStringField(TEXT("controller_id"),M.ControllerId);O->SetNumberField(TEXT("seq"),static_cast<double>(M.Sequence));
    O->SetStringField(TEXT("demo_id"),M.DemoId);O->SetStringField(TEXT("current_demo_id"),Demo);
    O->SetStringField(TEXT("code"),Code);O->SetStringField(TEXT("message"),Text);
    const FString C=Header(TEXT("STATUS"))+Field(TEXT("version"),TEXT("1"))+Field(TEXT("type"),Type)+Field(TEXT("controller_id"),M.ControllerId)
        +Field(TEXT("seq"),LexToString(M.Sequence))+Field(TEXT("demo_id"),M.DemoId)+Field(TEXT("current_demo_id"),Demo)+Field(TEXT("code"),Code)+Field(TEXT("message"),Text);
    return Encode(O,C,Secret);
}
