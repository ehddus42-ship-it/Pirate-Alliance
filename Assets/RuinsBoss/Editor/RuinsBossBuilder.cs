using System;
using System.IO;
using System.Linq;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AcRoguelike.RuinsBoss.Editor
{
    /// <summary>Repeatable assembly of the reviewed Meshy encounter, without rebuilding ordinary ruins rooms.</summary>
    public static class RuinsBossBuilder
    {
        public const string Root = "Assets/RuinsBoss";
        public const string Prefabs = Root + "/Resources/RuinsBoss";
        public const string ArenaPath = Root + "/Prefabs/Ruins_08_Boss.prefab";
        const string Generated = Root + "/Art/Generated";
        public static string Source(string key) => Root + "/Art/Meshy/" + key + "/" + key;

        [MenuItem("AC Roguelike/Ruins/Build Apocalypse Boss")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before authoring.");
            foreach (string path in new[] { Prefabs, Generated, Path.GetDirectoryName(ArenaPath) }) Directory.CreateDirectory(path);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string key in new[] { "storm_sovereign", "iron_ram", "siege_walker", "missile_turret", "apocalypse_missile" }) Require(Source(key) + ".glb");
            ConfigureHumanoid();
            BuildActor("storm_sovereign", "StormSovereign", RuinsBossVisualKind.StormSovereign, 4.6f);
            BuildActor("iron_ram", "IronRam", RuinsBossVisualKind.IronRam, 3.8f);
            BuildActor("siege_walker", "SiegeWalker", RuinsBossVisualKind.SiegeWalker, 4.3f);
            BuildTurret(); BuildMissile(); BuildArena(); ConnectStage();
            AssetDatabase.SaveAssets();
            Debug.Log("RUINS_BOSS_BUILD_OK: Meshy electrical sovereign, two war machines, rear artillery and missile integrated.");
        }

        public static void ConnectStage()
        {
            var stage = AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>("Assets/StageConcepts/Stages/Ruins.asset");
            var arena = AssetDatabase.LoadAssetAtPath<GameObject>(ArenaPath);
            if (!stage || !arena) throw new InvalidOperationException("Ruins stage/arena missing.");
            stage.endRoom = arena.GetComponent<LiminalRoom>();
            stage.subtitle = "폐허의 기계 군집을 돌파하고 폭풍의 군주를 쓰러뜨려라";
            EditorUtility.SetDirty(stage);
            var catalog = AssetDatabase.LoadAssetAtPath<GateMissionCatalog>("Assets/Liminal/Resources/GateMissions.asset");
            var mission = catalog ? catalog.Missions.FirstOrDefault(m => m != null && m.id == "ruins_clearance") : null;
            if (mission == null) throw new InvalidOperationException("G-04 mission missing.");
            mission.objective = "세 전투 구역 돌파 · 폭풍의 군주 격파 · 출구 도달";
            mission.gimmickName = "강화장갑 · 폭풍의 군주";
            mission.gimmickDescription = "적이 받는 피해 25% 감소. 전기 탄막 · 순서대로 깨어나는 전쟁 기계 · 3연발 낙하 미사일. 예고와 탄막 사이 빈틈을 보고 피하라.";
            EditorUtility.SetDirty(catalog);
        }

        static void ConfigureHumanoid()
        {
            foreach (var suffix in new[] { "_rig", "_motions" })
            {
                string path = Source("storm_sovereign") + suffix + ".fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (!importer) throw new FileNotFoundException("Boss rig/motions not ready: " + path);
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true; importer.importCameras = false; importer.importLights = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
            }
        }

        static void BuildActor(string key, string name, RuinsBossVisualKind kind, float height)
        {
            var root = new GameObject(name);
            try
            {
                bool boss = kind == RuinsBossVisualKind.StormSovereign;
                var body = root.AddComponent<CharacterController>();
                body.radius = boss ? .85f : 1.45f; body.height = boss ? 4.2f : 3.25f;
                body.center = Vector3.up * (body.height * .5f + .04f); body.skinWidth = .06f;
                body.minMoveDistance = 0; body.stepOffset = .25f; body.slopeLimit = 45;
                var pose = Child("Pose", Child("Visual", root.transform));
                var model = Child("MeshyModel", pose);
                AnimationClip idle = boss ? CopyClip("Idle", "Idle", true) : null;
                var instance = PlaceModel(Source(key) + (boss ? "_rig.fbx" : ".glb"), model, height, boss ? 8 : 6.6f, idle);
                if (kind == RuinsBossVisualKind.IronRam) model.localRotation = Quaternion.Euler(0, -90, 0);
                if (kind == RuinsBossVisualKind.SiegeWalker) RuinsWalkerRigBuilder.Attach(model);
                var rig = pose.gameObject.AddComponent<RuinsBossRig>();
                rig.kind = kind; rig.model = model;
                rig.glowMaterial = Material(boss ? "SovereignElectricity" : "MachinePower", boss ? new Color(.06f, .55f, 1) : new Color(1, .34f, .035f), true);
                if (boss)
                {
                    var source = Require(Source(key) + "_rig.glb");
                    var material = source.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).First(m => m);
                    var atlas = material.HasProperty("baseColorTexture") ? material.GetTexture("baseColorTexture") :
                        material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
                    if (!atlas) throw new InvalidOperationException("Sovereign Meshy atlas missing.");
                    var shader = Shader.Find("PirateAlliance/StormSovereignElectric");
                    if (!shader) throw new InvalidOperationException("Sovereign electrical shader missing.");
                    var electric = AssetDatabase.LoadAssetAtPath<Material>(Generated + "/SovereignBody.mat");
                    var template = new Material(shader) { name = "SovereignBody", renderQueue = 3000 };
                    // Start clean: the glTF source has opaque tags and differently named texture properties.
                    template.SetOverrideTag("RenderType", "Transparent");
                    template.SetTexture("_BaseMap", atlas);
                    template.SetColor("_BaseColor", Color.white);
                    template.SetFloat("_Opacity", .40f);
                    template.SetColor("_EmissionColor", new Color(1.8f, 3.5f, 4.5f));
                    template.SetFloat("_RimPower", 3.8f);
                    template.SetFloat("_CrackleStrength", 1.25f);
                    template.SetFloat("_ColorMask", 15);
                    template.SetFloat("_ZWrite", 0);
                    if (!electric) { electric = template; AssetDatabase.CreateAsset(electric, Generated + "/SovereignBody.mat"); }
                    else { EditorUtility.CopySerialized(template, electric); Object.DestroyImmediate(template); }
                    EditorUtility.SetDirty(electric);
                    var depth = SovereignDepthMaterial(electric);
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.sharedMaterials = Enumerable.Repeat(electric, Math.Max(1, renderer.sharedMaterials.Length)).ToArray();
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                        if (renderer is SkinnedMeshRenderer skin)
                        {
                            // Keep the nearest energy surface readable: rear hair must not blend over the face.
                            // This writes only depth; the visible surface still blends with the scene behind it.
                            var shell = Child("Energy silhouette depth", skin.transform).gameObject.AddComponent<SkinnedMeshRenderer>();
                            shell.sharedMesh = skin.sharedMesh; shell.bones = skin.bones; shell.rootBone = skin.rootBone;
                            shell.localBounds = skin.localBounds; shell.updateWhenOffscreen = skin.updateWhenOffscreen;
                            shell.sharedMaterials = Enumerable.Repeat(depth, Math.Max(1, skin.sharedMaterials.Length)).ToArray();
                            shell.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; shell.receiveShadows = false;
                            shell.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                            shell.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                        }
                    }
                    rig.lightningMaterial = LightningMaterial();
                    rig.humanoidAnimator = instance.GetComponentInChildren<Animator>() ?? instance.AddComponent<Animator>();
                    rig.humanoidAnimator.applyRootMotion = false; rig.humanoidAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    rig.idleClip = idle; rig.castClip = CopyClip("Cast", "Cast", false); rig.deathClip = CopyClip("Dead", "Death", false);
                }
                rig.RefreshRestPose();
                var health = root.AddComponent<TrainingEnemy>();
                health.maxHealth = boss ? 2000 : 450; health.respawnOnDeath = false; health.deferDeathVisuals = true;
                health.aimAnchor = Child("Aim", pose); health.aimAnchor.localPosition = Vector3.up * (boss ? 1.7f : 1.4f);
                health.visibleRenderers = root.GetComponentsInChildren<Renderer>(true);
                if (boss)
                {
                    var actor = root.AddComponent<RuinsStormBoss>(); actor.visualRig = rig; actor.displayName = "폭풍의 군주";
                    actor.muzzle = Child("ElectricMuzzle", root.transform); actor.muzzle.localPosition = Vector3.up * 1.05f;
                }
                else
                {
                    var actor = root.AddComponent<RuinsWarMachine>(); actor.visualRig = rig;
                    actor.kind = kind == RuinsBossVisualKind.IronRam ? RuinsWarMachineKind.IronRam : RuinsWarMachineKind.SiegeWalker;
                    actor.displayName = actor.kind == RuinsWarMachineKind.IronRam ? "철갑 파쇄기" : "공성 보행기";
                }
                PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/" + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void BuildTurret()
        {
            var root = new GameObject("MissileTurret");
            try
            {
                var model = Child("MeshyModel", root.transform);
                var mesh = PlaceModel(Source("missile_turret") + ".glb", model, 3.1f, 5.2f);
                // Reviewed barrel tips in normalized glTF coordinates, carried through the same scale/foot offset.
                foreach (int side in new[] { -1, 1 })
                {
                    var port = Child(side < 0 ? "LaunchPortA" : "LaunchPortB", model);
                    port.localPosition = mesh.transform.localPosition + Vector3.Scale(mesh.transform.localScale, new Vector3(side * .187f, -.044f, .84f));
                }
                var rig = root.AddComponent<RuinsBossRig>(); rig.kind = RuinsBossVisualKind.MissileTurret;
                rig.model = model; rig.glowMaterial = Material("MachinePower", new Color(1, .34f, .035f), true); rig.RefreshRestPose();
                var collider = root.AddComponent<BoxCollider>(); collider.center = Vector3.up * 1.3f; collider.size = new Vector3(2.6f, 2.6f, 2.4f);
                PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/MissileTurret.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void BuildMissile()
        {
            var root = new GameObject("MissileVisual");
            try
            {
                var model = PlaceModel(Source("apocalypse_missile") + ".glb", root.transform, 1, 100);
                var bounds = BoundsIn(root.transform);
                // Normalize the longest model axis to local +Z. The reviewed nose direction can be adjusted here.
                Vector3 size = bounds.size;
                if (size.y >= size.x && size.y >= size.z) model.transform.localRotation = Quaternion.Euler(90, 0, 0);
                else if (size.x >= size.z) model.transform.localRotation = Quaternion.Euler(0, -90, 0);
                bounds = BoundsIn(root.transform);
                float scale = .9f / Mathf.Max(.001f, bounds.size.z);
                model.transform.localScale *= scale; model.transform.localPosition *= scale;
                bounds = BoundsIn(root.transform); model.transform.localPosition -= bounds.center;
                PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/MissileVisual.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void BuildArena()
        {
            var root = new GameObject("Ruins_08_Boss");
            try
            {
                var room = root.AddComponent<LiminalRoom>();
                room.roomId = "Ruins_08_Boss"; room.displayName = "죽은 발전소"; room.kind = LiminalRoomKind.Boss;
                room.localBounds = new Bounds(new Vector3(0, 5, 21), new Vector3(36, 10, 42));
                room.designNotes = "G-04 finale: electric sovereign; dormant war machines on left/right; triple artillery in rear. The sovereign's defeat powers down the entire encounter.";
                var sockets = Child("Sockets", root.transform); var scenery = Child("Scenery", root.transform);
                room.entry = Marker("Entry", sockets, Vector3.zero);
                room.exit = Marker("Exit", sockets, new Vector3(0, 0, 42));
                room.playerSpawn = Marker("PlayerSpawn", sockets, new Vector3(0, .05f, 4));
                var arena = root.AddComponent<RuinsBossArena>();
                arena.bossSpawn = Marker("Boss", sockets, new Vector3(0, .05f, 25), 180);
                arena.leftSpawn = Marker("Left machine", sockets, new Vector3(-9, .05f, 23), 165);
                arena.rightSpawn = Marker("Right machine", sockets, new Vector3(9, .05f, 23), 195);
                arena.turretSpawn = Marker("Rear artillery", sockets, new Vector3(0, .05f, 35), 180);
                room.enemySpawns = new[] { arena.bossSpawn };
                var concrete = Existing("Concrete"); var dark = Existing("ConcreteDark"); var steel = Existing("RustedSteel");
                var soot = Existing("Soot"); var paint = Existing("FadedRoadPaint");
                var glow = Material("SovereignElectricity", new Color(.06f, .55f, 1), true);
                Box("Foundation", scenery, new Vector3(0, -.25f, 21), new Vector3(36, .5f, 42), concrete, true);
                for (int x = -15; x <= 15; x += 6)
                    Box("Concrete joint", scenery, new Vector3(x, .008f, 21), new Vector3(.055f, .015f, 41.8f), soot);
                for (int z = 3; z < 42; z += 6)
                    Box("Concrete joint", scenery, new Vector3(0, .008f, z), new Vector3(35.8f, .015f, .055f), soot);
                foreach (int side in new[] { -1, 1 })
                {
                    Box("Broken retaining wall", scenery, new Vector3(side * 18.25f, .6f, 21), new Vector3(.5f, 1.2f, 42.5f), dark);
                    Solid("Arena boundary", scenery, new Vector3(side * 18.25f, 3, 21), new Vector3(.5f, 6, 42.5f));
                    for (int z = 6; z < 41; z += 8)
                    {
                        Box("Rusted reinforcement", scenery, new Vector3(side * 18.1f, 1.2f, z), new Vector3(.7f, 2.4f, .5f), steel);
                        PlaceScenery("rubble_pile", scenery, new Vector3(side * 20, 0, z), 1.9f, side * 38 + z);
                    }
                    PlaceScenery("ruined_tower", scenery, new Vector3(side * 23, 0, 32), 11f, side * 65);
                    PlaceScenery("wrecked_vehicle", scenery, new Vector3(side * 21, 0, 15), 2.7f, side * 80);
                    Box("Machine bay", scenery, new Vector3(side * 9, .018f, 23), new Vector3(6.8f, .028f, 7.3f), soot);
                    for (int i = -3; i <= 3; i++)
                    {
                        var stripe = Box("Faded hazard stripe", scenery, new Vector3(side * 9 + i * .8f, .037f, 18.9f), new Vector3(.3f, .018f, .6f), paint);
                        stripe.localRotation = Quaternion.Euler(0, 30, 0);
                    }
                }
                foreach (float z in new[] { -.25f, 42.25f })
                    foreach (int side in new[] { -1, 1 })
                    {
                        Box("Gate wing", scenery, new Vector3(side * 10.7f, .6f, z), new Vector3(14.6f, 1.2f, .5f), dark);
                        Solid("Gate wing collision", scenery, new Vector3(side * 10.7f, 3, z), new Vector3(14.6f, 6, .5f));
                    }
                // The clear center gives diagonal dodges and the two charging machines space to pass.
                room.entranceGate = Gate("EntranceGate", sockets, 0, glow);
                room.exitGate = Gate("ExitGate", sockets, 42, glow); room.SetGates(false, false);
                PrefabUtility.SaveAsPrefabAsset(root, ArenaPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void PlaceScenery(string key, Transform parent, Vector3 position, float height, float yaw)
        {
            string path = "Assets/StageConcepts/Art/Meshy/" + key + "/" + key + ".glb";
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) return;
            PlaceModel(path, Marker(key, parent, position, yaw), height, 16);
        }
        public static Bounds BoundsIn(Transform parent)
        {
            Bounds bounds = default; bool found = false;
            foreach (var renderer in parent.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                var b = renderer.localBounds;
                if (renderer is SkinnedMeshRenderer skin)
                { var baked = new Mesh(); skin.BakeMesh(baked, true); b = baked.bounds; Object.DestroyImmediate(baked); }
                var matrix = parent.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; } else bounds.Encapsulate(p);
                }
            }
            if (!found) throw new InvalidOperationException("Model has no geometry: " + parent.name);
            return bounds;
        }
        static GameObject PlaceModel(string path, Transform parent, float height, float maximumWidth = 100, AnimationClip idle = null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Require(path), parent);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            if (idle) idle.SampleAnimation(go, .2f);
            var bounds = BoundsIn(parent);
            float scale = Mathf.Min(height / Mathf.Max(.001f, bounds.size.y), maximumWidth / Mathf.Max(bounds.size.x, bounds.size.z));
            go.transform.localScale = Vector3.one * scale;
            go.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
            foreach (var collider in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            return go;
        }
        static AnimationClip CopyClip(string match, string role, bool loop)
        {
            string source = Source("storm_sovereign") + "_motions.fbx";
            var clips = AssetDatabase.LoadAllAssetsAtPath(source).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            var original = clips.FirstOrDefault(c => c.name.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!original) throw new InvalidOperationException("Missing " + role + ": " + string.Join(", ", clips.Select(c => c.name)));
            string path = Generated + "/Sovereign_" + role + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (!clip) { clip = Object.Instantiate(original); AssetDatabase.CreateAsset(clip, path); }
            else EditorUtility.CopySerialized(original, clip);
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = loop; settings.loopBlend = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings); EditorUtility.SetDirty(clip); return clip;
        }
        static GameObject Require(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path) ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : throw new FileNotFoundException(path);
        static Material Existing(string key) => AssetDatabase.LoadAssetAtPath<Material>("Assets/StageConcepts/Materials/Ruins_" + key + ".mat");
        static Material Material(string name, Color color, bool emission)
        {
            string path = Generated + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", color); mat.SetFloat("_Metallic", .5f); mat.SetFloat("_Smoothness", .3f);
            if (emission) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", color * 2); }
            EditorUtility.SetDirty(mat); return mat;
        }
        static Material LightningMaterial()
        {
            string path = Generated + "/SovereignLightning.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(HitFeedback.Additive); AssetDatabase.CreateAsset(material, path); }
            else EditorUtility.CopySerialized(HitFeedback.Additive, material);
            material.name = "SovereignLightning";
            EditorUtility.SetDirty(material);
            return material;
        }
        static Material SovereignDepthMaterial(Material body)
        {
            string path = Generated + "/SovereignDepth.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(body); AssetDatabase.CreateAsset(material, path); }
            else EditorUtility.CopySerialized(body, material);
            material.name = "SovereignDepth";
            material.renderQueue = 2999;
            material.SetFloat("_ColorMask", 0); material.SetFloat("_ZWrite", 1);
            EditorUtility.SetDirty(material);
            return material;
        }
        static Transform Child(string name, Transform parent) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
        static Transform Marker(string name, Transform parent, Vector3 position, float yaw = 0)
        { var t = Child(name, parent); t.localPosition = position; t.localRotation = Quaternion.Euler(0, yaw, 0); return t; }
        static Transform Box(string name, Transform parent, Vector3 center, Vector3 size, Material mat, bool solid = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = center; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!solid) Object.DestroyImmediate(go.GetComponent<Collider>()); return go.transform;
        }
        static void Solid(string name, Transform parent, Vector3 center, Vector3 size)
        { var go = Child(name, parent); go.localPosition = center; go.gameObject.AddComponent<BoxCollider>().size = size; }
        static GameObject Gate(string name, Transform parent, float z, Material material)
        {
            var gate = Marker(name, parent, new Vector3(0, 0, z));
            var collider = gate.gameObject.AddComponent<BoxCollider>(); collider.center = new Vector3(0, 2.5f, 0); collider.size = new Vector3(6.8f, 5, .4f);
            for (int i = -3; i <= 3; i++) Box("Electrical seal", gate, new Vector3(i, 1.5f, 0), new Vector3(.055f, 3, .055f), material);
            return gate.gameObject;
        }
    }
}
