using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// "Hunter Association system window" UI kit, after the concept deck: navy panels, thin gold frames, a short
    /// gold bar next to a long rule under every title, cream text, and violet gate accents. Used by the run HUD and
    /// by the lobby dialogs, so both read as the same in-world system.
    /// </summary>
    public static class HunterUi
    {
        public static readonly Color Navy = new Color(.118f, .165f, .333f, .94f);
        public static readonly Color NavyDeep = new Color(.07f, .1f, .21f, .96f);
        public static readonly Color Gold = new Color(.86f, .76f, .49f);
        public static readonly Color GoldDim = new Color(.86f, .76f, .49f, .45f);
        public static readonly Color Cream = new Color(.97f, .95f, .91f);
        public static readonly Color Muted = new Color(.74f, .76f, .84f);
        public static readonly Color Gate = new Color(.6f, .5f, 1f);
        public static readonly Color Danger = new Color(1f, .33f, .3f);

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
            return r;
        }

        public static Image Fill(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
        {
            var r = Rect(name, parent, anchor, pivot, pos, size);
            var image = r.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A system window: navy body, gold hairline frame, gold corner ticks, and an optional title.</summary>
        public static RectTransform Window(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
            TMP_FontAsset font, string title = null, bool deep = false)
        {
            var body = Fill(name, parent, anchor, pivot, pos, size, deep ? NavyDeep : Navy).rectTransform;
            Frame(body, GoldDim, 1.2f);
            // Corner ticks, like the association's document frames.
            foreach (var (ax, ay) in new[] { (0f, 0f), (1f, 0f), (0f, 1f), (1f, 1f) })
            {
                Fill("Tick", body, new Vector2(ax, ay), new Vector2(ax, ay), Vector2.zero, new Vector2(16, 2.4f), Gold);
                Fill("Tick", body, new Vector2(ax, ay), new Vector2(ax, ay), Vector2.zero, new Vector2(2.4f, 16), Gold);
            }
            if (!string.IsNullOrEmpty(title)) Title(body, font, title, new Vector2(18, -12), size.x - 36);
            return body;
        }

        /// <summary>Title with the deck's underline: a short thick gold bar, then a long thin rule.</summary>
        public static TextMeshProUGUI Title(RectTransform parent, TMP_FontAsset font, string title, Vector2 pos, float width, int size = 20)
        {
            var t = Text("Title", parent, font, title, size, Cream, pos, new Vector2(width, size + 10), FontStyles.Bold);
            Fill("TitleBar", parent, new Vector2(0, 1), new Vector2(0, 1), pos + new Vector2(0, -(size + 12)), new Vector2(46, 3.5f), Gold);
            Fill("TitleRule", parent, new Vector2(0, 1), new Vector2(0, 1), pos + new Vector2(52, -(size + 13)), new Vector2(Mathf.Max(10, width - 52), 1.2f), GoldDim);
            return t;
        }

        public static void Frame(RectTransform target, Color color, float thickness)
        {
            Fill("FrameTop", target, new Vector2(.5f, 1), new Vector2(.5f, 1), Vector2.zero, new Vector2(0, thickness), color).rectTransform.Stretch(true);
            Fill("FrameBottom", target, new Vector2(.5f, 0), new Vector2(.5f, 0), Vector2.zero, new Vector2(0, thickness), color).rectTransform.Stretch(true);
            Fill("FrameLeft", target, new Vector2(0, .5f), new Vector2(0, .5f), Vector2.zero, new Vector2(thickness, 0), color).rectTransform.Stretch(false);
            Fill("FrameRight", target, new Vector2(1, .5f), new Vector2(1, .5f), Vector2.zero, new Vector2(thickness, 0), color).rectTransform.Stretch(false);
        }

        static void Stretch(this RectTransform r, bool horizontal)
        {
            if (horizontal) { r.anchorMin = new Vector2(0, r.anchorMin.y); r.anchorMax = new Vector2(1, r.anchorMax.y); r.sizeDelta = new Vector2(0, r.sizeDelta.y); }
            else { r.anchorMin = new Vector2(r.anchorMin.x, 0); r.anchorMax = new Vector2(r.anchorMax.x, 1); r.sizeDelta = new Vector2(r.sizeDelta.x, 0); }
        }

        public static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, string value, int size, Color color,
            Vector2 pos, Vector2 dimensions, FontStyles style = FontStyles.Normal, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var r = Rect(name, parent, new Vector2(0, 1), new Vector2(0, 1), pos, dimensions);
            var text = r.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = size; text.text = value; text.color = color; text.fontStyle = style;
            text.alignment = alignment;
            text.raycastTarget = false; text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.characterSpacing = 1.5f;
            return text;
        }

        /// <summary>Gold-framed navy button that lights up gold on hover.</summary>
        public static RectTransform Button(string name, Transform parent, TMP_FontAsset font, string label, Vector2 pos, Vector2 size, Action action, int fontSize = 19)
        {
            var image = Fill(name, parent, new Vector2(0, 1), new Vector2(0, 1), pos, size, new Color(.16f, .22f, .42f, 1));
            image.raycastTarget = true;
            Frame(image.rectTransform, GoldDim, 1.2f);
            var button = image.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.2f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(.8f, .75f, .6f);
            colors.colorMultiplier = 1.4f;
            button.colors = colors;
            button.onClick.AddListener(() => action());
            if (!string.IsNullOrEmpty(label))
                Text("Label", image.rectTransform, font, label, fontSize, Cream, Vector2.zero, size, FontStyles.Bold, TextAlignmentOptions.Center);
            return image.rectTransform;
        }

        /// <summary>Keyboard key chip, e.g. [E] next to an interaction label.</summary>
        public static RectTransform KeyChip(Transform parent, TMP_FontAsset font, string key, string label, Vector2 anchor, Vector2 pos)
        {
            var root = Rect("Prompt", parent, anchor, new Vector2(.5f, .5f), pos, new Vector2(260, 40));
            var chip = Fill("Key", root, new Vector2(0, .5f), new Vector2(0, .5f), Vector2.zero, new Vector2(34, 34), Gold).rectTransform;
            Text("KeyLabel", chip, font, key, 18, NavyDeep, Vector2.zero, new Vector2(34, 34), FontStyles.Bold, TextAlignmentOptions.Center);
            var plate = Fill("Plate", root, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(40, 0), new Vector2(200, 34), Navy).rectTransform;
            Frame(plate, GoldDim, 1);
            Text("Label", plate, font, label, 17, Cream, new Vector2(12, 0), new Vector2(180, 34), FontStyles.Bold, TextAlignmentOptions.Left);
            return root;
        }

        public static TMP_FontAsset CreateFont(Font sourceFont, out Font ownedSource)
        {
            ownedSource = null;
            if (!sourceFont)
            {
                ownedSource = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 30);
                sourceFont = ownedSource;
            }
            if (!sourceFont) return TMP_Settings.defaultFontAsset;
            return TMP_FontAsset.CreateFontAsset(sourceFont, 40, 5, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
        }
    }
}
