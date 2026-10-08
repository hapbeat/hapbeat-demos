#include "HapbeatDemoSwitchReceiver.h"
#include "HapbeatDemoSwitchProtocol.h"
#include "HapbeatDemoSessionLog.h"
#include "HapbeatDemoSessionPlatform.h"
#include "Sockets.h"
#include "SocketSubsystem.h"
#include "IPAddress.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/App.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"
#include "Misc/ConfigCacheIni.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformTime.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "Framework/Application/SlateApplication.h"

bool FHapbeatDemoSwitchReceiver::Configure(const FString& InDemoId)
{
    Stop();bEnabled=false;Sequences.Reset();Secret.Empty();DemoId=InDemoId;
    bBindFailed=false;BindFailedAt=NextBindAt=0;bForeground=true;
    if(!HapbeatDemoSwitchProtocol::IsIdentifier(DemoId)) {UE_LOG(LogHapbeatDemoSession,Error,TEXT("DEMO_SWITCH_DISABLED no valid demo_id"));return false;}
#if UE_BUILD_SHIPPING
    bTest=false;
#else
    bTest=FParse::Param(FCommandLine::Get(),TEXT("HapbeatDemoSwitchTest"));
#endif
    FString File=FPaths::ProjectSavedDir()/TEXT("Config/HapbeatDemoSession.json");
    if(bTest) FParse::Value(FCommandLine::Get(),TEXT("HapbeatDemoSwitchConfig="),File);
    StateFile=FPaths::ProjectSavedDir()/(bTest?TEXT("HapbeatDemoSession/TestSequences.json"):TEXT("HapbeatDemoSession/Sequences.json"));
    // The project opts in explicitly in its checked-in Config/DefaultGame.ini, as each Unity demo does in its
    // HapbeatDemoSwitchSettings asset. Without it the receiver stays off. A local file can override both.
    //   [HapbeatDemoSession.DemoSwitch]
    //   Enabled=True
    //   AllowUnsignedOnIsolatedLan=True
    bool Enabled=false,Unsigned=false;
    GConfig->GetBool(TEXT("HapbeatDemoSession.DemoSwitch"),TEXT("Enabled"),Enabled,GGameIni);
    GConfig->GetBool(TEXT("HapbeatDemoSession.DemoSwitch"),TEXT("AllowUnsignedOnIsolatedLan"),Unsigned,GGameIni);
    bool Isolated=Unsigned;
    if(FPaths::FileExists(File)) {
        FString Text;TSharedPtr<FJsonObject> Config;
        if(IFileManager::Get().FileSize(*File)>8192 || !FFileHelper::LoadFileToString(Text,*File)
            || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text),Config) || !Config.IsValid()) {
            UE_LOG(LogHapbeatDemoSession,Error,TEXT("DEMO_SWITCH_DISABLED invalid local configuration %s"),*File);return false;
        }
        Enabled=Unsigned=Isolated=false;
        Config->TryGetBoolField(TEXT("enabled"),Enabled);
        Config->TryGetStringField(TEXT("shared_secret"),Secret);
        Config->TryGetBoolField(TEXT("allow_unsigned"),Unsigned);
        Config->TryGetBoolField(TEXT("isolated_lan"),Isolated);
    }
    if(!Enabled) {UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SWITCH_DISABLED by local configuration"));return false;}
    bUnsigned=Secret.IsEmpty()&&Unsigned&&Isolated;
    if(Secret.IsEmpty()&&!bUnsigned) {UE_LOG(LogHapbeatDemoSession,Warning,TEXT("DEMO_SWITCH_DISABLED authentication is not configured"));return false;}
    if(!LoadReplayState()) {UE_LOG(LogHapbeatDemoSession,Error,TEXT("DEMO_SWITCH_DISABLED replay state is unreadable"));return false;}
    bEnabled=true;
    if(bUnsigned) UE_LOG(LogHapbeatDemoSession,Warning,TEXT("DEMO_SWITCH_UNSIGNED isolated demonstration LAN only"));
    UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SWITCH_CONFIGURED demo=%s mode=%s"),*DemoId,bTest?TEXT("loopback test"):TEXT("runtime"));
    return true;
}
bool FHapbeatDemoSwitchReceiver::LoadReplayState()
{
    if(!FPaths::FileExists(StateFile)) return true;
    FString Text;TSharedPtr<FJsonObject> State;
    if(IFileManager::Get().FileSize(*StateFile)>16384 || !FFileHelper::LoadFileToString(Text,*StateFile)
        || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text),State) || !State.IsValid() || State->Values.Num()>32) return false;
    for(const auto& Pair:State->Values) {
        if(!HapbeatDemoSwitchProtocol::IsIdentifier(Pair.Key)||Pair.Value->Type!=EJson::Number) return false;
        const double Value=Pair.Value->AsNumber();
        if(!FMath::IsFinite(Value)||Value<1||Value>9007199254740991.0||FMath::FloorToDouble(Value)!=Value) return false;
        Sequences.Add(Pair.Key,static_cast<int64>(Value));
    }
    return true;
}
bool FHapbeatDemoSwitchReceiver::Reserve(const FString& ControllerId,int64 Sequence)
{
    if(!Sequences.Contains(ControllerId)&&Sequences.Num()>=32) return false;
    auto State=MakeShared<FJsonObject>();
    for(const auto& Pair:Sequences) State->SetNumberField(Pair.Key,static_cast<double>(Pair.Value));
    State->SetNumberField(ControllerId,static_cast<double>(Sequence));
    FString Text;FJsonSerializer::Serialize(State,TJsonWriterFactory<>::Create(&Text));
    const FString Temp=StateFile+TEXT(".tmp");
    if(!IFileManager::Get().MakeDirectory(*FPaths::GetPath(StateFile),true)
        || !FFileHelper::SaveStringToFile(Text,*Temp,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)
        || !IFileManager::Get().Move(*StateFile,*Temp,true,true)) return false;
    Sequences.Add(ControllerId,Sequence);return true;
}
void FHapbeatDemoSwitchReceiver::Close()
{
    if(Socket) {Socket->Close();ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM)->DestroySocket(Socket);Socket=nullptr;}
    if(bMulticastLock) {HapbeatDemoSessionPlatform::SetMulticastLock(false);bMulticastLock=false;}
}
void FHapbeatDemoSwitchReceiver::Stop()
{
    if(Socket) UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SWITCH_STOPPED"));
    Close();bEnabled=false;Secret.Empty();PendingSource.Reset();
}
void FHapbeatDemoSwitchReceiver::FinishPending(bool bSuccess,const FString& LaunchedDemoId,const FString& Text)
{
    if(!PendingSource.IsValid()) return;
    const TSharedPtr<FInternetAddr> Destination=MoveTemp(PendingSource);
    UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SWITCH_CONTROL action=%s ok=%d launched=%s"),*PendingMessage.Action,bSuccess,*LaunchedDemoId);
    Send(bSuccess?HapbeatDemoSwitchProtocol::Status(PendingMessage,LaunchedDemoId,TEXT("READY"),TEXT("ok"),Secret)
        :HapbeatDemoSwitchProtocol::Status(PendingMessage,DemoId,TEXT("FAILED"),TEXT("launch_failed"),Secret,Text),*Destination);
}
void FHapbeatDemoSwitchReceiver::Send(const FString& Payload,const FInternetAddr& Destination)
{
    if(!Socket||Payload.IsEmpty()) return;
    const FTCHARToUTF8 Bytes(*Payload);int32 Sent=0;
    Socket->SendTo(reinterpret_cast<const uint8*>(Bytes.Get()),Bytes.Length(),Sent,Destination);
}
void FHapbeatDemoSwitchReceiver::Tick()
{
    if(!bEnabled) return;
    // Not foreground (boundary setup, system menu): still bound and answering DISCOVER / QUERY, commands refused.
    const bool Foreground=bTest||(FApp::UseVRFocus()?FApp::HasVRFocus():(FSlateApplication::IsInitialized()&&FSlateApplication::Get().IsActive()));
    if(Foreground!=bForeground) {bForeground=Foreground;UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SWITCH_FOREGROUND %d"),Foreground);}
    auto* Sockets=ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM);
    const double Now=FPlatformTime::Seconds();
    if(!Socket&&!bBindFailed&&Now>=NextBindAt) {
        // The previous application's socket can still hold the port for a moment after a hand-over.
        constexpr double RetryInterval=0.5,RetryWindow=5.0;
        Socket=Sockets->CreateSocket(NAME_DGram,TEXT("Hapbeat Demo Switch"),false);
        auto Address=Sockets->CreateInternetAddr();
        if(bTest) {bool Valid=false;Address->SetIp(TEXT("127.0.0.1"),Valid);}
        else Address->SetAnyAddress();
        Address->SetPort(bTest?17710:7710);
        if(!Socket || !Socket->SetReuseAddr(false) || !Socket->SetNonBlocking(true) || !Socket->Bind(*Address)) {
            Close();
            if(BindFailedAt==0) BindFailedAt=Now;
            if(Now-BindFailedAt>=RetryWindow) {bBindFailed=true;UE_LOG(LogHapbeatDemoSession,Error,TEXT("DEMO_SWITCH_LISTENER_FAILED port is unavailable"));}
            else {NextBindAt=Now+RetryInterval;UE_LOG(LogHapbeatDemoSession,Warning,TEXT("DEMO_SWITCH_BIND_RETRY port is unavailable, retrying"));}
            return;
        }
        BindFailedAt=NextBindAt=0;
        if(!bTest) bMulticastLock=HapbeatDemoSessionPlatform::SetMulticastLock(true);
        UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SWITCH_LISTENING port=%d demo=%s multicast_lock=%d"),bTest?17710:7710,*DemoId,bMulticastLock);
    }
    if(!Socket) return;
    for(int32 Count=0;Count<8&&Socket;++Count) {
        uint32 Pending=0;if(!Socket->HasPendingData(Pending)) break;
        TArray<uint8> Bytes;Bytes.SetNumUninitialized(1025);
        auto Source=Sockets->CreateInternetAddr();int32 Received=0;
        if(!Socket->RecvFrom(Bytes.GetData(),Bytes.Num(),Received,*Source)||Pending>1024||Received<1||Received>1024) continue;
        Bytes.SetNum(Received);
        FHapbeatDemoSwitchMessage M;if(!HapbeatDemoSwitchProtocol::Parse(Bytes,M)) continue;
        auto Status=[&](const TCHAR* Type,const TCHAR* Code,const TCHAR* Text=TEXT("")){Send(HapbeatDemoSwitchProtocol::Status(M,DemoId,Type,Code,Secret,Text),*Source);};
        if(!HapbeatDemoSwitchProtocol::Authenticate(M,Secret,bUnsigned)) {
            if(M.Type!=TEXT("DISCOVER")&&M.Type!=TEXT("QUERY")) Status(TEXT("FAILED"),TEXT("invalid_auth"));
            continue;
        }
        if(M.Type==TEXT("DISCOVER")) {Send(HapbeatDemoSwitchProtocol::Here(M,DemoId,Secret),*Source);continue;}
        // QUERY changes nothing: no sequence, answered by unicast like DISCOVER.
        if(M.Type==TEXT("QUERY")) {
            if(!GetState) continue;
            FHapbeatDemoSwitchState State=GetState();State.bForeground=bForeground;
            Send(HapbeatDemoSwitchProtocol::State(M,DemoId,State,Secret),*Source);continue;
        }
        if(!bForeground) {Status(TEXT("FAILED"),TEXT("not_allowed"),TEXT("not in foreground"));continue;}
        // One operation at a time: nothing else while a session_next waits for the hand-over.
        if(PendingSource.IsValid()) {Status(TEXT("FAILED"),TEXT("not_allowed"),TEXT("a launch is in progress"));continue;}
        // Launching another application (SWITCH) is not part of this receiver.
        const bool Allowed=M.Type==TEXT("CONTROL")&&M.DemoId==DemoId&&IsAllowed&&IsAllowed(M.Action);
        if(!Allowed) {Status(TEXT("FAILED"),TEXT("not_allowed"));continue;}
        if(M.Sequence<=Sequences.FindRef(M.ControllerId)) {Status(TEXT("FAILED"),TEXT("replay"));continue;}
        if(!Reserve(M.ControllerId,M.Sequence)) {Status(TEXT("FAILED"),TEXT("launch_failed"));continue;}
        Status(TEXT("ACK"),TEXT("ok"));
        // Pending before Execute: the hand-over may already end inside it (FinishPending).
        PendingMessage=M;PendingSource=Source;
        const EHapbeatControlResult Result=Execute?Execute(M.Action):EHapbeatControlResult::Failed;
        if(Result==EHapbeatControlResult::Pending) {
            UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SWITCH_CONTROL action=%s pending"),*M.Action);
            continue;
        }
        PendingSource.Reset();
        const bool Success=Result==EHapbeatControlResult::Ready;
        UE_LOG(LogHapbeatDemoSession,Display,TEXT("DEMO_SWITCH_CONTROL action=%s ok=%d"),*M.Action,Success);
        Status(Success?TEXT("READY"):TEXT("FAILED"),Success?TEXT("ok"):TEXT("launch_failed"));
    }
}
