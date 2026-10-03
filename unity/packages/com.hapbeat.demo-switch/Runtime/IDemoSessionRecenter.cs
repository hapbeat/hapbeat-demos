namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// Optional, next to <see cref="IDemoSessionHost"/> on the registered scene host: the demo's own start
    /// alignment for 視線をリセット (the in-view button and CONTROL `recenter`). Without it the package
    /// re-runs the scene's <see cref="XrStartAlignment"/>, or else turns and moves the XR Origin so the head
    /// is at the origin's start pose (<see cref="DemoRecenter.ResetView"/>).
    /// </summary>
    public interface IDemoSessionRecenter
    {
        /// <summary>
        /// Takes the participant's current head position and heading as the demo's start position and front
        /// (app-space; floor height unchanged). The open shared panels are placed in front afterwards by the package.
        /// </summary>
        void RecenterToStart();
    }
}
