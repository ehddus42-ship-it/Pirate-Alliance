using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.Ruins
{
    /// <summary>A locked ground strike: the warning, visual and single damage check share one centre and radius.</summary>
    public sealed class RuinsGroundPulse : MonoBehaviour
    {
        public bool Finished { get; private set; }
        public bool Exploded { get; private set; }
        public bool WarningVisible => warning && warning.Visible && !Exploded && !Finished;
        public int DamageAttempts { get; private set; }
        public bool HitPlayer { get; private set; }
        public float Radius { get; private set; }
        public Vector3 Center => transform.position;
        public float Delay { get; private set; }

        TrainingEnemy owner;
        LiminalPlayerHealth target;
        LiminalRoom room;
        bool hadRoom;
        int damage;
        float age;
        Telegraph warning;
        LineRenderer shockRing;
        Material pulseMaterial;
        readonly RaycastHit[] coverHits = new RaycastHit[64];
        readonly Collider[] coverOverlaps = new Collider[64];

        public static RuinsGroundPulse Create(Vector3 center, float radius, float delay, TrainingEnemy owner,
            LiminalPlayerHealth target, LiminalRoom room, int damage)
        {
            var go = new GameObject("Medusa induction strike");
            go.transform.SetParent(room ? room.transform : owner ? owner.transform.parent : null, true);
            go.transform.position = center;
            var pulse = go.AddComponent<RuinsGroundPulse>();
            pulse.owner = owner; pulse.target = target; pulse.room = room; pulse.hadRoom = room;
            pulse.Radius = Mathf.Max(.1f, radius); pulse.Delay = Mathf.Max(.15f, delay); pulse.damage = Mathf.Max(1, damage);
            pulse.warning = Telegraph.Create(go.transform, "Locked induction warning");
            pulse.warning.Circle(center, pulse.Radius, 0);
            if (owner) owner.Defeated += pulse.OwnerDefeated;
            if (target) target.Died += pulse.Cancel;
            return pulse;
        }

        void Update()
        {
            if (Finished) return;
            if (!owner || !owner.IsAlive || !owner.isActiveAndEnabled || !target || !target.IsAlive
                || !target.isActiveAndEnabled || (hadRoom && !room)) { Cancel(); return; }
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            age += dt;
            if (!Exploded)
            {
                warning.Circle(Center, Radius, Mathf.Clamp01(age / Delay));
                if (age >= Delay) Explode();
            }
            else
            {
                float t = Mathf.Clamp01((age - Delay) / .4f);
                if (shockRing)
                {
                    shockRing.widthMultiplier = Mathf.Lerp(.19f, .02f, t);
                    shockRing.startColor = shockRing.endColor = new Color(1, .48f, .13f, 1 - t);
                    shockRing.transform.localScale = Vector3.one * Mathf.Lerp(.15f, 1, t);
                }
                if (t >= 1) Cancel();
            }
        }

        void Explode()
        {
            Exploded = true;
            warning.Release();
            Vector3 toPlayer = target.transform.position - Center;
            toPlayer.y = 0;
            var body = target.GetComponent<CharacterController>();
            float playerRadius = body ? body.radius * Mathf.Max(Mathf.Abs(target.transform.lossyScale.x), Mathf.Abs(target.transform.lossyScale.z)) : .3f;
            if (toPlayer.sqrMagnitude <= (Radius + playerRadius) * (Radius + playerRadius) && !Covered(toPlayer))
            {
                DamageAttempts++;
                HitPlayer = target.TakeDamage(damage);
            }
            if (Finished) return; // Player death may cancel this instance synchronously from TakeDamage.
            HitFeedback.Sparks(Center + Vector3.up * .15f, Vector3.up, 13, .8f);
            HitFeedback.Dust(Center, Radius * .6f);
            HitFeedback.Shake(.045f * Mathf.Clamp01(1 - toPlayer.magnitude / 13), .15f);
            var ring = new GameObject("Induction shock ring");
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = Vector3.up * .07f;
            shockRing = ring.AddComponent<LineRenderer>();
            shockRing.useWorldSpace = false; shockRing.loop = true; shockRing.positionCount = 48;
            shockRing.widthMultiplier = .19f;
            pulseMaterial = Telegraph.CreateOverlayMaterial("Induction flash");
            shockRing.sharedMaterial = pulseMaterial;
            shockRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < 48; i++)
            {
                float a = i * Mathf.PI * 2 / 48;
                shockRing.SetPosition(i, new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Radius);
            }
        }

        bool Covered(Vector3 toPlayer)
        {
            float distance = toPlayer.magnitude;
            Vector3 origin = Center + Vector3.up * .65f;
            var physics = gameObject.scene.GetPhysicsScene();
            // Rays do not report a collider containing their start. A locked circle can fall
            // inside a debris block, so such a blocked emitter must not strike through that cover.
            int count = physics.OverlapSphere(origin, .025f, coverOverlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count >= coverOverlaps.Length) return true;
            for (int i = 0; i < count; i++)
            {
                var other = coverOverlaps[i];
                if (!other || other.GetComponentInParent<TrainingEnemy>() || other.GetComponentInParent<LiminalPlayerHealth>()) continue;
                return true;
            }
            if (distance <= .05f) return false;
            count = physics.Raycast(origin, toPlayer / distance, coverHits, distance, ~0, QueryTriggerInteraction.Ignore);
            if (count >= coverHits.Length) return true;
            for (int i = 0; i < count; i++)
            {
                var other = coverHits[i].collider;
                if (!other || other.GetComponentInParent<TrainingEnemy>() || other.GetComponentInParent<LiminalPlayerHealth>()) continue;
                if (Mathf.Abs(coverHits[i].normal.y) < .6f) return true;
            }
            return false;
        }

        void OwnerDefeated(TrainingEnemy _) => Cancel();
        public void Cancel()
        {
            if (Finished) return;
            Finished = true;
            if (warning) warning.Hide();
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
        void OnDestroy()
        {
            Unsubscribe();
            if (pulseMaterial) Destroy(pulseMaterial);
        }
    }
}
