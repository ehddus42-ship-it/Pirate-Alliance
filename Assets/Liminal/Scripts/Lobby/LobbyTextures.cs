using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>Small procedural textures for the hunter lobby (no texture assets needed).</summary>
    public static class LobbyTextures
    {
        static Texture2D paving, stripes, banner, windows;

        static Texture2D New(string name, int w, int h, FilterMode filter = FilterMode.Bilinear)
            => new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, filterMode = filter, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };

        /// <summary>Light granite paving slabs with dark seams.</summary>
        public static Texture2D Paving()
        {
            if (paving) return paving;
            const int n = 128;
            paving = New("Plaza Paving", n, n);
            var random = new System.Random(11);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool seam = x % 64 < 2 || (y + (x / 64 % 2) * 32) % 64 < 2;
                    float tone = .82f + (float)random.NextDouble() * .06f + ((x / 64 + y / 64) % 2) * .03f;
                    if (seam) tone = .55f;
                    byte v = (byte)(tone * 255);
                    px[y * n + x] = new Color32(v, v, (byte)Mathf.Min(255, v + 4), 255);
                }
            paving.SetPixels32(px); paving.Apply();
            return paving;
        }

        /// <summary>Yellow and black hazard stripes for barricades.</summary>
        public static Texture2D Stripes()
        {
            if (stripes) return stripes;
            const int n = 64;
            stripes = New("Hazard Stripes", n, n);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = ((x + y) / 16) % 2 == 0 ? new Color32(245, 196, 30, 255) : new Color32(28, 28, 30, 255);
            stripes.SetPixels32(px); stripes.Apply();
            return stripes;
        }

        /// <summary>Navy association banner: gold border and the wing emblem.</summary>
        public static Texture2D Banner()
        {
            if (banner) return banner;
            const int w = 96, h = 192;
            banner = New("Association Banner", w, h);
            banner.wrapMode = TextureWrapMode.Clamp;
            var navy = new Color(.12f, .17f, .34f); var gold = new Color(.86f, .76f, .49f); var white = new Color(.95f, .95f, .97f);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color c = navy;
                    bool border = x < 4 || x >= w - 4 || y < 4 || y >= h - 4;
                    bool inner = (x == 7 || x == w - 8) && y > 7 && y < h - 8 || (y == 7 || y == h - 8) && x > 7 && x < w - 8;
                    if (border) c = gold; else if (inner) c = gold * .8f;
                    // Wing emblem: two swept wings of three feathers, mirrored around the centre line.
                    float u = (x - w * .5f) / (w * .5f), v = (y - h * .62f) / (w * .5f);
                    float ax = Mathf.Abs(u);
                    for (int f = 0; f < 3; f++)
                    {
                        float off = f * .18f;
                        float centre = .15f + ax * .55f - off;
                        if (ax > .08f && ax < .8f - f * .12f && Mathf.Abs(v - centre) < .055f) c = white;
                    }
                    if (ax < .07f && v > -.25f && v < .35f) c = gold;
                    // Gold rule under the emblem, like the deck's title underline.
                    if (y > h * .3f && y < h * .3f + 3 && x > 18 && x < w - 18) c = gold;
                    px[y * w + x] = c;
                }
            banner.SetPixels(px); banner.Apply();
            return banner;
        }

        static Texture2D emblem;

        /// <summary>Association floor emblem: navy disc, double gold ring, white wings, transparent outside.</summary>
        public static Texture2D Emblem()
        {
            if (emblem) return emblem;
            const int n = 256;
            emblem = New("Association Emblem", n, n);
            emblem.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[n * n];
            var navy = new Color(.12f, .17f, .34f, .92f); var gold = new Color(.86f, .76f, .49f, 1); var white = new Color(.95f, .95f, .97f, 1);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + .5f) / n * 2 - 1, v = (y + .5f) / n * 2 - 1, r = Mathf.Sqrt(u * u + v * v);
                    Color c = new Color(0, 0, 0, 0);
                    if (r < 1) c = navy;
                    if (r > .9f && r < .97f || r > .8f && r < .83f) c = gold;
                    float ax = Mathf.Abs(u);
                    for (int f = 0; f < 3; f++)
                    {
                        float centre = .1f + ax * .45f - f * .14f;
                        if (r < .78f && ax > .07f && ax < .66f - f * .1f && Mathf.Abs(v - centre) < .045f) c = white;
                    }
                    if (ax < .055f && v > -.35f && v < .3f) c = gold;
                    // Soft edge so the disc does not alias on the floor.
                    if (r > .97f) c.a *= Mathf.Clamp01((1 - r) / .03f);
                    px[y * n + x] = c;
                }
            emblem.SetPixels(px); emblem.Apply();
            return emblem;
        }

        /// <summary>Office tower facade: a grid of windows, some lit warm, some cool, most dark.</summary>
        public static Texture2D Windows()
        {
            if (windows) return windows;
            const int w = 64, h = 128;
            windows = New("Tower Windows", w, h);
            var random = new System.Random(5);
            var px = new Color[w * h];
            var frame = new Color(.33f, .37f, .45f);
            for (int cy = 0; cy < h / 8; cy++)
                for (int cx = 0; cx < w / 8; cx++)
                {
                    double roll = random.NextDouble();
                    Color glass = roll < .18 ? new Color(1f, .86f, .6f) : roll < .3 ? new Color(.7f, .85f, 1f) : new Color(.16f, .22f, .32f);
                    for (int y = 0; y < 8; y++)
                        for (int x = 0; x < 8; x++)
                            px[(cy * 8 + y) * w + cx * 8 + x] = x < 1 || y < 2 ? frame : glass;
                }
            windows.SetPixels(px); windows.Apply();
            return windows;
        }
    }
}
