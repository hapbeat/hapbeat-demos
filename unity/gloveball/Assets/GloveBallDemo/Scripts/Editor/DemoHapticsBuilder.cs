using System.Collections.Generic;
using System.IO;
using System.Text;
using Hapbeat;
using Hapbeat.Editor;
using UnityEditor;
using UnityEngine;

namespace GloveBallDemo.Editor
{
    /// <summary>
    /// Generates everything the Hapbeat SDK needs before the scene is built: the config asset,
    /// the demo Kit (clips + manifest) and the EventMap. All script-driven, like the rest of
    /// this project - none of it is hand-authored in the editor.
    /// </summary>
    public static class DemoHapticsBuilder
    {
        private const string PackageName = "com.hapbeat.sdk";

        [MenuItem("GloveBall Demo/Build Haptics")]
        public static void BuildHaptics()
        {
            EnsureConfig();
            EnsureKit();
            BuildEventMap();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // ---------------------------------------------------------------- config

        private static void EnsureConfig()
        {
            EnsureFolder(DemoAssetPaths.ResourcesDir);

            var config = AssetDatabase.LoadAssetAtPath<HapbeatConfig>(DemoAssetPaths.HapbeatConfig);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<HapbeatConfig>();
                AssetDatabase.CreateAsset(config, DemoAssetPaths.HapbeatConfig);
            }

            // Shown on the device's OLED while this app holds the connection, so an operator can
            // tell which demo a headset is driving. Capped at 16 characters by the protocol.
            config.appName = "GloveBallDemo";
            EditorUtility.SetDirty(config);
            Debug.Log($"[DemoHaptics] config appName='{config.appName}' at {DemoAssetPaths.HapbeatConfig}");
        }

        // ------------------------------------------------------------------- kit

        /// <summary>
        /// Copies the clips this demo borrows from the SDK's showcase kit into a kit of its own,
        /// renamed after the shared mono clips in the catalog, and writes the manifest that goes with them.
        /// The <see cref="HapbeatKitsReadme"/> marker is what tells the SDK that this folder -
        /// rather than its own default - is where this project keeps its kits.
        /// </summary>
        private static void EnsureKit()
        {
            EnsureFolder(DemoAssetPaths.KitsDir);
            EnsureFolder(DemoAssetPaths.KitDir);
            EnsureFolder(DemoAssetPaths.KitStreamClipsDir);
            EnsureKitsMarker();

            var sourceDir = Path.Combine(ResolvePackagePath(), DemoHapticCatalog.SourceKitRelativePath);
            if (!Directory.Exists(sourceDir))
            {
                Debug.LogError($"[DemoHaptics] SDK sample kit not found: {sourceDir}");
                return;
            }

            // The catalog is the whole public haptic vocabulary. Remove clips left behind by
            // renamed or deleted events so an obsolete event cannot survive in the built Kit.
            var clips = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var definition in DemoHapticCatalog.Definitions)
            {
                if (clips.TryGetValue(definition.ClipName, out var existingSource))
                {
                    if (!string.Equals(existingSource, definition.SourceClip, System.StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.LogError($"[DemoHaptics] shared clip '{definition.ClipName}' has conflicting sources: " +
                                       $"'{existingSource}' and '{definition.SourceClip}'");
                        return;
                    }

                    continue;
                }

                clips.Add(definition.ClipName, definition.SourceClip);
            }

            foreach (var file in Directory.GetFiles(DemoAssetPaths.KitStreamClipsDir, "*.wav"))
            {
                if (clips.ContainsKey(Path.GetFileNameWithoutExtension(file)) || BallImpactKit.IsBallClip(Path.GetFileName(file)))
                {
                    continue;
                }

                AssetDatabase.DeleteAsset(file.Replace('\\', '/'));
            }

            var copied = 0;
            foreach (var clip in clips)
            {
                var source = Path.Combine(sourceDir, clip.Value);
                if (!File.Exists(source))
                {
                    Debug.LogError($"[DemoHaptics] source clip missing: {source}");
                    continue;
                }

                var destination = $"{DemoAssetPaths.KitStreamClipsDir}/{clip.Key}.wav";
                File.Copy(source, destination, overwrite: true);
                AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
                copied++;
            }

            WriteManifest();
            AssetDatabase.Refresh();
            Debug.Log($"[DemoHaptics] kit '{DemoHapticCatalog.KitName}' has {copied} clips");
        }

        private static void EnsureKitsMarker()
        {
            var path = $"{DemoAssetPaths.KitsDir}/HapbeatKitsReadme.asset";
            if (AssetDatabase.LoadAssetAtPath<HapbeatKitsReadme>(path) != null)
            {
                return;
            }

            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<HapbeatKitsReadme>(), path);
            Debug.Log($"[DemoHaptics] kits root marker at {path}");
        }

        /// <summary>
        /// Kit manifest, schema 2.0.0. Every event goes in the <c>stream_events</c> bucket
        /// because the demo streams every clip.
        ///
        /// Intensity is 1.0 across the board on purpose. The SDK multiplies gain by the
        /// manifest intensity before sending, so authoring a level in both places would mean
        /// two half-truths; the balance lives in the EventMap's gains alone.
        /// </summary>
        private static void WriteManifest()
        {
            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"schema_version\": \"2.0.0\",");
            json.AppendLine("  \"version\": \"1.0.0\",");
            json.AppendLine($"  \"name\": \"{DemoHapticCatalog.KitName}\",");
            json.AppendLine("  \"description\": \"Glove Ball solo demo. Clips borrowed from the SDK showcase kit; " +
                            "levels are set by the EventMap gains, so every intensity here is 1.0.\",");
            json.AppendLine("  \"author\": \"\",");
            json.AppendLine("  \"events\": {},");
            json.AppendLine("  \"stream_events\": {");

            for (var i = 0; i < DemoHapticCatalog.Definitions.Length; i++)
            {
                var definition = DemoHapticCatalog.Definitions[i];
                var comma = i == DemoHapticCatalog.Definitions.Length - 1 ? "" : ",";
                json.AppendLine($"    \"{DemoHapticCatalog.EventId(definition)}\": {{");
                json.AppendLine($"      \"clip\": \"{definition.ClipName}.wav\",");
                json.AppendLine($"      \"description\": \"{definition.DisplayName}\",");
                json.AppendLine("      \"parameters\": {");
                json.AppendLine("        \"intensity\": 1.0,");
                json.AppendLine($"        \"loop\": {(definition.Loop ? "true" : "false")}");
                json.AppendLine("      }");
                json.AppendLine($"    }}{comma}");
            }

            json.AppendLine("  }");
            json.AppendLine("}");

            File.WriteAllText(DemoAssetPaths.KitManifest, BallImpactKit.PreserveManifestEntries(json.ToString()));
            BallImpactKit.RegisterManifest();
            AssetDatabase.ImportAsset(DemoAssetPaths.KitManifest, ImportAssetOptions.ForceSynchronousImport);
        }

        // -------------------------------------------------------------- event map

        private static void BuildEventMap()
        {
            EnsureFolder(DemoAssetPaths.HapticsDir);

            var map = AssetDatabase.LoadAssetAtPath<HapbeatEventMap>(DemoAssetPaths.EventMap);
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<HapbeatEventMap>();
                AssetDatabase.CreateAsset(map, DemoAssetPaths.EventMap);
            }

            var existingEntries = new Dictionary<string, HapbeatEventEntry>(System.StringComparer.Ordinal);
            foreach (var entry in map.entries)
            {
                if (!existingEntries.TryAdd(entry.eventName, entry))
                {
                    Debug.LogError($"[DemoHaptics] duplicate existing EventMap entry '{entry.eventName}'");
                    return;
                }
            }

            var rebuiltEntries = new List<HapbeatEventEntry>();
            foreach (var definition in DemoHapticCatalog.Definitions)
            {
                existingEntries.TryGetValue(definition.Name, out var entry);
                entry ??= new HapbeatEventEntry();
                ConfigureEntry(entry, definition);
                rebuiltEntries.Add(entry);
            }

            // Keep stable entry ids so rebuilding haptics cannot break scene trigger references.
            map.entries.Clear();
            map.entries.AddRange(rebuiltEntries);

            BakeManifestIntensities(map);

            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
            Debug.Log($"[DemoHaptics] event map with {map.entries.Count} entries at {DemoAssetPaths.EventMap}");
        }

        private static void ConfigureEntry(HapbeatEventEntry entry, DemoHapticDefinition definition)
        {
            var clipPath = $"{DemoAssetPaths.KitStreamClipsDir}/{definition.ClipName}.wav";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            if (clip == null)
            {
                Debug.LogError($"[DemoHaptics] clip not imported: {clipPath}");
            }

            entry.mode = HapticMode.StreamClip;
            entry.displayName = definition.DisplayName;
            entry.category = DemoHapticCatalog.KitName;
            entry.eventName = definition.Name;
            entry.streamClip = clip;
            entry.loop = definition.Loop;
            entry.gain = definition.Gain;
            entry.target = definition.Target;
            entry.notes = definition.Notes;

            // Force the lazy id to materialise now, while the asset is being written: triggers
            // reference entries by this id, and an id assigned later would not be persisted.
            _ = entry.id;
        }

        /// <summary>
        /// Bakes each entry's manifest intensity onto the entry. The player build ships no
        /// manifest, so an unbaked entry falls back to raw gain and warns at runtime.
        /// </summary>
        private static void BakeManifestIntensities(HapbeatEventMap map)
        {
            HapbeatManifestIntensity.Invalidate();

            var resolved = 0;
            foreach (var entry in map.entries)
            {
                if (HapbeatManifestIntensity.TryResolve(entry, out var intensity, out _))
                {
                    entry.SetCachedManifestIntensity(intensity);
                    resolved++;
                }
                else
                {
                    Debug.LogWarning($"[DemoHaptics] no manifest intensity for {entry.eventId}");
                }
            }

            Debug.Log($"[DemoHaptics] baked manifest intensity for {resolved}/{map.entries.Count} entries");
        }

        // ----------------------------------------------------------------- utils

        /// <summary>Absolute path of the SDK package, wherever it is resolved from.</summary>
        private static string ResolvePackagePath()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(
                $"Packages/{PackageName}/package.json");
            if (info == null)
            {
                Debug.LogError($"[DemoHaptics] package {PackageName} is not installed");
                return string.Empty;
            }

            return info.resolvedPath;
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            var name = Path.GetFileName(assetPath);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
