using System.Collections.Generic;
using System.Reflection;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GloveBallDemo.Tests
{
    public class BallVariantTests
    {
        private const string Path = "Assets/GloveBallDemo/Prefabs/Ball.prefab";
        [Test]
        public void InactiveTargetCanResetBeforeAwake()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.SetActive(false);
            try
            {
                var target = go.AddComponent<TargetPanel>();
                Assert.DoesNotThrow(() => target.ResetForGeneration());
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test]
        public void FiveVariantsHaveDistinctSideSpecificEventsAndOneVisibleModel()
        {
            var ball = Object.Instantiate(AssetDatabase.LoadAssetAtPath<Ball>(Path));
            try
            {
                Assert.That(ball.VariantCount, Is.EqualTo(5));
                var ids = new HashSet<DemoHapticEvent>();
                for (var i = 0; i < 5; i++)
                {
                    ball.SelectVariant(i);
                    foreach (var surface in new[] { DemoHapticEvent.LeftArmCollide, DemoHapticEvent.RightArmCollide, DemoHapticEvent.BodyCollide })
                        Assert.That(ids.Add(ball.ImpactEvent(surface)), Is.True);
                    var so = new SerializedObject(ball);
                    var variants = so.FindProperty("_visualVariants");
                    for (var j = 0; j < 5; j++)
                        Assert.That(((GameObject)variants.GetArrayElementAtIndex(j).objectReferenceValue).activeSelf, Is.EqualTo(j == i));
                }
                Assert.That(ball.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(1));
                Assert.That(ball.GetComponent<Rigidbody>().mass, Is.EqualTo(.25f));
            }
            finally { Object.DestroyImmediate(ball.gameObject); }
        }

        [Test]
        public void PoolHonorsAllowedKindsAcrossReuseAndEmptyDisablesLaunches()
        {
            var go = new GameObject("Test pool");
            var pool = go.AddComponent<BallPool>();
            var so = new SerializedObject(pool);
            so.FindProperty("_prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Ball>(Path);
            so.FindProperty("_size").intValue = 1;
            var allowed = so.FindProperty("_launchBallKinds");
            allowed.arraySize = 1;
            allowed.GetArrayElementAtIndex(0).enumValueIndex = (int)BallKind.Perforated;
            so.ApplyModifiedPropertiesWithoutUndo();
            typeof(BallPool).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(pool, null);
            Ball instance = null;
            try
            {
                for (var i = 0; i < 20; i++)
                {
                    instance = pool.Take();
                    Assert.That(instance.VariantIndex, Is.EqualTo((int)BallKind.Perforated));
                    pool.Return(instance, "test");
                }
                so.Update();
                so.FindProperty("_launchBallKinds").arraySize = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(pool.Take(), Is.Null);
                Assert.That(pool.ActiveCount, Is.Zero);
            }
            finally { if (instance != null) Object.DestroyImmediate(instance.gameObject); Object.DestroyImmediate(go); }
        }
    }
}
