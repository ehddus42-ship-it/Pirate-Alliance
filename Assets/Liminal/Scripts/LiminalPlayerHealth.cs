using System;
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
        public int Health { get; private set; } = 100;
        public bool IsAlive => Health > 0;
        public int JustDodges { get; private set; }
        public event Action Died;
        public event Action Changed;
        /// <summary>An attack that would have hit was slipped through with a dash's invulnerability.</summary>
        public event Action JustDodged;
        public event Action<int> Hurt;
        float invulnerableUntil;
        int lastJustDash = -1;
        PlayerMotor motor;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            Health = maximumHealth;
            if (!GetComponent<JustDodgeFeedback>()) gameObject.AddComponent<JustDodgeFeedback>();
        }

        bool Dodging => motor && (motor.IsDashing || Time.time - motor.LastDashStart < motor.dashDuration + dodgeGrace);

        public bool TakeDamage(int amount)
        {
            if (!IsAlive || amount <= 0) return false;
            if (Dodging) { NotifyDodged(); return false; }
            if (Time.time < invulnerableUntil) return false;
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
        /// Called when an attack is evaded with a dash (damage blocked by the dash, or a grab that missed a
        /// dashing player). Counts once per dash.
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

        /// <summary>Ignores damage for the given time (support skills are cast with a short invulnerability).</summary>
        public void GrantInvulnerability(float seconds)
        { invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + Mathf.Max(0, seconds)); }

        public void ResetHealth()
        { maximumHealth = baseMaximum; Health = maximumHealth; invulnerableUntil = Time.time + 1; Changed?.Invoke(); }

        public void SetMaximum(int value, bool refill)
        { maximumHealth = Mathf.Max(1, value); Health = refill ? maximumHealth : Mathf.Min(Health, maximumHealth); Changed?.Invoke(); }

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
        SkinnedMeshRenderer[] skins;

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
            if (skins == null) skins = (motor && motor.visual ? motor.visual : transform).GetComponentsInChildren<SkinnedMeshRenderer>();
            HitFeedback.Afterimage(skins, Violet * .8f, .6f);
            if (motor) motor.ResetDashCooldown();
            if (melee) melee.GrantCounter(counterSeconds);
        }
    }
}
