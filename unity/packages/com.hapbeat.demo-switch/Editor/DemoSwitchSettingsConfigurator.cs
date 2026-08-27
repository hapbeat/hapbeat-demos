using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hapbeat.DemoSwitch.Editor
{
    public static class DemoSwitchSettingsConfigurator
    {
        private const string AssetDirectory = "Assets/Resources";
        private const string AssetPath = AssetDirectory + "/" + DemoSwitchSettings.ResourceName + ".asset";

        public static void ConfigureFromCommandLine()
        {
            var arguments = Environment.GetCommandLineArgs();
            var currentDemoId = ReadRequiredValue(arguments, "-demoSwitchCurrentDemo");
            var targets = ReadTargets(arguments);

            Configure(currentDemoId, targets);
        }

        internal static void Configure(string currentDemoId, IReadOnlyList<DemoSwitchTarget> targets)
        {
            if (!Directory.Exists(AssetDirectory)) Directory.CreateDirectory(AssetDirectory);
            var settings = AssetDatabase.LoadAssetAtPath<DemoSwitchSettings>(AssetPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<DemoSwitchSettings>();
                AssetDatabase.CreateAsset(settings, AssetPath);
            }

            Apply(settings, currentDemoId, targets);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Demo Switch] Configured '{currentDemoId}' with {targets.Count} allowlisted target(s) at {AssetPath}.");
        }

        internal static void Apply(DemoSwitchSettings settings, string currentDemoId, IReadOnlyList<DemoSwitchTarget> targets)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!DemoSwitchProtocol.IsIdentifier(currentDemoId))
                throw new ArgumentException("Current demo ID is invalid.", nameof(currentDemoId));
            if (targets == null || targets.Count == 0)
                throw new ArgumentException("At least one switch target is required.", nameof(targets));

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in targets)
            {
                if (target == null || !DemoSwitchProtocol.IsIdentifier(target.DemoId) ||
                    string.IsNullOrWhiteSpace(target.PackageName) || string.IsNullOrWhiteSpace(target.ActivityName))
                    throw new ArgumentException("Each target requires a valid demo ID, package, and activity.", nameof(targets));
                if (string.Equals(currentDemoId, target.DemoId, StringComparison.Ordinal) || !seenIds.Add(target.DemoId))
                    throw new ArgumentException("Target demo IDs must be unique and different from the current demo ID.", nameof(targets));
            }

            var serialized = new SerializedObject(settings);
            serialized.FindProperty("_receiverEnabled").boolValue = true;
            serialized.FindProperty("_port").intValue = 7710;
            serialized.FindProperty("_currentDemoId").stringValue = currentDemoId;
            serialized.FindProperty("_allowUnsignedOnIsolatedLan").boolValue = true;

            var serializedTargets = serialized.FindProperty("_targets");
            serializedTargets.arraySize = targets.Count;
            for (var index = 0; index < targets.Count; index++)
            {
                var item = serializedTargets.GetArrayElementAtIndex(index);
                item.FindPropertyRelative("_demoId").stringValue = targets[index].DemoId;
                item.FindPropertyRelative("_packageName").stringValue = targets[index].PackageName;
                item.FindPropertyRelative("_activityName").stringValue = targets[index].ActivityName;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }

        private static List<DemoSwitchTarget> ReadTargets(IReadOnlyList<string> arguments)
        {
            var targets = new List<DemoSwitchTarget>();
            for (var index = 0; index < arguments.Count; index++)
            {
                if (!string.Equals(arguments[index], "-demoSwitchTarget", StringComparison.Ordinal)) continue;
                if (index + 3 >= arguments.Count)
                    throw new ArgumentException("-demoSwitchTarget requires <demo-id> <package> <activity>.");
                targets.Add(new DemoSwitchTarget(arguments[index + 1], arguments[index + 2], arguments[index + 3]));
                index += 3;
            }
            return targets;
        }

        private static string ReadRequiredValue(IReadOnlyList<string> arguments, string flag)
        {
            for (var index = 0; index < arguments.Count - 1; index++)
            {
                if (string.Equals(arguments[index], flag, StringComparison.Ordinal)) return arguments[index + 1];
            }
            throw new ArgumentException(flag + " is required.");
        }
    }
}
