using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Voxelforge.Gameplay
{
    /// <summary>Small helpers that build the 4.4 HUD's uGUI pieces in code. Nothing here catches clicks.</summary>
    static class HudWidgets
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5; // 5 = UI
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static Image Image(string name, Transform parent, Sprite sprite, Color color, Image.Type type = UnityEngine.UI.Image.Type.Simple)
        {
            Image image = Rect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.type = sprite != null ? type : UnityEngine.UI.Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }

        public static TextMeshProUGUI Label(string name, Transform parent, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions alignment)
        {
            TextMeshProUGUI label = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
                label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>A small <see cref="HudSkin.comicFont"/> label with the crisp material, for numbers and keys.</summary>
        public static TextMeshProUGUI SmallLabel(string name, Transform parent, HudSkin skin, float size, Color color,
            TextAlignmentOptions alignment)
        {
            TextMeshProUGUI label = Label(name, parent, skin.comicFont, size, color, alignment);
            if (skin.comicSmall != null)
                label.fontSharedMaterial = skin.comicSmall;
            return label;
        }

        /// <summary>Fills the parent, less <paramref name="inset"/> on every side.</summary>
        public static RectTransform Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }

        /// <summary>Pins the rect's <paramref name="pivot"/> to the parent's <paramref name="anchor"/> point, plus <paramref name="offset"/>.</summary>
        public static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>Sets a colour only when it changed, so the canvas isn't rebuilt for nothing.</summary>
        public static void SetColor(Graphic graphic, Color color)
        {
            if (graphic.color != color)
                graphic.color = color;
        }

        public static void SetActive(Component component, bool active)
        {
            if (component.gameObject.activeSelf != active)
                component.gameObject.SetActive(active);
        }
    }
}
