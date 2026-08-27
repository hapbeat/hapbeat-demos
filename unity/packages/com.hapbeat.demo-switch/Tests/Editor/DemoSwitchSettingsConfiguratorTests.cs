using System.Collections.Generic;
using Hapbeat.DemoSwitch.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hapbeat.DemoSwitch.Tests
{
    public sealed class DemoSwitchSettingsConfiguratorTests
    {
        [Test]
        public void Apply_PreservesExistingSharedSecret()
        {
            var settings = ScriptableObject.CreateInstance<DemoSwitchSettings>();
            try
            {
                var before = new SerializedObject(settings);
                before.FindProperty("_sharedSecret").stringValue = "local-secret-must-survive";
                before.ApplyModifiedPropertiesWithoutUndo();

                DemoSwitchSettingsConfigurator.Apply(settings, "gloveball", new List<DemoSwitchTarget>
                {
                    new DemoSwitchTarget("handdemo", "com.Hapbeat.HapticHandDemo_G2", "com.unity3d.player.UnityPlayerGameActivity")
                });

                var after = new SerializedObject(settings);
                Assert.That(after.FindProperty("_sharedSecret").stringValue, Is.EqualTo("local-secret-must-survive"));
                Assert.That(settings.CurrentDemoId, Is.EqualTo("gloveball"));
                Assert.That(settings.TryResolveTarget("handdemo", out var target), Is.True);
                Assert.That(target.PackageName, Is.EqualTo("com.Hapbeat.HapticHandDemo_G2"));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
