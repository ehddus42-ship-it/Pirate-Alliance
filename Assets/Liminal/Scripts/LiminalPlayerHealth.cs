using System;
using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    [DisallowMultipleComponent]
    public sealed class LiminalPlayerHealth : MonoBehaviour
    {
        public int maximumHealth = 100;
        [Tooltip("Maximum health at the start of a run, before augments (permanent upgrades raise it).")]
        public int baseMaximum = 100;
        [Tooltip("After a dash ends, hits are still evaded (and count as a just dodge) for this long.")]
        public float dodgeGrace = .26f;
        [Tooltip("Just-dodge leeway (m). While a dash makes the player untouchable, an attack that misses by up to this much past the edge of its hit range still counts as a just dodge, not only one that would have connected.")]
        public float justDodgeReach = 1.2f;
        public int Health { get; private set; } = 100;
        public bool IsAlive => Health > 0;
        public int JustDodges { get; private set; }
        public event Action Died;
        public event Action Changed;
        /// <summary>An attack that would have hit, or came close, was slipped through with a dash's invulnerability.</summary>
        public event Action JustDodged;
        public event Action<int> Hurt;
        float invulnerableUntil;
        int lastJustDash = -1;
        readonly List<Func<int, bool>> guards = new List<Func<int, bool>>();

        /// <summary>
        /// A shield (an augment) looks at every hit that would land; returning true absorbs it. Guards are asked in
        /// the order they were added and the first one that absorbs ends the hit.
        /// </summary>
        public void AddGuard(Func<int, bool> guard) { if (guard != null && !guards.Contains(guard)) guards.Add(guard); }
        public void RemoveGuard(Func<int, bool> guard) => guards.Remove(guard);
        PlayerMotor motor;
        CharacterController body;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            body = GetComponent<CharacterController>();
            Health = maximumHealth;
            if (!GetComponent<JustDodgeFeedback>()) gameObject.AddComponent<JustDodgeFeedback>();
        }

        bool Dodging => motor && (motor.IsDashing || Time.time - motor.LastDashStart < motor.dashDuration + dodgeGrace);

        public bool TakeDamage(int amount)
        {
            if (!IsAlive || amount <= 0) return false;
            if (Dodging) { NotifyDodged(); return false; }
            if (Time.time < invulnerableUntil) return false;
            for (int i = 0; i < guards.Count; i++)
                if (guards[i](amount))
                {
                    // A blocked hit still costs the attacker its swing: the usual short grace follows.
                    invulnerableUntil = Time.time + .5f;
                    return false;
                }
            Health = Mathf.Max(0, Health - amount);
            invulnerableUntil = Time.time + .8f;
            // Taking a hit has weight too: a hard stop, a red flash on the body and the screen, a shake.
            HitFeedback.HitStop(.075f, .07f);
            HitFeedback.Shake(.1f, .2f);
            HitFeedback.ScreenFlash(new Color(1f, .12f, .1f, .3f), .28f);
            HitFeedback.FlashObject(motor && motor.visual ? motor.visual.gameObject : gameObject, new Color(1f, .35f, .3f), .14f);
            HitFeedback.Play(HitFeedback.Sfx.Hurt, .9f);
            Hurt?.Invoke(amount);
            Changed?.Invoke();
            if (Health == 0) Died?.Invoke();
            return true;
        }

        /// <summary>
        /// Called when an attack is evaded with a dash (damage blocked by the dash, a grab that missed a dashing
        /// player, or an attack that passed close by: see NearMiss). Counts once per dash.
        /// </summary>
        public void NotifyDodged()
        {
            if (!motor || !IsAlive || motor.DashCount == lastJustDash) return;
            lastJustDash = motor.DashCount;
            JustDodges++;
            JustDodged?.Invoke();
        }

        /// <summary>True while a dash (or its short grace) makes the player untouchable.</summary>
        public bool IsEvading => IsAlive && Dodging;

        /// <summary>Radius of the body (the CharacterController) on the ground plane.</summary>
        public float BodyRadius => body ? body.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) : .3f;
        float BodyHeight => body ? body.height * Mathf.Abs(transform.lossyScale.y) : 1.8f;

        // Just-dodge leeway. A hit only counts as a just dodge when it would have landed, so an attack that misses
        // calls NearMiss after its hit test: while a dash makes the player untouchable, a miss that came within
        // justDodgeReach of connecting still counts (once per dash, like any just dodge). Outside a dodge they do
        // nothing, so they are safe to call every frame. Each returns true when the miss was close enough to count.

        /// <summary>
        /// A miss by `missedBy` metres: how far the player stood outside the attack's hit range (zero or less means
        /// inside it, e.g. a hit that cover or line of sight stopped).
        /// </summary>
        public bool NearMiss(float missedBy)
        {
            if (!IsEvading || missedBy > justDodgeReach) return false;
            NotifyDodged();
            return true;
        }

        /// <summary>A round attack (blast, pulse, eruption) that hits when the player's position is within `reach` of `center`.</summary>
        public bool NearMiss(Vector3 center, float reach)
        {
            if (!IsEvading || !NearHeight(center.y, center.y)) return false;
            Vector3 offset = transform.position - center; offset.y = 0;
            return NearMiss(offset.magnitude - reach);
        }

        /// <summary>
        /// An attack swept along a path (a shot's step this frame, a charge, a lunge) that hits when the player's
        /// position is within `reach` of the path. For a sphere sweep against the body, reach is its radius plus BodyRadius.
        /// </summary>
        public bool NearMiss(Vector3 from, Vector3 to, float reach)
        {
            if (!IsEvading || !NearHeight(Mathf.Min(from.y, to.y), Mathf.Max(from.y, to.y))) return false;
            return NearMiss(PlanarSegmentDistance(transform.position, from, to) - reach);
        }

        /// <summary>A swing over a cone from `origin` toward `forward`, `radius` long and `halfAngle` degrees to each side.</summary>
        public bool NearMissFan(Vector3 origin, Vector3 forward, float radius, float halfAngle)
        {
            if (!IsEvading || !NearHeight(origin.y, origin.y)) return false;
            Vector3 offset = transform.position - origin; offset.y = 0; forward.y = 0;
            if (forward.sqrMagnitude < .0001f || Vector3.Angle(forward, offset) <= halfAngle) return NearMiss(offset.magnitude - radius);
            // Beside the cone: the nearest part of it is the edge on the player's side.
            float side = Vector3.SignedAngle(forward, offset, Vector3.up) < 0 ? -halfAngle : halfAngle;
            Vector3 edge = Quaternion.AngleAxis(side, Vector3.up) * forward.normalized * radius;
            return NearMiss(PlanarSegmentDistance(transform.position, origin, origin + edge));
        }

        // An attack far above the head (a lobbed can at the top of its arc) or below the floor is not a near miss.
        bool NearHeight(float low, float high)
        {
            float feet = transform.position.y;
            return high >= feet - justDodgeReach && low <= feet + BodyHeight + justDodgeReach;
        }

        static float PlanarSegmentDistance(Vector3 point, Vector3 a, Vector3 b)
        {
            point.y = a.y = b.y = 0;
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0;
            return Vector3.Distance(point, a + ab * t);
        }

        /// <summary>Ignores damage for the given time (support skills are cast with a short invulnerability).</summary>
        public void GrantInvulnerability(float seconds)
        { invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + Mathf.Max(0, seconds)); }

        public void ResetHealth()
        { maximumHealth = baseMaximum; Health = maximumHealth; invulnerableUntil = Time.time + 1; Changed?.Invoke(); }

        public void SetMaximum(int value, bool refill)
        { maximumHealth = Mathf.Max(1, value); Health = refill ? maximumHealth : Mathf.Min(Health, maximumHealth); Changed?.Invoke(); }

        /// <summary>Restores health up to the maximum. Returns the amount actually restored.</summary>
        public int Heal(int amount)
        {
            if (!IsAlive || amount <= 0) return 0;
            int before = Health;
            Health = Mathf.Min(maximumHealth, Health + amount);
            if (Health != before) Changed?.Invoke();
            return Health - before;
        }

        public void IncreaseMaximum(int amount)
        { maximumHealth += amount; Health = Mathf.Min(maximumHealth, Health + amount); Changed?.Invoke(); }
    }

    /// <summary>
    /// The payoff of a just dodge:
    /// - the world drops into slow motion for a beat;
    /// - a cold violet flash and a glassy chime;
    /// - an expanding ring and a burst of afterimages around the player;
    /// - the dash is refunded;
    /// - a counter window opens: for a moment every cut lands as a heavy hit with bonus damage.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JustDodgeFeedback : MonoBehaviour
    {
        public float slowSeconds = .5f, slowScale = .22f, counterSeconds = 1.3f;
        static readonly Color Violet = new Color(.55f, .45f, 1f);

        LiminalPlayerHealth health;
        PlayerMotor motor;
        MeleeSlash melee;

        void Awake()
        {
            health = GetComponent<LiminalPlayerHealth>();
            motor = GetComponent<PlayerMotor>();
            health.JustDodged += OnJustDodged;
        }

        void OnDestroy() { if (health) health.JustDodged -= OnJustDodged; }

        void OnJustDodged()
        {
            if (!melee) melee = GetComponent<MeleeSlash>();
            HitFeedback.SlowMotion(slowSeconds, slowScale);
            HitFeedback.ScreenFlash(new Color(Violet.r, Violet.g, Violet.b, .32f), .35f);
            HitFeedback.Play(HitFeedback.Sfx.JustDodge, 1f);
            HitFeedback.Shake(.04f, .12f);
            Vector3 p = transform.position + Vector3.up * .05f;
            HitFeedback.Dust(p, 1.5f);
            HitFeedback.Sparks(transform.position + Vector3.up * 1f, Vector3.up, 14, 1.1f);
            // Looked up each time: HunterRoster can swap the body between dodges.
            var skins = (motor && motor.visual ? motor.visual : transform).GetComponentsInChildren<SkinnedMeshRenderer>();
            HitFeedback.Afterimage(skins, Violet * .8f, .6f);
            if (motor) motor.ResetDashCooldown();
            if (melee) melee.GrantCounter(counterSeconds);
        }
    }
}
