namespace AcRoguelike
{
    /// <summary>
    /// Run modifiers (augments) that reshape melee hits. MeleeSlash looks for one on the player and asks it at every
    /// cut, so combat stays independent of the run layer that owns the modifiers.
    /// </summary>
    public interface IMeleeHitHooks
    {
        /// <summary>Crit chance (0..1) for this hit, given the weapon's base chance.</summary>
        float CritChance(float baseChance);
        /// <summary>Damage multiplier of a crit, given the weapon's base multiplier (may be random).</summary>
        float CritMultiplier(float baseMultiplier);
        /// <summary>Damage multiplier of a counter (just dodge) cut, given the base multiplier (may be random).</summary>
        float CounterMultiplier(float baseMultiplier);
        /// <summary>Extra damage multiplier for one target. comboFinal: the last cut of the combo's last swing.</summary>
        float DamageMultiplier(TrainingEnemy target, bool comboFinal);
        /// <summary>A crit landed on target.</summary>
        void OnCrit(TrainingEnemy target);
        /// <summary>A cut hit at least one enemy. first: the main target.</summary>
        void OnCutLanded(TrainingEnemy first, bool comboFinal);
    }
}
