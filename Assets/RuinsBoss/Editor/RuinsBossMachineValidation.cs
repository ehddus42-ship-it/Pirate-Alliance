#if UNITY_EDITOR
using System;
using System.Collections;
using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.RuinsBoss.Editor
{
    /// <summary>Real authored machines and their public attack API in an isolated physics scene.</summary>
    public static class RuinsBossMachineValidation
    {
        public static IEnumerator Run(Action<string> record)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("War machine validation requires PlayMode.");
            foreach (RuinsWarMachineKind kind in Enum.GetValues(typeof(RuinsWarMachineKind)))
            {
                using (var arena = new Arena(kind))
                {
                    var machine = arena.Machine;
                    machine.Health.ApplyMissionStats(1.2f, .75f);
                    int health = machine.Health.Health, maximum = machine.Health.maxHealth;
                    machine.Health.TakeDamage(100);
                    Require(machine.IsDormant && !machine.Health.CanBeTargeted && machine.Health.IgnoreDamage && !arena.MachineBody.enabled
                        && machine.Health.Health == health && machine.Health.HitCount == 0 && !machine.StartAttack(0),
                        kind + " must ignore damage, targeting, collision and attacks while dormant.");
                    Require(machine.Activate() && machine.ActivationCount == 1 && !machine.Activate(), kind + " activates once through its real public API.");
                    machine.Health.TakeDamage(100);
                    Require(!machine.IsDormant && machine.Health.CanBeTargeted && !machine.Health.IgnoreDamage && arena.MachineBody.enabled
                        && machine.Health.maxHealth == maximum && machine.Health.Health == health - 75,
                        kind + " awakening must retain its mission health and 75% incoming-damage modifier.");
                    record?.Invoke(kind + ": dormant damage/collision immunity; awakening enables combat without resetting mission armor.");
                }

                using (var arena = new Arena(kind))
                {
                    var machine = arena.Machine;
                    arena.PlacePlayer(new Vector3(0, .05f, 6));
                    int health = arena.Player.Health;
                    Require(machine.Activate() && machine.StartAttack(0) && machine.TelegraphVisible,
                        kind + " must begin a visible charge warning before moving.");
                    var deadline = new Deadline(5, kind + " player charge contact");
                    while (machine.State != RuinsBossState.Recovery)
                    { deadline.Check(); arena.Step(); yield return null; }
                    HitFeedback.CancelHitStop();
                    Require(machine.Attacks == 1 && machine.DamageAttempts == 1 && arena.Player.Health == health - machine.chargeDamage,
                        kind + " must deal exactly one charge hit to an unobstructed stationary player. "
                        + $"Attempts={machine.DamageAttempts}, Health={arena.Player.Health}/{health}, Travelled={machine.Travelled:F3}.");
                    float until = Time.time + .45f;
                    while (Time.time < until) { deadline.Check(); arena.Step(); yield return null; }
                    Require(machine.DamageAttempts == 1 && arena.Player.Health == health - machine.chargeDamage,
                        kind + " must not deal a second charge hit during recovery.");
                    record?.Invoke(kind + ": real charge hits a stationary player once and deals no repeated recovery damage.");
                }

                using (var arena = new Arena(kind))
                {
                    var machine = arena.Machine;
                    arena.PlacePlayer(new Vector3(0, .05f, 6));
                    arena.Box("Thin charge wall", new Vector3(0, 2, 3), new Vector3(10, 4, .025f));
                    int health = arena.Player.Health;
                    Require(machine.Activate() && machine.StartAttack(0), kind + " must accept the controlled wall-facing charge.");
                    var deadline = new Deadline(5, kind + " thin wall charge");
                    while (machine.State != RuinsBossState.Recovery)
                    {
                        Require(machine.transform.position.z + arena.MachineBody.radius <= 3.08f,
                            kind + " body penetrated the thin wall during its charge.");
                        deadline.Check(); arena.Step(); yield return null;
                    }
                    Require(machine.Travelled > .1f && machine.Travelled < 3 && machine.DamageAttempts == 0 && arena.Player.Health == health
                        && machine.transform.position.z + arena.MachineBody.radius <= 3.08f,
                        kind + " must stop at a 2.5 cm wall without hitting the player behind it.");
                    record?.Invoke(kind + ": 2.5 cm wall stops the charging body and prevents damage to the player behind it.");
                }

                using (var arena = new Arena(kind))
                {
                    var machine = arena.Machine;
                    arena.PlacePlayer(new Vector3(0, .05f, 6));
                    int health = arena.Player.Health, dodges = arena.Player.JustDodges;
                    Require(machine.Activate() && machine.StartAttack(0), kind + " starts its real charge for the dash check.");
                    var deadline = new Deadline(5, kind + " actual dash contact");
                    while (machine.State != RuinsBossState.Attack || Vector3.Distance(machine.transform.position, arena.Player.transform.position) > 4)
                    {
                        Require(machine.State != RuinsBossState.Recovery, kind + " ended its charge before the dash contact window.");
                        deadline.Check(); arena.Step(); yield return null;
                    }
                    // Dash toward the oncoming machine so this checks invulnerable contact, not simply moving out of its path.
                    // With no movement input PlayerMotor uses visual.forward, rather than the supplied aim point.
                    Vector3 dashToward = Vector3.ProjectOnPlane(machine.transform.position - arena.Player.transform.position, Vector3.up).normalized;
                    arena.Motor.visual.rotation = Quaternion.LookRotation(dashToward, Vector3.up);
                    arena.Motor.enabled = true;
                    arena.Motor.SetAutomationInput(Vector2.zero, machine.transform.position, true);
                    bool dashed = false;
                    while (machine.State != RuinsBossState.Recovery)
                    {
                        dashed |= arena.Motor.IsDashing;
                        deadline.Check(); arena.Step(); yield return null;
                    }
                    dashed |= arena.Motor.DashCount > 0;
                    arena.Motor.enabled = false;
                    Require(dashed && machine.DamageAttempts == 1 && arena.Player.Health == health && arena.Player.JustDodges == dodges + 1,
                        kind + " charge must contact once, respect the actual dash and register one just dodge. "
                        + $"Dashes={arena.Motor.DashCount}, Attempts={machine.DamageAttempts}, Dodges={arena.Player.JustDodges - dodges}, Health={arena.Player.Health}/{health}.");
                    record?.Invoke(kind + ": actual forward dash makes one charge contact harmless and registers one just dodge.");
                }

                using (var arena = new Arena(kind))
                {
                    var machine = arena.Machine;
                    arena.PlacePlayer(new Vector3(0, .05f, 15));
                    float side = arena.MachineBody.radius + arena.MachineBody.skinWidth + .07f;
                    arena.Box("Grazing upright", new Vector3(side, 2, 3.5f), new Vector3(.22f, 4, .55f));
                    Require(machine.Activate() && machine.StartAttack(0), kind + " starts the glancing-obstacle charge.");
                    var deadline = new Deadline(5, kind + " glancing charge");
                    Vector3 last = machine.transform.position;
                    float path = 0;
                    while (machine.State != RuinsBossState.Recovery)
                    {
                        Vector3 position = machine.transform.position;
                        path += Vector3.ProjectOnPlane(position - last, Vector3.up).magnitude; last = position;
                        Require(arena.Room.Contains(position, 1) && machine.Travelled <= 9.05f && path <= 9.05f,
                            kind + " glancing collision escaped its room or extended its 9 m charge budget.");
                        deadline.Check(); arena.Step(); yield return null;
                    }
                    path += Vector3.ProjectOnPlane(machine.transform.position - last, Vector3.up).magnitude;
                    Require(machine.Travelled > .2f && machine.Travelled <= 9.05f && path <= 9.05f
                        && arena.Room.Contains(machine.transform.position, 1) && machine.DamageAttempts == 0,
                        kind + " must resolve a grazing obstacle within its original charge distance and room.");
                    record?.Invoke(kind + ": grazing scenery neither extends the 9 m charge nor lets the body leave the room.");
                }
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
            { if (Time.time > gameEnd || Time.realtimeSinceStartup > realEnd) throw new TimeoutException("War machine validation timed out: " + label); }
        }

        sealed class Arena : IDisposable
        {
            readonly Scene scene;
            readonly PhysicsScene physics;
            readonly GameObject root;
            readonly CharacterController playerBody;
            public LiminalRoom Room { get; }
            public LiminalPlayerHealth Player { get; }
            public PlayerMotor Motor { get; }
            public RuinsWarMachine Machine { get; }
            public CharacterController MachineBody { get; }

            public Arena(RuinsWarMachineKind kind)
            {
                scene = SceneManager.CreateScene("War machine validation " + Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                physics = scene.GetPhysicsScene();
                root = new GameObject("Temporary machine combat arena"); SceneManager.MoveGameObjectToScene(root, scene);
                Room = root.AddComponent<LiminalRoom>(); Room.localBounds = new Bounds(Vector3.up * 3, new Vector3(80, 8, 80));
                Box("Floor", Vector3.down * .15f, new Vector3(80, .3f, 80));
                var player = new GameObject("Machine test target"); player.transform.SetParent(root.transform, false);
                player.transform.position = new Vector3(0, .05f, 6);
                playerBody = player.AddComponent<CharacterController>(); playerBody.height = 1.8f; playerBody.radius = .32f; playerBody.center = Vector3.up * .9f;
                Motor = player.AddComponent<PlayerMotor>();
                var visual = new GameObject("Target visual").transform; visual.SetParent(player.transform, false); Motor.visual = visual; Motor.enabled = false;
                Player = player.AddComponent<LiminalPlayerHealth>(); Player.SetMaximum(1000, true);
                var feedback = player.GetComponent<JustDodgeFeedback>(); if (feedback) Object.DestroyImmediate(feedback);
                Machine = RuinsWarMachine.Create(kind, Vector3.up * .05f, Quaternion.identity, root.transform);
                if (!Machine) throw new InvalidOperationException("Authored " + kind + " prefab is required for physics validation.");
                Machine.Setup(Player, Room, 0);
                MachineBody = Machine.GetComponent<CharacterController>();
                Physics.SyncTransforms();
            }
            public GameObject Box(string name, Vector3 position, Vector3 size)
            {
                var go = new GameObject(name); go.transform.SetParent(root.transform, false); go.transform.position = position;
                go.AddComponent<BoxCollider>().size = size; Physics.SyncTransforms(); return go;
            }
            public void PlacePlayer(Vector3 position)
            { playerBody.enabled = false; Player.transform.position = position; playerBody.enabled = true; Physics.SyncTransforms(); }
            public void Step() { Physics.SyncTransforms(); if (Time.deltaTime > 0) physics.Simulate(Time.deltaTime); }
            public void Dispose()
            {
                HitFeedback.CancelHitStop(); if (root) Object.DestroyImmediate(root);
                if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            }
        }
    }
}
#endif
