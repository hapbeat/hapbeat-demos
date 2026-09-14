using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GloveBallDemo.Editor
{
    /// <summary>Ball haptics belong to the demo Kit, not the visual-art directory.</summary>
    public static class BallImpactKit
    {
        public static readonly string[] Kinds = { "Bowling", "Volleyball", "Foam", "Basketball", "Perforated" };
        public static string ClipPath(string kind) => $"{DemoAssetPaths.KitStreamClipsDir}/{kind.ToLowerInvariant()}_impact.wav";
        public static bool IsBallClip(string filename)
        {
            foreach (var kind in Kinds)
                if (filename == Path.GetFileName(ClipPath(kind))) return true;
            return false;
        }

        [MenuItem("GloveBall Demo/Migrate Ball Impacts To Kit (no scene changes)")]
        public static void Migrate()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before migrating assets.");
            // Validate every move before changing anything. Never overwrite a different authored asset.
            foreach (var kind in Kinds)
            {
                var oldPath = $"Assets/GloveBallDemo/Art/Balls/{kind}Impact.wav";
                if (File.Exists(oldPath) && File.Exists(ClipPath(kind)))
                    throw new InvalidOperationException($"Both old and Kit clips exist: {kind}");
                if (!File.Exists(oldPath) && !File.Exists(ClipPath(kind)))
                    throw new InvalidOperationException($"Missing clip: {kind}");
            }
            foreach (var kind in Kinds)
            {
                var oldPath = $"Assets/GloveBallDemo/Art/Balls/{kind}Impact.wav";
                if (!File.Exists(oldPath)) continue;
                var error = AssetDatabase.MoveAsset(oldPath, ClipPath(kind));
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            }
            RegisterManifest();
            Debug.Log("[BallImpactKit] 5 clips and 15 events registered. GUIDs, EventMaps and scenes preserved.");
        }

        public static void RegisterManifest()
        {
            var manifest = JObject.Parse(File.ReadAllText(DemoAssetPaths.KitManifest));
            var events = (JObject)manifest["stream_events"];
            foreach (var kind in Kinds)
            {
                if (!File.Exists(ClipPath(kind))) continue;
                foreach (var surface in new[] { "l_arm_collide", "r_arm_collide", "body_collide" })
                {
                    string id = $"gloveball-kit.{kind.ToLowerInvariant()}_{surface}";
                    if (events[id] != null) continue; // Preserve later manifest authoring.
                    events[id] = new JObject {
                        ["clip"] = Path.GetFileName(ClipPath(kind)),
                        ["description"] = $"{kind} / {surface}",
                        ["parameters"] = new JObject { ["intensity"] = 1.0, ["loop"] = false }
                    };
                }
            }
            File.WriteAllText(DemoAssetPaths.KitManifest, manifest.ToString() + "\n");
            AssetDatabase.ImportAsset(DemoAssetPaths.KitManifest, ImportAssetOptions.ForceSynchronousImport);
        }

        public static string PreserveManifestEntries(string generated)
        {
            if (!File.Exists(DemoAssetPaths.KitManifest)) return generated;
            var previous = (JObject)JObject.Parse(File.ReadAllText(DemoAssetPaths.KitManifest))["stream_events"];
            var next = JObject.Parse(generated);
            foreach (var kind in Kinds)
                foreach (var surface in new[] { "l_arm_collide", "r_arm_collide", "body_collide" })
                {
                    string id = $"gloveball-kit.{kind.ToLowerInvariant()}_{surface}";
                    if (previous[id] != null) next["stream_events"][id] = previous[id].DeepClone();
                }
            return next.ToString() + "\n";
        }
    }
}
