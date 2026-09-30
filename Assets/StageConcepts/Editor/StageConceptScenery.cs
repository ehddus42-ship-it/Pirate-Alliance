#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AcRoguelike.StageConcepts.Editor
{
    /// <summary>
    /// Art dressing for the Stage 1 concept branch. Coordinates are room-local:
    /// x = -13..13, z = 0..36.4. The central five-metre lane remains traversable.
    /// Gameplay floors, doors, enemies and boundary colliders belong to the builder.
    /// </summary>
    public static class StageConceptScenery
    {
        private const string ArtRoot = "Assets/StageConcepts/Art/Meshy/";
        private const string MaterialRoot = "Assets/StageConcepts/Materials";
        private const string GeometryRoot = "Assets/StageConcepts/Geometry";
        private const string TextureRoot = "Assets/StageConcepts/Textures";
        private const string RootName = "Props";
        private static readonly string[] ThemeNames = { "Enchanted forest", "Program prison", "Earth after collapse", "Crystal caverns" };
        private static readonly Dictionary<string, Texture2D[]> SurfaceCache = new Dictionary<string, Texture2D[]>();

        public static void Build(Transform parent, int theme, int roomIndex)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (theme < 0 || theme > 3) throw new ArgumentOutOfRangeException(nameof(theme));
            EnsureFolder("Assets/StageConcepts");
            EnsureFolder(MaterialRoot);
            EnsureFolder(GeometryRoot);
            EnsureFolder(TextureRoot);
            Transform previous = parent.Find(RootName);
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            Transform oldLighting = parent.Find("Lighting");
            if (oldLighting != null) Object.DestroyImmediate(oldLighting.gameObject);
            GameObject root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            root.tag = "Untagged";
            GameObject lighting = new GameObject("Lighting");
            lighting.transform.SetParent(parent, false);
            Context c = new Context(root.transform, lighting.transform, theme, roomIndex);
            switch (theme)
            {
                case 0: Forest(c); break;
                case 1: Computer(c); break;
                case 2: Ruins(c); break;
                case 3: Cave(c); break;
            }
            c.Bake();
            root.name = RootName;
        }

        private static void Forest(Context c)
        {
            Material earth = Mat("Forest_Earth", C(0x50604A), 0.02f, 0.15f);
            Material earthLight = Mat("Forest_Path", C(0x62664E), 0.01f, 0.17f);
            Material moss = Mat("Forest_Moss", C(0x50634A), 0f, 0.12f);
            Material mossLight = Mat("Forest_MossLight", C(0x566B4E), 0f, 0.14f);
            Material stone = Mat("Forest_Stone", C(0x778178), 0.04f, 0.28f);
            Material stoneDark = Mat("Forest_StoneDark", C(0x404D4D), 0.02f, 0.25f);
            Material wood = Mat("Forest_Roots", C(0x5B4436), 0f, 0.12f);
            Material rune = Mat("Forest_Rune", C(0x7BEAB5), 0.12f, 0.55f, C(0x46FFBA), 2.0f);
            Material cap = Mat("Forest_MushroomCap", C(0x9C80C5), 0.02f, 0.45f);
            Material gill = Mat("Forest_MushroomGill", C(0xD5DBB6), 0f, 0.2f);
            Material pollen = Mat("Forest_Firefly", C(0xDAF08D), 0f, 0.5f, C(0xC3EE60), 2.7f);
            Material foliage = Mat("Forest_Foliage", C(0x467B49), 0f, 0.18f);
            Material foliageLight = Mat("Forest_FoliageLight", C(0x6A8B51), 0f, 0.18f);

            c.Box(earth, V(0, -0.10f, 18.2f), V(26, 0.22f, 36.4f));
            // Shallow, broken earth plates make the path legible without introducing steps.
            for (int z = 0; z < 18; z++)
            {
                float pz = 1.0f + z * 2f;
                float px = Mathf.Sin(z * 0.65f + c.Room * 0.7f) * 0.5f;
                c.Rock(earthLight, V(px, 0.035f, pz), V(c.Range(4.2f, 5.4f), 0.035f, 2.45f), c.Range(0, 360));
                for (int side = -1; side <= 1; side += 2)
                {
                    c.Rock(z % 3 == 0 ? mossLight : moss, V(side * c.Range(5.2f, 10.6f), 0.045f, pz), V(c.Range(4, 7), 0.08f, c.Range(2.4f, 4.0f)), c.Range(0, 360));
                }
            }

            // The tallest trees sit on the far/eastern edge for the isometric camera.
            float[] zs = { 6.5f, 17.5f, 29.0f };
            for (int i = 0; i < zs.Length; i++)
            {
                c.Model("ancient_tree", V(9.25f, 0, zs[i]), V(6.8f, 10.8f + (i == c.Room % 3 ? 2 : 0), 7.4f), 20 + i * 71 + c.Room * 13, true);
                if (i != c.Room % 3)
                    c.Model("ancient_tree", V(-10.0f, 0, zs[i] + 1.3f), V(4.8f, 6.3f, 5.2f), 110 + i * 47, true);
                for (int j = 0; j < 3; j++)
                {
                    float x = 9.1f - j * 0.75f;
                    float z = zs[i] - 1.6f + j * 1.3f;
                    c.Tube(wood, new[] { V(x, 0.10f, z), V(x - 1.1f, 0.23f, z - 0.5f), V(x - 2.15f, 0.07f, z + 0.1f) }, 0.10f + j * 0.035f, 5);
                }
            }
            float altarZ = 12.5f + (c.Room % 3) * 6.4f;
            float altarX = c.Room % 2 == 0 ? -7.0f : 6.6f;
            c.Model("rune_arch", V(altarX, 0, altarZ), V(6.0f, 6.8f, 3.2f), c.Room % 2 == 0 ? 190 : 205, false);
            c.Disc(stoneDark, V(altarX, 0.04f, altarZ - 0.8f), 3.2f, 0.10f, 20);
            c.Ring(rune, V(altarX, 0.10f, altarZ - 0.8f), 2.55f, 2.60f, 0.035f, 40);
            c.Ring(stone, V(altarX, 0.07f, altarZ - 0.8f), 2.75f, 3.12f, 0.10f, 16);
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30 * Mathf.Deg2Rad;
                Vector3 p = V(altarX + Mathf.Sin(a) * 2.22f, 0.125f, altarZ - 0.8f + Mathf.Cos(a) * 2.22f);
                c.Box(rune, p, V(0.10f, 0.025f, 0.36f), Quaternion.Euler(0, i * 30, 0));
                c.Box(rune, p + V(0.05f, 0, 0.08f), V(0.20f, 0.025f, 0.07f), Quaternion.Euler(0, i * 30, 0));
            }
            c.Model("giant_mushrooms", V(-7.5f, 0, 5.5f + c.Room * 0.6f), V(4.3f, 3.8f, 4.1f), 70 + c.Room * 29, false);
            c.Model("giant_mushrooms", V(6.3f, 0, 31.5f - c.Room * 0.4f), V(3.6f, 3.3f, 3.4f), 220, false);
            for (int i = 0; i < 36; i++)
            {
                float x = (i % 2 == 0 ? -1 : 1) * c.Range(4.0f, 11.6f);
                float z = c.Range(2.0f, 34.5f);
                float h = c.Range(0.18f, 0.52f);
                c.Cone(gill, V(x, h * 0.45f, z), 0.055f, 0.075f, h, 6);
                c.Cone(cap, V(x, h, z), h * 0.65f, h * 0.12f, h * 0.36f, 8);
                if (i % 3 == 0) c.Rock(stoneDark, V(x + 0.6f, 0.08f, z), V(c.Range(0.4f, 0.9f), 0.25f, c.Range(0.4f, 1.2f)), c.Range(0, 360));
                if (i % 2 == 0) c.Octahedron(pollen, V(x, c.Range(0.7f, 2.0f), z), V(0.055f, 0.08f, 0.055f));
            }
            // A continuous, layered understorey joins the separate Meshy landmarks.
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 23; i++)
                {
                    float z = 1.1f + i * 1.55f;
                    float x = side * c.Range(10.2f, 11.9f);
                    c.Rock(i % 3 == 0 ? foliageLight : foliage, V(x, c.Range(0.25f, 0.55f), z), V(c.Range(1.3f, 2.3f), c.Range(0.7f, 1.25f), c.Range(1.7f, 2.5f)), c.Range(0, 360));
                    for (int t = 0; t < 3; t++)
                        c.Grass(i % 2 == 0 ? foliage : foliageLight, V(side * c.Range(4.4f, 11.4f), 0.06f, z + t * 0.3f), c.Range(0.18f, 0.43f), i * 37 + t * 63);
                }
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -4.15f : 4.15f;
                float z = i < 2 ? 1.8f : 34.6f;
                c.Rock(stoneDark, V(x, 0.45f, z), V(1.5f, 0.95f, 1.3f), i * 29);
                c.Rock(stone, V(x, 1.35f, z), V(0.80f, 2.1f, 0.75f), i * 17);
                c.Ring(rune, V(x, 1.72f, z), 0.40f, 0.44f, 0.10f, 14);
                c.Octahedron(rune, V(x, 2.62f, z), V(0.14f, 0.27f, 0.14f));
                c.Grass(foliage, V(x + 0.5f, 0.02f, z - 0.25f), 0.6f, i * 31);
            }
            c.Light("Rune arch bounce", V(altarX, 2.0f, altarZ), C(0x63FFD1), 2.1f, 8f);
            c.Light("Mushroom glow", V(-6.5f, 1.7f, 7), C(0xBB89FA), 1.8f, 6.5f);
        }

        private static void Computer(Context c)
        {
            Material floor = Mat("Program_Floor", C(0x0E172B), 0.55f, 0.65f);
            Material panel = Mat("Program_Panels", C(0x1D2D44), 0.7f, 0.55f);
            Material edge = Mat("Program_Grid", C(0x237B91), 0.25f, 0.75f, C(0x1689AC), 0.9f);
            Material cyan = Mat("Program_Cyan", C(0x50DBF6), 0.18f, 0.70f, C(0x32CFFA), 2.6f);
            Material magenta = Mat("Program_Magenta", C(0xDD64DA), 0.15f, 0.60f, C(0xD542CD), 2.1f);
            Material black = Mat("Program_Frame", C(0x172131), 0.70f, 0.5f);
            Material amber = Mat("Program_Warning", C(0xFABE52), 0.20f, 0.50f, C(0xFF9B28), 1.7f);
            c.Box(floor, V(0, -0.12f, 18.2f), V(26, 0.26f, 36.4f));
            for (int x = -6; x <= 6; x++)
                c.Box(edge, V(x * 2, 0.019f, 18.2f), V(0.028f, 0.013f, 36.4f));
            for (int z = 0; z <= 18; z++)
                c.Box(edge, V(0, 0.019f, z * 2), V(26, 0.013f, 0.028f));
            for (int side = -1; side <= 1; side += 2)
            {
                for (int z = 0; z < 8; z++)
                {
                    float pz = 2.5f + z * 4.4f;
                    c.Box(panel, V(side * 8.1f, 0.035f, pz), V(5.5f, 0.045f, 3.7f));
                    c.Circuit(cyan, V(side * 3.0f, 0.07f, pz), side, z + c.Room);
                    c.Box(black, V(side * 12.35f, 1.2f, pz), V(0.28f, 2.4f, 3.9f));
                    c.Box(cyan, V(side * 12.14f, 2.40f, pz), V(0.04f, 0.045f, 3.9f));
                    // Segmented luminous lines suggest a tall grid of compiled data.
                    float wallHeight = side > 0 ? 6.2f : 3.1f;
                    c.Box(edge, V(side * 12.30f, wallHeight * 0.5f + 1, pz - 1.9f), V(0.055f, wallHeight, 0.055f));
                    for (int line = 0; line < 5; line++)
                    {
                        float y = 0.45f + line * 0.32f;
                        float length = c.Range(0.5f, 2.5f);
                        c.Box(line == 0 ? magenta : edge, V(side * 12.17f, y, pz - 1.2f + length * 0.5f), V(0.032f, 0.032f, length));
                    }
                }
                for (int i = 0; i < 3; i++)
                {
                    float serverZ = 7.5f + i * 10.5f;
                    c.Model("server_monolith", V(side * 9.5f, 0, serverZ), V(3.9f, side > 0 ? 7.3f : 4.7f, 4.0f), side > 0 ? 200 : 170, true);
                    // Independent rack status panels remain readable from both sides.
                    float height = side > 0 ? 4.9f : 3.7f;
                    Vector3 face = V(side * 7.65f, height * 0.47f, serverZ);
                    c.Box(black, face, V(0.12f, height * 0.78f, 2.20f));
                    c.Box(cyan, face + V(-side * 0.09f, height * 0.35f, 0), V(0.04f, 0.08f, 2.10f));
                    c.Box(cyan, face + V(-side * 0.09f, -height * 0.35f, 0), V(0.04f, 0.06f, 2.10f));
                    for (int rack = 0; rack < 7; rack++)
                    {
                        float y = face.y - height * 0.25f + rack * height * 0.078f;
                        c.Box(panel, V(face.x - side * 0.075f, y, serverZ), V(0.07f, 0.19f, 1.95f));
                        c.Box(rack % 3 == 0 ? magenta : cyan, V(face.x - side * 0.13f, y, serverZ - 0.73f), V(0.03f, 0.064f, 0.22f));
                        for (int digit = 0; digit < 5; digit++)
                            c.Box(edge, V(face.x - side * 0.13f, y, serverZ - 0.3f + digit * 0.23f), V(0.025f, 0.045f, 0.12f));
                    }
                    c.Box(cyan, V(side * 9.5f, 0.085f, serverZ), V(4.1f, 0.045f, 0.065f));
                }
            }
            float coreX = c.Room % 2 == 0 ? 6.5f : -6.5f;
            float coreZ = 17 + (c.Room - 2) * 1.5f;
            c.Disc(black, V(coreX, 0.15f, coreZ), 2.5f, 0.3f, 16);
            c.Ring(cyan, V(coreX, 0.32f, coreZ), 2.16f, 2.29f, 0.055f, 32);
            c.Model("processor_core", V(coreX, 1.4f, coreZ), V(4.3f, 5.4f, 4.3f), 45 + c.Room * 30, false);
            c.Ring(magenta, V(coreX, 3.9f, coreZ), 2.70f, 2.77f, 0.035f, 40, Quaternion.Euler(18, 0, 15));
            float gatewayX = -coreX;
            c.Model("data_gateway", V(gatewayX, 0, 27.3f), V(5.0f, 6.7f, 2.7f), -20, false);
            c.Ring(magenta, V(gatewayX, 0.065f, 26.8f), 2.30f, 2.43f, 0.05f, 32);
            for (int i = 0; i < 19; i++)
            {
                float x = (i % 2 == 0 ? 1 : -1) * c.Range(5.0f, 11.2f);
                float z = c.Range(3.0f, 34.0f);
                float y = c.Range(1.2f, 5.1f);
                float size = c.Range(0.14f, 0.46f);
                c.WireCube(i % 3 == 0 ? magenta : cyan, V(x, y, z), size, 0.024f, Quaternion.Euler(i * 11, i * 27, i * 15));
            }
            // Flush hazard bands keep the central travel lane physically clear.
            for (int i = 0; i < 9; i++)
            {
                c.Box(amber, V(-4.7f + i * 0.45f, 0.067f, 1.9f), V(0.15f, 0.018f, 0.65f), Quaternion.Euler(0, 35, 0));
                c.Box(amber, V(4.7f - i * 0.45f, 0.067f, 34.5f), V(0.15f, 0.018f, 0.65f), Quaternion.Euler(0, 35, 0));
            }
            c.Light("Core spill", V(coreX, 2.8f, coreZ), C(0x30BEEB), 3, 10);
            c.Light("Gateway spill", V(gatewayX, 2.5f, 27.3f), C(0xD563EE), 2.4f, 9);
        }

        private static void Ruins(Context c)
        {
            Material asphalt = Mat("Ruins_Asphalt", C(0x3B403F), 0.04f, 0.22f);
            Material asphaltLight = Mat("Ruins_AsphaltPatch", C(0x3E4340), 0.02f, 0.18f);
            Material crack = Mat("Ruins_Cracks", C(0x1F2928), 0f, 0.12f);
            Material concrete = Mat("Ruins_Concrete", C(0x828678), 0.04f, 0.2f);
            Material concreteDark = Mat("Ruins_ConcreteDark", C(0x5D6660), 0.02f, 0.18f);
            Material rust = Mat("Ruins_RustedSteel", C(0x895D43), 0.52f, 0.24f);
            Material road = Mat("Ruins_FadedRoadPaint", C(0xC9B983), 0.01f, 0.15f);
            Material dark = Mat("Ruins_WindowVoid", C(0x252D2B), 0.22f, 0.34f);
            Material weeds = Mat("Ruins_Weeds", C(0x566C49), 0f, 0.15f);
            Material ember = Mat("Ruins_Ember", C(0xE69F51), 0.2f, 0.35f, C(0xF28B31), 1.2f);
            c.Box(asphalt, V(0, -0.12f, 18.2f), V(26, 0.26f, 36.4f));
            for (int side = -1; side <= 1; side += 2)
            {
                c.Box(concreteDark, V(side * 9.9f, 0.07f, 18.2f), V(5.8f, 0.14f, 36.4f));
                c.Box(concrete, V(side * 6.9f, 0.11f, 18.2f), V(0.3f, 0.22f, 36.4f));
                for (int i = 0; i < 12; i++)
                    c.Box(crack, V(side * 9.9f, 0.151f, 1 + i * 3.1f), V(5.8f, 0.012f, 0.045f));
                for (int i = 0; i < 7; i++)
                    c.Box(road, V(side * 1.4f, 0.042f, 2.2f + i * 5f), V(0.11f, 0.022f, 2.8f));
            }
            for (int i = 0; i < 28; i++)
            {
                float x = c.Range(-5.8f, 5.8f);
                float z = c.Range(1, 35);
                c.Rock(asphaltLight, V(x, 0.026f, z), V(c.Range(0.7f, 2.3f), 0.014f, c.Range(0.5f, 2.3f)), c.Range(0, 360));
                c.Tube(crack, new[] { V(x - 0.6f, 0.051f, z), V(x, 0.051f, z + 0.2f), V(x + 0.3f, 0.051f, z + 1.0f), V(x + 1, 0.051f, z + 1.5f) }, 0.018f, 4);
            }
            for (int i = 0; i < 7; i++)
            {
                c.Box(road, V(-4.5f + i * 1.5f, 0.045f, 6.1f), V(0.7f, 0.023f, 1.6f));
                if (i % 2 == 0) c.Box(crack, V(-4.6f + i * 1.5f, 0.058f, 6.1f), V(0.3f, 0.015f, 1.0f), Quaternion.Euler(0, 12, 0));
            }
            c.Model("ruined_tower", V(9.4f, 0.12f, 10.0f), V(6.3f, 11.5f, 6.7f), 16 + c.Room * 7, true);
            c.Model("ruined_tower", V(9.0f, 0.12f, 28.0f), V(7.1f, 14.5f, 8.0f), -16 - c.Room * 5, true);
            c.Model("ruined_tower", V(-10.1f, 0.12f, 29.5f), V(4.8f, 8.5f, 5.7f), 64, true);
            c.Model("broken_overpass", V(-8.2f, 0.12f, 16.7f + c.Room * 0.75f), V(7.0f, 6.4f, 9.3f), 89, true);
            c.Model("wrecked_vehicle", V(-6.2f, 0.05f, 7.9f), V(3.7f, 2.3f, 5.6f), 12 + c.Room * 9, true);
            c.Model("wrecked_vehicle", V(5.0f, 0.05f, 21.5f), V(3.4f, 2.0f, 5.1f), -19 - c.Room * 11, true);
            // Broken low facades are kept to the western edge to preserve visibility.
            for (int i = 0; i < 5; i++)
            {
                float x = i % 2 == 0 ? -11.5f : 11.6f;
                float z = 3.5f + i * 6.5f;
                float h = i % 2 == 0 ? 2.2f : 4.6f;
                c.Box(concreteDark, V(x, h * 0.5f, z), V(1.1f, h, 3.3f), Quaternion.Euler(0, 0, (i - 2) * 3));
                c.Box(dark, V(x - Mathf.Sign(x) * 0.57f, h * 0.62f, z), V(0.035f, h * 0.48f, 1.3f));
                c.Box(concrete, V(x - Mathf.Sign(x) * 0.6f, h * 0.85f, z), V(0.16f, 0.16f, 1.7f));
            }
            for (int i = 0; i < 42; i++)
            {
                float x = (i % 2 == 0 ? 1 : -1) * c.Range(4.0f, 11.5f);
                float z = c.Range(1.5f, 35f);
                float s = c.Range(0.22f, 0.70f);
                c.Rock(i % 3 == 0 ? concreteDark : concrete, V(x, s * 0.3f, z), V(s * 1.5f, s * 0.65f, s), c.Range(0, 360));
                if (i % 4 == 0)
                {
                    c.Tube(rust, new[] { V(x, 0.1f, z), V(x + 0.1f, 0.7f, z + 0.15f), V(x + 0.6f, 1.0f, z + 0.25f) }, 0.035f, 5);
                    c.Tube(rust, new[] { V(x + 0.3f, 0.1f, z), V(x + 0.4f, 0.5f, z - 0.1f), V(x + 0.8f, 0.6f, z) }, 0.035f, 5);
                }
                if (i % 3 == 0)
                    for (int blade = 0; blade < 3; blade++)
                        c.Cone(weeds, V(x + blade * 0.1f, 0.22f, z + 0.25f), 0.13f, 0, 0.44f + blade * 0.07f, 3);
            }
            // Severed utility poles, crooked streetlamps and suspended broken cables.
            for (int i = 0; i < 3; i++)
            {
                float x = i % 2 == 0 ? 6.4f : -6.4f;
                float z = 4 + i * 13.3f;
                c.Tube(rust, new[] { V(x, 0, z), V(x + 0.22f, 3.7f, z), V(x - Mathf.Sign(x) * 1.1f, 4.3f, z) }, 0.085f, 7);
                c.Box(dark, V(x - Mathf.Sign(x) * 1.1f, 4.25f, z), V(0.7f, 0.18f, 0.35f));
                c.Box(ember, V(x - Mathf.Sign(x) * 1.1f, 4.14f, z), V(0.45f, 0.025f, 0.24f));
                c.Tube(rust, new[] { V(x + 0.22f, 3.6f, z), V(x + 0.5f, 2.2f, z + 0.6f), V(x + 0.3f, 1.6f, z + 0.4f) }, 0.025f, 4);
            }
            c.Light("Last streetlamp", V(5.25f, 3.9f, 4), C(0xE4A560), 1.5f, 7);
        }

        private static void Cave(Context c)
        {
            Material ground = Mat("Cave_Floor", C(0x303E45), 0.07f, 0.34f);
            Material slate = Mat("Cave_Slate", C(0x35464D), 0.11f, 0.4f);
            Material stone = Mat("Cave_Wall", C(0x49575E), 0.06f, 0.32f);
            Material dark = Mat("Cave_DarkRock", C(0x27373F), 0.07f, 0.3f);
            Material seam = Mat("Cave_Strata", C(0x637477), 0.09f, 0.45f);
            Material water = Mat("Cave_Water", C(0x183B48), 0.30f, 0.94f);
            Material waterEdge = Mat("Cave_WaterEdge", C(0x426E78), 0.15f, 0.85f);
            Material cyan = Mat("Cave_CrystalGlow", C(0x76DCCF), 0.15f, 0.70f, C(0x4CCFD3), 1.9f);
            Material violet = Mat("Cave_VioletGlow", C(0xA49CDE), 0.16f, 0.68f, C(0x8873D9), 1.3f);
            c.Box(ground, V(0, -0.13f, 18.2f), V(26, 0.28f, 36.4f));
            for (int i = 0; i < 52; i++)
            {
                float x = c.Range(-11.3f, 11.3f);
                float z = c.Range(1.0f, 35.4f);
                c.Rock(i % 3 == 0 ? seam : slate, V(x, 0.03f, z), V(c.Range(0.8f, 3.5f), 0.025f, c.Range(0.8f, 3.7f)), c.Range(0, 360));
            }
            // Low west-facing walls and a tall eastern silhouette expose the room interior.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 11; i++)
                {
                    float h = side > 0 ? c.Range(4.8f, 7.8f) : c.Range(2.0f, 3.6f);
                    float z = 1.9f + i * 3.22f;
                    c.Rock(i % 3 == 0 ? dark : stone, V(side * 11.8f, h * 0.43f, z), V(3.0f, h, 4.2f), c.Range(-20, 20));
                    c.Rock(seam, V(side * 11.25f, h * 0.38f, z), V(1.2f, 0.22f, 3.6f), side * 12 + i * 3);
                    if (side > 0 && i % 2 == 0)
                        c.Cone(dark, V(side * 10.8f, h - 0.5f, z), 0.02f, 0.40f, 2.1f, 7);
                }
            }
            c.Model("cavern_arch", V(7.5f, 0, 26.5f), V(8.0f, 8.8f, 5.0f), 212, false);
            c.Model("cavern_arch", V(-8.4f, 0, 12.0f), V(6.0f, 5.7f, 4.0f), 180, false);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? 1 : -1) * (6.5f + (i % 3) * 0.8f);
                float z = 6 + i * 8.0f;
                c.Model("stalagmite_cluster", V(x, 0, z), V(4.1f, 4.6f + (i % 2), 4.2f), 37 + i * 63 + c.Room * 11, true);
            }
            float poolX = c.Room % 2 == 0 ? -6.8f : 6.8f;
            float poolZ = 20.0f + c.Room * 1.2f;
            c.Rock(waterEdge, V(poolX, 0.057f, poolZ), V(6.6f, 0.018f, 8.6f), 15);
            c.Rock(water, V(poolX, 0.074f, poolZ), V(6.1f, 0.018f, 8.1f), 15);
            for (int i = 0; i < 3; i++)
                c.Ring(waterEdge, V(poolX - 0.4f, 0.095f, poolZ + 0.5f), 0.55f + i * 0.5f, 0.564f + i * 0.5f, 0.004f, 28);
            c.Model("crystal_cluster", V(-7.9f, 0, 30.5f), V(4.3f, 4.7f, 4.4f), 34 + c.Room * 15, false);
            c.Model("crystal_cluster", V(6.4f, 0, 13.7f), V(4.0f, 4.0f, 4.0f), 140, false);
            c.Model("crystal_cluster", V(poolX, 0.1f, poolZ + 1.6f), V(2.4f, 2.3f, 2.5f), 215, false);
            for (int i = 0; i < 24; i++)
            {
                float x = (i % 2 == 0 ? -1 : 1) * c.Range(4, 10.3f);
                float z = c.Range(1.8f, 34.8f);
                float h = c.Range(0.15f, 0.65f);
                c.Rock(dark, V(x, 0.12f, z), V(c.Range(0.3f, 0.8f), 0.25f, c.Range(0.3f, 0.7f)), c.Range(0, 360));
                if (i % 2 == 0)
                    c.Cone(i % 3 == 0 ? violet : cyan, V(x, h * 0.5f + 0.1f, z), h * 0.23f, 0, h, 5, Quaternion.Euler(i % 3 * 11, i * 23, i % 4 * 8));
            }
            c.Light("Crystal chamber glow", V(6.4f, 2.2f, 13.7f), C(0x58CADD), 2.8f, 9.5f);
            c.Light("Deep crystal glow", V(-7.9f, 2.7f, 30.5f), C(0x9D89DD), 2.5f, 9);
        }

        private static Color C(uint hex)
        {
            return new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, 1);
        }

        private static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

        private static Material Mat(string name, Color color, float metallic, float smoothness, Color emission = default, float emissionStrength = 0)
        {
            string path = MaterialRoot + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit shader is required by StageConceptScenery.");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            if (emissionStrength > 0)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission * emissionStrength);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
                int surface = -1;
                if (name.Contains("Earth") || name.Contains("Path")) surface = 0;
                else if (name.Contains("Moss") || name.Contains("Foliage")) surface = 5;
                else if (name.Contains("Asphalt")) surface = 3;
                else if (name.Contains("Concrete") || name.Contains("RoadPaint")) surface = 2;
                else if (name.Contains("Stone") || name.Contains("Rock") || name.Contains("Slate") || name.Contains("Strata") || name == "Cave_Floor" || name == "Cave_Wall") surface = 1;
                else if (name.Contains("Program_Floor") || name.Contains("Program_Panels") || name.Contains("Program_Frame")) surface = 4;
                if (surface >= 0)
                {
                    Texture2D[] maps = SurfaceMaps(surface);
                    material.SetTexture("_BaseMap", maps[0]);
                    material.SetTexture("_BumpMap", maps[1]);
                    material.SetTextureScale("_BaseMap", Vector2.one * (surface == 1 ? 0.65f : 0.85f));
                    material.SetFloat("_BumpScale", surface == 4 ? 0.22f : 0.70f);
                    material.EnableKeyword("_NORMALMAP");
                }
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        // Small, shared tile maps provide material grain instead of extra geometry.
        // Every map is periodic, uses mipmaps, and occupies only 256 x 256 pixels.
        private static Texture2D[] SurfaceMaps(int style)
        {
            string key = new[] { "LeafLitter", "StratifiedRock", "WeatheredConcrete", "AsphaltAggregate", "BrushedPanel", "MossGrain" }[style] + "_v1";
            if (SurfaceCache.TryGetValue(key, out Texture2D[] cached) && cached[0] && cached[1]) return cached;
            string colorPath = TextureRoot + "/" + key + "_Base.asset";
            string normalPath = TextureRoot + "/" + key + "_Normal.asset";
            Texture2D baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
            Texture2D normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (!baseMap || !normalMap)
            {
                const int size = 256;
                float[] height = new float[size * size];
                Color32[] pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size;
                        float v = y / (float)size;
                        float coarse = TileNoise(u, v, 5, style * 11.71f);
                        float middle = TileNoise(u, v, 17, style * 4.31f + 2);
                        float fine = TileNoise(u, v, 63, style * 2.43f + 7);
                        uint hash = unchecked((uint)(x * 374761393 + y * 668265263 + style * 73856093));
                        hash = (hash ^ (hash >> 13)) * 1274126177;
                        float grain = (hash & 65535) / 65535f;
                        float value = coarse * 0.30f + middle * 0.27f + fine * 0.25f + grain * 0.18f;
                        if (style == 1)
                        {
                            float ridge = Mathf.Abs(Mathf.Sin((v * 10 + coarse * 1.8f + middle * 0.25f) * Mathf.PI));
                            value = value * 0.70f + Mathf.Pow(ridge, 0.45f) * 0.3f;
                        }
                        else if (style == 3) value = middle * 0.14f + fine * 0.42f + grain * 0.44f;
                        else if (style == 4) value = 0.45f + (fine - 0.5f) * 0.18f + (Mathf.Sin(v * Mathf.PI * 160) * 0.025f);
                        else if (style == 5) value = coarse * 0.45f + fine * 0.35f + grain * 0.2f;
                        height[y * size + x] = value;
                        float luminance = style == 4 ? 0.79f + value * 0.17f : 0.49f + value * 0.57f;
                        if (style == 0 && grain > 0.89f) luminance *= 0.63f;
                        if (style == 2 && grain < 0.12f) luminance *= 0.72f;
                        if (style == 3 && grain > 0.80f) luminance += 0.11f;
                        Color tint = style == 0 ? new Color(1, 0.98f + fine * 0.04f, 0.93f + coarse * 0.08f) : Color.white;
                        pixels[y * size + x] = tint * Mathf.Clamp01(luminance);
                    }
                Color32[] normals = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = height[y * size + (x + 1) % size] - height[y * size + (x + size - 1) % size];
                        float dy = height[((y + 1) % size) * size + x] - height[((y + size - 1) % size) * size + x];
                        Vector3 n = new Vector3(-dx * 2.0f, -dy * 2.0f, 1).normalized;
                        normals[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1);
                    }
                if (!baseMap)
                {
                    baseMap = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = key + " grain", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
                    baseMap.SetPixels32(pixels); baseMap.Apply(true, false); AssetDatabase.CreateAsset(baseMap, colorPath);
                }
                if (!normalMap)
                {
                    normalMap = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = key + " micro normal", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
                    normalMap.SetPixels32(normals); normalMap.Apply(true, false); AssetDatabase.CreateAsset(normalMap, normalPath);
                }
            }
            cached = new[] { baseMap, normalMap };
            SurfaceCache[key] = cached;
            return cached;
        }

        private static float TileNoise(float u, float v, float frequency, float offset)
        {
            float x = u * frequency + offset;
            float y = v * frequency + offset * 0.73f;
            return Mathf.Lerp(Mathf.Lerp(Mathf.PerlinNoise(x, y), Mathf.PerlinNoise(x - frequency, y), u),
                Mathf.Lerp(Mathf.PerlinNoise(x, y - frequency), Mathf.PerlinNoise(x - frequency, y - frequency), u), v);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash < 0) return;
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        private sealed class Context
        {
            public readonly int Room;
            private readonly int theme;
            private readonly Transform root;
            private readonly Transform lighting;
            private readonly System.Random random;
            private readonly Dictionary<Material, Geometry> batches = new Dictionary<Material, Geometry>();
            private readonly HashSet<string> missing = new HashSet<string>();
            private int modelTriangles;

            public Context(Transform root, Transform lighting, int theme, int room)
            {
                this.root = root;
                this.lighting = lighting;
                this.theme = theme;
                Room = room;
                random = new System.Random(83019 + theme * 991 + room * 97);
            }

            public float Range(float min, float max) { return min + (float)random.NextDouble() * (max - min); }

            private Geometry G(Material material)
            {
                if (!batches.TryGetValue(material, out Geometry geometry))
                {
                    geometry = new Geometry();
                    batches.Add(material, geometry);
                }
                return geometry;
            }

            public void Box(Material m, Vector3 center, Vector3 size, Quaternion? rotation = null)
            {
                Geometry g = G(m);
                Quaternion q = rotation ?? Quaternion.identity;
                Vector3 h = size * 0.5f;
                Vector3[] p = { V(-h.x, -h.y, -h.z), V(h.x, -h.y, -h.z), V(h.x, -h.y, h.z), V(-h.x, -h.y, h.z), V(-h.x, h.y, -h.z), V(h.x, h.y, -h.z), V(h.x, h.y, h.z), V(-h.x, h.y, h.z) };
                for (int i = 0; i < p.Length; i++) p[i] = center + q * p[i];
                g.Quad(p[0], p[1], p[2], p[3]);
                g.Quad(p[4], p[7], p[6], p[5]);
                g.Quad(p[0], p[4], p[5], p[1]);
                g.Quad(p[1], p[5], p[6], p[2]);
                g.Quad(p[2], p[6], p[7], p[3]);
                g.Quad(p[3], p[7], p[4], p[0]);
            }

            public void Disc(Material m, Vector3 center, float radius, float height, int segments)
            {
                Cone(m, center, radius, radius, height, segments);
            }

            public void Cone(Material m, Vector3 center, float bottomRadius, float topRadius, float height, int segments, Quaternion? rotation = null)
            {
                Geometry g = G(m);
                Quaternion q = rotation ?? Quaternion.identity;
                Vector3 low = center + q * V(0, -height * 0.5f, 0);
                Vector3 high = center + q * V(0, height * 0.5f, 0);
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2 / segments;
                    float b = (i + 1) * Mathf.PI * 2 / segments;
                    Vector3 p0 = center + q * V(Mathf.Sin(a) * bottomRadius, -height * 0.5f, Mathf.Cos(a) * bottomRadius);
                    Vector3 p1 = center + q * V(Mathf.Sin(b) * bottomRadius, -height * 0.5f, Mathf.Cos(b) * bottomRadius);
                    Vector3 p2 = center + q * V(Mathf.Sin(b) * topRadius, height * 0.5f, Mathf.Cos(b) * topRadius);
                    Vector3 p3 = center + q * V(Mathf.Sin(a) * topRadius, height * 0.5f, Mathf.Cos(a) * topRadius);
                    g.Quad(p0, p1, p2, p3);
                    if (bottomRadius > 0) g.Triangle(low, p1, p0);
                    if (topRadius > 0) g.Triangle(high, p3, p2);
                }
            }

            public void Ring(Material m, Vector3 center, float innerRadius, float outerRadius, float thickness, int segments, Quaternion? rotation = null)
            {
                Geometry g = G(m);
                Quaternion q = rotation ?? Quaternion.identity;
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2 / segments;
                    float b = (i + 1) * Mathf.PI * 2 / segments;
                    Vector3 p0 = V(Mathf.Sin(a) * innerRadius, thickness * 0.5f, Mathf.Cos(a) * innerRadius);
                    Vector3 p1 = V(Mathf.Sin(a) * outerRadius, thickness * 0.5f, Mathf.Cos(a) * outerRadius);
                    Vector3 p2 = V(Mathf.Sin(b) * outerRadius, thickness * 0.5f, Mathf.Cos(b) * outerRadius);
                    Vector3 p3 = V(Mathf.Sin(b) * innerRadius, thickness * 0.5f, Mathf.Cos(b) * innerRadius);
                    g.Quad(center + q * p0, center + q * p1, center + q * p2, center + q * p3);
                    g.Quad(center + q * p1, center + q * (p1 - V(0, thickness, 0)), center + q * (p2 - V(0, thickness, 0)), center + q * p2);
                }
            }

            public void Rock(Material m, Vector3 center, Vector3 size, float yaw)
            {
                Geometry g = G(m);
                Quaternion q = Quaternion.Euler(0, yaw, 0);
                const int sides = 7;
                Vector3[] lower = new Vector3[sides];
                Vector3[] middle = new Vector3[sides];
                Vector3[] upper = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float a = (i + 0.1f) * Mathf.PI * 2 / sides;
                    float f = Range(0.81f, 1.04f);
                    lower[i] = center + q * Vector3.Scale(V(Mathf.Sin(a) * 0.40f * f, -0.42f, Mathf.Cos(a) * 0.40f * f), size);
                    middle[i] = center + q * Vector3.Scale(V(Mathf.Sin(a) * 0.50f * f, Range(-0.05f, 0.16f), Mathf.Cos(a) * 0.50f * f), size);
                    upper[i] = center + q * Vector3.Scale(V(Mathf.Sin(a + 0.12f) * 0.30f * f, Range(0.30f, 0.52f), Mathf.Cos(a + 0.12f) * 0.30f * f), size);
                }
                Vector3 top = center + V(0.03f * size.x, 0.48f * size.y, -0.02f * size.z);
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    g.Quad(lower[i], lower[j], middle[j], middle[i]);
                    g.Quad(middle[i], middle[j], upper[j], upper[i]);
                    g.Triangle(upper[i], upper[j], top);
                }
            }

            public void Octahedron(Material m, Vector3 center, Vector3 size)
            {
                Geometry g = G(m);
                Vector3[] ring = { center + V(size.x, 0, 0), center + V(0, 0, size.z), center + V(-size.x, 0, 0), center + V(0, 0, -size.z) };
                for (int i = 0; i < 4; i++)
                {
                    g.Triangle(center + V(0, size.y, 0), ring[(i + 1) % 4], ring[i]);
                    g.Triangle(center - V(0, size.y, 0), ring[i], ring[(i + 1) % 4]);
                }
            }

            public void Grass(Material m, Vector3 center, float height, float yaw)
            {
                Geometry g = G(m);
                Quaternion q = Quaternion.Euler(0, yaw, 0);
                for (int i = 0; i < 7; i++)
                {
                    float a = i * Mathf.PI * 2 / 7;
                    Vector3 radial = V(Mathf.Sin(a), 0, Mathf.Cos(a));
                    Vector3 across = V(radial.z, 0, -radial.x) * height * 0.10f;
                    Vector3 foot = radial * height * 0.22f;
                    Vector3 tip = radial * height * 0.58f + Vector3.up * height * (0.7f + i % 3 * 0.2f);
                    Vector3 p0 = center + q * (foot - across);
                    Vector3 p1 = center + q * (foot + across);
                    Vector3 p2 = center + q * tip;
                    g.Triangle(p0, p1, p2);
                    g.Triangle(p2, p1, p0);
                }
            }

            public void Tube(Material m, Vector3[] points, float radius, int sides)
            {
                for (int i = 0; i < points.Length - 1; i++)
                {
                    Vector3 delta = points[i + 1] - points[i];
                    if (delta.sqrMagnitude < 0.00001f) continue;
                    Cone(m, (points[i + 1] + points[i]) * 0.5f, radius, radius * 0.85f, delta.magnitude, sides, Quaternion.FromToRotation(Vector3.up, delta.normalized));
                }
            }

            public void Circuit(Material m, Vector3 start, int side, int variant)
            {
                float length = 2.5f + (variant % 3) * 0.45f;
                float dz = variant % 2 == 0 ? 0.7f : -0.7f;
                Box(m, start + V(side * length * 0.5f, 0, 0), V(length, 0.018f, 0.045f));
                Box(m, start + V(side * length, 0, dz * 0.5f), V(0.045f, 0.018f, Mathf.Abs(dz)));
                Box(m, start + V(side * (length + 0.65f), 0, dz), V(1.3f, 0.018f, 0.045f));
                Ring(m, start + V(side * (length + 1.3f), 0.008f, dz), 0.10f, 0.16f, 0.015f, 12);
            }

            public void WireCube(Material m, Vector3 center, float halfExtent, float width, Quaternion q)
            {
                for (int a = -1; a <= 1; a += 2)
                    for (int b = -1; b <= 1; b += 2)
                    {
                        Box(m, center + q * V(a * halfExtent, b * halfExtent, 0), V(width, width, halfExtent * 2), q);
                        Box(m, center + q * V(a * halfExtent, 0, b * halfExtent), V(width, halfExtent * 2, width), q);
                        Box(m, center + q * V(0, a * halfExtent, b * halfExtent), V(halfExtent * 2, width, width), q);
                    }
            }

            public void Light(string name, Vector3 position, Color color, float intensity, float range)
            {
                GameObject obj = new GameObject(name);
                obj.transform.SetParent(lighting, false);
                obj.transform.localPosition = position;
                Light light = obj.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = color;
                light.intensity = intensity;
                light.range = range;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.Auto;
            }

            public void Model(string key, Vector3 position, Vector3 targetSize, float yaw, bool collider)
            {
                GameObject slot = new GameObject("MeshySlot__" + key);
                slot.transform.SetParent(root, false);
                slot.transform.localPosition = position;
                slot.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                string path = ArtRoot + key + "/" + key + ".glb";
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (source == null)
                {
                    if (missing.Add(key))
                        Debug.LogWarning($"[Stage concepts] {ThemeNames[theme]} room {Room + 1}: required Meshy asset is missing: {path}. Explicit empty placement slots were retained; rebuild after importing the asset.", root.gameObject);
                    return;
                }
                GameObject model = PrefabUtility.InstantiatePrefab(source, slot.transform) as GameObject;
                if (model == null) throw new InvalidOperationException("Cannot instantiate Meshy model: " + path);
                model.name = "Meshy__" + key;
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                foreach (Collider existingCollider in model.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(existingCollider);
                if (!TryLocalBounds(model.transform, out Bounds bounds))
                {
                    Debug.LogWarning("[Stage concepts] Meshy model contains no renderable bounds: " + path, model);
                    return;
                }
                float scale = Mathf.Min(targetSize.x / Mathf.Max(0.001f, bounds.size.x), targetSize.y / Mathf.Max(0.001f, bounds.size.y), targetSize.z / Mathf.Max(0.001f, bounds.size.z));
                model.transform.localScale = Vector3.one * scale;
                model.transform.localPosition = new Vector3(-bounds.center.x * scale, -bounds.min.y * scale, -bounds.center.z * scale);
                Vector3 actual = bounds.size * scale;
                float radians = yaw * Mathf.Deg2Rad;
                float width = Mathf.Abs(Mathf.Cos(radians)) * actual.x + Mathf.Abs(Mathf.Sin(radians)) * actual.z;
                float depth = Mathf.Abs(Mathf.Sin(radians)) * actual.x + Mathf.Abs(Mathf.Cos(radians)) * actual.z;
                // Keep even a diagonally parked vehicle's visual bounds clear of the lane.
                // This is independent from the deliberately smaller collision footprint.
                float side = position.x < 0 ? -1 : 1;
                position.x = side * Mathf.Clamp(Mathf.Abs(position.x), 2.8f + width * 0.5f, 12.9f - width * 0.5f);
                position.z = Mathf.Clamp(position.z, 0.25f + depth * 0.5f, 36.15f - depth * 0.5f);
                slot.transform.localPosition = position;
                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++) modelTriangles += (int)filter.sharedMesh.GetIndexCount(sub) / 3;
                }
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        // Imported material assets are shared across all rooms.
                        if (!material.enableInstancing)
                        {
                            material.enableInstancing = true;
                            EditorUtility.SetDirty(material);
                        }
                    }
                }
                if (collider)
                {
                    // One room-aligned footprint per substantial landmark. Clamp to
                    // the outside of the five-metre central corridor and doorway pads.
                    width = Mathf.Min(width * 0.62f, (Mathf.Abs(position.x) - 3.05f) * 2);
                    depth = Mathf.Min(depth * 0.62f, Mathf.Min(position.z - 0.8f, 35.6f - position.z) * 2);
                    if (width > 0.15f && depth > 0.15f)
                    {
                        GameObject footprint = new GameObject("Landmark footprint__" + key);
                        footprint.transform.SetParent(root, false);
                        footprint.transform.localPosition = position;
                        BoxCollider box = footprint.AddComponent<BoxCollider>();
                        box.center = V(0, Mathf.Min(actual.y, 3f) * 0.5f, 0);
                        box.size = V(width, Mathf.Min(actual.y, 3f), depth);
                    }
                }
            }

            private static bool TryLocalBounds(Transform model, out Bounds bounds)
            {
                bool found = false;
                bounds = new Bounds();
                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    Bounds local = filter.sharedMesh.bounds;
                    Matrix4x4 matrix = model.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    for (int x = -1; x <= 1; x += 2)
                        for (int y = -1; y <= 1; y += 2)
                            for (int z = -1; z <= 1; z += 2)
                            {
                                Vector3 point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, V(x, y, z)));
                                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                                else bounds.Encapsulate(point);
                            }
                }
                return found;
            }

            public void Bake()
            {
                int triangles = modelTriangles;
                foreach (KeyValuePair<Material, Geometry> pair in batches)
                {
                    string name = "T" + theme + "_R" + Room + "_" + pair.Key.name;
                    string path = GeometryRoot + "/" + name + ".asset";
                    Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    bool isNew = mesh == null;
                    if (isNew) mesh = new Mesh { name = name };
                    mesh.Clear();
                    mesh.indexFormat = pair.Value.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                    mesh.SetVertices(pair.Value.Vertices);
                    mesh.SetTriangles(pair.Value.Triangles, 0);
                    mesh.SetUVs(0, pair.Value.Uvs);
                    mesh.RecalculateNormals();
                    mesh.RecalculateTangents();
                    mesh.RecalculateBounds();
                    if (isNew) AssetDatabase.CreateAsset(mesh, path);
                    else EditorUtility.SetDirty(mesh);
                    GameObject obj = new GameObject(pair.Key.name + " (batched)");
                    obj.transform.SetParent(root, false);
                    obj.AddComponent<MeshFilter>().sharedMesh = mesh;
                    MeshRenderer renderer = obj.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = pair.Key;
                    renderer.shadowCastingMode = pair.Key.IsKeywordEnabled("_EMISSION") ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    triangles += pair.Value.Triangles.Count / 3;
                }
                if (triangles > 150000)
                    Debug.LogWarning($"[Stage concepts] {ThemeNames[theme]} room {Room + 1} art has {triangles:N0} triangles; reduce source Meshy meshes to stay below the 150k per-room budget.", root.gameObject);
                if (missing.Count == 0)
                    Debug.Log($"[Stage concepts] {ThemeNames[theme]} room {Room + 1}: {triangles:N0} art triangles, {batches.Count} decoration material batches.", root.gameObject);
            }
        }

        private sealed class Geometry
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();
            public readonly List<Vector2> Uvs = new List<Vector2>();

            public void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int index = Vertices.Count;
                Vertices.Add(a); Vertices.Add(b); Vertices.Add(c);
                Vector3 normal = Vector3.Cross(b - a, c - a);
                Uvs.Add(ProjectUv(a, normal)); Uvs.Add(ProjectUv(b, normal)); Uvs.Add(ProjectUv(c, normal));
                Triangles.Add(index); Triangles.Add(index + 1); Triangles.Add(index + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int index = Vertices.Count;
                Vertices.Add(a); Vertices.Add(b); Vertices.Add(c); Vertices.Add(d);
                Vector3 normal = Vector3.Cross(b - a, c - a);
                Uvs.Add(ProjectUv(a, normal)); Uvs.Add(ProjectUv(b, normal)); Uvs.Add(ProjectUv(c, normal)); Uvs.Add(ProjectUv(d, normal));
                Triangles.Add(index); Triangles.Add(index + 1); Triangles.Add(index + 2);
                Triangles.Add(index); Triangles.Add(index + 2); Triangles.Add(index + 3);
            }

            private static Vector2 ProjectUv(Vector3 p, Vector3 normal)
            {
                float x = Mathf.Abs(normal.x), y = Mathf.Abs(normal.y), z = Mathf.Abs(normal.z);
                if (y >= x && y >= z) return new Vector2(p.x, p.z);
                if (x >= z) return new Vector2(p.z, p.y);
                return new Vector2(p.x, p.y);
            }
        }
    }
}
#endif
