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
    /// Resources/Hunters add the others. The pick is the party's hunter (HunterLoadout): the lobby's loadout officer
    /// changes it with Choose, and the lobby shows the picked hunter (the first one in her everyday clothes).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class HunterRoster : MonoBehaviour
    {
        public const string FirstHunterName = "아스트라이아";
        public const string ProfileFolder = "Hunters";
        public const string FirstHunterSummary = "검과 부적을 쓰는 기본 헌터\n6타 검술 콤보 · 좌클릭 유지 부적 시전";
        public const string FirstHunterPortrait = "Loadout/hunter_astraia";
        [Tooltip("Seconds between two swaps.")]
        public float swapCooldown = .6f;

        sealed class Hunter
        {
            public string id, name, summary, portrait;
            public GameObject visual;
            public Animator animator;
            public float[] durations, hitTimes;
            public float movement, queueWindow, blend, returnBlend;
            public MeleeSlash.Swing[] swings;
            public bool blade;
            public bool movementOnly, keepDash;
            public float moveSpeedOverride, walkSpeedOverride;
            public Transform[] origins;
            public Transform castOrigin;
            public IHunterSkill skill;
            public IHunterSkill[] skills;
        }

        readonly List<Hunter> hunters = new List<Hunter>();
        PlayerMotor motor;
        PlayerCombat combat;
        MeleeSlash melee;
        TalismanCaster caster;
        HunterCharacter character;
        LiminalPlayerHealth health;
        LiminalRunDirector director;
        InputAction swap;
        // selected: the player's pick. current: the body on screen (the lobby always wears the first hunter).
        int selected, current;
        bool initialized, inLobby;
        bool defaultAllowDash, defaultFaceMovementDirection, defaultMeleeEnabled;
        bool moveSpeedOverridden, walkSpeedOverridden;
        float sharedMoveSpeed, sharedWalkSpeed;
        float readyAt;

        public int Count { get { EnsureInitialized(); return hunters.Count; } }
        public int SelectedIndex => selected;
        public int CurrentIndex => current;
        public string CurrentName => current < hunters.Count ? hunters[current].name : FirstHunterName;
        public string CurrentId => current < hunters.Count ? hunters[current].id : null;
        public GameObject CurrentVisual => current < hunters.Count ? hunters[current].visual : null;
        public bool CurrentMovementOnly => current >= 0 && current < hunters.Count && hunters[current].movementOnly;
        public float SharedMoveSpeed => moveSpeedOverridden ? sharedMoveSpeed : motor ? motor.moveSpeed : 0;
        public float SharedWalkSpeed => walkSpeedOverridden ? sharedWalkSpeed : motor ? motor.walkSpeed : 0;
        /// <summary>The worn hunter's own skill (an ultimate on R), or null.</summary>
        public IHunterSkill CurrentSkill => current < hunters.Count ? hunters[current].skill : null;
        public int SwapCount { get; private set; }
        public bool InLobby => inLobby;
        public string NameAt(int index) => index >= 0 && index < Count ? hunters[index].name : null;
        public string IdAt(int index) => index >= 0 && index < Count ? hunters[index].id : null;
        public string SummaryAt(int index) => index >= 0 && index < Count ? hunters[index].summary : null;
        /// <summary>The hunter's loadout illustration, or null when it has none yet.</summary>
        public Texture2D PortraitAt(int index)
            => index >= 0 && index < Count && !string.IsNullOrEmpty(hunters[index].portrait) ? Resources.Load<Texture2D>(hunters[index].portrait) : null;
        public float CooldownRemaining => Mathf.Max(0, readyAt - Time.time);
        public InputAction SwapAction => swap;
        public event Action Swapped;

        /// <summary>A swap needs a gate in progress and a free body: not dashing, grabbed, thrown or down.</summary>
        public bool CanSwap
        {
            get
            {
                if (!initialized || hunters.Count < 2 || inLobby || Time.time < readyAt || Time.timeScale <= 0) return false;
                if (director && director.Phase != LiminalRunPhase.Exploring) return false;
                if (!motor.isActiveAndEnabled || motor.IsDashing || motor.IsHeld || motor.IsLaunched) return false;
                if (current < hunters.Count && hunters[current].skills != null)
                    foreach (var skill in hunters[current].skills) if (skill != null && skill.Busy) return false;
                return !health || health.IsAlive;
            }
        }

        void Awake()
        {
            swap = new InputAction("SwapHunter", InputActionType.Button);
            swap.AddBinding("<Keyboard>/f");
            swap.AddBinding("<Gamepad>/buttonNorth");
        }

        void OnEnable() => swap?.Enable();
        void OnDisable() => swap?.Disable();
        void OnDestroy() => swap?.Dispose();
        void Start() => EnsureInitialized();

        public void Bind(LiminalRunDirector run)
        {
            director = run;
            EnsureInitialized();
        }

        void Update()
        {
            if (initialized && Application.isFocused && swap.WasPressedThisFrame()) TrySwap();
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
            HunterLoadout.HunterId = hunters[index].id;
            Wear(index, true);
            readyAt = Time.time + swapCooldown;
            return true;
        }

        /// <summary>
        /// The loadout officer's pick: changes the party's hunter and puts it on at once, in the lobby too (the lobby
        /// changes the clothes around this call). Saved for the next session.
        /// </summary>
        public bool Choose(int index)
        {
            EnsureInitialized();
            if (index < 0 || index >= hunters.Count || index == selected) return false;
            if (!inLobby) return Select(index);
            selected = index;
            HunterLoadout.HunterId = hunters[index].id;
            Wear(index, true);
            return true;
        }

        /// <summary>Before the lobby dresses the body: the picked hunter (the lobby puts the first one in everyday clothes).</summary>
        public void EnterLobby()
        {
            EnsureInitialized();
            inLobby = true;
            Wear(selected, false);
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
            defaultAllowDash = motor.allowDash;
            defaultFaceMovementDirection = motor.faceMovementDirection;
            defaultMeleeEnabled = melee && melee.enabled;
            caster = GetComponent<TalismanCaster>();
            health = GetComponent<LiminalPlayerHealth>();
            if (!TryGetComponent(out character)) character = gameObject.AddComponent<HunterCharacter>();
            if (!director) director = FindFirstObjectByType<LiminalRunDirector>();

            // The scene's own body, exactly as the player prefab and MeleeSlash set it up.
            var first = new Hunter
            {
                id = character.characterId,
                name = FirstHunterName,
                summary = FirstHunterSummary,
                portrait = FirstHunterPortrait,
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
            first.skills = first.visual.GetComponents<IHunterSkill>();
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
            // The party saved last session (HunterLoadout); an unknown id keeps the first hunter.
            string saved = HunterLoadout.HunterId;
            for (int i = 1; i < hunters.Count; i++)
                if (hunters[i].id == saved) { selected = i; Wear(i, false); break; }
        }

        Hunter Spawn(HunterProfile profile, Hunter first)
        {
            if (!profile.model || (!profile.movementOnly && (!profile.controller || profile.swings == null || profile.swings.Length == 0
                || profile.attackDurations == null || profile.attackDurations.Length == 0)))
            {
                Debug.LogWarning("Hunter profile " + profile.name + " needs a model; combat hunters also need a controller and at least one swing.", profile);
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
            if (!animator && (!profile.movementOnly || profile.controller)) animator = visual.AddComponent<Animator>();
            if (animator)
            {
                if (profile.controller || !profile.movementOnly) animator.runtimeAnimatorController = profile.controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
            MatchHeight(visual.transform, profile.headBone, first);
            profile.Configure(visual, animator);
            if (profile.movementOnly)
                foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour is IHunterSkill) behaviour.enabled = false;

            var castOrigin = new GameObject("Hunter Cast Origin").transform;
            castOrigin.SetParent(visual.transform, false);
            castOrigin.localPosition = new Vector3(0, 1.2f, .3f) / Mathf.Max(.01f, visual.transform.localScale.y);
            var origins = profile.movementOnly ? null : profile.AttackOrigins(visual, animator);
            return new Hunter
            {
                id = profile.characterId,
                name = profile.displayName,
                summary = profile.summary,
                portrait = profile.portraitResource,
                visual = visual,
                animator = animator,
                durations = profile.attackDurations != null ? (float[])profile.attackDurations.Clone() : Array.Empty<float>(),
                hitTimes = profile.hitTimes != null ? (float[])profile.hitTimes.Clone() : new float[0],
                movement = profile.attackMovementMultiplier,
                queueWindow = profile.comboQueueWindow,
                blend = profile.animationBlend,
                returnBlend = profile.returnBlend,
                swings = profile.swings != null ? (MeleeSlash.Swing[])profile.swings.Clone() : Array.Empty<MeleeSlash.Swing>(),
                blade = profile.blade,
                movementOnly = profile.movementOnly,
                keepDash = profile.keepDash,
                moveSpeedOverride = profile.moveSpeedOverride,
                walkSpeedOverride = profile.walkSpeedOverride,
                origins = origins ?? new Transform[0],
                castOrigin = castOrigin,
                skill = profile.movementOnly ? null : visual.GetComponent<IHunterSkill>(),
                skills = profile.movementOnly ? Array.Empty<IHunterSkill>() : visual.GetComponents<IHunterSkill>(),
            };
        }

        void Wear(int index, bool effects)
        {
            if (index < 0 || index >= hunters.Count) return;
            var to = hunters[index];
            bool changed = index != current;
            if (changed)
            {
                var from = hunters[current];
                // Motor and combat turn the visual, not the root: the new body takes over the old facing.
                Quaternion facing = motor.visual ? motor.visual.rotation : transform.rotation;
                if (combat) combat.CancelAttack(false);
                if (effects) SwapEffects(from);
                from.visual.SetActive(false);
                to.visual.transform.rotation = facing;
                to.visual.SetActive(true);
                motor.visual = to.visual.transform;
                motor.animator = to.animator;
                current = index;
                SwapCount++;
            }
            ApplyCombo(to);
            RefreshGameplayAvailability();
            character.characterId = to.id;
            character.displayName = to.name;
            if (changed) Swapped?.Invoke();
        }

        void ApplyCombo(Hunter hunter)
        {
            // Keep the previous combat configuration intact while this preview body is worn.
            if (hunter.movementOnly) return;
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

        /// <summary>Reapply the worn hunter's input restrictions after a swap or a run phase change.</summary>
        public void RefreshGameplayAvailability()
        {
            if (!initialized || current < 0 || current >= hunters.Count) return;
            var hunter = hunters[current];
            bool combatAllowed = !hunter.movementOnly && !inLobby
                && (!director || director.Phase == LiminalRunPhase.Exploring);
            if (combat)
            {
                if (!combatAllowed) combat.CancelAttack();
                combat.enabled = combatAllowed;
            }
            // PlayerCombat.OnDisable restores caster input ownership, so disable the caster afterwards.
            if (caster) caster.enabled = combatAllowed;
            if (melee)
            {
                melee.enabled = defaultMeleeEnabled && !hunter.movementOnly;
                melee.SetWeaponVisible(!hunter.movementOnly && !inLobby && hunter.blade);
                melee.ResetBodyCache();
            }
            motor.allowDash = defaultAllowDash && (!hunter.movementOnly || hunter.keepDash);
            motor.faceMovementDirection = hunter.movementOnly || defaultFaceMovementDirection;
            ApplyMovementSpeeds(hunter);
        }

        void ApplyMovementSpeeds(Hunter hunter)
        {
            if (hunter.moveSpeedOverride > 0)
            {
                if (!moveSpeedOverridden) sharedMoveSpeed = motor.moveSpeed;
                motor.moveSpeed = hunter.moveSpeedOverride;
                moveSpeedOverridden = true;
            }
            else if (moveSpeedOverridden)
            {
                motor.moveSpeed = sharedMoveSpeed;
                moveSpeedOverridden = false;
            }
            if (hunter.walkSpeedOverride > 0)
            {
                if (!walkSpeedOverridden) sharedWalkSpeed = motor.walkSpeed;
                motor.walkSpeed = hunter.walkSpeedOverride;
                walkSpeedOverridden = true;
            }
            else if (walkSpeedOverridden)
            {
                motor.walkSpeed = sharedWalkSpeed;
                walkSpeedOverridden = false;
            }
        }

        /// <summary>A fresh run resets the shared speeds before the selected hunter's overrides are reapplied.</summary>
        public void ResetMovementSpeeds(float moveSpeed, float walkSpeed)
        {
            if (!motor) return;
            motor.moveSpeed = sharedMoveSpeed = moveSpeed;
            motor.walkSpeed = sharedWalkSpeed = walkSpeed;
            moveSpeedOverridden = walkSpeedOverridden = false;
            if (initialized && current >= 0 && current < hunters.Count) ApplyMovementSpeeds(hunters[current]);
        }

        /// <summary>The leaving hunter fades out as a violet afterimage while the new one steps in.</summary>
        void SwapEffects(Hunter from)
        {
            HitFeedback.Afterimage(from.visual.GetComponentsInChildren<SkinnedMeshRenderer>(), HitFeedback.Violet * .75f, .4f);
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
