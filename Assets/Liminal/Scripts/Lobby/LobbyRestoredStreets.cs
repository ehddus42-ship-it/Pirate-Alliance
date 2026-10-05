using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// Street furniture for an occupied plaza repaired after damage: mismatched granite kerbs, patched
    /// concrete planting beds, bolted metal repairs and a small, orderly maintenance bay. No lettering,
    /// numbers or signs. All positions remain on the plaza perimeter, outside the lobby activity areas.
    /// </summary>
    public sealed class LobbyRestoredStreets : MonoBehaviour
    {
        public int PropGroupCount { get; private set; }
        public int TriangleCount { get; private set; }

        List<Object> owned;
        readonly Dictionary<Material, Geometry> batches = new Dictionary<Material, Geometry>();
        Material oldConcrete, newConcrete, granite, mortar, steel, freshSteel, darkMetal;
        Material soil, green, sage, flower, sandbag, amber, rubber;

        public static LobbyRestoredStreets Build(Transform parent, List<Object> owned)
        {
            var go = new GameObject("Restored streets and planting");
            go.transform.SetParent(parent, false);
            var streets = go.AddComponent<LobbyRestoredStreets>();
            streets.owned = owned;
            streets.Construct();
            return streets;
        }

        void Construct()
        {
            oldConcrete = Surface("Weathered concrete", new Color(.48f, .50f, .49f), .15f, 0, true, 421);
            newConcrete = Surface("Fresh repair concrete", new Color(.70f, .69f, .64f), .22f, 0, true, 732);
            granite = Surface("Reused granite kerb", new Color(.58f, .60f, .60f), .25f, 0, true, 934);
            mortar = Surface("Dark repair seam", new Color(.28f, .31f, .31f), .13f);
            steel = Surface("Weathered galvanised metal", new Color(.33f, .39f, .42f), .51f, .66f);
            freshSteel = Surface("New galvanised repair", new Color(.65f, .71f, .73f), .66f, .72f);
            darkMetal = Surface("Repainted blue-grey metal", new Color(.19f, .26f, .29f), .37f, .30f);
            soil = Surface("Mulch", new Color(.20f, .17f, .13f), .12f, 0, true, 152);
            green = Surface("Old planting", new Color(.24f, .38f, .27f), .16f);
            sage = Surface("New planting", new Color(.40f, .52f, .34f), .18f);
            flower = Surface("Small azalea flowers", new Color(.54f, .32f, .38f), .12f);
            sandbag = Surface("Clean maintenance bags", new Color(.68f, .63f, .49f), .12f, 0, true, 39);
            amber = Surface("Unlettered reflectors", new Color(.75f, .49f, .17f), .48f);
            rubber = Surface("Rubber fittings", new Color(.09f, .12f, .13f), .20f);

            Planter(new Vector3(-19.35f, 0, -6), 180, false);
            Planter(new Vector3(19, 0, 6), 0, true);
            Planter(new Vector3(14.9f, 0, -14.5f), 90, false);
            UtilityCabinet(new Vector3(-19.25f, 0, 5), -90);
            MaintenanceBay(new Vector3(-18.6f, 0, -13.9f), 0);
            GuardRail(new Vector3(-20.55f, 0, .1f), 180, true);
            GuardRail(new Vector3(20.55f, 0, -8), 0, false);
            GuardRail(new Vector3(-20.55f, 0, 11.9f), 180, false);
            GuardRail(new Vector3(20.55f, 0, 12.1f), 0, true);
            FenceRepair(new Vector3(-18.8f, 0, 15.45f), -90);
            FenceRepair(new Vector3(18.8f, 0, 15.45f), -90);

            // The entrance in the middle remains completely open, as do the existing banner positions.
            foreach (float x in new[] { -18.5f, -12.7f, -7.1f, 7.1f, 12.7f, 18.5f })
                Bollard(new Vector3(x, 0, -16.2f), x < 0);
            Kerbs(new Vector3(-21.1f, 0, 0), 0, 15);
            Kerbs(new Vector3(21.1f, 0, 0), 0, 15);
            Kerbs(new Vector3(-13.65f, 0, -17.0f), 90, 6);
            Kerbs(new Vector3(13.65f, 0, -17.0f), 90, 6);
            foreach (float x in new[] { -20.3f, 20.3f })
                foreach (float z in new[] { -10.0f, 2.0f, 11.0f }) Drain(new Vector3(x, 0, z), 0);
            ServiceCover(new Vector3(16.8f, 0, -3), 0);
            ServiceCover(new Vector3(-16.7f, 0, 6.5f), 90);
            CommitMeshes();
        }

        void Planter(Vector3 position, float yaw, bool lighterPlanting)
        {
            var f = Group("Patched low planting bed", position, yaw);
            Box(f, new Vector3(0, .11f, 0), new Vector3(1.46f, .22f, 3.74f), oldConcrete);
            Box(f, new Vector3(0, .30f, 0), new Vector3(1.10f, .40f, 3.38f), soil);
            // Three old wall sections surround a visibly newer infill. The irregular seam reads as a
            // structural repair, rather than a neat decorative stripe on an untouched planter.
            Box(f, new Vector3(.66f, .34f, 0), new Vector3(.16f, .48f, 3.72f), oldConcrete);
            Box(f, new Vector3(-.66f, .34f, -1.23f), new Vector3(.16f, .48f, 1.26f), oldConcrete);
            Box(f, new Vector3(-.66f, .34f, .83f), new Vector3(.16f, .48f, 2.06f), oldConcrete);
            Box(f, new Vector3(-.66f, .34f, -.40f), new Vector3(.17f, .48f, .44f), newConcrete);
            PatchSeam(f, -.752f, -.61f);
            PatchSeam(f, -.752f, -.19f);
            foreach (int side in new[] { -1, 1 })
            {
                Box(f, new Vector3(0, .34f, side * 1.79f), new Vector3(1.46f, .48f, .16f), oldConcrete);
                Box(f, new Vector3(0, .606f, side * 1.79f), new Vector3(1.49f, .055f, .20f), side < 0 ? newConcrete : granite);
                Box(f, new Vector3(side * .66f, .606f, 0), new Vector3(.20f, .055f, 3.53f), granite);
            }
            Box(f, new Vector3(-.66f, .609f, -.40f), new Vector3(.21f, .061f, .53f), newConcrete);
            foreach (float z in new[] { -.79f, -.03f })
            {
                Box(f, new Vector3(-.758f, .37f, z), new Vector3(.026f, .48f, .095f), freshSteel);
                Box(f, new Vector3(-.665f, .628f, z), new Vector3(.21f, .026f, .095f), freshSteel);
                Bolt(f, new Vector3(-.783f, .22f, z), Vector3.left);
                Bolt(f, new Vector3(-.783f, .51f, z), Vector3.left);
            }
            for (int i = 0; i < 7; i++)
            {
                float z = -1.42f + i * .47f;
                float x = (i % 2 == 0 ? -.16f : .19f);
                Shrub(f, new Vector3(x, .50f, z), i * 53, lighterPlanting || i == 5);
            }
            // Few stones remain in the planting, kept out of the pedestrian surface.
            for (int i = 0; i < 8; i++)
                Rock(f, new Vector3(i % 2 == 0 ? -.39f : .41f, .51f, -1.46f + i * .41f),
                    new Vector3(.11f, .065f, .085f), i * 37, i % 3 == 0 ? newConcrete : granite);
            Collider(f, new Vector3(0, .33f, 0), new Vector3(1.52f, .66f, 3.80f));
        }

        void PatchSeam(Frame f, float x, float z)
        {
            Vector3[] seam =
            {
                new Vector3(x, .12f, z), new Vector3(x, .22f, z + .04f),
                new Vector3(x, .30f, z - .025f), new Vector3(x, .43f, z + .03f),
                new Vector3(x, .50f, z - .01f), new Vector3(x, .575f, z)
            };
            for (int i = 1; i < seam.Length; i++) Tube(f, seam[i - 1], seam[i], .009f, mortar, 5);
        }

        void UtilityCabinet(Vector3 position, float yaw)
        {
            var f = Group("Repaired public utility cabinet", position, yaw);
            Box(f, new Vector3(0, .10f, 0), new Vector3(1.36f, .20f, .76f), newConcrete);
            Box(f, new Vector3(0, .72f, 0), new Vector3(1.13f, 1.05f, .56f), darkMetal);
            Box(f, new Vector3(0, 1.27f, 0), new Vector3(1.20f, .075f, .66f), steel);
            Box(f, new Vector3(-.27f, .74f, -.299f), new Vector3(.48f, .89f, .035f), steel);
            Box(f, new Vector3(.28f, .74f, -.31f), new Vector3(.48f, .89f, .055f), freshSteel);
            foreach (float x in new[] { -.07f, .09f })
                Box(f, new Vector3(x, .82f, -.348f), new Vector3(.03f, .19f, .022f), rubber);
            for (int row = 0; row < 6; row++)
            {
                Box(f, new Vector3(-.27f, .44f + row * .055f, -.322f), new Vector3(.29f, .018f, .016f), darkMetal);
                Box(f, new Vector3(.28f, .44f + row * .055f, -.344f), new Vector3(.29f, .018f, .016f), steel);
            }
            foreach (float y in new[] { .40f, 1.09f })
            {
                Box(f, new Vector3(.51f, y, -.35f), new Vector3(.06f, .12f, .035f), freshSteel);
                Bolt(f, new Vector3(.50f, y, -.378f), Vector3.back);
            }
            // External conduit was sleeved where it enters the new plinth. No warning text or symbols.
            Tube(f, new Vector3(.69f, .20f, .06f), new Vector3(.69f, .78f, .06f), .045f, steel);
            Tube(f, new Vector3(.69f, .78f, .06f), new Vector3(.57f, .86f, .06f), .045f, steel);
            Tube(f, new Vector3(.69f, .16f, .06f), new Vector3(.69f, .37f, .06f), .065f, freshSteel);
            Collider(f, new Vector3(.04f, .65f, 0), new Vector3(1.48f, 1.30f, .79f));
        }

        void MaintenanceBay(Vector3 position, float yaw)
        {
            var f = Group("Orderly repair materials", position, yaw);
            // Only a small amount of material remains, stacked on dunnage rather than scattered rubble.
            Box(f, new Vector3(0, .045f, 0), new Vector3(2.14f, .065f, 1.40f), mortar);
            foreach (float z in new[] { -.46f, .45f })
                Box(f, new Vector3(-.42f, .12f, z), new Vector3(1.14f, .13f, .13f), sandbag);
            for (int row = 0; row < 3; row++)
                for (int x = 0; x < 3; x++)
                    Box(f, new Vector3(-.78f + x * .35f, .235f + row * .095f, -.02f + (row % 2) * .025f),
                        new Vector3(.32f, .086f, 1.01f), row == 2 ? newConcrete : granite);
            for (int i = 0; i < 3; i++)
            {
                Box(f, new Vector3(.59f, .145f + i * .14f, -.10f), new Vector3(.65f, .135f, .81f), sandbag, i % 2 == 0 ? 0 : 90);
                Box(f, new Vector3(.59f, .222f + i * .14f, -.10f), new Vector3(.63f, .01f, .08f), mortar, i % 2 == 0 ? 0 : 90);
            }
            // A shallow tray contains reclaimed chipped stone; the surrounding paving is clean.
            Box(f, new Vector3(.64f, .096f, .87f), new Vector3(.83f, .065f, .39f), steel);
            for (int i = 0; i < 6; i++)
                Rock(f, new Vector3(.35f + i % 3 * .25f, .17f, .78f + i / 3 * .17f),
                    new Vector3(.11f, .07f + i % 2 * .03f, .065f), i * 61, oldConcrete);
            foreach (float z in new[] { -.42f, .34f })
            {
                Box(f, new Vector3(-.43f, .494f, z), new Vector3(1.07f, .019f, .034f), darkMetal);
                Box(f, new Vector3(-.969f, .31f, z), new Vector3(.019f, .37f, .034f), darkMetal);
                Box(f, new Vector3(.103f, .31f, z), new Vector3(.019f, .37f, .034f), darkMetal);
            }
            Collider(f, new Vector3(0, .27f, .18f), new Vector3(2.17f, .54f, 1.79f));
        }

        void GuardRail(Vector3 position, float yaw, bool repairedPost)
        {
            var f = Group("Bolted street rail repair", position, yaw);
            foreach (float z in new[] { -1.80f, 0, 1.80f })
            {
                var material = repairedPost && z == 0 ? freshSteel : steel;
                Box(f, new Vector3(0, .052f, z), new Vector3(.30f, .10f, .31f), newConcrete);
                Box(f, new Vector3(0, .385f, z), new Vector3(.085f, .67f, .10f), material);
                Box(f, new Vector3(0, .117f, z), new Vector3(.20f, .033f, .21f), freshSteel);
                foreach (float x in new[] { -.07f, .07f }) Bolt(f, new Vector3(x, .143f, z), Vector3.up);
            }
            // Folded W-section sheet, with soft bevels in its section rather than a featureless bar.
            var profile = new[]
            {
                new Vector2(.02f, .76f), new Vector2(-.035f, .72f), new Vector2(-.070f, .66f),
                new Vector2(-.025f, .58f), new Vector2(-.070f, .50f), new Vector2(-.035f, .44f), new Vector2(.02f, .41f)
            };
            ExtrudeStrip(f, profile, -2.18f, 2.18f, steel);
            // The centre span is replaced, retaining the darker old rail on both sides.
            ExtrudeStrip(f, profile, -.57f, .57f, freshSteel, -.009f);
            foreach (float z in new[] { -.47f, .47f })
            {
                Bolt(f, new Vector3(-.092f, .50f, z), Vector3.left);
                Bolt(f, new Vector3(-.092f, .67f, z), Vector3.left);
            }
            foreach (float z in new[] { -1.90f, 1.90f })
                Box(f, new Vector3(-.083f, .60f, z), new Vector3(.023f, .095f, .14f), amber);
            Collider(f, new Vector3(-.02f, .39f, 0), new Vector3(.25f, .78f, 4.42f));
        }

        void FenceRepair(Vector3 position, float yaw)
        {
            var f = Group("Mended low municipal fence", position, yaw);
            foreach (float z in new[] { -1.30f, 1.30f })
            {
                Box(f, new Vector3(0, .055f, z), new Vector3(.32f, .10f, .32f), newConcrete);
                Tube(f, new Vector3(0, .10f, z), new Vector3(0, .94f, z), .035f, steel);
            }
            Tube(f, new Vector3(0, .89f, -1.30f), new Vector3(0, .89f, 1.30f), .026f, steel);
            Tube(f, new Vector3(0, .22f, -1.30f), new Vector3(0, .22f, 1.30f), .022f, steel);
            for (int i = 0; i < 11; i++)
            {
                float z = -1.16f + i * .23f;
                var material = i >= 4 && i <= 7 ? freshSteel : darkMetal;
                Tube(f, new Vector3(0, .23f, z), new Vector3(0, .87f, z), .013f, material, 6);
            }
            foreach (float z in new[] { -.28f, .51f })
            {
                Box(f, new Vector3(-.022f, .88f, z), new Vector3(.029f, .115f, .10f), freshSteel);
                Bolt(f, new Vector3(-.055f, .88f, z), Vector3.left);
            }
            Collider(f, new Vector3(0, .47f, 0), new Vector3(.18f, .94f, 2.72f));
        }

        void Bollard(Vector3 position, bool restored)
        {
            var f = Group("Reset street bollard", position, 0);
            Box(f, new Vector3(0, .055f, 0), new Vector3(.42f, .095f, .42f), restored ? newConcrete : granite);
            Cylinder(f, new Vector3(0, .39f, 0), .092f, .64f, restored ? freshSteel : steel, 12);
            Cylinder(f, new Vector3(0, .725f, 0), .099f, .043f, darkMetal, 12);
            Cylinder(f, new Vector3(0, .58f, 0), .097f, .075f, amber, 12);
            Cylinder(f, new Vector3(0, .17f, 0), .112f, .13f, darkMetal, 12);
            foreach (float x in new[] { -.15f, .15f }) Bolt(f, new Vector3(x, .115f, 0), Vector3.up);
            Collider(f, new Vector3(0, .38f, 0), new Vector3(.30f, .76f, .30f));
        }

        void Kerbs(Vector3 position, float yaw, int halfCount)
        {
            var f = Group("Old and new kerbstones", position, yaw);
            for (int i = -halfCount; i <= halfCount; i++)
            {
                bool repair = i > -3 && i < 2 || i % 9 == 5;
                float z = i * .98f;
                Box(f, new Vector3(0, .072f, z), new Vector3(.25f, .14f, .94f), repair ? newConcrete : granite);
                // Narrow chamfer-like shoulders and expansion joints break the uniform edge.
                Box(f, new Vector3(-.137f, .048f, z), new Vector3(.045f, .09f, .94f), repair ? newConcrete : oldConcrete);
                if (repair) Box(f, new Vector3(0, .013f, z + .483f), new Vector3(.34f, .023f, .024f), mortar);
            }
        }

        void Drain(Vector3 position, float yaw)
        {
            var f = Group("Cleared stormwater drain", position, yaw);
            Box(f, new Vector3(0, .014f, 0), new Vector3(.60f, .023f, 1.15f), newConcrete);
            Box(f, new Vector3(0, .030f, 0), new Vector3(.46f, .018f, 1.01f), rubber);
            foreach (float x in new[] { -.239f, .239f })
                Box(f, new Vector3(x, .042f, 0), new Vector3(.045f, .033f, 1.05f), steel);
            foreach (float z in new[] { -.50f, .50f })
                Box(f, new Vector3(0, .042f, z), new Vector3(.49f, .033f, .037f), steel);
            for (int i = 0; i < 12; i++)
                Box(f, new Vector3(0, .042f, -.447f + i * .0813f), new Vector3(.43f, .031f, .026f), steel);
        }

        void ServiceCover(Vector3 position, float yaw)
        {
            var f = Group("Rebedded utility cover", position, yaw);
            Box(f, new Vector3(0, .011f, 0), new Vector3(1.28f, .018f, 1.00f), newConcrete);
            Box(f, new Vector3(0, .025f, 0), new Vector3(1.11f, .024f, .84f), steel);
            Box(f, new Vector3(0, .039f, 0), new Vector3(1.01f, .01f, .73f), darkMetal);
            for (int i = -3; i <= 3; i++)
                Box(f, new Vector3(i * .132f, .048f, 0), new Vector3(.016f, .009f, .61f), steel);
            foreach (float z in new[] { -.25f, .25f })
            {
                Box(f, new Vector3(0, .054f, z), new Vector3(.17f, .015f, .048f), rubber);
                foreach (float x in new[] { -.45f, .45f }) Bolt(f, new Vector3(x, .06f, z), Vector3.up);
            }
        }

        void Shrub(Frame f, Vector3 basePosition, float angle, bool fresh)
        {
            var material = fresh ? sage : green;
            Tube(f, basePosition, basePosition + Vector3.up * .33f, .026f, darkMetal, 7);
            for (int i = 0; i < 6; i++)
            {
                float a = (angle + i * 60) * Mathf.Deg2Rad;
                var end = basePosition + new Vector3(Mathf.Sin(a) * .22f, .20f + i % 3 * .07f, Mathf.Cos(a) * .22f);
                Tube(f, basePosition + Vector3.up * .08f, end, .012f, material, 5);
                Rock(f, end, new Vector3(.19f, .13f, .16f), angle + i * 47, material);
                if (i % 3 == 0 && !fresh)
                    Rock(f, end + Vector3.up * .105f, new Vector3(.042f, .032f, .044f), i * 24, flower);
            }
            Rock(f, basePosition + Vector3.up * .35f, new Vector3(.19f, .17f, .18f), angle, material);
        }

        void Bolt(Frame f, Vector3 centre, Vector3 normal)
        {
            Tube(f, centre - normal * .011f, centre + normal * .007f, .030f, steel, 10);
            Tube(f, centre, centre + normal * .018f, .021f, freshSteel, 6);
        }

        void Rock(Frame f, Vector3 centre, Vector3 radius, float yaw, Material material)
        {
            var g = For(material); var q = Quaternion.Euler(0, yaw, 0);
            const int sides = 7;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                float ra = 1 + .12f * Mathf.Sin(i * 13.6f), rb = 1 + .12f * Mathf.Sin(((i + 1) % sides) * 13.6f);
                var p0 = centre + q * new Vector3(Mathf.Sin(a) * radius.x * ra, 0, Mathf.Cos(a) * radius.z * ra);
                var p1 = centre + q * new Vector3(Mathf.Sin(b) * radius.x * rb, 0, Mathf.Cos(b) * radius.z * rb);
                var upper = centre + new Vector3(radius.x * .12f, radius.y, radius.z * -.09f);
                var lower = centre + Vector3.down * radius.y * .65f;
                Triangle(f, g, p0, p1, upper);
                Triangle(f, g, p1, p0, lower);
            }
        }

        void ExtrudeStrip(Frame f, Vector2[] profile, float z0, float z1, Material material, float offset = 0)
        {
            var g = For(material);
            for (int i = 1; i < profile.Length; i++)
            {
                var a = new Vector3(profile[i - 1].x + offset, profile[i - 1].y, z0);
                var b = new Vector3(profile[i].x + offset, profile[i].y, z0);
                var c = new Vector3(b.x, b.y, z1);
                var d = new Vector3(a.x, a.y, z1);
                Triangle(f, g, a, b, c); Triangle(f, g, a, c, d);
                // Both faces are explicit; the repair sheet is visible from either side of the plaza.
                Triangle(f, g, c, b, a); Triangle(f, g, d, c, a);
            }
        }

        Frame Group(string label, Vector3 position, float yaw)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false); go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            PropGroupCount++;
            return new Frame(go.transform, position, Quaternion.Euler(0, yaw, 0));
        }

        static void Collider(Frame f, Vector3 centre, Vector3 size)
        {
            var collider = f.root.gameObject.AddComponent<BoxCollider>();
            collider.center = centre; collider.size = size;
        }

        Material Surface(string label, Color color, float smoothness, float metallic = 0, bool texture = false, int seed = 0)
        {
            var material = LiminalMonsterKit.Lit(color, smoothness, metallic);
            material.name = "Restored streets / " + label;
            if (texture)
            {
                const int n = 128;
                var image = new Texture2D(n, n, TextureFormat.RGBA32, true)
                { name = label + " aggregate", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 2 };
                var random = new System.Random(seed); var pixels = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float broad = Mathf.PerlinNoise(x * .037f + seed * .13f, y * .037f) * .13f;
                        float grain = (float)random.NextDouble() * .09f;
                        byte value = (byte)(Mathf.Clamp01(.78f + broad + grain) * 255);
                        pixels[y * n + x] = new Color32(value, value, value, 255);
                    }
                image.SetPixels32(pixels); image.Apply(true, true);
                material.mainTexture = image;
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", image);
                owned.Add(image);
            }
            owned.Add(material);
            return material;
        }

        Geometry For(Material material)
        {
            if (!batches.TryGetValue(material, out var geometry)) batches.Add(material, geometry = new Geometry());
            return geometry;
        }

        void CommitMeshes()
        {
            foreach (var pair in batches)
            {
                var g = pair.Value;
                var mesh = new Mesh { name = pair.Key.name };
                if (g.vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(g.vertices); mesh.SetNormals(g.normals); mesh.SetUVs(0, g.uv);
                mesh.SetTriangles(g.triangles, 0); mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                owned.Add(mesh); TriangleCount += g.triangles.Count / 3;
                var go = new GameObject(pair.Key.name);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = pair.Key;
                renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            }
            batches.Clear();
        }

        readonly struct Frame
        {
            public readonly Transform root;
            public readonly Vector3 position;
            public readonly Quaternion rotation;
            public Frame(Transform root, Vector3 position, Quaternion rotation)
            { this.root = root; this.position = position; this.rotation = rotation; }
            public Vector3 Point(Vector3 point) => position + rotation * point;
        }

        sealed class Geometry
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();
        }

        static void Triangle(Frame f, Geometry g, Vector3 a, Vector3 b, Vector3 c)
        {
            a = f.Point(a); b = f.Point(b); c = f.Point(c);
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            int index = g.vertices.Count;
            g.vertices.Add(a); g.vertices.Add(b); g.vertices.Add(c);
            g.normals.Add(normal); g.normals.Add(normal); g.normals.Add(normal);
            g.uv.Add(Uv(a, normal)); g.uv.Add(Uv(b, normal)); g.uv.Add(Uv(c, normal));
            g.triangles.Add(index); g.triangles.Add(index + 1); g.triangles.Add(index + 2);
        }

        static Vector2 Uv(Vector3 p, Vector3 n)
        {
            if (Mathf.Abs(n.y) >= Mathf.Max(Mathf.Abs(n.x), Mathf.Abs(n.z))) return new Vector2(p.x, p.z) * 1.4f;
            return Mathf.Abs(n.x) > Mathf.Abs(n.z) ? new Vector2(p.z, p.y) * 1.4f : new Vector2(p.x, p.y) * 1.4f;
        }

        void Box(Frame f, Vector3 centre, Vector3 size, Material material, float yaw = 0)
        {
            var g = For(material); var rotation = Quaternion.Euler(0, yaw, 0); var h = size * .5f;
            var x = rotation * Vector3.right; var y = Vector3.up; var z = rotation * Vector3.forward;
            Face(f, g, centre + x * h.x, z * h.z, y * h.y);
            Face(f, g, centre - x * h.x, -z * h.z, y * h.y);
            Face(f, g, centre + y * h.y, x * h.x, z * h.z);
            Face(f, g, centre - y * h.y, x * h.x, -z * h.z);
            Face(f, g, centre + z * h.z, -x * h.x, y * h.y);
            Face(f, g, centre - z * h.z, x * h.x, y * h.y);
        }

        static void Face(Frame f, Geometry g, Vector3 centre, Vector3 horizontal, Vector3 vertical)
        {
            var a = centre - horizontal - vertical; var b = centre - horizontal + vertical;
            var c = centre + horizontal + vertical; var d = centre + horizontal - vertical;
            Triangle(f, g, a, b, c); Triangle(f, g, a, c, d);
        }

        void Cylinder(Frame f, Vector3 centre, float radius, float height, Material material, int sides = 12)
        { Tube(f, centre - Vector3.up * height * .5f, centre + Vector3.up * height * .5f, radius, material, sides); }

        void Tube(Frame f, Vector3 start, Vector3 end, float radius, Material material, int sides = 8)
        {
            var g = For(material); var axis = (end - start).normalized;
            var rotation = Quaternion.FromToRotation(Vector3.up, axis);
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, c = (i + 1) * Mathf.PI * 2 / sides;
                var av = rotation * new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var cv = rotation * new Vector3(Mathf.Sin(c), 0, Mathf.Cos(c));
                var p0 = start + av * radius; var p1 = start + cv * radius;
                var p2 = end + cv * radius; var p3 = end + av * radius;
                Triangle(f, g, p0, p1, p2); Triangle(f, g, p0, p2, p3);
                Triangle(f, g, start, p1, p0); Triangle(f, g, end, p3, p2);
            }
        }
    }
}
