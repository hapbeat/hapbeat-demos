using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GloveBallDemo.Tests
{
    public class EditorMaintenanceTests
    {
        [Test]
        public void InteractiveAssetSetupAndMcpDoNotPersistMute()
        {
            foreach (var path in new[] { "Assets/Editor/LocalMcpConnection.cs", "Assets/GloveBallDemo/Scripts/Editor/BallFeelSetup.cs" })
                Assert.That(File.ReadAllText(path), Does.Not.Contain("audioMasterMute = true"), path);
        }

        [Test]
        public void ObstaclePrefabHasNoMissingBehaviours()
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UltimateGloveBall/Prefabs/Arena/Obstacles/Obstacle1.prefab");
            Assert.That(root,Is.Not.Null);
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject),Is.Zero,t.name);
        }

        [Test]
        public void RenderGraphIsEnabledAndRendererHasNoLegacyFeatures()
        {
            Assert.That(File.ReadAllText("Assets/UniversalRenderPipelineGlobalSettings.asset"),Does.Contain("m_EnableRenderCompatibilityMode: 0"));
            Assert.That(File.ReadAllText("Assets/Settings/Ultra_PipelineAsset_ForwardRenderer.asset"),Does.Contain("m_RendererFeatures: []"));
        }
    }
}
