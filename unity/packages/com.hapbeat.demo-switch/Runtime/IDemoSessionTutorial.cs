namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// Optional, next to <see cref="IDemoSessionHost"/> on the registered scene host: the demo's tutorial, started
    /// by CONTROL `tutorial_start`. Without it the runtime rejects `tutorial_start` with `FAILED/not_allowed`.
    /// Before the call the package closes the shared pause and the completion panel (gameplay is resumed).
    /// </summary>
    public interface IDemoSessionTutorial
    {
        /// <summary>Starts the tutorial from its beginning, also while it is running or after it finished; with or without a session.</summary>
        void StartTutorial();
    }
}
