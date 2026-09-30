using System;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    [DisallowMultipleComponent]
    public sealed class LiminalPlayerHealth : MonoBehaviour
    {
        public int maximumHealth = 100;
        public int Health { get; private set; } = 100;
        public bool IsAlive => Health > 0;
        public event Action Died;
        public event Action Changed;
        float invulnerableUntil;
        PlayerMotor motor;

        void Awake() { motor = GetComponent<PlayerMotor>(); Health = maximumHealth; }

        public bool TakeDamage(int amount)
        {
            if (!IsAlive || amount <= 0 || Time.time < invulnerableUntil || (motor && motor.IsDashing)) return false;
            Health = Mathf.Max(0, Health - amount);
            invulnerableUntil = Time.time + .8f;
            Changed?.Invoke();
            if (Health == 0) Died?.Invoke();
            return true;
        }

        /// <summary>Ignores damage for the given time (support skills are cast with a short invulnerability).</summary>
        public void GrantInvulnerability(float seconds)
        { invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + Mathf.Max(0, seconds)); }

        public void ResetHealth()
        { maximumHealth = 100; Health = maximumHealth; invulnerableUntil = Time.time + 1; Changed?.Invoke(); }

        public void IncreaseMaximum(int amount)
        { maximumHealth += amount; Health = Mathf.Min(maximumHealth, Health + amount); Changed?.Invoke(); }
    }
}
