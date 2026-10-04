using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.Ruins
{
    /// <summary>A visible, committed shot. Its swept sphere is the damage footprint, including at the muzzle.</summary>
    public sealed class RuinsProjectile : MonoBehaviour
    {
        public float Speed { get; private set; }
        public float CollisionRadius { get; private set; }
        public float Travelled { get; private set; }
        public float MaximumTravel => Energy ? 16f : 18f;
        public int Damage { get; private set; }
        public int DamageAttempts { get; private set; }
        public bool HitPlayer { get; private set; }
        public bool Finished { get; private set; }
        public bool Energy { get; private set; }
        public Vector3 Direction => direction;

        TrainingEnemy owner;
        LiminalPlayerHealth target;
        LiminalRoom room;
        bool hadRoom;
        Vector3 direction;
        Transform visual;
        float age;
        readonly RaycastHit[] hits = new RaycastHit[128];
        readonly Collider[] overlaps = new Collider[128];

        public static RuinsProjectile Fire(Vector3 position, Vector3 direction, TrainingEnemy owner,
            LiminalPlayerHealth target, LiminalRoom room, int damage, bool energy = false)
        {
            var go = new GameObject(energy ? "Carrion amber bolt" : "Bulwark scrap shell");
            go.transform.SetParent(room ? room.transform : owner ? owner.transform.parent : null, true);
            go.transform.position = position;
            var shot = go.AddComponent<RuinsProjectile>();
            shot.owner = owner; shot.target = target; shot.room = room; shot.hadRoom = room;
            direction.y = 0;
            shot.direction = direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;
            shot.Energy = energy; shot.Damage = Mathf.Max(1, damage);
            shot.Speed = energy ? 10.8f : 7.4f;
            shot.CollisionRadius = energy ? .18f : .30f;
            go.transform.rotation = Quaternion.LookRotation(shot.direction);
            shot.BuildVisual();
            if (owner) owner.Defeated += shot.OwnerDefeated;
            if (target) target.Died += shot.Cancel;
            return shot;
        }

        void BuildVisual()
        {
            visual = new GameObject("Shot visual").transform;
            visual.SetParent(transform, false);
            if (Energy)
            {
                RuinsVisual.Part(visual, "Hot core", PrimitiveType.Sphere, Vector3.zero,
                    new Vector3(.23f, .23f, .42f), RuinsVisual.Amber);
                for (int i = 0; i < 3; i++)
                    RuinsVisual.Part(visual, "Ion wake " + i, PrimitiveType.Sphere, new Vector3(0, 0, -.25f - .18f * i),
                        Vector3.one * (.13f - i * .025f), RuinsVisual.Amber);
            }
            else
            {
                RuinsVisual.Part(visual, "Welded projectile", PrimitiveType.Cube, Vector3.zero,
                    new Vector3(.36f, .36f, .57f), RuinsVisual.Iron).localRotation = Quaternion.Euler(0, 0, 35);
                RuinsVisual.Part(visual, "Hot nose", PrimitiveType.Sphere, new Vector3(0, 0, .29f),
                    Vector3.one * .3f, RuinsVisual.Amber);
                for (int i = 0; i < 3; i++)
                {
                    float angle = i * 120f;
                    var fin = RuinsVisual.Part(visual, "Stabilizer " + i, PrimitiveType.Cube,
                        Quaternion.Euler(0, 0, angle) * new Vector3(.19f, 0, -.19f),
                        new Vector3(.23f, .065f, .26f), RuinsVisual.Brass);
                    fin.localRotation = Quaternion.Euler(0, 0, angle);
                }
            }
        }

        void Update()
        {
            if (Finished) return;
            if (!owner || !owner.IsAlive || !owner.isActiveAndEnabled || !target || !target.IsAlive
                || !target.isActiveAndEnabled || (hadRoom && !room) || (room && !room.Contains(transform.position)))
            { Cancel(); return; }
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            float travel = Mathf.Min(Speed * Mathf.Min(dt, Mathf.Max(0, 7f - age)), MaximumTravel - Travelled);
            age += dt;
            if (visual) visual.Rotate(Vector3.forward, (Energy ? 210 : 490) * dt, Space.Self);
            if (RuinsSweep.Cast(gameObject.scene.GetPhysicsScene(), room, transform.position, CollisionRadius,
                direction, travel, target, hits, overlaps, out var impact))
            {
                transform.position += direction * Mathf.Max(0, impact.distance);
                Travelled += Mathf.Max(0, impact.distance);
                if (impact.player)
                {
                    DamageAttempts++;
                    HitPlayer = impact.player.TakeDamage(Damage);
                }
                HitFeedback.Sparks(transform.position, impact.normal, Energy ? 4 : 7, Energy ? .35f : .55f);
                Cancel();
                return;
            }
            transform.position += direction * travel;
            Travelled += travel;
            if (age >= 7f || Travelled >= MaximumTravel - .0001f) Cancel();
        }

        void OwnerDefeated(TrainingEnemy _) => Cancel();
        public void Cancel()
        {
            if (Finished) return;
            Finished = true;
            Unsubscribe();
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
        void Unsubscribe()
        {
            if (owner) owner.Defeated -= OwnerDefeated;
            if (target) target.Died -= Cancel;
        }
        void OnDisable() { if (!Finished) Cancel(); }
        void OnDestroy() => Unsubscribe();
    }

    internal static class RuinsSweep
    {
        internal struct Impact
        {
            public float distance;
            public Vector3 normal;
            public LiminalPlayerHealth player;
        }

        internal static bool Cast(PhysicsScene physics, LiminalRoom room, Vector3 origin, float radius,
            Vector3 direction, float distance, LiminalPlayerHealth target, RaycastHit[] hits, Collider[] overlaps, out Impact impact)
        {
            impact = new Impact { distance = distance, normal = -direction };
            bool found = false;
            int count = physics.OverlapSphere(origin, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count >= overlaps.Length) { impact.distance = 0; return true; }
            for (int i = 0; i < count; i++)
            {
                Collider other = overlaps[i];
                if (!other || other.GetComponentInParent<TrainingEnemy>()) continue;
                var player = other.GetComponentInParent<LiminalPlayerHealth>();
                if (player && player != target) continue;
                Vector3 normal = origin - other.ClosestPoint(origin);
                if (!player)
                {
                    if (normal.sqrMagnitude < .0001f) normal = -direction;
                    else
                    {
                        normal.Normalize();
                        if (Mathf.Abs(normal.y) > .6f || Vector3.Dot(normal, direction) >= -.0001f) continue;
                        normal = Vector3.ProjectOnPlane(normal, Vector3.up).normalized;
                    }
                }
                if (found && !impact.player && player) continue;
                impact = new Impact { distance = 0, normal = normal.sqrMagnitude > .01f ? normal.normalized : -direction, player = player };
                found = true;
            }
            count = physics.SphereCast(origin, radius, direction, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            if (count >= hits.Length) { impact.distance = 0; return true; }
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (!hit.collider || hit.distance > impact.distance || hit.collider.GetComponentInParent<TrainingEnemy>()) continue;
                var player = hit.collider.GetComponentInParent<LiminalPlayerHealth>();
                if (player && player != target) continue;
                if (found && hit.distance == impact.distance && !impact.player && player) continue;
                if (!player && Mathf.Abs(hit.normal.y) > .6f) continue;
                Vector3 normal = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
                if (!player && normal.sqrMagnitude < .01f) continue;
                impact = new Impact { distance = hit.distance, normal = normal.sqrMagnitude > .01f ? normal.normalized : -direction, player = player };
                found = true;
            }
            if (!room) return found;
            Vector3 local = room.transform.InverseTransformPoint(origin);
            Vector3 delta = room.transform.InverseTransformVector(direction);
            Vector3 scale = room.transform.lossyScale;
            for (int axis = 0; axis <= 2; axis += 2)
            {
                if (Mathf.Abs(delta[axis]) < .0001f) continue;
                float inset = radius / Mathf.Max(.001f, Mathf.Abs(scale[axis]));
                float lower = room.localBounds.min[axis] + inset, upper = room.localBounds.max[axis] - inset;
                float travel = ((delta[axis] > 0 ? upper : lower) - local[axis]) / delta[axis];
                if (travel < -.01f)
                {
                    bool outward = (local[axis] > upper && delta[axis] > 0) || (local[axis] < lower && delta[axis] < 0);
                    if (!outward) continue;
                    travel = 0;
                }
                if (travel > impact.distance) continue;
                Vector3 normal = Vector3.zero; normal[axis] = delta[axis] > 0 ? -1 : 1;
                impact = new Impact { distance = Mathf.Max(0, travel), normal = room.transform.TransformDirection(normal).normalized };
                found = true;
            }
            return found;
        }
    }

    internal static class RuinsVisual
    {
        static Material iron, brass, amber;
        internal static Material Iron => iron ? iron : iron = Material("Ruins gunmetal", new Color(.115f, .135f, .12f), .65f, .3f, false);
        internal static Material Brass => brass ? brass : brass = Material("Ruins weathered brass", new Color(.37f, .265f, .13f), .6f, .24f, false);
        internal static Material Amber => amber ? amber : amber = Material("Ruins amber cores", new Color(1, .36f, .04f), .15f, .4f, true);

        internal static Material Material(string name, Color color, float metallic, float smoothness, bool emissive)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            var material = new Material(shader) { name = name, enableInstancing = true };
            material.SetColor("_BaseColor", color); material.SetColor("_Color", color);
            material.SetFloat("_Metallic", metallic); material.SetFloat("_Smoothness", smoothness);
            if (emissive) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 1.3f); }
            return material;
        }

        internal static Transform Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var existing = parent.Find(name);
            var go = existing ? existing.gameObject : GameObject.CreatePrimitive(type);
            go.name = name; go.layer = 2;
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, Quaternion.identity);
            go.transform.localScale = scale;
            var collider = go.GetComponent<Collider>();
            if (collider)
            {
                collider.enabled = false;
                if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);
            }
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }
    }
}
