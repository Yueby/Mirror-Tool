using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Yueby.Utils;

namespace Yueby
{
    public partial class MirrorTool
    {
        private static void InitializeTargetList(MirrorConfig config)
        {
            if (config.targetObjectPaths == null)
            {
                config.targetObjectPaths = new List<string>();
            }

            targetList = new ReorderableListDroppable(
                config.targetObjectPaths,
                typeof(string),
                EditorGUIUtility.singleLineHeight,
                () => SceneView.RepaintAll());

            targetList.OnDraw = (rect, index, active, focused) =>
            {
                if (index < 0 || index >= config.targetObjectPaths.Count)
                {
                    return EditorGUIUtility.singleLineHeight;
                }

                float lineHeight = EditorGUIUtility.singleLineHeight;
                string targetPath = config.targetObjectPaths[index];

                if (Event.current == null)
                {
                    return lineHeight * 2 + listElementSpacing * 2;
                }

                GameObject targetObj = FindObjectByPath(targetPath);
                Rect objectFieldRect = new Rect(rect.x, rect.y, rect.width, lineHeight);
                GameObject newTargetObj = EditorGUI.ObjectField(objectFieldRect, targetObj, typeof(GameObject), true) as GameObject;

                if (newTargetObj != targetObj && newTargetObj != currentSelectedObject)
                {
                    if (newTargetObj != null)
                    {
                        if (targetObj != null)
                        {
                            BreakMirrorConnection(currentSelectedObject, targetObj);
                        }

                        string newTargetPath = GetObjectPath(newTargetObj);
                        config.targetObjectPaths[index] = newTargetPath;
                        EstablishMirrorConnection(currentSelectedObject, newTargetObj, config.mirrorAxis);
                    }
                    else if (targetObj != null)
                    {
                        BreakMirrorConnection(currentSelectedObject, targetObj);
                    }

                    targetList?.RefreshElementHeights();
                    return lineHeight;
                }

                if (cachedStatusStyle == null)
                {
                    cachedStatusStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                }
                string statusText = GetMirrorReferenceStatusText(currentSelectedObject, targetObj, targetPath);
                GUIContent statusContent = new GUIContent(statusText, statusText);
                float statusHeight = Mathf.Max(lineHeight, cachedStatusStyle.CalcHeight(statusContent, rect.width));
                Rect statusRect = new Rect(rect.x, rect.y + lineHeight + listElementSpacing, rect.width, statusHeight);
                EditorGUI.LabelField(statusRect, statusContent, cachedStatusStyle);

                return lineHeight + listElementSpacing + statusHeight + listElementSpacing;
            };

            targetList.OnAdd = list =>
            {
                config.targetObjectPaths.Add(string.Empty);
                targetList.RefreshElementHeights();
            };

            targetList.OnRemove = list =>
            {
                ScheduleMirrorRefresh();
            };

            targetList.OnRemoveBefore = index =>
            {
                if (index >= 0 && index < config.targetObjectPaths.Count)
                {
                    string targetPath = config.targetObjectPaths[index];
                    GameObject targetObj = FindObjectByPath(targetPath);
                    if (targetObj != null)
                    {
                        BreakMirrorConnection(currentSelectedObject, targetObj);
                    }
                }
            };

            targetList.RefreshElementHeights();
        }
    }
}
