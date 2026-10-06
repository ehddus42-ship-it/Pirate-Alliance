using UnityEngine;

namespace AcRoguelike.Liminal
{
    public enum RedFieldPhase { Hidden, Telegraph, Judgement }

    /// <summary>Thin safe-ground outlines before judgement; the red field appears only while it deals damage.</summary>
    public sealed class TrafficLightRedField : MonoBehaviour
    {
        const int DiscSegments = 56;
        public Material floorMaterial, telegraphMaterial, safeMaterial;
        public float lift = .07f;
        public RedFieldPhase Phase { get; private set; }
        public Vector3[] Centers { get; private set; } = new Vector3[0];
        public float Radius { get; private set; }
        Renderer floor;
        Renderer[] discs = new Renderer[0];
        Telegraph[] safeOutlines = new Telegraph[0];
        Transform[] discTransforms = new Transform[0];
        MaterialPropertyBlock block;
        Material floorOverlay, telegraphOverlay, safeOverlay;
        Mesh quad, disc;
        float phaseTime, growDuration = .7f;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public void Show(Vector3 center, Quaternion rotation, Vector2 size, Vector3[] safeCenters, float radius, float growTime)
        {
            Clear();
            Centers = safeCenters; Radius = radius; growDuration = Mathf.Max(.1f, growTime);
            block = block ?? new MaterialPropertyBlock();
            EnsureOverlayMaterials();
            quad = quad ? quad : MakeQuad(); disc = disc ? disc : MakeDisc();
            floor = MakeRenderer("RedFloor", quad, floorOverlay, center + Vector3.up * lift, rotation, new Vector3(size.x, 1, size.y), 0);
            discs = new Renderer[safeCenters.Length]; discTransforms = new Transform[safeCenters.Length];
            safeOutlines = new Telegraph[safeCenters.Length];
            for (int i = 0; i < safeCenters.Length; i++)
            {
                discs[i] = MakeRenderer("SafeZone" + i, disc, telegraphOverlay, safeCenters[i] + Vector3.up * (lift + .01f), Quaternion.identity, Vector3.zero, 1);
                discTransforms[i] = discs[i].transform;
                safeOutlines[i] = Telegraph.Create(transform, "SafeZoneOutline" + i);
            }
            Phase = RedFieldPhase.Telegraph; phaseTime = 0; Apply();
        }

        public void Judge()
        {
            if (Phase == RedFieldPhase.Hidden) return;
            Phase = RedFieldPhase.Judgement; phaseTime = 0;
            foreach (var d in discs) if (d) d.sharedMaterial = safeOverlay;
            Apply();
        }

        public void Hide() { Clear(); Phase = RedFieldPhase.Hidden; }

        void EnsureOverlayMaterials()
        {
            if (!floorOverlay) floorOverlay = MakeOverlay(floorMaterial, "Red Field Overlay");
            if (!telegraphOverlay) telegraphOverlay = MakeOverlay(telegraphMaterial, "Safe Zone Warning Overlay");
            if (!safeOverlay) safeOverlay = MakeOverlay(safeMaterial, "Safe Zone Active Overlay");
        }

        static Material MakeOverlay(Material source, string name)
        {
            // Keep the authored ring texture, while sharing the depth-independent warning shader.
            // These are owned runtime copies; the prefab's materials remain reusable by the builder.
            var material = Telegraph.CreateOverlayMaterial(name);
            if (!source) return material;
            if (source.HasProperty("_BaseColor")) material.SetColor(BaseColor, source.GetColor(BaseColor));
            if (source.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                material.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
                material.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
            }
            return material;
        }

        /// <summary>Judged from the character's feet on the floor plane.</summary>
        public bool IsSafe(Vector3 feet)
        {
            foreach (var c in Centers)
            {
                float dx = feet.x - c.x, dz = feet.z - c.z;
                if (dx * dx + dz * dz <= Radius * Radius) return true;
            }
            return false;
        }

        void Update() { if (Phase != RedFieldPhase.Hidden) { phaseTime += Time.deltaTime; Apply(); } }

        void Apply()
        {
            float pulse = .5f + .5f * Mathf.Sin(Time.time * (Phase == RedFieldPhase.Judgement ? 26 : 9));
            float grow = Mathf.SmoothStep(0, 1, phaseTime / growDuration);
            if (floor)
            {
                floor.enabled = Phase == RedFieldPhase.Judgement;
                float alpha = .52f + pulse * .16f;
                block.SetColor(BaseColor, new Color(1, .06f, .04f, alpha));
                floor.SetPropertyBlock(block);
            }
            for (int i = 0; i < discs.Length; i++)
            {
                if (!discs[i]) continue;
                bool judged = Phase == RedFieldPhase.Judgement;
                discs[i].enabled = judged;
                if (safeOutlines[i])
                {
                    if (judged) safeOutlines[i].Hide();
                    else safeOutlines[i].Circle(Centers[i], Radius * grow, Mathf.Clamp01(phaseTime / growDuration));
                }
                float scale = judged ? Radius * (1 + .03f * pulse) : Radius * grow;
                discTransforms[i].localScale = new Vector3(scale, 1, scale);
                block.SetColor(BaseColor, new Color(.35f, 1, .42f, .92f));
                discs[i].SetPropertyBlock(block);
            }
        }

        // sortingOrder keeps the safe circles above the red floor whatever their camera distance.
        Renderer MakeRenderer(string objectName, Mesh mesh, Material material, Vector3 position, Quaternion rotation, Vector3 scale, int order)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.sortingOrder = order;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            return renderer;
        }

        void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            floor = null; discs = new Renderer[0]; safeOutlines = new Telegraph[0]; discTransforms = new Transform[0]; Centers = new Vector3[0];
        }

        void OnDestroy()
        {
            if (floorOverlay) Destroy(floorOverlay);
            if (telegraphOverlay) Destroy(telegraphOverlay);
            if (safeOverlay) Destroy(safeOverlay);
            if (quad) Destroy(quad);
            if (disc) Destroy(disc);
        }

        static Mesh MakeQuad()
        {
            var m = new Mesh { name = "FieldQuad" };
            m.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f), new Vector3(.5f, 0, -.5f) };
            m.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 }; m.RecalculateNormals(); return m;
        }

        // Unit-radius disc; the UV ring texture is sampled from its centre so the glow edge stays circular at any size.
        static Mesh MakeDisc()
        {
            var m = new Mesh { name = "FieldDisc" };
            var v = new Vector3[DiscSegments + 1]; var uv = new Vector2[DiscSegments + 1]; var colors = new Color[DiscSegments + 1]; var t = new int[DiscSegments * 3];
            uv[0] = new Vector2(.5f, .5f);
            colors[0] = Color.white;
            for (int i = 0; i < DiscSegments; i++)
            {
                float a = i * Mathf.PI * 2 / DiscSegments;
                v[i + 1] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); uv[i + 1] = new Vector2(.5f + .5f * Mathf.Cos(a), .5f + .5f * Mathf.Sin(a));
                colors[i + 1] = Color.white;
                t[i * 3] = 0; t[i * 3 + 1] = 1 + (i + 1) % DiscSegments; t[i * 3 + 2] = 1 + i;
            }
            m.vertices = v; m.uv = uv; m.colors = colors; m.triangles = t; m.RecalculateNormals(); return m;
        }
    }
}
