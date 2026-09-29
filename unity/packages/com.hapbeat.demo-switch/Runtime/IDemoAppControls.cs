using System.Collections;

namespace Hapbeat.DemoSwitch
{
    /// <summary>Scene-local adapter. The shared receiver owns authentication and operation lifetime.</summary>
    public interface IDemoAppControls
    {
        bool CanExecuteControl(string action, string sceneId);
        IEnumerator ExecuteControl(string action, string sceneId);
    }
}
