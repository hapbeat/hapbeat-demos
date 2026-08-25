using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
            var definitionsByEvent = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                var type = definition.GetType();
                var eventName = (string)type.GetField("Name").GetValue(definition);
                Assert.That(definitionsByEvent.ContainsKey(eventName), Is.False,
                    $"duplicate catalog event name: {eventName}");
                definitionsByEvent.Add(eventName, definition);
            }

            var eventMapAsset = AssetDatabase.LoadMainAssetAtPath(EventMapPath);
            Assert.That(eventMapAsset, Is.Not.Null);
            var eventMap = new SerializedObject(eventMapAsset);
            var entries = eventMap.FindProperty("entries");
            var entriesByEvent = new Dictionary<string, SerializedProperty>(StringComparer.Ordinal);
            for (var i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                var eventName = entry.FindPropertyRelative("eventName").stringValue;
                Assert.That(entriesByEvent.ContainsKey(eventName), Is.False,
                    $"duplicate EventMap event name: {eventName}");
                entriesByEvent.Add(eventName, entry);
            }

            var manifestAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(ManifestPath);
            Assert.That(manifestAsset, Is.Not.Null);
            var manifest = manifestAsset.text;
            var kitName = (string)catalogType.GetField("KitName", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            var manifestPattern = new Regex(
                "^    \"" + Regex.Escape(kitName) +
                "\\.(?<eventName>[^\"]+)\": \\{\\r?\\n      \"clip\": \"(?<clip>[^\"]+)\",",
                RegexOptions.Multiline);
            var manifestEvents = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in manifestPattern.Matches(manifest))
            {
                var eventName = match.Groups["eventName"].Value;
                Assert.That(manifestEvents.ContainsKey(eventName), Is.False,
                    $"duplicate manifest event name: {eventName}");
                manifestEvents.Add(eventName, match.Groups["clip"].Value);
            }

            Assert.That(entriesByEvent.Count, Is.EqualTo(definitionsByEvent.Count));
            Assert.That(manifestEvents.Count, Is.EqualTo(definitionsByEvent.Count));
            foreach (var pair in definitionsByEvent)
            {
                var eventName = pair.Key;
                var definition = pair.Value;
                var type = definition.GetType();
                var clipName = (string)type.GetField("ClipName").GetValue(definition);
                var target = (string)type.GetField("Target").GetValue(definition);

                Assert.That(entriesByEvent.TryGetValue(eventName, out var entry), Is.True,
                    $"EventMap entry missing: {eventName}");
                Assert.That(entry.FindPropertyRelative("target").stringValue, Is.EqualTo(target), eventName);
                var clip = entry.FindPropertyRelative("streamClip").objectReferenceValue;
                Assert.That(clip, Is.Not.Null, eventName);
                Assert.That(AssetDatabase.GetAssetPath(clip), Is.EqualTo($"{ClipsPath}/{clipName}.wav"), eventName);

                Assert.That(manifestEvents.TryGetValue(eventName, out var manifestClip), Is.True,
                    $"manifest event missing: {eventName}");
                Assert.That(manifestClip, Is.EqualTo($"{clipName}.wav"), eventName);
            }
        }
    }
}
