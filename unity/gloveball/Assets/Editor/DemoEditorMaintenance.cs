using UnityEditor;
using UnityEngine;
using Unity.Pipeline.Editor;

public static class DemoEditorMaintenance
{
    [MenuItem("Hapbeat/Development/Restore Game Audio")]
    public static void RestoreGameAudio()
    {
        GloveBallDemo.Editor.BatchOps.UnmuteEditorAudio();
        AudioListener.volume = 1f;
        AudioListener.pause = false;
    }

    [MenuItem("Hapbeat/Development/Repair Editor Warnings")]
    public static void RepairWarnings()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play Mode before repairing assets.");
        // Pipeline CLI is optional alongside MCP. Start explicitly from Window/Pipeline when needed.
        var settings = EditorPipelineManager.Load();
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<EditorPipelineManager>();
            AssetDatabase.CreateAsset(settings,"Assets/Editor/GloveBallPipelineSettings.asset");
        }
        var serialized = new SerializedObject(settings);
        serialized.FindProperty("m_AutoStart").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        settings.StopServer();

        const string obstacle = "Assets/UltimateGloveBall/Prefabs/Arena/Obstacles/Obstacle1.prefab";
        var root = PrefabUtility.LoadPrefabContents(obstacle);
        int removed = 0;
        try
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            if (removed > 0) PrefabUtility.SaveAsPrefabAsset(root,obstacle);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Debug.Log($"[DemoEditorMaintenance] Pipeline auto-start disabled; removed {removed} unresolved Obstacle1 components. No scene changed.");
    }
}
