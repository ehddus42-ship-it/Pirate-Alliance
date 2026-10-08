using System;
using System.Collections.Generic;
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
        static readonly HashSet<TMP_FontAsset> ownedFonts = new HashSet<TMP_FontAsset>();
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
            float baseline = t.rectTransform.sizeDelta.y + 6;
            Fill("TitleBar", parent, new Vector2(0, 1), new Vector2(0, 1), pos + new Vector2(0, -baseline), new Vector2(46, 3.5f), Gold);
            Fill("TitleRule", parent, new Vector2(0, 1), new Vector2(0, 1), pos + new Vector2(52, -(baseline + 1)), new Vector2(Mathf.Max(10, width - 52), 1.2f), GoldDim);
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
            // Noto Sans KR needs about 1.45 em per line. A Latin-sized box can make TMP ellipsize
            // the entire label, including short titles and the currency counter.
            float lineHeight = font ? size * font.faceInfo.lineHeight / Mathf.Max(1, font.faceInfo.pointSize) : size * 1.5f;
            dimensions.y = Mathf.Max(dimensions.y, Mathf.Ceil(lineHeight + 2));
            var r = Rect(name, parent, new Vector2(0, 1), new Vector2(0, 1), pos, dimensions);
            var text = r.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            if (font) text.fontSharedMaterial = font.material;
            text.fontSize = size; text.text = value; text.color = color; text.fontStyle = style;
            text.enableAutoSizing = false;
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
            // This reference keeps the existing bundled Noto font in player builds without duplicating it
            // or depending on whatever fonts happen to be installed on the player's computer.
            var settings = Resources.Load<HunterUiFontSource>(HunterUiFontSource.ResourcePath);
            if ((!sourceFont || !sourceFont.HasCharacter('가')) && settings) sourceFont = settings.sourceFont;
            if (!sourceFont)
            {
                Debug.LogError("Hunter UI requires the bundled Korean font resource.");
                return TMP_Settings.defaultFontAsset;
            }

            var primary = NewFont(sourceFont, 2048, "Hunter UI - Static");
            if (!primary) return TMP_Settings.defaultFontAsset;
            var glyphs = settings && settings.commonCharacters ? settings.commonCharacters.text : "";
            primary.TryAddCharacters(" ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.,:;!?+-/%()[]·…???" + glyphs);
            // Freeze common UI glyphs before creating any labels. Opening an upgrade/mission dialog
            // then cannot change the atlas used by text that is already on screen.
            primary.atlasPopulationMode = AtlasPopulationMode.Static;
            var fallback = NewFont(settings && settings.sourceFont ? settings.sourceFont : sourceFont, 1024, "Hunter UI - Dynamic Fallback");
            if (fallback) primary.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
            return primary;
        }

        static TMP_FontAsset NewFont(Font source, int atlasSize, string name)
        {
            var font = TMP_FontAsset.CreateFontAsset(source, 40, 5, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                atlasSize, atlasSize, AtlasPopulationMode.Dynamic, true);
            if (!font) return null;
            font.name = name;
            font.hideFlags = HideFlags.DontSave;
            // Runtime-only assets are never serialized into a player build.
            ownedFonts.Add(font);
            return font;
        }

        public static void ReleaseFont(TMP_FontAsset font, Font ownedSource)
        {
            if (font && ownedFonts.Remove(font))
            {
                if (font.fallbackFontAssetTable != null)
                    foreach (var fallback in font.fallbackFontAssetTable) ReleaseFont(fallback, null);
                // TMP owns its atlas textures and material and disposes them in OnDestroy.
                UnityEngine.Object.Destroy(font);
            }
            if (ownedSource) UnityEngine.Object.Destroy(ownedSource);
        }
    }
}
