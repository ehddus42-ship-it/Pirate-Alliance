#if UNITY_EDITOR
using System;
using System.Collections;
using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.GameTheme.Editor
{
    /// <summary>Run from a PlayMode validation coroutine; does not open, save, or replace authored scenes.</summary>
    public static class GameTetrominoProjectileValidation
    {
        public static IEnumerator Run(Action<string> record)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Tetromino combat validation requires PlayMode.");
            using (var arena = new Arena())
            {
                arena.PlacePlayer(new Vector3(15, .05f, 15));
                var shot = TetrominoProjectile.Fire(new Vector3(-15, 1, -10), Vector3.forward, arena.Owner,
                    arena.Player, arena.Room, 9, TetrominoShape.T, false);
                Require(shot.GetComponentsInChildren<MeshFilter>().Length == 4, "A tetromino must have four visible cells.");
                float started = Time.time;
                var deadline = new Deadline(3, "soft-drop acceleration");
                while (Time.time - started < .25f) { deadline.Check(); arena.Step(); yield return null; }
                Require(shot && Mathf.Abs(shot.Speed - 1.32f) < .001f, "A soft-drop shot must start at 60 percent of its original speed.");
                Vector3 slowPosition = shot.transform.position;
                while (Time.time - started < 1.3f) { deadline.Check(); arena.Step(); yield return null; }
                Require(shot && Mathf.Abs(shot.Speed - 7.92f) < .001f && Vector3.Distance(slowPosition, shot.transform.position) > 3,
                    "A soft-drop shot must retain its acceleration timing and reach 60 percent of its original fast speed.");
                record?.Invoke("Soft-drop projectile: four cells, 1.32 m/s opening, unchanged acceleration timing, then 7.92 m/s travel.");
                Object.DestroyImmediate(shot.gameObject);

                arena.PlacePlayer(new Vector3(0, .05f, 6));
                var wall = arena.Box("Thin projectile wall", new Vector3(0, 1.5f, 3), new Vector3(8, 3, .025f));
                int health = arena.Player.Health;
                shot = TetrominoProjectile.Fire(new Vector3(0, .9f, 0), Vector3.forward, arena.Owner,
                    arena.Player, arena.Room, 9, TetrominoShape.Z, false);
                deadline = new Deadline(3, "thin-wall projectile stop");
                while (shot && !shot.Finished)
                {
                    Require(shot.transform.position.z < 3, "A projectile crossed the thin wall.");
                    deadline.Check(); arena.Step(); yield return null;
                }
                Require(shot.Finished && shot.DamageAttempts == 0 && arena.Player.Health == health,
                    "The thin wall must consume the projectile before player damage.");
                record?.Invoke("Projectile sweep: a 2.5 cm wall blocks the accelerated shot.");
                shot = TetrominoProjectile.Fire(new Vector3(0, .9f, 3), Vector3.forward, arena.Owner,
                    arena.Player, arena.Room, 9, TetrominoShape.L, false);
                deadline = new Deadline(1, "projectile starts inside scenery");
                while (shot && !shot.Finished) { deadline.Check(); arena.Step(); yield return null; }
                Require(shot.Finished && shot.DamageAttempts == 0,
                    "A projectile born inside scenery must not escape through it.");
                record?.Invoke("A projectile starting inside scenery resolves the overlap instead of tunnelling.");
                Object.DestroyImmediate(wall);

                var left = arena.Box("Left ricochet wall", new Vector3(-2, 1.5f, 0), new Vector3(.05f, 3, 16));
                var right = arena.Box("Right ricochet wall", new Vector3(2, 1.5f, 0), new Vector3(.05f, 3, 16));
                arena.PlacePlayer(new Vector3(0, .05f, 12));
                shot = TetrominoProjectile.Fire(new Vector3(0, 1, 0), Vector3.right, arena.Owner,
                    arena.Player, arena.Room, 9, TetrominoShape.I, true);
                Require(Mathf.Abs(shot.Speed - 6.6f) < .001f, "A ricochet must move at 60 percent of its original speed.");
                deadline = new Deadline(3, "four ricochets");
                int observedBounces = 0;
                Quaternion initialRotation = shot.transform.GetChild(0).localRotation;
                bool rotated = false;
                while (shot && !shot.Finished)
                {
                    observedBounces = Mathf.Max(observedBounces, shot.BounceCount);
                    rotated |= Quaternion.Angle(initialRotation, shot.transform.GetChild(0).localRotation) > 20;
                    Require(Mathf.Abs(shot.transform.position.x) < 2, "The I block tunneled through a ricochet wall.");
                    deadline.Check(); arena.Step(); yield return null;
                }
                // These getters contain only managed values and remain readable after native object destruction.
                Require(rotated && observedBounces >= 1 && shot.Finished && shot.BounceCount == 4,
                    "The spinning I block must disappear on its fourth wall contact.");
                record?.Invoke("I block: visible spin, solid-wall reflection, exact fourth-contact expiry.");
                Object.DestroyImmediate(left); Object.DestroyImmediate(right);

                arena.PlacePlayer(new Vector3(0, .05f, 2.8f));
                health = arena.Player.Health;
                shot = TetrominoProjectile.Fire(new Vector3(0, .9f, 0), Vector3.forward, arena.Owner,
                    arena.Player, arena.Room, 9, TetrominoShape.O, false);
                deadline = new Deadline(3, "player hit");
                while (shot && !shot.Finished) { deadline.Check(); arena.Step(); yield return null; }
                HitFeedback.CancelHitStop();
                Require(shot.DamageAttempts == 1 && shot.HitPlayer && arena.Player.Health == health - 9,
                    "A stationary player must take one projectile hit.");
                record?.Invoke("Projectile hit uses LiminalPlayerHealth once and deals the configured damage.");

                float grantedInvulnerabilityEnds = Time.time + 3;
                arena.Player.GrantInvulnerability(3);
                health = arena.Player.Health;
                shot = TetrominoProjectile.Fire(new Vector3(0, .9f, 0), Vector3.forward, arena.Owner,
                    arena.Player, arena.Room, 9, TetrominoShape.S, false);
                deadline = new Deadline(3, "invulnerable projectile contact");
                while (shot && !shot.Finished) { deadline.Check(); arena.Step(); yield return null; }
                Require(shot.DamageAttempts == 1 && !shot.HitPlayer && arena.Player.Health == health,
                    "Player invulnerability must reject projectile damage while consuming the shot.");
                record?.Invoke("Projectile damage respects the existing player invulnerability gate.");
                shot = TetrominoProjectile.Fire(arena.Player.transform.position + Vector3.up * .9f, Vector3.forward,
                    arena.Owner, arena.Player, arena.Room, 9, TetrominoShape.T, false);
                deadline = new Deadline(1, "projectile starts touching player");
                while (shot && !shot.Finished) { deadline.Check(); arena.Step(); yield return null; }
                Require(shot.DamageAttempts == 1 && !shot.HitPlayer && arena.Player.Health == health,
                    "A projectile starting inside the player must resolve exactly one contact through the invulnerability gate.");
                record?.Invoke("Initial player overlap resolves once and still respects invulnerability.");

                arena.PlacePlayer(new Vector3(0, .05f, 8));
                var charger = TetrominoCharger.Spawn(new Vector3(0, .05f, 0), Quaternion.identity,
                    arena.Owner, arena.Player, arena.Room);
                started = Time.time;
                deadline = new Deadline(3, "Z charge aim lock");
                while (Time.time - started < .2f) { deadline.Check(); arena.Step(); yield return null; }
                var warning = charger.GetComponentInChildren<Telegraph>();
                Require(charger.WindingUp && !charger.Charging && charger.Health.IsAlive
                    && charger.Health.CanBeTargeted && warning && warning.Visible,
                    "The Z block must be attackable during a visible windup.");
                Vector3 initialAim = charger.LockedDirection;
                arena.PlacePlayer(new Vector3(4, .05f, 8));
                while (Time.time - started < .4f) { deadline.Check(); arena.Step(); yield return null; }
                Require(Vector3.Angle(initialAim, charger.LockedDirection) > 15, "The early Z warning must track its target.");
                while (Time.time - started < .75f) { deadline.Check(); arena.Step(); yield return null; }
                Vector3 locked = charger.LockedDirection;
                arena.PlacePlayer(new Vector3(-4, .05f, 8));
                bool glinted = false;
                while (!charger.Charging)
                {
                    var cue = charger.GetComponent<AttackAnticipation>();
                    glinted |= cue && cue.Visible;
                    deadline.Check(); arena.Step(); yield return null;
                }
                Require(Time.time - started >= .8f && Vector3.Angle(locked, charger.LockedDirection) < .1f,
                    "Late movement must not redirect the Z block's committed charge.");
                Require(glinted, "The faceless Z block must flash at its body before charging.");
                record?.Invoke("Z charger: attackable, one-second outline, body glint, early tracking, committed late aim.");
                Object.DestroyImmediate(charger.gameObject);

                arena.PlacePlayer(new Vector3(0, .05f, 8));
                wall = arena.Box("Thin charger wall", new Vector3(0, 1.5f, 3), new Vector3(8, 3, .025f));
                health = arena.Player.Health;
                charger = TetrominoCharger.Spawn(new Vector3(0, .05f, 0), Quaternion.identity,
                    arena.Owner, arena.Player, arena.Room);
                deadline = new Deadline(4, "Z wall stop");
                while (charger && !charger.Finished)
                {
                    Require(charger.transform.position.z < 3, "A Z charger crossed the thin wall.");
                    deadline.Check(); arena.Step(); yield return null;
                }
                Require(charger.Finished && charger.Travelled > 1 && charger.Travelled < 3
                    && charger.DamageAttempts == 0 && arena.Player.Health == health,
                    "A Z charge must stop at the wall without damaging a player behind it.");
                record?.Invoke("Z charge: swept thin-wall stop, no damage behind scenery.");
                Object.DestroyImmediate(wall);

                deadline = new Deadline(4, "Z player hit");
                while (Time.time <= grantedInvulnerabilityEnds) { deadline.Check(); arena.Step(); yield return null; }
                arena.PlacePlayer(new Vector3(0, .05f, 5));
                health = arena.Player.Health;
                charger = TetrominoCharger.Spawn(new Vector3(0, .05f, 0), Quaternion.identity,
                    arena.Owner, arena.Player, arena.Room);
                while (charger && !charger.Finished) { deadline.Check(); arena.Step(); yield return null; }
                HitFeedback.CancelHitStop();
                Require(charger.Finished && charger.DamageAttempts == 1 && charger.HitPlayer
                    && arena.Player.Health == health - 14,
                    "A Z charge must deal exactly one 14-damage hit to a stationary player in an unobstructed lane. "
                    + $"Finished={charger.Finished}, DamageAttempts={charger.DamageAttempts}, HitPlayer={charger.HitPlayer}, "
                    + $"Travelled={charger.Travelled:F4}, HealthBefore={health}, HealthAfter={arena.Player.Health}, "
                    + $"Time={Time.time:F3}, GrantedInvulnerabilityEnds={grantedInvulnerabilityEnds:F3}.");
                record?.Invoke("Z charge: stationary player takes exactly one 14-damage hit in an unobstructed lane.");

                charger = TetrominoCharger.Spawn(new Vector3(0, .05f, 0), Quaternion.identity,
                    arena.Owner, arena.Player, arena.Room);
                charger.Health.TakeDamage(charger.Health.maxHealth);
                Require(charger.Finished && !charger.Health.IsAlive && !charger.Health.CanBeTargeted
                    && !charger.gameObject.activeSelf, "Killing a Z block must immediately cancel its pending charge.");
                record?.Invoke("Z block is killable through TrainingEnemy and immediately cancels its warning/charge.");

                shot = TetrominoProjectile.Fire(new Vector3(-5, 1, 0), Vector3.forward, arena.Owner,
                    arena.Player, arena.Room, 9, TetrominoShape.I, true);
                charger = TetrominoCharger.Spawn(new Vector3(5, .05f, 0), Quaternion.identity,
                    arena.Owner, arena.Player, arena.Room);
                arena.Owner.TakeDamage(arena.Owner.maxHealth);
                Require(shot.Finished && charger.Finished && !shot.gameObject.activeSelf && !charger.gameObject.activeSelf,
                    "Owner defeat must immediately cancel owned shots and summons.");
                record?.Invoke("Boss defeat immediately disables every owned projectile and Z summon.");
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
            {
                if (Time.time > gameEnd || Time.realtimeSinceStartup > realEnd)
                    throw new TimeoutException("Tetromino validation timed out: " + label);
            }
        }

        sealed class Arena : IDisposable
        {
            readonly Scene scene;
            readonly PhysicsScene physics;
            readonly GameObject root;
            public LiminalRoom Room { get; }
            public TrainingEnemy Owner { get; }
            public LiminalPlayerHealth Player { get; }
            readonly CharacterController playerBody;

            public Arena()
            {
                scene = SceneManager.CreateScene("Tetromino Validation " + Guid.NewGuid().ToString("N"),
                    new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                physics = scene.GetPhysicsScene();
                root = new GameObject("Temporary Tetromino Arena");
                SceneManager.MoveGameObjectToScene(root, scene);
                Room = root.AddComponent<LiminalRoom>();
                Room.localBounds = new Bounds(Vector3.up * 3, new Vector3(80, 8, 80));
                Box("Floor", Vector3.down * .15f, new Vector3(80, .3f, 80));
                var owner = Child("Owner");
                owner.transform.localPosition = new Vector3(-25, 0, -25);
                Owner = owner.AddComponent<TrainingEnemy>(); Owner.Configure(1000, false);
                var player = Child("Target");
                playerBody = player.AddComponent<CharacterController>();
                playerBody.height = 1.8f; playerBody.radius = .32f; playerBody.center = Vector3.up * .9f;
                Player = player.AddComponent<LiminalPlayerHealth>(); Player.SetMaximum(1000, true);
                Physics.SyncTransforms();
            }
            GameObject Child(string name)
            {
                var go = new GameObject(name); go.transform.SetParent(root.transform, false); return go;
            }
            public GameObject Box(string name, Vector3 position, Vector3 size)
            {
                var go = Child(name); go.transform.position = position;
                go.AddComponent<BoxCollider>().size = size;
                Physics.SyncTransforms();
                return go;
            }
            public void PlacePlayer(Vector3 position)
            {
                playerBody.enabled = false; Player.transform.position = position; playerBody.enabled = true;
                Physics.SyncTransforms();
            }
            public void Step()
            {
                Physics.SyncTransforms();
                if (Time.deltaTime > 0) physics.Simulate(Time.deltaTime);
            }
            public void Dispose()
            {
                HitFeedback.CancelHitStop();
                if (root) Object.DestroyImmediate(root);
                if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            }
        }
    }
}
#endif
