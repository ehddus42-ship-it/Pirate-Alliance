#if UNITY_EDITOR
using System;
using System.Collections;
using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.Ruins.Editor
{
    /// <summary>Real runtime attacks in an isolated physics scene; no authored scene is changed.</summary>
    public static class RuinsProjectileValidation
    {
        public static IEnumerator Run(Action<string> record)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Ruins projectile validation requires PlayMode.");
            using (var arena = new Arena())
            {
                arena.PlacePlayer(new Vector3(0, .05f, 6));
                var wall = arena.Box("Thin projectile wall", new Vector3(0, 1.5f, 3), new Vector3(8, 3, .025f));
                int health = arena.Player.Health;
                for (int type = 0; type < 2; type++)
                {
                    var shot = RuinsProjectile.Fire(new Vector3(0, .9f, 0), Vector3.forward, arena.Owner,
                        arena.Player, arena.Room, 9, type == 1);
                    Require(shot.GetComponentsInChildren<MeshRenderer>().Length >= 4, "A shot needs its visible core and wake or fins.");
                    var deadline = new Deadline(2, "thin wall stop");
                    while (shot && !shot.Finished)
                    {
                        Require(shot.transform.position.z < 3, "A ruins projectile crossed the thin wall.");
                        deadline.Check(); arena.Step(); yield return null;
                    }
                    Require(shot.Finished && shot.DamageAttempts == 0 && arena.Player.Health == health,
                        "A 2.5 cm wall must stop both shot types before player damage.");
                    shot = RuinsProjectile.Fire(new Vector3(0, .9f, 3), Vector3.forward, arena.Owner,
                        arena.Player, arena.Room, 9, type == 1);
                    deadline = new Deadline(1, "initial wall overlap");
                    while (shot && !shot.Finished) { deadline.Check(); arena.Step(); yield return null; }
                    Require(shot.Finished && shot.DamageAttempts == 0, "A shot spawned inside scenery must resolve that overlap.");
                }
                record?.Invoke("Both scrap and amber shots stop at 2.5 cm walls and resolve initial scenery overlap.");
                Object.DestroyImmediate(wall);

                arena.PlacePlayer(new Vector3(15, .05f, 20));
                var friendly = arena.Box("Other monster", new Vector3(0, .9f, 3), new Vector3(1.2f, 1.8f, 1.2f));
                friendly.AddComponent<TrainingEnemy>().Configure(100, false);
                for (int type = 0; type < 2; type++)
                {
                    var shot = RuinsProjectile.Fire(new Vector3(0, .9f, 0), Vector3.forward, arena.Owner,
                        arena.Player, arena.Room, 9, type == 1);
                    var deadline = new Deadline(4, "committed projectile range");
                    while (shot && !shot.Finished) { deadline.Check(); arena.Step(); yield return null; }
                    Require(shot.Finished && Mathf.Abs(shot.Travelled - shot.MaximumTravel) < .01f && shot.DamageAttempts == 0,
                        "Shots must pass other monsters and stop at their exact warning range.");
                }
                Object.DestroyImmediate(friendly);
                record?.Invoke("Shots ignore other monsters and expire at 18 m scrap / 16 m amber warning range.");

                arena.PlacePlayer(new Vector3(0, .05f, 2));
                health = arena.Player.Health;
                var contact = RuinsProjectile.Fire(new Vector3(0, .9f, 0), Vector3.forward, arena.Owner,
                    arena.Player, arena.Room, 9);
                var timeout = new Deadline(2, "single player contact");
                while (contact && !contact.Finished) { timeout.Check(); arena.Step(); yield return null; }
                HitFeedback.CancelHitStop();
                Require(contact.DamageAttempts == 1 && contact.HitPlayer && arena.Player.Health == health - 9,
                    "A stationary player must take exactly one configured shot hit.");
                record?.Invoke("Stationary player receives one 9-damage projectile hit.");

                arena.Player.GrantInvulnerability(1);
                health = arena.Player.Health;
                contact = RuinsProjectile.Fire(arena.Player.transform.position + Vector3.up * .9f, Vector3.forward,
                    arena.Owner, arena.Player, arena.Room, 9, true);
                timeout = new Deadline(1, "initial player overlap invulnerability");
                while (contact && !contact.Finished) { timeout.Check(); arena.Step(); yield return null; }
                Require(contact.DamageAttempts == 1 && !contact.HitPlayer && arena.Player.Health == health,
                    "Initial player overlap must resolve once through the existing invulnerability gate.");
                record?.Invoke("Initial player overlap resolves once and respects granted invulnerability.");

                float waitUntil = Time.time + 1.1f;
                timeout = new Deadline(2, "invulnerability expiry before actual dash");
                while (Time.time < waitUntil) { timeout.Check(); arena.Step(); yield return null; }
                arena.PlacePlayer(new Vector3(0, .05f, 0));
                arena.Motor.enabled = true;
                arena.Motor.SetAutomationInput(Vector2.zero, Vector3.forward * 8, true);
                timeout = new Deadline(1, "real player dash");
                while (!arena.Motor.IsDashing) { timeout.Check(); arena.Step(); yield return null; }
                int dodges = arena.Player.JustDodges;
                health = arena.Player.Health;
                contact = RuinsProjectile.Fire(arena.Player.transform.position + Vector3.up * .9f + Vector3.forward * .2f,
                    Vector3.back, arena.Owner, arena.Player, arena.Room, 9);
                timeout = new Deadline(1, "dash projectile contact");
                while (contact && !contact.Finished) { timeout.Check(); arena.Step(); yield return null; }
                Require(contact.DamageAttempts == 1 && !contact.HitPlayer && arena.Player.Health == health && arena.Player.JustDodges == dodges + 1,
                    "A real PlayerMotor dash must reject a contacting projectile and register a just dodge.");
                arena.Motor.enabled = false;
                record?.Invoke("Actual PlayerMotor dash rejects projectile damage and registers one just dodge.");

                waitUntil = Time.time + .5f;
                timeout = new Deadline(1, "dash grace expiry");
                while (Time.time < waitUntil) { timeout.Check(); arena.Step(); yield return null; }
                arena.PlacePlayer(new Vector3(0, .05f, .8f));
                wall = arena.Box("Pulse cover", new Vector3(0, 1.5f, .4f), new Vector3(4, 3, .025f));
                health = arena.Player.Health;
                var pulse = RuinsGroundPulse.Create(Vector3.zero, 1.15f, .3f, arena.Owner, arena.Player, arena.Room, 12);
                Require(pulse.WarningVisible && !pulse.Exploded, "A new ground pulse must display its pending circular warning.");
                timeout = new Deadline(2, "covered pulse");
                while (pulse && !pulse.Exploded) { timeout.Check(); arena.Step(); yield return null; }
                Require(pulse.Exploded && pulse.DamageAttempts == 0 && arena.Player.Health == health,
                    "A solid wall must cover a player inside the circular pulse radius.");
                pulse.Cancel();
                pulse = RuinsGroundPulse.Create(new Vector3(0, 0, .4f), 1.15f, .2f, arena.Owner, arena.Player, arena.Room, 12);
                timeout = new Deadline(2, "pulse emitter inside cover");
                while (pulse && !pulse.Exploded) { timeout.Check(); arena.Step(); yield return null; }
                Require(pulse.Exploded && pulse.DamageAttempts == 0 && arena.Player.Health == health,
                    "A pulse emitter embedded inside solid cover must not strike out through it.");
                pulse.Cancel(); Object.DestroyImmediate(wall);
                record?.Invoke("Locked circular warning shares its damage radius; solid scenery blocks its strike.");

                pulse = RuinsGroundPulse.Create(Vector3.zero, 1.15f, .3f, arena.Owner, arena.Player, arena.Room, 12);
                timeout = new Deadline(2, "open pulse hit");
                while (pulse && !pulse.Exploded) { timeout.Check(); arena.Step(); yield return null; }
                HitFeedback.CancelHitStop();
                Require(pulse.Exploded && pulse.DamageAttempts == 1 && pulse.HitPlayer && arena.Player.Health == health - 12,
                    "An unobstructed pulse must deal one configured hit inside its warned radius.");
                timeout = new Deadline(2, "pulse visual fade");
                while (pulse && !pulse.Finished) { timeout.Check(); arena.Step(); yield return null; }
                Require(pulse.Finished && pulse.DamageAttempts == 1, "The fading shock ring must not deal repeated damage.");
                record?.Invoke("Open pulse deals 12 damage once; its fading ring adds no further hits.");

                arena.PlacePlayer(new Vector3(15, .05f, 15));
                pulse = RuinsGroundPulse.Create(Vector3.zero, 1.15f, .8f, arena.Owner, arena.Player, arena.Room, 12);
                contact = RuinsProjectile.Fire(new Vector3(-8, 1, -8), Vector3.forward, arena.Owner, arena.Player, arena.Room, 9);
                float oldScale = Time.timeScale;
                Vector3 shotPosition = contact.transform.position;
                Time.timeScale = 0;
                try
                {
                    for (int i = 0; i < 3; i++) yield return null;
                    Require(contact && contact.transform.position == shotPosition && pulse && pulse.WarningVisible && !pulse.Exploded,
                        "Pause must freeze travelling shots and pending ground strikes.");
                }
                finally { Time.timeScale = oldScale; }
                pulse.Cancel(); contact.Cancel();
                record?.Invoke("Pause freezes projectile travel and pending pulse activation.");

                pulse = RuinsGroundPulse.Create(Vector3.zero, 1.15f, 1, arena.Owner, arena.Player, arena.Room, 12);
                contact = RuinsProjectile.Fire(new Vector3(-8, 1, -8), Vector3.forward, arena.Owner, arena.Player, arena.Room, 9);
                arena.Owner.TakeDamage(arena.Owner.maxHealth);
                Require(pulse.Finished && contact.Finished && !pulse.WarningVisible && !pulse.gameObject.activeSelf && !contact.gameObject.activeSelf,
                    "Owner defeat must immediately cancel every owned warning and shot.");
                record?.Invoke("Owner defeat immediately disables owned shots and circular warnings.");
            }

            using (var arena = new Arena())
            {
                arena.PlacePlayer(new Vector3(15, .05f, 15));
                var pulse = RuinsGroundPulse.Create(Vector3.zero, 1.15f, 1, arena.Owner, arena.Player, arena.Room, 12);
                var shot = RuinsProjectile.Fire(new Vector3(-8, 1, -8), Vector3.forward, arena.Owner, arena.Player, arena.Room, 9);
                arena.Player.TakeDamage(arena.Player.Health);
                HitFeedback.CancelHitStop();
                Require(pulse.Finished && shot.Finished && !pulse.WarningVisible && !pulse.gameObject.activeSelf && !shot.gameObject.activeSelf,
                    "Target death must immediately cancel every owned warning and shot.");
                record?.Invoke("Target death immediately disables shots and circular warnings.");
            }
            using (var arena = new Arena())
            {
                arena.PlacePlayer(new Vector3(15, .05f, 15));
                var pulse = RuinsGroundPulse.Create(Vector3.zero, 1.15f, 1, arena.Owner, arena.Player, arena.Room, 12);
                var shot = RuinsProjectile.Fire(new Vector3(-8, 1, -8), Vector3.forward, arena.Owner, arena.Player, arena.Room, 9);
                arena.Owner.gameObject.SetActive(false);
                arena.Step(); yield return null;
                Require(pulse.Finished && shot.Finished, "Disabling an owner must also cancel pending attacks.");
                record?.Invoke("Owner deactivation also cancels pending projectiles and ground strikes.");
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
                    throw new TimeoutException("Ruins projectile validation timed out: " + label);
            }
        }

        sealed class Arena : IDisposable
        {
            readonly Scene scene;
            readonly PhysicsScene physics;
            readonly GameObject root;
            readonly CharacterController playerBody;
            public LiminalRoom Room { get; }
            public TrainingEnemy Owner { get; }
            public LiminalPlayerHealth Player { get; }
            public PlayerMotor Motor { get; }

            public Arena()
            {
                scene = SceneManager.CreateScene("Ruins projectile validation " + Guid.NewGuid().ToString("N"),
                    new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                physics = scene.GetPhysicsScene();
                root = new GameObject("Temporary ruins combat arena");
                SceneManager.MoveGameObjectToScene(root, scene);
                Room = root.AddComponent<LiminalRoom>();
                Room.localBounds = new Bounds(Vector3.up * 3, new Vector3(80, 8, 80));
                Box("Floor", Vector3.down * .15f, new Vector3(80, .3f, 80));
                var owner = Child("Owner"); owner.transform.localPosition = new Vector3(-25, 0, -25);
                Owner = owner.AddComponent<TrainingEnemy>(); Owner.Configure(1000, false);
                var player = Child("Target");
                playerBody = player.AddComponent<CharacterController>();
                playerBody.height = 1.8f; playerBody.radius = .32f; playerBody.center = Vector3.up * .9f;
                Motor = player.AddComponent<PlayerMotor>();
                var visual = new GameObject("Target visual").transform; visual.SetParent(player.transform, false);
                Motor.visual = visual; Motor.enabled = false;
                Player = player.AddComponent<LiminalPlayerHealth>(); Player.SetMaximum(1000, true);
                // Keep the test's real just-dodge gate, without changing the global game's time or camera effects.
                var feedback = player.GetComponent<JustDodgeFeedback>();
                if (feedback) Object.DestroyImmediate(feedback);
                Physics.SyncTransforms();
            }

            GameObject Child(string name)
            {
                var go = new GameObject(name); go.transform.SetParent(root.transform, false); return go;
            }
            public GameObject Box(string name, Vector3 position, Vector3 size)
            {
                var go = Child(name); go.transform.position = position;
                go.AddComponent<BoxCollider>().size = size; Physics.SyncTransforms(); return go;
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
