using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace AcRoguelike
{
    [DefaultExecutionOrder(-20)]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        public Transform visual;
        public Animator animator;
        public Camera viewCamera;
        public Transform aimMarker;
        public float moveSpeed = 4.2f;
        public float walkSpeed = 2.1f;
        public float dashSpeed = 13f;
        public float dashDuration = .19f;
        [Tooltip("Seconds to recover one stamina pip (one dash).")]
        public float dashCooldown = .85f;
        [Tooltip("Stamina pips: each dash spends one, so this many dashes can be chained.")]
        [Min(1)] public int maxStamina = 3;
        [Tooltip("Stamina only starts recovering this long after the latest dash.")]
        public float staminaRegenDelay = .3f;
        [Tooltip("Shortest time between two dash starts, so chained dashes stay readable.")]
        public float dashInterval = .3f;
        [Tooltip("Dash speed over the dash: time 0..1 is dash progress, value is a speed multiplier. The distance stays dashSpeed x dashDuration; the curve only decides how it is spread. Default: an instant burst that eases out into run speed (tuned in Tools/CombatLab).")]
        public AnimationCurve dashSpeedCurve = DefaultDashSpeedCurve();
        public float turnSpeed = 540f;
        public float attackTurnSpeed = 1440f;
        public float aimDeadRadius = .65f;
        [Range(15f, 85f)] public float maxTorsoYaw = 62f;
        public float torsoTurnSpeed = 400f;
        public float acceleration = 32f;
        public float deceleration = 38f;
        [Tooltip("Face the movement direction while exploring, then face the aim point during attacks.")]
        public bool faceMovementDirection;
        public bool IsDashing => dashRemaining > 0 || dashMovedThisFrame;
        /// <summary>Burst at the start, most of the distance by 60%, and an end speed that matches the run (4.2 m/s at 12.5 m/s).</summary>
        public static AnimationCurve DefaultDashSpeedCurve() => new AnimationCurve(
            new Keyframe(0f, 1.4f, -.0894f, -.0894f),
            new Keyframe(.3f, 1.35f, -.4919f, -.4919f),
            new Keyframe(.75f, .6f, -1.3733f, -1.3733f),
            new Keyframe(1f, .33f, -1.08f, -1.08f));
        /// <summary>Speed the dash hands over at its end (m/s): what the curve's last key works out to.</summary>
        public float DashExitSpeed
        {
            get
            {
                float area = DashCurveArea();
                return area > 1e-5f ? dashSpeed * DashCurveValue(1f) / area : dashSpeed;
            }
        }
        /// <summary>Time until a dash can start: the dash interval, or the wait for the next stamina pip.</summary>
        public float CooldownRemaining => Mathf.Max(cooldownRemaining,
            StaminaFree || Stamina >= 1 ? 0 : (1 - Stamina) * dashCooldown + Mathf.Max(0, LastDashStart + staminaRegenDelay - Time.time));
        /// <summary>Current stamina in pips (0..maxStamina, fractional while recovering).</summary>
        public float Stamina { get; private set; } = 3;
        /// <summary>Out of combat dashes cost nothing (set by the run director) and the gauge stays full.</summary>
        public bool StaminaFree { get; set; }
        public Vector3 AimPoint { get; private set; }
        public Vector3 PlanarVelocity { get; private set; }
        public int DashCount { get; private set; }
        /// <summary>Time.time when the latest dash started (for just-dodge timing).</summary>
        public float LastDashStart { get; private set; } = -10;
        /// <summary>A successful just dodge refunds the dash: one stamina pip back, ready at once.</summary>
        public void ResetDashCooldown() { cooldownRemaining = 0; Stamina = Mathf.Min(maxStamina, Stamina + 1); }
        public InputAction MoveAction => move;
        public InputAction DashAction => dash;
        public InputAction WalkAction => walk;
        /// <summary>Held by an enemy (a locker tentacle): no input, no movement; the holder moves the transform.</summary>
        public bool IsHeld { get; private set; }
        /// <summary>Thrown through the air after a grab: no control until the player lands.</summary>
        public bool IsLaunched => launched;
        public event System.Action Landed;
        CharacterController body;
        CharacterObstacleSlide obstacleSlide;
        InputAction move, dash, walk, aimStick;
        PlayerCombat combat;
        Vector3 velocity, dashDirection;
        float verticalSpeed, dashRemaining, cooldownRemaining;
        const int DashCurveSamples = 64;
        // Cumulative area under dashSpeedCurve, so a frame's share of the dash distance is exact at any frame rate.
        float[] dashCurveArea;
        bool automated;
        Vector2 automatedMove;
        Vector3 automatedAim;
        bool automatedDash;
        bool automatedWalk;
        bool gamepadAiming, dashMovedThisFrame;
        Vector3 launchVelocity;
        float launchTime;
        bool launched;
        // Grounded as measured by this motor's own gravity move. CharacterController.isGrounded only reflects the
        // latest Move call, and the melee lunge and root motion move the body horizontally after it, which would
        // read as airborne and silently swallow a dash pressed mid-attack.
        bool grounded = true;
        Transform chest, neck;
        Animator cachedAnimator;
        RuntimeAnimatorController cachedController;
        readonly Dictionary<int, AnimatorControllerParameterType> animatorParameters = new Dictionary<int, AnimatorControllerParameterType>();
        float torsoYaw;
        static readonly int MoveX = Animator.StringToHash("MoveX");
        static readonly int MoveY = Animator.StringToHash("MoveY");
        static readonly int Dashing = Animator.StringToHash("Dashing");
        static readonly int Speed = Animator.StringToHash("Speed");
        static readonly int Grounded = Animator.StringToHash("Grounded");
        static readonly int Dash = Animator.StringToHash("Dash");
        static readonly int DashState = Animator.StringToHash("Base Layer.Dash");

        void Awake()
        {
            body = GetComponent<CharacterController>();
            combat = GetComponent<PlayerCombat>();
            move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");
            dash = new InputAction("Dash", InputActionType.Button);
            dash.AddBinding("<Keyboard>/space"); dash.AddBinding("<Keyboard>/leftShift");
            dash.AddBinding("<Gamepad>/buttonEast"); dash.AddBinding("<Gamepad>/leftShoulder");
            walk = new InputAction("Walk", InputActionType.Button);
            walk.AddBinding("<Keyboard>/leftCtrl");
            walk.AddBinding("<Gamepad>/leftTrigger");
            aimStick = new InputAction("Aim", InputActionType.Value, "<Gamepad>/rightStick");
            AimPoint = transform.position + Vector3.forward * 3;
            Stamina = maxStamina;
            CacheAnimator();
        }
        void CacheAnimator()
        {
            cachedAnimator = animator;
            cachedController = animator ? animator.runtimeAnimatorController : null;
            animatorParameters.Clear();
            chest = neck = null;
            if (!animator) return;
            animator.applyRootMotion = false;
            if (cachedController)
                foreach (var parameter in animator.parameters) animatorParameters[parameter.nameHash] = parameter.type;
            if (animator.avatar && animator.avatar.isValid && animator.avatar.isHuman)
            {
                chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                if (!chest) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                return;
            }
            foreach (var bone in animator.GetComponentsInChildren<Transform>())
            {
                if (bone.name == "Chest") chest = bone;
                else if (bone.name == "Neck") neck = bone;
            }
        }
        void OnEnable() { move?.Enable(); dash?.Enable(); walk?.Enable(); aimStick?.Enable(); }
        void OnDisable()
        {
            move?.Disable(); dash?.Disable(); walk?.Disable(); aimStick?.Disable();
            velocity = PlanarVelocity = Vector3.zero;
            dashRemaining = 0; dashMovedThisFrame = false;
            if (combat) combat.CancelAttack();
            ResetDashAnimation();
        }
        void OnDestroy() { move?.Dispose(); dash?.Dispose(); walk?.Dispose(); aimStick?.Dispose(); }

        public static Vector3 CameraRelative(Vector2 input, Transform cameraTransform)
        {
            if (!cameraTransform) return new Vector3(input.x, 0, input.y).normalized * Mathf.Min(1, input.magnitude);
            Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            input = Vector2.ClampMagnitude(input, 1);
            return right * input.x + forward * input.y;
        }

        public bool ScreenToGround(Vector2 screen, out Vector3 point)
        {
            point = AimPoint;
            if (!viewCamera) return false;
            Ray ray = viewCamera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0, transform.position.y + .015f, 0));
            if (!plane.Raycast(ray, out float distance) || distance < 0) return false;
            point = ray.GetPoint(distance); return true;
        }

        void Update()
        {
            dashMovedThisFrame = false;
            if (!viewCamera) viewCamera = Camera.main;
            if (!visual || !body.enabled) return;
            if (cachedAnimator != animator || (animator && cachedController != animator.runtimeAnimatorController)) CacheAnimator();
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            Vector2 input = automated ? automatedMove : move.ReadValue<Vector2>();
            bool wantsDash = automated ? automatedDash : dash.WasPressedThisFrame();
            bool walking = automated ? automatedWalk : walk.IsPressed();
            automatedDash = false;
            if (automated) AimPoint = automatedAim;
            else if (Application.isFocused) UpdateAim(input);
            if (!Application.isFocused && !automated) { input = Vector2.zero; wantsDash = false; }
            Vector3 desired = CameraRelative(input, viewCamera ? viewCamera.transform : null);
            float targetSpeed = walking ? Mathf.Min(walkSpeed, moveSpeed) : moveSpeed;
            if (combat && combat.IsAttacking) targetSpeed *= combat.MovementMultiplier;
            Vector3 aim = Vector3.ProjectOnPlane(AimPoint - transform.position, Vector3.up);
            cooldownRemaining = Mathf.Max(0, cooldownRemaining - dt);
            if (StaminaFree) Stamina = maxStamina;
            else if (Stamina < maxStamina && Time.time - LastDashStart >= staminaRegenDelay)
                Stamina = Mathf.Min(maxStamina, Stamina + dt / Mathf.Max(.01f, dashCooldown));
            // Dash always wins over an attack: it cancels the swing (and any hit stop) the moment it is pressed.
            if (wantsDash && cooldownRemaining <= 0 && (StaminaFree || Stamina >= 1) && (grounded || body.isGrounded))
            {
                dashDirection = desired.sqrMagnitude > .01f ? desired.normalized : visual.forward;
                dashDirection = Vector3.ProjectOnPlane(dashDirection, Vector3.up).normalized;
                dashRemaining = Mathf.Max(.01f, dashDuration); cooldownRemaining = dashInterval; DashCount++;
                if (!StaminaFree) Stamina -= 1;
                LastDashStart = Time.time;
                if (combat) combat.CancelAttack(false);
                HitFeedback.CancelHitStop();
                torsoYaw = 0;
                if (HasParameter(Dash, AnimatorControllerParameterType.Trigger)) animator.SetTrigger(Dash);
                // Cut straight into the dash pose instead of waiting for the attack clip's transition.
                if (animator && animator.isActiveAndEnabled && animator.HasState(0, DashState)) animator.CrossFadeInFixedTime(DashState, .03f, 0);
            }
            if (launched) { wantsDash = false; dashRemaining = 0; desired = Vector3.zero; }
            bool dashThisFrame = dashRemaining > 0;
            Vector3 displacement;
            if (launched)
            {
                // Ballistic flight after a throw: horizontal speed bleeds off a little, gravity below does the rest.
                launchTime += dt;
                displacement = launchVelocity * dt;
                launchVelocity = Vector3.MoveTowards(launchVelocity, Vector3.zero, 3f * dt);
                velocity = Vector3.zero;
            }
            else if (dashThisFrame)
            {
                float dashDt = Mathf.Min(dt, dashRemaining);
                // Integrate the dash speed curve over this frame, preserving distance at low FPS.
                float duration = Mathf.Max(.01f, dashDuration);
                float phase = 1f - dashRemaining / duration;
                float endPhase = Mathf.Clamp01(phase + dashDt / duration);
                displacement = dashDirection * (dashSpeed * duration * (DashProgress(endPhase) - DashProgress(phase)));
                displacement += desired * targetSpeed * Mathf.Max(0, dt - dashDt);
                dashRemaining = Mathf.Max(0, dashRemaining - dt);
                // Hand over without a hitch: a held stick runs on, a released one decelerates from the dash's exit speed.
                velocity = dashRemaining > 0 || desired.sqrMagnitude > .01f
                    ? desired * targetSpeed
                    : dashDirection * Mathf.Min(DashExitSpeed, moveSpeed);
            }
            else
            {
                Vector3 target = desired * targetSpeed;
                float rate = target.sqrMagnitude < velocity.sqrMagnitude ? deceleration : acceleration;
                velocity = Vector3.MoveTowards(velocity, target, Mathf.Max(.01f, rate) * dt);
                displacement = velocity * dt;
            }
            if (grounded && verticalSpeed < 0) verticalSpeed = -2f;
            else verticalSpeed = Mathf.Max(-25f, verticalSpeed - 25f * dt);
            if (launched && launchTime > .12f && grounded)
            {
                launched = false;
                launchVelocity = Vector3.zero;
                Landed?.Invoke();
            }
            Vector3 before = transform.position;
            CollisionFlags collision = launched
                ? body.Move(displacement + Vector3.up * (verticalSpeed * dt))
                : MoveWithSliding(displacement + Vector3.up * (verticalSpeed * dt));
            grounded = (collision & CollisionFlags.Below) != 0 || body.isGrounded;
            if ((collision & CollisionFlags.Below) != 0) verticalSpeed = -2f;
            dashMovedThisFrame = dashThisFrame;
            Vector3 actualMove = Vector3.ProjectOnPlane(transform.position - before, Vector3.up);
            // Touching a pillar's edge should slide, not cancel the entire dash.
            if (dashThisFrame && (collision & CollisionFlags.Sides) != 0
                && actualMove.magnitude < displacement.magnitude * .2f)
            { dashRemaining = 0; dashMovedThisFrame = false; velocity = Vector3.zero; }
            PlanarVelocity = dt > 0 ? actualMove / dt : Vector3.zero;
            // Feet prefer the travel direction. The hips follow the pointer only far enough
            // to keep the spine from twisting past a comfortable aiming angle.
            if (faceMovementDirection)
            {
                Vector3 facing = dashThisFrame ? dashDirection : combat && combat.IsAttacking ? combat.AttackDirection : desired;
                float facingSpeed = combat && combat.IsAttacking ? attackTurnSpeed * combat.AnimationSpeed : turnSpeed;
                if (facing.sqrMagnitude > .01f)
                    visual.rotation = Quaternion.RotateTowards(visual.rotation, Quaternion.LookRotation(facing), facingSpeed * dt);
                torsoYaw = Mathf.MoveTowards(torsoYaw, 0, torsoTurnSpeed * dt);
            }
            else if (aim.sqrMagnitude > aimDeadRadius * aimDeadRadius)
            {
                Vector3 aimForward = aim.normalized;
                Vector3 travel = dashThisFrame ? dashDirection : PlanarVelocity;
                Vector3 lowerForward = aimForward;
                if (travel.sqrMagnitude > .09f)
                {
                    lowerForward = travel.normalized;
                    float difference = Vector3.SignedAngle(lowerForward, aimForward, Vector3.up);
                    float aimAngle = Mathf.Abs(difference);
                    if (aimAngle > maxTorsoYaw)
                    {
                        // At exactly 180 degrees, +180 and -180 are the same facing.
                        // Ease out the torso twist near that point so both sides
                        // approach the same backward pose, without swapping strafes.
                        float torsoAllowance = maxTorsoYaw * (180f - aimAngle) / (180f - maxTorsoYaw);
                        float lowerYaw = difference - Mathf.Sign(difference) * torsoAllowance;
                        lowerForward = Quaternion.AngleAxis(lowerYaw, Vector3.up) * lowerForward;
                    }
                }
                visual.rotation = Quaternion.RotateTowards(visual.rotation, Quaternion.LookRotation(lowerForward), turnSpeed * dt);
                float targetYaw = Mathf.Clamp(Vector3.SignedAngle(visual.forward, aimForward, Vector3.up), -maxTorsoYaw, maxTorsoYaw);
                torsoYaw = Mathf.MoveTowards(torsoYaw, targetYaw, torsoTurnSpeed * dt);
            }
            else
            {
                // When the cursor is on the character, there is no useful aim direction.
                // Face the travel direction instead of playing a sideways gait.
                Vector3 travel = dashThisFrame ? dashDirection : PlanarVelocity;
                if (travel.sqrMagnitude > .09f)
                    visual.rotation = Quaternion.RotateTowards(visual.rotation, Quaternion.LookRotation(travel), turnSpeed * dt);
                torsoYaw = Mathf.MoveTowards(torsoYaw, 0f, torsoTurnSpeed * dt);
            }
            if (animator)
            {
                float safeSpeed = Mathf.Max(.01f, moveSpeed);
                Vector3 local = visual.InverseTransformDirection(PlanarVelocity) / safeSpeed;
                if (HasParameter(MoveX, AnimatorControllerParameterType.Float)) animator.SetFloat(MoveX, Mathf.Clamp(local.x, -1, 1), .08f, dt);
                if (HasParameter(MoveY, AnimatorControllerParameterType.Float)) animator.SetFloat(MoveY, Mathf.Clamp(local.z, -1, 1), .08f, dt);
                if (HasParameter(Speed, AnimatorControllerParameterType.Float)) animator.SetFloat(Speed, Mathf.Clamp01(PlanarVelocity.magnitude / safeSpeed), .08f, dt);
                if (HasParameter(Grounded, AnimatorControllerParameterType.Bool)) animator.SetBool(Grounded, grounded);
                if (HasParameter(Dashing, AnimatorControllerParameterType.Bool)) animator.SetBool(Dashing, IsDashing);
            }
            if (aimMarker) aimMarker.position = new Vector3(AimPoint.x, transform.position.y + .025f, AimPoint.z);
        }

        internal CollisionFlags MoveWithSliding(Vector3 displacement)
        {
            if (!body) body = GetComponent<CharacterController>();
            if (obstacleSlide == null) obstacleSlide = new CharacterObstacleSlide(body);
            return obstacleSlide.Move(displacement);
        }

        void UpdateAim(Vector2 input)
        {
            Vector2 stick = aimStick.ReadValue<Vector2>();
            var mouse = Mouse.current;
            if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > .1f || mouse.leftButton.wasPressedThisFrame)) gamepadAiming = false;
            if (stick.sqrMagnitude > .04f) gamepadAiming = true;
            else if (move.activeControl != null && move.activeControl.device is Gamepad && input.sqrMagnitude > .01f) gamepadAiming = true;
            if (gamepadAiming)
            {
                Vector2 aimInput = stick.sqrMagnitude > .04f ? stick : input;
                Vector3 direction = aimInput.sqrMagnitude > .01f
                    ? CameraRelative(aimInput, viewCamera ? viewCamera.transform : null).normalized : visual.forward;
                AimPoint = transform.position + direction * 5;
            }
            else if (mouse != null && ScreenToGround(mouse.position.ReadValue(), out var point)) AimPoint = point;
        }

        void LateUpdate()
        {
            if (faceMovementDirection || !animator || !animator.enabled || !animator.gameObject.activeInHierarchy) return;
            if (chest) chest.rotation = Quaternion.AngleAxis(torsoYaw * .72f, Vector3.up) * chest.rotation;
            if (neck) neck.rotation = Quaternion.AngleAxis(torsoYaw * .28f, Vector3.up) * neck.rotation;
        }

        void OnValidate() => dashCurveArea = null;

        float DashCurveValue(float t)
            => dashSpeedCurve != null && dashSpeedCurve.length > 0 ? Mathf.Max(0, dashSpeedCurve.Evaluate(t)) : 1f;

        float DashCurveArea()
        {
            if (dashCurveArea == null)
            {
                dashCurveArea = new float[DashCurveSamples + 1];
                float previous = DashCurveValue(0);
                for (int i = 1; i <= DashCurveSamples; i++)
                {
                    float value = DashCurveValue(i / (float)DashCurveSamples);
                    dashCurveArea[i] = dashCurveArea[i - 1] + (previous + value) * .5f / DashCurveSamples;
                    previous = value;
                }
            }
            return dashCurveArea[DashCurveSamples];
        }

        /// <summary>Share of the dash distance covered at dash progress t (0..1).</summary>
        float DashProgress(float t)
        {
            float area = DashCurveArea();
            if (area <= 1e-5f) return Mathf.Clamp01(t);
            float f = Mathf.Clamp01(t) * DashCurveSamples;
            int i = Mathf.Min((int)f, DashCurveSamples - 1);
            return Mathf.Lerp(dashCurveArea[i], dashCurveArea[i + 1], f - i) / area;
        }

        public void SetAutomationInput(Vector2 input, Vector3 aim, bool dashPressed = false, bool walkHeld = false)
        { automated = true; automatedMove = input; automatedAim = aim; automatedDash = dashPressed; automatedWalk = walkHeld; }
        public void ReleaseAutomation() { automated = false; automatedDash = false; automatedWalk = false; }
        /// <summary>
        /// Starts or ends an enemy grab. While held the CharacterController is off, so the holder can carry the
        /// transform freely; releasing turns it back on in place.
        /// </summary>
        public void SetHeld(bool held)
        {
            if (!body) body = GetComponent<CharacterController>();
            if (held == IsHeld) return;
            IsHeld = held;
            velocity = PlanarVelocity = Vector3.zero;
            dashRemaining = 0; dashMovedThisFrame = false; launched = false;
            if (combat) combat.CancelAttack();
            ResetDashAnimation();
            body.enabled = !held;
        }

        /// <summary>Throws the player: ballistic flight with no control until the next landing (raises Landed).</summary>
        public void Launch(Vector3 velocityWorld)
        {
            if (IsHeld) SetHeld(false);
            launchVelocity = Vector3.ProjectOnPlane(velocityWorld, Vector3.up);
            verticalSpeed = Mathf.Max(0, velocityWorld.y);
            launched = true;
            launchTime = 0;
            dashRemaining = 0;
            velocity = Vector3.zero;
            if (combat) combat.CancelAttack();
        }

        public void ResetAt(Vector3 position)
        {
            if (!body) body = GetComponent<CharacterController>();
            if (IsHeld) { IsHeld = false; body.enabled = true; }
            launched = false; launchVelocity = Vector3.zero; grounded = true;
            bool wasEnabled = body.enabled;
            body.enabled = false; transform.position = position; body.enabled = wasEnabled;
            velocity = Vector3.zero; verticalSpeed = -2; dashRemaining = 0; cooldownRemaining = 0; Stamina = maxStamina;
            PlanarVelocity = Vector3.zero; dashMovedThisFrame = false; torsoYaw = 0;
            ResetDashAnimation();
            AimPoint = position + (visual ? visual.forward : transform.forward) * 3;
            if (combat) combat.CancelAttack();
        }

        bool HasParameter(int hash, AnimatorControllerParameterType type) => animator && cachedController
            && animatorParameters.TryGetValue(hash, out var found) && found == type;

        void ResetDashAnimation()
        {
            if (cachedAnimator != animator || (animator && cachedController != animator.runtimeAnimatorController)) CacheAnimator();
            if (HasParameter(Dashing, AnimatorControllerParameterType.Bool)) animator.SetBool(Dashing, false);
            if (HasParameter(Dash, AnimatorControllerParameterType.Trigger)) animator.ResetTrigger(Dash);
        }
    }
}
