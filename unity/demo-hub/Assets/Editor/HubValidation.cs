using System;
using System.IO;
using System.Linq;
using Hapbeat.DemoSwitch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;

namespace Hapbeat.DemoHub.Editor
{
    public static class HubValidation
    {
        public static void Validate()
        {
            var scene = EditorSceneManager.OpenScene(HubProject.ScenePath);
            var cameras = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>()).ToArray();
            Require(cameras.Length == 1 && cameras[0].CompareTag("MainCamera"), "One main camera");
            var head = cameras[0];
            Require(head.GetComponent<TrackedPoseDriver>() != null, "Head tracking driver");
            var texts = head.GetComponentsInChildren<Text>();
            Require(texts.Length == 4, "Four waiting labels");
            foreach (var text in texts)
            {
                Require(!text.raycastTarget && text.font != null, "Passive label with bundled font");
                text.font.RequestCharactersInTexture(text.text, text.fontSize);
                foreach (var character in text.text.Where(x => !char.IsWhiteSpace(x)))
                    Require(text.font.HasCharacter(character), "Glyph exists: " + character);
            }
            Require(texts.Any(x => x.text.Contains("正面")) && texts.Any(x => x.text.Contains("Please face forward")), "Japanese and English");
            Require(scene.GetRootGameObjects().All(x => x.GetComponentsInChildren<AudioSource>().Length == 0), "No audio sources");
            var controller = UnityEngine.Object.FindAnyObjectByType<Hapbeat.DemoHub.DemoHubController>();
            Require(controller != null, "Demo Session controller");
            var waiting = new SerializedObject(controller).FindProperty("_waitingMessage").objectReferenceValue as GameObject;
            Require(waiting != null && waiting.transform.IsChildOf(head.transform), "Session controller toggles the waiting labels");
            var font = AssetDatabase.LoadAssetAtPath<Font>(HubProject.FontPath);
            Require(font != null && texts.All(x => x.font == font), "Waiting labels use the package font");
            foreach (var value in Hapbeat.DemoHub.HubText.All)
                foreach (var character in value.Where(x => !char.IsWhiteSpace(x) && x != '{' && x != '}'))
                    Require(font.HasCharacter(character), "Hub UI glyph exists: " + character);
            var settings = Resources.Load<DemoSwitchSettings>(DemoSwitchSettings.ResourceName);
            Require(settings != null && settings.ReceiverEnabled && settings.CurrentDemoId == "demo_hub", "Hub receiver identity");
            foreach (var id in new[] { "demo_hub", "gloveball", "handdemo", "gloveball_v2", "boxing" })
                Require(settings.TryResolveTarget(id, out _), "Launch target: " + id);
            Require(settings.GhostHands && Resources.Load<Shader>(DemoGhostHands.ShaderResourcePath) != null, "Ghost hands enabled with the package shader");
            Require(EditorBuildSettings.scenes.Length == 1 && EditorBuildSettings.scenes[0].path == HubProject.ScenePath, "Hub-only build");
            Debug.Log("[Hub] Validation passed: scene, tracking, Japanese glyphs, session controller, no audio, switch allowlist, ghost hands.");
        }

        // Runs without Play Mode: no UDP receiver, sound, or haptic traffic is started.
        public static void RenderPreview()
        {
            Validate();
            var camera = Camera.main;
            var target = new RenderTexture(1600, 1000, 24);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                Directory.CreateDirectory("tools/logs");
                File.WriteAllBytes("tools/logs/hub-preview.png", pixels.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(pixels);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Hub validation failed: " + message);
        }
    }
}
