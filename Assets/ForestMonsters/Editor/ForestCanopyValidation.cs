#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.Forest.Editor
{
    /// <summary>Opt-in real PlayMode actions in a temporary scene. Does not alter authored scenes or saves.</summary>
    public static class ForestCanopyValidation
    {
        public static IEnumerator Run(Action<string> record)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Forest canopy validation requires PlayMode.");
            using (var arena = new Arena())
            {
                arena.PlacePlayer(new Vector3(0, .04f, 6));
                var wall = arena.Box("Thin wind/root wall", new Vector3(0, 1.5f, 2.5f), new Vector3(8, 3, .025f));
                var wind = ForestWindProjectile.Fire(arena.Point(Vector3.zero), Vector3.forward, arena.Owner, arena.Player, arena.Room);
                var root = ForestRootWave.Fire(arena.Point(Vector3.zero), Vector3.forward, arena.Owner, arena.Player, arena.Room);
                Require(wind.GetComponentsInChildren<MeshRenderer>().Length >= 17 && root.ArchCount == 4, "Wind must have layered ribbons/leaves and roots four physical arches.");
                var limit = new Deadline(3, "wind/root wall"); int health = arena.Player.Health;
                while ((wind && !wind.Finished) || (root && !root.Finished)) { limit.Check(); arena.Step(); yield return null; }
                Require(wind.Finished && root.Finished && wind.DamageAttempts == 0 && root.DamageAttempts == 0 && arena.Player.Health == health,
                    "Thin walls must cancel wind and roots before player damage.");
                Object.DestroyImmediate(wall);
                record?.Invoke("Butterfly cyclone has three animated ribbons, six wisps and leaf motes; four root arches and both attacks stop at a 2.5 cm wall.");

                arena.PlacePlayer(new Vector3(0, .04f, 3));
                root = ForestRootWave.Fire(arena.Point(Vector3.zero), Vector3.forward, arena.Owner, arena.Player, arena.Room);
                limit = new Deadline(4, "root single hit"); bool surfaced = false;
                while (root && !root.Finished) { surfaced |= root.AboveGround; limit.Check(); arena.Step(); yield return null; }
                HitFeedback.CancelHitStop();
                Require(surfaced && root.DamageAttempts == 1 && root.HitPlayer && arena.Player.Health == health - 21,
                    "Surfacing root train must deal one 21-damage hit, not one per trailing arch.");
                record?.Invoke("Root arches repeatedly emerge/submerge; a stationary player receives exactly one 21-damage hit.");

                arena.PlacePlayer(new Vector3(15, .04f, 15));
                wind = ForestWindProjectile.Fire(arena.Point(new Vector3(-2.5f, 0, 0)), Vector3.forward, arena.Owner, arena.Player, arena.Room);
                root = ForestRootWave.Fire(arena.Point(new Vector3(2.5f, 0, 0)), Vector3.forward, arena.Owner, arena.Player, arena.Room);
                float captureAt = Time.time + .85f;
                while (Time.time < captureAt) { arena.Step(); yield return null; }
                ForestMonsterValidation.CaptureAttack(arena.Point(new Vector3(0, .5f, 3.5f)), "canopy_wind_and_root_arches");
                wind.Cancel(); root.Cancel();

                arena.PlacePlayer(new Vector3(0, .04f, 0));
                arena.Motor.enabled = true; arena.Motor.ResetDashCooldown();
                arena.Motor.SetAutomationInput(Vector2.zero, arena.Point(Vector3.forward * 8), true);
                limit = new Deadline(1, "actual wind dodge");
                while (!arena.Motor.IsDashing) { limit.Check(); arena.Step(); yield return null; }
                int dodges = arena.Player.JustDodges; health = arena.Player.Health;
                wind = ForestWindProjectile.Fire(arena.Player.transform.position + Vector3.forward * .15f, Vector3.back, arena.Owner, arena.Player, arena.Room);
                limit = new Deadline(1, "wind dodge contact");
                while (wind && !wind.Finished) { limit.Check(); arena.Step(); yield return null; }
                Require(wind.DamageAttempts == 1 && !wind.HitPlayer && arena.Player.Health == health && arena.Player.JustDodges == dodges + 1,
                    "Actual PlayerMotor dash must avoid wind and register one just dodge.");
                arena.Motor.enabled = false;
                record?.Invoke("Real PlayerMotor dash avoids a cyclone contact and registers one just dodge.");

                arena.PlacePlayer(new Vector3(15, .04f, 15));
                wind = ForestWindProjectile.Fire(arena.Point(Vector3.zero), Vector3.forward, arena.Owner, arena.Player, arena.Room);
                root = ForestRootWave.Fire(arena.Point(Vector3.zero), Vector3.right, arena.Owner, arena.Player, arena.Room);
                var sapling = ForestSapling.Throw(arena.Point(Vector3.up * 2), arena.Point(new Vector3(0, 0, 3)), arena.Owner, arena.Player, arena.Room, 0);
                Require(sapling, "Sapling prefab must load.");
                Vector3 start = wind.transform.position; float rootStart = root.Travelled, saplingStart = sapling.StateTime;
                float scale = Time.timeScale; Time.timeScale = 0;
                try
                {
                    for (int i = 0; i < 4; i++) yield return null;
                    Require(wind.transform.position == start && root.Travelled == rootStart && sapling.StateTime == saplingStart,
                        "Pause must freeze wind, roots and sapling flight.");
                }
                finally { Time.timeScale = scale; }
                arena.Owner.TakeDamage(arena.Owner.maxHealth);
                Require(wind.Finished && root.Finished && sapling.Finished && !wind.gameObject.activeSelf && !sapling.gameObject.activeSelf,
                    "Owner death must immediately disable wind/root/sapling hazards.");
                record?.Invoke("Time scale zero freezes all attack actors; owner defeat immediately removes wind, root and sapling hazards.");
            }

            using (var arena = new Arena())
            {
                arena.PlacePlayer(new Vector3(0, .04f, 3));
                var sapling = ForestSapling.Throw(arena.Point(Vector3.up * 2), arena.Point(new Vector3(0, 0, 2)), arena.Owner, arena.Player, arena.Room, 0);
                var limit = new Deadline(8, "sapling delayed chase/explosion"); int health = arena.Player.Health;
                bool dormant = false, chasing = false, warning = false; float landedAt = -1;
                while (sapling && !sapling.Finished)
                {
                    if (sapling.State == ForestSaplingState.Dormant && !dormant) { dormant = true; landedAt = Time.time; }
                    if (sapling.State == ForestSaplingState.Chasing)
                    {
                        Require(dormant && Time.time - landedAt >= sapling.armDelay - .12f, "Saplings must wait before chasing."); chasing = true;
                    }
                    if (!warning && sapling.State == ForestSaplingState.Exploding && sapling.StateTime > .3f)
                    {
                        warning = true; ForestMonsterValidation.CaptureAttack(sapling.transform.position, "canopy_sapling_explosion_warning");
                    }
                    limit.Check(); arena.Step(); yield return null;
                }
                HitFeedback.CancelHitStop();
                Require(dormant && chasing && warning && sapling.Exploded && sapling.DamageAttempts == 1 && arena.Player.Health == health - 23,
                    "A thrown sapling must wait, chase, warn then explode once.");
                record?.Invoke("Sapling follows its arc, waits 2.6 s, chases, warns for 0.8 s and deals one 23-damage explosion.");

                sapling = ForestSapling.Throw(arena.Point(Vector3.up * 2), arena.Point(new Vector3(0, 0, 2)), arena.Owner, arena.Player, arena.Room, 0);
                limit = new Deadline(2, "destroyable sapling landing");
                while (sapling.State == ForestSaplingState.Thrown) { limit.Check(); arena.Step(); yield return null; }
                health = arena.Player.Health;
                Require(sapling.Health.CanBeTargeted && !sapling.Health.IgnoreDamage, "Landed saplings must accept player weapon damage.");
                sapling.Health.TakeDamage(100);
                Require(sapling.Finished && sapling.DestroyedByPlayer && !sapling.Exploded && sapling.DamageAttempts == 0 && arena.Player.Health == health,
                    "Destroying a sapling must cancel its explosion.");
                record?.Invoke("Landed sapling receives TrainingEnemy weapon damage; destruction cancels explosion and removes its collider immediately.");

                var lowCover = arena.Box("Low cover under landing", new Vector3(0, .5f, 2), new Vector3(2, 1, 2));
                sapling = ForestSapling.Throw(arena.Point(Vector3.up * 2), arena.Point(new Vector3(0, 0, 2)), arena.Owner, arena.Player, arena.Room, 0);
                limit = new Deadline(2, "sapling low cover landing");
                while (sapling.State == ForestSaplingState.Thrown) { limit.Check(); arena.Step(); yield return null; }
                Require(sapling.transform.position.y >= .99f, "Descending sapling must land on top of low cover, never inside it. Actual=" +
                    (sapling.transform.position - arena.Point(Vector3.zero)) + "; support=" + sapling.LandingCollider + "; point=" +
                    sapling.LandingPoint + "; normal=" + sapling.LandingNormal);
                sapling.Cancel(); Object.DestroyImmediate(lowCover);
                record?.Invoke("Sapling flight catches upward cover faces and lands on top of a 1 m obstacle instead of inside it.");
            }

            using (var arena = new Arena())
            {
                arena.PlacePlayer(new Vector3(0, .04f, 3));
                var butterfly = ForestMonsterFactory.Create(ForestMonsterKind.LunarButterfly, arena.Point(Vector3.zero), Quaternion.identity, arena.Room.transform) as ForestButterfly;
                var tree = ForestMonsterFactory.Create(ForestMonsterKind.Elderwood, arena.Point(new Vector3(5, 0, 0)), Quaternion.identity, arena.Room.transform) as ForestTreant;
                Require(butterfly && tree, "Factory must provide butterfly and elderwood behavior components.");
                butterfly.Setup(arena.Player, arena.Room, 0); tree.Setup(arena.Player, arena.Room, 0);
                var rig = butterfly.GetComponentInChildren<ForestButterflyWingRig>();
                Require(rig && rig.AnimatedVertexCount > 100, "Butterfly must deform actual Meshy wing geometry.");
                float firstFlap = rig.FlapDegrees; bool sawPollenWarning = false, sawWindWarning = false, pollenCaptured = false;
                var limit = new Deadline(16, "natural butterfly/treant FSM");
                while (butterfly.WindShots == 0 || tree.RootWaves == 0)
                {
                    sawPollenWarning |= butterfly.State == ForestButterflyState.PollenWindup && butterfly.TelegraphVisible;
                    sawWindWarning |= butterfly.State == ForestButterflyState.WindWindup && butterfly.TelegraphVisible;
                    if (!pollenCaptured && butterfly.State == ForestButterflyState.PollenBurst && butterfly.StateTime > .08f)
                    {
                        pollenCaptured = true; ForestMonsterValidation.CaptureAttack(butterfly.transform.position + Vector3.up * .6f, "canopy_butterfly_pollen_burst");
                    }
                    limit.Check(); arena.Step(); yield return null;
                }
                Require(sawPollenWarning && sawWindWarning && butterfly.PollenBursts >= 1 && butterfly.DamageAttempts == 1 && Mathf.Abs(rig.FlapDegrees - firstFlap) > .01f,
                    "Butterfly must show both warnings, animate its wings and use one pollen damage attempt per burst.");
                Require(tree.SaplingsThrown >= 1 && tree.RootWaves >= 1, "Walking tree must naturally throw a sapling and send a root wave.");
                butterfly.Health.TakeDamage(butterfly.Health.maxHealth); tree.Health.TakeDamage(tree.Health.maxHealth);
                arena.Step(); yield return null;
                Require(tree.ActiveSaplings == 0 && butterfly.State == ForestButterflyState.Dead && tree.State == ForestTreantState.Dead,
                    "Monster death must cancel owned hazards and stop the action state machines.");
                record?.Invoke("Natural butterfly FSM shows pollen/wind warnings and actual mesh wing flaps; elderwood throws Meshy saplings then roots; deaths clean up all summons.");
            }
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        struct Deadline
        {
            readonly float gameEnd, realEnd; readonly string label;
            public Deadline(float seconds, string label) { gameEnd = Time.time + seconds; realEnd = Time.realtimeSinceStartup + seconds * 5 + 4; this.label = label; }
            public void Check() { if (Time.time > gameEnd || Time.realtimeSinceStartup > realEnd) throw new TimeoutException("Forest canopy: " + label); }
        }
        sealed class Arena : IDisposable
        {
            static readonly Vector3 Offset = new Vector3(1200, 0, 1200);
            readonly Scene scene; readonly PhysicsScene physics; readonly GameObject root; readonly CharacterController playerBody;
            readonly List<Material> materials = new List<Material>();
            public LiminalRoom Room { get; }
            public TrainingEnemy Owner { get; }
            public LiminalPlayerHealth Player { get; }
            public PlayerMotor Motor { get; }
            public Vector3 Point(Vector3 local) => Offset + local;
            public Arena()
            {
                scene = SceneManager.CreateScene("Forest canopy validation " + Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                physics = scene.GetPhysicsScene(); root = new GameObject("Temporary forest canopy arena");
                SceneManager.MoveGameObjectToScene(root, scene); root.transform.position = Offset;
                Room = root.AddComponent<LiminalRoom>(); Room.localBounds = new Bounds(Vector3.up * 3, new Vector3(70, 8, 70));
                Box("Floor", Vector3.down * .15f, new Vector3(70, .3f, 70));
                var owner = Child("Owner"); owner.transform.localPosition = new Vector3(-25, 0, -25); Owner = owner.AddComponent<TrainingEnemy>(); Owner.Configure(1000, false);
                var player = Child("Target"); playerBody = player.AddComponent<CharacterController>(); playerBody.height = 1.8f; playerBody.radius = .32f; playerBody.center = Vector3.up * .9f;
                Motor = player.AddComponent<PlayerMotor>(); Motor.visual = new GameObject("Target visual").transform; Motor.visual.SetParent(player.transform, false); Motor.enabled = false;
                Player = player.AddComponent<LiminalPlayerHealth>(); Player.SetMaximum(1000, true);
                var feedback = player.GetComponent<JustDodgeFeedback>(); if (feedback) Object.DestroyImmediate(feedback);
                Physics.SyncTransforms();
            }
            GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(root.transform, false); return go; }
            public GameObject Box(string name, Vector3 position, Vector3 size)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(root.transform, false);
                go.transform.position = Point(position); go.transform.localScale = size;
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                var material = new Material(shader); material.SetColor("_BaseColor", name == "Floor" ? new Color(.13f, .19f, .14f) : new Color(.3f, .33f, .26f));
                go.GetComponent<Renderer>().sharedMaterial = material; materials.Add(material);
                Physics.SyncTransforms(); return go;
            }
            public void PlacePlayer(Vector3 position) { playerBody.enabled = false; Player.transform.position = Point(position); playerBody.enabled = true; Physics.SyncTransforms(); }
            public void Step() { Physics.SyncTransforms(); if (Time.deltaTime > 0) physics.Simulate(Time.deltaTime); }
            public void Dispose()
            {
                HitFeedback.CancelHitStop(); if (root) Object.DestroyImmediate(root);
                foreach (var material in materials) if (material) Object.DestroyImmediate(material);
                if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            }
        }
    }
}
#endif
