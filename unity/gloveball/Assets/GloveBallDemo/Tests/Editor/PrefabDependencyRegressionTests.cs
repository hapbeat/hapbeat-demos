using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GloveBallDemo.Tests
{
    public sealed class PrefabDependencyRegressionTests
    {
        private static readonly Regex SourcePrefabGuidPattern = new Regex(
            @"m_SourcePrefab:\s*\{[^}]*\bguid:\s*(?<guid>[0-9a-f]{32})\b",
            RegexOptions.Compiled);

        [Test]
        public void EverySerializedPrefabSourceGuidResolves()
        {
            var unresolved = new List<string>();
            foreach (var prefabPath in Directory.GetFiles(
                         Application.dataPath, "*.prefab", SearchOption.AllDirectories))
            {
                var assetPath = "Assets" + prefabPath.Substring(Application.dataPath.Length)
                    .Replace('\\', '/');
                var contents = File.ReadAllText(prefabPath);
                foreach (Match match in SourcePrefabGuidPattern.Matches(contents))
                {
                    var guid = match.Groups["guid"].Value;
                    if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                    {
                        unresolved.Add($"{assetPath}: {guid}");
                    }
                }
            }

            Assert.That(unresolved, Is.Empty,
                "Prefab m_SourcePrefab GUIDs must resolve on a clean clone:\n" +
                string.Join("\n", unresolved));
        }
    }
}
