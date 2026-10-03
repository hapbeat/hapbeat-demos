#include "HapbeatDemoSessionHandoff.h"

void FHapbeatLaunchHandoff::Start()
{
    if(State!=EState::Idle) return;
    State=EState::Waiting;Seconds=0;
}

FHapbeatLaunchHandoff::EStep FHapbeatLaunchHandoff::Update(bool bBackgrounded,float Dt)
{
    if(State!=EState::Waiting) return EStep::None;
    if(bBackgrounded) {State=EState::Done;return EStep::Exit;}
    Seconds+=Dt;
    if(Seconds<TimeoutSeconds) return EStep::None;
    State=EState::Idle;
    return EStep::Failed;
}
