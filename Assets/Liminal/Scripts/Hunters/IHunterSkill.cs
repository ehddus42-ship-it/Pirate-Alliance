namespace AcRoguelike.Liminal
{
    /// <summary>
    /// A hunter's own active skill (an ultimate on R), living on that hunter's model so it only works while worn.
    /// HunterRoster holds swaps while one is playing; the HUD shows its cooldown.
    /// </summary>
    public interface IHunterSkill
    {
        /// <summary>Short label for the HUD.</summary>
        string Label { get; }
        /// <summary>Playing: no swapping.</summary>
        bool Busy { get; }
        float CooldownRemaining { get; }
    }
}
