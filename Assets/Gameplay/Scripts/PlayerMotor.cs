using UnityEngine;
using UnityEngine.InputSystem;

namespace AcRoguelike
{
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
        public float dashCooldown = .85f;
        public float turnSpeed = 540f;
        public float aimDeadRadius = .65f;
        [Range(15f, 85f)] public float maxTorsoYaw = 62f;
        public float torsoTurnSpeed = 400f;
        public float acceleration = 32f;
        public bool IsDashing => dashRemaining > 0;
        public float CooldownRemaining => Mathf.Max(0, cooldownRemaining);
        public Vector3 AimPoint { get; private set; }
        public Vector3 PlanarVelocity { get; private set; }
        public int DashCount { get; private set; }
        public InputAction MoveAction => move;
        public InputAction DashAction => dash;
        public InputAction WalkAction => walk;
        CharacterController body;
        InputAction move, dash, walk;
        Vector3 velocity, dashDirection;
        float verticalSpeed, dashRemaining, cooldownRemaining;
        bool automated;
        Vector2 automatedMove;
        Vector3 automatedAim;
        bool automatedDash;
        bool automatedWalk;
        Transform chest, neck;
        float torsoYaw;
        static readonly int MoveX = Animator.StringToHash("MoveX");
        static readonly int MoveY = Animator.StringToHash("MoveY");
        static readonly int Dashing = Animator.StringToHash("Dashing");

        void Awake()
        {
            body = GetComponent<CharacterController>();
            move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            dash = new InputAction("Dash", InputActionType.Button);
            dash.AddBinding("<Keyboard>/space"); dash.AddBinding("<Keyboard>/leftShift");
            walk = new InputAction("Walk", InputActionType.Button);
            walk.AddBinding("<Keyboard>/leftCtrl");
            AimPoint = transform.position + Vector3.forward * 3;
            if (animator)
            {
                animator.applyRootMotion = false;
                CacheAimBones();
            }
        }
        void CacheAimBones()
        {
            foreach (var bone in animator.GetComponentsInChildren<Transform>())
            {
                if (bone.name == "Chest") chest = bone;
                else if (bone.name == "Neck") neck = bone;
            }
        }
        void OnEnable() { move?.Enable(); dash?.Enable(); walk?.Enable(); }
        void OnDisable() { move?.Disable(); dash?.Disable(); walk?.Disable(); velocity = Vector3.zero; }
        void OnDestroy() { move?.Dispose(); dash?.Dispose(); walk?.Dispose(); }

        public static Vector3 CameraRelative(Vector2 input, Transform cameraTransform)
        {
            Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            input = Vector2.ClampMagnitude(input, 1);
            return right * input.x + forward * input.y;
        }

        public bool ScreenToGround(Vector2 screen, out Vector3 point)
        {
            point = AimPoint;
            if (!viewCamera) return false;
            Ray ray = viewCamera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0, .015f, 0));
            if (!plane.Raycast(ray, out float distance) || distance < 0) return false;
            point = ray.GetPoint(distance); return true;
        }

        void Update()
        {
            if (!viewCamera) viewCamera = Camera.main;
            if (!viewCamera || !visual) return;
            float dt = Time.deltaTime;
            Vector2 input = automated ? automatedMove : move.ReadValue<Vector2>();
            bool wantsDash = automated ? automatedDash : dash.WasPressedThisFrame();
            bool walking = automated ? automatedWalk : walk.IsPressed();
            automatedDash = false;
            if (automated) AimPoint = automatedAim;
            else if (Mouse.current != null && Application.isFocused && ScreenToGround(Mouse.current.position.ReadValue(), out var point)) AimPoint = point;
            if (!Application.isFocused && !automated) { input = Vector2.zero; wantsDash = false; }
            Vector3 desired = CameraRelative(input, viewCamera.transform);
            float targetSpeed = walking ? Mathf.Min(walkSpeed, moveSpeed) : moveSpeed;
            Vector3 aim = Vector3.ProjectOnPlane(AimPoint - transform.position, Vector3.up);
            cooldownRemaining = Mathf.Max(0, cooldownRemaining - dt);
            if (wantsDash && cooldownRemaining <= 0 && body.isGrounded)
            {
                dashDirection = desired.sqrMagnitude > .01f ? desired.normalized : visual.forward;
                dashRemaining = dashDuration; cooldownRemaining = dashCooldown; DashCount++;
            }
            bool dashThisFrame = dashRemaining > 0;
            Vector3 displacement;
            if (dashThisFrame)
            {
                float dashDt = Mathf.Min(dt, dashRemaining);
                displacement = dashDirection * dashSpeed * dashDt;
                displacement += desired * targetSpeed * Mathf.Max(0, dt - dashDt);
                dashRemaining = Mathf.Max(0, dashRemaining - dt);
                velocity = desired * targetSpeed;
            }
            else { velocity = Vector3.MoveTowards(velocity, desired * targetSpeed, acceleration * dt); displacement = velocity * dt; }
            if (body.isGrounded && verticalSpeed < 0) verticalSpeed = -2f;
            else verticalSpeed = Mathf.Max(-25f, verticalSpeed - 25f * dt);
            Vector3 before = transform.position;
            CollisionFlags collision = body.Move(displacement + Vector3.up * (verticalSpeed * dt));
            if ((collision & CollisionFlags.Below) != 0) verticalSpeed = -2f;
            if (dashThisFrame && (collision & CollisionFlags.Sides) != 0) dashRemaining = 0;
            PlanarVelocity = dt > 0 ? Vector3.ProjectOnPlane(transform.position - before, Vector3.up) / dt : Vector3.zero;
            // Feet prefer the travel direction. The hips follow the pointer only far enough
            // to keep the spine from twisting past a comfortable aiming angle.
            if (aim.sqrMagnitude > aimDeadRadius * aimDeadRadius)
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
                Vector3 local = visual.InverseTransformDirection(PlanarVelocity) / moveSpeed;
                animator.SetFloat(MoveX, Mathf.Clamp(local.x, -1, 1), .08f, dt);
                animator.SetFloat(MoveY, Mathf.Clamp(local.z, -1, 1), .08f, dt);
                animator.SetBool(Dashing, dashThisFrame);
            }
            if (aimMarker) aimMarker.position = new Vector3(AimPoint.x, .025f, AimPoint.z);
        }

        void LateUpdate()
        {
            if (!chest && animator) CacheAimBones();
            if (chest) chest.rotation = Quaternion.AngleAxis(torsoYaw * .72f, Vector3.up) * chest.rotation;
            if (neck) neck.rotation = Quaternion.AngleAxis(torsoYaw * .28f, Vector3.up) * neck.rotation;
        }

        public void SetAutomationInput(Vector2 input, Vector3 aim, bool dashPressed = false, bool walkHeld = false)
        { automated = true; automatedMove = input; automatedAim = aim; automatedDash = dashPressed; automatedWalk = walkHeld; }
        public void ReleaseAutomation() { automated = false; automatedDash = false; automatedWalk = false; }
        public void ResetAt(Vector3 position)
        {
            body.enabled = false; transform.position = position; body.enabled = true;
            velocity = Vector3.zero; verticalSpeed = -2; dashRemaining = 0; cooldownRemaining = 0;
        }
    }
}
