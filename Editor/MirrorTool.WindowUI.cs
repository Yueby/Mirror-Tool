using UnityEngine;
using UnityEditor;
using System.Linq;
using Yueby.Utils;
using Yueby.ModalWindow;

namespace Yueby
{
    public partial class MirrorTool
    {
        private static bool DrawCloseButton(Rect windowRect)
        {
            string closeIcon = EditorGUIUtility.isProSkin ? "d_winbtn_win_close" : "winbtn_win_close";
            float iconSize = EditorGUIUtility.singleLineHeight;
            float padding = 2;

            Rect closeRect = new Rect(
                windowRect.width - iconSize - padding,
                padding,
                iconSize,
                iconSize
            );

            GUIContent closeContent = EditorGUIUtility.IconContent(closeIcon);
            if (GUI.Button(closeRect, closeContent, EditorStyles.iconButton))
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        private static void DrawMirrorWindow(int id)
        {
            if (currentSelectedObject == null) return;

            float iconSize = EditorGUIUtility.singleLineHeight;
            float padding = 2;
            string detailsIcon = EditorGUIUtility.isProSkin ? "d_Settings" : "Settings";
            GUIContent buttonContent = EditorGUIUtility.IconContent(detailsIcon);
            buttonContent.tooltip = "Toggle Details";

            Rect detailsButtonRect = new Rect(
                padding,
                padding,
                iconSize,
                iconSize
            );

            if (GUI.Button(detailsButtonRect, buttonContent, EditorStyles.iconButton))
            {
                isDetailWindowVisible = !isDetailWindowVisible;
                if (isDetailWindowVisible)
                {
                    needUpdateDetailPosition = true;
                }
                SaveWindowSettings();
            }

            if (DrawCloseButton(windowRect))
            {
                isWindowVisible = false;
                SaveWindowSettings();
                return;
            }

            EditorUI.VerticalEGL(() =>
            {
                string sourcePath = currentSelectedObjectPath;
                if (string.IsNullOrEmpty(sourcePath)) return;
                if (!mirrorConfigs.ContainsKey(sourcePath))
                {
                    mirrorConfigs[sourcePath] = new MirrorConfig();
                    InitializeTargetList(mirrorConfigs[sourcePath]);
                }

                MirrorConfig config = mirrorConfigs[sourcePath];

                EditorUI.VerticalEGL(new GUIStyle("Badge"), () =>
                {
                    EditorUI.HorizontalEGL(() =>
                    {
                        EditorGUILayout.LabelField("Axis", GUILayout.Width(45));
                        int selectedAxis = 0;
                        if (Vector3.Dot(GetSafeMirrorAxis(config.mirrorAxis), Vector3.up) > 0.99f)
                            selectedAxis = 1;
                        else if (Vector3.Dot(GetSafeMirrorAxis(config.mirrorAxis), Vector3.forward) > 0.99f)
                            selectedAxis = 2;

                        int newSelection = GUILayout.SelectionGrid(selectedAxis, axisLabels, 3, EditorStyles.miniButton);

                        if (newSelection != selectedAxis)
                        {
                            switch (newSelection)
                            {
                                case 0: config.mirrorAxis = Vector3.right; break;
                                case 1: config.mirrorAxis = Vector3.up; break;
                                case 2: config.mirrorAxis = Vector3.forward; break;
                            }

                            SyncMirrorAxisAcrossConnections(currentSelectedObject, config);
                        }
                    });

                    EditorUI.HorizontalEGL(() =>
                    {
                        Rect dropRect = EditorGUILayout.GetControlRect(GUILayout.Height(25), GUILayout.Width(mainWindowSize.x));
                        EditorGUI.DrawRect(dropRect, new Color(0.2f, 0.2f, 0.2f, 0.3f));

                        cachedDropLabelStyle ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                        GUI.Label(dropRect, "Drop to here", cachedDropLabelStyle);

                        int targetCount = config.targetObjectPaths.Count;
                        if (targetCount > 0)
                        {
                            if (cachedBadgeTextStyle == null)
                            {
                                cachedBadgeTextStyle = new GUIStyle(EditorStyles.miniLabel)
                                {
                                    alignment = TextAnchor.MiddleCenter,
                                    fontSize = 9
                                };
                            }

                            string badgeText = targetCount.ToString();
                            Vector2 textSize = cachedBadgeTextStyle.CalcSize(new GUIContent(badgeText));

                            float badgeSize = Mathf.Max(14, textSize.x + 6);
                            float badgeOffset = 4;

                            Rect badgeRect = new Rect(
                                dropRect.x + badgeOffset,
                                dropRect.y + (dropRect.height - badgeSize) / 2,
                                badgeSize,
                                badgeSize
                            );

                            GUI.Box(badgeRect, "", "Badge");
                            GUI.Label(badgeRect, badgeText, cachedBadgeTextStyle);
                        }

                        if (dropRect.Contains(Event.current.mousePosition))
                        {
                            if (Event.current.type == EventType.DragUpdated)
                            {
                                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                                Event.current.Use();
                            }
                            else if (Event.current.type == EventType.DragPerform)
                            {
                                DragAndDrop.AcceptDrag();

                                var draggedObjects = DragAndDrop.objectReferences
                                    .Where(obj => obj is GameObject && obj != currentSelectedObject)
                                    .Cast<GameObject>()
                                    .ToList();

                                if (draggedObjects.Any())
                                {
                                    string message = draggedObjects.Count == 1
                                        ? $"Add '{draggedObjects[0].name}' to mirror list?"
                                        : $"Add {draggedObjects.Count} objects to mirror list?";

                                    if (EditorUtility.DisplayDialog(
                                        "Add Mirror Objects",
                                        message,
                                        "Add",
                                        "Cancel"))
                                    {
                                        foreach (var gameObj in draggedObjects)
                                        {
                                            string targetPath = GetObjectPath(gameObj);
                                            if (!config.targetObjectPaths.Contains(targetPath))
                                            {
                                                config.targetObjectPaths.Add(targetPath);
                                                EstablishMirrorConnection(currentSelectedObject, gameObj, config.mirrorAxis);
                                            }
                                        }
                                    }
                                }
                                Event.current.Use();
                            }
                        }
                    });
                });
            });

            GUI.DragWindow();
        }

        private static void DrawDetailWindow(int id)
        {
            if (currentSelectedObject == null) return;

            if (DrawCloseButton(detailWindowRect))
            {
                isDetailWindowVisible = false;
                SaveWindowSettings();
                return;
            }

            string sourcePath = currentSelectedObjectPath;
            if (string.IsNullOrEmpty(sourcePath)) return;

            if (!mirrorConfigs.ContainsKey(sourcePath))
            {
                mirrorConfigs[sourcePath] = new MirrorConfig();
                InitializeTargetList(mirrorConfigs[sourcePath]);
            }

            MirrorConfig config = mirrorConfigs[sourcePath];

            EditorUI.VerticalEGL(() =>
            {
                MirrorTool.ComponentSyncEnabled = EditorUI.Toggle(MirrorTool.ComponentSyncEnabled, "Component Sync");

                EditorGUILayout.Space(2);

                EditorUI.DrawCheckChanged(
                    () => config.mirrorAxis = EditorGUILayout.Vector3Field("Direction", config.mirrorAxis),
                    () => SyncMirrorAxisAcrossConnections(currentSelectedObject, config)
                );

                EditorGUILayout.Space(5);

                targetList?.DoLayout("Target Objects", new Vector2(0, 150), false, false, true, (objects) =>
                {
                    foreach (var obj in objects)
                    {
                        if (obj is GameObject gameObj && gameObj != currentSelectedObject)
                        {
                            string targetPath = GetObjectPath(gameObj);
                            if (!config.targetObjectPaths.Contains(targetPath))
                            {
                                config.targetObjectPaths.Add(targetPath);
                                EstablishMirrorConnection(currentSelectedObject, gameObj, config.mirrorAxis);
                            }
                        }
                    }
                });
            });

            GUI.DragWindow();
        }
    }
}
