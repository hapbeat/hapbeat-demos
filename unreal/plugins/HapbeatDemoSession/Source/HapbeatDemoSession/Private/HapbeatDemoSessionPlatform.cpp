#include "HapbeatDemoSessionPlatform.h"
#include "HapbeatDemoSessionTicket.h"
#include "HapbeatDemoSessionDeviceAddress.h"
#include "HapbeatDemoSessionLog.h"
#include "Misc/CommandLine.h"
#include "Misc/FileHelper.h"
#include "Misc/Parse.h"
#include "Misc/Paths.h"
#include "HAL/FileManager.h"
#if PLATFORM_ANDROID
#include "Android/AndroidJNI.h"
#include "Android/AndroidApplication.h"
#include "Android/AndroidJavaEnv.h"
#endif

namespace
{
#if !PLATFORM_ANDROID
    bool LoadSmallFile(const FString& Path,FString& Out,int64 MaxBytes=HapbeatDemoSession::MaxJsonBytes)
    {
        return FPaths::FileExists(Path)&&IFileManager::Get().FileSize(*Path)<=MaxBytes&&FFileHelper::LoadFileToString(Out,*Path);
    }
#else
    jmethodID Method(JNIEnv* Env,const ANSICHAR* Name,const ANSICHAR* Signature)
    {
        return FJavaWrapper::FindMethod(Env,FJavaWrapper::GameActivityClassID,Name,Signature,false);
    }
    bool CallString(const ANSICHAR* Name,FString& Out)
    {
        JNIEnv* Env=FAndroidApplication::GetJavaEnv();
        if(!Env) return false;
        const jmethodID M=Method(Env,Name,"()Ljava/lang/String;");
        if(!M) return false;
        jstring Value=static_cast<jstring>(FJavaWrapper::CallObjectMethod(Env,FJavaWrapper::GameActivityThis,M));
        if(!Value) return false;
        Out=FJavaHelper::FStringFromLocalRef(Env,Value);
        return true;
    }
#endif
}

bool HapbeatDemoSessionPlatform::ReadDescriptor(FString& Out)
{
#if PLATFORM_ANDROID
    return CallString("AndroidThunkJava_HapbeatSession_ReadDescriptor",Out);
#else
    return LoadSmallFile(FPaths::ProjectConfigDir()/TEXT("HapbeatDemoSession/hapbeat-demo-session.json"),Out);
#endif
}

bool HapbeatDemoSessionPlatform::TakeTicket(FString& Out)
{
#if PLATFORM_ANDROID
    return CallString("AndroidThunkJava_HapbeatSession_TakeTicket",Out);
#elif !UE_BUILD_SHIPPING
    // Desktop verification only: the same JSON a Quest launch would carry in its Intent extra.
    FString File;
    if(!FParse::Value(FCommandLine::Get(),TEXT("HapbeatSessionTicketFile="),File)) return false;
    if(!LoadSmallFile(File,Out)) {UE_LOG(LogHapbeatDemoSession,Warning,TEXT("DEMO_SESSION_TICKET unreadable file %s"),*File);Out.Empty();}
    return true;
#else
    return false;
#endif
}

bool HapbeatDemoSessionPlatform::ReadDeviceAddress(FString& Out,FString& Source)
{
#if PLATFORM_ANDROID
    if(!CallString("AndroidThunkJava_HapbeatSession_ReadDeviceAddress",Out)) return false;
    if(!CallString("AndroidThunkJava_HapbeatSession_DeviceAddressPath",Source)) Source=TEXT("hapbeat-device.json");
    return true;
#elif !UE_BUILD_SHIPPING
    // Desktop verification only: the file install-demos.ps1 pushes to a Quest.
    if(!FParse::Value(FCommandLine::Get(),TEXT("HapbeatDeviceAddressFile="),Source)) return false;
    if(!FPaths::FileExists(Source)) return false;
    // An oversized or unreadable file stays present with no content, so the caller rejects it with a warning.
    if(!LoadSmallFile(Source,Out,FHapbeatDeviceAddress::MaxJsonBytes)) Out.Empty();
    return true;
#else
    return false;
#endif
}

bool HapbeatDemoSessionPlatform::CanLaunch()
{
    return PLATFORM_ANDROID!=0;
}

bool HapbeatDemoSessionPlatform::Launch(const FHapbeatDemoSessionComponent& Target,const FString& Ticket)
{
#if PLATFORM_ANDROID
    JNIEnv* Env=FAndroidApplication::GetJavaEnv();
    if(!Env) return false;
    const jmethodID M=Method(Env,"AndroidThunkJava_HapbeatSession_Launch","(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;)Z");
    if(!M) return false;
    auto Package=FJavaHelper::ToJavaString(Env,Target.Package);
    auto Activity=FJavaHelper::ToJavaString(Env,Target.Activity);
    auto Json=FJavaHelper::ToJavaString(Env,Ticket);
    return FJavaWrapper::CallBooleanMethod(Env,FJavaWrapper::GameActivityThis,M,*Package,*Activity,*Json);
#else
    return false;
#endif
}

bool HapbeatDemoSessionPlatform::IsInstalled(const FString& Package)
{
#if PLATFORM_ANDROID
    JNIEnv* Env=FAndroidApplication::GetJavaEnv();
    if(!Env) return false;
    const jmethodID M=Method(Env,"AndroidThunkJava_HapbeatSession_IsInstalled","(Ljava/lang/String;)Z");
    if(!M) return false;
    auto Name=FJavaHelper::ToJavaString(Env,Package);
    return FJavaWrapper::CallBooleanMethod(Env,FJavaWrapper::GameActivityThis,M,*Name);
#else
    return false;
#endif
}

bool HapbeatDemoSessionPlatform::SetMulticastLock(bool bHeld)
{
#if PLATFORM_ANDROID
    JNIEnv* Env=FAndroidApplication::GetJavaEnv();
    if(!Env) return false;
    const jmethodID M=Method(Env,"AndroidThunkJava_HapbeatSession_SetMulticastLock","(Z)Z");
    if(!M) return false;
    return FJavaWrapper::CallBooleanMethod(Env,FJavaWrapper::GameActivityThis,M,static_cast<jboolean>(bHeld));
#else
    return false;
#endif
}

void HapbeatDemoSessionPlatform::FinishTask()
{
#if PLATFORM_ANDROID
    if(JNIEnv* Env=FAndroidApplication::GetJavaEnv())
        if(const jmethodID M=Method(Env,"AndroidThunkJava_HapbeatSession_FinishTask","()V"))
            FJavaWrapper::CallVoidMethod(Env,FJavaWrapper::GameActivityThis,M);
#endif
}

FString HapbeatDemoSessionPlatform::DeviceModel()
{
#if PLATFORM_ANDROID
    return (FAndroidMisc::GetDeviceMake()+TEXT(" ")+FAndroidMisc::GetDeviceModel()).TrimStartAndEnd();
#else
    return FPlatformMisc::GetDeviceMakeAndModel().Replace(TEXT("|"),TEXT(" ")).TrimStartAndEnd();
#endif
}
