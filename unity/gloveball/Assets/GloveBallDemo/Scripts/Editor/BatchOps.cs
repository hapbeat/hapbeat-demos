// Batch-mode entry points for the Glove Ball solo demo project.
// Every scene mutation happens here (scenes are never hand-edited as YAML).
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GloveBallDemo.Editor
{
    public static class BatchOps
    {
        private const string ArenaEnvScenePath = "Assets/GloveBallDemo/Scenes/ArenaEnv.unity";

        // ---------------------------------------------------------------- utils

        /// <summary>Reads a value passed after -executeMethod, e.g. "-gbScene Assets/Foo.unity".</summary>
        private static string GetArg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return fallback;
        }

        // ------------------------------------------------------------ editor mute

        // EditorUtility.audioMasterMute is the Game view's "Mute Audio" toggle, and it is scoped
        // neither to this run nor to this project: Unity persists it in EditorPrefs
        // (HKCU\Software\Unity Technologies\Unity Editor 5.x\AudioMasterMute_h3604209190 on
        // Windows), which every project on the machine shares. Setting it and quitting therefore
        // left the developer's interactive editor permanently silent - which is what happened
        // before these two keys existed. So the pre-run value is parked in EditorPrefs and put
        // back when the batch run ends.
        //
        // The "owed" flag is what makes the restore survive a killed run: a later batch run finds
        // the flag already set, leaves the recorded value alone, and eventually restores the
        // developer's original setting rather than the mute its predecessor left behind.
        private const string AudioMuteOwedKey = "GloveBallDemo.Batch.AudioMuteOwed";
        private const string AudioMutePreviousKey = "GloveBallDemo.Batch.AudioMutePrevious";

        private static bool _quitHookInstalled;

        /// <summary>
        /// Silences the editor for the duration of a batch run. Gameplay still plays its
        /// AudioSources, they just do not reach the speakers, so an unattended batch run never
        /// makes noise on the developer's machine. The previous setting is restored when the
        /// editor quits (see <see cref="RestoreEditorAudio"/>). This is an editor-only mute: it
        /// does not touch the audio settings that ship in the player build.
        ///
        /// Only a batch editor is muted. The same entry points are reachable from an open editor
        /// through the [CliCommand] wrappers in CliCommands.cs, and there the developer is sitting
        /// at the machine on purpose - silencing their Game view out from under them is the exact
        /// surprise the restore logic exists to avoid. Skipping the mute also keeps the
        /// owed/previous pair honest: nothing was changed, so nothing is owed back.
        /// </summary>
        public static void MuteEditorAudio()
        {
            if (!Application.isBatchMode)
            {
                return;
            }

            if (!EditorPrefs.GetBool(AudioMuteOwedKey, false))
            {
                EditorPrefs.SetBool(AudioMutePreviousKey, EditorUtility.audioMasterMute);
                EditorPrefs.SetBool(AudioMuteOwedKey, true);
            }

            EditorUtility.audioMasterMute = true;

            if (!_quitHookInstalled)
            {
                _quitHookInstalled = true;
                EditorApplication.quitting += RestoreEditorAudio;
            }
        }

        /// <summary>
        /// Puts the Game view mute toggle back the way the developer had it. Called on editor
        /// quit, and explicitly by anything that exits through EditorApplication.Exit, which
        /// bypasses the quitting callback.
        /// </summary>
        public static void RestoreEditorAudio()
        {
            if (!EditorPrefs.GetBool(AudioMuteOwedKey, false))
            {
                return;
            }

            var previous = EditorPrefs.GetBool(AudioMutePreviousKey, false);
            EditorUtility.audioMasterMute = previous;
            EditorPrefs.DeleteKey(AudioMuteOwedKey);
            EditorPrefs.DeleteKey(AudioMutePreviousKey);
            Debug.Log($"[BatchOps] restored editor audioMasterMute = {previous}");
        }

        /// <summary>Read-only report of the shared mute setting. Deliberately does not mute.</summary>
        public static void ReportEditorAudio()
        {
            Debug.Log(
                $"[BatchOps] audioMasterMute={EditorUtility.audioMasterMute} " +
                $"restoreOwed={EditorPrefs.GetBool(AudioMuteOwedKey, false)} " +
                $"recordedPrevious={EditorPrefs.GetBool(AudioMutePreviousKey, false)}");
        }

        /// <summary>Upgrades only Ball.prefab so its model is never distance-culled.</summary>
        public static void UpgradeBallPrefabForQuest()
        {
            MuteEditorAudio();
            DemoPrefabBuilder.UpgradeBallPrefabForQuest();
        }

        /// <summary>
        /// Clears the shared mute outright, for recovering from runs made before the restore
        /// existed. Deliberately does not call <see cref="MuteEditorAudio"/>.
        /// </summary>
        public static void UnmuteEditorAudio()
        {
            EditorPrefs.DeleteKey(AudioMuteOwedKey);
            EditorPrefs.DeleteKey(AudioMutePreviousKey);
            EditorUtility.audioMasterMute = false;
            Debug.Log("[BatchOps] audioMasterMute cleared");
        }

        private static void EnsureDirectory(string assetOrDiskPath)
        {
            var dir = Path.GetDirectoryName(assetOrDiskPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private static string ArtifactsDir
        {
            get
            {
                var dir = Path.Combine(Directory.GetCurrentDirectory(), "tools", "artifacts");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                return dir;
            }
        }

        // NOTE on .unitypackage import: there is deliberately no editor method for it.
        // AssetDatabase.ImportPackage(path, interactive:false) queues the work on the
        // editor loop, which never ticks under -executeMethod + -quit, so it silently
        // imports nothing. Use tools/import-package.ps1, which drives Unity's own
        // -importPackage CLI switch and completes synchronously in batch mode.

        // ------------------------------------------------------------ configure

        /// <summary>Linear color space + URP pipeline asset wiring.</summary>
        public static void ConfigureProject()
        {
            MuteEditorAudio();
            PlayerSettings.colorSpace = ColorSpace.Linear;
            Debug.Log("[BatchOps] colorSpace = Linear");

            var pipeline = FindOrCreateUrpAsset();
            if (pipeline == null)
            {
                Debug.LogError("[BatchOps] could not resolve a URP asset");
                EditorApplication.Exit(3);
                return;
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            Debug.Log($"[BatchOps] defaultRenderPipeline = {AssetDatabase.GetAssetPath(pipeline)}");

            var currentLevel = QualitySettings.GetQualityLevel();
            var names = QualitySettings.names;
            for (var i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }

            QualitySettings.SetQualityLevel(currentLevel, false);
            Debug.Log($"[BatchOps] assigned URP asset to {names.Length} quality levels");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static RenderPipelineAsset FindOrCreateUrpAsset()
        {
            // Prefer an asset that came over from the source project so its renderer
            // features / shader stripping match the imported art.
            var guids = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
            UniversalRenderPipelineAsset best = null;
            var bestPath = string.Empty;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null)
                {
                    continue;
                }

                Debug.Log($"[BatchOps] found URP asset: {path}");
                // Deterministic pick: shortest path wins, ties broken alphabetically.
                if (best == null || path.Length < bestPath.Length ||
                    (path.Length == bestPath.Length && string.CompareOrdinal(path, bestPath) < 0))
                {
                    best = asset;
                    bestPath = path;
                }
            }

            if (best != null)
            {
                return best;
            }

            Debug.LogWarning("[BatchOps] no URP asset found; creating one under Assets/GloveBallDemo/Settings");

            const string settingsDir = "Assets/GloveBallDemo/Settings";
            EnsureDirectory(settingsDir + "/dummy");
            AssetDatabase.Refresh();

            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, settingsDir + "/GloveBallDemo_Renderer.asset");

            var created = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(created, settingsDir + "/GloveBallDemo_URP.asset");
            AssetDatabase.SaveAssets();

            return created;
        }

        // ------------------------------------------------------- strip scripts

        /// <summary>
        /// Opens the source scene (-gbScene), removes every missing MonoBehaviour from all
        /// GameObjects including inactive ones, and saves the result as ArenaEnv.unity.
        /// </summary>
        public static void StripMissingScripts()
        {
            MuteEditorAudio();
            var sourceScenePath = GetArg("-gbScene", "Assets/UltimateGloveBall/Scenes/Arena.unity");
            var outputScenePath = GetArg("-gbOutScene", ArenaEnvScenePath);

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(sourceScenePath) == null)
            {
                Debug.LogError($"[BatchOps] source scene not found: {sourceScenePath}");
                EditorApplication.Exit(4);
                return;
            }

            var scene = EditorSceneManager.OpenScene(sourceScenePath, OpenSceneMode.Single);
            Debug.Log($"[BatchOps] opened {sourceScenePath}");

            var goCount = 0;
            var removed = 0;
            var affectedObjects = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    goCount++;
                    var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (count <= 0)
                    {
                        continue;
                    }

                    removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                    affectedObjects++;
                }
            }

            Debug.Log($"[BatchOps] scanned {goCount} GameObjects; removed {removed} missing MonoBehaviours from {affectedObjects} objects");

            EnsureDirectory(outputScenePath);
            AssetDatabase.Refresh();

            if (!EditorSceneManager.SaveScene(scene, outputScenePath))
            {
                Debug.LogError($"[BatchOps] failed to save {outputScenePath}");
                EditorApplication.Exit(5);
                return;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[BatchOps] saved {outputScenePath}");
        }

        // -------------------------------------------------------- screenshots

        /// <summary>
        /// Renders two 1920x1080 PNGs of the arena scene: a framed overview and a
        /// player-eye-height view. Must be run WITHOUT -nographics.
        /// </summary>
        public static void CaptureArenaScreenshot()
        {
            MuteEditorAudio();
            var scenePath = GetArg("-gbScene", ArenaEnvScenePath);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                Debug.LogError($"[BatchOps] scene not found: {scenePath}");
                EditorApplication.Exit(6);
                return;
            }

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Debug.Log($"[BatchOps] opened {scenePath} for capture");

            // The full scene bounds are ~98 m wide because they include the outer
            // environment (island, palms, blimp). Framing on that leaves the stadium a
            // speck, so frame on the stadium interior instead.
            if (!TryComputeBounds(scene, StadiumInteriorRadius, out var stadiumBounds))
            {
                Debug.LogError("[BatchOps] no renderers found; cannot frame a camera");
                EditorApplication.Exit(7);
                return;
            }

            if (!TryComputeBounds(scene, CourtRadius, out var courtBounds))
            {
                courtBounds = stadiumBounds;
            }

            Debug.Log($"[BatchOps] stadium bounds center={stadiumBounds.center} size={stadiumBounds.size}");
            Debug.Log($"[BatchOps] court bounds center={courtBounds.center} size={courtBounds.size}");

            var camGo = new GameObject("BatchCaptureCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 2000f;

            // 1) Overview: pull back along a diagonal far enough to fit the stadium sphere.
            var radius = stadiumBounds.extents.magnitude;
            // 0.8 of the bounding-sphere fit: the sphere is dominated by the roof trusses, so
            // the exact fit leaves the arena small. The steep elevation clears the decorative
            // balloons that float over the court and would otherwise fill the foreground.
            var distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.8f;
            var dir = new Vector3(0.55f, 0.8f, -0.55f).normalized;
            camGo.transform.position = stadiumBounds.center + dir * distance;
            camGo.transform.LookAt(stadiumBounds.center);
            CaptureTo(cam, Path.Combine(ArtifactsDir, "arena-overview.png"));

            // 2) Player eye height: standing just off one end of the court, looking across it.
            var longAxis = courtBounds.size.x >= courtBounds.size.z ? Vector3.right : Vector3.forward;
            var halfLength = Vector3.Scale(courtBounds.extents, longAxis).magnitude;
            var floorY = courtBounds.min.y;
            var eye = courtBounds.center - longAxis * (halfLength + 3f);
            eye.y = floorY + 1.7f;
            var lookAt = courtBounds.center;
            lookAt.y = floorY + 1.5f;
            camGo.transform.position = eye;
            camGo.transform.rotation = Quaternion.LookRotation((lookAt - eye).normalized, Vector3.up);
            CaptureTo(cam, Path.Combine(ArtifactsDir, "arena-player-eye.png"));

            UnityEngine.Object.DestroyImmediate(camGo);
        }

        // Horizontal radius around the origin used to isolate the stadium interior from
        // the surrounding island environment (values read off ReportSceneHealth output:
        // crowd 24x37 m at the origin, outer environment 98 m).
        private const float StadiumInteriorRadius = 30f;

        // The play court itself (BallSpawner / ObstaclesManager span ~7x9 m at the origin).
        private const float CourtRadius = 8f;

        /// <summary>
        /// Bounds of every renderer that actually *fits* inside a cylinder of
        /// <paramref name="horizontalRadius"/> around the world origin: its center must be
        /// inside, and its own XZ footprint must not exceed the cylinder. The size test is
        /// what excludes the stadium shell and island mesh, whose bounds are centered on the
        /// origin but span the whole 98 m scene.
        /// </summary>
        private static bool TryComputeBounds(Scene scene, float horizontalRadius, out Bounds bounds)
        {
            bounds = default;
            var any = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    // Particle renderers report zero-size bounds when not simulating.
                    if (r is ParticleSystemRenderer)
                    {
                        continue;
                    }

                    var b = r.bounds;
                    if (new Vector2(b.center.x, b.center.z).magnitude > horizontalRadius)
                    {
                        continue;
                    }

                    if (b.size.x > horizontalRadius * 2f || b.size.z > horizontalRadius * 2f)
                    {
                        continue;
                    }

                    if (!any)
                    {
                        bounds = r.bounds;
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(r.bounds);
                    }
                }
            }

            return any;
        }

        private static void CaptureTo(Camera cam, string filePath)
        {
            const int width = 1920;
            const int height = 1080;

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1
            };
            var previousTarget = cam.targetTexture;
            var previousActive = RenderTexture.active;

            cam.targetTexture = rt;
            // Warm-up renders: the first frame after opening a scene in batch mode can land
            // before the baked lightmaps and ambient probe are live, which produced captures
            // that were alternately blown out and pitch black.
            DynamicGI.UpdateEnvironment();
            cam.Render();
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            RenderTexture.active = previousActive;
            cam.targetTexture = previousTarget;

            File.WriteAllBytes(filePath, tex.EncodeToPNG());
            Debug.Log($"[BatchOps] wrote {filePath}");

            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }

        // --------------------------------------------------------- diagnostics

        /// <summary>
        /// Dumps the facts needed to judge whether the imported arena is intact:
        /// baked lightmap count, renderers stuck on the magenta error shader,
        /// missing meshes, and the layout of the root hierarchy.
        /// </summary>
        public static void ReportSceneHealth()
        {
            MuteEditorAudio();
            var scenePath = GetArg("-gbScene", ArenaEnvScenePath);
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Debug.Log($"[Health] scene={scenePath}");
            Debug.Log($"[Health] lightmaps={LightmapSettings.lightmaps.Length} lightProbes={(LightmapSettings.lightProbes == null ? 0 : LightmapSettings.lightProbes.count)}");
            Debug.Log($"[Health] ambientMode={RenderSettings.ambientMode} ambientIntensity={RenderSettings.ambientIntensity} fog={RenderSettings.fog} fogMode={RenderSettings.fogMode} fogDensity={RenderSettings.fogDensity} skybox={(RenderSettings.skybox == null ? "<none>" : RenderSettings.skybox.name)}");

            var errorShaderRenderers = new List<string>();
            var missingMeshes = new List<string>();
            var nullMaterials = new List<string>();
            var rendererCount = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                var rootRenderers = root.GetComponentsInChildren<Renderer>(true);
                var rootBounds = new Bounds(root.transform.position, Vector3.zero);
                var hasBounds = false;
                foreach (var r in rootRenderers)
                {
                    if (!hasBounds) { rootBounds = r.bounds; hasBounds = true; }
                    else { rootBounds.Encapsulate(r.bounds); }
                }

                Debug.Log($"[Health] root '{root.name}' active={root.activeSelf} renderers={rootRenderers.Length} bounds(center={rootBounds.center}, size={rootBounds.size})");

                foreach (var r in rootRenderers)
                {
                    rendererCount++;
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null)
                        {
                            nullMaterials.Add(GetPath(r.transform));
                            continue;
                        }

                        if (m.shader == null || m.shader.name == "Hidden/InternalErrorShader")
                        {
                            errorShaderRenderers.Add($"{GetPath(r.transform)} :: {m.name}");
                        }
                    }
                }

                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null)
                    {
                        missingMeshes.Add(GetPath(mf.transform));
                    }
                }
            }

            Debug.Log($"[Health] renderers={rendererCount} errorShader={errorShaderRenderers.Count} nullMaterial={nullMaterials.Count} missingMesh={missingMeshes.Count}");
            foreach (var s in errorShaderRenderers) { Debug.Log($"[Health] ERROR-SHADER {s}"); }
            foreach (var s in nullMaterials) { Debug.Log($"[Health] NULL-MATERIAL {s}"); }
            foreach (var s in missingMeshes) { Debug.Log($"[Health] MISSING-MESH {s}"); }
        }

        /// <summary>
        /// Lists everything standing between the player rig and the launcher line in the demo
        /// scene, so the spawn point can be placed where the sightline to the launchers is clear.
        /// Reports both the arena props (obstacles) and the demo's own rig objects.
        /// </summary>
        public static void ReportSightline()
        {
            MuteEditorAudio();
            var scene = EditorSceneManager.OpenScene(DemoAssetPaths.DemoScene, OpenSceneMode.Single);

            Transform head = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                {
                    if (cam.name == "Main Camera")
                    {
                        head = cam.transform;
                        break;
                    }
                }
            }

            if (head == null)
            {
                Debug.LogError("[Sightline] no rig camera found");
                EditorApplication.Exit(12);
                return;
            }

            Debug.Log($"[Sightline] eye={head.position}");

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer)
                    {
                        continue;
                    }

                    var b = r.bounds;
                    // The corridor the player has to see through: their own half of the court,
                    // at body height, within the width the launchers span.
                    if (b.center.z < head.position.z - 0.5f || b.center.z > 10.5f)
                    {
                        continue;
                    }

                    if (Mathf.Abs(b.center.x) > 8f || b.center.y > 4.5f || b.size.x > 16f || b.size.z > 24f)
                    {
                        continue;
                    }

                    Debug.Log($"[Sightline] PROP {GetPath(r.transform)} center={b.center} size={b.size}");
                }
            }

            // Colliders as well as renderers: the arena's play boundary is invisible, and a
            // target the ball cannot physically reach looks exactly like one that works.
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var c in root.GetComponentsInChildren<Collider>(true))
                {
                    var b = c.bounds;
                    if (b.size.x > 60f || b.size.z > 60f)
                    {
                        continue;
                    }

                    Debug.Log(
                        $"[Sightline] COLLIDER {GetPath(c.transform)} trigger={c.isTrigger} " +
                        $"center={b.center} size={b.size}");
                }
            }
        }

        private static string GetPath(Transform t)
        {
            var path = t.name;
            var p = t.parent;
            while (p != null)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }

            return path;
        }

        // ------------------------------------------------------------ demo build

        /// <summary>Regenerates every generated demo asset: input actions, prefabs, then the scene.</summary>
        public static void BuildDemo()
        {
            MuteEditorAudio();
            DemoInputBuilder.BuildInputActions();
            DemoPrefabBuilder.BuildPrefabs();
            // Before the scene: the scene wiring looks up EventMap entries by their stable ids.
            DemoHapticsBuilder.BuildHaptics();
            DemoSceneBuilder.BuildDemoScene();
        }

        /// <summary>
        /// Enters play mode on Demo.unity and reports errors + wave progress.
        /// Run with tools/run-unity.ps1 -NoQuit -Graphics; the method exits the editor itself.
        /// </summary>
        public static void SmokePlay()
        {
            MuteEditorAudio();
            GloveBallDemo.Editor.SmokePlay.Run();
        }

        /// <summary>
        /// Renders the demo scene from the player's rig camera, so the screenshot shows exactly
        /// what the headset would. Must be run WITHOUT -nographics.
        /// </summary>
        public static void CaptureDemoScreenshot()
        {
            MuteEditorAudio();
            var scenePath = GetArg("-gbScene", DemoAssetPaths.DemoScene);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                Debug.LogError($"[BatchOps] scene not found: {scenePath}");
                EditorApplication.Exit(8);
                return;
            }

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            Camera rigCamera = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                {
                    if (cam.name == "Main Camera")
                    {
                        rigCamera = cam;
                        break;
                    }
                }

                if (rigCamera != null)
                {
                    break;
                }
            }

            if (rigCamera == null)
            {
                Debug.LogError("[BatchOps] no rig camera found in the demo scene");
                EditorApplication.Exit(9);
                return;
            }

            var camGo = new GameObject("BatchCaptureCamera");
            var cam2 = camGo.AddComponent<Camera>();
            cam2.clearFlags = CameraClearFlags.Skybox;
            cam2.fieldOfView = rigCamera.fieldOfView;
            cam2.nearClipPlane = 0.05f;
            cam2.farClipPlane = 2000f;

            camGo.transform.SetPositionAndRotation(rigCamera.transform.position, rigCamera.transform.rotation);
            CaptureTo(cam2, Path.Combine(ArtifactsDir, "demo-player-eye.png"));

            // A second, higher framing that also shows the player rig itself in the court.
            var eye = rigCamera.transform.position + new Vector3(4.5f, 2.2f, -4.5f);
            camGo.transform.position = eye;
            camGo.transform.rotation = Quaternion.LookRotation(
                (rigCamera.transform.position + rigCamera.transform.forward * 9f + Vector3.up * 1.5f - eye).normalized,
                Vector3.up);
            CaptureTo(cam2, Path.Combine(ArtifactsDir, "demo-court-overview.png"));

            UnityEngine.Object.DestroyImmediate(camGo);
        }

        // ---------------------------------------------------------- android/apk

        /// <summary>Applies the Quest (OpenXR + Android player) configuration without building.</summary>
        public static void ConfigureAndroid()
        {
            MuteEditorAudio();
            DemoAndroidBuilder.ConfigureAndroidXr();
            DemoAndroidBuilder.ConfigureAndroidPlayer();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Builds the Quest APK. Override the destination with "-gbApk &lt;path&gt;".
        ///
        /// This op is the authority on what the distributed build is configured with: whatever
        /// state ProjectSettings.asset happens to be in when it runs, the settings asserted here
        /// are the ones that ship. So they are forced immediately before the build rather than
        /// assumed to have survived since the last time someone set them.
        ///
        /// runInBackground is forced because com.unity.pipeline's BasePipelineServer sets
        /// Application.runInBackground = true every time it starts, and in the editor that write
        /// lands in ProjectSettings.asset - so simply opening the project can flip the shipped
        /// value. The exhibition build must pause when the headset is taken off.
        /// </summary>
        public static void BuildApk()
        {
            MuteEditorAudio();

            if (!CheckInputBackends())
            {
                Debug.LogError("[BatchOps] BuildApk aborted: input backends are not build-ready");
                EditorApplication.Exit(34);
                return;
            }

            PlayerSettings.runInBackground = false;
            Debug.Log($"[BatchOps] BuildApk forced runInBackground={PlayerSettings.runInBackground}");

            var output = GetArg("-gbApk", Path.Combine(ArtifactsDir, "GloveBallDemo.apk"));
            DemoAndroidBuilder.BuildApk(output);
        }

        // --------------------------------------------------- batch input devices

        /// <summary>
        /// Gives a batch run a keyboard and a mouse, which a batch editor otherwise has none of.
        ///
        /// The XR Device Simulator's UI prints the key bound to each of its functions by reading
        /// action.controls[0] in its Start. With no keyboard device those actions resolve to
        /// nothing and it throws ArgumentOutOfRangeException
        /// (XRDeviceSimulatorUI.Initialize, line 468) - which, with Error Pause on, stops the
        /// player loop dead and any play-mode batch run with it. Adding the devices before play
        /// mode starts makes the batch run match the interactive editor, where both devices
        /// always exist, rather than papering over the difference.
        /// </summary>
        public static void EnsureDesktopInputDevices()
        {
            if (UnityEngine.InputSystem.Keyboard.current == null)
            {
                UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
                Debug.Log("[BatchOps] added a synthetic Keyboard (batch editor has none)");
            }

            if (UnityEngine.InputSystem.Mouse.current == null)
            {
                UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
                Debug.Log("[BatchOps] added a synthetic Mouse (batch editor has none)");
            }
        }

        // ------------------------------------------------------- package samples

        /// <summary>
        /// Imports the XR Interaction Toolkit's "XR Device Simulator" sample into
        /// Assets/Samples, which is what puts the simulator prefab in the project.
        ///
        /// Run this on its own: the sample brings an assembly definition with it, and the
        /// scripts it adds are only compiled on the next editor launch. Build the demo scene in
        /// a separate invocation so the injected prefab resolves against compiled code.
        /// </summary>
        public static void ImportXrDeviceSimulator()
        {
            MuteEditorAudio();

            var package = UnityEditor.PackageManager.PackageInfo.FindForPackageName(DemoAssetPaths.XriPackageName);
            if (package == null)
            {
                Debug.LogError($"[Simulator] package not installed: {DemoAssetPaths.XriPackageName}");
                EditorApplication.Exit(30);
                return;
            }

            var samples = UnityEditor.PackageManager.UI.Sample.FindByPackage(package.name, package.version);
            if (samples == null)
            {
                Debug.LogError($"[Simulator] no samples listed for {package.name}@{package.version}");
                EditorApplication.Exit(31);
                return;
            }

            foreach (var sample in samples)
            {
                if (sample.displayName != DemoAssetPaths.XrDeviceSimulatorSample)
                {
                    continue;
                }

                Debug.Log($"[Simulator] importing '{sample.displayName}' from {package.name}@{package.version} (alreadyImported={sample.isImported})");
                if (!sample.Import(UnityEditor.PackageManager.UI.Sample.ImportOptions.OverridePreviousImports))
                {
                    Debug.LogError("[Simulator] Sample.Import reported failure");
                    EditorApplication.Exit(32);
                    return;
                }

                AssetDatabase.Refresh();
                Debug.Log($"[Simulator] imported to {sample.importPath}");
                return;
            }

            Debug.LogError($"[Simulator] '{DemoAssetPaths.XrDeviceSimulatorSample}' is not among the samples of {package.name}@{package.version}");
            EditorApplication.Exit(33);
        }

        /// <summary>
        /// Enters play mode on Demo.unity and checks that the simulator's devices show up and
        /// that every demo input action resolves onto them. Run with
        /// tools/run-unity.ps1 -NoQuit -Graphics; the method exits the editor itself.
        /// </summary>
        public static void VerifySimulator()
        {
            MuteEditorAudio();
            SimulatorCheck.Run();
        }

        // ------------------------------------------------------ input backends

        /// <summary>
        /// Reports which input backends this project builds with.
        ///
        /// "Active Input Handling" (ProjectSettings.asset :: activeInputHandler) is what decides
        /// whether the Input System's native backend exists at all: 0 = Input Manager only,
        /// 1 = Input System only, 2 = Both. With 0, ENABLE_INPUT_SYSTEM is not defined and,
        /// per the package's own debugger warning, "no devices and input from hardware will come
        /// through in the new input system APIs" - so TrackedPoseDriver and the demo's
        /// InputActions would be dead in the player even though everything compiles.
        ///
        /// The setting has no public PlayerSettings accessor, so it is read the same way the
        /// Input System package reads it: as a serialized property of the PlayerSettings object.
        /// The #if pair below reports the defines as this editor assembly actually saw them.
        /// </summary>
        public static void ReportInputBackends()
        {
            MuteEditorAudio();

            if (!CheckInputBackends())
            {
                EditorApplication.Exit(34);
            }
        }

        /// <summary>
        /// The check behind <see cref="ReportInputBackends"/>, split out so BuildApk can gate a
        /// build on it. Logs the same report either way and returns whether the project is
        /// configured to build exclusively with the Input System backend; the caller decides how to fail.
        /// </summary>
        private static bool CheckInputBackends()
        {
            var raw = -1;
            foreach (var settings in Resources.FindObjectsOfTypeAll<PlayerSettings>())
            {
                var property = new SerializedObject(settings).FindProperty("activeInputHandler");
                if (property != null)
                {
                    raw = property.intValue;
                }

                break;
            }

            var label = raw switch
            {
                0 => "Input Manager (old) only",
                1 => "Input System (new) only",
                2 => "Both",
                _ => "<unreadable>"
            };

            Debug.Log($"[InputBackends] activeInputHandler={raw} ({label})");

#if ENABLE_INPUT_SYSTEM
            Debug.Log("[InputBackends] ENABLE_INPUT_SYSTEM=defined");
#else
            Debug.LogError("[InputBackends] ENABLE_INPUT_SYSTEM=NOT defined");
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            Debug.Log("[InputBackends] ENABLE_LEGACY_INPUT_MANAGER=defined");
#else
            Debug.Log("[InputBackends] ENABLE_LEGACY_INPUT_MANAGER=NOT defined");
#endif

            var ok = raw == 1;
#if !ENABLE_INPUT_SYSTEM
            ok = false;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            ok = false;
#endif
            if (!ok)
            {
                Debug.LogError("[InputBackends] expected activeInputHandler=1 with ENABLE_INPUT_SYSTEM defined and ENABLE_LEGACY_INPUT_MANAGER not defined");
                return false;
            }

            Debug.Log("[InputBackends] ok");
            return true;
        }

        /// <summary>No-op entry point used to force a compile + report asset counts.</summary>
        public static void CompileCheck()
        {
            MuteEditorAudio();
            AssetDatabase.Refresh();
            var scenes = AssetDatabase.FindAssets("t:SceneAsset");
            var mats = AssetDatabase.FindAssets("t:Material");
            var all = AssetDatabase.FindAssets(string.Empty, new[] { "Assets" });
            Debug.Log($"[BatchOps] CompileCheck ok. assets={all.Length} scenes={scenes.Length} materials={mats.Length}");
        }
    }
}
