using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hapbeat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Editor
{
    // Authored geometry and tactile waveforms; source audio provenance is in THIRD_PARTY_NOTICES.md.
    public static class BoxingContent
    {
        private const string Root = "Assets/Boxing/";
        [MenuItem("Hapbeat Boxing/Update Models And Surface Feedback")]
        public static void Upgrade()
        {
            var scene = EditorSceneManager.OpenScene(BoxingProject.ScenePath);
            var game = UnityEngine.Object.FindFirstObjectByType<BoxingGame>();
            ConfigureModels(game); ConfigureImpactVisuals(game.presentation); ConfigureFeedback(game);
            game.tuning.gloveRadius = 0.09f; EditorUtility.SetDirty(game.tuning);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            BoxingProject.Validate();
            Preview();
        }
        public static void Preview()
        {
            BoxingProject.Validate();
            var game = UnityEngine.Object.FindFirstObjectByType<BoxingGame>();
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false); game.Initialize();
            var pose = new BoxerPose { valid = true, head = new Vector3(0, 1.65f, 0), left = new Vector3(-0.25f, 1.25f, 0.34f), right = new Vector3(0.25f, 1.25f, 0.34f),
                headRotation = Quaternion.identity, leftRotation = Quaternion.identity, rightRotation = Quaternion.identity, leftClosed = true, rightClosed = true };
            game.menu.Close(); game.Round.Start(90); game.Round.Tick(3, false);
            var camera = game.input.headCamera; camera.transform.SetPositionAndRotation(pose.head, Quaternion.identity);
            game.presentation.Render(game, pose, true); Directory.CreateDirectory("Logs");
            BoxingVerification.Render(camera, "Logs/boxing-content-rest.png");
            for (int i = 0; i < 2000 && game.Opponent.GuardWeight < 0.99f; i++) game.Opponent.Tick(0.01f, pose.head, true);
            game.presentation.Render(game, pose, true);
            BoxingVerification.Render(camera, "Logs/boxing-content-guard.png");
        }

        private static Material Material(string name, Color color, bool unlit = false)
        {
            string path = Root + "Art/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", color);
            if (unlit)
            {
                material.SetFloat("_Surface", 1); material.SetFloat("_SrcBlend", 5); material.SetFloat("_DstBlend", 10);
                material.SetFloat("_SrcBlendAlpha", 1); material.SetFloat("_DstBlendAlpha", 10); material.SetFloat("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = 3000;
            }
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
            EditorUtility.SetDirty(material); return material;
        }
        private static void Clear(Transform root)
        {
            // Only explicitly selected model/effect children are replaced; rig and gameplay objects stay intact.
            for (int i = root.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
        }
        private static Transform Shape(Transform root, string name, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root, false); go.transform.localPosition = position; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material; return go.transform;
        }
        private static void Glove(Transform root, Material leather, Material dark, bool left)
        {
            Clear(root); root.localScale = Vector3.one;
            Shape(root, "Knuckle pad", new Vector3(0, 0.005f, 0.012f), new Vector3(0.16f, 0.14f, 0.17f), leather);
            Shape(root, "Palm", new Vector3(0, -0.035f, -0.018f), new Vector3(0.128f, 0.078f, 0.145f), dark);
            Shape(root, "Thumb", new Vector3(left ? 0.061f : -0.061f, -0.037f, -0.005f), new Vector3(0.058f, 0.064f, 0.104f), leather);
            Shape(root, "Cuff", new Vector3(0, -0.009f, -0.095f), new Vector3(0.125f, 0.103f, 0.065f), leather);
        }
        public static void ConfigureModels(BoxingGame game)
        {
            var v = game.presentation;
            var ivory = Material("IconIvory", new Color(0.84f, 0.9f, 0.91f));
            var navy = Material("IconNavy", new Color(0.055f, 0.13f, 0.19f));
            var dark = Material("IconInk", new Color(0.015f, 0.028f, 0.04f));
            var amber = Material("IconAmber", new Color(1, 0.56f, 0.13f));
            var blue = Material("GloveBlue", new Color(0.07f, 0.55f, 0.73f));
            var red = Material("GloveCoral", new Color(0.83f, 0.19f, 0.16f));
            Glove(v.leftGlove, blue, dark, true); Glove(v.rightGlove, blue, dark, false);
            Glove(v.enemyLeftGlove, red, dark, true); Glove(v.enemyRightGlove, red, dark, false);
            Clear(v.enemyHead); v.enemyHead.localScale = Vector3.one;
            Shape(v.enemyHead, "Icon head", Vector3.zero, new Vector3(0.275f, 0.32f, 0.28f), ivory);
            Shape(v.enemyHead, "Visor", new Vector3(0, 0.02f, -0.135f), new Vector3(0.19f, 0.055f, 0.035f), dark);
            for (int s = -1; s <= 1; s += 2)
                Shape(v.enemyHead, "Eye", new Vector3(s * 0.047f, 0.02f, -0.151f), new Vector3(0.032f, 0.022f, 0.009f), ivory);
            Clear(v.enemyTorso); v.enemyTorso.localScale = Vector3.one;
            Shape(v.enemyTorso, "Jersey", new Vector3(0, 0.015f, 0), new Vector3(0.46f, 0.58f, 0.28f), navy);
            Shape(v.enemyTorso, "Chest panel", new Vector3(0, 0.09f, -0.153f), new Vector3(0.28f, 0.24f, 0.028f), ivory);
            Shape(v.enemyTorso, "Badge", new Vector3(0, 0.09f, -0.172f), new Vector3(0.052f, 0.076f, 0.01f), amber);
            Shape(v.enemyTorso, "Neck", new Vector3(0, 0.30f, 0), new Vector3(0.115f, 0.13f, 0.115f), ivory);
            Clear(v.enemyHip); v.enemyHip.localScale = Vector3.one;
            Shape(v.enemyHip, "Shorts", Vector3.zero, new Vector3(0.37f, 0.29f, 0.27f), navy);
            Shape(v.enemyHip, "Belt", new Vector3(0, 0.10f, -0.005f), new Vector3(0.36f, 0.037f, 0.27f), amber);
            foreach (var part in v.enemyArms.Concat(v.enemyLegs))
                foreach (var renderer in part.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = part.name == "Boot" ? dark : ivory;
            EditorUtility.SetDirty(v);
            Debug.Log("BOXING_GLOVE_BOUNDS " + GloveSize(v.leftGlove).ToString("F3"));
        }
        public static Vector3 GloveSize(Transform glove)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var filter in glove.GetComponentsInChildren<MeshFilter>())
            {
                var b = filter.sharedMesh.bounds;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                    bounds.Encapsulate(glove.InverseTransformPoint(filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z)))));
            }
            return bounds.size;
        }
        public static void ConfigureImpactVisuals(BoxingPresentation view)
        {
            var oldPanel = view.impactText.transform.parent.Find("Damage tint");
            if (oldPanel != null) UnityEngine.Object.DestroyImmediate(oldPanel.gameObject);
            Clear(view.hitBurst);
            var line = view.hitBurst.GetComponent<LineRenderer>();
            if (line == null) line = view.hitBurst.gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = Material("ContactRing", Color.white, true);
            line.useWorldSpace = false; line.loop = true; line.widthMultiplier = 0.05f; line.positionCount = 24;
            for (int i = 0; i < 24; i++) { float a = i * Mathf.PI * 2 / 24; line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0)); }
            view.hitBurst.gameObject.SetActive(false);
        }

        public static void ConfigureFeedback(BoxingGame game)
        {
            Directory.CreateDirectory(Root + "Audio"); Directory.CreateDirectory(Root + "Haptics"); AssetDatabase.Refresh();
            var f = game.feedback;
            if (f.audioSource == null) f.audioSource = game.gameObject.AddComponent<AudioSource>();
            if (f.bellSource == null) f.bellSource = game.gameObject.AddComponent<AudioSource>();
            f.audioSource.playOnAwake = f.bellSource.playOnAwake = false;
            f.audioSource.spatialBlend = f.bellSource.spatialBlend = 0;
            string[] sounds = { "impactSoft_medium_000.ogg", "impactSoft_heavy_000.ogg", "impactPunch_medium_000.ogg", "impactPunch_heavy_000.ogg" };
            f.contactSounds = sounds.Select(n => ImportAudio(Root + "Audio/" + n)).ToArray();
            var source = ImportAudio(Root + "Audio/BoxingBellSource.wav");
            ComposeGong(source, Root + "Audio/BoxingGongFour.wav");
            f.bell = ImportAudio(Root + "Audio/BoxingGongFour.wav");
            var waves = new AudioClip[4];
            for (int i = 0; i < 4; i++)
            {
                string path = Root + "Haptics/" + (i < 2 ? "Glove" : "Body") + (i % 2 == 0 ? "Soft" : "Hard") + ".wav";
                WriteWave(path, 16000, HapticSamples(i < 2, i % 2 != 0)); waves[i] = ImportAudio(path);
            }
            foreach (var trigger in f.impactTriggers) if (trigger != null) UnityEngine.Object.DestroyImmediate(trigger.gameObject);
            var map = AssetDatabase.LoadAssetAtPath<HapbeatEventMap>(Root + "Haptics/BoxingEventMap.asset");
            if (map == null) { map = ScriptableObject.CreateInstance<HapbeatEventMap>(); AssetDatabase.CreateAsset(map, Root + "Haptics/BoxingEventMap.asset"); }
            map.entries.Clear(); f.impactTriggers = new HapbeatUnityEventTrigger[12];
            for (int i = 0; i < 12; i++)
            {
                string zone = i / 4 == 0 ? "left_glove" : i / 4 == 1 ? "right_glove" : "head";
                string name = zone + (i % 4 < 2 ? "_glove_" : "_body_") + (i % 2 == 0 ? "soft" : "hard");
                var entry = new HapbeatEventEntry { displayName = name, category = "boxing", eventName = name, mode = HapticMode.StreamClip,
                    streamClip = waves[i % 4], gain = 0.8f, target = i / 4 == 0 ? "*/pos_l_wrist" : i / 4 == 1 ? "*/pos_r_wrist" : "*/pos_neck" };
                map.entries.Add(entry);
                var go = new GameObject("Haptic " + name); go.transform.SetParent(f.transform, false);
                f.impactTriggers[i] = go.AddComponent<HapbeatUnityEventTrigger>(); f.impactTriggers[i].EditorSetupEntry(map, entry.id);
            }
            foreach (string obsolete in new[] { "Soft.wav", "Hard.wav", "Bell.wav" }) AssetDatabase.DeleteAsset(Root + "Haptics/" + obsolete);
            EditorUtility.SetDirty(map); EditorUtility.SetDirty(f);
        }
        private static AudioClip ImportAudio(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Audio source missing: " + path);
            var settings = importer.defaultSampleSettings; settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM; settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
        public static float[] HapticSamples(bool glove, bool hard)
        {
            int count = (int)(16000 * (glove ? hard ? 0.10f : 0.07f : hard ? 0.19f : 0.14f));
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                double t = i / 16000.0, decay = glove ? 0.018 : 0.050;
                double envelope = Math.Min(1, t / 0.003) * Math.Exp(-t / decay);
                if (glove && hard && t > 0.035) envelope += 0.45 * Math.Min(1, (t - 0.035) / 0.002) * Math.Exp(-(t - 0.035) / 0.012);
                samples[i] = (float)(Math.Sin(2 * Math.PI * (glove ? 155 : 75) * t) * envelope * (hard ? 0.8 : 0.6));
            }
            return samples;
        }
        private static void ComposeGong(AudioClip source, string path)
        {
            var data = new float[source.samples * source.channels];
            if (!source.GetData(data, 0)) throw new InvalidOperationException("Cannot decode CC0 boxing bell.");
            var mono = new float[source.samples];
            for (int i = 0; i < mono.Length; i++) for (int c = 0; c < source.channels; c++) mono[i] += data[i * source.channels + c] / source.channels;
            int window = source.frequency / 100;
            var energy = new float[mono.Length / window];
            for (int b = 0; b < energy.Length; b++) for (int i = 0; i < window; i++) energy[b] += mono[b * window + i] * mono[b * window + i];
            var peaks = new List<int>();
            foreach (int b in Enumerable.Range(1, energy.Length - 1).OrderByDescending(b => energy[b] - energy[b - 1]))
            {
                if (peaks.All(p => Math.Abs(p - b) > 30)) peaks.Add(b);
                if (peaks.Count == 3) break;
            }
            peaks.Sort();
            int start = Math.Max(0, peaks[2] - 1) * window;
            float[] offsets = { 0, 0.24f, 0.48f, 0.88f };
            var result = new float[mono.Length - start + (int)(source.frequency * offsets[3])];
            for (int hit = 0; hit < offsets.Length; hit++)
            {
                int offset = (int)(source.frequency * offsets[hit]);
                // Damp the first three hits so overlapping tails do not mask the four attacks.
                int count = hit == 3 ? mono.Length - start : Math.Min(mono.Length - start, (int)(source.frequency * (hit == 2 ? 0.32f : 0.20f)));
                for (int i = 0; i < count; i++)
                {
                    float attack = Mathf.Min(1, i / (source.frequency * 0.003f));
                    float release = hit == 3 ? 1 : Mathf.Min(1, (count - 1 - i) / (source.frequency * 0.025f));
                    result[offset + i] += mono[start + i] * attack * release * (hit == 3 ? 1 : 0.75f);
                }
            }
            float peak = result.Max(x => Mathf.Abs(x));
            for (int i = 0; i < result.Length; i++) result[i] *= 0.85f / Mathf.Max(peak, 0.001f) * Mathf.Min(1, (result.Length - 1 - i) / (source.frequency * 0.08f));
            WriteWave(path, source.frequency, result);
            Debug.Log("BOXING_GONG: source strikes at " + string.Join(",", peaks.Select(p => (p / 100f).ToString("F2"))) + "; four strikes at 0,0.24,0.48,0.88s");
        }
        private static void WriteWave(string path, int rate, float[] samples)
        {
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
            foreach (float value in samples) writer.Write((short)(Mathf.Clamp(value, -1, 1) * 32767));
        }
    }
}
