#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AcRoguelike.GameTheme.Editor
{
    /// <summary>Authors original arcade creatures. Exposed voxel faces share one point-filtered palette atlas.</summary>
    public static class GameThemeMonsterBuilder
    {
        public const string Root = "Assets/GameTheme";
        const string MeshFolder = Root + "/Art/Monsters/Meshes";
        const string ArtFolder = Root + "/Art/Monsters";
        const string PrefabFolder = Root + "/Prefabs/Monsters";
        const string LibraryPath = Root + "/Resources/GameTheme/GameVoxelMonsterLibrary.asset";
        static Material palette;
        // Dark chassis, cool metal, ivory, copper, orange, gold, teal, aqua, violet, lilac, eye light, hot pixel.
        static readonly Color32[] Colors =
        {
            new Color32(22, 29, 46, 255), new Color32(57, 72, 95, 255), new Color32(216, 224, 223, 255),
            new Color32(153, 58, 33, 255), new Color32(242, 106, 41, 255), new Color32(255, 189, 75, 255),
            new Color32(21, 103, 113, 255), new Color32(46, 188, 181, 255), new Color32(87, 57, 145, 255),
            new Color32(155, 117, 228, 255), new Color32(157, 255, 230, 255), new Color32(255, 215, 147, 255),
            new Color32(82, 92, 111, 255), new Color32(227, 156, 105, 255), new Color32(192, 169, 237, 255), new Color32(255, 244, 211, 255)
        };

        [MenuItem("Tools/Game Theme/Build Voxel Monsters")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(MeshFolder);
            Directory.CreateDirectory(PrefabFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
            AssetDatabase.Refresh();
            palette = BuildPalette();
            var library = AssetDatabase.LoadAssetAtPath<GameVoxelMonsterLibrary>(LibraryPath);
            if (!library) { library = ScriptableObject.CreateInstance<GameVoxelMonsterLibrary>(); AssetDatabase.CreateAsset(library, LibraryPath); }
            library.pixelMaw = Build(GameVoxelRole.PixelMaw);
            library.bitSentry = Build(GameVoxelRole.BitSentry);
            library.stackGuardian = Build(GameVoxelRole.StackGuardian);
            library.paletteMaterial = palette;
            var bolt = new Voxels(.12f);
            bolt.Box(-1, 1, -1, 1, -1, 1, 6);
            bolt.Box(0, 0, 0, 0, -2, 2, 10);
            bolt.Box(-1, 1, 0, 0, 0, 0, 10);
            bolt.Box(0, 0, -1, 1, 0, 0, 10);
            library.projectileMesh = SaveMesh(bolt.Mesh("Game Pixel Bolt"), "PixelBolt");
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            ValidateBuiltAssets();
            Debug.Log("GAME_MONSTERS_BUILD_OK: three voxel silhouettes, one shared palette, collision and combat components authored.");
        }

        static Material BuildPalette()
        {
            var albedo = new Texture2D(16, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            albedo.SetPixels32(Colors);
            albedo.Apply();
            File.WriteAllBytes(ArtFolder + "/VoxelPalette.png", albedo.EncodeToPNG());
            var glow = new Color32[16];
            for (int i = 0; i < 16; i++) glow[i] = i == 10 || i == 11 || i == 15 ? Colors[i] : new Color32(0, 0, 0, 255);
            albedo.SetPixels32(glow);
            albedo.Apply();
            File.WriteAllBytes(ArtFolder + "/VoxelEmission.png", albedo.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(albedo);
            AssetDatabase.Refresh();
            foreach (string path in new[] { ArtFolder + "/VoxelPalette.png", ArtFolder + "/VoxelEmission.png" })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }
            string materialPath = ArtFolder + "/VoxelPalette.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtFolder + "/VoxelPalette.png"));
            material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtFolder + "/VoxelEmission.png"));
            material.SetColor("_EmissionColor", Color.white * 1.7f);
            material.SetFloat("_Smoothness", .3f);
            material.SetFloat("_Metallic", .18f);
            material.EnableKeyword("_EMISSION");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static GameObject Build(GameVoxelRole role)
        {
            var root = new GameObject(role.ToString());
            try
            {
                var visual = Child(root.transform, "Visual", Vector3.zero);
                var pose = Child(visual, "Pose", Vector3.zero);
                switch (role)
                {
                    case GameVoxelRole.PixelMaw: BuildMaw(pose); break;
                    case GameVoxelRole.BitSentry: BuildSentry(pose); break;
                    case GameVoxelRole.StackGuardian: BuildGuardian(pose); break;
                }
                var body = root.AddComponent<CharacterController>();
                body.radius = role == GameVoxelRole.StackGuardian ? .72f : .58f;
                body.height = role == GameVoxelRole.StackGuardian ? 2.65f : role == GameVoxelRole.BitSentry ? 1.72f : 1.65f;
                body.center = Vector3.up * (body.height * .5f);
                body.stepOffset = .18f;
                body.skinWidth = .035f;
                body.minMoveDistance = 0;
                body.slopeLimit = 45;
                var aim = Child(root.transform, "AimAnchor", Vector3.up * (body.height * .6f));
                var health = root.AddComponent<TrainingEnemy>();
                health.maxHealth = role == GameVoxelRole.StackGuardian ? 150 : role == GameVoxelRole.BitSentry ? 58 : 70;
                health.respawnOnDeath = false;
                health.deferDeathVisuals = true;
                health.aimAnchor = aim;
                health.visibleRenderers = root.GetComponentsInChildren<Renderer>();
                root.AddComponent<GameVoxelMonster>().role = role;
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + role + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static void BuildMaw(Transform pose)
        {
            var body = new Voxels(.18f);
            body.Box(-3, 3, 3, 7, -3, 3, 3);
            body.Box(-4, 4, 4, 6, -2, 2, 4);
            body.Box(-3, 3, 7, 8, -2, 1, 4);
            body.Box(-2, 2, 8, 8, -1, 1, 5);
            // The mouth is a real open horizontal gap between upper shell and independently hinged jaw.
            body.Box(-3, 3, 3, 4, -2, 0, 0);
            body.Box(-3, 3, 5, 6, 4, 4, 0);
            body.Box(-3, -2, 6, 6, 4, 4, 10);
            body.Box(2, 3, 6, 6, 4, 4, 10);
            body.Box(-4, -4, 3, 5, -2, 1, 1);
            body.Box(4, 4, 3, 5, -2, 1, 1);
            for (int x = -3; x <= 3; x += 2) body.Box(x, x, 4, 4, 3, 3, 2);
            // Stepped rear exhaust and a top slot stay visible from the gameplay camera.
            body.Box(-2, 2, 4, 5, -4, -4, 1);
            body.Box(-1, 1, 5, 5, -4, -4, 11);
            body.Box(0, 0, 8, 8, -2, 1, 0);
            Part(pose, "Model", "PixelMawBody", body, Vector3.zero);
            var jaw = new Voxels(.18f);
            jaw.Box(-3, 3, -1, 0, 0, 6, 3);
            jaw.Box(-4, 4, -1, -1, 1, 5, 4);
            jaw.Box(-3, 3, 0, 0, 5, 6, 5);
            for (int x = -2; x <= 2; x += 2) jaw.Box(x, x, 1, 1, 5, 5, 2);
            Part(pose, "Jaw", "PixelMawJaw", jaw, new Vector3(0, .44f, -.55f));
            for (int side = -1; side <= 1; side += 2)
            {
                var tread = new Voxels(.18f);
                tread.Box(-1, 1, 0, 1, -3, 2, 0);
                tread.Box(-1, 1, 2, 2, -2, 1, 1);
                for (int z = -2; z <= 2; z += 2) tread.Box(side, side, 1, 1, z, z, 5);
                Part(pose, side < 0 ? "LeftLimb" : "RightLimb", "PixelMawTread" + side, tread, new Vector3(side * .66f, .1f, -.08f));
            }
        }

        static void BuildSentry(Transform pose)
        {
            var body = new Voxels(.16f);
            body.Box(-3, 3, 3, 6, -2, 2, 6);
            body.Box(-2, 2, 7, 8, -2, 1, 7);
            body.Box(-4, 4, 4, 5, -1, 1, 7);
            body.Box(-3, 3, 4, 5, 3, 3, 0);
            body.Box(-2, -1, 5, 5, 3, 3, 10);
            body.Box(1, 2, 5, 5, 3, 3, 10);
            body.Box(0, 0, 3, 3, 3, 3, 2);
            body.Box(-2, 2, 4, 5, -3, -3, 1);
            body.Box(-1, 1, 5, 5, -3, -3, 10);
            body.Box(-2, 2, 2, 2, -1, 1, 0);
            // Two broad feet anchor the invader-like zigzag silhouette to the ground.
            for (int side = -1; side <= 1; side += 2)
            {
                body.Box(side < 0 ? -4 : 2, side < 0 ? -2 : 4, 1, 1, -1, 1, 1);
                body.Box(side < 0 ? -5 : 3, side < 0 ? -3 : 5, 0, 0, -1, 2, 6);
            }
            Part(pose, "Model", "BitSentryBody", body, Vector3.up * .08f);
            var crown = new Voxels(.16f);
            crown.Box(-3, -3, 0, 1, 0, 0, 7);
            crown.Box(3, 3, 0, 1, 0, 0, 7);
            crown.Box(-4, -3, 2, 2, 0, 0, 10);
            crown.Box(3, 4, 2, 2, 0, 0, 10);
            Part(pose, "Crown", "BitSentryCrown", crown, new Vector3(0, 1.34f, -.16f));
            for (int side = -1; side <= 1; side += 2)
            {
                var claw = new Voxels(.16f);
                claw.Box(-1, 1, -1, 1, -1, 3, 6);
                claw.Box(-1, 1, -1, 1, 2, 3, 7);
                claw.Box(0, 0, 0, 0, 4, 4, 10);
                claw.Box(-1, -1, 0, 0, 4, 4, 0);
                claw.Box(1, 1, 0, 0, 4, 4, 0);
                Part(pose, side < 0 ? "LeftLimb" : "RightLimb", "BitSentryClaw" + side, claw, new Vector3(side * .78f, .78f, 0));
            }
        }

        static void BuildGuardian(Transform pose)
        {
            var body = new Voxels(.2f);
            // Offset stacked blocks, with dark joint gaps and bevel-like stepped corners.
            body.Box(-2, 2, 3, 5, -2, 2, 0);
            body.Box(-3, 2, 5, 8, -2, 2, 8);
            body.Box(-2, 1, 6, 8, 3, 3, 9);
            body.Box(-2, 1, 5, 5, 3, 3, 14);
            body.Box(-1, 0, 6, 7, 4, 4, 0);
            body.Box(-1, 0, 7, 7, 4, 4, 11);
            body.Box(-2, 1, 9, 9, -1, 1, 0);
            body.Box(-1, 2, 10, 12, -1, 1, 8);
            body.Box(-1, 2, 10, 11, 2, 2, 0);
            body.Box(0, 1, 11, 11, 2, 2, 11);
            body.Box(-1, 2, 12, 12, 1, 1, 14);
            body.Box(0, 2, 12, 12, -1, 0, 9);
            // Each leg is a shifted block chain, so the silhouette is recognisable from the top-down camera.
            body.Box(-3, -1, 1, 3, -1, 1, 1);
            body.Box(1, 3, 1, 3, -1, 1, 1);
            body.Box(-4, -1, 0, 1, -1, 3, 8);
            body.Box(1, 4, 0, 1, -1, 3, 8);
            body.Box(-4, -1, 1, 1, 3, 3, 9);
            body.Box(1, 4, 1, 1, 3, 3, 9);
            Part(pose, "Model", "StackGuardianBody", body, Vector3.up * .1f);
            var arm = new Voxels(.2f);
            arm.Box(-1, 1, -3, 0, -1, 1, 8);
            arm.Box(-1, 1, -4, -3, 0, 2, 1);
            arm.Box(-1, 1, -5, -4, 0, 2, 9);
            arm.Box(0, 0, -4, -4, 3, 3, 11);
            Part(pose, "LeftLimb", "StackGuardianBrace", arm, new Vector3(-.86f, 1.7f, 0));
            var hammer = new Voxels(.2f);
            hammer.Box(-1, 1, -3, 0, -1, 1, 8);
            hammer.Box(-1, 1, -4, -3, 0, 2, 1);
            hammer.Box(-2, 2, -6, -4, 0, 4, 8);
            hammer.Box(-2, 2, -4, -4, 1, 3, 9);
            hammer.Box(-2, 2, -6, -6, 1, 3, 14);
            hammer.Box(-2, 2, -5, -5, 4, 4, 11);
            Part(pose, "RightLimb", "StackGuardianHammer", hammer, new Vector3(.94f, 1.8f, .05f));
        }

        static Transform Child(Transform parent, string name, Vector3 position)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = position;
            return child;
        }

        static void Part(Transform parent, string name, string assetName, Voxels data, Vector3 position)
        {
            Transform part = Child(parent, name, position);
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = SaveMesh(data.Mesh(assetName), assetName);
            var renderer = part.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = palette;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = MeshFolder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!existing) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        [MenuItem("Tools/Game Theme/Validate Voxel Monsters")]
        public static void ValidateBuiltAssets()
        {
            var library = AssetDatabase.LoadAssetAtPath<GameVoxelMonsterLibrary>(LibraryPath);
            if (!library || !library.paletteMaterial || !library.projectileMesh) throw new InvalidOperationException("Game monster library is incomplete.");
            foreach (GameVoxelRole role in Enum.GetValues(typeof(GameVoxelRole)))
            {
                var prefab = library.Prefab(role);
                if (!prefab || !prefab.GetComponent<GameVoxelMonster>() || !prefab.GetComponent<TrainingEnemy>() || !prefab.GetComponent<CharacterController>())
                    throw new InvalidOperationException(role + " is missing combat components.");
                int triangles = 0;
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>()) triangles += filter.sharedMesh.triangles.Length / 3;
                if (triangles > 9000) throw new InvalidOperationException(role + " exceeded its 9,000 triangle ceiling: " + triangles);
                var renderers = prefab.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 6) throw new InvalidOperationException(role + " has excessive independent draw parts.");
                foreach (var renderer in renderers)
                    if (renderer.sharedMaterials.Length != 1 || renderer.sharedMaterial != library.paletteMaterial)
                        throw new InvalidOperationException(role + " is not sharing the palette.");
                if (!prefab.transform.Find("Visual/Pose") || !prefab.GetComponent<TrainingEnemy>().aimAnchor)
                    throw new InvalidOperationException(role + " is missing an animation or targeting pivot.");
                Debug.Log($"GAME_MONSTER_VALID: {role}, {triangles} triangles, {renderers.Length} renderers, one shared material.");
            }
            if (Mathf.Abs(GameVoxelMonster.SegmentDistance(new Vector3(3, 8, 2), Vector3.zero, Vector3.forward * 4) - 3) > .001f)
                throw new InvalidOperationException("Charge sweep must remain horizontal.");
        }

        sealed class Voxels
        {
            readonly Dictionary<Vector3Int, byte> cells = new Dictionary<Vector3Int, byte>();
            readonly float size;
            static readonly Vector3Int[] Neighbors = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back };
            static readonly Vector3[][] Corners =
            {
                new[] { new Vector3(1,-1,-1), new Vector3(1,1,-1), new Vector3(1,1,1), new Vector3(1,-1,1) },
                new[] { new Vector3(-1,-1,1), new Vector3(-1,1,1), new Vector3(-1,1,-1), new Vector3(-1,-1,-1) },
                new[] { new Vector3(-1,1,-1), new Vector3(-1,1,1), new Vector3(1,1,1), new Vector3(1,1,-1) },
                new[] { new Vector3(-1,-1,1), new Vector3(-1,-1,-1), new Vector3(1,-1,-1), new Vector3(1,-1,1) },
                new[] { new Vector3(1,-1,1), new Vector3(1,1,1), new Vector3(-1,1,1), new Vector3(-1,-1,1) },
                new[] { new Vector3(-1,-1,-1), new Vector3(-1,1,-1), new Vector3(1,1,-1), new Vector3(1,-1,-1) }
            };
            public Voxels(float cellSize) { size = cellSize; }
            public void Box(int xmin, int xmax, int ymin, int ymax, int zmin, int zmax, byte color)
            {
                for (int x = xmin; x <= xmax; x++)
                    for (int y = ymin; y <= ymax; y++)
                        for (int z = zmin; z <= zmax; z++) cells[new Vector3Int(x, y, z)] = color;
            }
            public Mesh Mesh(string name)
            {
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();
                var uv = new List<Vector2>();
                var triangles = new List<int>();
                foreach (var pair in cells)
                    for (int face = 0; face < 6; face++)
                    {
                        if (cells.ContainsKey(pair.Key + Neighbors[face])) continue;
                        int first = vertices.Count;
                        foreach (Vector3 corner in Corners[face])
                        {
                            vertices.Add((Vector3)pair.Key * size + corner * (size * .5f));
                            normals.Add((Vector3)Neighbors[face]);
                            uv.Add(new Vector2((pair.Value + .5f) / 16f, .5f));
                        }
                        triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                        triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
                    }
                var mesh = new Mesh { name = name };
                if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
#endif
