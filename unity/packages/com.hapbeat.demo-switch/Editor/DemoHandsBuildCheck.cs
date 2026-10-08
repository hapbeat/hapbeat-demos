using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hapbeat.DemoSwitch.Editor
{
    /// <summary>
    /// Stops a player build whose Demo Switch settings draw the shared hands (Hands flag) while Meta's hand mesh
    /// is not in the project: without the private assets link (a worktree or a clone where the project's
    /// tools/link-*.ps1 did not run) the build would ship the procedural ghost hands without a word. A public
    /// clone that has no private assets sets <see cref="AllowProceduralVariable"/>=1 to build with them.
    /// </summary>
    internal sealed class DemoHandsBuildCheck : IPreprocessBuildWithReport
    {
        public const string AllowProceduralVariable = "HAPBEAT_DEMO_PROCEDURAL_HANDS";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = Resources.Load<DemoSwitchSettings>(DemoSwitchSettings.ResourceName);
            var error = Check(settings != null && settings.Hands, MetaHandsPresent(), Environment.GetEnvironmentVariable(AllowProceduralVariable));
            if (error != null) throw new BuildFailedException(error);
        }

        internal static bool MetaHandsPresent() =>
            Resources.Load<GameObject>(DemoHands.MetaLeftModelPath) != null && Resources.Load<GameObject>(DemoHands.MetaRightModelPath) != null;

        /// <summary>The build error, or null when the build may go on.</summary>
        internal static string Check(bool hands, bool metaHandsPresent, string allowProcedural)
        {
            if (!hands || metaHandsPresent) return null;
            if (allowProcedural == "1")
            {
                Debug.LogWarning("[Demo Hands] Building with the procedural ghost hands (" + AllowProceduralVariable + "=1; Meta hand meshes not in this project).");
                return null;
            }
            return "[Demo Hands] Meta hand meshes (Resources/" + DemoHands.MetaLeftModelPath + ") are not in this project, so the build would draw the procedural ghost hands. "
                + "Link the private assets (Assets/HapbeatPrivate, the project's tools/link-*.ps1; in a git worktree pass -WorkspaceRoot of the main checkout) "
                + "and build again, or set " + AllowProceduralVariable + "=1 for a build without the private assets.";
        }
    }
}
