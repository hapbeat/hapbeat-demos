#include "HapbeatDemoSessionDeviceAddress.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

// Names are unique within the module: unity builds merge this file with HapbeatDemoSessionTicket.cpp.
namespace
{
    bool RejectAddress(FString& Error,const TCHAR* What) {Error=What;return false;}
    /** An integer JSON number. */
    bool GetAddressInteger(const TSharedPtr<FJsonObject>& O,const TCHAR* Name,int32& Out)
    {
        const TSharedPtr<FJsonValue>* V=O->Values.Find(Name);
        if(!V||!V->IsValid()||(*V)->Type!=EJson::Number) return false;
        const double D=(*V)->AsNumber();
        if(!FMath::IsFinite(D)||FMath::FloorToDouble(D)!=D||FMath::Abs(D)>1000) return false;
        Out=static_cast<int32>(D);return true;
    }
    bool IsAddressAxis(int32 V) {return V==FHapbeatDeviceAddress::Unchanged||(V>=1&&V<=99);}
}

bool FHapbeatDeviceAddress::Parse(const FString& Json,FHapbeatDeviceAddress& Out,FString& Error)
{
    if(FTCHARToUTF8(*Json).Length()>MaxJsonBytes) return RejectAddress(Error,TEXT("larger than 1024 bytes"));
    TSharedPtr<FJsonObject> O;
    if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Json),O)||!O.IsValid()) return RejectAddress(Error,TEXT("not a JSON object"));
    for(const auto& Pair:O->Values)
        if(Pair.Key!=TEXT("version")&&Pair.Key!=TEXT("player")&&Pair.Key!=TEXT("group")) return RejectAddress(Error,TEXT("unknown field"));
    int32 Version=0;FHapbeatDeviceAddress A;
    if(!GetAddressInteger(O,TEXT("version"),Version)||Version!=1) return RejectAddress(Error,TEXT("version"));
    if(!GetAddressInteger(O,TEXT("player"),A.Player)||!IsAddressAxis(A.Player)) return RejectAddress(Error,TEXT("player"));
    if(!GetAddressInteger(O,TEXT("group"),A.Group)||!IsAddressAxis(A.Group)) return RejectAddress(Error,TEXT("group"));
    Out=A;return true;
}
