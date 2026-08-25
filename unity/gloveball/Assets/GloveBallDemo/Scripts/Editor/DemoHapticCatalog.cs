using GloveBallDemo.Runtime;

namespace GloveBallDemo.Editor
{
    /// <summary>One haptic event: its Kit clip, its EventMap entry and its default gain.</summary>
    public readonly struct DemoHapticDefinition
    {
        /// <summary>Gameplay event this haptic answers.</summary>
        public readonly DemoHapticEvent Event;

        /// <summary>Event name inside the kit. Left/right events keep distinct names.</summary>
        public readonly string Name;

        /// <summary>Shared mono clip name inside the kit, without the .wav extension.</summary>
        public readonly string ClipName;

        public readonly string DisplayName;

        /// <summary>Clip borrowed from the SDK's showcase kit, by file name.</summary>
        public readonly string SourceClip;

        /// <summary>Contracts-canonical device target for this event.</summary>
        public readonly string Target;

        public readonly float Gain;

        public readonly bool Loop;

        public readonly string Notes;

        public DemoHapticDefinition(
            DemoHapticEvent evt,
            string name,
            string clipName,
            string displayName,
            string sourceClip,
            string target,
            float gain,
            bool loop,
            string notes)
        {
            Event = evt;
            Name = name;
            ClipName = clipName;
            DisplayName = displayName;
            SourceClip = sourceClip;
            Target = target;
            Gain = gain;
            Loop = loop;
            Notes = notes;
        }
    }

    /// <summary>
    /// The demo's haptic vocabulary, in one place: the kit builder, the EventMap builder and
    /// the scene wiring all read this table, so a new event cannot end up half-wired.
    ///
    /// Every entry is StreamClip. Streaming the clip means the device needs no Kit installed
    /// beforehand, which is what lets an exhibition unit be swapped for a spare and just work.
    /// </summary>
    public static class DemoHapticCatalog
    {
        /// <summary>Kit name, and therefore the event-id category (DEC-040: kit-name.file-name).</summary>
        public const string KitName = "gloveball-kit";

        /// <summary>Clips are borrowed from this SDK sample kit.</summary>
        public const string SourceKitRelativePath = "Samples~/Showcase/Kit/showcase-kit/stream-clips";

        public const string LeftWristTarget = "*/pos_l_wrist";
        public const string RightWristTarget = "*/pos_r_wrist";
        public const string NeckTarget = "*/pos_neck";
        public const string RightAnkleTarget = "*/pos_r_ankle";

        public static readonly DemoHapticDefinition[] Definitions =
        {
            new DemoHapticDefinition(DemoHapticEvent.LeftGrab, "l_grab", "grab", "Left Grab",
                "z1_pin_hit.wav", LeftWristTarget, 0.7f, false,
                "Left hand grip confirmation."),

            new DemoHapticDefinition(DemoHapticEvent.RightGrab, "r_grab", "grab", "Right Grab",
                "z1_pin_hit.wav", RightWristTarget, 0.7f, false, "Right hand grip confirmation."),

            new DemoHapticDefinition(DemoHapticEvent.LeftRelease, "l_release", "release", "Left Release",
                "z5_shot_light.wav", LeftWristTarget, 0.5f, false, "Left hand release."),

            new DemoHapticDefinition(DemoHapticEvent.RightRelease, "r_release", "release", "Right Release",
                "z5_shot_light.wav", RightWristTarget, 0.5f, false, "Right hand release."),

            new DemoHapticDefinition(DemoHapticEvent.LeftArmCollide, "l_arm_collide", "arm_collide", "Left Arm Collide",
                "z5_tar_hit_light.wav", LeftWristTarget, 0.7f, false, "Incoming ball reflected by left arm."),

            new DemoHapticDefinition(DemoHapticEvent.RightArmCollide, "r_arm_collide", "arm_collide", "Right Arm Collide",
                "z5_tar_hit_light.wav", RightWristTarget, 0.7f, false, "Incoming ball reflected by right arm."),

            new DemoHapticDefinition(DemoHapticEvent.BodyCollide, "body_collide", "body_collide", "Body Collide",
                "z5_shot_heavy.wav", NeckTarget, 1.0f, false,
                "The hardest hit in the demo: taking a ball to the body resets the combo."),

            new DemoHapticDefinition(DemoHapticEvent.LeftChargeLoop, "l_charge_loop", "charge_loop", "Left Charge Loop",
                "z5_charge_loop.wav", LeftWristTarget, 0.4f, true, "Left trigger charge loop."),

            new DemoHapticDefinition(DemoHapticEvent.RightChargeLoop, "r_charge_loop", "charge_loop", "Right Charge Loop",
                "z5_charge_loop.wav", RightWristTarget, 0.4f, true, "Right trigger charge loop."),

            new DemoHapticDefinition(DemoHapticEvent.TargetHit, "target_hit", "target_hit", "Target Hit",
                "z5_tar_hit_light.wav", NeckTarget, 0.6f, false,
                "Bright hit that reads as a score."),

            new DemoHapticDefinition(DemoHapticEvent.LauncherHit, "launcher_hit", "launcher_hit", "Launcher Hit",
                "z5_tar_hit_heavy.wav", RightAnkleTarget, 0.8f, false,
                "Heavier than a panel hit: launchers are worth more and take three to stun."),

            new DemoHapticDefinition(DemoHapticEvent.LauncherStunned, "launcher_stunned", "launcher_stunned", "Launcher Stunned",
                "z2_door_slam.wav", RightAnkleTarget, 0.6f, false,
                "The reward moment when a launcher goes quiet for five seconds."),

            new DemoHapticDefinition(DemoHapticEvent.BallIncomingWarning, "incoming_warn", "incoming_warn", "Incoming Warning",
                "z4_slider_tick.wav", RightAnkleTarget, 0.3f, false,
                "Fires 0.6 s before a shot. Weak on purpose - a cue, not an event."),

            new DemoHapticDefinition(DemoHapticEvent.WaveStarted, "wave_start", "wave_start", "Wave Start",
                "z2_door_unlock.wav", NeckTarget, 0.5f, false, "Wave transition jingle."),

            new DemoHapticDefinition(DemoHapticEvent.WaveCleared, "wave_clear", "wave_clear", "Wave Clear",
                "z2_door_open.wav", NeckTarget, 0.5f, false, "Wave transition jingle."),

            new DemoHapticDefinition(DemoHapticEvent.GameOver, "game_over", "game_over", "Game Over",
                "z2_door_close.wav", NeckTarget, 0.5f, false, "End of run."),

        };

        /// <summary>Contracts-canonical event id for a definition (kit-name.file-name).</summary>
        public static string EventId(DemoHapticDefinition definition) => $"{KitName}.{definition.Name}";
    }
}
