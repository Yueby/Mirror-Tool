using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using Yueby.Core.Utils;
using Yueby.Utils;
using Yueby.ModalWindow;

namespace Yueby
{
    [InitializeOnLoad]
    public class MirrorTool
    {
        // 镜像对象配置字典
        private static Dictionary<string, MirrorConfig> mirrorConfigs = new Dictionary<string, MirrorConfig>();
        private static GameObject currentSelectedObject;
        private static bool isWindowVisible = false;
        private static Vector2 windowPosition;
        private static Rect windowRect;
        private static readonly string configPath;
        private static readonly string enableKey = "MirrorTool_Enabled";
        private static readonly string windowSettingsKey = "MirrorTool_WindowSettings";
        private static bool isEnabled;
        private static bool showMirrorAxis = true;
        private static PivotRotation lastPivotRotation;
        private static bool isDetailWindowVisible = false;
        private static Rect detailWindowRect;
        private static readonly Vector2 mainWindowSize = new Vector2(120, 90);
        private static readonly Vector2 detailWindowSize = new Vector2(200, 240);
        private static bool needUpdateDetailPosition = false;

        // 公共访问器
        public static bool ShowMirrorAxis
        {
            get => showMirrorAxis;
            set => showMirrorAxis = value;
        }

        // 配置数据类
        [System.Serializable]
        public class MirrorConfig
        {
            public List<string> targetObjectPaths = new List<string>();
            public Vector3 mirrorAxis = Vector3.right;

            public MirrorConfig(string targetPath, Vector3 axis)
            {
                targetObjectPaths = new List<string> { targetPath };
                mirrorAxis = axis;
            }

            public MirrorConfig()
            {
                targetObjectPaths = new List<string>();
                mirrorAxis = Vector3.right;
            }
        }

        [System.Serializable]
        private class WindowSettings
        {
            public float windowPosX = 50;
            public float windowPosY = 10;
            public float detailWindowPosX = 0;
            public float detailWindowPosY = 0;
            public bool detailWindowVisible = false;

            public WindowSettings()
            {
                windowPosX = 50;
                windowPosY = 10;
                detailWindowPosX = 0;
                detailWindowPosY = 0;
                detailWindowVisible = false;
            }
        }

        static MirrorTool()
        {
            string projectPath = Application.dataPath;
            projectPath = projectPath.Substring(0, projectPath.Length - 6);
            configPath = System.IO.Path.Combine(projectPath, "ProjectSettings", "MirrorTool.json");

            isEnabled = EditorPrefs.GetBool(enableKey, false);
            LoadWindowSettings();
            if (isEnabled)
            {
                SceneView.duringSceneGui += OnSceneGUI;
            }
            LoadConfigs();
        }

        [MenuItem("Tools/YuebyTools/Mirror Tool %#m", false)]
        private static void ToggleTool()
        {
            isEnabled = !isEnabled;
            EditorPrefs.SetBool(enableKey, isEnabled);

            if (isEnabled)
            {
                SceneView.duringSceneGui += OnSceneGUI;
                YuebyLogger.LogInfo($"Mirror Tool Enabled (Shortcut: Ctrl+Shift+M)");
            }
            else
            {
                SceneView.duringSceneGui -= OnSceneGUI;
                isWindowVisible = false;
                YuebyLogger.LogInfo($"Mirror Tool Disabled (Shortcut: Ctrl+Shift+M)");
            }

            SceneView.RepaintAll();
        }

        [MenuItem("Tools/YuebyTools/Mirror Tool %#m", true)]
        private static bool ValidateToggleTool()
        {
            Menu.SetChecked("Tools/YuebyTools/Mirror Tool %#m", isEnabled);
            return true;
        }

        private static void BreakMirrorConnection(GameObject source, GameObject target)
        {
            if (source == null || target == null) return;

            string sourcePath = GetObjectPath(source);
            string targetPath = GetObjectPath(target);

            // 从源对象配置中移除目标路径
            if (mirrorConfigs.TryGetValue(sourcePath, out var sourceConfig))
            {
                sourceConfig.targetObjectPaths.Remove(targetPath);
                if (sourceConfig.targetObjectPaths.Count == 0)
                {
                    mirrorConfigs.Remove(sourcePath);
                }
            }

            // 从目标对象配置中移除源路径
            if (mirrorConfigs.TryGetValue(targetPath, out var targetConfig))
            {
                targetConfig.targetObjectPaths.Remove(sourcePath);
                if (targetConfig.targetObjectPaths.Count == 0)
                {
                    mirrorConfigs.Remove(targetPath);
                }
            }

            SaveConfigs();
            SceneView.RepaintAll();
            targetList?.RefreshElementHeights();
        }

        public static void EstablishMirrorConnection(GameObject source, GameObject target, Vector3 axis)
        {
            if (source == null || target == null) return;

            string sourcePath = GetObjectPath(source);
            string targetPath = GetObjectPath(target);

            // 为源对象建立连接
            if (!mirrorConfigs.ContainsKey(sourcePath))
            {
                mirrorConfigs[sourcePath] = new MirrorConfig();
            }
            if (!mirrorConfigs[sourcePath].targetObjectPaths.Contains(targetPath))
            {
                mirrorConfigs[sourcePath].targetObjectPaths.Add(targetPath);
            }
            mirrorConfigs[sourcePath].mirrorAxis = axis;

            // 为目标对象建立反向连接
            if (!mirrorConfigs.ContainsKey(targetPath))
            {
                mirrorConfigs[targetPath] = new MirrorConfig();
            }
            if (!mirrorConfigs[targetPath].targetObjectPaths.Contains(sourcePath))
            {
                mirrorConfigs[targetPath].targetObjectPaths.Add(sourcePath);
            }
            mirrorConfigs[targetPath].mirrorAxis = axis;

            SaveConfigs();
            SceneView.RepaintAll();
            targetList?.RefreshElementHeights();
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (lastPivotRotation != Tools.pivotRotation)
            {
                lastPivotRotation = Tools.pivotRotation;
                SceneView.RepaintAll();
            }

            if (Selection.gameObjects.Length != 1)
            {
                isWindowVisible = false;
                currentSelectedObject = null;
                targetList = null;
                return;
            }

            GameObject selectedObject = Selection.gameObjects[0];

            if (currentSelectedObject != selectedObject)
            {
                currentSelectedObject = selectedObject;
                if (windowRect.width == 0 || windowRect.height == 0)
                {
                    windowPosition = new Vector2(50, 10);
                    windowRect = new Rect(windowPosition, mainWindowSize);
                }
                else
                {
                    windowRect = new Rect(windowPosition, mainWindowSize);
                }
                isWindowVisible = true;

                // 初始化或更新目标列表
                string sourcePath = GetObjectPath(currentSelectedObject);
                if (!mirrorConfigs.ContainsKey(sourcePath))
                {
                    mirrorConfigs[sourcePath] = new MirrorConfig();
                }

                InitializeTargetList(mirrorConfigs[sourcePath]);
            }

            if (!isWindowVisible) return;

            Handles.BeginGUI();

            // 绘制主窗口
            Vector2 oldWindowPos = windowRect.position;
            windowRect = GUILayout.Window(0, windowRect, DrawMirrorWindow, "Mirror Tool");
            if (windowRect.position != oldWindowPos)
            {
                SaveWindowSettings();
            }
            windowPosition = windowRect.position;

            // 更新并绘制详情窗口
            if (isDetailWindowVisible)
            {
                if (needUpdateDetailPosition)
                {
                    float detailX = windowRect.x + windowRect.width + 5;
                    detailWindowRect = new Rect(detailX, windowRect.y, detailWindowSize.x, detailWindowSize.y);
                    needUpdateDetailPosition = false;
                    SaveWindowSettings();
                }
                Vector2 oldDetailPos = detailWindowRect.position;
                detailWindowRect = GUILayout.Window(1, detailWindowRect, DrawDetailWindow, "Mirror Details");
                if (detailWindowRect.position != oldDetailPos)
                {
                    SaveWindowSettings();
                }
            }

            Handles.EndGUI();

            if (currentSelectedObject != null && mirrorConfigs.TryGetValue(GetObjectPath(currentSelectedObject), out MirrorConfig config))
            {
                foreach (var targetPath in config.targetObjectPaths)
                {
                    GameObject targetObj = FindObjectByPath(targetPath);
                    if (targetObj != null)
                    {
                        ApplyMirror(currentSelectedObject, targetObj, config.mirrorAxis);

                        if (showMirrorAxis)
                        {
                            Vector3 targetPos = targetObj.transform.position;
                            float handleSize = HandleUtility.GetHandleSize(targetPos) * 0.15f;

                            Vector3 rightDir = Tools.pivotRotation == PivotRotation.Local ? targetObj.transform.right : Vector3.right;
                            Vector3 upDir = Tools.pivotRotation == PivotRotation.Local ? targetObj.transform.up : Vector3.up;
                            Vector3 forwardDir = Tools.pivotRotation == PivotRotation.Local ? targetObj.transform.forward : Vector3.forward;

                            // 根据当前工具类型显示不同的handle样式
                            switch (Tools.current)
                            {
                                case Tool.Move:
                                    // 保持原有的箭头样式
                                    Handles.color = Color.red;
                                    Handles.ArrowHandleCap(0, targetPos, Quaternion.LookRotation(rightDir), handleSize * 2f, EventType.Repaint);
                                    Handles.color = Color.green;
                                    Handles.ArrowHandleCap(0, targetPos, Quaternion.LookRotation(upDir), handleSize * 2f, EventType.Repaint);
                                    Handles.color = Color.blue;
                                    Handles.ArrowHandleCap(0, targetPos, Quaternion.LookRotation(forwardDir), handleSize * 2f, EventType.Repaint);
                                    break;

                                case Tool.Rotate:
                                    // 显示旋转环
                                    Handles.color = Color.red;
                                    Handles.CircleHandleCap(0, targetPos, Quaternion.LookRotation(rightDir), handleSize * 2f, EventType.Repaint);
                                    Handles.color = Color.green;
                                    Handles.CircleHandleCap(0, targetPos, Quaternion.LookRotation(upDir), handleSize * 2f, EventType.Repaint);
                                    Handles.color = Color.blue;
                                    Handles.CircleHandleCap(0, targetPos, Quaternion.LookRotation(forwardDir), handleSize * 2f, EventType.Repaint);
                                    break;

                                case Tool.Scale:
                                    // 显示缩放立方体
                                    float cubeSize = handleSize * 0.5f;
                                    float offset = handleSize * 2f;
                                    
                                    // 始终使用本地坐标系计算位置
                                    Vector3 rightPos = targetPos + targetObj.transform.right * offset;
                                    Vector3 upPos = targetPos + targetObj.transform.up * offset;
                                    Vector3 forwardPos = targetPos + targetObj.transform.forward * offset;
                                    
                                    // 始终使用本地空间的旋转
                                    Quaternion rightRot = targetObj.transform.rotation * Quaternion.FromToRotation(Vector3.forward, Vector3.right);
                                    Quaternion upRot = targetObj.transform.rotation * Quaternion.FromToRotation(Vector3.forward, Vector3.up);
                                    Quaternion forwardRot = targetObj.transform.rotation;
                                    
                                    // 绘制连接线
                                    Handles.color = new Color(1f, 0f, 0f, 0.8f);
                                    Handles.DrawLine(targetPos, rightPos);
                                    Handles.color = new Color(0f, 1f, 0f, 0.8f);
                                    Handles.DrawLine(targetPos, upPos);
                                    Handles.color = new Color(0f, 0f, 1f, 0.8f);
                                    Handles.DrawLine(targetPos, forwardPos);
                                    
                                    // 绘制缩放方块
                                    Handles.color = Color.red;
                                    Handles.CubeHandleCap(0, rightPos, rightRot, cubeSize, EventType.Repaint);
                                    Handles.color = Color.green;
                                    Handles.CubeHandleCap(0, upPos, upRot, cubeSize, EventType.Repaint);
                                    Handles.color = Color.blue;
                                    Handles.CubeHandleCap(0, forwardPos, forwardRot, cubeSize, EventType.Repaint);
                                    break;
                            }

                            Handles.color = new Color(1f, 1f, 1f, 0.5f);
                            Handles.DrawDottedLine(currentSelectedObject.transform.position, targetPos, 2f);
                        }
                    }
                }
            }
        }

        private static void InitializeTargetList(MirrorConfig config)
        {
            if (config.targetObjectPaths == null)
            {
                config.targetObjectPaths = new List<string>();
            }

            targetList = new ReorderableListDroppable(config.targetObjectPaths, typeof(string), EditorGUIUtility.singleLineHeight, () => SceneView.RepaintAll());
            targetList.OnDraw = (rect, index, active, focused) =>
            {
                if (index < 0 || index >= config.targetObjectPaths.Count) return EditorGUIUtility.singleLineHeight;

                var targetPath = config.targetObjectPaths[index];
                var targetObj = FindObjectByPath(targetPath);
                var newTargetObj = EditorGUI.ObjectField(rect, targetObj, typeof(GameObject), true) as GameObject;

                if (newTargetObj != targetObj)
                {
                    if (newTargetObj != null)
                    {
                        // 如果是替换，先断开旧的连接
                        if (targetObj != null)
                        {
                            BreakMirrorConnection(currentSelectedObject, targetObj);
                        }

                        string newTargetPath = GetObjectPath(newTargetObj);
                        config.targetObjectPaths[index] = newTargetPath;
                        EstablishMirrorConnection(currentSelectedObject, newTargetObj, config.mirrorAxis);
                    }
                    else
                    {
                        // 如果是删除，断开连接
                        if (targetObj != null)
                        {
                            BreakMirrorConnection(currentSelectedObject, targetObj);
                        }
                    }
                }
                return EditorGUIUtility.singleLineHeight;
            };

            targetList.OnAdd = (list) =>
            {
                config.targetObjectPaths.Add("");
                SaveConfigs();
                targetList.RefreshElementHeights();
            };

            targetList.OnRemove = (list) =>
            {
                SaveConfigs();
                targetList.RefreshElementHeights();
            };

            targetList.OnRemoveBefore = (index) =>
            {
                if (index >= 0 && index < config.targetObjectPaths.Count)
                {
                    var targetPath = config.targetObjectPaths[index];
                    var targetObj = FindObjectByPath(targetPath);
                    if (targetObj != null)
                    {
                        BreakMirrorConnection(currentSelectedObject, targetObj);
                    }
                }
            };

            targetList.RefreshElementHeights();
        }

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

            string closeIcon = EditorGUIUtility.isProSkin ? "d_winbtn_win_close" : "winbtn_win_close";
            float iconSize = EditorGUIUtility.singleLineHeight;
            float padding = 2;

            // 详情按钮 - 放在关闭按钮左边
            string detailsIcon = isDetailWindowVisible ?
                (EditorGUIUtility.isProSkin ? "d_Settings" : "Settings") :
                (EditorGUIUtility.isProSkin ? "d_Settings" : "Settings");

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

            // 关闭按钮
            if (DrawCloseButton(windowRect))
            {
                isWindowVisible = false;
                SaveWindowSettings();
                return;
            }

            EditorUI.VerticalEGL(() =>
            {
                string sourcePath = GetObjectPath(currentSelectedObject);
                if (!mirrorConfigs.ContainsKey(sourcePath))
                {
                    mirrorConfigs[sourcePath] = new MirrorConfig();
                    InitializeTargetList(mirrorConfigs[sourcePath]);
                }

                MirrorConfig config = mirrorConfigs[sourcePath];

                // 主窗口内容
                EditorUI.VerticalEGL(new GUIStyle("Badge"), () =>
                {
                    // Axis 选择
                    EditorUI.HorizontalEGL(() =>
                    {
                        EditorGUILayout.LabelField("Axis", GUILayout.Width(45));
                        int selectedAxis = 0;
                        if (Vector3.Dot(config.mirrorAxis.normalized, Vector3.up) > 0.99f)
                            selectedAxis = 1;
                        else if (Vector3.Dot(config.mirrorAxis.normalized, Vector3.forward) > 0.99f)
                            selectedAxis = 2;

                        int newSelection = GUILayout.SelectionGrid(selectedAxis, new string[] { "X", "Y", "Z" }, 3, EditorStyles.miniButton);

                        if (newSelection != selectedAxis)
                        {
                            switch (newSelection)
                            {
                                case 0: config.mirrorAxis = Vector3.right; break;
                                case 1: config.mirrorAxis = Vector3.up; break;
                                case 2: config.mirrorAxis = Vector3.forward; break;
                            }
                        }
                    });

                    // 投放区域
                    EditorUI.HorizontalEGL(() =>
                    {
                        Rect dropRect = EditorGUILayout.GetControlRect(GUILayout.Height(25), GUILayout.Width(mainWindowSize.x));
                        EditorGUI.DrawRect(dropRect, new Color(0.2f, 0.2f, 0.2f, 0.3f));

                        GUI.Label(dropRect, "Drop to here", new GUIStyle(EditorStyles.centeredGreyMiniLabel));

                        // 添加数量badge
                        int targetCount = config.targetObjectPaths.Count;
                        if (targetCount > 0)
                        {
                            GUIStyle badgeStyle = new GUIStyle(EditorStyles.miniLabel)
                            {
                                alignment = TextAnchor.MiddleCenter,
                                fontSize = 9
                            };

                            string badgeText = targetCount.ToString();
                            Vector2 textSize = badgeStyle.CalcSize(new GUIContent(badgeText));

                            float badgeSize = Mathf.Max(14, textSize.x + 6); // 最小14，文本两边各加3像素padding
                            float badgeOffset = 4;

                            Rect badgeRect = new Rect(
                                dropRect.x + badgeOffset,
                                dropRect.y + (dropRect.height - badgeSize) / 2,
                                badgeSize,
                                badgeSize
                            );

                            GUI.Box(badgeRect, "", "Badge");
                            GUI.Label(badgeRect, badgeText, badgeStyle);
                        }

                        // 处理拖拽
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
                                
                                // 获取拖拽的对象列表
                                var draggedObjects = DragAndDrop.objectReferences
                                    .Where(obj => obj is GameObject)
                                    .Cast<GameObject>()
                                    .ToList();
                                    
                                if (draggedObjects.Any())
                                {
                                    // 构建确认消息
                                    string message = draggedObjects.Count == 1 
                                        ? $"Add '{draggedObjects[0].name}' to mirror list?" 
                                        : $"Add {draggedObjects.Count} objects to mirror list?";
                                        
                                    // 显示确认对话框
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
                                        SaveConfigs();
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

            string sourcePath = GetObjectPath(currentSelectedObject);

            // 添加检查，如果配置不存在则创建新的
            if (!mirrorConfigs.ContainsKey(sourcePath))
            {
                mirrorConfigs[sourcePath] = new MirrorConfig();
                InitializeTargetList(mirrorConfigs[sourcePath]);
            }

            MirrorConfig config = mirrorConfigs[sourcePath];

            EditorUI.VerticalEGL(() =>
            {
                // Direction 设置
                EditorUI.DrawCheckChanged(
                    () => config.mirrorAxis = EditorGUILayout.Vector3Field("Direction", config.mirrorAxis),
                    () =>
                    {
                        foreach (var targetPath in config.targetObjectPaths)
                        {
                            var targetObj = FindObjectByPath(targetPath);
                            if (targetObj != null)
                            {
                                EstablishMirrorConnection(currentSelectedObject, targetObj, config.mirrorAxis);
                            }
                        }
                    }
                );

                EditorGUILayout.Space(5);

                // 目标对象列表
                targetList?.DoLayout("Target Objects", new Vector2(0, 150), false, false, true, (objects) =>
                {
                    foreach (var obj in objects)
                    {
                        if (obj is GameObject gameObj)
                        {
                            string targetPath = GetObjectPath(gameObj);
                            if (!config.targetObjectPaths.Contains(targetPath))
                            {
                                config.targetObjectPaths.Add(targetPath);
                                EstablishMirrorConnection(currentSelectedObject, gameObj, config.mirrorAxis);
                            }
                        }
                    }
                    SaveConfigs();
                });
            });

            // 添加拖拽功能
            GUI.DragWindow();
        }

        private static void ApplyMirror(GameObject source, GameObject target, Vector3 axis)
        {
            if (source == null || target == null) return;

            Vector3 sourcePos = source.transform.position;
            Vector3 mirrorPos = Vector3.Reflect(sourcePos, axis.normalized);
            target.transform.position = mirrorPos;

            Quaternion sourceRot = source.transform.rotation;
            Vector3 forward = Vector3.Reflect(sourceRot * Vector3.forward, axis.normalized);
            Vector3 up = Vector3.Reflect(sourceRot * Vector3.up, axis.normalized);
            target.transform.rotation = Quaternion.LookRotation(forward, up);

            target.transform.localScale = source.transform.localScale;
        }

        private static void SaveConfigs()
        {
            CleanEmptyConfigs();
            string json = JsonUtility.ToJson(new SerializableDict(mirrorConfigs), true);
            System.IO.File.WriteAllText(configPath, json);
        }

        private static void LoadConfigs()
        {
            if (System.IO.File.Exists(configPath))
            {
                string json = System.IO.File.ReadAllText(configPath);
                SerializableDict serializableDict = JsonUtility.FromJson<SerializableDict>(json);
                mirrorConfigs = serializableDict.ToDictionary();
            }
            else
            {
                mirrorConfigs = new Dictionary<string, MirrorConfig>();
            }
        }

        public static string GetObjectPath(GameObject obj)
        {
            if (obj == null) return "";
            string path = obj.name;
            Transform parent = obj.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        public static GameObject FindObjectByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return GameObject.Find(path);
        }

        [System.Serializable]
        private class SerializableDict
        {
            public List<string> keys = new List<string>();
            public List<MirrorConfig> values = new List<MirrorConfig>();

            public SerializableDict(Dictionary<string, MirrorConfig> dict)
            {
                foreach (var kvp in dict)
                {
                    keys.Add(kvp.Key);
                    values.Add(kvp.Value);
                }
            }

            public Dictionary<string, MirrorConfig> ToDictionary()
            {
                Dictionary<string, MirrorConfig> dict = new Dictionary<string, MirrorConfig>();
                for (int i = 0; i < keys.Count; i++)
                {
                    dict[keys[i]] = values[i];
                }
                return dict;
            }
        }

        private static ReorderableListDroppable targetList;

        public static void ClearAllData()
        {
            mirrorConfigs.Clear();
            SaveConfigs();
            SceneView.RepaintAll();
            YuebyLogger.LogInfo("Mirror Tool data cleared.");
        }

        private static void CleanEmptyConfigs()
        {
            var keysToRemove = mirrorConfigs.Where(kvp => kvp.Value.targetObjectPaths.Count == 0)
                                          .Select(kvp => kvp.Key)
                                          .ToList();

            foreach (var key in keysToRemove)
            {
                mirrorConfigs.Remove(key);
            }
        }

        private static void LoadWindowSettings()
        {
            string json = EditorPrefs.GetString(windowSettingsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                WindowSettings settings = JsonUtility.FromJson<WindowSettings>(json);
                
                windowPosition = new Vector2(settings.windowPosX, settings.windowPosY);
                windowRect = new Rect(windowPosition, mainWindowSize);
                
                isDetailWindowVisible = settings.detailWindowVisible;
                if (settings.detailWindowPosX > 0 && settings.detailWindowPosY > 0)
                {
                    detailWindowRect = new Rect(settings.detailWindowPosX, settings.detailWindowPosY, detailWindowSize.x, detailWindowSize.y);
                }
                else
                {
                    detailWindowRect = new Rect(windowPosition.x + mainWindowSize.x + 5, windowPosition.y, detailWindowSize.x, detailWindowSize.y);
                }
            }
            else
            {
                windowPosition = new Vector2(50, 10);
                windowRect = new Rect(windowPosition, mainWindowSize);
                detailWindowRect = new Rect(windowPosition.x + mainWindowSize.x + 5, windowPosition.y, detailWindowSize.x, detailWindowSize.y);
            }
        }

        private static void SaveWindowSettings()
        {
            WindowSettings settings = new WindowSettings
            {
                windowPosX = windowRect.x,
                windowPosY = windowRect.y,
                detailWindowPosX = isDetailWindowVisible ? detailWindowRect.x : 0,
                detailWindowPosY = isDetailWindowVisible ? detailWindowRect.y : 0,
                detailWindowVisible = isDetailWindowVisible
            };
            
            string json = JsonUtility.ToJson(settings, true);
            EditorPrefs.SetString(windowSettingsKey, json);
        }
    }
}