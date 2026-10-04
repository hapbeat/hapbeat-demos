using System.Collections;

namespace Hapbeat.DemoSwitch
{
    /// <summary>Scene-local adapter. The shared receiver owns authentication and operation lifetime.</summary>
    public interface IDemoAppControls
    {
        bool CanExecuteControl(string action, string sceneId);
        IEnumerator ExecuteControl(string action, string sceneId);
    }

    /// <summary>
    /// Optional, on an enabled scene MonoBehaviour (e.g. the <see cref="IDemoAppControls"/> adapter of a demo with
    /// its own menu): whether that menu currently pauses the demo. Reported as STATE `paused` together with the shared pause.
    /// </summary>
    public interface IDemoAppMenuState
    {
        bool IsMenuOpen { get; }
    }
}
