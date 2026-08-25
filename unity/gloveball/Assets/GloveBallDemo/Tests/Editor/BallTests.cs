using System.Collections;
using System.Reflection;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;

namespace GloveBallDemo.Tests
{
    public class BallTests
    {
        private GameObject _ballObject;
        private GameObject _anchorObject;
        private Ball _ball;

        [SetUp]
        public void SetUp()
        {
            _ballObject = new GameObject("Ball");
            _ballObject.SetActive(false);
            _ballObject.AddComponent<Rigidbody>();
            _ball = _ballObject.AddComponent<Ball>();
            _ballObject.SetActive(true);
            typeof(Ball).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_ball, null);
            _anchorObject = new GameObject("HoldAnchor");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_ballObject);
            Object.DestroyImmediate(_anchorObject);
        }

        [UnityTest]
        public IEnumerator HeldBallSnapsToHoldAnchor()
        {
            yield return null;

            _anchorObject.transform.SetPositionAndRotation(
                new Vector3(4f, 5f, 6f),
                Quaternion.Euler(25f, 40f, 55f));

            _ball.LaunchIncoming(new Vector3(1f, 2f, 3f), Vector3.zero);
            Assert.IsTrue(_ball.TryGrab(_anchorObject.transform));
            Assert.IsTrue(_ball.Body.isKinematic);
            Assert.That(_ball.transform.parent, Is.EqualTo(_anchorObject.transform));
            Assert.That(_ball.transform.localPosition, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void GeneratedBallPrefabUsesAboutFiveSecondFlightLifetime()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GloveBallDemo/Prefabs/Ball.prefab");
            var ball = prefab != null ? prefab.GetComponent<Ball>() : null;
            Assert.That(ball, Is.Not.Null);
            var lifetime = new SerializedObject(ball).FindProperty("_maxLifetime").floatValue;
            Assert.That(lifetime, Is.EqualTo(5f).Within(0.01f));
        }

        [Test]
        public void HeldBallDoesNotExpireAtFlightLifetime()
        {
            _ball.LaunchIncoming(Vector3.zero, Vector3.zero);
            Assert.That(_ball.TryGrab(_anchorObject.transform), Is.True);
            typeof(Ball).GetField("_stateAge", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_ball, 100f);
            typeof(Ball).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_ball, null);
            Assert.That(_ball.State, Is.EqualTo(BallState.Held));
            Assert.That(_ballObject.activeSelf, Is.True);
        }

    }
}
