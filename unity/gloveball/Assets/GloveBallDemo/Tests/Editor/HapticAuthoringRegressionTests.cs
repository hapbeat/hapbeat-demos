using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

namespace GloveBallDemo.Tests
{
    public sealed class HapticAuthoringRegressionTests
    {
        private const string EventMapPath = "Assets/GloveBallDemo/Haptics/GloveBallEventMap.asset";
        private const string ManifestPath = "Assets/GloveBallDemo/Kits/gloveball-kit/gloveball-kit-manifest.json";
        private const string ClipsPath = "Assets/GloveBallDemo/Kits/gloveball-kit/stream-clips";

        [Test]
        public void CatalogManifestAndEventMapUseTheSameSharedMonoClipsAndTargets()
        {
            var catalogType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("GloveBallDemo.Editor.DemoHapticCatalog"))
                .FirstOrDefault(type => type != null);
            Assert.That(catalogType, Is.Not.Null, "DemoHapticCatalog did not compile");

            var definitions = (Array)catalogType.GetField("Definitions", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            var eventNames = new HashSet<string>(StringComparer.Ordinal);
            var eventMap = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(EventMapPath));
            var entries = eventMap.FindProperty("entries");
            var manifestAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(ManifestPath);
            Assert.That(manifestAsset, Is.Not.Null);
            var manifest = manifestAsset.text;

            Assert.That(entries.arraySize, Is.EqualTo(definitions.Length));
            for (var i = 0; i < definitions.Length; i++)
            {
                var definition = definitions.GetValue(i);
                var type = definition.GetType();
                var eventName = (string)type.GetField("Name").GetValue(definition);
                var clipName = (string)type.GetField("ClipName").GetValue(definition);
                var target = (string)type.GetField("Target").GetValue(definition);
                Assert.That(eventNames.Add(eventName), Is.True, $"duplicate event name: {eventName}");

                var entry = entries.GetArrayElementAtIndex(i);
                Assert.That(entry.FindPropertyRelative("eventName").stringValue, Is.EqualTo(eventName));
                Assert.That(entry.FindPropertyRelative("target").stringValue, Is.EqualTo(target), eventName);
                var clip = entry.FindPropertyRelative("streamClip").objectReferenceValue;
                Assert.That(clip, Is.Not.Null, eventName);
                Assert.That(AssetDatabase.GetAssetPath(clip), Is.EqualTo($"{ClipsPath}/{clipName}.wav"), eventName);

                var eventIndex = manifest.IndexOf($"\"gloveball-kit.{eventName}\"", StringComparison.Ordinal);
                Assert.That(eventIndex, Is.GreaterThanOrEqualTo(0), eventName);
                var clipIndex = manifest.IndexOf($"\"clip\": \"{clipName}.wav\"", eventIndex, StringComparison.Ordinal);
                Assert.That(clipIndex, Is.InRange(eventIndex, eventIndex + 300), eventName);
            }
        }
    }
}
