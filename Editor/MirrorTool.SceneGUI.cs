using UnityEngine;
using UnityEditor;

namespace Yueby
{
    public partial class MirrorTool
    {
        private static void OnSceneGUI(SceneView sceneView)
        {
            if (lastPivotRotation != Tools.pivotRotation)
            {
                lastPivotRotation = Tools.pivotRotation;
                SceneView.RepaintAll();
            }

            GameObject[] selectedObjects = Selection.gameObjects;

            if (selectedObjects.Length == 0)
            {
                isWindowVisible = false;
                currentSelectedObject = null;
                currentSelectedObjectPath = null;
                targetList = null;
                return;
            }

            if (selectedObjects.Length == 1)
            {
                HandleSingleSelectionUI(selectedObjects[0]);
            }
            else
            {
                isWindowVisible = false;
                currentSelectedObject = null;
                currentSelectedObjectPath = null;
                targetList = null;
            }

            ApplyAndDrawMirrorsForSelection(selectedObjects);
        }

        private static void HandleSingleSelectionUI(GameObject selectedObject)
        {
            if (currentSelectedObject != selectedObject || targetList == null)
            {
                currentSelectedObject = selectedObject;
                if (windowRect.width == 0 || windowRect.height == 0)
                {
                    windowPosition = new Vector2(50, 10);
                }
                windowRect = new Rect(windowPosition, mainWindowSize);
                isWindowVisible = true;

                currentSelectedObjectPath = GetObjectPath(currentSelectedObject);
                if (!mirrorConfigs.ContainsKey(currentSelectedObjectPath))
                {
                    mirrorConfigs[currentSelectedObjectPath] = new MirrorConfig();
                }

                InitializeTargetList(mirrorConfigs[currentSelectedObjectPath]);
            }

            if (!isWindowVisible) return;

            Handles.BeginGUI();

            Vector2 oldWindowPos = windowRect.position;
            windowRect = GUILayout.Window(0, windowRect, DrawMirrorWindow, "Mirror Tool");
            if (windowRect.position != oldWindowPos)
            {
                SaveWindowSettings();
            }
            windowPosition = windowRect.position;

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
        }

        private static void ApplyAndDrawMirrorsForSelection(GameObject[] selectedObjects)
        {
            foreach (GameObject sourceObj in selectedObjects)
            {
                if (sourceObj == null) continue;

                string sourcePath = GetObjectPath(sourceObj);
                if (!mirrorConfigs.TryGetValue(sourcePath, out MirrorConfig config)) continue;

                foreach (var targetPath in config.targetObjectPaths)
                {
                    GameObject targetObj = FindObjectByPath(targetPath);
                    if (targetObj == null) continue;

                    ApplyMirror(sourceObj, targetObj, config.mirrorAxis);

                    if (showMirrorAxis)
                    {
                        DrawMirrorTargetHandles(sourceObj, targetObj);
                    }
                }
            }
        }

        private static void DrawMirrorTargetHandles(GameObject sourceObj, GameObject targetObj)
        {
            Vector3 targetPos = targetObj.transform.position;
            float handleSize = HandleUtility.GetHandleSize(targetPos) * 0.15f;
            bool isLocal = Tools.pivotRotation == PivotRotation.Local;

            Vector3 rightDir = isLocal ? targetObj.transform.right : Vector3.right;
            Vector3 upDir = isLocal ? targetObj.transform.up : Vector3.up;
            Vector3 forwardDir = isLocal ? targetObj.transform.forward : Vector3.forward;

            switch (Tools.current)
            {
                case Tool.Move:
                    DrawAxisHandle(Handles.ArrowHandleCap, targetPos, rightDir, upDir, forwardDir, handleSize * 2f);
                    break;

                case Tool.Rotate:
                    DrawAxisHandle(Handles.CircleHandleCap, targetPos, rightDir, upDir, forwardDir, handleSize * 2f);
                    break;

                case Tool.Scale:
                    DrawScaleHandles(targetObj.transform, targetPos, handleSize);
                    break;
            }

            Handles.color = new Color(1f, 1f, 1f, 0.5f);
            Handles.DrawDottedLine(sourceObj.transform.position, targetPos, 2f);
        }

        private static void DrawAxisHandle(Handles.CapFunction capFunc, Vector3 position, Vector3 right, Vector3 up, Vector3 forward, float size)
        {
            Handles.color = Color.red;
            capFunc(0, position, Quaternion.LookRotation(right), size, EventType.Repaint);
            Handles.color = Color.green;
            capFunc(0, position, Quaternion.LookRotation(up), size, EventType.Repaint);
            Handles.color = Color.blue;
            capFunc(0, position, Quaternion.LookRotation(forward), size, EventType.Repaint);
        }

        private static void DrawScaleHandles(Transform targetTransform, Vector3 targetPos, float handleSize)
        {
            float cubeSize = handleSize * 0.5f;
            float offset = handleSize * 2f;

            Vector3 rightPos = targetPos + targetTransform.right * offset;
            Vector3 upPos = targetPos + targetTransform.up * offset;
            Vector3 forwardPos = targetPos + targetTransform.forward * offset;

            Quaternion rightRot = targetTransform.rotation * Quaternion.FromToRotation(Vector3.forward, Vector3.right);
            Quaternion upRot = targetTransform.rotation * Quaternion.FromToRotation(Vector3.forward, Vector3.up);
            Quaternion forwardRot = targetTransform.rotation;

            Handles.color = new Color(1f, 0f, 0f, 0.8f);
            Handles.DrawLine(targetPos, rightPos);
            Handles.color = new Color(0f, 1f, 0f, 0.8f);
            Handles.DrawLine(targetPos, upPos);
            Handles.color = new Color(0f, 0f, 1f, 0.8f);
            Handles.DrawLine(targetPos, forwardPos);

            Handles.color = Color.red;
            Handles.CubeHandleCap(0, rightPos, rightRot, cubeSize, EventType.Repaint);
            Handles.color = Color.green;
            Handles.CubeHandleCap(0, upPos, upRot, cubeSize, EventType.Repaint);
            Handles.color = Color.blue;
            Handles.CubeHandleCap(0, forwardPos, forwardRot, cubeSize, EventType.Repaint);
        }
    }
}
