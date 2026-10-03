using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace AcRoguelike
{
    /// <summary>Owns attack input, animation timing and buffered combos; PlayerMotor owns displacement.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMotor), typeof(TalismanCaster))]
    public sealed class PlayerCombat : MonoBehaviour
    {
        [Header("Three-hit attack")]
        public float[] attackDurations = { .52f, .56f, .70f };
        public float[] hitTimes = { .18f, .20f, .27f };
        [Tooltip("Optional cast origins for Attack1/2/3. An empty entry keeps the caster's current origin.")]
        public Transform[] attackOrigins = new Transform[3];
        [Range(0, 1)] public float attackMovementMultiplier = .18f;
        public float inputBuffer = .22f;
        public float comboQueueWindow = .20f;
        public float comboRecovery = .14f;
        public bool holdToAttack = true;
        [Tooltip("The original caster cooldown. Cooldown upgrades speed up both the attack motion and its hit timing.")]
        public float referenceCooldown = .7f;
        public float animationBlend = .07f;
        [Tooltip("Cross-fade from the last attack pose back to locomotion.")]
        public float returnBlend = .1f;

        public bool IsAttacking => AttackIndex >= 0;
        /// <summary>Zero-based attack index, or -1 when idle. Maps to Animator states Attack1 to Attack6.</summary>
        public int AttackIndex { get; private set; } = -1;
        public Vector3 AttackDirection { get; private set; }
        public float AnimationSpeed => 1f / attackTimeScale;
        public float MovementMultiplier => IsAttacking ? attackMovementMultiplier : 1;
        public int AttackCount { get; private set; }
        public int CompletedComboCount { get; private set; }
        public int SuccessfulCastCount { get; private set; }
        public InputAction AttackAction => attack;
        /// <summary>Hits in one combo: one per configured attack duration, at most the four Animator attack states.</summary>
        public int ComboLength => Mathf.Clamp(attackDurations != null ? attackDurations.Length : 3, 1, AttackStates.Length);

        PlayerMotor motor;
        TalismanCaster caster;
        MeleeSlash melee;
        TrainingEnemy attackTarget;
        InputAction attack;
        Animator cachedAnimator;
        RuntimeAnimatorController cachedController;
        readonly Dictionary<int, AnimatorControllerParameterType> parameters = new Dictionary<int, AnimatorControllerParameterType>();
        float attackClock, attackTimeScale = 1, bufferRemaining, recoveryRemaining;
        bool hitResolved, nextQueued, automated, automatedPressed, automatedHeld, previousExternalInput;

        static readonly int Attacking = Animator.StringToHash("Attacking");
        static readonly int AttackIndexParameter = Animator.StringToHash("AttackIndex");
        static readonly int AttackSpeed = Animator.StringToHash("AttackSpeed");
        static readonly int AttackTrigger = Animator.StringToHash("Attack");
        static readonly int Locomotion = Animator.StringToHash("Base Layer.Locomotion");
        static readonly int[] AttackStates =
        {
            Animator.StringToHash("Base Layer.Attack1"),
            Animator.StringToHash("Base Layer.Attack2"),
            Animator.StringToHash("Base Layer.Attack3"),
            Animator.StringToHash("Base Layer.Attack4"),
            Animator.StringToHash("Base Layer.Attack5"),
            Animator.StringToHash("Base Layer.Attack6")
        };

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            caster = GetComponent<TalismanCaster>();
            // The basic attack is a katana combo; the caster still provides target selection and cast counting.
            melee = GetComponent<MeleeSlash>();
            if (!melee) melee = gameObject.AddComponent<MeleeSlash>();
            previousExternalInput = caster.externalInput;
            attack = new InputAction("Attack", InputActionType.Button);
            attack.AddBinding("<Mouse>/leftButton");
            attack.AddBinding("<Keyboard>/j");
            attack.AddBinding("<Gamepad>/rightShoulder");
            attack.AddBinding("<Gamepad>/rightTrigger");
            AttackDirection = transform.forward;
        }

        void OnEnable()
        {
            attack?.Enable();
            if (caster) caster.externalInput = true;
        }

        void OnDisable()
        {
            attack?.Disable();
            CancelAttack();
            if (caster) caster.externalInput = previousExternalInput;
        }

        void OnDestroy() => attack?.Dispose();

        void Update()
        {
            CacheAnimator();
            float dt = Time.deltaTime;
            if (!motor || !motor.isActiveAndEnabled || !caster || !caster.isActiveAndEnabled || dt <= 0)
            {
                automatedPressed = false;
                return;
            }
            if (!Application.isFocused && !automated)
            {
                bufferRemaining = 0;
                CancelAttack();
                return;
            }

            bool pressed = automated ? automatedPressed : attack.WasPressedThisFrame();
            bool held = automated ? automatedHeld : attack.IsPressed();
            automatedPressed = false;
            // UI buttons must not also issue an attack into the scene underneath them.
            if (!automated && EventSystem.current && EventSystem.current.IsPointerOverGameObject()
                && attack.activeControl != null && attack.activeControl.device is Mouse)
            { pressed = held = false; }

            recoveryRemaining = Mathf.Max(0, recoveryRemaining - dt);
            bufferRemaining = Mathf.Max(0, bufferRemaining - dt);
            if (pressed) bufferRemaining = Mathf.Max(.01f, inputBuffer);
            if (motor.IsDashing || motor.IsHeld || motor.IsLaunched)
            {
                if (IsAttacking) CancelAttack(false);
                // A fresh press during the end of a dodge may carry into the next action.
                return;
            }

            if (!IsAttacking)
            {
                if (recoveryRemaining <= 0 && (bufferRemaining > 0 || (holdToAttack && held))) BeginAttack(0);
                return;
            }

            attackClock += dt / attackTimeScale;
            float duration = Duration(AttackIndex);
            if (!hitResolved && attackClock >= HitTime(AttackIndex))
            {
                hitResolved = true;
                if (melee && melee.isActiveAndEnabled)
                {
                    if (melee.ResolveSwing(AttackIndex, attackTarget, AttackDirection)) SuccessfulCastCount++;
                }
                else if (caster.TryCastFromAnimation(attackTarget)) SuccessfulCastCount++;
            }
            if (AttackIndex < ComboLength - 1 && attackClock >= duration - Mathf.Max(0, comboQueueWindow))
            {
                if (bufferRemaining > 0 || (holdToAttack && held))
                { nextQueued = true; bufferRemaining = 0; }
            }
            if (attackClock < duration) return;
            if (nextQueued && AttackIndex < ComboLength - 1)
            {
                BeginAttack(AttackIndex + 1);
                return;
            }

            bool finishedCombo = AttackIndex == ComboLength - 1;
            if (finishedCombo) CompletedComboCount++;
            EndAttack();
            recoveryRemaining = finishedCombo ? Mathf.Max(0, comboRecovery) * attackTimeScale : 0;
        }

        void BeginAttack(int index)
        {
            AttackIndex = index;
            AttackCount++;
            attackClock = 0;
            hitResolved = nextQueued = false;
            bufferRemaining = 0;
            attackTimeScale = Mathf.Clamp(caster.cooldown / Mathf.Max(.01f, referenceCooldown), .3f, 2f);
            if (attackOrigins != null && index < attackOrigins.Length && attackOrigins[index])
                caster.castOrigin = attackOrigins[index];
            Vector3 direction = Vector3.ProjectOnPlane(motor.AimPoint - transform.position, Vector3.up);
            AttackDirection = direction.sqrMagnitude > .1f ? direction.normalized
                : motor.visual ? motor.visual.forward : transform.forward;
            attackTarget = caster.FindAnimationTarget(AttackDirection);
            if (attackTarget)
            {
                Vector3 targetDirection = Vector3.ProjectOnPlane(attackTarget.transform.position - transform.position, Vector3.up);
                if (targetDirection.sqrMagnitude > .01f) AttackDirection = targetDirection.normalized;
            }
            if (melee && melee.isActiveAndEnabled) melee.BeginSwing(index, attackTarget, AttackDirection);
            if (Has(Attacking, AnimatorControllerParameterType.Bool)) cachedAnimator.SetBool(Attacking, true);
            if (Has(AttackIndexParameter, AnimatorControllerParameterType.Int)) cachedAnimator.SetInteger(AttackIndexParameter, index);
            if (Has(AttackSpeed, AnimatorControllerParameterType.Float)) cachedAnimator.SetFloat(AttackSpeed, 1f / attackTimeScale);
            if (CanAnimate && cachedAnimator.HasState(0, AttackStates[index]))
            {
                // Direct state changes also support controllers without Any State trigger transitions.
                if (Has(AttackTrigger, AnimatorControllerParameterType.Trigger)) cachedAnimator.ResetTrigger(AttackTrigger);
                cachedAnimator.CrossFadeInFixedTime(AttackStates[index], Mathf.Max(0, animationBlend), 0, 0);
            }
            else if (Has(AttackTrigger, AnimatorControllerParameterType.Trigger)) cachedAnimator.SetTrigger(AttackTrigger);
        }

        void EndAttack(bool returnToLocomotion = true)
        {
            if (melee && AttackIndex >= 0) melee.EndSwing();
            AttackIndex = -1;
            attackTarget = null;
            attackClock = 0;
            hitResolved = nextQueued = false;
            if (Has(Attacking, AnimatorControllerParameterType.Bool)) cachedAnimator.SetBool(Attacking, false);
            if (Has(AttackIndexParameter, AnimatorControllerParameterType.Int)) cachedAnimator.SetInteger(AttackIndexParameter, -1);
            if (Has(AttackTrigger, AnimatorControllerParameterType.Trigger)) cachedAnimator.ResetTrigger(AttackTrigger);
            if (returnToLocomotion && CanAnimate && cachedAnimator.HasState(0, Locomotion))
                cachedAnimator.CrossFadeInFixedTime(Locomotion, Mathf.Max(0, returnBlend), 0);
        }

        public void CancelAttack(bool returnToLocomotion = true)
        {
            CacheAnimator();
            bool wasAttacking = IsAttacking;
            bufferRemaining = recoveryRemaining = 0;
            automatedPressed = false;
            EndAttack(returnToLocomotion && wasAttacking);
        }

        float Duration(int index) => attackDurations != null && index < attackDurations.Length
            ? Mathf.Max(.15f, attackDurations[index]) : .6f;
        float HitTime(int index) => hitTimes != null && index < hitTimes.Length
            ? Mathf.Clamp(hitTimes[index], .01f, Duration(index) - .01f) : Duration(index) * .35f;

        void CacheAnimator()
        {
            var current = motor ? motor.animator : null;
            var controller = current ? current.runtimeAnimatorController : null;
            if (cachedAnimator == current && cachedController == controller) return;
            cachedAnimator = current;
            cachedController = controller;
            parameters.Clear();
            if (!current || !controller) return;
            foreach (var parameter in current.parameters) parameters[parameter.nameHash] = parameter.type;
        }

        bool Has(int hash, AnimatorControllerParameterType type) => cachedAnimator && cachedController
            && parameters.TryGetValue(hash, out var found) && found == type;
        bool CanAnimate => cachedAnimator && cachedController && cachedAnimator.isActiveAndEnabled && cachedAnimator.layerCount > 0;

        public void SetAutomationInput(bool attackPressed, bool attackHeld = false)
        { automated = true; automatedPressed = attackPressed; automatedHeld = attackHeld; }

        public void ReleaseAutomation()
        { automated = false; automatedPressed = automatedHeld = false; }
    }
}
