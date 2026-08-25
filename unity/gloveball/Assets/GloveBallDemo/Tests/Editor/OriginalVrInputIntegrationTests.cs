using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnityEngine.InputSystem;

namespace GloveBallDemo.Tests
{
    public sealed class OriginalVrInputIntegrationTests
    {
        [TestCase("TriggerLeft", "<XRController>{LeftHand}/triggerPressed")]
        [TestCase("TriggerRight", "<XRController>{RightHand}/triggerPressed")]
        [TestCase("LeftGrip", "<XRController>{LeftHand}/gripPressed")]
        [TestCase("RightGrip", "<XRController>{RightHand}/gripPressed")]
        [TestCase("Move", "<XRController>{LeftHand}/primary2DAxis")]
        [TestCase("SnapTurn", "<XRController>{RightHand}/primary2DAxis")]
        [TestCase("LeftMenu", "<XRController>{LeftHand}/menuButton")]
        [TestCase("LeftSecondary", "<XRController>{LeftHand}/secondaryButton")]
        [TestCase("RightSecondary", "<XRController>{RightHand}/secondaryButton")]
        [TestCase("MenuNavigate", "<XRController>{LeftHand}/primary2DAxis")]
        [TestCase("MenuNavigate", "<XRController>{RightHand}/primary2DAxis")]
        [TestCase("LeftPrimary", "<XRController>{LeftHand}/primaryButton")]
        [TestCase("RightPrimary", "<XRController>{RightHand}/primaryButton")]
        public void DemoControls_ContainsOriginalArenaBinding(string actionName, string bindingPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/GloveBallDemo/Input/DemoControls.inputactions");
            Assert.That(asset, Is.Not.Null);

            var action = asset.FindAction("XR/" + actionName);
            Assert.That(action, Is.Not.Null, $"Missing XR/{actionName}");
            Assert.That(action.bindings.Any(binding => binding.path == bindingPath), Is.True,
                $"XR/{actionName} does not bind {bindingPath}");
        }
    }
}
