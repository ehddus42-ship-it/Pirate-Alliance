using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// A playable hunter that HunterRoster can swap in with F. The scene's own player model stays the first hunter;
    /// every profile under Resources/Hunters adds one more. A hunter is a body and a combo on the one player:
    /// PlayerMotor, health, augments and lobby upgrades stay, only the model, its Animator and the melee combo change.
    /// The Animator controller needs Base Layer states Locomotion (float Speed 0..1), Attack1.. per swing and Dash.
    /// An optional everyday outfit (everydayModel) is worn with G: the same hunter, moving only.
    /// </summary>
    public class HunterProfile : ScriptableObject
    {
        [Tooltip("Matches AugmentDefinition.characterId of this hunter's own augments.")]
        public string characterId = "hunter";
        public string displayName = "헌터";
        [Tooltip("Roster order after the scene's own hunter.")]
        public int order;
        [Tooltip("Rigged model (feet at the origin, facing +Z). Spawned under the player and switched on by the roster.")]
        public GameObject model;
        public RuntimeAnimatorController controller;
        [Tooltip("Bone whose rest height is matched to the first hunter's head, so every hunter fills the same body. Empty keeps the model's own size.")]
        public string headBone = "Head";

        [Header("Combo (PlayerCombat / MeleeSlash)")]
        [Tooltip("Seconds per swing at attack speed 1; the swing count is the combo length.")]
        public float[] attackDurations = { .5f, .5f, .7f };
        public float[] hitTimes = { .2f, .2f, .3f };
        [Range(0, 1)] public float attackMovementMultiplier = .05f;
        public float comboQueueWindow = .3f, animationBlend = .06f, returnBlend = .2f;
        public MeleeSlash.Swing[] swings = new MeleeSlash.Swing[0];
        [Tooltip("Katana look (the sword, slash arcs and petals). Off for unarmed hunters.")]
        public bool blade;

        [Header("Everyday outfit (G, optional)")]
        [Tooltip("The same hunter in her everyday clothes (same rig). G switches to it inside a gate: she only walks and runs, no attacks, dash or skill.")]
        public GameObject everydayModel;
        [Tooltip("Locomotion (float Speed 0..1) for the everyday model.")]
        public RuntimeAnimatorController everydayController;

        /// <summary>Adds the hunter's own components to the freshly spawned model (still inactive).</summary>
        public virtual void Configure(GameObject visual, Animator animator) { }

        /// <summary>Adds the everyday outfit's own components to its freshly spawned model (still inactive).</summary>
        public virtual void ConfigureEveryday(GameObject visual, Animator animator) { }

        /// <summary>Cast and line-of-sight origin per swing; null keeps a chest-height origin on the model.</summary>
        public virtual Transform[] AttackOrigins(GameObject visual, Animator animator) => null;
    }
}
