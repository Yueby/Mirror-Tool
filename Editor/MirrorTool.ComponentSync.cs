using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Yueby
{
    public partial class MirrorTool
    {
        private static bool componentSyncEnabled;
        private const string componentSyncKey = "MirrorTool_ComponentSync";

        private static readonly Dictionary<Component, HashSet<string>> pendingSyncComponents =
            new Dictionary<Component, HashSet<string>>();

        public static bool ComponentSyncEnabled
        {
            get => componentSyncEnabled;
            set
            {
                if (componentSyncEnabled == value) return;
                componentSyncEnabled = value;
                EditorPrefs.SetBool(componentSyncKey, value);
                UpdateComponentSyncSubscription();
            }
        }

        private static void InitializeComponentSync()
        {
            componentSyncEnabled = EditorPrefs.GetBool(componentSyncKey, true);
            UpdateComponentSyncSubscription();
            EditorApplication.update -= ComponentSyncUpdate;
            EditorApplication.update += ComponentSyncUpdate;
        }

        private static void UpdateComponentSyncSubscription()
        {
            Undo.postprocessModifications -= OnPostprocessModifications;
            if (componentSyncEnabled)
            {
                Undo.postprocessModifications += OnPostprocessModifications;
            }
            else
            {
                pendingSyncComponents.Clear();
            }
        }

        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (!componentSyncEnabled || modifications == null || modifications.Length == 0)
                return modifications;

            foreach (var mod in modifications)
            {
                Object target = mod.currentValue.target;
                if (target == null || target is Transform)
                    continue;

                if (!(target is Component component))
                    continue;

                GameObject sourceObj = component.gameObject;
                if (sourceObj == null)
                    continue;

                string sourcePath = GetObjectPath(sourceObj);
                if (!mirrorConfigs.TryGetValue(sourcePath, out MirrorConfig config))
                    continue;

                if (config.targetObjectPaths == null || config.targetObjectPaths.Count == 0)
                    continue;

                if (!pendingSyncComponents.TryGetValue(component, out var properties))
                {
                    properties = new HashSet<string>();
                    pendingSyncComponents[component] = properties;
                }

                properties.Add(mod.currentValue.propertyPath);
            }

            return modifications;
        }

        private static void ComponentSyncUpdate()
        {
            if (!componentSyncEnabled || pendingSyncComponents.Count == 0)
                return;

            SuspendMirrorToolEvents();

            foreach (var kvp in pendingSyncComponents)
            {
                if (kvp.Key != null)
                {
                    SyncComponentToMirrorTargets(kvp.Key, kvp.Value);
                }
            }

            pendingSyncComponents.Clear();

            EditorApplication.delayCall += ResumeMirrorToolEvents;
        }

        private static void SuspendMirrorToolEvents()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private static void ResumeMirrorToolEvents()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Selection.selectionChanged -= OnSelectionChanged;
            Selection.selectionChanged += OnSelectionChanged;
        }

        private static void SyncComponentToMirrorTargets(Component sourceComponent, HashSet<string> propertyPaths)
        {
            if (sourceComponent == null || propertyPaths == null || propertyPaths.Count == 0)
                return;

            GameObject sourceObj = sourceComponent.gameObject;
            string sourcePath = GetObjectPath(sourceObj);

            if (!mirrorConfigs.TryGetValue(sourcePath, out MirrorConfig config))
                return;

            System.Type componentType = sourceComponent.GetType();
            int typeIndex = GetComponentTypeIndex(sourceObj, sourceComponent);
            if (typeIndex < 0)
                return;

            SerializedObject sourceSerializedObject = new SerializedObject(sourceComponent);

            foreach (string targetPath in config.targetObjectPaths)
            {
                GameObject targetObj = FindObjectByPath(targetPath);
                if (targetObj == null)
                    continue;

                Component targetComponent = GetComponentByTypeIndex(targetObj, componentType, typeIndex);
                if (targetComponent == null)
                    continue;

                CopySerializedProperties(sourceSerializedObject, targetComponent, propertyPaths);
            }
        }

        private static int GetComponentTypeIndex(GameObject obj, Component component)
        {
            if (obj == null || component == null)
                return -1;

            Component[] components = obj.GetComponents(component.GetType());
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == component)
                    return i;
            }

            return -1;
        }

        private static Component GetComponentByTypeIndex(GameObject obj, System.Type type, int index)
        {
            if (obj == null || type == null || index < 0)
                return null;

            Component[] components = obj.GetComponents(type);
            return index < components.Length ? components[index] : null;
        }

        private static void CopySerializedProperties(SerializedObject sourceSerializedObject, Component targetComponent, HashSet<string> propertyPaths)
        {
            if (targetComponent == null)
                return;

            SerializedObject targetSerializedObject = new SerializedObject(targetComponent);

            bool anyChanged = false;
            foreach (string propertyPath in propertyPaths)
            {
                if (string.IsNullOrEmpty(propertyPath) || propertyPath == "m_Script")
                    continue;

                SerializedProperty sourceProp = sourceSerializedObject.FindProperty(propertyPath);
                if (sourceProp == null)
                    continue;

                SerializedProperty targetProp = targetSerializedObject.FindProperty(propertyPath);
                if (targetProp == null || sourceProp.propertyType != targetProp.propertyType)
                    continue;

                targetSerializedObject.CopyFromSerializedProperty(sourceProp);
                anyChanged = true;
            }

            if (anyChanged)
            {
                targetSerializedObject.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
