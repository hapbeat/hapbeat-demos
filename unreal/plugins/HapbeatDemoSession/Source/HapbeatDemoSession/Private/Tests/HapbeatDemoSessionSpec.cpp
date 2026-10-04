#if WITH_DEV_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"
#include "Misc/ScopeExit.h"
#include "Engine/World.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "HapbeatDemoSessionTicket.h"
#include "HapbeatDemoSwitchProtocol.h"
#include "HapbeatDemoSessionUi.h"
#include "HapbeatDemoSessionPanelAnchor.h"
#include "HapbeatDemoSessionPause.h"
#include "HapbeatDemoSessionHandoff.h"
#include "HapbeatDemoSessionSubsystem.h"
#include "HeadMountedDisplayTypes.h"
#include "GameFramework/Actor.h"
#include "HapbeatDemoSessionDeviceAddress.h"
#include "HapbeatConfig.h"
#include "HapbeatSubsystem.h"
#include "Engine/GameInstance.h"

namespace HapbeatDemoSessionSpecData
{
    // repos-core/hapbeat-contracts/fixtures/sample-demo-session.json (contracts 042d295).
    const TCHAR* Volley=TEXT(R"({"version":1,"demo_id":"volley","title":{"ja":"バレーボール","en":"Volleyball"},"minutes":3,"supports":{"haptics_toggle":true},
 "options":[{"id":"scene","label":{"ja":"モード","en":"Mode"},"default":"block","values":[{"value":"block","label":{"ja":"ブロック","en":"Block"}},{"value":"receive","label":{"ja":"レシーブ","en":"Receive"}}]},
  {"id":"points","label":{"ja":"点数","en":"Points"},"default":"7","when":{"scene":["block"]},"values":[{"value":"3","label":{"ja":"3点先取"}},{"value":"5","label":{"ja":"5点先取"}},{"value":"7","label":{"ja":"7点先取"}}]},
  {"id":"balls","label":{"ja":"球数","en":"Balls"},"default":"10","when":{"scene":["receive"]},"values":[{"value":"10","label":{"ja":"10球"}},{"value":"20","label":{"ja":"20球"}}]}]})");
    const TCHAR* Trex=TEXT(R"({"version":1,"demo_id":"trex-encounter","title":{"ja":"T-Rex エンカウンター","en":"T-Rex Encounter"},"minutes":3,"supports":{"haptics_toggle":true},"options":[]})");
    const TCHAR* Ticket=TEXT(R"({"version":1,"session_id":"0f3a9c2e7b1d4a56","index":1,"haptics_ui":false,
 "steps":[{"demo_id":"volley","title":"バレー ブロック 3点","package":"jp.hapbeat.volley","activity":"com.unity3d.player.UnityPlayerGameActivity","options":{"scene":"block","points":"3"},"retry":true},
  {"demo_id":"trex-encounter","title":"T-Rex","package":"com.hapbeat.trexencounter","activity":"com.epicgames.unreal.GameActivity","options":{},"retry":false}],
 "finish":{"package":"jp.hapbeat.demohub","activity":"com.unity3d.player.UnityPlayerGameActivity"}})");
    FString With(const FString& From,const FString& To) {return FString(Ticket).Replace(*From,*To);}
    /** World point of a widget pixel on a panel placed at At (+X toward the user), Depth cm in front of its face. */
    FVector PanelPoint(const FTransform& At,const FVector2D& Size,const FVector2D& Pixel,float Depth)
    {
        const float Cm=AHapbeatDemoSessionUi::PanelCmPerPixel;
        return At.GetLocation()+At.GetRotation().RotateVector(FVector(Depth,-(Pixel.X-Size.X*.5f)*Cm,-(Pixel.Y-Size.Y*.5f)*Cm));
    }
    TArray<uint8> Bytes(const FString& Json) {const FTCHARToUTF8 U(*Json);return TArray<uint8>(reinterpret_cast<const uint8*>(U.Get()),U.Length());}
    TSharedPtr<FJsonObject> Object(const FString& Json) {TSharedPtr<FJsonObject> O;FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Json),O);return O;}
}

BEGIN_DEFINE_SPEC(FHapbeatDemoSessionSpec,"HapbeatDemoSession",EAutomationTestFlags::EditorContext|EAutomationTestFlags::EngineFilter)
END_DEFINE_SPEC(FHapbeatDemoSessionSpec)

void FHapbeatDemoSessionSpec::Define()
{
    using namespace HapbeatDemoSessionSpecData;
    Describe(TEXT("Ticket"),[this]()
    {
        It(TEXT("accepts the contract fixture for the step it names"),[this]()
        {
            FHapbeatDemoSessionTicket T;FString Error;
            TestTrue(TEXT("accepted for trex-encounter"),HapbeatDemoSession::AcceptTicket(Ticket,TEXT("trex-encounter"),T,Error));
            TestEqual(TEXT("index"),T.Index,1);TestEqual(TEXT("steps"),T.Steps.Num(),2);
            TestFalse(TEXT("retry"),T.Steps[1].bRetry);
            TestEqual(TEXT("options of step 0 kept in order"),T.Steps[0].Options.Num(),2);
        });
        It(TEXT("rejects a ticket whose step is another demo"),[this]()
        {
            FHapbeatDemoSessionTicket T;FString Error;
            TestFalse(TEXT("volley is not steps[1]"),HapbeatDemoSession::AcceptTicket(Ticket,TEXT("volley"),T,Error));
            TestFalse(TEXT("unknown demo"),HapbeatDemoSession::AcceptTicket(Ticket,TEXT("safety-mill"),T,Error));
        });
        It(TEXT("hands the last step over to finish with index + 1 and the current haptics_ui"),[this]()
        {
            FHapbeatDemoSessionTicket T;FString Error;
            TestTrue(TEXT("accepted"),HapbeatDemoSession::AcceptTicket(Ticket,TEXT("trex-encounter"),T,Error));
            TestFalse(TEXT("last step"),T.HasNextStep());
            TestEqual(TEXT("next target is finish"),T.NextTarget().Package,FString(TEXT("jp.hapbeat.demohub")));
            const FString Json=T.MakeNext(true,false).ToJson();
            FHapbeatDemoSessionTicket Next;
            TestTrue(TEXT("next ticket is schema-valid"),FHapbeatDemoSessionTicket::Parse(Json,Next,Error));
            TestEqual(TEXT("index advanced"),Next.Index,2);
            TestTrue(TEXT("haptics_ui carried"),Next.bHapticsUi);
            TestEqual(TEXT("session kept"),Next.SessionId,T.SessionId);
            TestEqual(TEXT("steps kept"),Next.Steps.Num(),2);
            TestEqual(TEXT("options kept"),Next.Steps[0].Options[1].Value,FString(TEXT("3")));
            TestEqual(TEXT("title kept"),Next.Steps[0].Title,T.Steps[0].Title);
            TestFalse(TEXT("index == len(steps) is only for the finish runtime"),HapbeatDemoSession::AcceptTicket(Json,TEXT("trex-encounter"),Next,Error));
        });
        It(TEXT("targets the next step from a middle step"),[this]()
        {
            FHapbeatDemoSessionTicket T;FString Error;
            TestTrue(TEXT("accepted"),HapbeatDemoSession::AcceptTicket(With(TEXT("\"index\":1"),TEXT("\"index\":0")),TEXT("volley"),T,Error));
            TestTrue(TEXT("has next"),T.HasNextStep());
            TestEqual(TEXT("next package"),T.NextTarget().Package,FString(TEXT("com.hapbeat.trexencounter")));
            FHapbeatDemoSessionTicket Next;
            TestTrue(TEXT("next parses"),FHapbeatDemoSessionTicket::Parse(T.MakeNext(false,false).ToJson(),Next,Error));
            TestTrue(TEXT("next is accepted by the next demo"),HapbeatDemoSession::AcceptTicket(T.MakeNext(false,false).ToJson(),TEXT("trex-encounter"),Next,Error));
            TestFalse(TEXT("haptics_ui false"),Next.bHapticsUi);
            TestFalse(TEXT("recenter_ui omitted = false"),Next.bRecenterUi);
            TestFalse(TEXT("recenter_ui false is not written"),T.MakeNext(false,false).ToJson().Contains(TEXT("recenter_ui")));
        });
        It(TEXT("reads the optional recenter_ui and hand_style and hands them on"),[this]()
        {
            FHapbeatDemoSessionTicket T;FString Error;
            const FString Json=With(TEXT("\"haptics_ui\":false,"),TEXT("\"haptics_ui\":false,\"hand_style\":\"skin\",\"recenter_ui\":true,"));
            TestTrue(TEXT("accepted"),HapbeatDemoSession::AcceptTicket(Json,TEXT("trex-encounter"),T,Error));
            TestTrue(TEXT("recenter_ui"),T.bRecenterUi);TestEqual(TEXT("hand_style"),T.HandStyle,FString(TEXT("skin")));
            FHapbeatDemoSessionTicket Next;
            TestTrue(TEXT("next parses"),FHapbeatDemoSessionTicket::Parse(T.MakeNext(false,true).ToJson(),Next,Error));
            TestTrue(TEXT("recenter_ui carried"),Next.bRecenterUi);TestEqual(TEXT("hand_style kept"),Next.HandStyle,FString(TEXT("skin")));
            TestTrue(TEXT("hidden on the way"),FHapbeatDemoSessionTicket::Parse(T.MakeNext(false,false).ToJson(),Next,Error)&&!Next.bRecenterUi);
            TestFalse(TEXT("recenter_ui must be a boolean"),FHapbeatDemoSessionTicket::Parse(With(TEXT("\"haptics_ui\":false,"),TEXT("\"haptics_ui\":false,\"recenter_ui\":1,")),T,Error));
            TestFalse(TEXT("hand_style ghost or skin"),FHapbeatDemoSessionTicket::Parse(With(TEXT("\"haptics_ui\":false,"),TEXT("\"haptics_ui\":false,\"hand_style\":\"robot\",")),T,Error));
        });
        It(TEXT("rejects tickets that break the schema"),[this]()
        {
            const TArray<FString> Bad={
                TEXT("not json"),TEXT("[]"),
                With(TEXT("\"version\":1,"),TEXT("\"version\":2,")),
                With(TEXT("\"version\":1,"),TEXT("")),
                With(TEXT("\"version\":1,"),TEXT("\"version\":1,\"extra\":true,")),
                With(TEXT("0f3a9c2e7b1d4a56"),TEXT("0F3A9C2E7B1D4A56")),
                With(TEXT("0f3a9c2e7b1d4a56"),TEXT("0f3a9c2e7b1d4a5")),
                With(TEXT("\"index\":1"),TEXT("\"index\":33")),
                With(TEXT("\"index\":1"),TEXT("\"index\":-1")),
                With(TEXT("\"index\":1"),TEXT("\"index\":0.5")),
                With(TEXT("\"index\":1"),TEXT("\"index\":\"1\"")),
                With(TEXT("\"haptics_ui\":false"),TEXT("\"haptics_ui\":0")),
                With(TEXT("jp.hapbeat.volley"),TEXT("volley")),
                With(TEXT("jp.hapbeat.volley"),TEXT("jp..volley")),
                With(TEXT("jp.hapbeat.volley"),TEXT("jp.hapbeat.1volley")),
                With(TEXT("\"points\":\"3\""),TEXT("\"points\":\"Three\"")),
                With(TEXT("\"points\":\"3\""),TEXT("\"points\":3")),
                With(TEXT("\"retry\":true"),TEXT("\"retry\":\"yes\"")),
                With(TEXT("\"title\":\"T-Rex\""),TEXT("\"title\":\"\"")),
                With(TEXT("\"title\":\"T-Rex\""),FString::Printf(TEXT("\"title\":\"%s\""),*FString::ChrN(41,TEXT('a')))),
                With(TEXT("\"retry\":false}"),TEXT("\"retry\":false,\"note\":\"x\"}")),
                With(TEXT("\"finish\":{\"package\":\"jp.hapbeat.demohub\","),TEXT("\"finish\":{")),
                With(TEXT("\"steps\":["),TEXT("\"steps\":[],\"x\":[")),
            };
            for(int32 I=0;I<Bad.Num();++I) {
                FHapbeatDemoSessionTicket T;FString Error;
                TestFalse(FString::Printf(TEXT("bad ticket %d rejected"),I),FHapbeatDemoSessionTicket::Parse(Bad[I],T,Error));
            }
            FHapbeatDemoSessionTicket T;FString Error;
            TestTrue(TEXT("40-character title accepted"),FHapbeatDemoSessionTicket::Parse(With(TEXT("\"title\":\"T-Rex\""),FString::Printf(TEXT("\"title\":\"%s\""),*FString::ChrN(40,TEXT('あ')))),T,Error));
            const FString Padded=FString(Ticket).Replace(TEXT("{\"version\""),*(TEXT("{")+FString::ChrN(16384,TEXT(' '))+TEXT("\"version\"")));
            TestFalse(TEXT("over 16384 bytes rejected"),FHapbeatDemoSessionTicket::Parse(Padded,T,Error));
            const FString Small=FString(Ticket).Replace(TEXT("{\"version\""),*(TEXT("{")+FString::ChrN(1000,TEXT(' '))+TEXT("\"version\"")));
            TestTrue(TEXT("whitespace under the limit accepted"),FHapbeatDemoSessionTicket::Parse(Small,T,Error));
        });
    });
    Describe(TEXT("DeviceAddress"),[this]()
    {
        It(TEXT("parses what install-demos.ps1 writes"),[this]()
        {
            FHapbeatDeviceAddress A;FString Error;
            TestTrue(TEXT("player and group"),FHapbeatDeviceAddress::Parse(TEXT(R"({"version":1,"player":2,"group":7})"),A,Error));
            TestEqual(TEXT("player"),A.Player,2);TestEqual(TEXT("group"),A.Group,7);
            TestTrue(TEXT("bounds and -1"),FHapbeatDeviceAddress::Parse(TEXT(R"({"group":99,"player":-1,"version":1})"),A,Error));
            TestEqual(TEXT("player unchanged"),A.Player,FHapbeatDeviceAddress::Unchanged);TestEqual(TEXT("group 99"),A.Group,99);
            TestTrue(TEXT("both -1"),FHapbeatDeviceAddress::Parse(TEXT(R"({"version":1,"player":-1,"group":-1})"),A,Error));
            TestTrue(TEXT("1"),FHapbeatDeviceAddress::Parse(TEXT(R"({"version":1,"player":1,"group":1})"),A,Error));
        });
        It(TEXT("rejects anything outside the schema and keeps the output untouched"),[this]()
        {
            const TCHAR* Bad[]={
                TEXT(""),TEXT("not json"),TEXT("[1,2]"),TEXT(R"({"version":1,"player":2})"),TEXT(R"({"version":1,"group":2})"),
                TEXT(R"({"player":2,"group":2})"),TEXT(R"({"version":2,"player":2,"group":2})"),TEXT(R"({"version":"1","player":2,"group":2})"),
                TEXT(R"({"version":1,"player":0,"group":2})"),TEXT(R"({"version":1,"player":100,"group":2})"),TEXT(R"({"version":1,"player":2,"group":-2})"),
                TEXT(R"({"version":1,"player":2.5,"group":2})"),TEXT(R"({"version":1,"player":"2","group":2})"),TEXT(R"({"version":1,"player":null,"group":2})"),
                TEXT(R"({"version":1,"player":2,"group":2,"extra":1})"),TEXT(R"({"version":1,"player":2,"group":1e9})")};
            for(const TCHAR* Json:Bad) {
                FHapbeatDeviceAddress A;A.Player=5;A.Group=6;FString Error;
                TestFalse(FString::Printf(TEXT("rejects %s"),Json),FHapbeatDeviceAddress::Parse(Json,A,Error));
                TestTrue(FString::Printf(TEXT("untouched by %s"),Json),A.Player==5&&A.Group==6);
            }
            FHapbeatDeviceAddress A;FString Error;
            const FString Padded=FString(TEXT(R"({"version":1,"player":2,"group":2)"))+FString::ChrN(1024,' ')+TEXT("}");
            TestFalse(TEXT("over 1024 bytes"),FHapbeatDeviceAddress::Parse(Padded,A,Error));
        });
        It(TEXT("leaves a -1 axis at the SDK's current override and sets the other"),[this]()
        {
            const UHapbeatConfig* Config=GetDefault<UHapbeatConfig>();
            if(UHapbeatSubsystem::NormalizeAddressOverride(Config->ForcedOverridePlayer)>=1||UHapbeatSubsystem::NormalizeAddressOverride(Config->ForcedOverrideGroup)>=1) {
                AddInfo(TEXT("this project pins an address axis; the SDK keeps it, nothing to check here"));return;
            }
            UHapbeatSubsystem* Hapbeat=NewObject<UHapbeatSubsystem>(NewObject<UGameInstance>());
            Hapbeat->SetAddressOverride(3,5,false);
            FHapbeatDeviceAddress A;FString Error;
            TestTrue(TEXT("parsed"),FHapbeatDeviceAddress::Parse(TEXT(R"({"version":1,"player":-1,"group":2})"),A,Error));
            Hapbeat->SetAddressOverride(FHapbeatDeviceAddress::ResolveAxis(A.Player,Hapbeat->GetOverridePlayer()),FHapbeatDeviceAddress::ResolveAxis(A.Group,Hapbeat->GetOverrideGroup()),false);
            TestEqual(TEXT("player kept"),Hapbeat->GetOverridePlayer(),3);TestEqual(TEXT("group set"),Hapbeat->GetOverrideGroup(),2);
            TestTrue(TEXT("parsed"),FHapbeatDeviceAddress::Parse(TEXT(R"({"version":1,"player":-1,"group":-1})"),A,Error));
            Hapbeat->SetAddressOverride(FHapbeatDeviceAddress::ResolveAxis(A.Player,Hapbeat->GetOverridePlayer()),FHapbeatDeviceAddress::ResolveAxis(A.Group,Hapbeat->GetOverrideGroup()),false);
            TestEqual(TEXT("both kept: player"),Hapbeat->GetOverridePlayer(),3);TestEqual(TEXT("both kept: group"),Hapbeat->GetOverrideGroup(),2);
            // Why ResolveAxis exists: -1 handed to the SDK directly clears that axis.
            Hapbeat->SetAddressOverride(UHapbeatSubsystem::AddressOverrideDisabled,2,false);
            TestEqual(TEXT("-1 clears the axis in the SDK"),Hapbeat->GetOverridePlayer(),int32(UHapbeatSubsystem::AddressOverrideDisabled));
            TestEqual(TEXT("ResolveAxis keeps a value"),FHapbeatDeviceAddress::ResolveAxis(7,3),7);
        });
    });
    Describe(TEXT("Descriptor"),[this]()
    {
        It(TEXT("parses the contract fixtures"),[this]()
        {
            FHapbeatDemoSessionDescriptor D;FString Error;
            TestTrue(TEXT("trex"),FHapbeatDemoSessionDescriptor::Parse(Trex,D,Error));
            TestEqual(TEXT("trex id"),D.DemoId,FString(TEXT("trex-encounter")));TestTrue(TEXT("toggle"),D.bHapticsToggle);
            TestTrue(TEXT("volley"),FHapbeatDemoSessionDescriptor::Parse(Volley,D,Error));
            TestEqual(TEXT("volley options"),D.Options.Num(),3);
            TestEqual(TEXT("when"),D.Options[1].WhenOption,FString(TEXT("scene")));
        });
        It(TEXT("rejects invalid descriptors"),[this]()
        {
            FHapbeatDemoSessionDescriptor D;FString Error;
            TestFalse(TEXT("bad demo_id"),FHapbeatDemoSessionDescriptor::Parse(FString(Trex).Replace(TEXT("trex-encounter"),TEXT("T-Rex")),D,Error));
            TestFalse(TEXT("unknown field"),FHapbeatDemoSessionDescriptor::Parse(FString(Trex).Replace(TEXT("\"minutes\""),TEXT("\"package\":\"a.b\",\"minutes\"")),D,Error));
            TestFalse(TEXT("missing supports"),FHapbeatDemoSessionDescriptor::Parse(FString(Trex).Replace(TEXT("\"supports\":{\"haptics_toggle\":true},"),TEXT("")),D,Error));
            TestFalse(TEXT("minutes 0"),FHapbeatDemoSessionDescriptor::Parse(FString(Trex).Replace(TEXT("\"minutes\":3"),TEXT("\"minutes\":0")),D,Error));
        });
    });
    Describe(TEXT("Options"),[this]()
    {
        auto Resolve=[](TArray<TPair<FString,FString>> Requested,TArray<FString>& Warnings)
        {
            FHapbeatDemoSessionDescriptor D;FString Error;
            FHapbeatDemoSessionDescriptor::Parse(Volley,D,Error);
            return HapbeatDemoSession::ResolveOptions(D,Requested,&Warnings);
        };
        It(TEXT("keeps valid values of active options"),[this,Resolve]()
        {
            TArray<FString> W;const auto R=Resolve({{TEXT("scene"),TEXT("block")},{TEXT("points"),TEXT("3")}},W);
            TestEqual(TEXT("two active"),R.Num(),2);TestEqual(TEXT("points"),R.FindRef(TEXT("points")),FString(TEXT("3")));
            TestEqual(TEXT("no warnings"),W.Num(),0);
        });
        It(TEXT("replaces unknown values with the default and warns"),[this,Resolve]()
        {
            TArray<FString> W;const auto R=Resolve({{TEXT("scene"),TEXT("block")},{TEXT("points"),TEXT("9")}},W);
            TestEqual(TEXT("default points"),R.FindRef(TEXT("points")),FString(TEXT("7")));TestEqual(TEXT("one warning"),W.Num(),1);
        });
        It(TEXT("follows when and ignores unknown or inactive options"),[this,Resolve]()
        {
            TArray<FString> W;const auto R=Resolve({{TEXT("scene"),TEXT("receive")},{TEXT("points"),TEXT("3")},{TEXT("speed"),TEXT("fast")}},W);
            TestFalse(TEXT("points inactive"),R.Contains(TEXT("points")));
            TestEqual(TEXT("balls default"),R.FindRef(TEXT("balls")),FString(TEXT("10")));
            TestEqual(TEXT("inactive + missing + unknown warnings"),W.Num(),3);
            TArray<FString> W2;const auto R2=Resolve({{TEXT("scene"),TEXT("dance")}},W2);
            TestEqual(TEXT("unknown scene -> default block"),R2.FindRef(TEXT("scene")),FString(TEXT("block")));
            TestEqual(TEXT("block's points default"),R2.FindRef(TEXT("points")),FString(TEXT("7")));
        });
    });
    Describe(TEXT("DemoSwitchProtocol"),[this]()
    {
        const FString Secret=TEXT("unit-test-secret");
        // HMAC-SHA256(key "unit-test-secret") of the canonical COMMAND bytes (computed with Python's hmac).
        const FString HapticsOn=TEXT(R"({"version":1,"type":"CONTROL","controller_id":"m5-main","seq":43,"demo_id":"trex-encounter","action":"haptics_on","scene_id":"","auth":"841bb222d35a7ae01a34f98f48f805bccad510b80a7ea7207a4124b04a23ce6c"})");
        const FString Switch=TEXT(R"({"version":1,"type":"SWITCH","controller_id":"m5-main","seq":42,"demo_id":"gloveball","auth":"8b807970128a39666df7154645092486b43e9880c311baadf354fe69cc904327"})");
        It(TEXT("authenticates signed CONTROL and SWITCH"),[this,Secret,HapticsOn,Switch]()
        {
            FHapbeatDemoSwitchMessage M;
            TestTrue(TEXT("CONTROL parses"),HapbeatDemoSwitchProtocol::Parse(Bytes(HapticsOn),M));
            TestEqual(TEXT("action"),M.Action,FString(TEXT("haptics_on")));TestEqual(TEXT("seq"),M.Sequence,int64(43));
            TestTrue(TEXT("CONTROL signature"),HapbeatDemoSwitchProtocol::Authenticate(M,Secret,false));
            TestFalse(TEXT("wrong key"),HapbeatDemoSwitchProtocol::Authenticate(M,TEXT("other"),false));
            TestFalse(TEXT("signed packet in unsigned mode"),HapbeatDemoSwitchProtocol::Authenticate(M,FString(),true));
            M.Action=TEXT("haptics_off");
            TestFalse(TEXT("action is signed"),HapbeatDemoSwitchProtocol::Authenticate(M,Secret,false));
            TestTrue(TEXT("SWITCH parses"),HapbeatDemoSwitchProtocol::Parse(Bytes(Switch),M));
            TestTrue(TEXT("SWITCH signature"),HapbeatDemoSwitchProtocol::Authenticate(M,Secret,false));
        });
        It(TEXT("accepts unsigned only when allowed"),[this]()
        {
            FHapbeatDemoSwitchMessage M;
            TestTrue(TEXT("DISCOVER parses"),HapbeatDemoSwitchProtocol::Parse(Bytes(TEXT(R"({"version":1,"type":"DISCOVER","controller_id":"m5-main","nonce":"0123456789abcdef"})")),M));
            TestTrue(TEXT("isolated unsigned"),HapbeatDemoSwitchProtocol::Authenticate(M,FString(),true));
            TestFalse(TEXT("unsigned not allowed"),HapbeatDemoSwitchProtocol::Authenticate(M,FString(),false));
            TestFalse(TEXT("secret set, no auth"),HapbeatDemoSwitchProtocol::Authenticate(M,TEXT("k"),true));
        });
        It(TEXT("rejects malformed packets"),[this,HapticsOn]()
        {
            const TArray<FString> Bad={
                HapticsOn.Replace(TEXT(",\"auth\""),TEXT(",\"extra\":1,\"auth\"")),
                HapticsOn.Replace(TEXT(",\"auth\""),TEXT(",\"seq\":44,\"auth\"")),
                HapticsOn.Replace(TEXT("\"version\":1"),TEXT("\"version\":1.0")),
                HapticsOn.Replace(TEXT("\"scene_id\":\"\""),TEXT("\"scene_id\":\"block\"")),
                HapticsOn.Replace(TEXT("\"scene_id\":\"\""),TEXT("\"scene_id\":null")),
                HapticsOn.Replace(TEXT("\"seq\":43"),TEXT("\"seq\":0")),
                HapticsOn.Replace(TEXT("m5-main"),TEXT("M5")),
                HapticsOn+TEXT("{}"),
                FString::ChrN(1025,TEXT('x')),
            };
            for(int32 I=0;I<Bad.Num();++I) {FHapbeatDemoSwitchMessage M;TestFalse(FString::Printf(TEXT("malformed %d"),I),HapbeatDemoSwitchProtocol::Parse(Bytes(Bad[I]),M));}
        });
        It(TEXT("answers with the current demo"),[this,Secret,HapticsOn]()
        {
            FHapbeatDemoSwitchMessage M;HapbeatDemoSwitchProtocol::Parse(Bytes(HapticsOn),M);
            const TSharedPtr<FJsonObject> Ready=Object(HapbeatDemoSwitchProtocol::Status(M,TEXT("trex-encounter"),TEXT("READY"),TEXT("ok"),Secret));
            TestTrue(TEXT("status json"),Ready.IsValid());
            if(!Ready.IsValid()) return;
            TestEqual(TEXT("current_demo_id"),Ready->GetStringField(TEXT("current_demo_id")),FString(TEXT("trex-encounter")));
            TestEqual(TEXT("signed status"),Ready->GetStringField(TEXT("auth")).Len(),64);
            const TSharedPtr<FJsonObject> Unsigned=Object(HapbeatDemoSwitchProtocol::Status(M,TEXT("safety-mill"),TEXT("FAILED"),TEXT("not_allowed"),FString()));
            TestTrue(TEXT("unsigned status has no auth"),Unsigned.IsValid()&&!Unsigned->HasField(TEXT("auth")));
        });
        // QUERY / STATE (contracts cae1715 fixtures unsigned_query / unsigned_state). Vectors: HMAC-SHA256 with key
        // "unit-test-secret" (Python's hmac) over these canonical strings:
        //   QUERY: "HAPBEAT-DEMO-SWITCH/1\nQUERY\nversion=1:1\ntype=5:QUERY\ncontroller_id=12:remote-pixel\nnonce=16:0123456789abcdef\n"
        //   STATE: "HAPBEAT-DEMO-SWITCH/1\nSTATE\nversion=1:1\ntype=5:STATE\ncontroller_id=12:remote-pixel\nnonce=16:0123456789abcdef\n
        //           current_demo_id=8:handdemo\nhaptics_on=4:true\nhaptics_ui=5:false\nrecenter_ui=5:false\npaused=5:false\nstep_index=1:1\nstep_count=1:3\n"
        //   STATE outside a session: "...current_demo_id=11:safety-mill\nhaptics_on=5:false\nhaptics_ui=4:true\nrecenter_ui=4:true\npaused=4:true\n
        //           step_index=2:-1\nstep_count=1:0\n" (same header and first four fields)
        const FString Query=TEXT(R"({"version":1,"type":"QUERY","controller_id":"remote-pixel","nonce":"0123456789abcdef"})");
        It(TEXT("authenticates QUERY and rejects malformed ones"),[this,Secret,Query]()
        {
            FHapbeatDemoSwitchMessage M;
            TestTrue(TEXT("QUERY parses"),HapbeatDemoSwitchProtocol::Parse(Bytes(Query),M));
            TestEqual(TEXT("nonce"),M.Nonce,FString(TEXT("0123456789abcdef")));
            TestTrue(TEXT("isolated unsigned"),HapbeatDemoSwitchProtocol::Authenticate(M,FString(),true));
            TestFalse(TEXT("secret set, no auth"),HapbeatDemoSwitchProtocol::Authenticate(M,Secret,false));
            const FString Signed=Query.Replace(TEXT("\"}"),TEXT("\",\"auth\":\"78a864c5ba7793ab04e1f174e3d9e39a6452498bb6131e141f8007757d3ced30\"}"));
            TestTrue(TEXT("signed QUERY parses"),HapbeatDemoSwitchProtocol::Parse(Bytes(Signed),M));
            TestTrue(TEXT("QUERY signature"),HapbeatDemoSwitchProtocol::Authenticate(M,Secret,false));
            M.Nonce=TEXT("0123456789abcdee");
            TestFalse(TEXT("nonce is signed"),HapbeatDemoSwitchProtocol::Authenticate(M,Secret,false));
            const TArray<FString> Bad={
                Query.Replace(TEXT("\"}"),TEXT("\",\"seq\":1}")),
                Query.Replace(TEXT("\"}"),TEXT("\",\"demo_id\":\"handdemo\"}")),
                Query.Replace(TEXT("0123456789abcdef"),TEXT("0123456789ABCDEF")),
                Query.Replace(TEXT("\"nonce\":\"0123456789abcdef\""),TEXT("\"nonce\":1")),
                Query.Replace(TEXT(",\"nonce\":\"0123456789abcdef\""),TEXT("")),
                Query.Replace(TEXT("QUERY"),TEXT("STATE")),
            };
            for(int32 I=0;I<Bad.Num();++I) {FHapbeatDemoSwitchMessage B;TestFalse(FString::Printf(TEXT("malformed QUERY %d"),I),HapbeatDemoSwitchProtocol::Parse(Bytes(Bad[I]),B));}
        });
        It(TEXT("answers QUERY with STATE"),[this,Secret,Query]()
        {
            FHapbeatDemoSwitchMessage M;HapbeatDemoSwitchProtocol::Parse(Bytes(Query),M);
            FHapbeatDemoSwitchState S;S.bHapticsOn=true;S.StepIndex=1;S.StepCount=3;
            TestEqual(TEXT("unsigned STATE = fixture"),HapbeatDemoSwitchProtocol::State(M,TEXT("handdemo"),S,FString()),
                FString(TEXT(R"({"version":1,"type":"STATE","controller_id":"remote-pixel","nonce":"0123456789abcdef","current_demo_id":"handdemo","haptics_on":true,"haptics_ui":false,"recenter_ui":false,"paused":false,"step_index":1,"step_count":3})")));
            const TSharedPtr<FJsonObject> Signed=Object(HapbeatDemoSwitchProtocol::State(M,TEXT("handdemo"),S,Secret));
            TestTrue(TEXT("signed STATE"),Signed.IsValid()&&Signed->GetStringField(TEXT("auth"))==TEXT("3c238c3a0d0fb308b7db1a8f59b4ced8a8c2e550fa8980be42982c0705c86ea8"));
            FHapbeatDemoSwitchState Out;Out.bHapticsOn=false;Out.bHapticsUi=Out.bRecenterUi=Out.bPaused=true;
            const FString Outside=HapbeatDemoSwitchProtocol::State(M,TEXT("safety-mill"),Out,Secret);
            TestTrue(TEXT("outside a session: -1 / 0"),Outside.Contains(TEXT(R"("paused":true,"step_index":-1,"step_count":0,"auth":"1b4f30e7273c75fb570b743731fd66e26410f18cb94daa126778734b55d98225")")));
        });
    });
    Describe(TEXT("ControlRoute"),[this]()
    {
        auto Route=[](const TCHAR* Action,bool bRegistered,bool bPause,bool bToggle){return UHapbeatDemoSessionSubsystem::RouteControl(Action,bRegistered,bPause,bToggle);};
        It(TEXT("menu and restart fall back to the shared pause only when no handler is registered"),[this,Route]()
        {
            TestTrue(TEXT("menu_open: shared pause"),Route(TEXT("menu_open"),false,true,false)==EHapbeatControlRoute::SharedPause);
            TestTrue(TEXT("menu_close: shared pause"),Route(TEXT("menu_close"),false,true,false)==EHapbeatControlRoute::SharedPause);
            TestTrue(TEXT("restart: shared restart"),Route(TEXT("restart"),false,true,false)==EHapbeatControlRoute::SharedRestart);
            TestTrue(TEXT("menu_open: the demo's own menu"),Route(TEXT("menu_open"),true,true,false)==EHapbeatControlRoute::Registered);
            TestTrue(TEXT("restart: the demo's own restart"),Route(TEXT("restart"),true,true,false)==EHapbeatControlRoute::Registered);
            TestTrue(TEXT("menu_open without pause or handler"),Route(TEXT("menu_open"),false,false,false)==EHapbeatControlRoute::NotAllowed);
            TestTrue(TEXT("restart without pause or handler"),Route(TEXT("restart"),false,false,false)==EHapbeatControlRoute::NotAllowed);
            TestTrue(TEXT("scene has no shared handler"),Route(TEXT("scene"),false,true,true)==EHapbeatControlRoute::NotAllowed);
        });
        It(TEXT("tutorial_start needs a registered handler; haptics and recenter are the plugin's"),[this,Route]()
        {
            TestTrue(TEXT("tutorial_start registered"),Route(TEXT("tutorial_start"),true,true,true)==EHapbeatControlRoute::Registered);
            TestTrue(TEXT("tutorial_start without one"),Route(TEXT("tutorial_start"),false,true,true)==EHapbeatControlRoute::NotAllowed);
            TestTrue(TEXT("haptics_on with the toggle"),Route(TEXT("haptics_on"),false,false,true)==EHapbeatControlRoute::Plugin);
            TestTrue(TEXT("haptics_on without the toggle"),Route(TEXT("haptics_on"),true,true,false)==EHapbeatControlRoute::NotAllowed);
            TestTrue(TEXT("recenter"),Route(TEXT("recenter"),true,false,false)==EHapbeatControlRoute::Plugin);
            TestTrue(TEXT("recenter_ui_show"),Route(TEXT("recenter_ui_show"),false,false,false)==EHapbeatControlRoute::Plugin);
        });
    });
    Describe(TEXT("Press"),[this]()
    {
        // A 200 x 100 px panel at 0.1 cm/px facing +X; one button in the middle (pixels 50..150 x 25..75).
        const FTransform Plane(FQuat::Identity,FVector::ZeroVector);
        const FVector2D Size(200,100);
        const TArray<FBox2D> Buttons={FBox2D(FVector2D(50,25),FVector2D(150,75))};
        auto Finger=[](float Depth,float Right=0){FHapbeatSessionPointerInput In;In.bFinger[1]=true;In.Finger[1]=FVector(Depth,-Right,0);return In;};
        It(TEXT("presses once per poke and needs a fresh approach"),[this,Plane,Size,Buttons,Finger]()
        {
            FHapbeatSessionPressTracker P;
            TestEqual(TEXT("approach arms"),P.Update(Plane,.1f,Size,Buttons,Finger(8),true),int32(INDEX_NONE));
            TestEqual(TEXT("push presses"),P.Update(Plane,.1f,Size,Buttons,Finger(0),true),0);
            TestEqual(TEXT("holding does not repeat"),P.Update(Plane,.1f,Size,Buttons,Finger(-1),true),int32(INDEX_NONE));
            P.Update(Plane,.1f,Size,Buttons,Finger(8),true);
            TestEqual(TEXT("second poke"),P.Update(Plane,.1f,Size,Buttons,Finger(.5f),true),0);
            TestEqual(TEXT("started inside: no press"),P.Update(Plane,.1f,Size,Buttons,Finger(0),true),int32(INDEX_NONE));
            P.Update(Plane,.1f,Size,Buttons,Finger(8,30),true);
            TestEqual(TEXT("beside the button"),P.Update(Plane,.1f,Size,Buttons,Finger(0,30),true),int32(INDEX_NONE));
        });
        It(TEXT("ignores pokes while not accepting"),[this,Plane,Size,Buttons,Finger]()
        {
            FHapbeatSessionPressTracker P;
            P.Update(Plane,.1f,Size,Buttons,Finger(8),false);
            TestEqual(TEXT("disabled"),P.Update(Plane,.1f,Size,Buttons,Finger(0),false),int32(INDEX_NONE));
            TestEqual(TEXT("enabled mid-push: still needs an approach"),P.Update(Plane,.1f,Size,Buttons,Finger(0),true),int32(INDEX_NONE));
            P.Update(Plane,.1f,Size,Buttons,Finger(8),true);
            TestEqual(TEXT("then presses"),P.Update(Plane,.1f,Size,Buttons,Finger(0),true),0);
        });
        It(TEXT("presses with a controller ray on the trigger's rising edge"),[this,Plane,Size,Buttons]()
        {
            FHapbeatSessionPressTracker P;
            FHapbeatSessionPointerInput In;In.bRay[0]=true;In.RayOrigin[0]=FVector(40,0,0);In.RayDirection[0]=FVector(-1,0,0);
            float Hit[2]={150,150};int32 Hover=INDEX_NONE;
            TestEqual(TEXT("aiming only"),P.Update(Plane,.1f,Size,Buttons,In,true,&Hover,Hit),int32(INDEX_NONE));
            TestEqual(TEXT("hover"),Hover,0);TestTrue(TEXT("ray stops at the panel"),FMath::IsNearlyEqual(Hit[0],40.f));
            In.bTrigger[0]=true;
            TestEqual(TEXT("trigger presses"),P.Update(Plane,.1f,Size,Buttons,In,true),0);
            TestEqual(TEXT("held trigger does not repeat"),P.Update(Plane,.1f,Size,Buttons,In,true),int32(INDEX_NONE));
            In.bTrigger[0]=false;P.Update(Plane,.1f,Size,Buttons,In,true);
            In.RayDirection[0]=FVector(-1,.5f,0).GetSafeNormal();In.bTrigger[0]=true;
            TestEqual(TEXT("off the button"),P.Update(Plane,.1f,Size,Buttons,In,true),int32(INDEX_NONE));
            In.bTrigger[0]=false;P.Update(Plane,.1f,Size,Buttons,In,true);
            In.RayOrigin[0]=FVector(-40,0,0);In.RayDirection[0]=FVector(1,0,0);In.bTrigger[0]=true;
            TestEqual(TEXT("from behind"),P.Update(Plane,.1f,Size,Buttons,In,true),int32(INDEX_NONE));
        });
    });
    Describe(TEXT("CompletionPanel"),[this]()
    {
        It(TEXT("stacks the buttons like the Unity panels"),[this]()
        {
            // DemoSessionCompletionPanel / DemoPausePanel in millimetres, x 2 px.
            const FHapbeatSessionPanelLayout C=AHapbeatDemoSessionUi::CompletionLayout(2);
            TestTrue(TEXT("completion size"),C.Size.Equals(FVector2D(880,2*(194+2*82))));
            TestTrue(TEXT("first button"),C.Buttons[0].Min.Equals(FVector2D(60,252))&&C.Buttons[0].Max.Equals(FVector2D(820,388)));
            TestTrue(TEXT("second button under it"),FMath::IsNearlyEqual(C.Buttons[1].Min.Y,252+164.f));
            TestTrue(TEXT("error line 14 mm under the last button"),FMath::IsNearlyEqual(C.Error.Min.Y,C.Buttons[1].Max.Y+28.f));
            const FHapbeatSessionPanelLayout P=AHapbeatDemoSessionUi::PauseLayout(4);
            TestTrue(TEXT("pause size"),P.Size.Equals(FVector2D(880,2*(148+4*82))));
            TestTrue(TEXT("pause first button"),FMath::IsNearlyEqual(P.Buttons[0].Min.Y,184.f));
            for(int32 I=1;I<4;++I) TestTrue(TEXT("same column"),FMath::IsNearlyEqual(P.Buttons[I].Min.X,P.Buttons[0].Min.X)&&FMath::IsNearlyEqual(P.Buttons[I].Min.Y,P.Buttons[I-1].Max.Y+28.f));
        });
        It(TEXT("ignores the buttons for the first second, then retries / goes next"),[this]()
        {
            UWorld* World=UWorld::CreateWorld(EWorldType::EditorPreview,false);
            ON_SCOPE_EXIT { World->DestroyWorld(false); };
            AHapbeatDemoSessionUi* Ui=World->SpawnActor<AHapbeatDemoSessionUi>();
            if(!TestNotNull(TEXT("ui"),Ui)) return;
            FHapbeatSessionCompletionView View;View.StepNumber=1;View.StepCount=2;View.bRetry=true;View.NextLabel=TEXT("次へ：T-Rex");
            const FVector Eye(0,0,160);
            const FTransform At=AHapbeatDemoSessionUi::PlacePanel(Eye,FRotator::ZeroRotator);
            Ui->ShowCompletion(View,At);
            const FHapbeatSessionPanelLayout L=AHapbeatDemoSessionUi::CompletionLayout(2);
            auto Poke=[&](int32 Button,float Depth,float Dt)
            {
                FHapbeatSessionPointerInput In;In.bFinger[1]=true;In.Finger[1]=PanelPoint(At,L.Size,L.Buttons[Button].GetCenter(),Depth);
                return Ui->Step(In,Eye,FRotator::ZeroRotator,Dt);
            };
            Poke(0,8,.3f);
            const auto Early=Poke(0,0,.3f);
            TestFalse(TEXT("no retry within 1 s"),Early.bRetry);TestFalse(TEXT("not accepting yet"),Ui->IsCompletionAccepting());
            Poke(0,8,.5f);
            TestTrue(TEXT("accepting after 1 s"),Ui->IsCompletionAccepting());
            TestTrue(TEXT("retry (top)"),Poke(0,0,.1f).bRetry);
            Poke(1,8,.1f);
            const auto Next=Poke(1,0,.1f);
            TestTrue(TEXT("next (below)"),Next.bNext);TestFalse(TEXT("only next"),Next.bRetry);
            Ui->HideCompletion();
            TestFalse(TEXT("hidden"),Ui->IsCompletionShown());
        });
    });
    Describe(TEXT("PanelPlacement"),[this]()
    {
        const FVector Eye(0,0,160);
        It(TEXT("puts the panel 55 cm ahead and 12 cm below the eye without an anchor"),[this,Eye]()
        {
            const FTransform T=AHapbeatDemoSessionUi::PlacePanel(Eye,FRotator(-40,90,0));
            TestTrue(TEXT("head yaw only"),T.GetLocation().Equals(FVector(0,55,148),.01f));
            TestTrue(TEXT("faces the eye"),FVector::DotProduct(T.GetRotation().GetAxisX(),(Eye-T.GetLocation()).GetSafeNormal())>.999f);
        });
        It(TEXT("uses the anchor's location, turned toward the eye, or its own rotation never facing away"),[this,Eye]()
        {
            UWorld* World=UWorld::CreateWorld(EWorldType::EditorPreview,false);
            ON_SCOPE_EXIT { World->DestroyWorld(false); };
            TestNull(TEXT("no anchor yet"),UHapbeatDemoSessionPanelAnchor::Find(World));
            AActor* Owner=World->SpawnActor<AActor>();
            if(!TestNotNull(TEXT("owner"),Owner)) return;
            auto* Anchor=NewObject<UHapbeatDemoSessionPanelAnchor>(Owner);
            Owner->SetRootComponent(Anchor);Anchor->RegisterComponent();
            Anchor->SetWorldLocationAndRotation(FVector(100,30,120),FRotator(0,40,0));
            TestTrue(TEXT("found"),UHapbeatDemoSessionPanelAnchor::Find(World)==Anchor);
            FTransform T=AHapbeatDemoSessionUi::PlacePanel(Eye,FRotator::ZeroRotator,Anchor);
            TestTrue(TEXT("at the anchor"),T.GetLocation().Equals(FVector(100,30,120),.01f));
            const FVector Flat=(Eye-T.GetLocation()).GetSafeNormal2D();
            TestTrue(TEXT("yaw toward the eye"),FVector::DotProduct(T.GetRotation().GetAxisX(),Flat)>.999f);
            TestTrue(TEXT("upright"),FMath::IsNearlyZero(T.GetRotation().GetAxisX().Z,1e-4f));
            Anchor->bUseRotation=true;
            Anchor->SetWorldRotation(FRotator(0,170,0)); // +X toward the eye (which is at -X)
            T=AHapbeatDemoSessionUi::PlacePanel(Eye,FRotator::ZeroRotator,Anchor);
            TestTrue(TEXT("anchor rotation kept"),T.GetRotation().Equals(Anchor->GetComponentQuat(),1e-4f));
            Anchor->SetWorldRotation(FRotator(0,-10,0)); // +X away from the eye
            T=AHapbeatDemoSessionUi::PlacePanel(Eye,FRotator::ZeroRotator,Anchor);
            TestTrue(TEXT("turned round to face the eye"),FVector::DotProduct(T.GetRotation().GetAxisX(),Eye-T.GetLocation())>0);
            TestTrue(TEXT("still upright"),FMath::IsNearlyZero(T.GetRotation().GetAxisZ().Z-1.f,1e-4f));
        });
    });
    Describe(TEXT("Pause"),[this]()
    {
        // Left hand 40 cm in front of the eye (+X), palm toward the eye: wrist below the fingers, thumb side (+Y)
        // ... built so that -cross(middle - wrist, index - little) points back at the eye.
        auto Hand=[](float PinchCm,bool bPalmToEye)
        {
            FHapbeatPauseInput In;In.Eye=FVector(0,0,160);In.bLeftHand=true;
            In.LeftHand.Init(FVector(40,0,150),EHandKeypointCount);
            const float S=bPalmToEye?1.f:-1.f;
            auto Set=[&](EHandKeypoint K,const FVector& P){In.LeftHand[int32(K)]=P;};
            Set(EHandKeypoint::Palm,FVector(40,0,150));
            Set(EHandKeypoint::Wrist,FVector(40,0,142));
            Set(EHandKeypoint::MiddleProximal,FVector(40,0,155));
            Set(EHandKeypoint::IndexProximal,FVector(40,-3*S,155));
            Set(EHandKeypoint::LittleProximal,FVector(40,3*S,154));
            Set(EHandKeypoint::ThumbTip,FVector(36,-4*S,152));
            Set(EHandKeypoint::IndexTip,FVector(36,-4*S,152+PinchCm));
            return In;
        };
        It(TEXT("detects the palm-facing pinch only with the palm toward the eye"),[this,Hand]()
        {
            const FHapbeatPauseInput Toward=Hand(.5f,true), Away=Hand(.5f,false), Open=Hand(5.f,true);
            TestTrue(TEXT("palm to the eye, pinched"),FHapbeatPauseDetector::IsPalmPinch(Toward.LeftHand,Toward.Eye,1.5f));
            TestFalse(TEXT("back of the hand to the eye"),FHapbeatPauseDetector::IsPalmPinch(Away.LeftHand,Away.Eye,1.5f));
            TestFalse(TEXT("not pinched"),FHapbeatPauseDetector::IsPalmPinch(Open.LeftHand,Open.Eye,1.5f));
        });
        It(TEXT("SystemMenu fires on the menu button's rising edge"),[this,Hand]()
        {
            FHapbeatPauseDetector D;
            FHapbeatPauseInput In=Hand(5.f,true);
            for(int32 I=0;I<10;++I) TestFalse(TEXT("an open hand does nothing"),D.Update(In,.1f));
            In.bMenuButton=true;
            TestTrue(TEXT("press"),D.Update(In,.1f));
            TestFalse(TEXT("held"),D.Update(In,.1f));
            In.bMenuButton=false;D.Update(In,.1f);
            In.bMenuButton=true;
            TestTrue(TEXT("second press"),D.Update(In,.1f));
            FHapbeatPauseInput NoHand;NoHand.bMenuButton=true;
            D.Update(FHapbeatPauseInput(),.1f);
            TestTrue(TEXT("without a tracked hand"),D.Update(NoHand,.1f));
        });
        It(TEXT("SystemMenu fires after a short hold of the palm-facing pinch, once until it is let go"),[this,Hand]()
        {
            constexpr float T=.0625f;
            // 1/16 s steps (exact in float): 4 of them stay under the default 0.3 s, the 5th passes it.
            FHapbeatPauseDetector D;
            TestEqual(TEXT("default hold"),D.HoldSeconds,FHapbeatPauseSettings::SystemMenuHoldSeconds);
            const FHapbeatPauseInput Pinch=Hand(.5f,true), Loose=Hand(2.5f,true), Open=Hand(5.f,true), Away=Hand(.5f,false);
            bool Fired=false;
            for(int32 I=0;I<4;++I) Fired|=D.Update(I<2?Pinch:Loose,T); // starts at <= 1.5 cm, holds up to 3 cm
            TestFalse(TEXT("not before 0.3 s"),Fired);
            TestTrue(TEXT("past 0.3 s"),D.Update(Pinch,T));
            for(int32 I=0;I<40;++I) TestFalse(TEXT("still held: once only"),D.Update(I%2?Pinch:Loose,T));
            D.Update(Open,T);
            for(int32 I=0;I<4;++I) TestFalse(TEXT("second hold, not yet"),D.Update(Pinch,T));
            TestTrue(TEXT("again after a release (closes the pause)"),D.Update(Pinch,T));
            D.Update(Open,T);
            for(int32 I=0;I<3;++I) D.Update(Pinch,T);
            D.Update(Away,T);
            for(int32 I=0;I<4;++I) TestFalse(TEXT("turning the palm away restarts the count"),D.Update(Pinch,T));
            TestTrue(TEXT("after a full hold"),D.Update(Pinch,T));
            D.Update(Open,T);
            for(int32 I=0;I<20;++I) TestFalse(TEXT("the back of the hand never fires"),D.Update(Away,T));
            FHapbeatPauseDetector Slow;Slow.HoldSeconds=.5f;
            bool SlowFired=false;
            for(int32 I=0;I<7;++I) SlowFired|=Slow.Update(Pinch,T);
            TestFalse(TEXT("HoldSeconds is the setting"),SlowFired);
            TestTrue(TEXT("at 0.5 s (8 steps)"),Slow.Update(Pinch,T));
        });
        It(TEXT("SystemMenu: the runtime's hand menu flag fires at once and is the same gesture as the hold"),[this,Hand]()
        {
            constexpr float T=.0625f;
            FHapbeatPauseDetector D;
            FHapbeatPauseInput Flag=Hand(.5f,true);Flag.bHandMenu=true;
            const FHapbeatPauseInput Pinch=Hand(.5f,true), Open=Hand(5.f,true);
            TestTrue(TEXT("flag fires at once"),D.Update(Flag,T));
            for(int32 I=0;I<20;++I) TestFalse(TEXT("the pinch held after it does not fire again"),D.Update(Pinch,T));
            D.Update(Open,T);
            for(int32 I=0;I<4;++I) D.Update(Pinch,T);
            TestTrue(TEXT("hold fires first"),D.Update(Pinch,T));
            TestFalse(TEXT("a flag later in the same gesture does nothing"),D.Update(Flag,T));
            D.Update(Open,T);
            FHapbeatPauseInput FlagNoJoints;FlagNoJoints.bHandMenu=true;
            TestTrue(TEXT("flag without joints"),D.Update(FlagNoJoints,T));
            TestFalse(TEXT("flag held"),D.Update(FlagNoJoints,T));
            D.Update(FHapbeatPauseInput(),T);
            TestTrue(TEXT("flag again after a release"),D.Update(FlagNoJoints,T));
            FHapbeatPauseDetector B;B.Gesture=EHapbeatPauseGesture::PalmPinchHold;B.HoldSeconds=FHapbeatPauseSettings::PalmPinchHoldSeconds;
            TestFalse(TEXT("B ignores the flag"),B.Update(FlagNoJoints,T));
        });
        It(TEXT("PalmPinchHold fires after 2 s of the gesture and needs a release before the next"),[this,Hand]()
        {
            // 0.125 s steps: 16 of them are exactly HoldSeconds.
            FHapbeatPauseDetector D;D.Gesture=EHapbeatPauseGesture::PalmPinchHold;D.HoldSeconds=FHapbeatPauseSettings::PalmPinchHoldSeconds;
            const FHapbeatPauseInput Pinch=Hand(.5f,true), Loose=Hand(2.5f,true), Open=Hand(5.f,true), Away=Hand(.5f,false);
            bool Fired=false;
            for(int32 I=0;I<15;++I) Fired|=D.Update(I<2?Pinch:Loose,.125f); // starts at <= 1.5 cm, holds up to 3 cm
            TestFalse(TEXT("not before 2 s"),Fired);
            TestTrue(TEXT("at 2 s"),D.Update(Pinch,.125f));
            for(int32 I=0;I<30;++I) TestFalse(TEXT("still held: once only"),D.Update(Pinch,.125f));
            D.Update(Open,.125f);
            for(int32 I=0;I<15;++I) D.Update(Pinch,.125f);
            TestTrue(TEXT("again after a release"),D.Update(Pinch,.125f));
            D.Update(Open,.125f);
            for(int32 I=0;I<8;++I) D.Update(Pinch,.125f);
            D.Update(Away,.125f);
            for(int32 I=0;I<15;++I) TestFalse(TEXT("turning the palm away restarts the count"),D.Update(Pinch,.125f));
            FHapbeatPauseInput Button=Open;Button.bMenuButton=true;
            TestTrue(TEXT("the controller button works in B too"),D.Update(Button,.125f));
        });
        It(TEXT("Reset waits for a release"),[this,Hand]()
        {
            FHapbeatPauseDetector D;D.Gesture=EHapbeatPauseGesture::PalmPinchHold;D.HoldSeconds=FHapbeatPauseSettings::PalmPinchHoldSeconds;
            const FHapbeatPauseInput Pinch=Hand(.5f,true), Open=Hand(5.f,true);
            for(int32 I=0;I<8;++I) D.Update(Pinch,.125f);
            D.Reset();
            for(int32 I=0;I<30;++I) TestFalse(TEXT("held through the reset"),D.Update(Pinch,.125f));
            D.Update(Open,.125f);
            for(int32 I=0;I<15;++I) D.Update(Pinch,.125f);
            TestTrue(TEXT("after a release"),D.Update(Pinch,.125f));
        });
        It(TEXT("parses the Gesture setting"),[this]()
        {
            EHapbeatPauseGesture G=EHapbeatPauseGesture::SystemMenu;
            TestTrue(TEXT("B"),FHapbeatPauseSettings::ParseGesture(TEXT("palmpinchhold"),G));
            TestTrue(TEXT("B parsed"),G==EHapbeatPauseGesture::PalmPinchHold);
            TestFalse(TEXT("unknown"),FHapbeatPauseSettings::ParseGesture(TEXT("Hold"),G));
            TestTrue(TEXT("unknown leaves it"),G==EHapbeatPauseGesture::PalmPinchHold);
            TestTrue(TEXT("A"),FHapbeatPauseSettings::ParseGesture(TEXT("SystemMenu"),G));
            TestTrue(TEXT("A parsed"),G==EHapbeatPauseGesture::SystemMenu);
        });
        It(TEXT("pause panel: resume / restart, and Hub only when installed"),[this]()
        {
            UWorld* World=UWorld::CreateWorld(EWorldType::EditorPreview,false);
            ON_SCOPE_EXIT { World->DestroyWorld(false); };
            AHapbeatDemoSessionUi* Ui=World->SpawnActor<AHapbeatDemoSessionUi>();
            if(!TestNotNull(TEXT("ui"),Ui)) return;
            const FVector Eye(0,0,160);
            const FTransform At=AHapbeatDemoSessionUi::PlacePanel(Eye,FRotator::ZeroRotator);
            int32 Count=0;
            auto Press=[&](int32 Button)
            {
                const FHapbeatSessionPanelLayout L=AHapbeatDemoSessionUi::PauseLayout(Count);
                FHapbeatSessionPointerInput In;In.bFinger[1]=true;In.Finger[1]=PanelPoint(At,L.Size,L.Buttons[Button].GetCenter(),8);
                Ui->Step(In,Eye,FRotator::ZeroRotator,.1f);
                In.Finger[1]=PanelPoint(At,L.Size,L.Buttons[Button].GetCenter(),0);
                return Ui->Step(In,Eye,FRotator::ZeroRotator,.1f);
            };
            auto Wait=[&](){FHapbeatSessionPointerInput None;for(int32 I=0;I<11;++I) Ui->Step(None,Eye,FRotator::ZeroRotator,.1f);};
            // Top to bottom: 再開 / 最初からやり直す / Hub に戻る.
            Count=3;Ui->ShowPause(true,FString(),At);
            TestTrue(TEXT("shown"),Ui->IsPauseShown());
            TestFalse(TEXT("not within 1 s"),Press(0).bResume);
            Wait();
            TestTrue(TEXT("accepting"),Ui->IsPauseAccepting());
            TestTrue(TEXT("resume"),Press(0).bResume);
            TestTrue(TEXT("restart"),Press(1).bRestart);
            const auto Hub=Press(2);
            TestTrue(TEXT("hub"),Hub.bHub);TestFalse(TEXT("only hub"),Hub.bResume||Hub.bRestart||Hub.bNext||Hub.bRetry);
            Ui->HidePause();
            TestFalse(TEXT("hidden"),Ui->IsPauseShown());
            // Without the Hub: 再開 / 最初からやり直す only; nothing where the third button was.
            Count=2;Ui->ShowPause(false,FString(),At);Wait();
            TestTrue(TEXT("resume (no hub)"),Press(0).bResume);
            TestTrue(TEXT("restart (no hub)"),Press(1).bRestart);
            const FHapbeatSessionPanelLayout Three=AHapbeatDemoSessionUi::PauseLayout(3);
            FHapbeatSessionPointerInput In;In.bFinger[1]=true;In.Finger[1]=PanelPoint(At,AHapbeatDemoSessionUi::PauseLayout(2).Size,Three.Buttons[2].GetCenter(),8);
            Ui->Step(In,Eye,FRotator::ZeroRotator,.1f);In.Finger[1].X-=8;
            const auto None=Ui->Step(In,Eye,FRotator::ZeroRotator,.1f);
            TestFalse(TEXT("no hub button"),None.bHub||None.bResume||None.bRestart||None.bPauseNext);
            Ui->HidePause();
            // Session with the Hub: 再開 / 最初からやり直す / 次へ / Hub に戻る.
            Count=4;Ui->ShowPause(true,TEXT("次へ：T-Rex"),At);Wait();
            TestTrue(TEXT("resume (4)"),Press(0).bResume);
            TestTrue(TEXT("restart (4)"),Press(1).bRestart);
            const auto Next=Press(2);
            TestTrue(TEXT("next (4)"),Next.bPauseNext);TestFalse(TEXT("only next"),Next.bNext||Next.bHub||Next.bResume||Next.bRestart);
            TestTrue(TEXT("hub (4)"),Press(3).bHub);
            Ui->HidePause();
            // Session without the Hub (last step: デモを終了).
            Count=3;Ui->ShowPause(false,TEXT("デモを終了"),At);Wait();
            TestTrue(TEXT("resume (3)"),Press(0).bResume);
            TestTrue(TEXT("restart (3)"),Press(1).bRestart);
            const auto Finish=Press(2);
            TestTrue(TEXT("finish (3)"),Finish.bPauseNext);TestFalse(TEXT("no hub (3)"),Finish.bHub);
        });
        It(TEXT("a recenter moves the open panel in front of the head without restarting its input delay"),[this]()
        {
            UWorld* World=UWorld::CreateWorld(EWorldType::EditorPreview,false);
            ON_SCOPE_EXIT { World->DestroyWorld(false); };
            AHapbeatDemoSessionUi* Ui=World->SpawnActor<AHapbeatDemoSessionUi>();
            if(!TestNotNull(TEXT("ui"),Ui)) return;
            const FVector Eye(0,0,160);
            Ui->ShowPause(false,FString(),AHapbeatDemoSessionUi::PlacePanel(Eye,FRotator::ZeroRotator));
            FHapbeatSessionPointerInput None;
            for(int32 I=0;I<11;++I) Ui->Step(None,Eye,FRotator::ZeroRotator,.1f);
            TestTrue(TEXT("accepting"),Ui->IsPauseAccepting());
            // After the recenter the user faces +Y (the panel had ended up behind / beside them).
            const FTransform At=AHapbeatDemoSessionUi::PlacePanel(Eye,FRotator(0,90,0));
            Ui->Reposition(At);
            TestTrue(TEXT("still accepting"),Ui->IsPauseAccepting());
            TestTrue(TEXT("in front of the new facing"),At.GetLocation().Equals(FVector(0,55,148),.01f));
            const FHapbeatSessionPanelLayout L=AHapbeatDemoSessionUi::PauseLayout(2);
            FHapbeatSessionPointerInput In;In.bFinger[1]=true;In.Finger[1]=PanelPoint(At,L.Size,L.Buttons[0].GetCenter(),8);Ui->Step(In,Eye,FRotator(0,90,0),.1f);
            In.Finger[1]=PanelPoint(At,L.Size,L.Buttons[0].GetCenter(),0);
            TestTrue(TEXT("pressed at the new place"),Ui->Step(In,Eye,FRotator(0,90,0),.1f).bResume);
            Ui->HidePause();
            Ui->Reposition(AHapbeatDemoSessionUi::PlacePanel(Eye,FRotator::ZeroRotator));
            TestFalse(TEXT("a closed panel stays closed"),Ui->IsPauseShown());
        });
    });
    Describe(TEXT("Recenter"),[this]()
    {
        It(TEXT("the default recenter puts the head over the pawn's start, facing its start yaw, at the same height"),[this]()
        {
            // Pawn at (100, 0, 0) turned 90 deg; the head 30 cm in front of it (+Y) and 160 cm up, looking 20 deg further.
            const FTransform Pawn(FRotator(0,90,0),FVector(100,0,0));
            const FVector Eye(100,30,160);
            const FTransform T=UHapbeatDemoSessionSubsystem::RecenterPawn(Pawn,Eye,110,FVector(0,0,0),0);
            const FVector NewEye=T.TransformPosition(Pawn.InverseTransformPosition(Eye));
            TestTrue(TEXT("head over the start"),FVector2D(NewEye).IsNearlyZero(.01f));
            TestTrue(TEXT("height unchanged"),FMath::IsNearlyEqual(NewEye.Z,160.f,.01f));
            const float NewFacing=T.GetRotation().Rotator().Yaw+(110-90);
            TestTrue(TEXT("facing the start yaw"),FMath::IsNearlyZero(FRotator::NormalizeAxis(NewFacing),.01f));
        });
        It(TEXT("the 視線をリセット button presses like the haptics button and reports the controls' heading"),[this]()
        {
            UWorld* World=UWorld::CreateWorld(EWorldType::EditorPreview,false);
            ON_SCOPE_EXIT { World->DestroyWorld(false); };
            AHapbeatDemoSessionUi* Ui=World->SpawnActor<AHapbeatDemoSessionUi>();
            if(!TestNotNull(TEXT("ui"),Ui)) return;
            const FVector Eye(0,0,160);
            Ui->SetRecenterButton(true);
            FHapbeatSessionPointerInput None;
            Ui->Step(None,Eye,FRotator(0,40,0),.1f);
            TestTrue(TEXT("heading from the head"),FMath::IsNearlyEqual(Ui->GetControlsYaw(),40.f));
            // A glance 20 deg down-left (inside the dead zone) keeps the heading.
            Ui->Step(None,Eye,FRotator(-30,20,0),.1f);
            TestTrue(TEXT("a glance keeps it"),FMath::IsNearlyEqual(Ui->GetControlsYaw(),40.f));
            // The pair: 45 cm away, 15 deg left of the heading, 30 deg down; 視線をリセット on the left, haptics on the
            // right, 112 x 68 mm plates 8 mm apart (side by side, not overlapping), each facing the eye.
            const FVector Centre=AHapbeatDemoSessionUi::InViewButtonLocation(Eye,40,true);
            const FVector Haptics=AHapbeatDemoSessionUi::InViewButtonLocation(Eye,40,false);
            const FVector Pair=Eye+FRotator(-30,40-15,0).Vector()*45.f;
            TestTrue(TEXT("pair centre"),((Centre+Haptics)*.5f).Equals(Pair,.01f));
            TestTrue(TEXT("12 cm apart, level"),FMath::IsNearlyEqual(FVector::Dist(Centre,Haptics),12.f,.01f)&&FMath::IsNearlyEqual(Centre.Z,Haptics.Z,.01f));
            TestTrue(TEXT("reset on the left"),FVector::DotProduct(Haptics-Centre,FRotator(0,25,0).Quaternion().GetRightVector())>0);
            const FTransform At((Eye-Centre).Rotation(),Centre);
            const FVector2D Plate(224,136);
            FHapbeatSessionPointerInput In;In.bFinger[0]=true;In.Finger[0]=PanelPoint(At,Plate,Plate*.5f,8);
            TestFalse(TEXT("approach"),Ui->Step(In,Eye,FRotator(-30,20,0),.1f).bRecenter);
            In.Finger[0]=PanelPoint(At,Plate,Plate*.5f,0);
            const auto E=Ui->Step(In,Eye,FRotator(-30,20,0),.1f);
            TestTrue(TEXT("pressed"),E.bRecenter);TestFalse(TEXT("not haptics"),E.bToggleHaptics);
            Ui->SetRecenterButton(false);
            In.Finger[0]=PanelPoint(At,Plate,Plate*.5f,8);Ui->Step(In,Eye,FRotator(-30,20,0),.1f);
            In.Finger[0]=PanelPoint(At,Plate,Plate*.5f,0);
            TestFalse(TEXT("hidden: nothing"),Ui->Step(In,Eye,FRotator(-30,20,0),.1f).bRecenter);
        });
    });
    Describe(TEXT("Handoff"),[this]()
    {
        It(TEXT("ends once after the application goes to the background"),[this]()
        {
            using EStep=FHapbeatLaunchHandoff::EStep;
            FHapbeatLaunchHandoff H;
            TestTrue(TEXT("idle: nothing"),H.Update(true,.1f)==EStep::None);
            H.Start();
            TestTrue(TEXT("waiting"),H.IsWaiting());
            TestTrue(TEXT("still in front"),H.Update(false,1.f)==EStep::None);
            TestTrue(TEXT("focus lost: exit"),H.Update(true,.1f)==EStep::Exit);
            TestTrue(TEXT("done"),H.IsDone());
            TestTrue(TEXT("not a second time"),H.Update(true,.1f)==EStep::None);
            H.Start();
            TestFalse(TEXT("no restart after exit"),H.IsWaiting());
            TestTrue(TEXT("still nothing"),H.Update(true,10.f)==EStep::None);
        });
        It(TEXT("fails after 5 s in front and does not end the demo"),[this]()
        {
            using EStep=FHapbeatLaunchHandoff::EStep;
            FHapbeatLaunchHandoff H;
            H.Start();
            int32 Exits=0,Fails=0;
            for(int32 I=0;I<49;++I) {const EStep S=H.Update(false,.1f);Exits+=S==EStep::Exit;Fails+=S==EStep::Failed;}
            TestEqual(TEXT("nothing within 4.9 s"),Exits+Fails,0);
            TestTrue(TEXT("failed at 5 s"),H.Update(false,.15f)==EStep::Failed);
            TestFalse(TEXT("no longer waiting"),H.IsWaiting());TestFalse(TEXT("not done"),H.IsDone());
            TestTrue(TEXT("a late focus loss ends nothing"),H.Update(true,.1f)==EStep::None);
            H.Start();
            TestTrue(TEXT("can be tried again"),H.IsWaiting());
            TestTrue(TEXT("then exits"),H.Update(true,.1f)==EStep::Exit);
        });
    });
}
#endif
