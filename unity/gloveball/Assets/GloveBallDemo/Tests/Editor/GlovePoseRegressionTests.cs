using NUnit.Framework;
using System.Linq;
using System.Reflection;
using GloveBallDemo.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloveBallDemo.Tests
{
    public sealed class GlovePoseRegressionTests
    {
        [TestCase("LeftHand", -1f)]
        [TestCase("RightHand", 1f)]
        public void GeneratedDemoGloveVisualPoseMatchesPreGuardTrackedHandPose(string handName, float xScale)
        {
            var scene = EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/Demo.unity", OpenSceneMode.Additive);
            try
            {
                var hand = FindTransform(scene, handName);
                Assert.That(hand, Is.Not.Null, $"{handName} is missing from Demo.unity");
                Assert.That(Quaternion.Angle(hand.localRotation, Quaternion.identity), Is.LessThan(0.001f));
                Assert.That(hand.localScale, Is.EqualTo(Vector3.one));
                var glove = hand.Find("Glove");
                Assert.That(glove, Is.Not.Null, $"{handName}/Glove is missing from Demo.unity");
                Assert.That(Quaternion.Angle(glove.localRotation, Quaternion.identity), Is.LessThan(0.001f));
                Assert.That(glove.localScale, Is.EqualTo(new Vector3(xScale, 1f, 1f)));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void GeneratedCourtLetsBallsPassInvisibleWallsAndAddsNonBlockingVisibleFenceColliders()
        {
            var scene = EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/Demo.unity", OpenSceneMode.Additive);
            try
            {
                var walls = FindTransform(scene, "Walls");
                Assert.That(walls, Is.Not.Null, "source court Walls are missing");
                foreach (var collider in walls.GetComponentsInChildren<Collider>(true))
                    Assert.That(collider.enabled, Is.False, $"{collider.name} still reflects balls at the court edge");

                foreach (var fenceName in new[] { "Railing_front", "Railing_back.001" })
                {
                    var fence = FindTransform(scene, fenceName);
                    Assert.That(fence, Is.Not.Null, $"visible fence {fenceName} is missing");
                    var collider = fence.GetComponent<Collider>();
                    Assert.That(collider, Is.Not.Null, $"visible fence {fenceName} needs a collider");
                    Assert.That(collider.isTrigger, Is.True, $"balls must pass through {fenceName} without reflecting");
                }

                XrLocomotionController locomotion = null;
                foreach (var root in scene.GetRootGameObjects())
                    locomotion = root.GetComponentInChildren<XrLocomotionController>(true) ?? locomotion;
                Assert.That(locomotion, Is.Not.Null);
                var properties = new SerializedObject(locomotion);
                Assert.That(properties.FindProperty("_playAreaSize").vector2Value.x, Is.GreaterThan(1f));
                Assert.That(properties.FindProperty("_playAreaSize").vector2Value.y, Is.GreaterThan(1f));
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void StickLocomotionConstrainsTheTrackedHeadInsideTheGeneratedPlayArea()
        {
            var rig = new GameObject("Rig");
            var head = new GameObject("Head");
            head.transform.SetParent(rig.transform, false);
            var locomotion = rig.AddComponent<XrLocomotionController>();
            try
            {
                var properties = new SerializedObject(locomotion);
                properties.FindProperty("_head").objectReferenceValue = head.transform;
                properties.FindProperty("_playAreaCenter").vector2Value = Vector2.zero;
                properties.FindProperty("_playAreaSize").vector2Value = new Vector2(10f, 20f);
                properties.FindProperty("_boundaryPadding").floatValue = 0.25f;
                properties.ApplyModifiedPropertiesWithoutUndo();

                typeof(XrLocomotionController)
                    .GetMethod("MoveWithinPlayArea", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(locomotion, new object[] { new Vector3(100f, 0f, -100f) });

                Assert.That(head.transform.position.x, Is.EqualTo(4.75f).Within(0.001f));
                Assert.That(head.transform.position.z, Is.EqualTo(-9.75f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(rig);
            }
        }

        [Test]
        public void GeneratedGlovePrefabAndDemoSceneContainNoGuardContent()
        {
            var glovePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GloveBallDemo/Prefabs/Glove.prefab");
            Assert.That(glovePrefab, Is.Not.Null, "Glove.prefab is missing");
            AssertNoGuardContent(glovePrefab);

            var scene = EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/Demo.unity", OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                    AssertNoGuardContent(root);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void GeneratedDemoSceneLeavesOriginalObstacleAndPostGameManagersInactive()
        {
            var scene = EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/Demo.unity", OpenSceneMode.Additive);
            try
            {
                AssertInactive(scene, "ObstaclesManager");
                AssertInactive(scene, "PostGame");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void GeneratedTargetPrefabHasNoOuterFrameRenderer()
        {
            var targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GloveBallDemo/Prefabs/TargetPanel.prefab");
            Assert.That(targetPrefab, Is.Not.Null, "TargetPanel.prefab is missing");
            foreach (var renderer in targetPrefab.GetComponentsInChildren<Renderer>(true))
                Assert.That(renderer.name, Is.Not.EqualTo("OuterFrame"));
        }

        [Test]
        public void GeneratedTargetPrefabUsesACircularConvexTriggerWithoutPhysicsMaterial()
        {
            var targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GloveBallDemo/Prefabs/TargetPanel.prefab");
            Assert.That(targetPrefab, Is.Not.Null);
            Assert.That(targetPrefab.GetComponentsInChildren<BoxCollider>(true), Is.Empty);
            var hitCollider = targetPrefab.transform.Find("HitCollider");
            Assert.That(hitCollider, Is.Not.Null, "HitCollider child is the Inspector-adjustable physical pose/size");
            var mesh = hitCollider.GetComponent<MeshCollider>();
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.isTrigger, Is.True);
            Assert.That(mesh.convex, Is.True);
            Assert.That(mesh.sharedMaterial, Is.Null);
            Assert.That(targetPrefab.GetComponent<TargetPanel>(), Is.Not.Null);
            Assert.That(targetPrefab.GetComponent<Rigidbody>().isKinematic, Is.True);
        }

        [Test]
        public void GeneratedDemoExposesRoundTuningAndContainsNoLivesUi()
        {
            var scene = EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/Demo.unity", OpenSceneMode.Additive);
            try
            {
                var controllerTransform = FindTransform(scene, "DemoGameController");
                var controller = controllerTransform != null ? controllerTransform.GetComponent<DemoGameController>() : null;
                Assert.That(controller, Is.Not.Null);
                var settings = new SerializedObject(controller);
                Assert.That(settings.FindProperty("_roundCount").intValue, Is.EqualTo(3));
                Assert.That(settings.FindProperty("_roundDuration").floatValue, Is.EqualTo(30f));
                Assert.That(settings.FindProperty("_roundIntermission").floatValue, Is.EqualTo(3f));
                Assert.That(settings.FindProperty("_targetMinCount").intValue, Is.EqualTo(3));
                Assert.That(settings.FindProperty("_targetMaxCount").intValue, Is.EqualTo(4));
                Assert.That(FindTransform(scene, "Lives"), Is.Null);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void GeneratedGlovePrefabWiresThePalmGripAnchor()
        {
            var glovePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GloveBallDemo/Prefabs/Glove.prefab");
            var anchor = glovePrefab != null ? glovePrefab.transform.Find("GripAnchor") : null;
            Assert.That(anchor, Is.Not.Null, "Glove.prefab needs GripAnchor");
            Assert.That(anchor.localPosition, Is.EqualTo(new Vector3(0f, -0.0467f, 0.2447f)));
            Assert.That(Quaternion.Angle(anchor.localRotation, Quaternion.Euler(10f, 0f, 0f)), Is.LessThan(0.001f));
            var controller = glovePrefab.GetComponent<GloveController>();
            var property = new SerializedObject(controller).FindProperty("_gripAnchor");
            Assert.That(property.objectReferenceValue, Is.EqualTo(anchor));
            var catchTransform = glovePrefab.transform.Find("CatchVolume");
            Assert.That(catchTransform, Is.Not.Null, "CatchVolume position is editable on the prefab child Transform");
            Assert.That(catchTransform.GetComponent<SphereCollider>(), Is.Not.Null,
                "CatchVolume size is editable through SphereCollider.radius in the Prefab Inspector");
        }

        [Test]
        public void GeneratedGlovePrefabWiresAnInspectorTunableHeldBallAimRay()
        {
            var glovePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GloveBallDemo/Prefabs/Glove.prefab");
            var controller = glovePrefab != null ? glovePrefab.GetComponent<GloveController>() : null;
            Assert.That(controller, Is.Not.Null);
            var properties = new SerializedObject(controller);
            var ray = properties.FindProperty("_aimRay").objectReferenceValue as LineRenderer;
            Assert.That(ray, Is.Not.Null);
            Assert.That(ray.name, Is.EqualTo("AimRay"));
            Assert.That(properties.FindProperty("_aimRayMinimumLength").floatValue, Is.GreaterThan(0f));
            Assert.That(properties.FindProperty("_aimRayMaximumLength").floatValue, Is.EqualTo(2.5f).Within(.001f));
            Assert.That(properties.FindProperty("_aimRayWidth").floatValue, Is.GreaterThan(0f));
            Assert.That(properties.FindProperty("_aimRayColor").colorValue,
                Is.EqualTo(new Color(.55f, .9f, 1f, .85f)));
            Assert.That(Vector4.Distance(ray.sharedMaterial.color, new Color(.55f, .9f, 1f, 1f)), Is.LessThan(.001f));
            Assert.That(properties.FindProperty("_grabWindowDuration").floatValue, Is.EqualTo(.3f).Within(.001f));
            Assert.That(ray.enabled, Is.False);
        }

        [Test]
        public void BuildGlovePrefabPreservesInspectorTunedAimRaySettings()
        {
            const string prefabPath = "Assets/GloveBallDemo/Prefabs/Glove.prefab";
            var builderType = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("GloveBallDemo.Editor.DemoPrefabBuilder"))
                .FirstOrDefault(type => type != null);
            var buildMethod = builderType?.GetMethod("BuildGlovePrefab", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(buildMethod, Is.Not.Null);

            var controller = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponent<GloveController>();
            var serialized = new SerializedObject(controller);
            var originalMinimumLength = serialized.FindProperty("_aimRayMinimumLength").floatValue;
            var originalMaximumLength = serialized.FindProperty("_aimRayMaximumLength").floatValue;
            var originalWidth = serialized.FindProperty("_aimRayWidth").floatValue;
            var originalColor = serialized.FindProperty("_aimRayColor").colorValue;
            var originalGrabWindow = serialized.FindProperty("_grabWindowDuration").floatValue;
            var tunedMinimumLength = originalMinimumLength + .12f;
            var tunedMaximumLength = originalMaximumLength + .37f;
            var tunedWidth = originalWidth + .004f;
            var tunedColor = new Color(.75f, .2f, .9f, .6f);
            var tunedGrabWindow = originalGrabWindow + .25f;

            try
            {
                SetAimRaySettings(controller, tunedMinimumLength, tunedMaximumLength, tunedWidth, tunedColor, tunedGrabWindow);
                buildMethod.Invoke(null, null);

                controller = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponent<GloveController>();
                serialized = new SerializedObject(controller);
                Assert.That(serialized.FindProperty("_aimRayMinimumLength").floatValue, Is.EqualTo(tunedMinimumLength).Within(.0001f));
                Assert.That(serialized.FindProperty("_aimRayMaximumLength").floatValue, Is.EqualTo(tunedMaximumLength).Within(.0001f));
                Assert.That(serialized.FindProperty("_aimRayWidth").floatValue, Is.EqualTo(tunedWidth).Within(.0001f));
                Assert.That(serialized.FindProperty("_aimRayColor").colorValue, Is.EqualTo(tunedColor));
                Assert.That(serialized.FindProperty("_grabWindowDuration").floatValue, Is.EqualTo(tunedGrabWindow).Within(.0001f));
            }
            finally
            {
                controller = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponent<GloveController>();
                SetAimRaySettings(controller, originalMinimumLength, originalMaximumLength, originalWidth, originalColor, originalGrabWindow);
                buildMethod.Invoke(null, null);
            }
        }

        [Test]
        public void GeneratedTargetRootExposesSafeThreeDimensionalLayoutBounds()
        {
            var scene = EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/Demo.unity", OpenSceneMode.Additive);
            try
            {
                var targets = FindTransform(scene, "Targets");
                var field = targets != null ? targets.GetComponent<TargetLayoutField>() : null;
                Assert.That(field, Is.Not.Null);
                var properties = new SerializedObject(field);
                Assert.That(properties.FindProperty("_minHeight").floatValue, Is.EqualTo(.7f).Within(.05f));
                Assert.That(properties.FindProperty("_maxHeight").floatValue, Is.GreaterThan(3f));
                Assert.That(properties.FindProperty("_minZ").floatValue, Is.EqualTo(0f).Within(.001f));
                Assert.That(properties.FindProperty("_maxZ").floatValue, Is.GreaterThan(7f));
                Assert.That(properties.FindProperty("_minimumSpacing").floatValue, Is.EqualTo(2f).Within(.001f));
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void GeneratedTargetPrefabExposesAModestlyInsetHitVolume()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GloveBallDemo/Prefabs/TargetPanel.prefab");
            var panel = prefab != null ? prefab.GetComponent<TargetPanel>() : null;
            Assert.That(panel, Is.Not.Null);
            var properties = new SerializedObject(panel);
            var collider = properties.FindProperty("_hitVolume").objectReferenceValue as MeshCollider;
            Assert.That(collider, Is.Not.Null);
            Assert.That(properties.FindProperty("_hitVolumeOffset").vector3Value, Is.EqualTo(new Vector3(0f, 0f, .02f)));
            Assert.That(properties.FindProperty("_hitVolumeScale").vector3Value, Is.EqualTo(new Vector3(1.2f, .2f, 1.2f)));
            Assert.That(collider.transform.localScale.x * .5f, Is.LessThan(.68f),
                "hit radius stays modestly inside the visible CyanRing radius");
        }

        [TestCase("LeftHand")]
        [TestCase("RightHand")]
        public void GeneratedDemoGloveKeepsCatchVolumeAndGripAnchorWiring(string handName)
        {
            var scene = EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/Demo.unity", OpenSceneMode.Additive);
            try
            {
                var hand = FindTransform(scene, handName);
                var controller = hand != null ? hand.GetComponentInChildren<GloveController>(true) : null;
                Assert.That(controller, Is.Not.Null, $"{handName} needs GloveController");
                var properties = new SerializedObject(controller);
                var catchVolume = properties.FindProperty("_catchVolume").objectReferenceValue as CatchVolume;
                var gripAnchor = properties.FindProperty("_gripAnchor").objectReferenceValue as Transform;
                Assert.That(catchVolume, Is.Not.Null, $"{handName} CatchVolume wiring was lost during BuildDemo");
                Assert.That(gripAnchor, Is.Not.Null, $"{handName} GripAnchor wiring was lost during BuildDemo");
                Assert.That(gripAnchor.name, Is.EqualTo("GripAnchor"));
                Assert.That(controller.GripAnchor, Is.EqualTo(gripAnchor));
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void GeneratedDemoSceneContainsAClosedQuestMenuAndBodyHitAudioBinding()
        {
            var scene = EditorSceneManager.OpenScene("Assets/GloveBallDemo/Scenes/Demo.unity", OpenSceneMode.Additive);
            try
            {
                var menu = FindTransform(scene, "QuestMenu");
                Assert.That(menu, Is.Not.Null, "QuestMenu is missing from Demo.unity");
                var menuController = menu.GetComponent<QuestMenuController>();
                Assert.That(menuController, Is.Not.Null);
                var panel = menu.Find("Panel");
                Assert.That(panel, Is.Not.Null);
                Assert.That(panel.gameObject.activeSelf, Is.False);
                var menuProperties = new SerializedObject(menuController);
                Assert.That(menuProperties.FindProperty("_game").objectReferenceValue, Is.Not.Null);
                Assert.That(menuProperties.FindProperty("_panel").objectReferenceValue, Is.Not.Null);
                foreach (var inputName in new[]
                {
                    "_leftMenuInput",
                    "_leftSecondaryInput",
                    "_rightSecondaryInput",
                    "_navigateInput",
                    "_leftPrimaryInput",
                    "_rightPrimaryInput"
                })
                {
                    var input = menuProperties.FindProperty(inputName);
                    Assert.That(input, Is.Not.Null, $"QuestMenu is missing {inputName}");
                    Assert.That(input.FindPropertyRelative("m_UseReference").boolValue, Is.True, $"{inputName} must use an action reference");
                    Assert.That(input.FindPropertyRelative("m_Reference").objectReferenceValue, Is.Not.Null, $"{inputName} is unassigned");
                }

                HapticEventRelay relay = null;
                foreach (var root in scene.GetRootGameObjects())
                    relay = root.GetComponentInChildren<HapticEventRelay>(true) ?? relay;
                Assert.That(relay, Is.Not.Null);
                var bindings = new SerializedObject(relay).FindProperty("_bindings");
                var found = false;
                for (var i = 0; i < bindings.arraySize; i++)
                {
                    var binding = bindings.GetArrayElementAtIndex(i);
                    if (binding.FindPropertyRelative("Event").enumValueIndex == (int)DemoHapticEvent.BodyCollide)
                    {
                        found = true;
                        Assert.That(binding.FindPropertyRelative("Clip").objectReferenceValue, Is.Not.Null);
                    }
                }
                Assert.That(found, Is.True);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static void SetAimRaySettings(GloveController controller, float minimumLength, float maximumLength, float width, Color color, float grabWindow)
        {
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("_aimRayMinimumLength").floatValue = minimumLength;
            serialized.FindProperty("_aimRayMaximumLength").floatValue = maximumLength;
            serialized.FindProperty("_aimRayWidth").floatValue = width;
            serialized.FindProperty("_aimRayColor").colorValue = color;
            serialized.FindProperty("_grabWindowDuration").floatValue = grabWindow;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        private static void AssertNoGuardContent(GameObject root)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                Assert.That(transform.name, Does.Not.Contain("Shield"));
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                Assert.That(component, Is.Not.Null, $"{root.name} has a missing component");
                Assert.That(component.GetType().Name, Is.Not.EqualTo("PlayerShieldController"));
            }
        }

        private static void AssertInactive(Scene scene, string name)
        {
            var target = FindTransform(scene, name);
            Assert.That(target, Is.Not.Null, $"{name} is missing from Demo.unity");
            Assert.That(target.gameObject.activeSelf, Is.False, $"{name} must be inactive in Demo.unity");
        }

        private static Transform FindTransform(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform.name == name)
                    {
                        return transform;
                    }
                }
            }

            return null;
        }
    }
}
