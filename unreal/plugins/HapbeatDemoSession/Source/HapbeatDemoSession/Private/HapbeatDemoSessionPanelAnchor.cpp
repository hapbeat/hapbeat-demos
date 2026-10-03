#include "HapbeatDemoSessionPanelAnchor.h"
#include "UObject/UObjectIterator.h"

UHapbeatDemoSessionPanelAnchor* UHapbeatDemoSessionPanelAnchor::Find(const UWorld* World)
{
    if(!World) return nullptr;
    for(TObjectIterator<UHapbeatDemoSessionPanelAnchor> It;It;++It)
        if(It->GetWorld()==World&&It->IsRegistered()&&!It->IsTemplate()) return *It;
    return nullptr;
}
