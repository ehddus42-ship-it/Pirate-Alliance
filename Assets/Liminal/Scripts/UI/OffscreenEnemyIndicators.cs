using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// Small red warning triangles with "!" on the screen edge, one per living room enemy the camera cannot see.
    /// Each sits where the line from the screen centre to the enemy leaves the screen, and a small arrow tip next
    /// to it points the rest of the way. Owned and ticked by <see cref="LiminalHud"/>.
    /// </summary>
    public sealed class OffscreenEnemyIndicators
    {
        // Distance kept from the screen edge, in canvas units. The top keeps clear of the status and gate windows.
        const float Edge = 30, TopEdge = 84;
        static readonly Color FillColor = new Color(.93f, .16f, .15f), RimColor = new Color(.24f, .02f, .03f);

        sealed class Marker { public RectTransform root, pointer; }

        readonly RectTransform root;
        readonly TMP_FontAsset font;
        readonly Texture2D texture;
        readonly Sprite triangle;
        readonly List<Marker> markers = new List<Marker>();
        readonly List<TrainingEnemy> enemies = new List<TrainingEnemy>();
        Camera view;

        public OffscreenEnemyIndicators(Transform parent, TMP_FontAsset font)
        {
            this.font = font;
            root = HunterUi.Rect("OffscreenEnemies", parent, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            texture = TriangleTexture();
            triangle = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100);
            triangle.hideFlags = HideFlags.DontSave;
        }

        public void Tick(LiminalRunDirector run, bool visible)
        {
            int used = 0;
            if (visible && run && FindView())
            {
                run.GetLivingEnemies(enemies);
                Rect area = root.rect;
                float right = area.xMax - Edge, left = area.xMin + Edge, top = area.yMax - TopEdge, bottom = area.yMin + Edge;
                float pulse = 1 + .08f * Mathf.Sin(Time.unscaledTime * 7);
                foreach (var enemy in enemies)
                {
                    if (!enemy.gameObject.activeInHierarchy) continue;
                    Vector3 screen = view.WorldToScreenPoint(enemy.AimPoint);
                    bool behind = screen.z < 0;
                    if (!behind && view.pixelRect.Contains(screen)) continue;
                    // Behind the camera the projection flips; mirror it so the arrow still points the right way.
                    if (behind) screen = new Vector3(view.pixelRect.xMax - (screen.x - view.pixelRect.xMin), view.pixelRect.yMax - (screen.y - view.pixelRect.yMin), 0);
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out Vector2 local)) continue;
                    Vector2 from = area.center, delta = local - from;
                    if (delta.sqrMagnitude < 1) continue;
                    // Slide along the ray from the centre until it meets the inset screen border.
                    float reachX = delta.x > 0 ? (right - from.x) / delta.x : delta.x < 0 ? (left - from.x) / delta.x : float.MaxValue;
                    float reachY = delta.y > 0 ? (top - from.y) / delta.y : delta.y < 0 ? (bottom - from.y) / delta.y : float.MaxValue;
                    float reach = Mathf.Clamp01(Mathf.Min(reachX, reachY));
                    Vector2 direction = delta.normalized;
                    var marker = used < markers.Count ? markers[used] : Create();
                    used++;
                    marker.root.gameObject.SetActive(true);
                    marker.root.anchoredPosition = from + delta * reach;
                    marker.root.localScale = Vector3.one * pulse;
                    marker.pointer.anchoredPosition = direction * 19;
                    marker.pointer.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90);
                }
            }
            for (int i = used; i < markers.Count; i++)
                if (markers[i].root.gameObject.activeSelf) markers[i].root.gameObject.SetActive(false);
        }

        public void Release()
        {
            if (triangle) Object.Destroy(triangle);
            if (texture) Object.Destroy(texture);
        }

        bool FindView()
        {
            if (view && view.isActiveAndEnabled) return true;
            var rig = Object.FindFirstObjectByType<IsometricFollowCamera>();
            view = rig ? rig.GetComponent<Camera>() : null;
            if (!view) view = Camera.main;
            return view;
        }

        Marker Create()
        {
            var marker = new Marker();
            marker.root = HunterUi.Rect("EnemyMarker", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(26, 23));
            // Anchored at the canvas centre, so anchoredPosition is relative to the same centre as root.rect.
            marker.pointer = Triangle("Pointer", marker.root, new Vector2(12, 9));
            Triangle("Warning", marker.root, new Vector2(26, 23));
            HunterUi.Text("Mark", marker.root, font, "!", 15, Color.white, new Vector2(0, -.5f), new Vector2(26, 22), FontStyles.Bold, TextAlignmentOptions.Center)
                .characterSpacing = 0;
            markers.Add(marker);
            return marker;
        }

        RectTransform Triangle(string name, RectTransform parent, Vector2 size)
        {
            var image = HunterUi.Fill(name, parent, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, size, Color.white);
            image.sprite = triangle;
            return image.rectTransform;
        }

        /// <summary>An upward triangle, red with a dark rim, anti-aliased over one pixel.</summary>
        static Texture2D TriangleTexture()
        {
            const int size = 64;
            const float rim = 4.5f;
            var corners = new[] { new Vector2(size * .5f, size - 4), new Vector2(3, 6), new Vector2(size - 3, 6) };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + .5f, y + .5f);
                    // Counter-clockwise corners: inside is left of every edge, so the smallest cross product is the depth.
                    float depth = float.MaxValue;
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 a = corners[i], edge = corners[(i + 1) % 3] - a, offset = p - a;
                        depth = Mathf.Min(depth, (edge.x * offset.y - edge.y * offset.x) / edge.magnitude);
                    }
                    Color color = Color.Lerp(RimColor, FillColor, Mathf.Clamp01(depth - rim + .5f));
                    color.a = Mathf.Clamp01(depth + .5f);
                    pixels[y * size + x] = color;
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Offscreen Enemy Triangle", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
