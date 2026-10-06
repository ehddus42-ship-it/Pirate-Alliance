#if UNITY_EDITOR
using System;
using System.Collections;
using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.RuinsBoss.Editor
{
    public static class RuinsBossProjectileValidation
    {
        public static IEnumerator Run(Action<string> record)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Ruins boss projectile validation requires PlayMode.");
            using (var arena = new Arena())
            {
                arena.PlacePlayer(new Vector3(0, .05f, 6));
                var wall = arena.Box("Thin wall", new Vector3(0, 1.5f, 3), new Vector3(8, 3, .025f));
                int health = arena.Player.Health;
                for (int type = 0; type < 2; type++)
                {
                    var shot = type == 0
                        ? RuinsBossProjectile.FireElectric(new Vector3(0, .9f, 0), Vector3.forward, arena.Owner, arena.Player, arena.Room, 9, 13)
                        : RuinsBossProjectile.FireHoming(new Vector3(0, .9f, 0), Vector3.forward, arena.Owner, arena.Player, arena.Room, 9, 13);
                    var deadline = new Deadline(2, "thin wall");
                    while (shot && !shot.Finished)
                    {
                        Require(shot.transform.position.z < 3, "A boss projectile crossed a 2.5 cm wall.");
                        deadline.Check(); arena.Step(); yield return null;
                    }
                    Require(shot.Finished && shot.DamageAttempts == 0 && arena.Player.Health == health,
                        "Thin scenery must stop electric and guided projectiles before player contact.");
                    shot = type == 0
                        ? RuinsBossProjectile.FireElectric(new Vector3(0, .9f, 3), Vector3.forward, arena.Owner, arena.Player, arena.Room, 9)
                        : RuinsBossProjectile.FireHoming(new Vector3(0, .9f, 3), Vector3.forward, arena.Owner, arena.Player, arena.Room, 9);
                    deadline = new Deadline(1, "muzzle inside wall");
                    while (shot && !shot.Finished) { deadline.Check(); arena.Step(); yield return null; }
                    Require(shot.Finished && shot.DamageAttempts == 0, "A muzzle inside a wall must not escape through it.");
                }
                Object.DestroyImmediate(wall);
                record?.Invoke("Electric and guided missiles use continuous sweeps: thin walls and initial wall overlap block both.");

                arena.PlacePlayer(new Vector3(20, .05f, 20));
                var fast = RuinsBossProjectile.FireElectric(new Vector3(-15, 1, -15), Vector3.forward,
                    arena.Owner, arena.Player, arena.Room, 9, 200);
                var rangeDeadline = new Deadline(2, "high speed range expiry");
                while (fast && !fast.Finished) { rangeDeadline.Check(); arena.Step(); yield return null; }
                Require(fast.Finished && Mathf.Abs(fast.Travelled - 28) < .01f && fast.DamageAttempts == 0,
                    "A fast projectile must stop exactly at its lifetime range without a negative-distance follow-up sweep.");
                record?.Invoke("A 200 m/s shot still expires at 28 m without overshoot or a second sweep beyond its range.");

                arena.PlacePlayer(new Vector3(15, .05f, 20));
                var missile = RuinsBossProjectile.FireHoming(new Vector3(0, .9f, 0), Vector3.forward,
                    arena.Owner, arena.Player, arena.Room, 9);
                var timeout = new Deadline(3, "gentle guidance");
                while (missile && missile.Age < 1.0f) { timeout.Check(); arena.Step(); yield return null; }
                Require(missile && !missile.Finished && Vector3.Angle(Vector3.forward, missile.Direction) > 2
                    && Vector3.Angle(Vector3.forward, missile.Direction) <= 9.1f,
                    "Guidance must visibly turn, but cannot exceed ten degrees/sec for its first .9 seconds.");
                Vector3 locked = missile.Direction;
                arena.PlacePlayer(new Vector3(-15, .05f, 20));
                while (missile && missile.Age < 1.4f) { timeout.Check(); arena.Step(); yield return null; }
                Require(missile && Vector3.Angle(locked, missile.Direction) < .05f, "Guidance must stop after .9 seconds, even if the player changes sides.");
                missile.Cancel();
                record?.Invoke("Missile steering is limited to nine total degrees and locks permanently after .9 seconds.");

                arena.PlacePlayer(new Vector3(0, .05f, 2));
                health = arena.Player.Health;
                var contact = RuinsBossProjectile.FireElectric(new Vector3(0, .9f, 0), Vector3.forward,
                    arena.Owner, arena.Player, arena.Room, 11);
                timeout = new Deadline(2, "electric player hit");
                while (contact && !contact.Finished) { timeout.Check(); arena.Step(); yield return null; }
                HitFeedback.CancelHitStop();
                Require(contact.DamageAttempts == 1 && contact.HitPlayer && arena.Player.Health == health - 11,
                    "An electric shot must damage a stationary player exactly once.");
                arena.Player.GrantInvulnerability(.8f);
                health = arena.Player.Health;
                contact = RuinsBossProjectile.FireHoming(arena.Player.transform.position + Vector3.up * .9f,
                    Vector3.forward, arena.Owner, arena.Player, arena.Room, 17);
                timeout = new Deadline(1, "missile invulnerability");
                while (contact && !contact.Finished) { timeout.Check(); arena.Step(); yield return null; }
                Require(contact.DamageAttempts == 1 && !contact.HitPlayer && arena.Player.Health == health,
                    "Initial missile overlap must resolve once through the existing invulnerability gate.");
                record?.Invoke("Electric contact damages once; initial missile overlap respects player invulnerability.");

                float waitUntil = Time.time + .9f;
                timeout = new Deadline(2, "dash setup");
                while (Time.time < waitUntil) { timeout.Check(); arena.Step(); yield return null; }
                arena.PlacePlayer(Vector3.up * .05f);
                arena.Motor.enabled = true;
                arena.Motor.SetAutomationInput(Vector2.zero, Vector3.forward * 8, true);
                timeout = new Deadline(1, "actual dash");
                while (!arena.Motor.IsDashing) { timeout.Check(); arena.Step(); yield return null; }
                int dodges = arena.Player.JustDodges;
                contact = RuinsBossProjectile.FireElectric(arena.Player.transform.position + Vector3.up * .9f + Vector3.forward * .2f,
                    Vector3.back, arena.Owner, arena.Player, arena.Room, 11);
                timeout = new Deadline(1, "dash contact");
                while (contact && !contact.Finished) { timeout.Check(); arena.Step(); yield return null; }
                Require(contact.DamageAttempts == 1 && !contact.HitPlayer && arena.Player.Health == health && arena.Player.JustDodges == dodges + 1,
                    "A real dash must evade the electric shot and register a just dodge.");
                arena.Motor.enabled = false;
                record?.Invoke("Actual PlayerMotor dash evades electric contact and registers one just dodge.");

                arena.PlacePlayer(Vector3.up * .05f);
                Vector3 landing = Vector3.zero;
                var artillery = RuinsBossProjectile.LaunchArtillery(new Vector3(-8, 3, -5), landing, arena.Owner, arena.Player, arena.Room, 21);
                Require(!artillery.WarningVisible && artillery.Impact.GetComponentsInChildren<Telegraph>(true).Length == 0
                    && artillery.Impact.Radius == RuinsBossProjectile.ArtilleryRadius,
                    "Artillery must preserve its damage footprint without a landing preview.");
                float firedAt = Time.time;
                bool ascended = false, descending = false;
                timeout = new Deadline(4, "locked artillery flight");
                while (artillery && !artillery.Finished)
                {
                    if (artillery.Age > .6f) arena.PlacePlayer(new Vector3(10, .05f, 10));
                    ascended |= artillery.transform.position.y > 7;
                    descending |= artillery.Age > 1.2f && artillery.Direction.y < -.8f;
                    Require(artillery.LockedPoint == landing, "Artillery must not follow the target after launch.");
                    Require(!artillery.WarningVisible, "Artillery must remain free of landing previews throughout flight.");
                    timeout.Check(); arena.Step(); yield return null;
                }
                Require(artillery.Finished && ascended && descending && Time.time - firedAt >= 1.5f && artillery.DamageAttempts == 0,
                    "Artillery must rise, descend onto its fixed impact point after at least 1.5 seconds, and miss a player who left it.");
                record?.Invoke("Artillery rises then falls onto a fixed 2.1 m footprint after 1.8 seconds without a landing preview; moving away avoids it.");

                arena.PlacePlayer(new Vector3(0, .05f, 1));
                health = arena.Player.Health;
                wall = arena.Box("Blast cover", new Vector3(0, 1.5f, .5f), new Vector3(5, 3, .025f));
                var impact = RuinsBossImpact.Create(Vector3.zero, 2.1f, 1.5f, arena.Owner, arena.Player, arena.Room, 21);
                timeout = new Deadline(3, "blast cover");
                while (impact && !impact.Exploded) { timeout.Check(); arena.Step(); yield return null; }
                Require(impact.Exploded && impact.DamageAttempts == 0 && arena.Player.Health == health, "Blast cover must block damage through a thin wall.");
                impact.Cancel();
                impact = RuinsBossImpact.Create(new Vector3(0, 0, .5f), 2.1f, 1.5f, arena.Owner, arena.Player, arena.Room, 21);
                timeout = new Deadline(3, "embedded blast cover");
                while (impact && !impact.Exploded) { timeout.Check(); arena.Step(); yield return null; }
                Require(impact.Exploded && impact.DamageAttempts == 0 && arena.Player.Health == health, "A blast centre inside scenery must not bypass cover.");
                impact.Cancel(); Object.DestroyImmediate(wall);
                record?.Invoke("Artillery cover blocks blasts across walls and when the blast centre is embedded in debris.");

                artillery = RuinsBossProjectile.LaunchArtillery(new Vector3(-8, 3, -5), Vector3.zero, arena.Owner, arena.Player, arena.Room, 21);
                timeout = new Deadline(4, "artillery single hit");
                while (artillery && !artillery.Finished) { timeout.Check(); arena.Step(); yield return null; }
                HitFeedback.CancelHitStop();
                Require(artillery.DamageAttempts == 1 && artillery.HitPlayer && arena.Player.Health == health - 21,
                    "Uncovered artillery must deal one configured hit inside its impact footprint.");
                record?.Invoke("Uncovered artillery deals one 21-damage hit through the existing player health gate.");

                arena.PlacePlayer(new Vector3(20, .05f, 20));
                var electric = RuinsBossProjectile.FireElectric(new Vector3(-10, 1, -10), Vector3.forward, arena.Owner, arena.Player, arena.Room, 9);
                artillery = RuinsBossProjectile.LaunchArtillery(new Vector3(-8, 3, -5), Vector3.zero, arena.Owner, arena.Player, arena.Room, 21);
                Vector3 before = electric.transform.position, arcBefore = artillery.transform.position;
                float oldScale = Time.timeScale; Time.timeScale = 0;
                try
                {
                    for (int i = 0; i < 3; i++) yield return null;
                    Require(electric && electric.transform.position == before && artillery && artillery.transform.position == arcBefore
                        && !artillery.WarningVisible && !artillery.Impact.Exploded,
                        "Pause must freeze bullet travel, artillery flight and the pending explosion without a landing preview.");
                }
                finally { Time.timeScale = oldScale; }
                electric.Cancel(); artillery.Cancel();
                record?.Invoke("Pause freezes electric travel, artillery flight and pending explosions.");

                electric = RuinsBossProjectile.FireElectric(new Vector3(-10, 1, -10), Vector3.forward, arena.Owner, arena.Player, arena.Room, 9);
                missile = RuinsBossProjectile.FireHoming(new Vector3(-12, 1, -10), Vector3.forward, arena.Owner, arena.Player, arena.Room, 9);
                artillery = RuinsBossProjectile.LaunchArtillery(new Vector3(-8, 3, -5), Vector3.zero, arena.Owner, arena.Player, arena.Room, 21);
                impact = artillery.Impact;
                arena.Owner.TakeDamage(arena.Owner.maxHealth);
                Require(electric.Finished && missile.Finished && artillery.Finished && impact.Finished && !impact.WarningVisible,
                    "Owner defeat must immediately cancel every projectile and artillery warning.");
                record?.Invoke("Owner defeat immediately cancels electric shots, side missiles, artillery and landing warnings.");
            }

            using (var arena = new Arena())
            {
                arena.PlacePlayer(new Vector3(0, .05f, .6f));
                arena.Player.SetMaximum(5, true);
                var lethal = RuinsBossProjectile.LaunchArtillery(new Vector3(-8, 3, -5), Vector3.zero, arena.Owner, arena.Player, arena.Room, 21);
                var impact = lethal.Impact;
                var deadline = new Deadline(4, "lethal artillery callback order");
                while (arena.Player.IsAlive) { deadline.Check(); arena.Step(); yield return null; }
                HitFeedback.CancelHitStop();
                Require(lethal.Finished && impact.Finished && lethal.DamageAttempts == 1 && lethal.HitPlayer,
                    "A lethal impact must retain its single successful damage result when the synchronous death event cancels its projectile.");
                record?.Invoke("Lethal artillery retains one successful hit even when the player's death synchronously cancels its owner shot.");
            }
            using (var arena = new Arena())
            {
                arena.PlacePlayer(new Vector3(20, .05f, 20));
                var electric = RuinsBossProjectile.FireElectric(new Vector3(-10, 1, -10), Vector3.forward, arena.Owner, arena.Player, arena.Room, 9);
                var artillery = RuinsBossProjectile.LaunchArtillery(new Vector3(-8, 3, -5), Vector3.zero, arena.Owner, arena.Player, arena.Room, 21);
                var impact = artillery.Impact;
                arena.Player.TakeDamage(arena.Player.Health); HitFeedback.CancelHitStop();
                Require(electric.Finished && artillery.Finished && impact.Finished, "Player death must immediately cancel attacks and warnings.");
                record?.Invoke("Player death immediately cancels projectiles and landing warnings.");
            }
            using (var arena = new Arena())
            {
                arena.PlacePlayer(new Vector3(20, .05f, 20));
                var electric = RuinsBossProjectile.FireElectric(new Vector3(-10, 1, -10), Vector3.forward, arena.Owner, arena.Player, arena.Room, 9);
                var artillery = RuinsBossProjectile.LaunchArtillery(new Vector3(-8, 3, -5), Vector3.zero, arena.Owner, arena.Player, arena.Room, 21);
                var impact = artillery.Impact;
                var unload = arena.Unload();
                var deadline = new Deadline(3, "room unload");
                while (!unload.isDone) { deadline.Check(); yield return null; }
                Require(electric.Finished && artillery.Finished && impact.Finished, "Unloading a room must clean up shots and warnings.");
                record?.Invoke("Room scene unload cleans up all owned projectiles and artillery warnings.");
            }
        }

        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        struct Deadline
        {
            readonly float gameEnd, realEnd;
            readonly string label;
            public Deadline(float seconds, string label)
            { gameEnd = Time.time + seconds; realEnd = Time.realtimeSinceStartup + seconds * 4 + 3; this.label = label; }
            public void Check()
            { if (Time.time > gameEnd || Time.realtimeSinceStartup > realEnd) throw new TimeoutException("Boss projectile validation timed out: " + label); }
        }

        sealed class Arena : IDisposable
        {
            readonly Scene scene;
            readonly PhysicsScene physics;
            readonly GameObject root;
            readonly CharacterController body;
            public LiminalRoom Room { get; }
            public TrainingEnemy Owner { get; }
            public LiminalPlayerHealth Player { get; }
            public PlayerMotor Motor { get; }
            public Arena()
            {
                scene = SceneManager.CreateScene("Boss projectile validation " + Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                physics = scene.GetPhysicsScene();
                root = new GameObject("Temporary boss projectile arena"); SceneManager.MoveGameObjectToScene(root, scene);
                Room = root.AddComponent<LiminalRoom>(); Room.localBounds = new Bounds(Vector3.up * 3, new Vector3(80, 8, 80));
                Box("Floor", Vector3.down * .15f, new Vector3(80, .3f, 80));
                var owner = Child("Owner"); owner.transform.localPosition = new Vector3(-30, 0, -30);
                Owner = owner.AddComponent<TrainingEnemy>(); Owner.Configure(1000, false);
                var player = Child("Target"); body = player.AddComponent<CharacterController>();
                body.height = 1.8f; body.radius = .32f; body.center = Vector3.up * .9f;
                Motor = player.AddComponent<PlayerMotor>();
                var visual = new GameObject("Target visual").transform; visual.SetParent(player.transform, false); Motor.visual = visual; Motor.enabled = false;
                Player = player.AddComponent<LiminalPlayerHealth>(); Player.SetMaximum(1000, true);
                var feedback = player.GetComponent<JustDodgeFeedback>(); if (feedback) Object.DestroyImmediate(feedback);
                Physics.SyncTransforms();
            }
            GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(root.transform, false); return go; }
            public GameObject Box(string name, Vector3 position, Vector3 size)
            { var go = Child(name); go.transform.position = position; go.AddComponent<BoxCollider>().size = size; Physics.SyncTransforms(); return go; }
            public void PlacePlayer(Vector3 position)
            { body.enabled = false; Player.transform.position = position; body.enabled = true; Physics.SyncTransforms(); }
            public void Step() { Physics.SyncTransforms(); if (Time.deltaTime > 0) physics.Simulate(Time.deltaTime); }
            public AsyncOperation Unload() => SceneManager.UnloadSceneAsync(scene);
            public void Dispose()
            {
                HitFeedback.CancelHitStop(); if (root) Object.DestroyImmediate(root);
                if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            }
        }
    }
}
#endif
