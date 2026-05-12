using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Yueby
{
    public partial class MirrorTool
    {
        private static void SyncMirrorAxisAcrossConnections(GameObject source, MirrorConfig config)
        {
            if (source == null || config == null)
            {
                return;
            }

            foreach (var targetPath in config.targetObjectPaths)
            {
                var targetObj = FindObjectByPath(targetPath);
                if (targetObj != null)
                {
                    EstablishMirrorConnection(source, targetObj, config.mirrorAxis);
                }
            }
        }

        private static void ApplyMirror(GameObject source, GameObject target, Vector3 axis)
        {
            if (source == null || target == null) return;

            MirrorSpaceContext mirrorContext = ResolveMirrorSpaceContext(source, target, axis);
            ApplyMirroredPosition(source.transform, target.transform, mirrorContext);
            ApplyMirroredRotation(source.transform, target.transform, mirrorContext.planeNormal);
            target.transform.localScale = source.transform.localScale;
        }

        private static MirrorSpaceContext ResolveMirrorSpaceContext(GameObject source, GameObject target, Vector3 axis)
        {
            Vector3 safeAxis = GetSafeMirrorAxis(axis);
            if (TryResolveCommonMirrorReference(source, target, out var referenceObject, out var referenceDisplayName))
            {
                return new MirrorSpaceContext
                {
                    useWorldSpaceFallback = false,
                    referencePath = referenceDisplayName,
                    planePoint = referenceObject.transform.position,
                    planeNormal = referenceObject.transform.TransformDirection(safeAxis).normalized
                };
            }

            return new MirrorSpaceContext
            {
                useWorldSpaceFallback = true,
                referencePath = string.Empty,
                planePoint = Vector3.zero,
                planeNormal = safeAxis.normalized
            };
        }

        private static void ApplyMirroredPosition(Transform sourceTransform, Transform targetTransform, MirrorSpaceContext mirrorContext)
        {
            Vector3 sourceOffset = sourceTransform.position - mirrorContext.planePoint;
            Vector3 mirroredOffset = Vector3.Reflect(sourceOffset, mirrorContext.planeNormal);
            targetTransform.position = mirrorContext.planePoint + mirroredOffset;
        }

        private static void ApplyMirroredRotation(Transform sourceTransform, Transform targetTransform, Vector3 planeNormal)
        {
            Vector3 forward = Vector3.Reflect(sourceTransform.rotation * Vector3.forward, planeNormal);
            Vector3 up = Vector3.Reflect(sourceTransform.rotation * Vector3.up, planeNormal);

            if (forward.sqrMagnitude < minVectorSqrMagnitude || up.sqrMagnitude < minVectorSqrMagnitude)
            {
                targetTransform.rotation = sourceTransform.rotation;
                return;
            }

            forward.Normalize();
            up = Vector3.ProjectOnPlane(up, forward);
            if (up.sqrMagnitude < minVectorSqrMagnitude)
            {
                targetTransform.rotation = sourceTransform.rotation;
                return;
            }

            up.Normalize();
            targetTransform.rotation = Quaternion.LookRotation(forward, up);
        }

        private static bool TryResolveCommonMirrorReference(GameObject source, GameObject target, out GameObject referenceObject, out string referenceDisplayName)
        {
            referenceObject = null;
            referenceDisplayName = string.Empty;

            if (source == null || target == null || !HasMirrorReferenceData())
            {
                return false;
            }

            long cacheKey = GetMirrorReferenceResolutionCacheKey(source, target);
            if (mirrorReferenceResolutionCache.TryGetValue(cacheKey, out MirrorReferenceResolutionCacheEntry cachedResolution))
            {
                if (!cachedResolution.hasCommonReference)
                {
                    return false;
                }

                if (cachedResolution.referenceObject != null)
                {
                    referenceObject = cachedResolution.referenceObject;
                    referenceDisplayName = cachedResolution.referenceDisplayName;
                    return true;
                }

                mirrorReferenceResolutionCache.Remove(cacheKey);
            }

            MirrorReferenceAncestorCacheEntry sourceAncestorCache = GetMirrorReferenceAncestorCache(source);
            MirrorReferenceAncestorCacheEntry targetAncestorCache = GetMirrorReferenceAncestorCache(target);

            if (TryResolveCommonMirrorReferenceFromCandidates(targetAncestorCache.referenceIdCandidates, sourceAncestorCache.referenceIdKeys, out referenceObject, out referenceDisplayName))
            {
                CacheMirrorReferenceResolution(cacheKey, true, referenceObject, referenceDisplayName);
                return true;
            }

            if (source.scene != target.scene)
            {
                CacheMirrorReferenceResolution(cacheKey, false, null, string.Empty);
                return false;
            }

            if (TryResolveCommonMirrorReferenceFromCandidates(targetAncestorCache.legacyReferenceCandidates, sourceAncestorCache.legacyReferencePathKeys, out referenceObject, out referenceDisplayName))
            {
                CacheMirrorReferenceResolution(cacheKey, true, referenceObject, referenceDisplayName);
                return true;
            }

            CacheMirrorReferenceResolution(cacheKey, false, null, string.Empty);
            return false;
        }

        private static MirrorReferenceAncestorCacheEntry GetMirrorReferenceAncestorCache(GameObject targetObject)
        {
            if (targetObject == null)
            {
                return new MirrorReferenceAncestorCacheEntry();
            }

            int instanceId = targetObject.GetInstanceID();
            if (mirrorReferenceAncestorCache.TryGetValue(instanceId, out MirrorReferenceAncestorCacheEntry cachedAncestors))
            {
                return cachedAncestors;
            }

            MirrorReferenceAncestorCacheEntry ancestorCache = new MirrorReferenceAncestorCacheEntry();
            Transform ancestor = targetObject.transform;
            while (ancestor != null)
            {
                GameObject ancestorObject = ancestor.gameObject;

                string referenceId = GetObjectReferenceId(ancestorObject);
                if (!string.IsNullOrEmpty(referenceId)
                    && mirrorReferenceIds.Contains(referenceId)
                    && ancestorCache.referenceIdKeys.Add(referenceId))
                {
                    ancestorCache.referenceIdCandidates.Add(new MirrorReferenceCandidate
                    {
                        key = referenceId,
                        referenceObject = ancestorObject,
                        displayName = GetObjectPath(ancestorObject)
                    });
                }

                string ancestorPath = GetObjectPath(ancestorObject);
                if (!string.IsNullOrEmpty(ancestorPath)
                    && legacyMirrorReferencePaths.Contains(ancestorPath)
                    && ancestorCache.legacyReferencePathKeys.Add(ancestorPath))
                {
                    ancestorCache.legacyReferenceCandidates.Add(new MirrorReferenceCandidate
                    {
                        key = ancestorPath,
                        referenceObject = ancestorObject,
                        displayName = ancestorPath
                    });
                }

                ancestor = ancestor.parent;
            }

            mirrorReferenceAncestorCache[instanceId] = ancestorCache;
            return ancestorCache;
        }

        private static bool TryResolveCommonMirrorReferenceFromCandidates(List<MirrorReferenceCandidate> candidates, HashSet<string> sourceKeys, out GameObject referenceObject, out string referenceDisplayName)
        {
            referenceObject = null;
            referenceDisplayName = string.Empty;

            if (candidates == null || sourceKeys == null || sourceKeys.Count == 0)
            {
                return false;
            }

            foreach (MirrorReferenceCandidate candidate in candidates)
            {
                if (!string.IsNullOrEmpty(candidate.key)
                    && sourceKeys.Contains(candidate.key)
                    && candidate.referenceObject != null)
                {
                    referenceObject = candidate.referenceObject;
                    referenceDisplayName = candidate.displayName;
                    return true;
                }
            }

            return false;
        }

        private static void CacheMirrorReferenceResolution(long cacheKey, bool hasCommonReference, GameObject referenceObject, string referenceDisplayName)
        {
            mirrorReferenceResolutionCache[cacheKey] = new MirrorReferenceResolutionCacheEntry
            {
                hasCommonReference = hasCommonReference,
                referenceObject = referenceObject,
                referenceDisplayName = referenceDisplayName ?? string.Empty
            };
        }

        private static long GetMirrorReferenceResolutionCacheKey(GameObject source, GameObject target)
        {
            uint sourceInstanceId = unchecked((uint)source.GetInstanceID());
            uint targetInstanceId = unchecked((uint)target.GetInstanceID());
            return ((long)sourceInstanceId << 32) | targetInstanceId;
        }

        private static string GetMirrorReferenceStatusText(GameObject source, GameObject target, string targetPath)
        {
            if (source == null)
            {
                return "当前回退世界空间";
            }

            if (target == null)
            {
                return string.IsNullOrEmpty(targetPath)
                    ? "当前回退世界空间（未指定目标对象）"
                    : $"目标对象当前不可解析：{targetPath}";
            }

            if (TryResolveCommonMirrorReference(source, target, out _, out var referenceDisplayName))
            {
                return $"已解析共同基准：{referenceDisplayName}";
            }

            return HasMirrorReferenceData()
                ? "未找到共同镜像基准，当前回退世界空间"
                : "当前回退世界空间";
        }

        private static Vector3 GetSafeMirrorAxis(Vector3 axis)
        {
            return axis.sqrMagnitude < minVectorSqrMagnitude ? Vector3.right : axis.normalized;
        }
    }
}
