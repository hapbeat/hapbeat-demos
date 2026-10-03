#pragma once
#include "CoreMinimal.h"

/**
 * Hand-over to the runtime this demo has just started (次へ / デモを終了 / Hub に戻る).
 *
 * Ending this task right after startActivity lets Quest switch to its home environment, which then wins over the
 * runtime that is starting (logcat 2026-10-03: the Hub was sent to the background, and finish ran twice). So
 * after a successful launch this waits until this application has gone to the background (VR focus lost or the
 * activity paused) and only then reports Exit, once. Still in front after TimeoutSeconds: Failed, and the demo
 * stays (the panel shows an error).
 */
struct HAPBEATDEMOSESSION_API FHapbeatLaunchHandoff
{
    static constexpr float TimeoutSeconds=5.f;
    enum class EStep : uint8
    {
        None,
        /** Backgrounded: stop haptics, sound and the 7710 listener and end the task (reported once). */
        Exit,
        /** Not backgrounded within TimeoutSeconds: the launch failed, the demo stays. */
        Failed,
    };
    /** After a successful launch. Ignored while waiting or after Exit. */
    void Start();
    /** One frame; bBackgrounded: this application has lost focus / was paused since Start. */
    EStep Update(bool bBackgrounded,float Dt);
    bool IsWaiting() const {return State==EState::Waiting;}
    /** Exit has been reported: nothing starts again. */
    bool IsDone() const {return State==EState::Done;}
private:
    enum class EState : uint8 {Idle,Waiting,Done};
    EState State=EState::Idle;
    float Seconds=0;
};
