using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using Yueby.Core.Utils;

namespace Yueby
{
    public partial class MirrorTool
    {
        private static void SaveConfigs()
        {
            NormalizeConfigData();
            InvalidateMirrorReferenceCaches();
            mirrorReferenceInstanceIdCacheDirty = true;
            string json = JsonUtility.ToJson(new SerializableDict(mirrorConfigs, mirrorReferenceIds, legacyMirrorReferencePaths), true);
            System.IO.File.WriteAllText(configPath, json);
        }

        private static void LoadConfigs()
        {
            mirrorConfigs = new Dictionary<string, MirrorConfig>();
            mirrorReferenceIds = new HashSet<string>();
            legacyMirrorReferencePaths = new HashSet<string>();

            if (System.IO.File.Exists(configPath))
            {
                string json = System.IO.File.ReadAllText(configPath);
                SerializableDict serializableDict = JsonUtility.FromJson<SerializableDict>(json);
                if (serializableDict != null)
                {
                    mirrorConfigs = serializableDict.ToDictionary();
                    mirrorReferenceIds = serializableDict.ToMirrorReferenceIdSet();
                    legacyMirrorReferencePaths = serializableDict.ToLegacyMirrorReferencePathSet();
                }
            }

            InvalidateAllCachesIncludingPaths();
            bool needsSave = NormalizeConfigData();
            needsSave |= MigrateReferenceData();
            if (needsSave)
            {
                SaveConfigs();
            }
        }

        public static string GetObjectPath(GameObject obj)
        {
            if (obj == null) return "";

            int instanceId = obj.GetInstanceID();
            if (objectPathCache.TryGetValue(instanceId, out string cached))
            {
                int lastSlash = cached.LastIndexOf('/');
                int nameStart = lastSlash + 1;
                int nameLen = cached.Length - nameStart;
                string objName = obj.name;

                if (nameLen == objName.Length && string.CompareOrdinal(cached, nameStart, objName, 0, nameLen) == 0)
                {
                    return cached;
                }

                objectPathCache.Remove(instanceId);
            }

            string path = obj.name;
            Transform parent = obj.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            objectPathCache[instanceId] = path;
            return path;
        }

        public static GameObject FindObjectByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            if (objectByPathCache.TryGetValue(path, out GameObject cached))
            {
                if (cached != null) return cached;
                objectByPathCache.Remove(path);
            }

            GameObject obj = GameObject.Find(path);
            if (obj != null)
            {
                objectByPathCache[path] = obj;
            }

            return obj;
        }

        private static string GetObjectReferenceId(GameObject obj)
        {
            if (obj == null)
            {
                return string.Empty;
            }

            int instanceId = obj.GetInstanceID();
            if (objectReferenceIdCache.TryGetValue(instanceId, out string cachedReferenceId))
            {
                return cachedReferenceId;
            }

            GlobalObjectId globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(obj);
            string referenceId = globalObjectId.ToString();
            if (string.IsNullOrEmpty(referenceId))
            {
                objectReferenceIdCache[instanceId] = string.Empty;
                return string.Empty;
            }

            if (!TryResolveObjectByReferenceId(referenceId, out GameObject resolvedObject) || resolvedObject != obj)
            {
                objectReferenceIdCache[instanceId] = string.Empty;
                return string.Empty;
            }

            objectReferenceIdCache[instanceId] = referenceId;
            return referenceId;
        }

        private static bool TryResolveObjectByReferenceId(string referenceId, out GameObject targetObject)
        {
            targetObject = null;
            if (string.IsNullOrEmpty(referenceId) || !GlobalObjectId.TryParse(referenceId, out GlobalObjectId globalObjectId))
            {
                return false;
            }

            targetObject = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalObjectId) as GameObject;
            return targetObject != null;
        }

        private static bool TryFindUniqueLoadedSceneObjectByPath(string targetPath, out GameObject targetObject)
        {
            targetObject = null;
            if (string.IsNullOrEmpty(targetPath))
            {
                return false;
            }

            List<GameObject> matches = new List<GameObject>();
            int sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int sceneIndex = 0; sceneIndex < sceneCount; sceneIndex++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                foreach (GameObject rootObject in scene.GetRootGameObjects())
                {
                    CollectLoadedSceneObjectsByPath(rootObject.transform, targetPath, matches);
                    if (matches.Count > 1)
                    {
                        targetObject = null;
                        return false;
                    }
                }
            }

            if (matches.Count != 1)
            {
                return false;
            }

            targetObject = matches[0];
            return true;
        }

        private static void CollectLoadedSceneObjectsByPath(Transform currentTransform, string targetPath, List<GameObject> matches)
        {
            if (currentTransform == null || matches == null || matches.Count > 1)
            {
                return;
            }

            GameObject currentObject = currentTransform.gameObject;
            if (GetObjectPath(currentObject) == targetPath)
            {
                matches.Add(currentObject);
                if (matches.Count > 1)
                {
                    return;
                }
            }

            for (int childIndex = 0; childIndex < currentTransform.childCount; childIndex++)
            {
                CollectLoadedSceneObjectsByPath(currentTransform.GetChild(childIndex), targetPath, matches);
                if (matches.Count > 1)
                {
                    return;
                }
            }
        }

        private static bool HasMirrorReferenceData()
        {
            return mirrorReferenceIds.Count > 0 || legacyMirrorReferencePaths.Count > 0;
        }

        private static void InvalidateAllCaches()
        {
            hierarchyIconBaseXCache.Clear();
            objectByPathCache.Clear();
            InvalidateMirrorReferenceCaches();
        }

        private static void InvalidateAllCachesIncludingPaths()
        {
            objectPathCache.Clear();
            InvalidateAllCaches();
        }

        private static void InvalidateMirrorReferenceCaches()
        {
            mirrorReferenceAncestorCache.Clear();
            mirrorReferenceResolutionCache.Clear();
        }

        private static void EnsureMirrorReferenceInstanceIdCache()
        {
            if (!mirrorReferenceInstanceIdCacheDirty) return;

            knownMirrorReferenceInstanceIds.Clear();

            foreach (string referenceId in mirrorReferenceIds)
            {
                if (TryResolveObjectByReferenceId(referenceId, out GameObject obj))
                {
                    knownMirrorReferenceInstanceIds.Add(obj.GetInstanceID());
                }
            }

            mirrorReferenceInstanceIdCacheDirty = false;
        }

        private static bool TryMigrateLegacyMirrorReferencePaths()
        {
            if (legacyMirrorReferencePaths.Count == 0)
            {
                return false;
            }

            bool changed = false;
            HashSet<string> cleanedPaths = new HashSet<string>();

            foreach (string legacyPath in legacyMirrorReferencePaths)
            {
                if (string.IsNullOrEmpty(legacyPath))
                {
                    changed = true;
                    continue;
                }

                cleanedPaths.Add(legacyPath);

                if (!TryFindUniqueLoadedSceneObjectByPath(legacyPath, out GameObject referenceObject))
                {
                    continue;
                }

                string referenceId = GetObjectReferenceId(referenceObject);
                if (!string.IsNullOrEmpty(referenceId))
                {
                    changed |= mirrorReferenceIds.Add(referenceId);
                }
            }

            if (!cleanedPaths.SetEquals(legacyMirrorReferencePaths))
            {
                legacyMirrorReferencePaths = cleanedPaths;
                changed = true;
            }

            return changed;
        }

        private static bool TryBackfillPathsFromReferenceIds()
        {
            if (mirrorReferenceIds.Count == 0)
            {
                return false;
            }

            bool changed = false;
            foreach (string referenceId in mirrorReferenceIds)
            {
                if (!TryResolveObjectByReferenceId(referenceId, out GameObject obj))
                {
                    continue;
                }

                string path = GetObjectPath(obj);
                if (!string.IsNullOrEmpty(path))
                {
                    changed |= legacyMirrorReferencePaths.Add(path);
                }
            }

            return changed;
        }

        [System.Serializable]
        private class SerializableDict
        {
            public List<string> keys = new List<string>();
            public List<MirrorConfig> values = new List<MirrorConfig>();
            public List<string> mirrorReferenceIds = new List<string>();
            public List<string> mirrorReferencePaths = new List<string>();

            public SerializableDict()
            {
            }

            public SerializableDict(Dictionary<string, MirrorConfig> dict, HashSet<string> referenceIds, HashSet<string> unresolvedLegacyReferencePaths)
            {
                foreach (var kvp in dict)
                {
                    keys.Add(kvp.Key);
                    values.Add(kvp.Value ?? new MirrorConfig());
                }

                if (referenceIds != null)
                {
                    mirrorReferenceIds = referenceIds.Where(referenceId => !string.IsNullOrEmpty(referenceId)).ToList();
                }

                mirrorReferencePaths = unresolvedLegacyReferencePaths == null
                    ? new List<string>()
                    : unresolvedLegacyReferencePaths.Where(path => !string.IsNullOrEmpty(path)).ToList();
            }

            public Dictionary<string, MirrorConfig> ToDictionary()
            {
                Dictionary<string, MirrorConfig> dict = new Dictionary<string, MirrorConfig>();
                if (keys == null || values == null)
                {
                    return dict;
                }

                int count = Mathf.Min(keys.Count, values.Count);
                for (int i = 0; i < count; i++)
                {
                    if (string.IsNullOrEmpty(keys[i]))
                    {
                        continue;
                    }

                    MirrorConfig value = values[i] ?? new MirrorConfig();
                    if (value.targetObjectPaths == null)
                    {
                        value.targetObjectPaths = new List<string>();
                    }
                    dict[keys[i]] = value;
                }
                return dict;
            }

            public HashSet<string> ToMirrorReferenceIdSet()
            {
                HashSet<string> ids = new HashSet<string>();
                if (mirrorReferenceIds == null)
                {
                    return ids;
                }

                foreach (string referenceId in mirrorReferenceIds)
                {
                    if (!string.IsNullOrEmpty(referenceId) && GlobalObjectId.TryParse(referenceId, out _))
                    {
                        ids.Add(referenceId);
                    }
                }

                return ids;
            }

            public HashSet<string> ToLegacyMirrorReferencePathSet()
            {
                return mirrorReferencePaths == null
                    ? new HashSet<string>()
                    : new HashSet<string>(mirrorReferencePaths.Where(path => !string.IsNullOrEmpty(path)));
            }
        }

        public static void ClearAllData()
        {
            mirrorConfigs.Clear();
            mirrorReferenceIds.Clear();
            legacyMirrorReferencePaths.Clear();
            objectReferenceIdCache.Clear();
            targetList = null;
            InvalidateAllCachesIncludingPaths();
            SaveConfigs();
            SceneView.RepaintAll();
            YuebyLogger.LogInfo("Mirror Tool data cleared.");
        }

        private static void CleanEmptyConfigs()
        {
            var keysToRemove = mirrorConfigs.Where(kvp => kvp.Value == null || kvp.Value.targetObjectPaths == null || kvp.Value.targetObjectPaths.Count == 0)
                                          .Select(kvp => kvp.Key)
                                          .ToList();

            foreach (var key in keysToRemove)
            {
                mirrorConfigs.Remove(key);
            }
        }

        private static bool MigrateReferenceData()
        {
            bool changed = false;
            if (TryMigrateLegacyMirrorReferencePaths()) changed = true;
            if (TryBackfillPathsFromReferenceIds()) changed = true;
            return changed;
        }

        private static bool NormalizeConfigData()
        {
            bool changed = false;

            HashSet<string> normalizedReferenceIds = new HashSet<string>();
            foreach (string referenceId in mirrorReferenceIds)
            {
                if (string.IsNullOrEmpty(referenceId))
                {
                    changed = true;
                    continue;
                }

                if (GlobalObjectId.TryParse(referenceId, out _))
                {
                    normalizedReferenceIds.Add(referenceId);
                }
                else
                {
                    changed = true;
                }
            }

            if (!normalizedReferenceIds.SetEquals(mirrorReferenceIds))
            {
                mirrorReferenceIds = normalizedReferenceIds;
                changed = true;
            }

            HashSet<string> normalizedLegacyReferencePaths = new HashSet<string>(legacyMirrorReferencePaths.Where(path => !string.IsNullOrEmpty(path)));
            if (!normalizedLegacyReferencePaths.SetEquals(legacyMirrorReferencePaths))
            {
                legacyMirrorReferencePaths = normalizedLegacyReferencePaths;
                changed = true;
            }

            foreach (string key in mirrorConfigs.Keys.ToList())
            {
                if (string.IsNullOrEmpty(key))
                {
                    mirrorConfigs.Remove(key);
                    changed = true;
                    continue;
                }

                MirrorConfig config = mirrorConfigs[key] ?? new MirrorConfig();
                if (mirrorConfigs[key] == null)
                {
                    mirrorConfigs[key] = config;
                    changed = true;
                }

                if (config.targetObjectPaths == null)
                {
                    config.targetObjectPaths = new List<string>();
                    changed = true;
                }

                List<string> cleanedTargetPaths = new List<string>();
                HashSet<string> seenTargetPaths = new HashSet<string>();
                foreach (string targetPath in config.targetObjectPaths)
                {
                    if (string.IsNullOrEmpty(targetPath))
                    {
                        changed = true;
                        continue;
                    }

                    if (seenTargetPaths.Add(targetPath))
                    {
                        cleanedTargetPaths.Add(targetPath);
                    }
                    else
                    {
                        changed = true;
                    }
                }

                if (!cleanedTargetPaths.SequenceEqual(config.targetObjectPaths))
                {
                    config.targetObjectPaths.Clear();
                    config.targetObjectPaths.AddRange(cleanedTargetPaths);
                    changed = true;
                }
            }

            int configCountBeforeClean = mirrorConfigs.Count;
            CleanEmptyConfigs();
            if (mirrorConfigs.Count != configCountBeforeClean)
            {
                changed = true;
            }

            return changed;
        }
    }
}
