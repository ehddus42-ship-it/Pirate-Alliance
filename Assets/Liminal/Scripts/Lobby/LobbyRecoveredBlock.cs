using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AcRoguelike.Liminal
{
    /// <summary>Occupied low-rise street fronts beyond the playable plaza. Repaired masonry, shutters and utilities tell the story without signs.</summary>
    public sealed class LobbyRecoveredBlock : MonoBehaviour
    {
        readonly Dictionary<Material, Geometry> batches = new Dictionary<Material, Geometry>();
        List<Object> owned;
        Material brick, plaster, repair, concrete, charcoal, glass, metal, blue, cream, paint, tar;

        public static LobbyRecoveredBlock Build(Transform parent, List<Object> owned)
        {
            var go = new GameObject("Recovered neighbourhood");
            go.transform.SetParent(parent, false);
            var block = go.AddComponent<LobbyRecoveredBlock>(); block.owned = owned;
            block.Construct(); return block;
        }

        void Construct()
        {
            brick = Mat("Old red brick", Color.white, .2f);
            var brickTexture = Brick(); owned.Add(brickTexture); brick.mainTexture = brickTexture;
            brick.SetTexture("_BaseMap", brickTexture);
            plaster = Mat("Aged warm render", new Color(.59f, .58f, .53f), .19f);
            repair = Mat("New repair mortar", new Color(.72f, .73f, .70f), .2f);
            concrete = Mat("Granite kerb", new Color(.53f, .56f, .56f), .23f);
            charcoal = Mat("Roof membrane and frames", new Color(.12f, .16f, .19f), .25f);
            glass = Mat("Smoky blue glass", new Color(.18f, .29f, .34f), .67f);
            metal = Mat("Galvanised replacement steel", new Color(.49f, .55f, .58f), .55f, .4f);
            blue = Mat("Roof waterproofing", new Color(.20f, .37f, .36f), .22f);
            cream = Mat("Warm indoor panels", new Color(.83f, .76f, .58f), .28f);
            paint = Mat("Worn road paint", new Color(.77f, .77f, .68f), .14f);
            tar = Mat("Road resurfacing", new Color(.13f, .145f, .16f), .14f);
            Building(new Vector3(-27.5f, 0, 17), 7, 5.8f, 5.4f, true, 0);
            Building(new Vector3(-16.5f, 0, 27.5f), 8.5f, 8.2f, 6, false, 1);
            Building(new Vector3(17.5f, 0, 27), 8, 6, 5.5f, true, 2);
            Building(new Vector3(28, 0, 16.5f), 7.5f, 8.4f, 6, false, 3);
            RoadMarkings();
            foreach (var pair in batches)
            {
                var g = pair.Value;
                var mesh = new Mesh { name = pair.Key.name, indexFormat = g.vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(g.vertices); mesh.SetNormals(g.normals); mesh.SetUVs(0, g.uv);
                mesh.SetTriangles(g.triangles, 0); mesh.RecalculateBounds(); owned.Add(mesh);
                var go = new GameObject(pair.Key.name); go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = pair.Key;
            }
            batches.Clear();
        }

        void Building(Vector3 c, float width, float height, float depth, bool redBrick, int variant)
        {
            float front = -depth * .5f;
            Box(c + Vector3.up * (height * .5f), new Vector3(width, height, depth), redBrick ? brick : plaster);
            Box(c + new Vector3(0, .18f, 0), new Vector3(width + .25f, .36f, depth + .25f), concrete);
            // A replaced lower corner and stepped patches preserve the old footprint without exposed structural danger.
            Box(c + new Vector3(width * .33f, .90f, front - .035f), new Vector3(width * .23f, 1.42f, .08f), repair);
            Box(c + new Vector3(width * .36f, 1.65f, front - .035f), new Vector3(width * .17f, .26f, .08f), repair);
            Box(c + new Vector3(-width * .38f, 2.75f, front - .035f), new Vector3(.60f, .72f, .08f), repair);
            Box(c + new Vector3(-width * .33f, 3.17f, front - .035f), new Vector3(.32f, .19f, .08f), repair);
            // The camera can see this side repair even when the restored shopfront covers the lower facade patch.
            Box(c + new Vector3(width * .5f + .03f, 1.16f, -.35f), new Vector3(.075f, 1.98f, depth * .38f), repair);
            Box(c + new Vector3(width * .5f + .03f, 2.24f, -.12f), new Vector3(.075f, .27f, depth * .27f), repair);
            for (int i = 0; i < 3; i++)
            {
                float y = .55f + .5f * i;
                Box(c + new Vector3(width * .5f + .078f, y, -.32f), new Vector3(.028f, .05f, .70f), metal);
                foreach (float z in new[] { -.61f, -.03f })
                    Box(c + new Vector3(width * .5f + .10f, y, z), new Vector3(.018f, .042f, .042f), charcoal);
            }
            // Fixed steel splice straps at the repaired corner, with short visible anchor plates.
            for (int i = 0; i < 3; i++)
            {
                float y = .54f + i * .41f;
                Box(c + new Vector3(width * .38f, y, front - .086f), new Vector3(.53f, .045f, .035f), metal);
                foreach (float x in new[] { -.21f, .21f })
                    Box(c + new Vector3(width * .38f + x, y, front - .11f), new Vector3(.035f, .035f, .014f), charcoal);
            }
            // Reopened shop windows and a newly replaced roller shutter. All fascia panels are intentionally blank.
            for (int bay = 0; bay < 3; bay++)
            {
                float x = (bay - 1) * width * .29f;
                float w = width * .23f;
                Box(c + new Vector3(x, 1.46f, front - .065f), new Vector3(w, 2.18f, .12f), charcoal);
                if (bay == variant % 3)
                {
                    Box(c + new Vector3(x, 1.47f, front - .145f), new Vector3(w - .12f, 2.05f, .035f), metal);
                    for (int line = 0; line < 17; line++)
                        Box(c + new Vector3(x, .50f + line * .12f, front - .172f), new Vector3(w - .14f, .018f, .012f), concrete);
                    Box(c + new Vector3(x, .54f, front - .196f), new Vector3(.3f, .035f, .024f), charcoal);
                }
                else
                {
                    Box(c + new Vector3(x, 1.48f, front - .14f), new Vector3(w - .12f, 2.03f, .03f), glass);
                    Box(c + new Vector3(x, 1.48f, front - .17f), new Vector3(.04f, 2.06f, .035f), metal);
                    Box(c + new Vector3(x, 1.66f, front - .17f), new Vector3(w - .12f, .045f, .035f), metal);
                    Box(c + new Vector3(x + w * .11f, 1.30f, front - .19f), new Vector3(.025f, .26f, .04f), cream);
                }
                Box(c + new Vector3(x, 2.81f, front - .18f), new Vector3(w + .10f, .27f, .38f), variant % 2 == 0 ? blue : charcoal);
            }
            int floors = height > 7 ? 2 : 1;
            for (int floor = 0; floor < floors; floor++)
                for (int i = 0; i < 4; i++)
                {
                    float x = (i - 1.5f) * width * .21f, y = 4.05f + floor * 2.5f;
                    Box(c + new Vector3(x, y, front - .065f), new Vector3(1.15f, 1.30f, .13f), concrete);
                    Box(c + new Vector3(x, y, front - .144f), new Vector3(1.03f, 1.16f, .035f), (i + floor + variant) % 5 == 0 ? cream : glass);
                    Box(c + new Vector3(x, y, front - .17f), new Vector3(.045f, 1.17f, .035f), metal);
                    Box(c + new Vector3(x, y - .66f, front - .19f), new Vector3(1.28f, .075f, .28f), repair);
                }
            // Flat roof parapet, waterproofing, a modest rooftop enclosure and outdoor compressor units.
            Box(c + new Vector3(0, height + .025f, 0), new Vector3(width - .3f, .06f, depth - .3f), blue);
            Box(c + new Vector3(0, height + .22f, front), new Vector3(width + .08f, .44f, .18f), repair);
            Box(c + new Vector3(0, height + .22f, -front), new Vector3(width + .08f, .44f, .18f), concrete);
            foreach (float side in new[] { -1f, 1f })
                Box(c + new Vector3(side * width * .5f, height + .22f, 0), new Vector3(.18f, .44f, depth), concrete);
            Box(c + new Vector3(-width * .22f, height + .64f, .5f), new Vector3(1.9f, 1.25f, 1.8f), plaster);
            Box(c + new Vector3(-width * .22f, height + 1.31f, .5f), new Vector3(2.05f, .12f, 1.95f), metal);
            for (int i = 0; i < 2; i++)
            {
                Vector3 ac = c + new Vector3(width * .12f + i * 1.25f, height + .42f, -.4f);
                Box(ac, new Vector3(.98f, .7f, .65f), repair);
                for (int l = 0; l < 6; l++)
                    Box(ac + new Vector3(0, -.24f + l * .095f, -.34f), new Vector3(.77f, .024f, .025f), charcoal);
                Box(ac + new Vector3(0, -.37f, 0), new Vector3(1.1f, .09f, .76f), metal);
            }
            Box(c + new Vector3(-width * .46f, height * .48f, front - .16f), new Vector3(.07f, height * .91f, .07f), metal);
            Box(c + new Vector3(-width * .42f, .14f, front - .16f), new Vector3(.65f, .08f, .08f), metal);
        }

        void RoadMarkings()
        {
            // One cut-and-filled road patch and a restrained crossing reinforce that this is a functioning street.
            Box(new Vector3(-10, -.003f, -22), new Vector3(8.8f, .006f, 3.4f), tar);
            for (int i = 0; i < 7; i++)
                Box(new Vector3(0, .006f, -19.4f - i * .84f), new Vector3(3.7f, .006f, .42f), paint);
            for (int side = -1; side <= 1; side += 2)
            {
                Box(new Vector3(side * 23.9f, .006f, -1.5f), new Vector3(.10f, .006f, 31), paint);
                for (int z = -15; z <= 12; z += 6)
                    Box(new Vector3(side * 26.8f, .006f, z), new Vector3(.11f, .006f, 2.8f), paint);
                for (int i = 0; i < 4; i++)
                {
                    float x = side * (6.5f + i * 3.5f);
                    Box(new Vector3(x, .006f, -25.8f), new Vector3(2.2f, .006f, .1f), paint);
                }
            }
            // Short asphalt seam rectangles, outside the playable floor, are flush and cannot snag feet.
            foreach (float x in new[] { -14.5f, -5.5f })
                Box(new Vector3(x, .001f, -22), new Vector3(.06f, .006f, 3.48f), charcoal);
        }

        Material Mat(string name, Color color, float smoothness, float metallic = 0)
        {
            var m = LiminalMonsterKit.Lit(color, smoothness, metallic); m.name = "Recovered city / " + name;
            owned.Add(m); return m;
        }
        static Texture2D Brick()
        {
            const int n = 512;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Weathered brick without lettering", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var px = new Color32[n * n]; var random = new System.Random(823);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int row = y / 64, bx = (x + row % 2 * 64) % n;
                    bool joint = y % 64 < 4 || bx % 128 < 4;
                    float grain = (float)random.NextDouble() * .052f;
                    float brickTone = .04f * Mathf.Sin((bx / 128 + row * 7) * 23.71f);
                    float age = Mathf.PerlinNoise(x * .035f, y * .035f) * .06f;
                    px[y * n + x] = joint ? new Color(.36f + grain, .36f + grain, .34f + grain)
                        : new Color(.43f + grain + brickTone + age, .25f + grain + brickTone + age, .20f + grain + brickTone + age);
                }
            texture.SetPixels32(px); texture.Apply(true, true); return texture;
        }

        sealed class Geometry
        {
            public readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();
        }
        void Box(Vector3 centre, Vector3 size, Material material)
        {
            if (!batches.TryGetValue(material, out var g)) { g = new Geometry(); batches.Add(material, g); }
            Vector3 h = size * .5f;
            void Face(Vector3 normal, Vector3 right, Vector3 up, float depth, float width, float height)
            {
                Vector3 c = centre + normal * depth;
                int k = g.vertices.Count;
                g.vertices.Add(c - right * width - up * height); g.vertices.Add(c + right * width - up * height);
                g.vertices.Add(c + right * width + up * height); g.vertices.Add(c - right * width + up * height);
                for (int i = 0; i < 4; i++) g.normals.Add(normal);
                g.uv.Add(Vector2.zero); g.uv.Add(new Vector2(width * 2, 0)); g.uv.Add(new Vector2(width * 2, height * 2)); g.uv.Add(new Vector2(0, height * 2));
                g.triangles.Add(k); g.triangles.Add(k + 1); g.triangles.Add(k + 2); g.triangles.Add(k); g.triangles.Add(k + 2); g.triangles.Add(k + 3);
            }
            Face(Vector3.forward, Vector3.right, Vector3.up, h.z, h.x, h.y);
            Face(Vector3.back, Vector3.left, Vector3.up, h.z, h.x, h.y);
            Face(Vector3.right, Vector3.back, Vector3.up, h.x, h.z, h.y);
            Face(Vector3.left, Vector3.forward, Vector3.up, h.x, h.z, h.y);
            Face(Vector3.up, Vector3.right, Vector3.back, h.y, h.x, h.z);
            Face(Vector3.down, Vector3.right, Vector3.forward, h.y, h.x, h.z);
        }
    }
}
