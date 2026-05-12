using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Yueby
{
    public partial class MirrorTool
    {
        private static bool pendingHierarchyRefresh;
        private static bool pendingSelectionRefresh;
        private static HashSet<string> previouslyExpandedHighlightPaths = new HashSet<string>();
        private static MethodInfo cachedExpandMethod;
        private static object cachedExpandTarget;
        private static bool cachedExpandMethodNeedsBoolArg;

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                mirrorReferenceInstanceIdCacheDirty = true;
                objectPathCache.Clear();
                objectByPathCache.Clear();
            }
        }

        private static void OnHierarchyChanged()
        {
            currentSelectedObjectPath = currentSelectedObject != null
                ? GetObjectPath(currentSelectedObject)
                : null;

            if (!pendingHierarchyRefresh)
            {
                pendingHierarchyRefresh = true;
                EditorApplication.delayCall += () =>
                {
                    pendingHierarchyRefresh = false;
                    InvalidateAllCaches();

                    if (currentSelectedObject != null)
                    {
                        currentSelectedObjectPath = GetObjectPath(currentSelectedObject);
                    }

                    if (!string.IsNullOrEmpty(currentSelectedObjectPath) && mirrorConfigs.TryGetValue(currentSelectedObjectPath, out var config))
                    {
                        InitializeTargetList(config);
                    }
                    else
                    {
                        targetList = null;
                    }

                    UpdateHighlightedMirrorObjectPaths();
                    EnsureHighlightedMirrorObjectsVisible();
                    EditorApplication.RepaintHierarchyWindow();
                    SceneView.RepaintAll();
                };
            }
        }

        private static void OnSelectionChanged()
        {
            var selected = Selection.gameObjects;
            currentSelectedObjectPath = selected.Length == 1 && selected[0] != null
                ? GetObjectPath(selected[0])
                : null;

            if (!pendingSelectionRefresh)
            {
                pendingSelectionRefresh = true;
                EditorApplication.delayCall += () =>
                {
                    pendingSelectionRefresh = false;
                    UpdateHighlightedMirrorObjectPaths();
                    EnsureHighlightedMirrorObjectsVisible();
                    EditorApplication.RepaintHierarchyWindow();
                    SceneView.RepaintAll();
                };
            }
        }

        private static void UpdateHighlightedMirrorObjectPaths()
        {
            highlightedMirrorObjectPaths.Clear();
            highlightedMirrorObjectGroupIndices.Clear();
            selectedHighlightedMirrorObjectPaths.Clear();

            GameObject[] selectedObjects = Selection.gameObjects;
            if (selectedObjects.Length == 0)
            {
                return;
            }

            HashSet<string> processedPaths = new HashSet<string>();
            List<string> selectedPaths = new List<string>(selectedObjects.Length);
            int nextGroupIndex = 0;

            foreach (GameObject selectedObject in selectedObjects)
            {
                if (selectedObject == null)
                {
                    selectedPaths.Add(null);
                    continue;
                }

                string selectedPath = GetObjectPath(selectedObject);
                selectedPaths.Add(selectedPath);

                if (string.IsNullOrEmpty(selectedPath) || processedPaths.Contains(selectedPath))
                {
                    continue;
                }

                HashSet<string> connectedMirrorPaths = CollectConnectedMirrorObjectPaths(selectedPath);
                processedPaths.UnionWith(connectedMirrorPaths);

                if (connectedMirrorPaths.Count <= 1)
                {
                    continue;
                }

                foreach (string connectedPath in connectedMirrorPaths)
                {
                    highlightedMirrorObjectPaths.Add(connectedPath);
                    highlightedMirrorObjectGroupIndices[connectedPath] = nextGroupIndex;
                }

                nextGroupIndex++;
            }

            foreach (string selectedPath in selectedPaths)
            {
                if (!string.IsNullOrEmpty(selectedPath) && highlightedMirrorObjectPaths.Contains(selectedPath))
                {
                    selectedHighlightedMirrorObjectPaths.Add(selectedPath);
                }
            }
        }

        private static HashSet<string> CollectConnectedMirrorObjectPaths(string rootPath)
        {
            HashSet<string> connectedPaths = new HashSet<string>();
            if (string.IsNullOrEmpty(rootPath))
            {
                return connectedPaths;
            }

            Queue<string> pendingPaths = new Queue<string>();
            pendingPaths.Enqueue(rootPath);

            while (pendingPaths.Count > 0)
            {
                string currentPath = pendingPaths.Dequeue();
                if (string.IsNullOrEmpty(currentPath) || !connectedPaths.Add(currentPath))
                {
                    continue;
                }

                if (!mirrorConfigs.TryGetValue(currentPath, out MirrorConfig config))
                {
                    continue;
                }

                foreach (string targetPath in config.targetObjectPaths)
                {
                    if (!string.IsNullOrEmpty(targetPath))
                    {
                        pendingPaths.Enqueue(targetPath);
                    }
                }
            }

            return connectedPaths;
        }

        private static void EnsureHighlightedMirrorObjectsVisible()
        {
            if (highlightedMirrorObjectPaths.Count == 0)
            {
                previouslyExpandedHighlightPaths.Clear();
                return;
            }

            HashSet<string> newPaths = new HashSet<string>(highlightedMirrorObjectPaths);
            newPaths.ExceptWith(previouslyExpandedHighlightPaths);
            previouslyExpandedHighlightPaths = new HashSet<string>(highlightedMirrorObjectPaths);

            if (newPaths.Count == 0) return;

            System.Type hierarchyWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
            if (hierarchyWindowType == null) return;

            Object[] hierarchyWindows = Resources.FindObjectsOfTypeAll(hierarchyWindowType);
            if (hierarchyWindows == null || hierarchyWindows.Length == 0) return;

            foreach (Object hierarchyWindow in hierarchyWindows)
            {
                EnsurePathsVisibleInHierarchyWindow(hierarchyWindow, newPaths);
            }
        }

        private static void EnsurePathsVisibleInHierarchyWindow(Object hierarchyWindow, HashSet<string> paths)
        {
            object sceneHierarchy = GetHierarchySceneHierarchyController(hierarchyWindow);
            if (sceneHierarchy == null) return;

            HashSet<int> ancestorInstanceIds = new HashSet<int>();
            foreach (string objectPath in paths)
            {
                GameObject targetObject = FindObjectByPath(objectPath);
                if (targetObject == null) continue;

                Transform current = targetObject.transform.parent;
                while (current != null)
                {
                    if (!ancestorInstanceIds.Add(current.gameObject.GetInstanceID())) break;
                    current = current.parent;
                }
            }

            foreach (int instanceId in ancestorInstanceIds)
            {
                TryExpandHierarchyItem(sceneHierarchy, hierarchyWindow, instanceId);
            }

            if (hierarchyWindow is EditorWindow editorWindow)
            {
                editorWindow.Repaint();
            }
        }

        private static object GetHierarchySceneHierarchyController(Object hierarchyWindow)
        {
            return GetReflectedMemberValue(hierarchyWindow, "sceneHierarchy")
                ?? GetReflectedMemberValue(hierarchyWindow, "m_SceneHierarchy");
        }

        private static bool TryExpandHierarchyItem(object sceneHierarchy, Object hierarchyWindow, int instanceId)
        {
            if (cachedExpandMethod != null && cachedExpandTarget != null)
            {
                object target = cachedExpandTarget == sceneHierarchy ? sceneHierarchy : hierarchyWindow;
                if (target != null)
                {
                    if (cachedExpandMethodNeedsBoolArg)
                        cachedExpandMethod.Invoke(target, new object[] { instanceId, true });
                    else
                        cachedExpandMethod.Invoke(target, new object[] { instanceId });
                    return true;
                }
            }

            if (TryResolveAndInvokeExpandMethod(sceneHierarchy, "SetExpanded", instanceId, sceneHierarchy)) return true;
            if (TryResolveAndInvokeExpandMethod(hierarchyWindow, "SetExpanded", instanceId, sceneHierarchy)) return true;
            if (TryResolveAndInvokeExpandMethod(sceneHierarchy, "ChangeFoldingForSingleItem", instanceId, sceneHierarchy)) return true;
            if (TryResolveAndInvokeExpandMethod(hierarchyWindow, "ChangeFoldingForSingleItem", instanceId, sceneHierarchy)) return true;
            if (TryAddExpandedHierarchyStateId(sceneHierarchy, instanceId)) return true;
            if (TryResolveAndInvokeExpandMethod(sceneHierarchy, "SetExpandedRecursive", instanceId, sceneHierarchy)) return true;
            if (TryResolveAndInvokeExpandMethod(hierarchyWindow, "SetExpandedRecursive", instanceId, sceneHierarchy)) return true;
            return false;
        }

        private static bool TryResolveAndInvokeExpandMethod(object target, string methodName, int instanceId, object sceneHierarchy)
        {
            if (target == null) return false;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            MethodInfo method = target.GetType().GetMethod(methodName, flags, null, new[] { typeof(int), typeof(bool) }, null);
            if (method != null)
            {
                method.Invoke(target, new object[] { instanceId, true });
                cachedExpandMethod = method;
                cachedExpandTarget = target == sceneHierarchy ? sceneHierarchy : target;
                cachedExpandMethodNeedsBoolArg = true;
                return true;
            }

            method = target.GetType().GetMethod(methodName, flags, null, new[] { typeof(int) }, null);
            if (method != null)
            {
                method.Invoke(target, new object[] { instanceId });
                cachedExpandMethod = method;
                cachedExpandTarget = target == sceneHierarchy ? sceneHierarchy : target;
                cachedExpandMethodNeedsBoolArg = false;
                return true;
            }

            return false;
        }

        private static bool TryAddExpandedHierarchyStateId(object sceneHierarchy, int instanceId)
        {
            object treeView = GetReflectedMemberValue(sceneHierarchy, "treeView")
                ?? GetReflectedMemberValue(sceneHierarchy, "m_TreeView");
            object treeViewState = GetReflectedMemberValue(treeView, "state")
                ?? GetReflectedMemberValue(treeView, "m_TreeViewState")
                ?? GetReflectedMemberValue(sceneHierarchy, "m_TreeViewState");
            IList expandedIds = GetReflectedMemberValue(treeViewState, "expandedIDs") as IList;

            if (expandedIds == null) return false;

            for (int i = 0; i < expandedIds.Count; i++)
            {
                if (expandedIds[i] is int expandedId && expandedId == instanceId)
                    return true;
            }

            expandedIds.Add(instanceId);
            TryInvokeParameterlessMethod(treeView, "Reload");
            TryInvokeParameterlessMethod(sceneHierarchy, "ReloadData");
            return true;
        }

        private static bool TryInvokeParameterlessMethod(object target, string methodName)
        {
            if (target == null) return false;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo method = target.GetType().GetMethod(methodName, flags, null, System.Type.EmptyTypes, null);
            if (method == null) return false;

            method.Invoke(target, null);
            return true;
        }

        private static object GetReflectedMemberValue(object target, string memberName)
        {
            if (target == null)
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = target.GetType().GetProperty(memberName, flags);
            if (property != null)
            {
                return property.GetValue(target, null);
            }

            FieldInfo field = target.GetType().GetField(memberName, flags);
            return field?.GetValue(target);
        }

        private static GameObject GetContextGameObject(MenuCommand command)
        {
            return command != null && command.context is GameObject contextObject
                ? contextObject
                : Selection.activeGameObject;
        }

        private static void SetMirrorReferenceState(GameObject targetObject, bool isMirrorReference)
        {
            if (targetObject == null)
            {
                return;
            }

            string targetPath = GetObjectPath(targetObject);
            string referenceId = GetObjectReferenceId(targetObject);
            if (string.IsNullOrEmpty(targetPath) && string.IsNullOrEmpty(referenceId))
            {
                return;
            }

            bool changed = false;
            if (isMirrorReference)
            {
                if (!string.IsNullOrEmpty(referenceId))
                {
                    changed |= mirrorReferenceIds.Add(referenceId);
                }

                if (!string.IsNullOrEmpty(targetPath))
                {
                    changed |= legacyMirrorReferencePaths.Add(targetPath);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(referenceId))
                {
                    changed |= mirrorReferenceIds.Remove(referenceId);
                }

                if (!string.IsNullOrEmpty(targetPath))
                {
                    changed |= legacyMirrorReferencePaths.Remove(targetPath);
                }
            }

            if (!changed)
            {
                return;
            }

            ScheduleMirrorRefresh();
        }

        private static bool IsMirrorReference(GameObject targetObject)
        {
            return targetObject != null && IsMirrorReference(targetObject, GetObjectPath(targetObject));
        }

        private static bool IsMirrorReference(GameObject targetObject, string objectPath)
        {
            if (targetObject == null) return false;

            EnsureMirrorReferenceInstanceIdCache();
            return knownMirrorReferenceInstanceIds.Contains(targetObject.GetInstanceID())
                || (!string.IsNullOrEmpty(objectPath) && legacyMirrorReferencePaths.Contains(objectPath));
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
