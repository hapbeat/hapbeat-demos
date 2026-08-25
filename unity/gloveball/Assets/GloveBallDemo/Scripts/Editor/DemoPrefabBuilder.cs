using System.IO;
using GloveBallDemo.Runtime;
using UnityEditor;
using UnityEngine;

namespace GloveBallDemo.Editor
{
    /// <summary>
    /// Builds the gameplay prefabs from imported art plus primitives.
    /// The Ultimate Glove Ball ball prefabs are deliberately not reused: they still carry
    /// missing (networking) MonoBehaviours, so only their meshes are borrowed.
    /// </summary>
    public static class DemoPrefabBuilder
    {
        private const float BallDiameter = 0.24f;
        /// <summary>Largest dimension of the glove model, in metres.</summary>
        private const float GloveSize = 0.3f;

        [MenuItem("GloveBall Demo/Build Prefabs")]
        public static void BuildPrefabs()
        {
            Directory.CreateDirectory(DemoAssetPaths.PrefabsDir);
            Directory.CreateDirectory(DemoAssetPaths.MaterialsDir);
            AssetDatabase.Refresh();

            BuildBallPrefab();
            BuildGlovePrefab();
            BuildLauncherPrefab();
            BuildTargetPanelPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DemoPrefabs] done");
        }

        // ------------------------------------------------------------------ ball

        private static void BuildBallPrefab()
        {
            var root = new GameObject("Ball");

            var body = root.AddComponent<Rigidbody>();
            body.mass = 0.25f;
            // Drag-free on purpose: BallisticSolver aims with a closed-form drag-free solution,
            // so any damping here becomes a systematic undershoot (~0.7 m over the 15 m court,
            // enough to drop every shot in front of the player instead of on them).
            body.linearDamping = 0f;
            body.angularDamping = 0.05f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // Balls travel at 16 m/s; discrete detection would tunnel through the arena shell.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var collider = root.AddComponent<SphereCollider>();
            collider.radius = BallDiameter * 0.5f;
            collider.material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(DemoAssetPaths.BallPhysicsMaterial);

            root.AddComponent<Ball>();

            var model = InstantiateModel(DemoAssetPaths.BallModel, root.transform, "Model");
            if (model != null)
            {
                ScaleToDiameter(model, BallDiameter);
                StripColliders(model);
            }

            SavePrefab(root, DemoAssetPaths.BallPrefab);
        }

        // ----------------------------------------------------------------- glove

        private static void BuildGlovePrefab()
        {
            var aimRayMaterial = CreateLitMaterial(DemoAssetPaths.AimRayMaterial, new Color(0.55f, 0.9f, 1f));
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(DemoAssetPaths.GlovePrefab);
            var existingCatch = existing != null ? existing.transform.Find("CatchVolume") : null;
            var catchPosition = existingCatch != null ? existingCatch.localPosition : new Vector3(0f, 0f, 0.12f);
            var existingCatchCollider = existingCatch != null ? existingCatch.GetComponent<SphereCollider>() : null;
            var catchRadius = existingCatchCollider != null ? existingCatchCollider.radius : 0.21f;
            var aimRayMinimumLength = 0.75f;
            var aimRayMaximumLength = 2.5f;
            var aimRayWidth = 0.012f;
            var aimRayColor = new Color(0.55f, 0.9f, 1f, 0.85f);
            var grabWindowDuration = 0.3f;
            var existingController = existing != null ? existing.GetComponent<GloveController>() : null;
            if (existingController != null)
            {
                var existingSettings = new SerializedObject(existingController);
                var minimumLengthProperty = existingSettings.FindProperty("_aimRayMinimumLength");
                var maximumLengthProperty = existingSettings.FindProperty("_aimRayMaximumLength");
                var widthProperty = existingSettings.FindProperty("_aimRayWidth");
                var colorProperty = existingSettings.FindProperty("_aimRayColor");
                var grabWindowProperty = existingSettings.FindProperty("_grabWindowDuration");
                if (minimumLengthProperty != null) aimRayMinimumLength = minimumLengthProperty.floatValue;
                if (maximumLengthProperty != null) aimRayMaximumLength = maximumLengthProperty.floatValue;
                if (widthProperty != null) aimRayWidth = widthProperty.floatValue;
                if (colorProperty != null)
                {
                    var existingColor = colorProperty.colorValue;
                    aimRayColor = Approximately(existingColor, new Color(0.1f, 0.9f, 1f, 0.85f))
                        ? new Color(0.55f, 0.9f, 1f, 0.85f)
                        : existingColor;
                }
                if (grabWindowProperty != null)
                    grabWindowDuration = Mathf.Approximately(grabWindowProperty.floatValue, 1f)
                        ? 0.3f
                        : grabWindowProperty.floatValue;
            }

            var root = new GameObject("Glove");
            var glove = root.AddComponent<GloveController>();

            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);

            var gloveMaterial = AssetDatabase.LoadAssetAtPath<Material>(DemoAssetPaths.GloveMaterial);
            var bodyModel = InstantiateModel(DemoAssetPaths.GloveBodyModel, model.transform, "GloveBody");
            var handModel = InstantiateModel(DemoAssetPaths.GloveHandModel, model.transform, "GloveHand");
            ApplyMaterial(bodyModel, gloveMaterial);
            ApplyMaterial(handModel, gloveMaterial);
            StripColliders(model);

            // The source models are authored much larger than a hand; bring the whole glove
            // down to something that reads as a fist at arm's length.
            ScaleToDiameter(model, GloveSize);

            Animator animator = null;
            if (handModel != null)
            {
                // Note: ?? does not work on Unity objects - a missing component is a non-null
                // wrapper around a null native pointer.
                animator = handModel.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = handModel.AddComponent<Animator>();
                }

                animator.runtimeAnimatorController =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DemoAssetPaths.GloveHandController);
                animator.applyRootMotion = false;
            }

            var gripAnchor = new GameObject("GripAnchor");
            gripAnchor.transform.SetParent(root.transform, false);
            gripAnchor.transform.localPosition = new Vector3(0f, -0.0467f, 0.2447f);
            gripAnchor.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);

            var catchGo = new GameObject("CatchVolume");
            catchGo.transform.SetParent(root.transform, false);
            // This is palm-forward rather than near the wrist so a held grip catches at the palm.
            catchGo.transform.localPosition = catchPosition;
            var catchCollider = catchGo.AddComponent<SphereCollider>();
            catchCollider.isTrigger = true;
            catchCollider.radius = catchRadius;
            var catchVolume = catchGo.AddComponent<CatchVolume>();

            var aimRayGo = new GameObject("AimRay");
            aimRayGo.transform.SetParent(root.transform, false);
            var aimRay = aimRayGo.AddComponent<LineRenderer>();
            aimRay.sharedMaterial = aimRayMaterial;
            aimRay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            aimRay.receiveShadows = false;
            aimRay.textureMode = LineTextureMode.Stretch;
            aimRay.enabled = false;

            var chargeAudio = root.AddComponent<AudioSource>();
            chargeAudio.spatialBlend = 1f;
            chargeAudio.playOnAwake = false;
            chargeAudio.loop = true;
            chargeAudio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(DemoAssetPaths.SpringChargeClip);

            var so = new SerializedObject(glove);
            so.FindProperty("_catchVolume").objectReferenceValue = catchVolume;
            so.FindProperty("_aimRay").objectReferenceValue = aimRay;
            so.FindProperty("_aimRayMinimumLength").floatValue = aimRayMinimumLength;
            so.FindProperty("_aimRayMaximumLength").floatValue = aimRayMaximumLength;
            so.FindProperty("_aimRayWidth").floatValue = aimRayWidth;
            so.FindProperty("_aimRayColor").colorValue = aimRayColor;
            so.FindProperty("_grabWindowDuration").floatValue = grabWindowDuration;
            so.FindProperty("_gripAnchor").objectReferenceValue = gripAnchor.transform;
            so.FindProperty("_handAnimator").objectReferenceValue = animator;
            so.FindProperty("_chargeAudio").objectReferenceValue = chargeAudio;
            so.FindProperty("_releaseClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(DemoAssetPaths.SpringReleaseClip);
            so.ApplyModifiedPropertiesWithoutUndo();

            SavePrefab(root, DemoAssetPaths.GlovePrefab);
        }

        // -------------------------------------------------------------- launcher

        private static void BuildLauncherPrefab()
        {
            var material = CreateLitMaterial(DemoAssetPaths.LauncherMaterial, new Color(0.6f, 0.15f, 0.2f));

            var root = new GameObject("BallLauncher");
            var launcher = root.AddComponent<BallLauncher>();

            // The trigger has to sit on the root: with no Rigidbody, trigger messages are only
            // delivered to the GameObject that owns the collider.
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, -0.35f, 0f);
            trigger.size = new Vector3(0.95f, 1.75f, 0.95f);

            var body = CreatePrimitive(PrimitiveType.Cube, root.transform, "Body", material);
            body.transform.localPosition = new Vector3(0f, -0.65f, 0f);
            body.transform.localScale = new Vector3(0.7f, 1.3f, 0.7f);

            var barrel = CreatePrimitive(PrimitiveType.Cylinder, root.transform, "Barrel", material);
            barrel.transform.localPosition = new Vector3(0f, 0f, -0.25f);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            barrel.transform.localScale = new Vector3(0.22f, 0.35f, 0.22f);

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(root.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0f, -0.6f);

            var lightGo = new GameObject("WarningLight");
            lightGo.transform.SetParent(muzzle.transform, false);
            var warnLight = lightGo.AddComponent<Light>();
            warnLight.type = LightType.Point;
            warnLight.color = new Color(1f, 0.4f, 0.2f);
            warnLight.intensity = 3f;
            warnLight.range = 4f;
            warnLight.enabled = false;

            var so = new SerializedObject(launcher);
            so.FindProperty("_muzzle").objectReferenceValue = muzzle.transform;
            so.FindProperty("_warningLight").objectReferenceValue = warnLight;
            so.FindProperty("_bodyRenderer").objectReferenceValue = body.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            SavePrefab(root, DemoAssetPaths.LauncherPrefab);
        }

        // ----------------------------------------------------------- target panel

        private static void BuildTargetPanelPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(DemoAssetPaths.TargetPanelPrefab);
            var existingCollider = existing != null ? existing.transform.Find("HitCollider") ?? existing.transform.Find("HitTrigger") : null;
            var colliderPosition = existingCollider != null ? existingCollider.localPosition : new Vector3(0f, 0f, .02f);
            var colliderRotation = existingCollider != null ? existingCollider.localRotation : Quaternion.Euler(90f, 0f, 0f);
            var colliderScale = existingCollider != null ? existingCollider.localScale : new Vector3(1.2f, 0.2f, 1.2f);
            var idleColor = new Color(0.15f, 0.45f, 0.75f);
            var hitColor = new Color(1f, 0.85f, 0.25f);
            var flashDuration = 0.2f;
            var existingPanel = existing != null ? existing.GetComponent<TargetPanel>() : null;
            if (existingPanel != null)
            {
                var settings = new SerializedObject(existingPanel);
                idleColor = settings.FindProperty("_idleColor").colorValue;
                hitColor = settings.FindProperty("_hitColor").colorValue;
                flashDuration = settings.FindProperty("_flashDuration").floatValue;
                var offsetProperty = settings.FindProperty("_hitVolumeOffset");
                var scaleProperty = settings.FindProperty("_hitVolumeScale");
                if (offsetProperty != null) colliderPosition = offsetProperty.vector3Value;
                if (scaleProperty != null) colliderScale = scaleProperty.vector3Value;
            }

            var cyan = CreateLitMaterial(DemoAssetPaths.MaterialsDir + "/TargetCyan.mat", new Color(0.05f, 0.9f, 1f));
            var light = CreateLitMaterial(DemoAssetPaths.MaterialsDir + "/TargetLight.mat", new Color(0.96f, 0.96f, 0.9f));
            var center = CreateLitMaterial(DemoAssetPaths.MaterialsDir + "/TargetCenter.mat", new Color(1f, 0.15f, 0.35f));

            var root = new GameObject("TargetPanel");

            var panel = root.AddComponent<TargetPanel>();
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            // A convex cylinder gives the circular panel a pass-through 3D trigger. Its 0.6 m
            // depth remains deliberate: a 21 m/s ball travels 0.42 m in one fixed step. Position
            // and size live on this child Transform, so Prefab Inspector tuning survives BuildDemo.
            var colliderObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            colliderObject.name = "HitCollider";
            colliderObject.transform.SetParent(root.transform, false);
            colliderObject.transform.localPosition = colliderPosition;
            colliderObject.transform.localRotation = colliderRotation;
            colliderObject.transform.localScale = colliderScale;
            var colliderMesh = colliderObject.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(colliderObject.GetComponent<Renderer>());
            Object.DestroyImmediate(colliderObject.GetComponent<Collider>());
            Object.DestroyImmediate(colliderObject.GetComponent<MeshFilter>());
            var hitCollider = colliderObject.AddComponent<MeshCollider>();
            hitCollider.sharedMesh = colliderMesh;
            hitCollider.convex = true;
            hitCollider.isTrigger = true;

            BuildBullseyeRing(root.transform, "CyanRing", cyan, 0.68f, 0.075f);
            BuildBullseyeRing(root.transform, "LightRing", light, 0.47f, 0.09f);
            BuildBullseyeRing(root.transform, "Center", center, 0.22f, 0.105f);

            var so = new SerializedObject(panel);
            so.FindProperty("_renderer").objectReferenceValue = root.transform.Find("CyanRing").GetComponent<Renderer>();
            so.FindProperty("_idleColor").colorValue = idleColor;
            so.FindProperty("_hitColor").colorValue = hitColor;
            so.FindProperty("_flashDuration").floatValue = flashDuration;
            so.FindProperty("_hitVolume").objectReferenceValue = hitCollider;
            so.FindProperty("_hitVolumeOffset").vector3Value = colliderPosition;
            so.FindProperty("_hitVolumeScale").vector3Value = colliderScale;
            so.ApplyModifiedPropertiesWithoutUndo();

            SavePrefab(root, DemoAssetPaths.TargetPanelPrefab);
        }

        private static void BuildBullseyeRing(Transform parent, string name, Material material, float radius, float depth)
        {
            var ring = CreatePrimitive(PrimitiveType.Cylinder, parent, name, material);
            // TargetLayoutPlanner aims the panel's local +Z at the player. Layer the bullseye
            // discs forward so the circular target remains readable without a rectangular frame.
            ring.transform.localPosition = new Vector3(0f, 0f, depth);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = new Vector3(radius, 0.025f, radius);
            StripColliders(ring);
        }

        // ----------------------------------------------------------------- utils

        private static GameObject InstantiateModel(string assetPath, Transform parent, string name)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (source == null)
            {
                Debug.LogError($"[DemoPrefabs] model not found: {assetPath}");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            // Model prefabs are read-only assets; unpack so the demo prefab owns its own copy.
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            return instance;
        }

        private static void ScaleToDiameter(GameObject go, float diameter)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
            }

            var largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (largest <= 1e-4f)
            {
                return;
            }

            var factor = diameter / largest;
            go.transform.localScale = go.transform.localScale * factor;
            Debug.Log($"[DemoPrefabs] scaled {go.name} by {factor:0.000} (source size {largest:0.000} m)");
        }

        private static void StripColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(c);
            }
        }

        private static void ApplyMaterial(GameObject go, Material material)
        {
            if (go == null || material == null)
            {
                return;
            }

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var materials = r.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }

                r.sharedMaterials = materials;
            }
        }

        private static GameObject CreatePrimitive(PrimitiveType type, Transform parent, string name, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }

            return go;
        }

        private static Material CreateLitMaterial(string path, Color color)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.color = color;
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static bool Approximately(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < .0001f && Mathf.Abs(a.g - b.g) < .0001f &&
            Mathf.Abs(a.b - b.b) < .0001f && Mathf.Abs(a.a - b.a) < .0001f;

        private static void SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path, out var success);
            Object.DestroyImmediate(root);
            if (success)
            {
                Debug.Log($"[DemoPrefabs] saved {path}");
            }
            else
            {
                Debug.LogError($"[DemoPrefabs] failed to save {path}");
            }
        }
    }
}
