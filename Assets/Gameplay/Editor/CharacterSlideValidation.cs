#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.Editor
{
    /// <summary>Real CharacterController probes in disposable, isolated physics scenes.</summary>
    public static class CharacterSlideValidation
    {
        const string Output = "Library/CharacterSlideValidation";
        [Serializable] sealed class Result
        {
            public string name;
            public bool passed;
            public Vector3 raw, assisted;
            public float rawTravel, assistedTravel, largestExcess;
            public string error;
        }
        [Serializable] sealed class Report
        {
            public string status = "passed", utc = DateTime.UtcNow.ToString("O");
            public List<Result> cases = new List<Result>();
        }
        struct Trace
        {
            public Vector3 end;
            public float travel, excess;
            public CollisionFlags flags;
        }
        sealed class World : IDisposable
        {
            public readonly Scene scene;
            public readonly CharacterController body;
            public World(Vector3 position)
            {
                scene = EditorSceneManager.NewPreviewScene();
                var go = Create("Probe character", position);
                body = go.AddComponent<CharacterController>();
                body.radius = .32f; body.height = 1.8f; body.skinWidth = .025f;
                body.stepOffset = .2f; body.minMoveDistance = 0; body.slopeLimit = 45;
            }
            public GameObject Create(string name, Vector3 position)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.position = position;
                return go;
            }
            public BoxCollider Box(Vector3 position, Vector3 size)
            {
                var collider = Create("Obstacle", position).AddComponent<BoxCollider>();
                collider.size = size;
                return collider;
            }
            public void Ground() { Box(new Vector3(0, -.2f, 0), new Vector3(30, .4f, 30)); }
            public void Pillar(bool round)
            {
                if (!round) Box(new Vector3(0, 1.5f, 0), new Vector3(1.2f, 3, 1.2f));
                else
                {
                    var pillar = Create("Rounded decoration", new Vector3(0, 1.5f, 0)).AddComponent<CapsuleCollider>();
                    pillar.radius = .65f; pillar.height = 3;
                }
            }
            public void Dispose() { if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void RunBatch()
        {
            string json = Run();
            Debug.Log("CHARACTER_SLIDE_VALIDATION " + json);
            if (JsonUtility.FromJson<Report>(json).status != "passed")
                throw new InvalidOperationException("Character slide validation failed. See " + Output + "/report.json");
        }

        public static string Run()
        {
            var report = new Report();
            Scene previous = SceneManager.GetActiveScene();
            try
            {
                foreach (float dt in new[] { 1f / 30, 1f / 60, 1f / 120 })
                {
                    foreach (bool round in new[] { false, true })
                    {
                        bool rounded = round;
                        Pair(report, (round ? "round" : "square") + " grazing dt=" + dt.ToString("F4"),
                            new Vector3(round ? .87f : .83f, 1, -1.8f), w => w.Pillar(rounded),
                            Vector3.forward * 4, dt, .8f, (a, b) =>
                            {
                                Require(b.end.z >= a.end.z - .015f, "Assistance reduced forward progress.");
                                Require(b.end.z > .85f, "A grazing approach stayed stuck at the pillar.");
                                Budget(b);
                            });
                    }
                    Pair(report, "head-on wall dt=" + dt.ToString("F4"), new Vector3(0, 1, -1.5f),
                        w => w.Box(new Vector3(0, 1, 0), new Vector3(8, 2, .5f)),
                        Vector3.forward * 8, dt, .5f, (a, b) =>
                        {
                            Require(b.end.z < -.5f, "Crossed the wall.");
                            Require(Mathf.Abs(b.end.x) < .01f, "Head-on collision gained sideways movement.");
                            Require((b.end - a.end).magnitude < .02f, "Head-on behavior changed."); Budget(b);
                        });
                    Pair(report, "grazing wall recovers momentum dt=" + dt.ToString("F4"), new Vector3(0, 1, -.8f),
                        w => w.Box(new Vector3(0, 1, 0), new Vector3(12, 2, .5f)),
                        new Vector3(4, 0, 2), dt, .8f, (a, b) =>
                    {
                        Require(b.end.x > a.end.x + .06f, "No useful tangential movement was recovered.");
                        Require(b.end.z < -.5f, "Passed through the wall during assistance."); Budget(b);
                    });
                }
                Pair(report, "two-wall inside corner", new Vector3(-.8f, 1, -.8f), w =>
                {
                    w.Box(new Vector3(0, 1, 1), new Vector3(5, 2, .5f));
                    w.Box(new Vector3(1, 1, 0), new Vector3(.5f, 2, 5));
                }, new Vector3(1, 0, 1).normalized * 8, 1f / 60, .6f, (a, b) =>
                {
                    Require(b.end.x < .51f && b.end.z < .51f, "Escaped a closed corner."); Budget(b);
                });
                Pair(report, "flat ground", new Vector3(0, .95f, -2), w => w.Ground(),
                    new Vector3(0, -2, 4), 1f / 60, .8f, (a, b) =>
                    {
                        Require((a.end - b.end).magnitude < .015f, "Flat-ground movement changed.");
                        Require(b.end.y > .8f, "Fell through the ground."); Budget(b);
                    });
                Pair(report, "walkable ramp", new Vector3(0, 1.15f, -2), w =>
                {
                    w.Ground();
                    var ramp = w.Box(new Vector3(0, .2f, 0), new Vector3(4, .3f, 6));
                    ramp.transform.rotation = Quaternion.Euler(-15, 0, 0);
                }, new Vector3(0, -2, 4), 1f / 60, .8f, (a, b) =>
                {
                    Require((a.end - b.end).magnitude < .06f, "Slope movement was treated as obstacle assistance.");
                    Require(b.end.y > .8f, "Fell through the ramp."); Budget(b);
                });
                Pair(report, "room predicate rejects correction", new Vector3(.83f, 1, -1), w => w.Pillar(false),
                    Vector3.forward * 3, .2f, .2f, (a, b) =>
                    { Require((a.end - b.end).magnitude < .025f, "Rejected correction changed movement."); Budget(b); }, p => p.x <= .8301f);
                foreach (string type in new[] { "trigger", "ignored layer", "dynamic body", "other character" })
                {
                    string obstacleType = type;
                    bool ignoredBefore = Physics.GetIgnoreLayerCollision(0, 2);
                    try
                    {
                        if (type == "ignored layer") Physics.IgnoreLayerCollision(0, 2, true);
                        Pair(report, type + " receives no assistance", new Vector3(.83f, 1, -1.8f), w =>
                        {
                            if (obstacleType == "other character")
                            {
                                var other = w.Create("Other character", new Vector3(0, 1, 0)).AddComponent<CharacterController>();
                                other.radius = .6f; other.height = 2;
                                return;
                            }
                            var c = w.Box(new Vector3(0, 1.5f, 0), new Vector3(1.2f, 3, 1.2f));
                            if (obstacleType == "trigger") c.isTrigger = true;
                            if (obstacleType == "ignored layer") c.gameObject.layer = 2;
                            if (obstacleType == "dynamic body") c.gameObject.AddComponent<Rigidbody>().useGravity = false;
                        }, Vector3.forward * 4, 1f / 60, .8f, (a, b) =>
                        { Require((a.end - b.end).magnitude < .025f, "Excluded obstacle changed movement."); Budget(b); });
                    }
                    finally { if (type == "ignored layer") Physics.IgnoreLayerCollision(0, 2, ignoredBefore); }
                }
            }
            catch (Exception ex) { report.cases.Add(new Result { name = "suite", error = ex.ToString() }); }
            finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
            foreach (var result in report.cases) if (!result.passed) report.status = "failed";
            Directory.CreateDirectory(Output);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Output + "/report.json", json);
            return json;
        }

        static void Pair(Report report, string name, Vector3 start, Action<World> setup, Vector3 velocity,
            float dt, float duration, Action<Trace, Trace> check, Predicate<Vector3> allowed = null)
        {
            var result = new Result { name = name };
            report.cases.Add(result);
            try
            {
                Trace raw = Probe(start, setup, velocity, dt, duration, false, null);
                Trace assisted = Probe(start, setup, velocity, dt, duration, true, allowed);
                result.raw = raw.end; result.assisted = assisted.end;
                result.rawTravel = raw.travel; result.assistedTravel = assisted.travel; result.largestExcess = assisted.excess;
                check(raw, assisted); result.passed = true;
            }
            catch (Exception ex) { result.error = ex.Message; }
        }
        static Trace Probe(Vector3 start, Action<World> setup, Vector3 velocity, float dt, float duration,
            bool assisted, Predicate<Vector3> allowed)
        {
            using (var world = new World(start))
            {
                setup(world); Physics.SyncTransforms();
                var mover = new CharacterObstacleSlide(world.body, allowed);
                var trace = new Trace();
                int steps = Mathf.RoundToInt(duration / dt);
                for (int i = 0; i < steps; i++)
                {
                    Vector3 before = world.body.transform.position, step = velocity * dt;
                    trace.flags |= assisted ? mover.Move(step) : world.body.Move(step);
                    float moved = Planar(world.body.transform.position - before);
                    trace.travel += moved; trace.excess = Mathf.Max(trace.excess, moved - Planar(step));
                }
                trace.end = world.body.transform.position;
                return trace;
            }
        }
        static float Planar(Vector3 v) { return new Vector2(v.x, v.z).magnitude; }
        static void Budget(Trace t) { Require(t.excess < .006f, "Per-step planar movement exceeded its distance budget."); }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
#endif
