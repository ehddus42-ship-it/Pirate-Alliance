using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>Construction helpers shared by the office monsters: bodies, materials and procedural textures.</summary>
    public static class LiminalMonsterKit
    {
        static Texture2D paper, glyphs;
        static Material paperMaterial, glyphMaterial, glyphGlowMaterial;

        /// <summary>
        /// Builds root (CharacterController, TrainingEnemy, monster) → Visual (hit pivot) → Pose (procedural
        /// animation) → the concept-map prop. The prop's colliders are removed; the CharacterController is the body.
        /// </summary>
        public static T Build<T>(string name, GameObject source, Vector3 position, Quaternion rotation, Transform parent,
            float radius, float height, Vector3 fallbackSize, out Transform model, float modelYaw = 0, float modelScale = 1, Vector3 modelOffset = default)
            where T : LiminalPropMonster
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(position, rotation);
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            var pose = new GameObject("Pose").transform;
            pose.SetParent(visual, false);
            GameObject instance;
            if (source)
            {
                instance = Object.Instantiate(source, pose);
                instance.transform.localPosition = modelOffset;
                instance.transform.localRotation = Quaternion.Euler(0, modelYaw, 0);
                instance.transform.localScale = instance.transform.localScale * modelScale;
                foreach (var c in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            }
            else
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(instance.GetComponent<Collider>());
                instance.transform.SetParent(pose, false);
                instance.transform.localScale = fallbackSize;
                instance.transform.localPosition = Vector3.up * fallbackSize.y * .5f;
            }
            instance.name = "Model";
            model = instance.transform;
            var body = root.AddComponent<CharacterController>();
            body.radius = radius;
            body.height = height;
            body.center = Vector3.up * (height * .5f);
            body.stepOffset = .2f;
            body.skinWidth = .04f;
            var anchor = new GameObject("AimAnchor").transform;
            anchor.SetParent(root.transform, false);
            anchor.localPosition = Vector3.up * (height * .6f);
            var health = root.AddComponent<TrainingEnemy>();
            health.aimAnchor = anchor;
            health.respawnOnDeath = false;
            return root.AddComponent<T>();
        }

        /// <summary>Local bounds of every renderer under `model`, in `space` coordinates.</summary>
        public static Bounds LocalBounds(Transform model, Transform space)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = space.InverseTransformPoint(corner);
                    if (!any) { bounds = new Bounds(local, Vector3.zero); any = true; }
                    else bounds.Encapsulate(local);
                }
            }
            return any ? bounds : new Bounds(Vector3.up * .5f, Vector3.one);
        }

        public static LineRenderer Telegraph(Transform parent, List<Object> owned)
        {
            var go = new GameObject("AttackTelegraph") { layer = 2 };
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.numCornerVertices = 3;
            line.numCapVertices = 3;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
            material.color = new Color(1, .45f, .15f);
            owned.Add(material);
            line.sharedMaterial = material;
            line.enabled = false;
            return line;
        }

        public static Material Lit(Color color, float smoothness = .5f, float metallic = 0)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            return m;
        }

        public static Material Emissive(Color color)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            return m;
        }

        static Material ParticleMaterial(Texture texture, bool additive)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(shader);
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_Cull", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (texture) { m.mainTexture = texture; if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture); }
            m.SetColor("_BaseColor", Color.white);
            m.renderQueue = 3050;
            return m;
        }

        // ---- paper -----------------------------------------------------------------------------------------
        /// <summary>A photocopied sheet: off-white, grey toner text lines, a smudged copy border. Double sided.</summary>
        public static Material PaperMaterial
        {
            get
            {
                if (paperMaterial) return paperMaterial;
                if (!paper)
                {
                    const int w = 48, h = 64;
                    paper = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Photocopy Paper", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    var random = new System.Random(7);
                    var pixels = new Color32[w * h];
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            float edge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                            float shade = .93f + (float)random.NextDouble() * .04f - (edge < 2 ? .12f : 0);
                            // Text lines: rows of grey dashes with ragged ends, a heading block near the top.
                            bool text = x > 5 && x < w - 6 && y > 6 && y < h - 8 && (y % 5 == 0 || y % 5 == 1)
                                && random.NextDouble() > .2 && x < w - 6 - (y * 7 % 13);
                            bool heading = y > h - 14 && y < h - 9 && x > 5 && x < w / 2;
                            if (text) shade -= .42f;
                            if (heading) shade -= .55f;
                            byte v = (byte)(Mathf.Clamp01(shade) * 255);
                            pixels[y * w + x] = new Color32(v, v, (byte)Mathf.Min(255, v + 6), 255);
                        }
                    paper.SetPixels32(pixels);
                    paper.Apply();
                }
                paperMaterial = Lit(Color.white, .15f);
                paperMaterial.mainTexture = paper;
                if (paperMaterial.HasProperty("_BaseMap")) paperMaterial.SetTexture("_BaseMap", paper);
                if (paperMaterial.HasProperty("_Cull")) paperMaterial.SetFloat("_Cull", 0);
                return paperMaterial;
            }
        }

        // ---- binary glyphs ---------------------------------------------------------------------------------
        static readonly string[] Zero = { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." };
        static readonly string[] One = { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." };
        public const int GlyphCellWidth = 7, GlyphCellHeight = 9;

        /// <summary>Pixel-font atlas with "0" (left cell) and "1" (right cell), point filtered for a digital look.</summary>
        public static Texture2D Glyphs
        {
            get
            {
                if (glyphs) return glyphs;
                glyphs = new Texture2D(GlyphCellWidth * 2, GlyphCellHeight, TextureFormat.RGBA32, false)
                { name = "Binary Glyphs", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[glyphs.width * glyphs.height];
                for (int g = 0; g < 2; g++)
                {
                    var rows = g == 0 ? Zero : One;
                    for (int r = 0; r < rows.Length; r++)
                        for (int c = 0; c < rows[r].Length; c++)
                            if (rows[r][c] == '#')
                            {
                                int x = g * GlyphCellWidth + 1 + c, y = GlyphCellHeight - 2 - r;
                                pixels[y * glyphs.width + x] = new Color32(255, 255, 255, 255);
                            }
                }
                glyphs.SetPixels32(pixels);
                glyphs.Apply();
                return glyphs;
            }
        }

        public static Material GlyphMaterial => glyphMaterial ? glyphMaterial : glyphMaterial = ParticleMaterial(Glyphs, true);

        /// <summary>Soft additive glow (no texture) for the scanline bar under the digits.</summary>
        public static Material GlyphGlowMaterial => glyphGlowMaterial ? glyphGlowMaterial : glyphGlowMaterial = ParticleMaterial(null, true);
    }
}
