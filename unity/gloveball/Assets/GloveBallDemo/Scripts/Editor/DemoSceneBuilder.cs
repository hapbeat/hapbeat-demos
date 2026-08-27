using System.Collections.Generic;
using GloveBallDemo.Core;
using GloveBallDemo.Runtime;
using Hapbeat;
using Hapbeat.DemoSwitch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using Unity.XR.CoreUtils;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GloveBallDemo.Editor
{
    /// <summary>
    /// Generates Demo.unity from ArenaEnv.unity plus a scripted gameplay hierarchy.
    /// ArenaEnv.unity itself is never written to; the arena objects are only read (for the
    /// play-field bounds) and, where they conflict with the demo rig, disabled in the copy.
    /// </summary>
    public static class DemoSceneBuilder
    {
        private const string GameplayRootName = "GloveBallDemo";

        /// <summary>Name of the injected XR Device Simulator instance in the generated scene.</summary>
        public const string SimulatorObjectName = "XR Device Simulator (EditorOnly)";

        /// <summary>Unity's built-in tag for objects that are stripped from player builds.</summary>
        public const string EditorOnlyTag = "EditorOnly";

        /// <summary>Scene-authored horizontal pose used after the HMD reports tracking.</summary>
        public const string XrStartAnchorName = "XR Start Anchor";

        /// <summary>Fallback play field if the arena floor cannot be measured (metres).</summary>
        private static readonly Vector2 FallbackFieldSize = new Vector2(13f, 21f);

        /// <summary>
        /// Sideways offset of the player's standing spot from the middle of the court.
        ///
        /// The arena's own obstacles sit in three columns, at x = -3, 0 and +3 on both halves of
        /// the court. Standing on the centre line puts the near cube (0.8 m across, 1.8 m ahead)
        /// squarely in the middle of the view, right where the launchers and target panels are.
        /// Half-way between two columns leaves the whole lane ahead clear, and the ball paths
        /// from all three launchers pass wide of every obstacle.
        /// </summary>
        private const float PlayerLaneOffset = 1.5f;

        /// <summary>Gap between the player and the wall behind them, in metres.</summary>
        private const float PlayerStandoff = 2f;

        /// <summary>
        /// Standing eye height, in metres. Used both as the authored height of the rig's camera
        /// offset object and as the XR Origin's Camera Y Offset, so that every tracking situation
        /// agrees on where the player's eyes are. See <see cref="BuildXrRig"/>.
        /// </summary>
        private const float EyeHeight = 1.6f;

        /// <summary>Gap between the launcher line and the far wall, in metres.</summary>
        private const float LauncherStandoff = 3f;

        [MenuItem("GloveBall Demo/Build Demo Scene")]
        public static void BuildDemoScene()
        {
            var scene = EditorSceneManager.OpenScene(DemoAssetPaths.ArenaEnvScene, OpenSceneMode.Single);
            Debug.Log($"[DemoScene] opened {DemoAssetPaths.ArenaEnvScene}");

            RemovePreviousBuild(scene);
            DisableOriginalManagers(scene);
            var field = MeasurePlayField(scene);
            ConfigureCourtBoundaryColliders(scene);
            Debug.Log($"[DemoScene] play field = {field.size.x:0.00} x {field.size.z:0.00} m centered {field.center}");

            var root = new GameObject(GameplayRootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            var floorY = field.min.y;
            var playerZ = field.min.z + PlayerStandoff;
            var launcherZ = field.max.z - LauncherStandoff;
            Debug.Log($"[DemoScene] playerZ={playerZ:0.00} launcherZ={launcherZ:0.00}");

            var hapbeatManager = BuildHapbeatManager(root.transform);
            BuildHapticsGate(root, hapbeatManager);
            var relay = BuildHapticRelay(root.transform);
            var pool = BuildBallPool(root.transform);
            var launchers = BuildLaunchers(root.transform, launcherZ, floorY);
            // Behind the far wall: the wall has no renderer, so nothing occludes the readout, and
            // a scoreboard inside the play volume would be pelted with balls.
            var scoreboard = BuildScoreboard(root.transform, field.max.z + 0.8f, floorY);
            var xrStart = new Vector3(field.center.x + PlayerLaneOffset, floorY, playerZ);
            var head = BuildXrRig(root.transform, xrStart, field);
            BuildXrStartAnchor(root.transform, xrStart);
            var hitZone = BuildHitZone(root.transform, head);
            var targetLayout = BuildTargetPanels(root.transform, head, floorY, field.min.x, field.max.x, field.min.z, field.max.z);

            var controller = BuildGameController(root.transform, launchers, pool, hitZone, scoreboard, targetLayout);
            BuildQuestMenu(head, controller);
            BuildDeviceSimulator(root.transform);

            DisableConflictingAudioAndCameras(scene, head);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, DemoAssetPaths.DemoScene))
            {
                Debug.LogError($"[DemoScene] failed to save {DemoAssetPaths.DemoScene}");
                EditorApplication.Exit(11);
                return;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[DemoScene] saved {DemoAssetPaths.DemoScene}");
        }

        // ------------------------------------------------------------- geometry

        /// <summary>
        /// The volume a ball can actually occupy: the box enclosed by the arena's four wall
        /// colliders.
        ///
        /// The floor plane is 13 x 21 m but the walls stand at x = +-4.5 and z = +-9, so anything
        /// placed by floor size alone ends up embedded in - or behind - a wall. That is exactly
        /// what happened to the first target panel line: it sat at z = 10.2, behind a solid,
        /// rendererless wall at z = 9, so every throw died on the wall and no target could ever
        /// be hit. Measuring the walls is what makes the layout correct by construction.
        /// </summary>
        private static Bounds MeasurePlayField(Scene scene)
        {
            if (TryMeasureWalledVolume(scene, out var walled))
            {
                return walled;
            }

            Debug.LogWarning("[DemoScene] arena wall colliders not found; falling back to the floor plane");
            return MeasureFloorPlane(scene);
        }

        private static bool TryMeasureWalledVolume(Scene scene, out Bounds bounds)
        {
            bounds = default;
            var minX = float.NegativeInfinity;
            var maxX = float.PositiveInfinity;
            var minZ = float.NegativeInfinity;
            var maxZ = float.PositiveInfinity;
            var walls = 0;

            foreach (var rootGo in scene.GetRootGameObjects())
            {
                foreach (var t in rootGo.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "Walls")
                    {
                        continue;
                    }

                    foreach (var wall in t.GetComponentsInChildren<Collider>(true))
                    {
                        var b = wall.bounds;
                        walls++;

                        // A wall is thin along the axis it blocks; clip the play box to the face
                        // that looks inwards.
                        if (b.size.z < b.size.x)
                        {
                            if (b.center.z > 0f)
                            {
                                maxZ = Mathf.Min(maxZ, b.min.z);
                            }
                            else
                            {
                                minZ = Mathf.Max(minZ, b.max.z);
                            }
                        }
                        else if (b.center.x > 0f)
                        {
                            maxX = Mathf.Min(maxX, b.min.x);
                        }
                        else
                        {
                            minX = Mathf.Max(minX, b.max.x);
                        }
                    }
                }
            }

            if (walls < 4 || float.IsInfinity(minX) || float.IsInfinity(maxX) ||
                float.IsInfinity(minZ) || float.IsInfinity(maxZ) || maxX <= minX || maxZ <= minZ)
            {
                return false;
            }

            var center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
            bounds = new Bounds(center, new Vector3(maxX - minX, 0f, maxZ - minZ));
            Debug.Log($"[DemoScene] measured {walls} wall colliders");
            return true;
        }

        private static Bounds MeasureFloorPlane(Scene scene)
        {
            foreach (var rootGo in scene.GetRootGameObjects())
            {
                foreach (var t in rootGo.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "NavMeshPlane")
                    {
                        continue;
                    }

                    var renderer = t.GetComponentInChildren<Renderer>(true);
                    if (renderer == null)
                    {
                        continue;
                    }

                    var measured = renderer.bounds;
                    measured.center = new Vector3(measured.center.x, 0f, measured.center.z);
                    measured.size = new Vector3(measured.size.x, 0f, measured.size.z);
                    return measured;
                }
            }

            Debug.LogWarning("[DemoScene] NavMeshPlane not found; using the fallback field size");
            return new Bounds(Vector3.zero, new Vector3(FallbackFieldSize.x, 0f, FallbackFieldSize.y));
        }

        private static void RemovePreviousBuild(Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == GameplayRootName)
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        private static void DisableOriginalManagers(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform.name == "ObstaclesManager" || transform.name == "PostGame")
                    {
                        transform.gameObject.SetActive(false);
                    }
                }
            }
        }

        private static void ConfigureCourtBoundaryColliders(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform.name == "Walls" && transform.parent != null && transform.parent.name == "Colliders")
                    {
                        foreach (var collider in transform.GetComponentsInChildren<Collider>(true))
                        {
                            collider.enabled = false;
                        }
                    }

                    if (transform.name != "Railing_front" && transform.name != "Railing_back.001")
                    {
                        continue;
                    }

                    var meshFilter = transform.GetComponent<MeshFilter>();
                    if (meshFilter == null || meshFilter.sharedMesh == null)
                    {
                        Debug.LogWarning($"[DemoScene] visible fence mesh missing on {transform.name}");
                        continue;
                    }

                    foreach (var existingCollider in transform.GetComponents<Collider>())
                    {
                        Object.DestroyImmediate(existingCollider);
                    }

                    var fenceCollider = transform.gameObject.AddComponent<BoxCollider>();
                    fenceCollider.center = meshFilter.sharedMesh.bounds.center;
                    fenceCollider.size = meshFilter.sharedMesh.bounds.size;
                    // Locomotion is bounded explicitly because the XR Origin is moved directly.
                    // A trigger still represents the visible fence without reflecting balls.
                    fenceCollider.isTrigger = true;
                }
            }
        }

        // --------------------------------------------------------------- pieces

        private static HapticEventRelay BuildHapticRelay(Transform parent)
        {
            var go = new GameObject("HapticEventRelay");
            go.transform.SetParent(parent, false);

            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.maxDistance = 40f;

            var relay = go.AddComponent<HapticEventRelay>();
            var so = new SerializedObject(relay);
            so.FindProperty("_audioSource").objectReferenceValue = source;

            var bindings = so.FindProperty("_bindings");
            var table = new (DemoHapticEvent evt, string clip, float volume)[]
            {
                (DemoHapticEvent.BallIncomingWarning, "ball_launch_bland", 0.6f),
                (DemoHapticEvent.LeftGrab, "ball_grab_1", 0.9f),
                (DemoHapticEvent.RightGrab, "ball_grab_1", 0.9f),
                (DemoHapticEvent.LeftRelease, "ball_hit_2", 0.7f),
                (DemoHapticEvent.RightRelease, "ball_hit_2", 0.7f),
                (DemoHapticEvent.LeftArmCollide, "ball_hit_shield_1", 0.8f),
                (DemoHapticEvent.RightArmCollide, "ball_hit_shield_1", 0.8f),
                (DemoHapticEvent.BodyCollide, "ball_hit_1", 1f),
                (DemoHapticEvent.TargetHit, "ball_hit_shield_1", 0.9f),
                (DemoHapticEvent.LauncherHit, "ball_hit_shield_2", 0.9f),
                (DemoHapticEvent.LauncherStunned, "ball_bounce_2", 0.8f),
                (DemoHapticEvent.WaveStarted, "Countdown_beep_B", 0.8f),
                (DemoHapticEvent.WaveCleared, "UI_confirm_2", 0.8f),
                (DemoHapticEvent.GameOver, "airhorn", 0.7f)
            };

            bindings.arraySize = table.Length;
            for (var i = 0; i < table.Length; i++)
            {
                var element = bindings.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Event").enumValueIndex = (int)table[i].evt;
                element.FindPropertyRelative("Clip").objectReferenceValue = LoadClip(table[i].clip);
                element.FindPropertyRelative("Volume").floatValue = table[i].volume;
            }

            AttachHapbeatTriggers(go, so);

            so.ApplyModifiedPropertiesWithoutUndo();
            return relay;
        }

        /// <summary>
        /// Puts one SDK trigger per haptic event on the relay's GameObject and points the relay
        /// at them. Going through the SDK's own trigger components (rather than calling
        /// HapbeatManager directly) is what keeps every event editable in the EventMap window.
        /// </summary>
        private static void AttachHapbeatTriggers(GameObject go, SerializedObject relaySo)
        {
            var map = AssetDatabase.LoadAssetAtPath<HapbeatEventMap>(DemoAssetPaths.EventMap);
            if (map == null)
            {
                Debug.LogError($"[DemoScene] event map not found: {DemoAssetPaths.EventMap}. Run Build Haptics first.");
                return;
            }

            var bindings = relaySo.FindProperty("_hapbeatBindings");
            var oneShots = new List<DemoHapticDefinition>();
            foreach (var definition in DemoHapticCatalog.Definitions)
            {
                if (!definition.Loop)
                {
                    oneShots.Add(definition);
                }
            }

            bindings.arraySize = oneShots.Count;
            for (var i = 0; i < oneShots.Count; i++)
            {
                var definition = oneShots[i];
                var entry = map.FindByEventId(DemoHapticCatalog.EventId(definition));
                if (entry == null)
                {
                    Debug.LogError($"[DemoScene] no event map entry for {DemoHapticCatalog.EventId(definition)}");
                    continue;
                }

                var trigger = go.AddComponent<HapbeatUnityEventTrigger>();
                trigger.EditorSetupEntry(map, entry.id);

                var element = bindings.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Event").enumValueIndex = (int)definition.Event;
                element.FindPropertyRelative("Trigger").objectReferenceValue = trigger;
            }

            var loops = new List<DemoHapticDefinition>();
            foreach (var definition in DemoHapticCatalog.Definitions) if (definition.Loop) loops.Add(definition);
            var loopBindings = relaySo.FindProperty("_loopBindings");
            loopBindings.arraySize = loops.Count;
            for (var i = 0; i < loops.Count; i++)
            {
                var trigger = BuildLoopTrigger(go, map, loops[i]);
                var element = loopBindings.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Event").enumValueIndex = (int)loops[i].Event;
                element.FindPropertyRelative("Trigger").objectReferenceValue = trigger;
            }
            Debug.Log($"[DemoScene] wired {oneShots.Count} one-shots + {loops.Count} charge loops");
        }

        /// <summary>
        /// The held-ball loop is a sequence trigger: Fire() starts the looping stream, Stop()
        /// ends it, and the relay pushes the hand's speed onto it in between. Only the loop
        /// phase is used - the catch and throw one-shots are events in their own right.
        /// </summary>
        private static HapbeatSequenceTrigger BuildLoopTrigger(GameObject go, HapbeatEventMap map, DemoHapticDefinition definition)
        {
            var entry = map.FindByEventId(DemoHapticCatalog.EventId(definition));
            if (entry == null)
            {
                Debug.LogError($"[DemoScene] no event map entry for {DemoHapticCatalog.EventId(definition)}");
                return null;
            }

            var trigger = go.AddComponent<HapbeatSequenceTrigger>();
            trigger.EditorSetupEntry(map, entry.id);

            var so = new SerializedObject(trigger);
            so.FindProperty("_onStartEntryId").stringValue = "";
            so.FindProperty("_onStopEntryId").stringValue = "";
            // No one-shot to wait for, so the loop starts immediately.
            so.FindProperty("_startShotDelay").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return trigger;
        }

        /// <summary>
        /// The SDK manager. It is a scene singleton that survives loads; the config is assigned
        /// explicitly rather than relying on its Resources fallback, so a missing asset is a
        /// visible error at build time instead of silent defaults at runtime.
        /// </summary>
        /// <summary>
        /// Puts the gate on the gameplay root, wired to the manager object it switches off. It
        /// lives on a different GameObject on purpose: its execution order only guarantees that
        /// its Awake runs before the manager's, and deactivating the manager's object at that
        /// point is what keeps the manager's own Awake from ever running.
        /// </summary>
        private static void BuildHapticsGate(GameObject root, GameObject hapbeatManager)
        {
            var gate = root.AddComponent<DemoHapticsGate>();
            var so = new SerializedObject(gate);
            so.FindProperty("_sdkRoot").objectReferenceValue = hapbeatManager;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject BuildHapbeatManager(Transform parent)
        {
            var go = new GameObject("HapbeatManager");
            go.transform.SetParent(parent, false);
            var manager = go.AddComponent<HapbeatManager>();

            var config = AssetDatabase.LoadAssetAtPath<HapbeatConfig>(DemoAssetPaths.HapbeatConfig);
            if (config == null)
            {
                Debug.LogError($"[DemoScene] Hapbeat config not found: {DemoAssetPaths.HapbeatConfig}");
                return go;
            }

            var so = new SerializedObject(manager);
            so.FindProperty("_config").objectReferenceValue = config;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[DemoScene] Hapbeat manager wired to {DemoAssetPaths.HapbeatConfig} (appName='{config.appName}')");
            return go;
        }

        private static AudioClip LoadClip(string fileName)
        {
            var path = $"{DemoAssetPaths.SoundDir}/{fileName}.wav";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                Debug.LogWarning($"[DemoScene] audio clip not found: {path}");
            }

            return clip;
        }

        private static BallPool BuildBallPool(Transform parent)
        {
            var go = new GameObject("BallPool");
            go.transform.SetParent(parent, false);
            var pool = go.AddComponent<BallPool>();

            var so = new SerializedObject(pool);
            so.FindProperty("_prefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Ball>(DemoAssetPaths.BallPrefab);
            so.FindProperty("_size").intValue = 32;
            so.FindProperty("_verboseLog").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            return pool;
        }

        private static BallLauncher[] BuildLaunchers(Transform parent, float z, float floorY)
        {
            var group = new GameObject("Launchers");
            group.transform.SetParent(parent, false);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DemoAssetPaths.LauncherPrefab);
            var xs = new[] { -3f, 0f, 3f };
            var launchers = new List<BallLauncher>();

            for (var i = 0; i < xs.Length; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                instance.name = $"Launcher_{i}";
                instance.transform.position = new Vector3(xs[i], floorY + 1.2f, z);
                // Barrels point down -Z, towards the player's court.
                instance.transform.rotation = Quaternion.identity;
                launchers.Add(instance.GetComponent<BallLauncher>());
            }

            return launchers.ToArray();
        }

        private static TargetLayoutField BuildTargetPanels(
            Transform parent,
            Transform player,
            float floorY,
            float minX,
            float maxX,
            float minZ,
            float maxZ)
        {
            var group = new GameObject("Targets");
            group.transform.SetParent(parent, false);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DemoAssetPaths.TargetPanelPrefab);

            var targets = new TargetPanel[TargetLayoutPlanner.MaximumTargetCount];
            for (var i = 0; i < targets.Length; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                instance.name = $"Target_{i}";
                targets[i] = instance.GetComponent<TargetPanel>();
            }

            var layout = group.AddComponent<TargetLayoutField>();
            var so = new SerializedObject(layout);
            var targetsProperty = so.FindProperty("_targets");
            targetsProperty.arraySize = targets.Length;
            for (var i = 0; i < targets.Length; i++)
            {
                targetsProperty.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
            }
            so.FindProperty("_player").objectReferenceValue = player;
            so.FindProperty("_minX").floatValue = minX + 0.8f;
            so.FindProperty("_maxX").floatValue = maxX - 0.8f;
            so.FindProperty("_minZ").floatValue = Mathf.Max(minZ + 0.8f, player.position.z + 3f);
            so.FindProperty("_maxZ").floatValue = maxZ - 0.9f;
            so.FindProperty("_minHeight").floatValue = floorY + 0.7f;
            so.FindProperty("_maxHeight").floatValue = floorY + 3.2f;
            so.FindProperty("_minimumSpacing").floatValue = 2f;
            so.ApplyModifiedPropertiesWithoutUndo();
            // Demo.unity must already have a legible layout before Countdown advances to Wave.
            layout.BeginWave(0);
            return layout;
        }

        private static ScoreboardPresenter BuildScoreboard(Transform parent, float z, float floorY)
        {
            var go = new GameObject("Scoreboard", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(1400f, 620f);
            rect.localScale = Vector3.one * 0.005f; // 7.0 x 3.1 m
            // Clear of the target panels: their top edge is at 4.0 m, and the readout is 3.1 m
            // tall, so anything lower than this has its bottom row hidden behind a panel.
            rect.position = new Vector3(0f, floorY + 5.8f, z);
            // A world-space canvas is read from its -Z side, which is where the player stands.
            // Turning it to "face" the player would mirror the text.
            rect.rotation = Quaternion.identity;

            var font = AssetDatabase.LoadAssetAtPath<Font>(DemoAssetPaths.UiFont);
            if (font == null)
            {
                Debug.LogWarning($"[DemoScene] UI font not found: {DemoAssetPaths.UiFont}");
            }

            // The readout used to be drawn straight onto the arena's own jumbotron, which is
            // smaller than the canvas: the bottom row spilled off the screen and was rendered
            // over the crowd. The panel below gives every row a surface of its own, so the layout
            // no longer depends on the size of the art behind it.
            AddPanel(rect, new Color(0.04f, 0.05f, 0.09f, 0.94f));

            const float column = 300f;      // centre of each column, from the middle
            const float columnWidth = 620f; // leaves a 80 px margin at the panel edge

            var message = AddLabel(rect, font, "Message", new Vector2(0f, 175f), new Vector2(1300f, 170f), 120, TextAnchor.MiddleCenter, Color.white);
            var score = AddLabel(rect, font, "Score", new Vector2(-column, 10f), new Vector2(columnWidth, 110f), 78, TextAnchor.MiddleLeft, new Color(1f, 0.92f, 0.4f));
            var wave = AddLabel(rect, font, "Wave", new Vector2(column, 10f), new Vector2(columnWidth, 110f), 78, TextAnchor.MiddleRight, Color.white);
            var combo = AddLabel(rect, font, "Combo", new Vector2(0f, -145f), new Vector2(900f, 100f), 70, TextAnchor.MiddleCenter, new Color(0.55f, 0.9f, 1f));

            var presenter = go.AddComponent<ScoreboardPresenter>();
            var so = new SerializedObject(presenter);
            so.FindProperty("_scoreText").objectReferenceValue = score;
            so.FindProperty("_comboText").objectReferenceValue = combo;
            so.FindProperty("_waveText").objectReferenceValue = wave;
            so.FindProperty("_messageText").objectReferenceValue = message;
            so.ApplyModifiedPropertiesWithoutUndo();

            AddHapbeatStatusLine(go, rect, font);

            return presenter;
        }

        /// <summary>
        /// Hapbeat connection state, on the scoreboard rather than in a screen-space overlay:
        /// a screen-space HUD is not readable in a headset, and an operator needs to be able to
        /// see at a glance whether the haptics are live.
        /// </summary>
        private static void AddHapbeatStatusLine(GameObject scoreboard, RectTransform rect, Font font)
        {
            var status = AddLabel(rect, font, "HapbeatStatus", new Vector2(0f, -255f),
                new Vector2(1300f, 70f), 44, TextAnchor.MiddleCenter, new Color(0.6f, 0.65f, 0.7f));

            var overlay = scoreboard.AddComponent<HapbeatStatusOverlay>();
            var so = new SerializedObject(overlay);
            so.FindProperty("_statusText").objectReferenceValue = status;
            // No log pane: the scoreboard has no room for a scrolling history, and the overlay
            // treats a missing log text as "don't render one".
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Full-bleed background so the readout is legible against any arena art.</summary>
        private static void AddPanel(RectTransform parent, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static Text AddLabel(
            RectTransform parent,
            Font font,
            string name,
            Vector2 position,
            Vector2 size,
            int fontSize,
            TextAnchor anchor,
            Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            // Best-fit rather than overflow: the score and the wave timer change width as they
            // count up, and a row that grows past its rect ends up drawn outside the panel.
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(12, fontSize / 2);
            text.resizeTextMaxSize = fontSize;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.text = name;
            return text;
        }

        /// <summary>
        /// Builds the standard XR Origin rig:
        /// <c>XR Origin</c> → <c>Camera Offset</c> → <c>Main Camera</c> + <c>LeftHand</c>/<c>RightHand</c>.
        ///
        /// The offset object is what lets one rig serve both the headset and the desk, because
        /// the two measure poses from different places. A TrackedPoseDriver writes the device
        /// pose straight into its own local position, so whatever the pose is measured from
        /// decides where the player's eyes end up:
        ///
        ///  - Headset with Floor tracking: the pose already carries the player's real height.
        ///    XROrigin.SetupCamera sets the mode on the input subsystem and calls
        ///    MoveOffsetHeight(0f), zeroing the offset object, so the camera lands at pose height.
        ///  - Headset that cannot do Floor: XROrigin falls back to Device and applies
        ///    CameraYOffset to the offset object instead - which is why it is set to the same
        ///    <see cref="EyeHeight"/> authored here.
        ///  - XR Device Simulator (no XR loader, so no input subsystem at all): the subsystem
        ///    list is empty, SetupCamera never reaches MoveOffsetHeight, and the offset object
        ///    keeps the height authored here. The simulated HMD reports poses from the origin,
        ///    so the camera again ends up at eye height.
        ///
        /// Read off XROrigin.cs in com.unity.xr.core-utils@2.5.3 (SetupCamera / MoveOffsetHeight).
        /// </summary>
        private static Transform BuildXrRig(Transform parent, Vector3 origin, Bounds playField)
        {
            var rig = new GameObject("XR Origin");
            rig.transform.SetParent(parent, false);
            rig.transform.position = origin;

            var offsetGo = new GameObject("Camera Offset");
            offsetGo.transform.SetParent(rig.transform, false);
            offsetGo.transform.localPosition = new Vector3(0f, EyeHeight, 0f);

            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(offsetGo.transform, false);
            camGo.transform.localPosition = Vector3.zero;

            var cam = camGo.GetComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1000f;
            cam.fieldOfView = 70f;

            AddPoseDriver(camGo, DemoInputBuilder.HeadPosition, DemoInputBuilder.HeadRotation);

            var glovePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DemoAssetPaths.GlovePrefab);
            var leftGrip = DemoInputBuilder.FindReference(DemoInputBuilder.LeftGrip);
            var rightGrip = DemoInputBuilder.FindReference(DemoInputBuilder.RightGrip);
            var leftTrigger = DemoInputBuilder.FindReference(DemoInputBuilder.TriggerLeft);
            var rightTrigger = DemoInputBuilder.FindReference(DemoInputBuilder.TriggerRight);

            // Hands hang off the same offset object as the camera, so their authored resting
            // spots stay where they were in world space: 1.15 m above the floor.
            BuildHand(offsetGo.transform, glovePrefab, "LeftHand", new Vector3(-0.25f, 1.15f - EyeHeight, 0.3f),
                DemoInputBuilder.LeftPosition, DemoInputBuilder.LeftRotation, leftTrigger, leftGrip, GloveSide.Left);
            BuildHand(offsetGo.transform, glovePrefab, "RightHand", new Vector3(0.25f, 1.15f - EyeHeight, 0.3f),
                DemoInputBuilder.RightPosition, DemoInputBuilder.RightRotation, rightTrigger, rightGrip, GloveSide.Right);

            var xrOrigin = rig.AddComponent<XROrigin>();
            xrOrigin.Origin = rig;
            xrOrigin.Camera = cam;
            xrOrigin.CameraFloorOffsetObject = offsetGo;
            xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            xrOrigin.CameraYOffset = EyeHeight;

            var bootstrap = rig.AddComponent<XrRigBootstrap>();
            var so = new SerializedObject(bootstrap);
            so.FindProperty("_actions").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<InputActionAsset>(DemoAssetPaths.InputActions);
            so.FindProperty("_head").objectReferenceValue = camGo.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            var locomotion = rig.AddComponent<XrLocomotionController>();
            var locomotionSo = new SerializedObject(locomotion);
            SetActionReference(locomotionSo.FindProperty("_moveInput"), DemoInputBuilder.FindReference(DemoInputBuilder.Move));
            SetActionReference(locomotionSo.FindProperty("_snapTurnInput"), DemoInputBuilder.FindReference(DemoInputBuilder.SnapTurn));
            locomotionSo.FindProperty("_head").objectReferenceValue = camGo.transform;
            locomotionSo.FindProperty("_playAreaCenter").vector2Value = new Vector2(playField.center.x, playField.center.z);
            locomotionSo.FindProperty("_playAreaSize").vector2Value = new Vector2(playField.size.x, playField.size.z);
            locomotionSo.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log(
                $"[DemoScene] XR Origin at {rig.transform.position} camera offset {offsetGo.transform.localPosition} " +
                $"requestedTrackingOrigin={xrOrigin.RequestedTrackingOriginMode} cameraYOffset={xrOrigin.CameraYOffset:0.00}");

            return camGo.transform;
        }

        private static void BuildXrStartAnchor(Transform parent, Vector3 position)
        {
            var anchor = new GameObject(XrStartAnchorName);
            anchor.transform.SetParent(parent, false);
            anchor.transform.position = position;
            anchor.transform.rotation = Quaternion.identity;
            var alignment = anchor.AddComponent<XrStartAlignment>();
            var alignmentSo = new SerializedObject(alignment);
            alignmentSo.FindProperty("_xrOrigin").objectReferenceValue = parent.GetComponentInChildren<XROrigin>();
            alignmentSo.FindProperty("_anchor").objectReferenceValue = anchor.transform;
            alignmentSo.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildHand(
            Transform rig,
            GameObject glovePrefab,
            string name,
            Vector3 localPosition,
            string positionAction,
            string rotationAction,
            InputActionReference trigger,
            InputActionReference grip,
            GloveSide side)
        {
            var hand = new GameObject(name);
            hand.transform.SetParent(rig, false);
            hand.transform.localPosition = localPosition;
            AddPoseDriver(hand, positionAction, rotationAction);
            var armBody = hand.AddComponent<Rigidbody>();
            armBody.isKinematic = true;
            var arm = hand.AddComponent<CapsuleCollider>();
            arm.radius = 0.11f;
            arm.height = 0.55f;
            arm.direction = 2;
            arm.center = new Vector3(0f, 0f, -0.18f);
            arm.material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(DemoAssetPaths.BallPhysicsMaterial);
            var impact = hand.AddComponent<ArmImpactSurface>();
            var impactSo = new SerializedObject(impact);
            impactSo.FindProperty("_side").enumValueIndex = (int)side;
            impactSo.ApplyModifiedPropertiesWithoutUndo();

            if (glovePrefab == null)
            {
                Debug.LogError("[DemoScene] glove prefab missing");
                return;
            }

            var glove = (GameObject)PrefabUtility.InstantiatePrefab(glovePrefab, hand.transform);
            glove.name = "Glove";
            glove.transform.localPosition = Vector3.zero;
            glove.transform.localRotation = Quaternion.identity;
            glove.transform.localScale = side == GloveSide.Left ? new Vector3(-1f, 1f, 1f) : Vector3.one;

            var controller = glove.GetComponent<GloveController>();
            var so = new SerializedObject(controller);
            SetActionReference(so.FindProperty("_triggerInput"), trigger);
            SetActionReference(so.FindProperty("_gripInput"), grip);
            so.FindProperty("_side").enumValueIndex = (int)side;
            so.FindProperty("_handRoot").objectReferenceValue = hand.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetActionReference(SerializedProperty property, InputActionReference action)
        {
            property.FindPropertyRelative("m_UseReference").boolValue = true;
            property.FindPropertyRelative("m_Reference").objectReferenceValue = action;
        }

        private static void AddPoseDriver(GameObject go, string positionAction, string rotationAction)
        {
            var driver = go.AddComponent<TrackedPoseDriver>();
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            driver.positionInput = new InputActionProperty(DemoInputBuilder.FindReference(positionAction));
            driver.rotationInput = new InputActionProperty(DemoInputBuilder.FindReference(rotationAction));
        }

        /// <summary>
        /// Drops the XR Interaction Toolkit's XR Device Simulator into the scene so the demo can
        /// be played at the desk with mouse and keyboard.
        ///
        /// The instance is tagged <see cref="EditorOnlyTag"/>, which is Unity's own mechanism for
        /// keeping an object out of player builds: the whole branch, simulator and its runtime UI
        /// included, is stripped when the APK is built, so the shipped demo is byte-for-byte the
        /// headset-only rig it was before.
        ///
        /// The sample is optional. A project that has not imported it still builds a valid scene;
        /// it just has no desk-play path, which the warning says out loud.
        /// </summary>
        private static void BuildDeviceSimulator(Transform parent)
        {
            var prefab = FindDeviceSimulatorPrefab();
            if (prefab == null)
            {
                Debug.LogWarning(
                    $"[DemoScene] '{DemoAssetPaths.XrDeviceSimulatorPrefabName}' prefab not found under " +
                    $"{DemoAssetPaths.SamplesRoot}; the scene will have no desk-play simulator. " +
                    "Run BatchOps.ImportXrDeviceSimulator (or import the sample from the Package Manager) and rebuild.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = SimulatorObjectName;
            instance.tag = EditorOnlyTag;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            // Keep the fake devices inactive until XR initialization has had a chance to register
            // a real Horizon Link headset. The gate lives on the active parent so it can decide
            // whether to leave this child off or enable it as the no-headset fallback.
            var gate = parent.gameObject.AddComponent<RealHmdSimulatorGate>();
            gate.EditorSetup(instance);

            // Camera Transform is left unset on purpose: XRDeviceSimulator falls back to
            // Camera.main, which is the rig camera this builder tags MainCamera. Eye height needs
            // no compensation here either - the rig's XR Origin already handles it (BuildXrRig).

            Debug.Log(
                $"[DemoScene] added '{instance.name}' (tag={instance.tag}, initially inactive, " +
                $"real-HMD gate=on) " +
                $"from {AssetDatabase.GetAssetPath(prefab)}");
        }

        private static GameObject FindDeviceSimulatorPrefab()
        {
            if (!AssetDatabase.IsValidFolder(DemoAssetPaths.SamplesRoot))
            {
                return null;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { DemoAssetPaths.SamplesRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != DemoAssetPaths.XrDeviceSimulatorPrefabName)
                {
                    continue;
                }

                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            return null;
        }

        private static PlayerHitZone BuildHitZone(Transform parent, Transform head)
        {
            var go = new GameObject("PlayerHitZone");
            go.transform.SetParent(parent, false);

            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.isTrigger = false;
            capsule.radius = 0.25f;
            capsule.height = 1.3f;
            capsule.direction = 1; // Y axis
            capsule.material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(DemoAssetPaths.BallPhysicsMaterial);
            go.AddComponent<Rigidbody>().isKinematic = true;

            var zone = go.AddComponent<PlayerHitZone>();
            var so = new SerializedObject(zone);
            so.FindProperty("_head").objectReferenceValue = head;
            so.ApplyModifiedPropertiesWithoutUndo();
            return zone;
        }

        private static DemoGameController BuildGameController(
            Transform parent,
            BallLauncher[] launchers,
            BallPool pool,
            PlayerHitZone hitZone,
            ScoreboardPresenter scoreboard,
            TargetLayoutField targetLayout)
        {
            var go = new GameObject("DemoGameController");
            go.transform.SetParent(parent, false);
            var controller = go.AddComponent<DemoGameController>();

            var so = new SerializedObject(controller);
            var array = so.FindProperty("_launchers");
            array.arraySize = launchers.Length;
            for (var i = 0; i < launchers.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = launchers[i];
            }

            so.FindProperty("_pool").objectReferenceValue = pool;
            so.FindProperty("_hitZone").objectReferenceValue = hitZone;
            so.FindProperty("_scoreboard").objectReferenceValue = scoreboard;
            so.FindProperty("_targetLayout").objectReferenceValue = targetLayout;
            so.ApplyModifiedPropertiesWithoutUndo();
            return controller;
        }

        private static void BuildQuestMenu(Transform head, DemoGameController game)
        {
            var go = new GameObject("QuestMenu", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(head, false);
            go.transform.localPosition = new Vector3(0f, 0.08f, 0.85f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * 0.001f;

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(900f, 450f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(rect, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.015f, 0.03f, 0.08f, 0.94f);

            var font = AssetDatabase.LoadAssetAtPath<Font>(DemoAssetPaths.UiFont);
            AddLabel(panelRect, font, "Title", new Vector2(0f, 150f), new Vector2(800f, 70f), 52,
                TextAnchor.MiddleCenter, new Color(0.4f, 0.9f, 1f)).text = "GLOVEBALL MENU";
            var mode = AddLabel(panelRect, font, "Mode", new Vector2(0f, 40f), new Vector2(760f, 85f), 48,
                TextAnchor.MiddleLeft, Color.white);
            var restart = AddLabel(panelRect, font, "Restart", new Vector2(0f, -55f), new Vector2(760f, 85f), 48,
                TextAnchor.MiddleLeft, Color.white);
            var hint = AddLabel(panelRect, font, "Hint", new Vector2(0f, -155f), new Vector2(820f, 70f), 26,
                TextAnchor.MiddleCenter, new Color(0.7f, 0.8f, 0.9f));

            var menu = go.AddComponent<QuestMenuController>();
            var so = new SerializedObject(menu);
            so.FindProperty("_game").objectReferenceValue = game;
            so.FindProperty("_panel").objectReferenceValue = panel;
            so.FindProperty("_modeText").objectReferenceValue = mode;
            so.FindProperty("_restartText").objectReferenceValue = restart;
            so.FindProperty("_hintText").objectReferenceValue = hint;
            SetActionReference(so.FindProperty("_leftMenuInput"), DemoInputBuilder.FindReference(DemoInputBuilder.LeftMenu));
            SetActionReference(so.FindProperty("_leftSecondaryInput"), DemoInputBuilder.FindReference(DemoInputBuilder.LeftSecondary));
            SetActionReference(so.FindProperty("_rightSecondaryInput"), DemoInputBuilder.FindReference(DemoInputBuilder.RightSecondary));
            SetActionReference(so.FindProperty("_navigateInput"), DemoInputBuilder.FindReference(DemoInputBuilder.MenuNavigate));
            SetActionReference(so.FindProperty("_leftPrimaryInput"), DemoInputBuilder.FindReference(DemoInputBuilder.LeftPrimary));
            SetActionReference(so.FindProperty("_rightPrimaryInput"), DemoInputBuilder.FindReference(DemoInputBuilder.RightPrimary));
            so.ApplyModifiedPropertiesWithoutUndo();
            panel.SetActive(false);
        }

        /// <summary>
        /// The arena scene ships with its own camera and audio listeners. Two enabled listeners
        /// is a runtime warning, and a stray camera would fight the rig for the display, so they
        /// are switched off here — in the generated scene only.
        /// </summary>
        private static void DisableConflictingAudioAndCameras(Scene scene, Transform head)
        {
            var disabledCameras = 0;
            var disabledListeners = 0;

            foreach (var rootGo in scene.GetRootGameObjects())
            {
                if (rootGo.name == GameplayRootName)
                {
                    continue;
                }

                foreach (var cam in rootGo.GetComponentsInChildren<Camera>(true))
                {
                    if (cam.transform == head)
                    {
                        continue;
                    }

                    cam.enabled = false;
                    disabledCameras++;
                }

                foreach (var listener in rootGo.GetComponentsInChildren<AudioListener>(true))
                {
                    listener.enabled = false;
                    disabledListeners++;
                }
            }

            Debug.Log($"[DemoScene] disabled {disabledCameras} arena cameras and {disabledListeners} audio listeners");
        }
    }
}
