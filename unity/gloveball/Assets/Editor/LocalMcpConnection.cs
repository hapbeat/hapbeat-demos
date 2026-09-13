using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit, local-only Editor connection. Does not modify scenes or enter Play Mode.</summary>
public static class LocalMcpConnection
{
    [MenuItem("Hapbeat/Development/Connect Local MCP")]
    public static async void Connect()
    {
        EditorUtility.audioMasterMute = true;
        EditorPrefs.SetBool("MCPForUnity.TelemetryDisabled", true);
        EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", false);
        var config = EditorConfigurationCache.Instance;
        config.SetUseHttpTransport(true);
        config.SetHttpTransportScope("local");
        config.SetHttpBaseUrl("http://127.0.0.1:8091");
        bool connected = await MCPServiceLocator.Bridge.StartAsync();
        Debug.Log($"[LocalMcpConnection] Connected={connected}; endpoint=127.0.0.1:8091");
    }
}
