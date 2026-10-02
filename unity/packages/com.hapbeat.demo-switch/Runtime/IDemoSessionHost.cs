using System.Collections.Generic;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// Scene-local Demo Session adapter (one per scene). Call <see cref="DemoSession.RegisterHost"/> from
    /// <c>Start</c> and <see cref="DemoSession.UnregisterHost"/> when destroyed. On registration the session
    /// pushes the current haptics state and, in session mode, the step options. When the demo's own
    /// experience completes in session mode, call <see cref="DemoSession.ShowCompletion"/>.
    /// </summary>
    public interface IDemoSessionHost
    {
        /// <summary>Normalized active options of the current step. Called only in session mode.</summary>
        void ApplyOptions(IReadOnlyDictionary<string, string> options);

        /// <summary>Restarts the same step inside the demo ("もう一度"), not the OS process.</summary>
        void Restart();

        /// <summary>Hapbeat output only; sound and visuals stay unchanged. Off stops playing loops/streams.</summary>
        void SetHapticsEnabled(bool enabled);

        /// <summary>True while the completion panel owns input; gameplay input must stop.</summary>
        void SetGameplayPaused(bool paused);
    }
}
