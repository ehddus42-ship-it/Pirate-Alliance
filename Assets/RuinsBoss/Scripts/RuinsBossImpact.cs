using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.RuinsBoss
{
    /// <summary>A fixed artillery footprint with cover-tested damage and an explosion rim, revealed only on impact.</summary>
    [DefaultExecutionOrder(35)]
    public sealed class RuinsBossImpact : MonoBehaviour
    {
        public float Radius { get; private set; }
        public float Delay { get; private set; }
        public Vector3 Center => transform.position;
        public bool Finished { get; private set; }
        public bool Exploded { get; private set; }
        public bool HitPlayer { get; private set; }
        public int DamageAttempts { get; private set; }
        public bool WarningVisible => false;

        TrainingEnemy owner;
        LiminalPlayerHealth target;
        LiminalRoom room;
        RuinsStormBoss ownerBoss;
        RuinsWarMachine ownerMachine;
        bool hadRoom, hadBrain;
        int damage;
        float age, startedAt;
        LineRenderer ring, column;
        readonly RaycastHit[] hits = new RaycastHit[64];
        readonly Collider[] overlaps = new Collider[64];

        public static RuinsBossImpact Create(Vector3 center, float radius, float delay, TrainingEnemy owner,
            LiminalPlayerHealth target, LiminalRoom room, int damage)
        {
            var go = new GameObject("Locked artillery impact");
            go.transform.SetParent(room ? room.transform : owner ? owner.transform.parent : null, true);
            go.transform.position = center;
            var impact = go.AddComponent<RuinsBossImpact>();
            impact.owner = owner; impact.target = target; impact.room = room; impact.hadRoom = room;
            if (owner)
            {
                impact.ownerBoss = owner.GetComponent<RuinsStormBoss>();
                impact.ownerMachine = owner.GetComponent<RuinsWarMachine>();
                impact.hadBrain = impact.ownerBoss || impact.ownerMachine;
            }
            impact.startedAt = Time.time;
            impact.Radius = Mathf.Max(.1f, radius); impact.Delay = Mathf.Max(1.5f, delay); impact.damage = Mathf.Max(1, damage);
            if (owner) owner.Defeated += impact.OwnerDefeated;
            if (target) target.Died += impact.Cancel;
            return impact;
        }

        void Update()
        {
            if (Finished) return;
            if (!owner || !owner.IsAlive || !owner.isActiveAndEnabled || !target || !target.IsAlive || !target.isActiveAndEnabled
                || (hadBrain && !ownerBoss && !ownerMachine)
                || (ownerBoss && (ownerBoss.EncounterCancelled || !ownerBoss.isActiveAndEnabled))
                || (ownerMachine && (ownerMachine.EncounterCancelled || !ownerMachine.isActiveAndEnabled))
                || (hadRoom && !room) || (room && !room.gameObject.activeInHierarchy)) { Cancel(); return; }
            if (Time.deltaTime <= 0) return;
            age = Mathf.Max(0, Time.time - startedAt);
            if (!Exploded)
            {
                if (age >= Delay) Detonate();
            }
            else
            {
                float t = Mathf.Clamp01((age - Delay) / .48f);
                if (ring)
                {
                    ring.transform.localScale = Vector3.one * Mathf.Lerp(.1f, 1, t);
                    ring.widthMultiplier = Mathf.Lerp(.2f, .01f, t);
                    ring.startColor = ring.endColor = new Color(1, .55f, .17f, 1 - t);
                }
                if (column)
                {
                    column.widthMultiplier = Mathf.Lerp(.42f, .02f, t);
                    column.startColor = new Color(1, .8f, .42f, 1 - t); column.endColor = new Color(1, .35f, .07f, 0);
                    column.SetPosition(1, Vector3.up * Mathf.Lerp(.35f, 2.4f, t));
                }
                if (t >= 1) Cancel();
            }
        }

        void Detonate()
        {
            Exploded = true;
            Vector3 to = target.transform.position - Center; to.y = 0;
            var body = target.GetComponent<CharacterController>();
            float bodyRadius = body ? body.radius * Mathf.Max(Mathf.Abs(target.transform.lossyScale.x), Mathf.Abs(target.transform.lossyScale.z)) : .3f;
            if (to.sqrMagnitude <= (Radius + bodyRadius) * (Radius + bodyRadius) && !Covered(to))
            { DamageAttempts++; HitPlayer = target.TakeDamage(damage); }
            if (Finished) return;
            HitFeedback.Sparks(Center + Vector3.up * .15f, Vector3.up, 20, 1.05f);
            HitFeedback.Dust(Center, Radius * .75f);
            HitFeedback.Shake(.08f * Mathf.Clamp01(1 - to.magnitude / 15), .2f);
            ring = MakeLine("Blast perimeter", 48, true);
            for (int i = 0; i < 48; i++)
            {
                float angle = i * Mathf.PI * 2 / 48;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle), .08f, Mathf.Sin(angle)) * Radius);
            }
            column = MakeLine("Blast plume", 2, false);
            column.SetPosition(0, Vector3.up * .1f); column.SetPosition(1, Vector3.up * .6f);
            column.widthMultiplier = .42f;
        }

        LineRenderer MakeLine(string name, int count, bool loop)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false; line.positionCount = count; line.loop = loop;
            line.sharedMaterial = HitFeedback.Additive; line.widthMultiplier = .2f;
            line.startColor = line.endColor = new Color(1, .68f, .25f, .95f);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        bool Covered(Vector3 to)
        {
            var physics = gameObject.scene.GetPhysicsScene();
            Vector3 origin = Center + Vector3.up * .65f;
            int count = physics.OverlapSphere(origin, .025f, overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count >= overlaps.Length) return true;
            for (int i = 0; i < count; i++)
            {
                var collider = overlaps[i];
                if (collider && !collider.GetComponentInParent<TrainingEnemy>() && !collider.GetComponentInParent<LiminalPlayerHealth>()) return true;
            }
            float distance = to.magnitude;
            if (distance < .025f) return false;
            count = physics.Raycast(origin, to / distance, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            if (count >= hits.Length) return true;
            for (int i = 0; i < count; i++)
            {
                var collider = hits[i].collider;
                if (collider && !collider.GetComponentInParent<TrainingEnemy>() && !collider.GetComponentInParent<LiminalPlayerHealth>()
                    && Mathf.Abs(hits[i].normal.y) < .6f) return true;
            }
            return false;
        }

        void OwnerDefeated(TrainingEnemy _) => Cancel();
        public void Cancel()
        {
            if (Finished) return;
            Finished = true;
            Unsubscribe(); gameObject.SetActive(false); Destroy(gameObject);
        }
        void Unsubscribe()
        {
            if (owner) owner.Defeated -= OwnerDefeated;
            if (target) target.Died -= Cancel;
        }
        void OnDisable() { if (!Finished) Cancel(); }
        void OnDestroy() => Unsubscribe();
    }
}
