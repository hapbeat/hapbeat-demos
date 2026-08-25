using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GloveBallDemo.Editor
{
    /// <summary>
    /// Generates DemoControls.inputactions. Hand-editing the asset is not an option in this
    /// project (everything is built from script), so the bindings live here.
    /// </summary>
    public static class DemoInputBuilder
    {
        public const string MapName = "XR";
        public const string HeadPosition = "HeadPosition";
        public const string HeadRotation = "HeadRotation";
        public const string LeftPosition = "LeftPosition";
        public const string LeftRotation = "LeftRotation";
        public const string RightPosition = "RightPosition";
        public const string RightRotation = "RightRotation";
        public const string LeftGrip = "LeftGrip";
        public const string RightGrip = "RightGrip";
        public const string TriggerLeft = "TriggerLeft";
        public const string TriggerRight = "TriggerRight";
        public const string Move = "Move";
        public const string SnapTurn = "SnapTurn";
        public const string LeftMenu = "LeftMenu";
        public const string LeftSecondary = "LeftSecondary";
        public const string RightSecondary = "RightSecondary";
        public const string MenuNavigate = "MenuNavigate";
        public const string LeftPrimary = "LeftPrimary";
        public const string RightPrimary = "RightPrimary";

        [MenuItem("GloveBall Demo/Build Input Actions")]
        public static void BuildInputActions()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "DemoControls";

            var map = asset.AddActionMap(MapName);

            AddPose(map, HeadPosition, "Vector3", "<XRHMD>/centerEyePosition");
            AddPose(map, HeadRotation, "Quaternion", "<XRHMD>/centerEyeRotation");
            AddPose(map, LeftPosition, "Vector3", "<XRController>{LeftHand}/devicePosition");
            AddHandRotation(map, LeftRotation, "LeftHand");
            AddPose(map, RightPosition, "Vector3", "<XRController>{RightHand}/devicePosition");
            AddHandRotation(map, RightRotation, "RightHand");

            AddButton(map, LeftGrip, "<XRController>{LeftHand}/gripPressed");
            AddButton(map, RightGrip, "<XRController>{RightHand}/gripPressed");

            // Grip physically holds a contacted ball; trigger charges its release.
            AddButton(map, TriggerLeft, "<XRController>{LeftHand}/triggerPressed");
            AddButton(map, TriggerRight, "<XRController>{RightHand}/triggerPressed");
            AddPose(map, Move, "Vector2", "<XRController>{LeftHand}/primary2DAxis");
            AddPose(map, SnapTurn, "Vector2", "<XRController>{RightHand}/primary2DAxis");

            // The runtime menu consumes these only while open; gameplay keeps its normal inputs
            // otherwise, with no XR ray or EventSystem dependency.
            AddButton(map, LeftMenu, "<XRController>{LeftHand}/menuButton");
            AddButton(map, LeftSecondary, "<XRController>{LeftHand}/secondaryButton");
            AddButton(map, RightSecondary, "<XRController>{RightHand}/secondaryButton");
            var navigate = map.AddAction(MenuNavigate, InputActionType.Value, expectedControlLayout: "Vector2");
            navigate.AddBinding("<XRController>{LeftHand}/primary2DAxis");
            navigate.AddBinding("<XRController>{RightHand}/primary2DAxis");
            AddButton(map, LeftPrimary, "<XRController>{LeftHand}/primaryButton");
            AddButton(map, RightPrimary, "<XRController>{RightHand}/primaryButton");

            Directory.CreateDirectory(DemoAssetPaths.InputDir);
            File.WriteAllText(DemoAssetPaths.InputActions, asset.ToJson());
            Object.DestroyImmediate(asset);

            AssetDatabase.ImportAsset(DemoAssetPaths.InputActions, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.Refresh();

            var imported = AssetDatabase.LoadAssetAtPath<InputActionAsset>(DemoAssetPaths.InputActions);
            if (imported == null)
            {
                Debug.LogError($"[DemoInput] failed to import {DemoAssetPaths.InputActions}");
                return;
            }

            Debug.Log($"[DemoInput] wrote {DemoAssetPaths.InputActions} with {imported.FindActionMap(MapName).actions.Count} actions");
        }

        private static void AddPose(InputActionMap map, string name, string controlType, string binding)
        {
            var action = map.AddAction(name, InputActionType.Value, expectedControlLayout: controlType);
            action.AddBinding(binding);
        }

        private static void AddButton(InputActionMap map, string name, string binding)
        {
            var action = map.AddAction(name, InputActionType.Button, expectedControlLayout: "Button");
            action.AddBinding(binding);
        }

        private static void AddHandRotation(InputActionMap map, string name, string handUsage)
        {
            var action = map.AddAction(name, InputActionType.Value, expectedControlLayout: "Quaternion");

            // OpenXR deviceRotation is the grip pose. Its axes describe an object held in a fist,
            // so applying it directly to this glove model makes the fingers point away from the
            // controller's pointing direction. Prefer the aim pose used by controller visuals and
            // rays. XR Device Simulator only exposes deviceRotation, hence the standard XRI
            // QuaternionFallback composite keeps desk simulation working.
            action.AddCompositeBinding("QuaternionFallback")
                .With("first", $"<XRController>{{{handUsage}}}/pointerRotation")
                .With("second", $"<XRController>{{{handUsage}}}/deviceRotation");
        }

        /// <summary>Finds the InputActionReference sub-asset the importer generated for an action.</summary>
        public static InputActionReference FindReference(string actionName)
        {
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(DemoAssetPaths.InputActions))
            {
                if (o is InputActionReference reference && reference.action != null && reference.action.name == actionName)
                {
                    return reference;
                }
            }

            Debug.LogWarning($"[DemoInput] no InputActionReference for '{actionName}'");
            return null;
        }
    }
}
