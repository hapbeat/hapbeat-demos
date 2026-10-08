using System;
using System.Collections;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// CONTROL `hand_style_ghost` / `hand_style_skin`: the shared hands (<see cref="DemoHands.Instance"/>). Refused without them,
    /// and `hand_style_skin` while they have only the ghost look (no Meta hand mesh). On the Hub the Hub-wide setting changes
    /// (<see cref="IDemoSwitchHubHost.TrySetHandStyle"/>, as its manage screen); elsewhere only the hands of this runtime, which
    /// the following session steps inherit (<see cref="DemoSession.LaunchNext"/>).
    /// </summary>
    internal sealed class DemoHandStyleControls : IDemoAppControls
    {
        private readonly IDemoSwitchHubHost _hub;

        public DemoHandStyleControls(IDemoSwitchHubHost hub)
        {
            _hub = hub;
        }

        public bool CanExecuteControl(string action, string sceneId)
        {
            var hands = DemoHands.Instance;
            return sceneId == string.Empty && hands != null &&
                   (action == "hand_style_ghost" || (action == "hand_style_skin" && hands.UsesMetaModel));
        }

        public IEnumerator ExecuteControl(string action, string sceneId)
        {
            if (!CanExecuteControl(action, sceneId)) throw new InvalidOperationException("No shared hands with this look.");
            var style = action == "hand_style_skin" ? DemoHandStyle.Skin : DemoHandStyle.Ghost;
            if (_hub == null) DemoHands.Instance.SetStyle(style);
            else if (!_hub.TrySetHandStyle(style, out var error)) throw new InvalidOperationException(error);
            yield break;
        }
    }

    /// <summary>CONTROL `session_retry`: the completion panel's もう一度, only while the panel offers it (the step's `retry`).</summary>
    internal sealed class DemoSessionRetryControls : IDemoAppControls
    {
        public bool CanExecuteControl(string action, string sceneId) =>
            action == "session_retry" && sceneId == string.Empty && DemoSession.IsCompletionShown &&
            DemoSession.CurrentStep != null && DemoSession.CurrentStep.Retry;

        public IEnumerator ExecuteControl(string action, string sceneId)
        {
            if (!CanExecuteControl(action, sceneId)) throw new InvalidOperationException("The completion panel does not offer もう一度.");
            DemoSession.RetryFromCompletion();
            yield break;
        }
    }

    /// <summary>CONTROL `hub_top`: the Hub's top screen; accepted on every Hub screen, including the manage screen.</summary>
    internal sealed class DemoHubTopControls : IDemoAppControls
    {
        private readonly IDemoSwitchHubHost _hub;

        public DemoHubTopControls(IDemoSwitchHubHost hub)
        {
            _hub = hub;
        }

        public bool CanExecuteControl(string action, string sceneId) => action == "hub_top" && sceneId == string.Empty && _hub != null;

        public IEnumerator ExecuteControl(string action, string sceneId)
        {
            if (!CanExecuteControl(action, sceneId)) throw new InvalidOperationException("Only the Hub has a top screen.");
            _hub.ShowTop();
            yield break;
        }
    }
}
