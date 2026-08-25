namespace GloveBallDemo.Editor
{
    /// <summary>Single place for every path the build scripts touch.</summary>
    public static class DemoAssetPaths
    {
        public const string Root = "Assets/GloveBallDemo";

        public const string ScenesDir = Root + "/Scenes";
        public const string PrefabsDir = Root + "/Prefabs";
        public const string MaterialsDir = Root + "/Materials";
        public const string InputDir = Root + "/Input";

        public const string ArenaEnvScene = ScenesDir + "/ArenaEnv.unity";
        public const string DemoScene = ScenesDir + "/Demo.unity";

        public const string InputActions = InputDir + "/DemoControls.inputactions";

        // ---- Package samples ----

        /// <summary>Where the Package Manager drops imported package samples.</summary>
        public const string SamplesRoot = "Assets/Samples";

        /// <summary>Package that ships the XR Device Simulator sample.</summary>
        public const string XriPackageName = "com.unity.xr.interaction.toolkit";

        /// <summary>Display name of the sample, as listed in that package's package.json.</summary>
        public const string XrDeviceSimulatorSample = "XR Device Simulator";

        /// <summary>
        /// File name (no extension) of the simulator prefab inside that sample. Its full path
        /// carries the package version ("Assets/Samples/XR Interaction Toolkit/3.0.8/..."), so
        /// the prefab is looked up by name rather than hard-coded to one version.
        /// </summary>
        public const string XrDeviceSimulatorPrefabName = "XR Device Simulator";

        // ---- Hapbeat ----

        public const string HapticsDir = Root + "/Haptics";
        public const string EventMap = HapticsDir + "/GloveBallEventMap.asset";

        /// <summary>Kits root. A HapbeatKitsReadme marker here points the SDK's scanner at it.</summary>
        public const string KitsDir = Root + "/Kits";

        public const string KitDir = KitsDir + "/gloveball-kit";
        public const string KitStreamClipsDir = KitDir + "/stream-clips";
        public const string KitManifest = KitDir + "/gloveball-kit-manifest.json";

        /// <summary>HapbeatManager falls back to Resources.Load("HapbeatConfig"), so it lives here.</summary>
        public const string ResourcesDir = "Assets/Resources";

        public const string HapbeatConfig = ResourcesDir + "/HapbeatConfig.asset";

        public const string BallPrefab = PrefabsDir + "/Ball.prefab";
        public const string GlovePrefab = PrefabsDir + "/Glove.prefab";
        public const string LauncherPrefab = PrefabsDir + "/BallLauncher.prefab";
        public const string TargetPanelPrefab = PrefabsDir + "/TargetPanel.prefab";

        public const string LauncherMaterial = MaterialsDir + "/LauncherBody.mat";
        public const string TargetMaterial = MaterialsDir + "/TargetPanel.mat";
        public const string AimRayMaterial = MaterialsDir + "/AimRay.mat";

        // Assets borrowed from the imported Ultimate Glove Ball art (read-only).
        private const string Ugb = "Assets/UltimateGloveBall";

        public const string BallModel = Ugb + "/Models/Ball_Bland_LOD.fbx";
        public const string GloveBodyModel = Ugb + "/Models/glove_body.fbx";
        public const string GloveHandModel = Ugb + "/Models/glove_hand.fbx";
        public const string GloveHandController = Ugb + "/Animations/glove_hand.controller";
        public const string GloveMaterial = Ugb + "/Materials/Glove_Mat.mat";
        public const string BallPhysicsMaterial = Ugb + "/Physics/BallBounce.physicMaterial";
        public const string SpringChargeClip = Ugb + "/Sound/spring_charge_1.wav";
        public const string SpringReleaseClip = Ugb + "/Sound/spring_release_1.wav";
        public const string SoundDir = Ugb + "/Sound";

        public const string UiFont = "Assets/TextMesh Pro/Fonts/Rubik-Medium.ttf";
    }
}
