using UnityEngine;
using UnityEditor;
using Yueby.Utils;
using Yueby.ModalWindow;

namespace Yueby
{
    public class MirrorToolSettingsDrawer : ModalEditorWindowDrawer<object>
    {
        public MirrorToolSettingsDrawer()
        {
            this.Title = "Mirror Tool Settings";

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float helpBoxHeight = 40f;
            float buttonHeight = 25f;
            float totalHeight = lineHeight * 4 + helpBoxHeight * 2 + buttonHeight + lineHeight * 5;

            position = new Rect(0, 0, 300, totalHeight);
        }

        public override void OnDraw()
        {
            EditorUI.VerticalEGL(() =>
            {
                EditorUI.VerticalEGL(new GUIStyle("Badge"), () =>
                {
                    EditorUI.TitleLabelField("Visualization");
                    MirrorTool.ShowMirrorAxis = EditorUI.Toggle(MirrorTool.ShowMirrorAxis, "Show Mirror Axis");
                    EditorGUILayout.Space(2);
                    EditorGUILayout.HelpBox("Show or hide mirror axis in scene view", MessageType.Info);
                });

                EditorGUILayout.Space(5);

                EditorUI.VerticalEGL(new GUIStyle("Badge"), () =>
                {
                    EditorUI.TitleLabelField("Component Sync");
                    MirrorTool.ComponentSyncEnabled = EditorUI.Toggle(MirrorTool.ComponentSyncEnabled, "Enable Component Sync");
                    EditorGUILayout.Space(2);
                    EditorGUILayout.HelpBox("Sync component property changes from source to mirror targets (matched by type and index)", MessageType.Info);
                });

                EditorGUILayout.Space(5);

                EditorUI.VerticalEGL(new GUIStyle("Badge"), () =>
                {
                    EditorUI.TitleLabelField("Data Management");
                    if (GUILayout.Button("Clear All Mirror Data", GUILayout.Height(25)))
                    {
                        if (EditorUtility.DisplayDialog("Clear Mirror Data",
                            "Are you sure you want to clear all mirror data? This action cannot be undone.",
                            "Yes", "No"))
                        {
                            MirrorTool.ClearAllData();
                            Close();
                        }
                    }
                });
            });
        }
    }
}