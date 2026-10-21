namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// Optional, next to <see cref="IDemoSessionHost"/> on the registered scene host of a demo with its own floor tracking (a
    /// component that writes the camera offset's Y every frame): its eye height correction. Without it the package moves the
    /// XR Origin vertically (<see cref="DemoEyeHeight"/>), which such a demo would overwrite or add to.
    /// </summary>
    public interface IDemoSessionHeight
    {
        /// <summary>
        /// Sets the demo's own height correction so the head is now <paramref name="target"/> metres above the demo's floor
        /// (<see cref="DemoEyeHeight.Correction"/> gives the change). Called by 視線をリセット, a system recenter and the first
        /// tracked frame of a scene; called again it must keep the result (no accumulation).
        /// </summary>
        void ApplyEyeHeight(float target);
    }
}
