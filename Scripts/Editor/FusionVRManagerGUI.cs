#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fusion.VR.Editor
{
    [CustomEditor(typeof(FusionVRManager))]
    public class FusionVRManagerGUI : UnityEditor.Editor
    {
        private static Texture2D logo;

        public override void OnInspectorGUI()
        {
            FusionVRManager manager = (FusionVRManager)target;

            DrawLogo();
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(10);
            DrawValidation(manager);
            DrawSetupControls(manager);
            DrawRuntimeControls(manager);
        }

        private static void DrawLogo()
        {
            if (logo == null)
                logo = Resources.Load<Texture2D>("FusionVR/Assets/FusionVRLogoNoBackSmall");

            if (logo == null)
                return;

            Rect rect = GUILayoutUtility.GetRect(1f, 90f, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(rect, logo, ScaleMode.ScaleToFit, true);
        }

        private static void DrawValidation(FusionVRManager manager)
        {
            if (string.IsNullOrWhiteSpace(manager.FusionAppId))
            {
                EditorGUILayout.HelpBox(
                    "Fusion App ID is missing. FusionVR cannot connect until it is assigned.",
                    MessageType.Error
                );
            }

            if (manager.Head == null || manager.LeftHand == null || manager.RightHand == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign the local XR Head, Left Hand, and Right Hand transforms.",
                    MessageType.Warning
                );
            }

            if (manager.VoiceAndRunner == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a runner prefab containing NetworkRunner. Photon Voice components are optional.",
                    MessageType.Error
                );
            }

            if (!string.IsNullOrWhiteSpace(manager.VoiceAppId))
            {
                EditorGUILayout.HelpBox(
                    "Voice App ID is set. Make sure the matching Fusion 2 Voice integration is installed. " +
                    "Networking still works without Voice.",
                    MessageType.Info
                );
            }
        }

        private static void DrawSetupControls(FusionVRManager manager)
        {
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                return;

            if (GUILayout.Button(new GUIContent(
                    "Auto-Fill Rig References",
                    "Find likely XR head and controller transforms and copy Photon App IDs from Photon App Settings.")))
            {
                Undo.RecordObject(manager, "Auto-Fill FusionVR References");
                manager.CheckDefaultValues();
                EditorUtility.SetDirty(manager);
            }
        }

        private static void DrawRuntimeControls(FusionVRManager manager)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode to use connection controls.",
                    MessageType.None
                );
                return;
            }

            using (new EditorGUI.DisabledScope(FusionVRManager.IsSessionOperationInProgress))
            {
                if (manager.Runner == null)
                {
                    if (GUILayout.Button("Create Runner"))
                        FusionVRManager.Connect(false);

                    if (GUILayout.Button("Connect and Join Default Queue"))
                        FusionVRManager.Connect(true);

                    return;
                }

                EditorGUILayout.LabelField("Runner State", manager.Runner.State.ToString());
                EditorGUILayout.LabelField("Game Mode", manager.Runner.GameMode.ToString());

                if (!manager.Runner.IsRunning)
                {
                    if (GUILayout.Button("Join Default Queue"))
                        _ = FusionVRManager.JoinRandomRoom(manager.DefaultQueue, manager.DefaultRoomLimit);
                }
                else
                {
                    if (GUILayout.Button("Leave Session"))
                        _ = FusionVRManager.LeaveRoomAsync();
                }
            }
        }
    }
}
#endif
