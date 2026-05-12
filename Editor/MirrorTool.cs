using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Yueby.Core.Utils;
using Yueby.Utils;
using Yueby.ModalWindow;

namespace Yueby
{
    [InitializeOnLoad]
    public partial class MirrorTool
    {
        private static Dictionary<string, MirrorConfig> mirrorConfigs = new Dictionary<string, MirrorConfig>();
        private static HashSet<string> mirrorReferenceIds = new HashSet<string>();
        private static HashSet<string> legacyMirrorReferencePaths = new HashSet<string>();
        private static GameObject currentSelectedObject;
        private static string currentSelectedObjectPath;
        private static bool isWindowVisible;
        private static Vector2 windowPosition;
        private static Rect windowRect;
        private static readonly string configPath;
        private static bool isEnabled;
        private static bool showMirrorAxis = true;
        private static PivotRotation lastPivotRotation;
        private static bool isDetailWindowVisible;
        private static Rect detailWindowRect;
        private static bool needUpdateDetailPosition;
        private static ReorderableListDroppable targetList;
        private static Dictionary<int, string> objectPathCache = new Dictionary<int, string>();
        private static Dictionary<string, GameObject> objectByPathCache = new Dictionary<string, GameObject>();
        private static Dictionary<int, float> hierarchyIconBaseXCache = new Dictionary<int, float>();
        private static Dictionary<int, string> objectReferenceIdCache = new Dictionary<int, string>();
        private static Dictionary<int, MirrorReferenceAncestorCacheEntry> mirrorReferenceAncestorCache = new Dictionary<int, MirrorReferenceAncestorCacheEntry>();
        private static Dictionary<long, MirrorReferenceResolutionCacheEntry> mirrorReferenceResolutionCache = new Dictionary<long, MirrorReferenceResolutionCacheEntry>();
        private static HashSet<string> highlightedMirrorObjectPaths = new HashSet<string>();
        private static Dictionary<string, int> highlightedMirrorObjectGroupIndices = new Dictionary<string, int>();
        private static HashSet<string> selectedHighlightedMirrorObjectPaths = new HashSet<string>();
        private static bool pendingMirrorRefresh;
        private static HashSet<int> knownMirrorReferenceInstanceIds = new HashSet<int>();
        private static bool mirrorReferenceInstanceIdCacheDirty = true;

        private const string enableKey = "MirrorTool_Enabled";
        private const string windowSettingsKey = "MirrorTool_WindowSettings";
        private const string toggleMirrorReferenceMenuPath = "GameObject/YuebyTools/Mirror Tool/Mirror Reference";
        private const float listElementSpacing = 2f;
        private const float minVectorSqrMagnitude = 0.000001f;
        private static readonly Vector2 mainWindowSize = new Vector2(120, 90);
        private static readonly Vector2 detailWindowSize = new Vector2(200, 265);
        private static readonly string[] axisLabels = { "X", "Y", "Z" };
        private static GUIStyle cachedStatusStyle;
        private static GUIStyle cachedBadgeTextStyle;
        private static GUIStyle cachedDropLabelStyle;

        public static bool ShowMirrorAxis
        {
            get => showMirrorAxis;
            set => showMirrorAxis = value;
        }

        [System.Serializable]
        public class MirrorConfig
        {
            public List<string> targetObjectPaths = new List<string>();
            public Vector3 mirrorAxis = Vector3.right;

            public MirrorConfig() { }
        }

        [System.Serializable]
        private class WindowSettings
        {
            public float windowPosX = 50;
            public float windowPosY = 10;
            public float detailWindowPosX = 0;
            public float detailWindowPosY = 0;
            public bool detailWindowVisible = false;

            public WindowSettings() { }
        }

        private struct MirrorSpaceContext
        {
            public bool useWorldSpaceFallback;
            public string referencePath;
            public Vector3 planePoint;
            public Vector3 planeNormal;
        }

        private struct MirrorReferenceCandidate
        {
            public string key;
            public GameObject referenceObject;
            public string displayName;
        }

        private class MirrorReferenceAncestorCacheEntry
        {
            public HashSet<string> referenceIdKeys = new HashSet<string>();
            public List<MirrorReferenceCandidate> referenceIdCandidates = new List<MirrorReferenceCandidate>();
            public HashSet<string> legacyReferencePathKeys = new HashSet<string>();
            public List<MirrorReferenceCandidate> legacyReferenceCandidates = new List<MirrorReferenceCandidate>();
        }

        private struct MirrorReferenceResolutionCacheEntry
        {
            public bool hasCommonReference;
            public GameObject referenceObject;
            public string referenceDisplayName;
        }

        static MirrorTool()
        {
            string projectPath = Application.dataPath;
            projectPath = projectPath.Substring(0, projectPath.Length - 6);
            configPath = System.IO.Path.Combine(projectPath, "ProjectSettings", "MirrorTool.json");

            isEnabled = EditorPrefs.GetBool(enableKey, false);
            LoadWindowSettings();
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyWindowItemGUI;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Selection.selectionChanged += OnSelectionChanged;
            if (isEnabled)
            {
                SceneView.duringSceneGui += OnSceneGUI;
            }
            LoadConfigs();
            InitializeComponentSync();
            UpdateHighlightedMirrorObjectPaths();
            EnsureHighlightedMirrorObjectsVisible();
        }

        [MenuItem("Tools/YuebyTools/Mirror Tool %#m", false)]
        private static void ToggleTool()
        {
            isEnabled = !isEnabled;
            EditorPrefs.SetBool(enableKey, isEnabled);

            if (isEnabled)
            {
                SceneView.duringSceneGui += OnSceneGUI;
                YuebyLogger.LogInfo("Mirror Tool Enabled (Shortcut: Ctrl+Shift+M)");
            }
            else
            {
                SceneView.duringSceneGui -= OnSceneGUI;
                isWindowVisible = false;
                YuebyLogger.LogInfo("Mirror Tool Disabled (Shortcut: Ctrl+Shift+M)");
            }

            SceneView.RepaintAll();
        }

        [MenuItem("Tools/YuebyTools/Mirror Tool %#m", true)]
        private static bool ValidateToggleTool()
        {
            Menu.SetChecked("Tools/YuebyTools/Mirror Tool %#m", isEnabled);
            return true;
        }

        [MenuItem(toggleMirrorReferenceMenuPath, false, 49)]
        private static void ToggleMirrorReference(MenuCommand command)
        {
            GameObject targetObject = GetContextGameObject(command);
            if (targetObject == null)
            {
                return;
            }

            bool isMirrorReference = IsMirrorReference(targetObject);
            SetMirrorReferenceState(targetObject, !isMirrorReference);
        }

        [MenuItem(toggleMirrorReferenceMenuPath, true)]
        private static bool ValidateToggleMirrorReference()
        {
            GameObject targetObject = Selection.activeGameObject;
            bool hasTarget = targetObject != null;
            Menu.SetChecked(toggleMirrorReferenceMenuPath, hasTarget && IsMirrorReference(targetObject));
            return hasTarget;
        }

        private static void ScheduleMirrorRefresh()
        {
            if (pendingMirrorRefresh) return;
            pendingMirrorRefresh = true;

            EditorApplication.delayCall += () =>
            {
                pendingMirrorRefresh = false;
                SaveConfigs();
                UpdateHighlightedMirrorObjectPaths();
                EnsureHighlightedMirrorObjectsVisible();
                targetList?.RefreshElementHeights();
                EditorApplication.RepaintHierarchyWindow();
                SceneView.RepaintAll();
            };
        }

        private static void BreakMirrorConnection(GameObject source, GameObject target)
        {
            if (source == null || target == null) return;

            string sourcePath = GetObjectPath(source);
            string targetPath = GetObjectPath(target);

            if (mirrorConfigs.TryGetValue(sourcePath, out var sourceConfig))
            {
                sourceConfig.targetObjectPaths.Remove(targetPath);
                if (sourceConfig.targetObjectPaths.Count == 0)
                {
                    mirrorConfigs.Remove(sourcePath);
                }
            }

            if (mirrorConfigs.TryGetValue(targetPath, out var targetConfig))
            {
                targetConfig.targetObjectPaths.Remove(sourcePath);
                if (targetConfig.targetObjectPaths.Count == 0)
                {
                    mirrorConfigs.Remove(targetPath);
                }
            }

            ScheduleMirrorRefresh();
        }

        public static void EstablishMirrorConnection(GameObject source, GameObject target, Vector3 axis)
        {
            if (source == null || target == null || source == target) return;

            string sourcePath = GetObjectPath(source);
            string targetPath = GetObjectPath(target);

            EnsureMirrorLink(sourcePath, targetPath, axis);
            EnsureMirrorLink(targetPath, sourcePath, axis);

            ScheduleMirrorRefresh();
        }

        private static void EnsureMirrorLink(string fromPath, string toPath, Vector3 axis)
        {
            if (!mirrorConfigs.TryGetValue(fromPath, out var config))
            {
                config = new MirrorConfig();
                mirrorConfigs[fromPath] = config;
            }

            if (!config.targetObjectPaths.Contains(toPath))
            {
                config.targetObjectPaths.Add(toPath);
            }

            config.mirrorAxis = axis;
        }
    }
}
