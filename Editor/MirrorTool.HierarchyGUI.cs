using UnityEditor;
using UnityEngine;

namespace Yueby
{
    public partial class MirrorTool
    {
        private const int hierarchyMirrorIconTextureSize = 16;
        private const float hierarchyMirrorIconSize = 12f;
        private const float hierarchyMirrorIconSpacing = 3f;
        private const float hierarchyGameObjectIconSpacing = 2f;

        private static readonly GUIContent hierarchyMirrorIconContent = new GUIContent { tooltip = "Mirror Reference" };
        private static readonly GUIContent hierarchyConnectionIconContent = new GUIContent { tooltip = "Has Mirror Connection" };

        private static Texture2D hierarchyMirrorIconDarkSkin;
        private static Texture2D hierarchyMirrorIconLightSkin;
        private static Texture2D hierarchyConnectionIconDarkSkin;
        private static Texture2D hierarchyConnectionIconLightSkin;
        private static GUIStyle hierarchyMirrorNameStyle;
        private static GUIStyle hierarchyReferenceLabelStyle;
        private static bool hierarchyReferenceLabelStyleIsProSkin;

        private static readonly Color[] hierarchyMirrorGroupColorsDarkSkin =
        {
            new Color(0.40f, 0.74f, 1f, 1f),
            new Color(1f, 0.66f, 0.34f, 1f),
            new Color(0.98f, 0.48f, 0.80f, 1f),
            new Color(0.46f, 0.88f, 0.62f, 1f),
            new Color(0.97f, 0.87f, 0.39f, 1f),
            new Color(0.73f, 0.60f, 1f, 1f)
        };

        private static readonly Color[] hierarchyMirrorGroupColorsLightSkin =
        {
            new Color(0.10f, 0.43f, 0.86f, 1f),
            new Color(0.82f, 0.42f, 0.10f, 1f),
            new Color(0.76f, 0.24f, 0.60f, 1f),
            new Color(0.14f, 0.62f, 0.34f, 1f),
            new Color(0.71f, 0.58f, 0.08f, 1f),
            new Color(0.45f, 0.30f, 0.82f, 1f)
        };

        private static bool HasAnyMirrorData()
        {
            return mirrorConfigs.Count > 0
                || highlightedMirrorObjectPaths.Count > 0
                || mirrorReferenceIds.Count > 0
                || legacyMirrorReferencePaths.Count > 0;
        }

        private static void OnHierarchyWindowItemGUI(int instanceId, Rect selectionRect)
        {
            if (!HasAnyMirrorData()) return;

            GameObject targetObject = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (targetObject == null)
            {
                return;
            }

            string objectPath = GetObjectPath(targetObject);
            if (string.IsNullOrEmpty(objectPath))
            {
                return;
            }

            bool isHighlighted = highlightedMirrorObjectPaths.Count > 0
                && highlightedMirrorObjectPaths.Contains(objectPath);

            if (isHighlighted)
            {
                DrawHierarchyMirrorHighlight(selectionRect, objectPath);
            }

            bool hasMirrorConnection = HasMirrorConnection(objectPath);
            bool isMirrorRef = HasMirrorReferenceData() && IsMirrorReference(targetObject, objectPath);

            if (!hasMirrorConnection && !isMirrorRef && !isHighlighted)
            {
                return;
            }

            float iconBaseX = GetHierarchyIconBaseX(targetObject, selectionRect);
            float iconY = selectionRect.y + (selectionRect.height - hierarchyMirrorIconSize) * 0.5f;
            float currentX = iconBaseX;

            if (hasMirrorConnection)
            {
                if (currentX + hierarchyMirrorIconSize <= selectionRect.xMax)
                {
                    Rect connectionIconRect = new Rect(currentX, iconY, hierarchyMirrorIconSize, hierarchyMirrorIconSize);
                    DrawHierarchyConnectionIcon(connectionIconRect);
                    currentX += hierarchyMirrorIconSize + 2f;
                }
            }

            if (isMirrorRef)
            {
                if (currentX + hierarchyMirrorIconSize <= selectionRect.xMax)
                {
                    Rect referenceIconRect = new Rect(currentX, iconY, hierarchyMirrorIconSize, hierarchyMirrorIconSize);
                    DrawHierarchyMirrorIcon(referenceIconRect);
                    currentX += hierarchyMirrorIconSize + 2f;
                }
            }

            if (isHighlighted)
            {
                string referenceLabel = GetHighlightedMirrorReferenceLabel(targetObject, objectPath);
                if (!string.IsNullOrEmpty(referenceLabel) && currentX + 10f <= selectionRect.xMax)
                {
                    DrawMirrorReferenceLabel(selectionRect, currentX, referenceLabel);
                }
            }
        }

        private static float GetHierarchyIconBaseX(GameObject targetObject, Rect selectionRect)
        {
            if (string.IsNullOrEmpty(targetObject.name))
            {
                return selectionRect.xMax;
            }

            int instanceId = targetObject.GetInstanceID();
            if (hierarchyIconBaseXCache.TryGetValue(instanceId, out float cachedOffset))
            {
                return selectionRect.x + cachedOffset;
            }

            GUIStyle nameStyle = GetHierarchyMirrorNameStyle();
            Vector2 nameSize = nameStyle.CalcSize(new GUIContent(targetObject.name));
            float gameObjectIconWidth = GetHierarchyGameObjectIconWidth(targetObject, selectionRect);
            float offset = gameObjectIconWidth + nameSize.x + hierarchyMirrorIconSpacing;
            hierarchyIconBaseXCache[instanceId] = offset;
            return selectionRect.x + offset;
        }

        private static bool HasMirrorConnection(string objectPath)
        {
            return !string.IsNullOrEmpty(objectPath)
                && mirrorConfigs.TryGetValue(objectPath, out MirrorConfig config)
                && config.targetObjectPaths != null
                && config.targetObjectPaths.Count > 0;
        }

        private static float GetHierarchyGameObjectIconWidth(GameObject targetObject, Rect selectionRect)
        {
            GUIContent objectContent = EditorGUIUtility.ObjectContent(targetObject, typeof(GameObject));
            Texture objectIcon = objectContent.image;
            if (objectIcon == null)
            {
                return 0f;
            }

            float displayedIconWidth = Mathf.Min(selectionRect.height, hierarchyMirrorIconTextureSize);
            return displayedIconWidth + hierarchyGameObjectIconSpacing;
        }

        private static void DrawHierarchyMirrorIcon(Rect iconRect)
        {
            GUIContent iconContent = GetHierarchyMirrorIconContent();
            GUI.Label(iconRect, iconContent, GUIStyle.none);
        }

        private static void DrawHierarchyConnectionIcon(Rect iconRect)
        {
            hierarchyConnectionIconContent.image = GetHierarchyConnectionIconTexture();
            GUI.Label(iconRect, hierarchyConnectionIconContent, GUIStyle.none);
        }

        private static string GetHighlightedMirrorReferenceLabel(GameObject targetObject, string objectPath)
        {
            if (targetObject == null || string.IsNullOrEmpty(objectPath))
            {
                return null;
            }

            GameObject peerObject = FindMirrorPeer(targetObject, objectPath);
            if (peerObject == null)
            {
                return null;
            }

            if (TryResolveCommonMirrorReference(targetObject, peerObject, out _, out string displayName))
            {
                int lastSlash = displayName.LastIndexOf('/');
                return lastSlash >= 0 ? displayName.Substring(lastSlash + 1) : displayName;
            }

            return null;
        }

        private static GameObject FindMirrorPeer(GameObject targetObject, string targetPath)
        {
            if (mirrorConfigs.TryGetValue(targetPath, out MirrorConfig config) && config.targetObjectPaths != null)
            {
                foreach (string peerPath in config.targetObjectPaths)
                {
                    GameObject peer = FindObjectByPath(peerPath);
                    if (peer != null && peer != targetObject)
                    {
                        return peer;
                    }
                }
            }

            return null;
        }

        private static void DrawMirrorReferenceLabel(Rect selectionRect, float startX, string label)
        {
            GUIStyle labelStyle = GetHierarchyReferenceLabelStyle();
            GUIContent content = new GUIContent(label);
            Vector2 labelSize = labelStyle.CalcSize(content);

            float availableWidth = selectionRect.xMax - startX;
            if (labelSize.x > availableWidth)
            {
                labelSize.x = availableWidth;
            }

            Rect labelRect = new Rect(startX, selectionRect.y, labelSize.x, selectionRect.height);
            GUI.Label(labelRect, content, labelStyle);
        }

        private static GUIStyle GetHierarchyReferenceLabelStyle()
        {
            if (hierarchyReferenceLabelStyle != null && hierarchyReferenceLabelStyleIsProSkin == EditorGUIUtility.isProSkin)
            {
                return hierarchyReferenceLabelStyle;
            }

            hierarchyReferenceLabelStyleIsProSkin = EditorGUIUtility.isProSkin;
            hierarchyReferenceLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(1f, 1f, 1f, 0.35f)
                        : new Color(0f, 0f, 0f, 0.40f)
                }
            };

            return hierarchyReferenceLabelStyle;
        }

        private static void DrawHierarchyMirrorHighlight(Rect selectionRect, string objectPath)
        {
            if (!highlightedMirrorObjectGroupIndices.TryGetValue(objectPath, out int groupIndex))
            {
                return;
            }

            bool isSelectedObject = selectedHighlightedMirrorObjectPaths.Count > 0
                && selectedHighlightedMirrorObjectPaths.Contains(objectPath);
            Rect highlightRect = new Rect(selectionRect.x - 1f, selectionRect.y + 1f, selectionRect.width + 2f, Mathf.Max(1f, selectionRect.height - 2f));
            Color accentColor = GetHierarchyMirrorHighlightBaseColor(groupIndex);
            Color fillColor = GetHierarchyMirrorHighlightFillColor(accentColor, isSelectedObject);
            Color lineColor = GetHierarchyMirrorHighlightAccentColor(accentColor, isSelectedObject);

            EditorGUI.DrawRect(highlightRect, fillColor);
            EditorGUI.DrawRect(new Rect(highlightRect.x, highlightRect.y, 2f, highlightRect.height), lineColor);
        }

        private static Color GetHierarchyMirrorHighlightBaseColor(int groupIndex)
        {
            Color[] palette = EditorGUIUtility.isProSkin
                ? hierarchyMirrorGroupColorsDarkSkin
                : hierarchyMirrorGroupColorsLightSkin;
            int paletteIndex = Mathf.Abs(groupIndex) % palette.Length;
            return palette[paletteIndex];
        }

        private static Color GetHierarchyMirrorHighlightFillColor(Color baseColor, bool isSelectedObject)
        {
            float alpha = EditorGUIUtility.isProSkin
                ? (isSelectedObject ? 0.18f : 0.10f)
                : (isSelectedObject ? 0.16f : 0.09f);

            return new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }

        private static Color GetHierarchyMirrorHighlightAccentColor(Color baseColor, bool isSelectedObject)
        {
            float alpha = isSelectedObject ? 0.98f : 0.76f;
            return new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }

        private static GUIContent GetHierarchyMirrorIconContent()
        {
            hierarchyMirrorIconContent.image = GetHierarchyMirrorIconTexture();
            return hierarchyMirrorIconContent;
        }

        private static GUIStyle GetHierarchyMirrorNameStyle()
        {
            return hierarchyMirrorNameStyle ??= new GUIStyle(EditorStyles.label ?? GUI.skin?.label ?? GUIStyle.none);
        }

        private static Texture2D GetHierarchyMirrorIconTexture()
        {
            if (EditorGUIUtility.isProSkin)
            {
                hierarchyMirrorIconDarkSkin ??= CreateHierarchyMirrorIconTexture(
                    new Color(0.5f, 0.74f, 1f, 1f),
                    new Color(0.75f, 0.88f, 1f, 1f));

                return hierarchyMirrorIconDarkSkin;
            }

            hierarchyMirrorIconLightSkin ??= CreateHierarchyMirrorIconTexture(
                new Color(0.11f, 0.36f, 0.7f, 1f),
                new Color(0.21f, 0.53f, 0.92f, 1f));

            return hierarchyMirrorIconLightSkin;
        }

        private static Texture2D CreateBlankIconTexture(string name)
        {
            Texture2D texture = new Texture2D(hierarchyMirrorIconTextureSize, hierarchyMirrorIconTextureSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
                name = name
            };

            Color[] pixels = new Color[hierarchyMirrorIconTextureSize * hierarchyMirrorIconTextureSize];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.clear;
            }

            texture.SetPixels(pixels);
            return texture;
        }

        private static Texture2D CreateHierarchyMirrorIconTexture(Color lineColor, Color arrowColor)
        {
            Texture2D texture = CreateBlankIconTexture("MirrorToolHierarchyIcon");

            DrawVerticalLine(texture, 8, 2, 13, lineColor);
            DrawHorizontalLine(texture, 2, 6, 6, arrowColor);
            DrawHorizontalLine(texture, 10, 14, 6, arrowColor);
            DrawHorizontalLine(texture, 3, 5, 10, arrowColor);
            DrawHorizontalLine(texture, 11, 13, 10, arrowColor);
            DrawChevron(texture, 6, 6, true, arrowColor);
            DrawChevron(texture, 10, 6, false, arrowColor);
            DrawChevron(texture, 5, 10, false, arrowColor);
            DrawChevron(texture, 11, 10, true, arrowColor);

            texture.Apply();
            return texture;
        }

        private static void DrawVerticalLine(Texture2D texture, int x, int startY, int endY, Color color)
        {
            for (int y = startY; y <= endY; y++)
            {
                texture.SetPixel(x, y, color);
            }
        }

        private static void DrawHorizontalLine(Texture2D texture, int startX, int endX, int y, Color color)
        {
            for (int x = startX; x <= endX; x++)
            {
                texture.SetPixel(x, y, color);
            }
        }

        private static void DrawChevron(Texture2D texture, int tipX, int tipY, bool pointsRight, Color color)
        {
            int direction = pointsRight ? -1 : 1;

            texture.SetPixel(tipX, tipY, color);
            texture.SetPixel(tipX + direction, tipY + 1, color);
            texture.SetPixel(tipX + direction, tipY - 1, color);
        }

        private static Texture2D GetHierarchyConnectionIconTexture()
        {
            if (EditorGUIUtility.isProSkin)
            {
                hierarchyConnectionIconDarkSkin ??= CreateHierarchyConnectionIconTexture(
                    new Color(0.46f, 0.88f, 0.62f, 1f),
                    new Color(0.64f, 0.95f, 0.76f, 1f));

                return hierarchyConnectionIconDarkSkin;
            }

            hierarchyConnectionIconLightSkin ??= CreateHierarchyConnectionIconTexture(
                new Color(0.14f, 0.55f, 0.30f, 1f),
                new Color(0.18f, 0.68f, 0.38f, 1f));

            return hierarchyConnectionIconLightSkin;
        }

        private static Texture2D CreateHierarchyConnectionIconTexture(Color lineColor, Color arrowColor)
        {
            Texture2D texture = CreateBlankIconTexture("MirrorToolConnectionIcon");

            DrawVerticalLine(texture, 8, 3, 12, lineColor);
            DrawHorizontalLine(texture, 3, 7, 8, arrowColor);
            DrawHorizontalLine(texture, 9, 13, 8, arrowColor);
            DrawChevron(texture, 3, 8, false, arrowColor);
            DrawChevron(texture, 13, 8, true, arrowColor);
            DrawPixel(texture, 5, 5, arrowColor);
            DrawPixel(texture, 6, 5, arrowColor);
            DrawPixel(texture, 10, 5, arrowColor);
            DrawPixel(texture, 11, 5, arrowColor);
            DrawPixel(texture, 5, 11, arrowColor);
            DrawPixel(texture, 6, 11, arrowColor);
            DrawPixel(texture, 10, 11, arrowColor);
            DrawPixel(texture, 11, 11, arrowColor);

            texture.Apply();
            return texture;
        }

        private static void DrawPixel(Texture2D texture, int x, int y, Color color)
        {
            texture.SetPixel(x, y, color);
        }
    }
}
