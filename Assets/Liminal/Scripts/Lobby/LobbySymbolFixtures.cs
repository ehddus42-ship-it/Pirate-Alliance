using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// Repaired plaza amenities. Every visible mark is modelled geometry: no letters, numbers, brands or texture text.
    /// Original placement is retained; each fixture uses a small collision footprint and batches its static details by material.
    /// </summary>
    public sealed class LobbySymbolFixtures : MonoBehaviour
    {
        List<Object> owned;
        Material oldSteel, freshIvory, dark, metal, turquoise, amber, blue, coral, yellow, rust, glass, screen;
        public int FixtureCount { get; private set; }

        public static LobbySymbolFixtures Build(Transform parent, List<Object> owned)
        {
            var root = new GameObject("LobbySymbolFixtures");
            root.transform.SetParent(parent, false);
            var fixtures = root.AddComponent<LobbySymbolFixtures>();
            fixtures.owned = owned;
            fixtures.Construct();
            return fixtures;
        }

        void Construct()
        {
            oldSteel = Mat("Reused blue grey steel", new Color(.25f, .32f, .35f), .30f, .45f);
            freshIvory = Mat("Replacement enamel", new Color(.85f, .86f, .79f), .48f, .20f);
            dark = Mat("Rubber and cabinet recess", new Color(.045f, .065f, .082f), .22f);
            metal = Mat("New stainless fasteners", new Color(.47f, .53f, .55f), .65f, .78f);
            turquoise = Mat("Association turquoise", new Color(.09f, .48f, .48f), .35f);
            amber = Mat("Warm amber drink", new Color(.90f, .49f, .12f), .42f);
            blue = Mat("Cool blue drink", new Color(.16f, .47f, .68f), .40f);
            coral = Mat("Coral drink", new Color(.73f, .24f, .17f), .40f);
            yellow = Mat("Safety yellow", new Color(.93f, .67f, .12f), .27f);
            rust = Mat("Sealed old abrasion", new Color(.30f, .23f, .17f), .16f);
            screen = Mat("Low brightness indicator", new Color(.28f, .68f, .64f), .40f);
            screen.EnableKeyword("_EMISSION"); screen.SetColor("_EmissionColor", new Color(.07f, .23f, .21f));
            glass = Mat("Window glass", new Color(.58f, .79f, .84f, .16f), .84f);
            glass.SetFloat("_Surface", 1); glass.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            glass.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); glass.SetFloat("_ZWrite", 0);
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); glass.renderQueue = (int)RenderQueue.Transparent;
            glass.SetOverrideTag("RenderType", "Transparent");

            Vending(); Water();
            Caution(new Vector3(-1.9f, 0, 7.6f), 20, "West safety stand");
            Caution(new Vector3(1.9f, 0, 7.6f), -20, "East safety stand");
            Directory();
        }

        void Vending()
        {
            var b = Begin("Repaired drinks cabinet", new Vector3(13.2f, 0, -5.5f), -90);
            // Retained shell around an open display cavity; the replacement door is visibly cleaner than its sides.
            Box(b, new Vector3(0, .09f, 0), new Vector3(.91f, .14f, .76f), dark);
            Box(b, new Vector3(0, .88f, -.33f), new Vector3(.91f, 1.58f, .10f), oldSteel);
            Box(b, new Vector3(-.42f, .91f, 0), new Vector3(.08f, 1.62f, .73f), oldSteel);
            Box(b, new Vector3(.42f, .91f, 0), new Vector3(.08f, 1.62f, .73f), oldSteel);
            Panel(b, new Vector3(0, 1.71f, 0), new Vector2(.93f, .11f), .77f, .035f, oldSteel);
            Box(b, new Vector3(-.065f, 1.06f, .16f), new Vector3(.63f, .91f, .035f), dark);
            // Four columns and three shelves of unlabelled cans. Coloured sleeves replace brand graphics.
            Material[] drinks = { turquoise, amber, blue, coral };
            for (int row = 0; row < 3; row++)
            {
                float y = .77f + row * .235f;
                Box(b, new Vector3(-.07f, y - .105f, .275f), new Vector3(.65f, .028f, .23f), metal);
                for (int column = 0; column < 4; column++)
                {
                    Vector3 centre = new Vector3(-.30f + column * .15f, y, .28f);
                    Cylinder(b, centre, .049f, .174f, drinks[(column + row) % drinks.Length], 12);
                    Cylinder(b, centre + Vector3.up * .087f, .047f, .007f, metal, 12);
                    Cylinder(b, centre - Vector3.up * .087f, .047f, .007f, metal, 12);
                    Tube(b, centre + new Vector3(-.015f, .093f, 0), centre + new Vector3(.01f, .093f, 0), .006f, dark, 6);
                }
            }
            // A narrow bolted door frame rather than a solid block across the window.
            Box(b, new Vector3(-.421f, .97f, .398f), new Vector3(.07f, 1.36f, .075f), freshIvory);
            Box(b, new Vector3(.255f, 1.04f, .398f), new Vector3(.058f, 1.00f, .075f), freshIvory);
            Box(b, new Vector3(-.08f, 1.51f, .398f), new Vector3(.70f, .08f, .075f), freshIvory);
            Box(b, new Vector3(-.08f, .59f, .398f), new Vector3(.70f, .08f, .075f), freshIvory);
            Panel(b, new Vector3(.35f, 1.025f, .389f), new Vector2(.17f, .94f), .10f, .018f, freshIvory);
            Box(b, new Vector3(-.075f, 1.06f, .414f), new Vector3(.61f, .82f, .006f), glass);
            for (int i = 0; i < 3; i++)
            {
                Disk(b, new Vector3(.35f, 1.24f - i * .15f, .445f), .031f, dark);
                Disk(b, new Vector3(.35f, 1.244f - i * .15f, .45f), .024f, i == 1 ? screen : metal);
            }
            Box(b, new Vector3(.35f, .765f, .451f), new Vector3(.078f, .018f, .012f), dark);
            Panel(b, new Vector3(0, .38f, .384f), new Vector2(.84f, .32f), .08f, .02f, freshIvory);
            Panel(b, new Vector3(-.07f, .38f, .43f), new Vector2(.50f, .18f), .035f, .022f, dark);
            Box(b, new Vector3(-.07f, .303f, .465f), new Vector3(.49f, .021f, .08f), metal);
            Panel(b, new Vector3(0, 1.615f, .396f), new Vector2(.84f, .18f), .045f, .022f, turquoise);
            CupIcon(b, new Vector3(0, 1.615f, .423f), .12f, freshIvory);
            // Two understated steel repair straps and their fasteners explain the salvaged chassis.
            foreach (float y in new[] { .22f, 1.57f })
            {
                Box(b, new Vector3(-.468f, y, -.025f), new Vector3(.012f, .075f, .53f), metal);
                for (int end = -1; end <= 1; end += 2)
                    Tube(b, new Vector3(-.478f, y, end * .23f), new Vector3(-.46f, y, end * .23f), .018f, dark, 6);
            }
            foreach (float x in new[] { -.412f, .418f })
                foreach (float y in new[] { .24f, 1.45f }) Bolt(b, new Vector3(x, y, .439f));
            Box(b, new Vector3(-.470f, .42f, -.19f), new Vector3(.008f, .14f, .095f), rust);
            End(b, new Vector3(0, .90f, .025f), new Vector3(.95f, 1.80f, .83f));
        }

        void Water()
        {
            var b = Begin("Refitted drinking water station", new Vector3(13.2f, 0, -3.6f), -90);
            Panel(b, new Vector3(0, .47f, 0), new Vector2(.53f, .86f), .51f, .045f, oldSteel);
            Box(b, new Vector3(0, .08f, 0), new Vector3(.56f, .12f, .56f), dark);
            Panel(b, new Vector3(0, .46f, .27f), new Vector2(.46f, .68f), .045f, .028f, freshIvory);
            // An accessible recessed cup niche under two unlabelled temperature buttons.
            Panel(b, new Vector3(0, .90f, .035f), new Vector2(.53f, .28f), .44f, .032f, freshIvory);
            Box(b, new Vector3(0, .80f, .27f), new Vector3(.34f, .20f, .026f), dark);
            Box(b, new Vector3(0, .704f, .305f), new Vector3(.40f, .031f, .17f), metal);
            for (int i = -1; i <= 1; i += 2)
            {
                Disk(b, new Vector3(i * .10f, .968f, .275f), .029f, i < 0 ? blue : coral);
                Tube(b, new Vector3(i * .10f, .882f, .30f), new Vector3(i * .10f, .837f, .30f), .017f, metal);
            }
            for (int i = 0; i < 5; i++) Box(b, new Vector3(-.14f + i * .07f, .724f, .314f), new Vector3(.013f, .005f, .13f), dark);
            // Bottle silhouette and water level, deliberately free of any label or brand.
            Cylinder(b, new Vector3(0, 1.08f, 0), .11f, .13f, dark, 16);
            Cylinder(b, new Vector3(0, 1.29f, 0), .202f, .36f, blue, 20);
            Cylinder(b, new Vector3(0, 1.39f, 0), .207f, .17f, glass, 20);
            Cylinder(b, new Vector3(0, 1.486f, 0), .18f, .025f, metal, 20);
            Cylinder(b, new Vector3(0, 1.132f, 0), .194f, .026f, metal, 20);
            DropIcon(b, new Vector3(0, .45f, .298f), .17f, turquoise);
            foreach (float x in new[] { -.184f, .184f })
                foreach (float y in new[] { .165f, .753f }) Bolt(b, new Vector3(x, y, .300f));
            Box(b, new Vector3(-.276f, .28f, -.07f), new Vector3(.012f, .07f, .28f), metal);
            End(b, new Vector3(0, .76f, .015f), new Vector3(.58f, 1.52f, .62f));
        }

        void Caution(Vector3 position, float yaw, string name)
        {
            var b = Begin(name, position, yaw);
            // A reusable folding steel stand with newly repainted yellow faces and scuffed protective feet.
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Tube(b, new Vector3(x * .225f, .06f, z * .19f), new Vector3(x * .225f, .83f, 0), .024f, oldSteel, 8);
                    Box(b, new Vector3(x * .225f, .035f, z * .19f), new Vector3(.085f, .055f, .075f), dark);
                }
            Tube(b, new Vector3(-.225f, .845f, 0), new Vector3(.225f, .845f, 0), .032f, metal, 12);
            Panel(b, new Vector3(0, .48f, 0), new Vector2(.49f, .60f), .13f, .035f, yellow);
            // The same warning works from either approach. Symbols only: outlined hazard triangle and a lightning bolt.
            foreach (float side in new[] { -1f, 1f })
            {
                float z = side * .071f;
                Stroke(b, new Vector3(0, .692f, z), new Vector3(-.163f, .405f, z), .025f, dark);
                Stroke(b, new Vector3(-.163f, .405f, z), new Vector3(.163f, .405f, z), .025f, dark);
                Stroke(b, new Vector3(.163f, .405f, z), new Vector3(0, .692f, z), .025f, dark);
                Stroke(b, new Vector3(.025f, .61f, z + side * .005f), new Vector3(-.048f, .512f, z + side * .005f), .025f, dark);
                Stroke(b, new Vector3(-.048f, .512f, z + side * .005f), new Vector3(.046f, .527f, z + side * .005f), .025f, dark);
                Stroke(b, new Vector3(.046f, .527f, z + side * .005f), new Vector3(-.018f, .439f, z + side * .005f), .025f, dark);
                foreach (float x in new[] { -.19f, .19f }) Disk(b, new Vector3(x, .737f, z + side * .005f), .014f, metal);
                // A single replacement strip, riveted rather than taped over any old wording.
                Box(b, new Vector3(0, .263f, z), new Vector3(.38f, .058f, .01f), freshIvory);
                foreach (float x in new[] { -.16f, .16f }) Disk(b, new Vector3(x, .263f, z + side * .01f), .013f, metal);
            }
            End(b, new Vector3(0, .45f, 0), new Vector3(.538f, .90f, .467f));
        }

        void Directory()
        {
            var b = Begin("Symbol route totem", new Vector3(3.5f, 0, -12), 180);
            Panel(b, new Vector3(0, .075f, 0), new Vector2(.74f, .12f), .60f, .025f, oldSteel);
            Panel(b, new Vector3(0, .51f, 0), new Vector2(.33f, .87f), .24f, .025f, oldSteel);
            Panel(b, new Vector3(0, 1.325f, 0), new Vector2(.76f, 1.03f), .26f, .045f, freshIvory);
            Panel(b, new Vector3(0, 1.81f, 0), new Vector2(.79f, .09f), .33f, .025f, oldSteel);
            // Physical patch over the old pedestal and four new anchor bolts in the retained base.
            Box(b, new Vector3(0, .35f, .137f), new Vector3(.27f, .15f, .025f), metal);
            foreach (float x in new[] { -.11f, .11f })
                foreach (float y in new[] { .302f, .398f }) Bolt(b, new Vector3(x, y, .154f));
            foreach (int side in new[] { -1, 1 })
            {
                float z = side * .138f;
                Panel(b, new Vector3(0, 1.335f, z), new Vector2(.644f, .834f), .018f, .02f, dark);
                // A stylised local block map: neighbourhood masses, pedestrian route, gateway and medical icons.
                foreach (var item in new[] { new Vector3(-.20f, 1.59f, .105f), new Vector3(.13f, 1.60f, .13f),
                    new Vector3(-.18f, 1.36f, .15f), new Vector3(.17f, 1.24f, .16f), new Vector3(-.14f, 1.04f, .12f) })
                    Box(b, new Vector3(item.x, item.y, z + side * .018f), new Vector3(item.z, item.z * .67f, .014f), oldSteel);
                Box(b, new Vector3(.02f, 1.37f, z + side * .023f), new Vector3(.035f, .59f, .008f), freshIvory);
                Box(b, new Vector3(0, 1.48f, z + side * .023f), new Vector3(.44f, .026f, .008f), freshIvory);
                Box(b, new Vector3(-.115f, 1.14f, z + side * .023f), new Vector3(.27f, .025f, .008f), freshIvory);
                // Dashed turquoise journey, terminating in a geometric gate. No street names or numeric markers.
                for (int i = 0; i < 7; i++) Disk(b, new Vector3(.075f, 1.11f + i * .077f, z + side * .035f), .014f, turquoise);
                for (int i = 0; i < 3; i++) Disk(b, new Vector3(-.11f + i * .061f, 1.11f, z + side * .035f), .014f, turquoise);
                Ring(b, new Vector3(.075f, 1.67f, z + side * .038f), .051f, .012f, screen);
                Ring(b, new Vector3(.075f, 1.67f, z + side * .041f), .027f, .008f, turquoise);
                Box(b, new Vector3(-.18f, 1.245f, z + side * .035f), new Vector3(.102f, .026f, .012f), turquoise);
                Box(b, new Vector3(-.18f, 1.245f, z + side * .036f), new Vector3(.026f, .102f, .013f), turquoise);
                // A location pin is a round head and taper, not a letter or exclamation mark.
                Ring(b, new Vector3(-.16f, 1.07f, z + side * .04f), .029f, .012f, amber);
                Stroke(b, new Vector3(-.186f, 1.054f, z + side * .04f), new Vector3(-.16f, 1.017f, z + side * .04f), .017f, amber);
                Stroke(b, new Vector3(-.134f, 1.054f, z + side * .04f), new Vector3(-.16f, 1.017f, z + side * .04f), .017f, amber);
                foreach (float x in new[] { -.336f, .336f })
                    foreach (float y in new[] { .88f, 1.77f }) Disk(b, new Vector3(x, y, z + side * .015f), .013f, metal);
            }
            End(b, new Vector3(0, .95f, 0), new Vector3(.80f, 1.90f, .63f));
        }

        void CupIcon(Batch b, Vector3 centre, float size, Material material)
        {
            var shape = new[] { new Vector2(-.60f, .50f), new Vector2(.30f, .50f), new Vector2(.19f, -.48f), new Vector2(-.46f, -.48f) };
            Polygon(b, centre, shape, size, material);
            Stroke(b, centre + new Vector3(.30f, .31f, 0) * size, centre + new Vector3(.66f, .31f, 0) * size, size * .14f, material);
            Stroke(b, centre + new Vector3(.66f, .31f, 0) * size, centre + new Vector3(.62f, -.22f, 0) * size, size * .14f, material);
            Stroke(b, centre + new Vector3(.62f, -.22f, 0) * size, centre + new Vector3(.23f, -.22f, 0) * size, size * .14f, material);
        }
        void DropIcon(Batch b, Vector3 centre, float size, Material material)
        {
            Polygon(b, centre, new[] { new Vector2(0, .73f), new Vector2(.46f, .02f), new Vector2(.42f, -.34f),
                new Vector2(.23f, -.52f), new Vector2(-.23f, -.52f), new Vector2(-.42f, -.34f), new Vector2(-.46f, .02f) }, size, material);
        }
        void Bolt(Batch b, Vector3 centre) => Tube(b, centre - Vector3.forward * .006f, centre + Vector3.forward * .006f, .014f, metal, 6);
        static void Disk(Batch b, Vector3 centre, float radius, Material material) => Tube(b, centre - Vector3.forward * .003f, centre + Vector3.forward * .003f, radius, material, 16);
        static void Stroke(Batch b, Vector3 start, Vector3 end, float width, Material material) => Tube(b, start, end, width * .5f, material, 8);
        static void Ring(Batch b, Vector3 centre, float radius, float width, Material material)
        {
            for (int i = 0; i < 20; i++)
            {
                float a = i * Mathf.PI * 2 / 20, c = (i + 1) * Mathf.PI * 2 / 20;
                Stroke(b, centre + new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0) * radius,
                    centre + new Vector3(Mathf.Sin(c), Mathf.Cos(c), 0) * radius, width, material);
            }
        }

        Material Mat(string label, Color color, float smoothness, float metallic = 0)
        {
            var material = LiminalMonsterKit.Lit(color, smoothness, metallic);
            material.name = "Repaired plaza / " + label; owned.Add(material); return material;
        }
        Batch Begin(string name, Vector3 position, float yaw)
        {
            var root = new GameObject(name).transform;
            root.SetParent(transform, false); root.localPosition = position; root.localRotation = Quaternion.Euler(0, yaw, 0);
            FixtureCount++; return new Batch(root);
        }
        void End(Batch b, Vector3 centre, Vector3 size)
        {
            foreach (var pair in b.geometry)
            {
                var mesh = new Mesh { name = b.root.name + " / " + pair.Key.name };
                var geometry = pair.Value;
                if (geometry.vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(geometry.vertices); mesh.SetNormals(geometry.normals); mesh.SetTriangles(geometry.triangles, 0); mesh.RecalculateBounds();
                owned.Add(mesh);
                var go = new GameObject(pair.Key.name); go.transform.SetParent(b.root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = pair.Key;
                renderer.shadowCastingMode = pair.Key == glass ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }
            var collider = b.root.gameObject.AddComponent<BoxCollider>(); collider.center = centre; collider.size = size;
        }
        sealed class Batch
        {
            public readonly Transform root;
            public readonly Dictionary<Material, Geometry> geometry = new Dictionary<Material, Geometry>();
            public Batch(Transform root) { this.root = root; }
            public Geometry For(Material material)
            {
                if (!geometry.TryGetValue(material, out var value)) geometry.Add(material, value = new Geometry());
                return value;
            }
        }
        sealed class Geometry
        {
            public readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            public readonly List<int> triangles = new List<int>();
            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
            {
                // All helpers provide the intended outward normal; enforce winding even for back-facing symbol panels.
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0) { var swap = b; b = c; c = swap; }
                int index = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); normals.Add(normal); normals.Add(normal); normals.Add(normal);
                triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            { Triangle(a, b, c, normal); Triangle(a, c, d, normal); }
        }
        static void Polygon(Batch b, Vector3 centre, Vector2[] points, float scale, Material material)
        {
            var g = b.For(material); Vector3 sum = Vector3.zero;
            foreach (var p in points) sum += new Vector3(p.x, p.y, 0) * scale;
            Vector3 middle = centre + sum / points.Length;
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i]; var q = points[(i + 1) % points.Length];
                g.Triangle(middle, centre + new Vector3(p.x, p.y, 0) * scale, centre + new Vector3(q.x, q.y, 0) * scale, Vector3.forward);
            }
        }
        static void Panel(Batch b, Vector3 centre, Vector2 size, float depth, float bevel, Material material)
        {
            var g = b.For(material); float x = size.x * .5f, y = size.y * .5f, z = depth * .5f;
            float cut = Mathf.Min(bevel, Mathf.Min(x, y) * .8f);
            var edge = new[] { new Vector3(-x + cut, -y, 0), new Vector3(x - cut, -y, 0), new Vector3(x, -y + cut, 0), new Vector3(x, y - cut, 0),
                new Vector3(x - cut, y, 0), new Vector3(-x + cut, y, 0), new Vector3(-x, y - cut, 0), new Vector3(-x, -y + cut, 0) };
            for (int i = 0; i < edge.Length; i++)
            {
                Vector3 a = centre + edge[i], c = centre + edge[(i + 1) % edge.Length], front = Vector3.forward * z;
                g.Triangle(centre + front, a + front, c + front, Vector3.forward);
                g.Triangle(centre - front, c - front, a - front, Vector3.back);
                var d = c - a; g.Quad(a - front, c - front, c + front, a + front, new Vector3(d.y, -d.x, 0).normalized);
            }
        }
        static void Box(Batch b, Vector3 centre, Vector3 size, Material material)
        {
            var g = b.For(material); var h = size * .5f;
            Face(g, centre + Vector3.right * h.x, Vector3.forward * h.z, Vector3.up * h.y, Vector3.right);
            Face(g, centre - Vector3.right * h.x, -Vector3.forward * h.z, Vector3.up * h.y, Vector3.left);
            Face(g, centre + Vector3.up * h.y, Vector3.right * h.x, Vector3.forward * h.z, Vector3.up);
            Face(g, centre - Vector3.up * h.y, Vector3.right * h.x, -Vector3.forward * h.z, Vector3.down);
            Face(g, centre + Vector3.forward * h.z, -Vector3.right * h.x, Vector3.up * h.y, Vector3.forward);
            Face(g, centre - Vector3.forward * h.z, Vector3.right * h.x, Vector3.up * h.y, Vector3.back);
        }
        static void Face(Geometry geometry, Vector3 centre, Vector3 horizontal, Vector3 vertical, Vector3 normal)
        { geometry.Quad(centre - horizontal - vertical, centre - horizontal + vertical, centre + horizontal + vertical, centre + horizontal - vertical, normal); }
        static void Cylinder(Batch b, Vector3 centre, float radius, float height, Material material, int sides = 12)
        { Tube(b, centre - Vector3.up * height * .5f, centre + Vector3.up * height * .5f, radius, material, sides); }
        static void Tube(Batch b, Vector3 start, Vector3 end, float radius, Material material, int sides = 10)
        {
            var g = b.For(material); var axis = (end - start).normalized;
            var rotation = Quaternion.FromToRotation(Vector3.up, axis);
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, c = (i + 1) * Mathf.PI * 2 / sides;
                var av = rotation * new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var cv = rotation * new Vector3(Mathf.Sin(c), 0, Mathf.Cos(c));
                var p0 = start + av * radius; var p1 = start + cv * radius;
                var p2 = end + cv * radius; var p3 = end + av * radius;
                g.Quad(p0, p1, p2, p3, (av + cv).normalized);
                g.Triangle(start, p1, p0, -axis); g.Triangle(end, p3, p2, axis);
            }
        }
    }
}
