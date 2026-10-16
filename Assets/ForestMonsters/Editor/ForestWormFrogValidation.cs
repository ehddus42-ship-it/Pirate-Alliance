#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.Forest.Editor
{
    /// <summary>Opt-in real gameplay checks in a remote additive arena. Authored scenes and progression stay unchanged.</summary>
    public static class ForestWormFrogValidation
    {
        public static IEnumerator Run(Action<string> record)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Forest combat validation requires Play Mode.");
            float oldScale = Time.timeScale;
            Time.timeScale = 1;
            try
            {
                for (int scenario = 0; scenario < 5; scenario++)
                {
                    using (var arena = new Arena())
                    {
                        arena.PlacePlayer(new Vector3(0, .03f, 5));
                        var frog = arena.Frog();
                        int health = arena.Player.Health;
                        var wait = new Deadline(4, "frog tongue capture");
                        while (!frog.IsPulling) { wait.Check(); arena.Step(); yield return null; }
                        HitFeedback.CancelHitStop();
                        Require(frog.Grabs == 1 && arena.Motor.IsHeld && !arena.Body.enabled,
                            "A tongue contact must own exactly one held player.");
                        Require(arena.Player.Health == health - frog.tongueDamage,
                            "The initial tongue hit must pass through player damage once.");
                        Require(!arena.Player.GetComponent<ForestFrogTetherGuard>().Available,
                            "A second frog must not acquire an already held player.");
                        if (scenario == 0)
                            ForestMonsterValidation.CaptureAttack((frog.transform.position + arena.Player.transform.position) * .5f,
                                "frog-tongue-capture");
                        float caughtZ = arena.Player.transform.localPosition.z;
                        switch (scenario)
                        {
                            case 1:
                                arena.Box("New obstacle on tether path", new Vector3(0, 1.5f, 3.5f), new Vector3(5, 3, .06f));
                                break;
                            case 2: frog.Health.TakeDamage(frog.Health.Health); break;
                            case 3: frog.gameObject.SetActive(false); break;
                            case 4: arena.Motor.enabled = false; break;
                        }
                        wait = new Deadline(2, "bounded frog release");
                        while (arena.Motor.IsHeld) { wait.Check(); arena.Step(); yield return null; }
                        Require(arena.Body.enabled && !frog.IsPulling && frog.Releases == 1,
                            "Arrival, a wall, owner death, deactivation and leaving gameplay must all restore the player controller.");
                        Require(!arena.Player.GetComponent<ForestFrogTetherGuard>().Available,
                            "Release must start the shared anti-chain-grab cooldown.");
                        if (scenario == 0)
                        {
                            Require(arena.Player.transform.localPosition.z < caughtZ - .8f,
                                "A successful tongue must actually pull the player toward the frog.");
                            Require(arena.Player.transform.localPosition.z > .8f,
                                "The frog must release in front of its body, not inside it.");
                        }
                        if (scenario == 1)
                            Require(arena.Player.transform.localPosition.z > 3.6f,
                                "A tether must stop on the player's side of a thin wall.");
                        string[] labels = { "arrival", "thin-wall obstruction", "frog defeat", "frog disable", "player motor / gameplay disable" };
                        record?.Invoke("Frog tongue: one hit, safe release on " + labels[scenario] + ", 2.2 s anti-chain-grab cooldown.");
                    }
                }

                using (var arena = new Arena())
                {
                    arena.PlacePlayer(new Vector3(0, .03f, 3));
                    var frog = arena.Frog();
                    var wait = new Deadline(4, "tongue warning before dash");
                    while (frog.State != ForestFrogState.TongueWindup || frog.StateTime < frog.tongueWindup - .08f)
                    { wait.Check(); arena.Step(); yield return null; }
                    arena.Motor.SetAutomationInput(Vector2.zero, arena.Player.transform.position + Vector3.forward * 8, true);
                    wait = new Deadline(1, "actual dash activation");
                    while (!arena.Motor.IsDashing) { wait.Check(); arena.Step(); yield return null; }
                    int health = arena.Player.Health;
                    wait = new Deadline(2, "tongue misses dashing target");
                    while (frog.TongueShots == 0 || frog.State == ForestFrogState.TongueExtend)
                    { wait.Check(); arena.Step(); yield return null; }
                    Require(frog.Grabs == 0 && !arena.Motor.IsHeld && arena.Player.Health == health,
                        "An actual PlayerMotor dash must prevent tongue damage and a grab.");
                    record?.Invoke("Frog tongue respects the real player dash / grace window.");
                }

                using (var arena = new Arena())
                {
                    arena.PlacePlayer(new Vector3(0, .03f, 1));
                    var worm = arena.Worm();
                    yield return null;
                    Require(worm.Underground && worm.TelegraphVisible && worm.Health.IgnoreDamage && !worm.Health.CanBeTargeted,
                        "Buried worm must display a circular warning and disable damage / targeting.");
                    int health = arena.Player.Health;
                    var wait = new Deadline(3, "warned worm eruption");
                    while (worm.Eruptions == 0 || worm.StateTime < .21f)
                    { wait.Check(); arena.Step(); yield return null; }
                    HitFeedback.CancelHitStop();
                    Require(arena.Player.Health == health - worm.eruptionDamage && !worm.Health.IgnoreDamage,
                        "The warned eruption must deal its damage once and expose the worm.");
                    Require(arena.Owner.GetComponentsInChildren<ForestWormSoilBurst>().Length > 0,
                        "Eruption must create the torn earth and flying clod effect.");
                    ForestMonsterValidation.CaptureAttack(worm.transform.position, "worm-earth-eruption");
                    arena.PlacePlayer(new Vector3(0, .03f, 5));
                    wait = new Deadline(5, "worm charge warning");
                    while (worm.State != ForestWormState.ChargeWarning) { wait.Check(); arena.Step(); yield return null; }
                    Require(worm.IsWindingUp && worm.Charges == 0, "Charge must have a warning before motion.");
                    health = arena.Player.Health;
                    wait = new Deadline(4, "worm committed charge");
                    while (worm.Charges == 0 || worm.State == ForestWormState.Charge)
                    { wait.Check(); arena.Step(); yield return null; }
                    HitFeedback.CancelHitStop();
                    Require(worm.Charges == 1 && worm.ChargeHits == 1 && arena.Player.Health == health - worm.chargeDamage,
                        "A whole-body charge sweep must hit once, then enter recovery.");
                    Require(worm.BodyAnimationReady, "Imported worm geometry must have a readable deforming body.");
                    record?.Invoke("Moonworm circular warning → exposed eruption with Meshy soil burst → fixed line warning → one-hit wriggling charge.");
                }

                using (var arena = new Arena())
                {
                    arena.PlacePlayer(new Vector3(0, .03f, 7));
                    var worm = arena.Worm();
                    var wait = new Deadline(6, "worm fixed charge before wall");
                    while (worm.State != ForestWormState.ChargeWarning) { wait.Check(); arena.Step(); yield return null; }
                    arena.Box("Thin charge wall", new Vector3(0, 1.5f, 4.2f), new Vector3(7, 3, .04f));
                    int health = arena.Player.Health;
                    wait = new Deadline(4, "worm wall stop");
                    while (worm.Charges == 0 || worm.State == ForestWormState.Charge)
                    { wait.Check(); arena.Step(); yield return null; }
                    Require(worm.transform.localPosition.z < 4 && arena.Player.Health == health && worm.ChargeHits == 0,
                        "A fixed charge must stop at solid scenery without damaging a player behind it.");
                    record?.Invoke("Moonworm charge stops at a thin wall and cannot hit through cover.");
                }
            }
            finally { HitFeedback.CancelHitStop(); Time.timeScale = oldScale; }
        }

        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        struct Deadline
        {
            readonly float gameEnd, realEnd;
            readonly string label;
            public Deadline(float seconds, string label)
            { gameEnd = Time.time + seconds; realEnd = Time.realtimeSinceStartup + seconds * 5 + 3; this.label = label; }
            public void Check()
            {
                if (Time.time > gameEnd || Time.realtimeSinceStartup > realEnd)
                    throw new TimeoutException("Forest worm / frog validation: " + label);
            }
        }

        sealed class Arena : IDisposable
        {
            readonly Scene scene;
            readonly GameObject root;
            public Transform Owner => root.transform;
            public LiminalRoom Room { get; }
            public LiminalPlayerHealth Player { get; }
            public PlayerMotor Motor { get; }
            public CharacterController Body { get; }
            static readonly Vector3 Offset = new Vector3(12000, 0, 12000);

            public Arena()
            {
                // Existing monster movement uses Physics.*. A remote default-physics additive scene preserves those real queries.
                scene = SceneManager.CreateScene("Temporary forest combat check " + Guid.NewGuid().ToString("N"));
                root = new GameObject("Temporary forest combat arena");
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = Offset;
                Room = root.AddComponent<LiminalRoom>();
                Room.localBounds = new Bounds(Vector3.up * 3, new Vector3(50, 8, 50));
                Box("Ground", Vector3.down * .15f, new Vector3(50, .3f, 50));
                var target = new GameObject("Test player"); target.transform.SetParent(root.transform, false);
                Body = target.AddComponent<CharacterController>();
                Body.height = 1.8f; Body.radius = .32f; Body.center = Vector3.up * .9f; Body.skinWidth = .02f;
                Motor = target.AddComponent<PlayerMotor>();
                var visual = new GameObject("Player visual").transform; visual.SetParent(target.transform, false);
                Motor.visual = visual;
                Player = target.AddComponent<LiminalPlayerHealth>(); Player.SetMaximum(1000, true);
                var feedback = target.GetComponent<JustDodgeFeedback>();
                if (feedback) Object.DestroyImmediate(feedback);
                Motor.SetAutomationInput(Vector2.zero, Offset + Vector3.forward * 10);
                Physics.SyncTransforms();
            }
            public GameObject Box(string name, Vector3 position, Vector3 size)
            {
                var go = new GameObject(name); go.transform.SetParent(root.transform, false);
                go.transform.localPosition = position; go.AddComponent<BoxCollider>().size = size;
                Physics.SyncTransforms(); return go;
            }
            public void PlacePlayer(Vector3 position)
            {
                Body.enabled = false; Player.transform.localPosition = position; Body.enabled = true;
                Motor.SetAutomationInput(Vector2.zero, Player.transform.position + Vector3.forward * 10);
                Physics.SyncTransforms();
            }
            public ForestFrog Frog()
            {
                var frog = ForestMonsterFactory.Create(ForestMonsterKind.MossFrog, Offset, Quaternion.identity, Owner) as ForestFrog;
                Require(frog, "Built MossFrog prefab is required for runtime validation.");
                frog.Setup(Player, Room, 0);
                typeof(ForestFrog).GetField("nextHop", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(frog, float.MaxValue);
                typeof(ForestFrog).GetField("nextAttack", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(frog, Time.time + .2f);
                return frog;
            }
            public ForestWorm Worm()
            {
                var worm = ForestMonsterFactory.Create(ForestMonsterKind.Moonworm, Offset, Quaternion.identity, Owner) as ForestWorm;
                Require(worm, "Built Moonworm prefab is required for runtime validation.");
                worm.Setup(Player, Room, 0);
                return worm;
            }
            public void Step() => Physics.SyncTransforms();
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
