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
            const FString Json=T.MakeNext(true).ToJson();
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
            TestTrue(TEXT("next parses"),FHapbeatDemoSessionTicket::Parse(T.MakeNext(false).ToJson(),Next,Error));
            TestTrue(TEXT("next is accepted by the next demo"),HapbeatDemoSession::AcceptTicket(T.MakeNext(false).ToJson(),TEXT("trex-encounter"),Next,Error));
            TestFalse(TEXT("haptics_ui false"),Next.bHapticsUi);
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
        It(TEXT("ignores the buttons for the first second, then retries / goes next"),[this]()
        {
            UWorld* World=UWorld::CreateWorld(EWorldType::EditorPreview,false);
            ON_SCOPE_EXIT { World->DestroyWorld(false); };
            AHapbeatDemoSessionUi* Ui=World->SpawnActor<AHapbeatDemoSessionUi>();
            if(!TestNotNull(TEXT("ui"),Ui)) return;
            FHapbeatSessionCompletionView View;View.StepNumber=1;View.StepCount=2;View.bRetry=true;View.NextLabel=TEXT("次へ：T-Rex");
            const FVector Eye(0,0,160);
            Ui->ShowCompletion(View,Eye,FRotator::ZeroRotator);
            // Panel 55 cm ahead, 12 cm down, facing the eye; 0.045 cm/px, 960 x 420 px. Retry button pixels 40..360 x 280..392,
            // next 400..920 x 280..392 (layout in HapbeatDemoSessionUi.cpp).
            const FVector Centre=Eye+FVector(55,0,-12);
            const FQuat Q=(Eye-Centre).Rotation().Quaternion();
            auto Point=[&](float Px,float Py,float Depth){return Centre+Q.RotateVector(FVector(Depth,-(Px-480)*.045f,-(Py-210)*.045f));};
            auto Poke=[&](float Px,float Py,float Depth,float Dt){FHapbeatSessionPointerInput In;In.bFinger[1]=true;In.Finger[1]=Point(Px,Py,Depth);return Ui->Step(In,Eye,FRotator::ZeroRotator,Dt);};
            Poke(200,336,8,.3f);
            const auto Early=Poke(200,336,0,.3f);
            TestFalse(TEXT("no retry within 1 s"),Early.bRetry);TestFalse(TEXT("not accepting yet"),Ui->IsCompletionAccepting());
            Poke(200,336,8,.5f);
            TestTrue(TEXT("accepting after 1 s"),Ui->IsCompletionAccepting());
            TestTrue(TEXT("retry"),Poke(200,336,0,.1f).bRetry);
            Poke(660,336,8,.1f);
            const auto Next=Poke(660,336,0,.1f);
            TestTrue(TEXT("next"),Next.bNext);TestFalse(TEXT("only next"),Next.bRetry);
            Ui->HideCompletion();
            TestFalse(TEXT("hidden"),Ui->IsCompletionShown());
        });
    });
}
#endif
