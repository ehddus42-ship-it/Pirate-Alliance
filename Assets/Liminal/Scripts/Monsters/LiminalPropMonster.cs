using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// Shared body of the office-prop monsters (copier, locker, monitor turret). A concept-map prop is the
    /// visual. This base owns everything the three have in common:
    /// - health (<see cref="TrainingEnemy"/>) and grounded movement inside the room;
    /// - the hit reaction: a damped spring rocks the prop away from the hit, with an impact squash, a knockback
    ///   and a short stagger;
    /// - the death: knocked flying, it falls over, crashes with dust and a camera shake, lies there, then sinks;
    /// - premium floor telegraphs (<see cref="Telegraph"/>) and distance-scaled "the floor shook" camera shakes.
    /// Subclasses only write a procedural pose (<see cref="poseOffset"/>, <see cref="poseRotation"/>,
    /// <see cref="poseScale"/>) and their attacks in <see cref="Think"/>.
    /// </summary>
    [RequireComponent(typeof(TrainingEnemy))]
    public abstract class LiminalPropMonster : MonoBehaviour
    {
        public string displayName = "사무용품";
        public TrainingEnemy Health { get; private set; }
        public LiminalPlayerHealth Target => player;
        public bool IsWindingUp { get; protected set; }
        public bool DeathFinished { get; private set; }

        protected LiminalPlayerHealth player;
        protected LiminalRoom room;
        protected CharacterController body;
        CharacterObstacleSlide movement;
        /// <summary>Hit-reaction pivot (at the feet). Its child <see cref="pose"/> carries the procedural animation.</summary>
        protected Transform visual, pose;
        protected Telegraph telegraph;
        protected int stage;
        protected Vector3 poseOffset;
        protected Quaternion poseRotation = Quaternion.identity;
        protected Vector3 poseScale = Vector3.one;
        protected float staggerUntil;
        protected readonly List<Object> owned = new List<Object>();

        Vector3 knockback, tiltAxis = Vector3.right;
        float tilt, tiltVelocity, squash;
        bool dying;

        /// <summary>Knockback and tilt multiplier for the current action (heavy actions resist).</summary>
        protected virtual float Weight => 1;
        protected virtual float HitTilt => 12;
        protected virtual float KnockbackSpeed => 3.2f;
        protected virtual bool CanMove => true;
        protected abstract int MaxHealth(int stageIndex);
        protected abstract void Think(float dt, Vector3 toPlayer, float distance);
        protected virtual void OnSetup() { }
        protected virtual void OnHit(Vector3 direction, float impact) { }
        protected virtual void OnDeathStart(Vector3 direction) { }
        /// <summary>Half the thickness of the prop along the side it falls onto, so it lies on the floor.</summary>
        protected virtual float LyingHalfDepth => .4f;

        public void Setup(LiminalPlayerHealth target, LiminalRoom owner, int stageIndex)
        {
            player = target;
            room = owner;
            stage = stageIndex;
            Health = GetComponent<TrainingEnemy>();
            body = GetComponent<CharacterController>();
            visual = transform.Find("Visual");
            pose = visual ? visual.Find("Pose") : null;
            Health.Configure(MaxHealth(stageIndex), false);
            Health.deferDeathVisuals = true;
            Health.Damaged -= OnDamaged; Health.Damaged += OnDamaged;
            Health.Defeated -= OnDefeated; Health.Defeated += OnDefeated;
            if (!telegraph) telegraph = Telegraph.Create(transform);
            OnSetup();
        }

        void Update()
        {
            if (!Health || !Health.IsAlive || dying || Time.deltaTime <= 0) return;
            if (!player) player = FindFirstObjectByType<LiminalPlayerHealth>();
            if (!player || !player.IsAlive) { if (telegraph) telegraph.Hide(); return; }
            float dt = Time.deltaTime;
            if (knockback.sqrMagnitude > .0025f)
            {
                if (CanMove) MoveBody(knockback * dt);
                knockback = Vector3.MoveTowards(knockback, Vector3.zero, 20f * dt);
            }
            Vector3 to = player.transform.position - transform.position;
            to.y = 0;
            Think(dt, to, to.magnitude);
        }

        void LateUpdate()
        {
            if (!visual || dying) return;
            float dt = Time.deltaTime;
            if (dt > 0)
            {
                // Under-damped spring: tips away from the hit, swings back past upright, settles.
                const float omega = 17f, damping = .3f;
                tiltVelocity += (-omega * omega * tilt - 2 * damping * omega * tiltVelocity) * dt;
                tilt = Mathf.Clamp(tilt + tiltVelocity * dt, -HitTilt * 1.8f, HitTilt * 2.4f);
                squash = Mathf.MoveTowards(squash, 0, .6f * dt);
            }
            visual.localRotation = Quaternion.AngleAxis(tilt, Quaternion.Inverse(transform.rotation) * tiltAxis);
            visual.localScale = new Vector3(1 + squash * .55f, 1 - squash, 1 + squash * .55f);
            if (pose)
            {
                pose.localPosition = poseOffset;
                pose.localRotation = poseRotation;
                pose.localScale = poseScale;
            }
        }

        void OnDamaged(TrainingEnemy enemy, int amount)
        {
            if (!enemy.IsAlive || dying) return;
            Vector3 direction = HitDirection();
            float impact = Mathf.Clamp(enemy.LastHitImpact, .3f, 2f);
            float weight = Weight;
            knockback += direction * KnockbackSpeed * impact * weight;
            Vector3 axis = Vector3.Cross(Vector3.up, direction);
            if (axis.sqrMagnitude > .001f) tiltAxis = axis.normalized;
            tiltVelocity += HitTilt * 15f * impact * Mathf.Lerp(.45f, 1f, weight);
            squash = Mathf.Max(squash, .08f * impact);
            if (weight >= .99f) staggerUntil = Time.time + .18f * Mathf.Min(impact, 1.6f);
            OnHit(direction, impact);
        }

        protected Vector3 HitDirection()
        {
            Vector3 direction = Health ? Health.LastHitDirection : Vector3.zero;
            if (direction.sqrMagnitude < .01f && player) direction = transform.position - player.transform.position;
            direction.y = 0;
            return direction.sqrMagnitude > .001f ? direction.normalized : -transform.forward;
        }

        // ---- movement --------------------------------------------------------------------------------------
        /// <summary>Moves the body horizontally, never out of the room. Returns the distance actually travelled.</summary>
        protected float MoveBody(Vector3 step)
        {
            if (!body || !body.enabled) return 0;
            step.y = 0;
            if (room && !room.Contains(transform.position + step, 1f)) step = Vector3.zero;
            Vector3 before = transform.position;
            if (movement == null) movement = new CharacterObstacleSlide(body, CanOccupy);
            movement.Move(step + Vector3.down * (4f * Time.deltaTime));
            return Vector3.ProjectOnPlane(transform.position - before, Vector3.up).magnitude;
        }

        bool CanOccupy(Vector3 position) => !room || room.Contains(position, 1f);

        protected Vector3 Steer(Vector3 preferred, float probe = 1.1f)
        {
            if (preferred.sqrMagnitude < .0001f) return Vector3.zero;
            preferred.Normalize();
            if (Clear(preferred, probe)) return preferred;
            for (int angle = 35; angle <= 140; angle += 35)
            {
                Vector3 a = Quaternion.Euler(0, angle, 0) * preferred;
                if (Clear(a, probe)) return a;
                Vector3 b = Quaternion.Euler(0, -angle, 0) * preferred;
                if (Clear(b, probe)) return b;
            }
            return Vector3.zero;
        }

        protected bool Clear(Vector3 direction, float distance)
        {
            float radius = body ? body.radius * .85f : .4f;
            foreach (var hit in Physics.SphereCastAll(transform.position + Vector3.up * .9f, radius, direction, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
                if (hit.collider.GetComponentInParent<LiminalPlayerHealth>()) continue;
                return false;
            }
            return true;
        }

        protected void Face(Vector3 direction, float degreesPerSecond)
        {
            direction.y = 0;
            if (direction.sqrMagnitude > .001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), degreesPerSecond * Time.deltaTime);
        }

        protected bool CanSeePlayer(float height = 1f)
        {
            if (!player) return false;
            Vector3 origin = transform.position + Vector3.up * height;
            Vector3 to = player.transform.position + Vector3.up * .9f - origin;
            foreach (var hit in Physics.RaycastAll(origin, to.normalized, to.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
                if (hit.collider.GetComponentInParent<LiminalPlayerHealth>()) continue;
                if (hit.collider.GetComponentInParent<LiminalPropMonster>()) continue;
                return false;
            }
            return true;
        }

        /// <summary>A heavy landing: dust ring and a camera shake that fades with distance from the player.</summary>
        protected void Thud(Vector3 point, float strength)
        {
            HitFeedback.Dust(new Vector3(point.x, transform.position.y, point.z), .7f + strength * .6f);
            float distance = player ? Vector3.Distance(player.transform.position, point) : 6;
            float near = Mathf.Clamp01(1.25f - distance / 16f);
            if (near > 0) HitFeedback.Shake(.05f * strength * near, .1f + .08f * strength);
        }

        // ---- death -----------------------------------------------------------------------------------------
        void OnDefeated(TrainingEnemy _)
        {
            if (dying) return;
            dying = true;
            IsWindingUp = false;
            if (telegraph) telegraph.Hide();
            if (body) body.enabled = false;
            StartCoroutine(Death());
        }

        IEnumerator Death()
        {
            Vector3 direction = HitDirection();
            float impact = Mathf.Clamp(Health ? Health.LastHitImpact : 1, .6f, 2f);
            OnDeathStart(direction);
            if (visual) { visual.localRotation = Quaternion.identity; visual.localScale = Vector3.one; }
            float ground = transform.position.y;
            Vector3 velocity = direction * (3.2f + 2.4f * impact) + Vector3.up * (4.8f + 1.3f * impact);
            Quaternion start = transform.rotation;
            // Falls over backwards, away from the attacker, and lands on its back.
            Quaternion lying = Quaternion.AngleAxis(90f, Vector3.Cross(Vector3.up, direction)) * Quaternion.LookRotation(-direction);
            float air = 0;
            int bounces = 0;
            while (bounces < 2 && air < 3f)
            {
                float dt = Time.deltaTime;
                air += dt;
                velocity.y -= 22f * dt;
                Vector3 p = transform.position + velocity * dt;
                float tip = Mathf.SmoothStep(0, 1, air / .5f);
                transform.rotation = Quaternion.Slerp(start, lying, tip);
                float floor = ground + LyingHalfDepth * tip;
                if (p.y <= floor && velocity.y < 0)
                {
                    p.y = floor;
                    bounces++;
                    velocity = new Vector3(velocity.x * .3f, bounces == 1 ? 2.2f : 0, velocity.z * .3f);
                    Thud(p + direction * .6f, bounces == 1 ? 1.6f : .8f);
                    if (bounces == 1) HitFeedback.Sparks(p + Vector3.up * .3f, Vector3.up, 12, 1.1f);
                }
                transform.position = p;
                yield return null;
            }
            transform.rotation = lying;
            yield return new WaitForSeconds(1.1f);
            for (float t = 0; t < .8f; t += Time.deltaTime)
            {
                transform.position += Vector3.down * (1.2f * Time.deltaTime);
                yield return null;
            }
            HitFeedback.Dust(new Vector3(transform.position.x, ground, transform.position.z), .6f);
            if (Health) Health.HideNow();
            DeathFinished = true;
        }

        protected virtual void OnDestroy()
        {
            if (Health) { Health.Damaged -= OnDamaged; Health.Defeated -= OnDefeated; }
            foreach (var o in owned) if (o) Destroy(o);
        }

        // ---- telegraphs ------------------------------------------------------------------------------------
        public bool TelegraphVisible => telegraph && telegraph.Visible;

        protected void TelegraphFan(Vector3 origin, Vector3 forward, float radius, float halfAngle, float progress)
        {
            if (!telegraph) return;
            origin.y = transform.position.y;
            telegraph.Fan(origin, forward, radius, halfAngle, progress);
        }

        protected void TelegraphLine(Vector3 origin, Vector3 forward, float length, float halfWidth, float progress)
        {
            if (!telegraph) return;
            origin.y = transform.position.y;
            telegraph.Line(origin, forward, length, halfWidth, progress);
        }

        /// <summary>The telegraphed attack fires now: the telegraph bursts and fades.</summary>
        protected void HideTelegraph() { if (telegraph) telegraph.Release(); }
    }
}
