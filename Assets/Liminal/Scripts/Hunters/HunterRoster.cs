using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// F (gamepad Y) swaps the playable hunter inside a gate. Every hunter shares the one player: PlayerMotor, health,
    /// augments, lobby upgrades and the just-dodge counter stay; a swap changes the body (model + Animator), the combo
    /// timing and the melee swings. The scene's own model is the first hunter; HunterProfile assets under
    /// Resources/Hunters add the others. The lobby always shows the first hunter (her everyday clothes are hers), and
    /// the picked hunter comes back when the gate opens.
    /// G (gamepad d-pad up) puts the worn hunter in her everyday clothes when her profile has them (everydayModel):
    /// she only walks and runs (no attacks, talismans, dash or skill) until G again; a swap or the lobby dresses her
    /// for battle again.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class HunterRoster : MonoBehaviour
    {
        public const string FirstHunterName = "아스트라이아";
        public const string ProfileFolder = "Hunters";
        [Tooltip("Seconds between two swaps.")]
        public float swapCooldown = .6f;

        sealed class Hunter
        {
            public string id, name;
            public GameObject visual;
            public Animator animator;
            public float[] durations, hitTimes;
            public float movement, queueWindow, blend, returnBlend;
            public MeleeSlash.Swing[] swings;
            public bool blade;
            public Transform[] origins;
            public Transform castOrigin;
            public IHunterSkill skill;
            public GameObject everyday;             // optional everyday outfit (same rig), worn with G
            public Animator everydayAnimator;
        }

        readonly List<Hunter> hunters = new List<Hunter>();
        PlayerMotor motor;
        PlayerCombat combat;
        MeleeSlash melee;
        TalismanCaster caster;
        HunterCharacter character;
        LiminalPlayerHealth health;
        LiminalRunDirector director;
        InputAction swap, outfit;
        // selected: the player's pick. current: the body on screen (the lobby always wears the first hunter).
        int selected, current;
        bool initialized, inLobby, everyday, combatLocked, casterLocked;
        float readyAt, outfitReadyAt;

        public int Count { get { EnsureInitialized(); return hunters.Count; } }
        public int SelectedIndex => selected;
        public int CurrentIndex => current;
        public string CurrentName => current < hunters.Count ? hunters[current].name : FirstHunterName;
        public string CurrentId => current < hunters.Count ? hunters[current].id : null;
        public GameObject CurrentVisual => current < hunters.Count ? (everyday ? hunters[current].everyday : hunters[current].visual) : null;
        /// <summary>The worn hunter's own skill (an ultimate on R), or null. None in everyday clothes.</summary>
        public IHunterSkill CurrentSkill => current < hunters.Count && !everyday ? hunters[current].skill : null;
        /// <summary>The worn hunter has an everyday outfit (G).</summary>
        public bool HasEveryday => current < hunters.Count && hunters[current].everyday;
        /// <summary>In everyday clothes: moving only.</summary>
        public bool WearsEveryday => everyday;
        public InputAction OutfitAction => outfit;
        public int SwapCount { get; private set; }
        public float CooldownRemaining => Mathf.Max(0, readyAt - Time.time);
        public InputAction SwapAction => swap;
        public event Action Swapped;
        public event Action OutfitChanged;

        /// <summary>A swap needs a gate in progress and a free body: not dashing, grabbed, thrown or down.</summary>
        public bool CanSwap
        {
            get
            {
                if (!initialized || hunters.Count < 2 || inLobby || Time.time < readyAt || Time.timeScale <= 0) return false;
                if (director && director.Phase != LiminalRunPhase.Exploring) return false;
                if (!motor.isActiveAndEnabled || motor.IsDashing || motor.IsHeld || motor.IsLaunched) return false;
                var skill = CurrentSkill;
                if (skill != null && skill.Busy) return false;
                return !health || health.IsAlive;
            }
        }

        /// <summary>G needs a gate in progress, a hunter with everyday clothes and a free body (not attacking either).</summary>
        public bool CanChangeOutfit
        {
            get
            {
                if (!initialized || !HasEveryday || inLobby || Time.time < outfitReadyAt || Time.timeScale <= 0) return false;
                if (director && director.Phase != LiminalRunPhase.Exploring) return false;
                if (!motor.isActiveAndEnabled || motor.IsDashing || motor.IsHeld || motor.IsLaunched) return false;
                if (combat && combat.isActiveAndEnabled && combat.IsAttacking) return false;
                var skill = CurrentSkill;
                if (skill != null && skill.Busy) return false;
                return !health || health.IsAlive;
            }
        }

        void Awake()
        {
            swap = new InputAction("SwapHunter", InputActionType.Button);
            swap.AddBinding("<Keyboard>/f");
            swap.AddBinding("<Gamepad>/buttonNorth");
            outfit = new InputAction("ChangeOutfit", InputActionType.Button);
            outfit.AddBinding("<Keyboard>/g");
            outfit.AddBinding("<Gamepad>/dpad/up");
        }

        void OnEnable() { swap?.Enable(); outfit?.Enable(); }
        void OnDisable() { swap?.Disable(); outfit?.Disable(); }
        void OnDestroy() { swap?.Dispose(); outfit?.Dispose(); }
        void Start() => EnsureInitialized();

        public void Bind(LiminalRunDirector run)
        {
            director = run;
            EnsureInitialized();
        }

        void Update()
        {
            if (initialized && Application.isFocused && swap.WasPressedThisFrame()) TrySwap();
            if (initialized && Application.isFocused && outfit.WasPressedThisFrame()) TryChangeOutfit();
        }

        /// <summary>G: everyday clothes on / off for the worn hunter, when CanChangeOutfit allows it.</summary>
        public bool TryChangeOutfit()
        {
            EnsureInitialized();
            if (!CanChangeOutfit) return false;
            SetEveryday(!everyday, true);
            outfitReadyAt = Time.time + swapCooldown;
            return true;
        }

        /// <summary>The next hunter in the roster, when CanSwap allows it.</summary>
        public bool TrySwap()
        {
            EnsureInitialized();
            return CanSwap && Select((selected + 1) % hunters.Count);
        }

        /// <summary>Picks a hunter and puts it on at once (outside the lobby). Ignores the swap cooldown.</summary>
        public bool Select(int index)
        {
            EnsureInitialized();
            if (inLobby || index < 0 || index >= hunters.Count || index == selected) return false;
            selected = index;
            Wear(index, true);
            readyAt = Time.time + swapCooldown;
            return true;
        }

        /// <summary>Before the lobby dresses the body: back to the first hunter, whose everyday clothes the lobby shows.</summary>
        public void EnterLobby()
        {
            EnsureInitialized();
            inLobby = true;
            Wear(0, false);
        }

        /// <summary>After the lobby restores the combat outfit: the picked hunter comes back.</summary>
        public void LeaveLobby()
        {
            EnsureInitialized();
            inLobby = false;
            Wear(selected, false);
        }

        void EnsureInitialized()
        {
            if (initialized) return;
            motor = GetComponent<PlayerMotor>();
            if (!motor || !motor.visual) return;
            initialized = true;
            combat = GetComponent<PlayerCombat>();
            melee = GetComponent<MeleeSlash>();
            caster = GetComponent<TalismanCaster>();
            health = GetComponent<LiminalPlayerHealth>();
            if (!TryGetComponent(out character)) character = gameObject.AddComponent<HunterCharacter>();
            if (!director) director = FindFirstObjectByType<LiminalRunDirector>();

            // The scene's own body, exactly as the player prefab and MeleeSlash set it up.
            var first = new Hunter
            {
                id = character.characterId,
                name = FirstHunterName,
                visual = motor.visual.gameObject,
                animator = motor.animator,
                durations = combat ? combat.attackDurations : null,
                hitTimes = combat ? combat.hitTimes : null,
                movement = combat ? combat.attackMovementMultiplier : 1,
                queueWindow = combat ? combat.comboQueueWindow : 0,
                blend = combat ? combat.animationBlend : 0,
                returnBlend = combat ? combat.returnBlend : 0,
                swings = melee ? melee.swings : null,
                blade = !melee || melee.bladeEffects,
                origins = combat ? combat.attackOrigins : null,
                castOrigin = caster ? caster.castOrigin : null,
            };
            hunters.Add(first);
            var profiles = Resources.LoadAll<HunterProfile>(ProfileFolder);
            Array.Sort(profiles, (a, b) => a.order != b.order ? a.order.CompareTo(b.order) : string.CompareOrdinal(a.name, b.name));
            foreach (var profile in profiles)
            {
                var hunter = Spawn(profile, first);
                if (hunter != null) hunters.Add(hunter);
            }
            selected = current = 0;
            character.displayName = FirstHunterName;
        }

        Hunter Spawn(HunterProfile profile, Hunter first)
        {
            if (!profile.model || !profile.controller || profile.swings == null || profile.swings.Length == 0
                || profile.attackDurations == null || profile.attackDurations.Length == 0)
            {
                Debug.LogWarning("Hunter profile " + profile.name + " needs a model, a controller and at least one swing.", profile);
                return null;
            }
            var visual = Instantiate(profile.model, transform);
            // Inactive before the hunter's own components go on, so they wake up only when the hunter is worn.
            visual.SetActive(false);
            visual.name = "Hunter_" + profile.characterId;
            visual.transform.localPosition = first.visual.transform.localPosition;
            visual.transform.localRotation = Quaternion.identity;
            SetLayer(visual.transform, first.visual.layer);
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            var animator = visual.GetComponent<Animator>();
            if (!animator) animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = profile.controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
            MatchHeight(visual.transform, profile.headBone, first);
            profile.Configure(visual, animator);

            GameObject everydayVisual = null;
            Animator everydayAnimator = null;
            if (profile.everydayModel && profile.everydayController)
            {
                everydayVisual = Instantiate(profile.everydayModel, transform);
                everydayVisual.SetActive(false);
                everydayVisual.name = "Hunter_" + profile.characterId + "_Everyday";
                everydayVisual.transform.localPosition = first.visual.transform.localPosition;
                everydayVisual.transform.localRotation = Quaternion.identity;
                SetLayer(everydayVisual.transform, first.visual.layer);
                foreach (var collider in everydayVisual.GetComponentsInChildren<Collider>(true)) Destroy(collider);
                everydayAnimator = everydayVisual.GetComponent<Animator>();
                if (!everydayAnimator) everydayAnimator = everydayVisual.AddComponent<Animator>();
                everydayAnimator.runtimeAnimatorController = profile.everydayController;
                everydayAnimator.applyRootMotion = false;
                everydayAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var skin in everydayVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
                MatchHeight(everydayVisual.transform, profile.headBone, first);
                profile.ConfigureEveryday(everydayVisual, everydayAnimator);
            }

            var castOrigin = new GameObject("Hunter Cast Origin").transform;
            castOrigin.SetParent(visual.transform, false);
            castOrigin.localPosition = new Vector3(0, 1.2f, .3f) / Mathf.Max(.01f, visual.transform.localScale.y);
            var origins = profile.AttackOrigins(visual, animator);
            return new Hunter
            {
                id = profile.characterId,
                name = profile.displayName,
                visual = visual,
                animator = animator,
                durations = (float[])profile.attackDurations.Clone(),
                hitTimes = profile.hitTimes != null ? (float[])profile.hitTimes.Clone() : new float[0],
                movement = profile.attackMovementMultiplier,
                queueWindow = profile.comboQueueWindow,
                blend = profile.animationBlend,
                returnBlend = profile.returnBlend,
                swings = (MeleeSlash.Swing[])profile.swings.Clone(),
                blade = profile.blade,
                origins = origins ?? new Transform[0],
                castOrigin = castOrigin,
                skill = visual.GetComponent<IHunterSkill>(),
                everyday = everydayVisual,
                everydayAnimator = everydayAnimator,
            };
        }

        void Wear(int index, bool effects)
        {
            if (index < 0 || index >= hunters.Count) return;
            var to = hunters[index];
            bool changed = index != current;
            if (everyday && !changed) SetEveryday(false, false);
            if (changed)
            {
                var from = hunters[current];
                // Motor and combat turn the visual, not the root: the new body takes over the old facing.
                Quaternion facing = motor.visual ? motor.visual.rotation : transform.rotation;
                if (combat) combat.CancelAttack(false);
                if (effects) SwapEffects(everyday ? from.everyday : from.visual);
                if (everyday)
                {   // the leaving hunter was in her everyday clothes: those go, she is back in her battle body next time
                    from.everyday.SetActive(false);
                    everyday = false;
                    ApplyActionLock();
                }
                from.visual.SetActive(false);
                to.visual.transform.rotation = facing;
                to.visual.SetActive(true);
                motor.visual = to.visual.transform;
                motor.animator = to.animator;
                current = index;
                SwapCount++;
            }
            ApplyCombo(to);
            character.characterId = to.id;
            character.displayName = to.name;
            if (changed) Swapped?.Invoke();
        }

        /// <summary>Puts the worn hunter into (or out of) her everyday clothes: same place and facing, moving only.</summary>
        void SetEveryday(bool on, bool effects)
        {
            var hunter = current < hunters.Count ? hunters[current] : null;
            if (hunter == null || !hunter.everyday || on == everyday) return;
            var from = on ? hunter.visual : hunter.everyday;
            var to = on ? hunter.everyday : hunter.visual;
            Quaternion facing = motor.visual ? motor.visual.rotation : transform.rotation;
            if (combat) combat.CancelAttack(false);
            if (effects) OutfitEffects(from);
            float speed = motor.animator && HasFloat(motor.animator, SpeedParameter) ? motor.animator.GetFloat(SpeedParameter) : 0;
            from.SetActive(false);
            to.transform.rotation = facing;
            to.SetActive(true);
            motor.visual = to.transform;
            motor.animator = on ? hunter.everydayAnimator : hunter.animator;
            if (motor.animator && HasFloat(motor.animator, SpeedParameter)) motor.animator.SetFloat(SpeedParameter, speed);
            everyday = on;
            ApplyActionLock();
            if (melee) melee.ResetBodyCache();
            OutfitChanged?.Invoke();
            Swapped?.Invoke();
        }

        /// <summary>Everyday clothes: no attacks (melee combo and talismans) and no dash; moving is untouched.</summary>
        void ApplyActionLock()
        {
            if (combat)
            {
                if (everyday && combat.enabled) { combat.enabled = false; combatLocked = true; }
                else if (!everyday && combatLocked) { combat.enabled = true; combatLocked = false; }
            }
            if (caster)
            {
                if (everyday && caster.enabled) { caster.enabled = false; casterLocked = true; }
                else if (!everyday && casterLocked) { caster.enabled = true; casterLocked = false; }
            }
            motor.DashLocked = everyday;
        }

        static readonly int SpeedParameter = Animator.StringToHash("Speed");

        static bool HasFloat(Animator animator, int id)
        {
            if (!animator.runtimeAnimatorController || !animator.isActiveAndEnabled) return false;
            foreach (var parameter in animator.parameters)
                if (parameter.nameHash == id && parameter.type == AnimatorControllerParameterType.Float) return true;
            return false;
        }

        /// <summary>A quick change of clothes: a soft puff of dust and a pale afterimage of the old outfit.</summary>
        void OutfitEffects(GameObject from)
        {
            HitFeedback.Afterimage(from.GetComponentsInChildren<SkinnedMeshRenderer>(), new Color(1, .95f, .9f, .5f), .3f);
            HitFeedback.Dust(transform.position + Vector3.up * .05f, .8f);
        }

        void ApplyCombo(Hunter hunter)
        {
            if (combat && hunter.durations != null)
            {
                combat.attackDurations = hunter.durations;
                combat.hitTimes = hunter.hitTimes;
                combat.attackMovementMultiplier = hunter.movement;
                combat.comboQueueWindow = hunter.queueWindow;
                combat.animationBlend = hunter.blend;
                combat.returnBlend = hunter.returnBlend;
                combat.attackOrigins = hunter.origins;
            }
            if (caster && hunter.castOrigin) caster.castOrigin = hunter.castOrigin;
            if (melee)
            {
                if (hunter.swings != null && hunter.swings.Length > 0) melee.swings = hunter.swings;
                melee.bladeEffects = hunter.blade;
                melee.SetWeaponVisible(hunter.blade);
                melee.ResetBodyCache();
            }
        }

        /// <summary>The leaving hunter fades out as a violet afterimage while the new one steps in.</summary>
        void SwapEffects(GameObject leaving)
        {
            HitFeedback.Afterimage(leaving.GetComponentsInChildren<SkinnedMeshRenderer>(), HitFeedback.Violet * .75f, .4f);
            Vector3 feet = transform.position + Vector3.up * .05f;
            HitFeedback.Dust(feet, 1.1f);
            HitFeedback.Sparks(transform.position + Vector3.up, Vector3.up, 12, .9f);
            HitFeedback.ScreenFlash(new Color(HitFeedback.Violet.r, HitFeedback.Violet.g, HitFeedback.Violet.b, .18f), .22f);
            HitFeedback.Play(HitFeedback.Sfx.JustDodge, .5f, 1.3f);
        }

        static void MatchHeight(Transform visual, string boneName, Hunter first)
        {
            if (string.IsNullOrEmpty(boneName)) return;
            Transform head = FindDeep(visual, boneName);
            Transform goal = first.animator && first.animator.isHuman
                ? first.animator.GetBoneTransform(HumanBodyBones.Head) : FindDeep(first.visual.transform, boneName);
            if (!head || !goal) return;
            float own = head.position.y - visual.position.y;
            float target = goal.position.y - first.visual.transform.position.y;
            if (own < .1f || target < .1f) return;
            visual.localScale *= Mathf.Clamp(target / own, .6f, 1.5f);
        }

        static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                var found = FindDeep(child, name);
                if (found) return found;
            }
            return null;
        }

        static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root) SetLayer(child, layer);
        }
    }
}
