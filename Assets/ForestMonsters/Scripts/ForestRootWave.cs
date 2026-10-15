using System.Collections.Generic;
using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace AcRoguelike.Forest
{
    /// <summary>A single committed root path. The arch train surfaces and submerges as its front advances.</summary>
    public sealed class ForestRootWave : MonoBehaviour
    {
        public const float Range = 11f, WarningHalfWidth = .9f;
        public float Travelled { get; private set; }
        public int DamageAttempts { get; private set; }
        public bool HitPlayer { get; private set; }
        public bool Finished { get; private set; }
        public bool AboveGround { get; private set; }
        public Vector3 Direction { get; private set; }
        public int ArchCount => arches.Count;
        TrainingEnemy owner; LiminalPlayerHealth target; LiminalRoom room;
        bool hadRoom; Vector3 origin; float dustDistance;
        Material bark, vein; Mesh rootMesh;
        readonly List<Transform> arches = new List<Transform>();
        readonly RaycastHit[] hits = new RaycastHit[96]; readonly Collider[] overlaps = new Collider[96];

        public static ForestRootWave Fire(Vector3 ground, Vector3 direction, TrainingEnemy owner, LiminalPlayerHealth target, LiminalRoom room)
        {
            var go = new GameObject("Elderwood • burrowing root arches");
            go.transform.SetParent(room ? room.transform : owner.transform.parent, true); go.transform.position = ground;
            var wave = go.AddComponent<ForestRootWave>(); wave.owner = owner; wave.target = target; wave.room = room; wave.hadRoom = room;
            wave.origin = ground; wave.Direction = ForestAttackUtility.Flat(direction); go.transform.rotation = Quaternion.LookRotation(wave.Direction);
            wave.Build(); if (owner) owner.Defeated += wave.OnOwnerDeath; if (target) target.Died += wave.Cancel;
            return wave;
        }
        void Build()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit"); if (!shader) shader = Shader.Find("Standard");
            bark = new Material(shader) { name = "Moonwood ridged root bark" }; bark.SetColor("_BaseColor", new Color(.18f, .21f, .09f));
            bark.SetFloat("_Smoothness", .23f);
            vein = ForestVfx.Material("Living root sap veins", new Color(.45f, .93f, .34f, .8f));
            rootMesh = BuildArch();
            for (int i = 0; i < 4; i++)
            {
                var arch = new GameObject("Surfacing macaroni arch " + i); arch.transform.SetParent(transform, false);
                arch.AddComponent<MeshFilter>().sharedMesh = rootMesh; arch.AddComponent<MeshRenderer>().sharedMaterial = bark;
                for (int j = 0; j < 2; j++)
                {
                    var line = ForestVfx.Line(arch.transform, "Sap seam " + j, vein, .024f, 25);
                    for (int k = 0; k <= 24; k++)
                    {
                        float t = k / 24f; line.SetPosition(k, new Vector3((j == 0 ? -1 : 1) * .13f,
                            Mathf.Sin(t * Mathf.PI) * 1.1f + .055f, Mathf.Lerp(-.9f, .9f, t)));
                    }
                }
                arch.SetActive(false); arches.Add(arch.transform);
            }
        }
        static Mesh BuildArch()
        {
            const int steps = 24, sides = 8;
            var vertices = new Vector3[(steps + 1) * sides]; var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length]; var triangles = new int[steps * sides * 6];
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                var center = new Vector3(0, Mathf.Sin(t * Mathf.PI) * 1.05f, Mathf.Lerp(-.9f, .9f, t));
                var tangent = new Vector3(0, Mathf.Cos(t * Mathf.PI) * 1.05f * Mathf.PI, 1.8f).normalized;
                var normal = Vector3.Cross(tangent, Vector3.right).normalized;
                float thickness = Mathf.Lerp(.09f, .23f, Mathf.Sin(t * Mathf.PI));
                for (int j = 0; j < sides; j++)
                {
                    float a = j / (float)sides * Mathf.PI * 2;
                    var n = Vector3.right * Mathf.Cos(a) + normal * Mathf.Sin(a);
                    int index = i * sides + j;
                    vertices[index] = center + n * (thickness * (1 + Mathf.Sin(t * 33 + a * 3) * .12f)); normals[index] = n;
                    uv[index] = new Vector2(j / (float)sides, t * 3);
                    if (i == steps) continue;
                    int next = i * sides + (j + 1) % sides, q = (i * sides + j) * 6;
                    triangles[q] = index; triangles[q + 1] = next; triangles[q + 2] = index + sides;
                    triangles[q + 3] = next; triangles[q + 4] = next + sides; triangles[q + 5] = index + sides;
                }
            }
            var mesh = new Mesh { name = "Low poly thick arched living root" }; mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.triangles = triangles; mesh.RecalculateBounds(); return mesh;
        }
        void Update()
        {
            if (Finished) return;
            if (!ForestAttackUtility.Alive(owner, target, room, hadRoom)) { Cancel(); return; }
            float dt = Time.deltaTime; if (dt <= 0) return;
            float step = Mathf.Min(4.8f * dt, Range - Travelled);
            Vector3 start = origin + Direction * Travelled + Vector3.up * .68f;
            if (ForestAttackUtility.Sweep(gameObject.scene.GetPhysicsScene(), room, start, .48f, Direction, step, null, hits, overlaps, out var wall))
            {
                Travelled += Mathf.Max(0, wall.distance);
                ForestVfx.Burst(room ? room.transform : null, origin + Direction * Travelled, new Color(.6f, .74f, .3f, .75f), 1, 24);
                Cancel(); return;
            }
            float phase = Mathf.Repeat(Travelled / 2.2f, 1); AboveGround = phase > .12f && phase < .9f;
            if (AboveGround && DamageAttempts == 0 && ForestAttackUtility.Sweep(gameObject.scene.GetPhysicsScene(), room, start, .58f,
                Direction, step, target, hits, overlaps, out var impact) && impact.player)
            { DamageAttempts++; HitPlayer = target.TakeDamage(21); }
            Travelled += step;
            for (int i = 0; i < arches.Count; i++)
            {
                float offset = Travelled - i * 1.7f;
                bool visible = offset >= 0; arches[i].gameObject.SetActive(visible); if (!visible) continue;
                float cycle = Mathf.Repeat(offset / 2.2f, 1);
                float emerge = Mathf.Sin(cycle * Mathf.PI);
                arches[i].localPosition = new Vector3(Mathf.Sin(i * 2.6f) * .1f, Mathf.Lerp(-1.3f, 0, emerge), offset - .9f);
                arches[i].localRotation = Quaternion.Euler(0, 0, (i % 2 == 0 ? 1 : -1) * 8);
            }
            if (Travelled >= dustDistance)
            {
                dustDistance += 1.1f;
                ForestVfx.Burst(room ? room.transform : null, origin + Direction * Travelled, new Color(.46f, .49f, .23f, .55f), .6f, 9);
            }
            if (Travelled >= Range - .001f) Cancel();
        }
        void OnOwnerDeath(TrainingEnemy _) => Cancel();
        public void Cancel()
        {
            if (Finished) return; Finished = true; Unsubscribe(); gameObject.SetActive(false); Destroy(gameObject);
        }
        void Unsubscribe() { if (owner) owner.Defeated -= OnOwnerDeath; if (target) target.Died -= Cancel; }
        void OnDisable() { if (!Finished) Cancel(); }
        void OnDestroy() { Unsubscribe(); if (rootMesh) Destroy(rootMesh); if (bark) Destroy(bark); if (vein) Destroy(vein); }
    }
}
