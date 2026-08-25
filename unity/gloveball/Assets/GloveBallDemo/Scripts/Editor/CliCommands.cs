// Unity CLI (com.unity.pipeline) wrappers around the demo's batch entry points.
//
// tools/run-unity.ps1 boots a private batch editor, which cannot run while the project is open
// in the interactive editor (Unity holds an exclusive project lock). These [CliCommand] methods
// expose the same operations over the Pipeline server of an already-open editor, so
// "unity command gb_compile_check" works without closing the editor first.
//
// Trial status: com.unity.pipeline is an experimental package. run-unity.ps1 remains the
// supported path; if the package is dropped, delete this file and nothing else changes.
using Unity.Pipeline.Commands;

namespace GloveBallDemo.Editor
{
    public static class CliCommands
    {
        [CliCommand("gb_compile_check", "Refresh the AssetDatabase and report asset counts (BatchOps.CompileCheck).",
            Tags = new[] { "gloveball" })]
        public static string CompileCheck()
        {
            BatchOps.CompileCheck();
            return "ok";
        }

        [CliCommand("gb_scene_health", "Report lightmaps, error-shader renderers and missing meshes for a scene (BatchOps.ReportSceneHealth).",
            Tags = new[] { "gloveball" })]
        public static string ReportSceneHealth()
        {
            BatchOps.ReportSceneHealth();
            return "ok";
        }

        [CliCommand("gb_build_demo", "Regenerate the demo's input actions, prefabs, haptics and scene (BatchOps.BuildDemo).",
            Tags = new[] { "gloveball" })]
        public static string BuildDemo()
        {
            BatchOps.BuildDemo();
            return "ok";
        }

        [CliCommand("gb_input_backends", "Report activeInputHandler and the input-backend defines (BatchOps.ReportInputBackends).",
            Tags = new[] { "gloveball" })]
        public static string ReportInputBackends()
        {
            BatchOps.ReportInputBackends();
            return "ok";
        }

        [CliCommand("gb_configure_air_link", "Configure Windows OpenXR for Meta Horizon Link Play Mode.",
            Tags = new[] { "gloveball", "xr" })]
        public static string ConfigureAirLink()
        {
            DemoAirLinkBuilder.ConfigureAirLinkPlayMode();
            return "Windows Standalone OpenXR configured";
        }
    }
}
