using System;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    public enum VendingMonsterState { Dormant, Awakening, Idle, Chase, ChargeWindup, Charging, ChargeRecover, CanThrow, Recovery, Dead }

    [DisallowMultipleComponent, RequireComponent(typeof(TrainingEnemy), typeof(CharacterController))]
    public sealed class VendingMonster : MonoBehaviour
    {
        [Header("Ambush")]
        [Min(.5f)] public float detectionRadius = 7f;
        public bool requireLineOfSight = true;
        [Header("Movement and attacks")]
        public float walkSpeed = 1.8f;
        public float chargeSpeed = 8.5f;
        public float chargeDistance = 10f;
        public float attackCooldown = 1.65f;
        public int chargeDamage = 22;
        public int canDamage = 14;
        [Header("Rig and reusable assets")]
        public Animator animator;
        public VendingMonsterRig rig;
        public Transform canGrip;
        public Transform dispenserSocket;
        public GameObject canPrefab;
        public Renderer[] limbRenderers;
        public LineRenderer warning;
        public AudioSource voice;
        public AudioClip emergeSound, footstepSound, canPullSound, throwSound, impactSound;
        [Header("Hit reaction")]
        [Tooltip("Knockback speed of a normal hit (m/s); scaled by the hit's impact.")]
        public float knockbackSpeed = 3.4f;
        [Tooltip("How far the cabinet rocks back per normal hit (degrees).")]
        public float hitTilt = 16f;
        public float staggerTime = .2f;
        public VendingMonsterState State { get; private set; } = VendingMonsterState.Dormant;
        public TrainingEnemy Health { get; private set; }
        public int ChargeCount { get; private set; }
        public int CansThrown { get; private set; }
        public int CansGripped { get; private set; }
        public float StateTime => stateTime;
        public LiminalPlayerHealth Target => player;
        public event Action<VendingMonsterState> StateChanged;

        LiminalPlayerHealth player;
        LiminalRoom room;
        CharacterController body;
        float stateTime, nextAttack, chargeTravel, footstepTime;
        int pattern, lastHits;
        bool initialized, hitDuringCharge, canPulled, canReleased, aimLocked;
        Vector3 chargeDirection, lockedThrowPoint;
        GameObject heldCan;
        // Hit reaction: a damped spring rocks the cabinet away from the hit, a squash pops back, the arms lag behind.
        Vector3 knockback, tiltAxis = Vector3.right;
        float tilt, tiltVelocity, squash, staggerUntil;

        void Awake()
        {
            Health = GetComponent<TrainingEnemy>(); body = GetComponent<CharacterController>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            Health.Defeated += OnDefeated;
            Health.Damaged += OnDamaged;
            // The monster plays its own death: knocked flying, limbs shrivel back in, crashes onto its back, sinks.
            Health.deferDeathVisuals = true;
        }

        void Start()
        {
            if (!initialized) Initialize(FindFirstObjectByType<LiminalPlayerHealth>(), GetComponentInParent<LiminalRoom>(), 0);
        }

        public void Initialize(LiminalPlayerHealth target, LiminalRoom owner = null, int stage = 0)
        {
            if (!Health) Health = GetComponent<TrainingEnemy>();
            if (!body) body = GetComponent<CharacterController>();
            player = target; room = owner;
            Health.Configure(165 + stage * 28, false);
            Health.CanBeTargeted = false;
            initialized = true; lastHits = 0; pattern = 0;
            SetState(VendingMonsterState.Dormant);
        }

        void Update()
        {
            if (!initialized || !Health || !Health.IsAlive || State == VendingMonsterState.Dead || Time.deltaTime <= 0) return;
            if (!player) player = FindFirstObjectByType<LiminalPlayerHealth>();
            if (!player || !player.IsAlive) { if (warning) warning.enabled = false; return; }
            stateTime += Time.deltaTime;
            UpdateKnockback();
            Vector3 delta = player.transform.position - transform.position; delta.y = 0;
            float distance = delta.magnitude;
            if (State == VendingMonsterState.Dormant)
            {
                if (Health.HitCount > lastHits || distance <= detectionRadius && (!requireLineOfSight || CanSeePlayer())) Activate();
                lastHits = Health.HitCount;
                return;
            }
            switch (State)
            {
                case VendingMonsterState.Awakening:
                    SetBodyHeight(Mathf.Lerp(1.9f, 3.08f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.35f, 2.65f, stateTime))));
                    if (stateTime >= VendingMonsterRig.AwakenDuration) { nextAttack = Time.time + .65f; SetState(VendingMonsterState.Idle); }
                    break;
                case VendingMonsterState.Idle:
                case VendingMonsterState.Chase:
                    TurnTo(delta, 150);
                    if (Time.time >= nextAttack && distance < 13 && CanSeePlayer())
                    {
                        if (pattern++ % 2 == 0 && distance > 2.5f) SetState(VendingMonsterState.ChargeWindup);
                        else SetState(VendingMonsterState.CanThrow);
                        break;
                    }
                    bool walk = distance > 4 && Time.time >= staggerUntil;
                    if (walk)
                    {
                        if (State != VendingMonsterState.Chase) SetState(VendingMonsterState.Chase);
                        Vector3 beforeMove=transform.position;
                        Move(Steer(delta.normalized) * walkSpeed * Time.deltaTime);
                        if(animator)animator.speed=Mathf.Clamp01(Vector3.ProjectOnPlane(transform.position-beforeMove,Vector3.up).magnitude/(walkSpeed*Time.deltaTime));
                        StepSound(.42f);
                    }
                    else if (State != VendingMonsterState.Idle) SetState(VendingMonsterState.Idle);
                    break;
                case VendingMonsterState.ChargeWindup:
                    if (stateTime < .35f) { TurnTo(delta, 90); chargeDirection = transform.forward; }
                    DrawChargeWarning();
                    if (stateTime >= VendingMonsterRig.WindupDuration) SetState(VendingMonsterState.Charging);
                    break;
                case VendingMonsterState.Charging:
                    float distanceThisFrame = chargeSpeed * Mathf.SmoothStep(.45f, 1, stateTime / .23f) * Time.deltaTime;
                    Vector3 previous = transform.position;
                    Move(chargeDirection * distanceThisFrame);
                    float travelled = Vector3.Distance(previous, transform.position); chargeTravel += travelled;
                    if (!hitDuringCharge && SweptPlayer(previous, transform.position, 1.04f))
                    { player.TakeDamage(chargeDamage); hitDuringCharge = true; Play(impactSound, .8f); }
                    if(animator)animator.speed=Mathf.Clamp01(travelled/(chargeSpeed*Time.deltaTime));
                    StepSound(.15f);
                    if (chargeTravel >= chargeDistance || travelled < distanceThisFrame * .3f || stateTime >= 1.65f)
                    { Play(impactSound, .45f); SetState(VendingMonsterState.ChargeRecover); }
                    break;
                case VendingMonsterState.ChargeRecover:
                    if (stateTime >= VendingMonsterRig.ChargeRecoverDuration) FinishAttack();
                    break;
                case VendingMonsterState.CanThrow:
                    // Aim locks before the release. The player can dodge the physical projectile.
                    if (stateTime < 1.2f) TurnTo(delta, 110);
                    if (!aimLocked && stateTime >= 1.45f) { lockedThrowPoint=player.transform.position+Vector3.up*.85f;aimLocked=true; }
                    if (stateTime >= VendingMonsterRig.ThrowDuration) FinishAttack();
                    break;
                case VendingMonsterState.Recovery:
                    if (stateTime >= .6f) SetState(VendingMonsterState.Idle);
                    break;
            }
        }

        void LateUpdate()
        {
            ApplyHitPose();
            if(State!=VendingMonsterState.CanThrow || !Health || !Health.IsAlive || Time.deltaTime<=0) return;
            // Event fallback also runs after Animator evaluation, covering large frame steps.
            if(!canPulled && stateTime>=VendingMonsterRig.CanGrabTime) PullCan();
            if(!canReleased && stateTime>=VendingMonsterRig.CanReleaseTime) ReleaseCan();
        }
        public void GrabCanFromAnimation() {if(State==VendingMonsterState.CanThrow&&!canPulled)PullCan();}
        public void ReleaseCanFromAnimation() {if(State==VendingMonsterState.CanThrow&&!canReleased)ReleaseCan();}

        public bool Activate()
        {
            if (State != VendingMonsterState.Dormant || !Health || !Health.IsAlive) return false;
            Health.CanBeTargeted = true;
            SetState(VendingMonsterState.Awakening); Play(emergeSound, .9f);
            return true;
        }

        void SetState(VendingMonsterState state)
        {
            State = state; stateTime = 0; footstepTime = 0;
            if (warning) warning.enabled = state == VendingMonsterState.ChargeWindup;
            if (state == VendingMonsterState.Dormant)
            {
                foreach (var r in limbRenderers) if (r) r.enabled = false;
                SetBodyHeight(1.9f);
            }
            else if (state == VendingMonsterState.Awakening)
            { foreach (var r in limbRenderers) if (r) r.enabled = true; }
            else if (state == VendingMonsterState.ChargeWindup) chargeDirection = transform.forward;
            else if (state == VendingMonsterState.Charging)
            { chargeTravel = 0; hitDuringCharge = false; ChargeCount++; }
            else if (state == VendingMonsterState.CanThrow)
            { canPulled = false; canReleased = false; aimLocked=false; }
            if (animator)
            {
                animator.speed = 1;
                if (state == VendingMonsterState.Dormant || state == VendingMonsterState.Awakening) animator.Play(state.ToString(), 0, 0);
                else animator.CrossFadeInFixedTime(state.ToString(), .12f, 0, 0);
            }
            StateChanged?.Invoke(state);
        }

        void FinishAttack() { nextAttack = Time.time + attackCooldown; SetState(VendingMonsterState.Recovery); }
        void SetBodyHeight(float height) { body.height = height; body.center = Vector3.up * (height * .5f); }
        void TurnTo(Vector3 direction, float degrees)
        { if (direction.sqrMagnitude > .001f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), degrees * Time.deltaTime); }
        void Move(Vector3 step)
        {
            if (room && !room.Contains(transform.position + step, .7f)) step = Vector3.zero;
            body.Move(step + Vector3.down * (3 * Time.deltaTime));
        }
        Vector3 Steer(Vector3 direction)
        {
            for (int i = 0; i < 7; i++)
            {
                float angle = i == 0 ? 0 : (i % 2 == 0 ? -1 : 1) * ((i + 1) / 2) * 30;
                Vector3 candidate = Quaternion.Euler(0, angle, 0) * direction;
                bool blocked = false;
                foreach (var hit in Physics.SphereCastAll(transform.position + Vector3.up * .9f, body.radius * .9f, candidate, 1.1f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform.IsChildOf(transform) || hit.transform == transform || hit.collider.GetComponentInParent<LiminalPlayerHealth>()) continue;
                    blocked = true; break;
                }
                if (!blocked) return candidate;
            }
            return Vector3.zero;
        }
        bool CanSeePlayer()
        {
            Vector3 origin = transform.position + Vector3.up * 1.2f;
            Vector3 to = player.transform.position + Vector3.up * .85f - origin;
            foreach (var hit in Physics.RaycastAll(origin, to.normalized, to.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == transform || hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<LiminalPlayerHealth>()) continue;
                return false;
            }
            return true;
        }
        bool SweptPlayer(Vector3 a, Vector3 b, float radius)
        {
            Vector3 p = player.transform.position; a.y = b.y = p.y = 0;
            Vector3 ab = b - a; float t = ab.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
            return Vector3.Distance(p, a + ab * t) < radius && Mathf.Abs(player.transform.position.y - transform.position.y) < 2.5f;
        }
        void DrawChargeWarning()
        {
            if (!warning) return;
            Vector3 p = transform.position + Vector3.up * .06f;
            Vector3 side = Vector3.Cross(Vector3.up, chargeDirection) * .72f;
            warning.positionCount = 5;
            warning.SetPositions(new[] { p-side, p+side, p+chargeDirection*chargeDistance+side, p+chargeDirection*chargeDistance-side, p-side });
            warning.widthMultiplier = Mathf.Lerp(.025f, .11f, stateTime / VendingMonsterRig.WindupDuration);
        }
        void PullCan()
        {
            canPulled = true;
            if (!canPrefab || !canGrip) return;
            heldCan = Instantiate(canPrefab, canGrip); heldCan.transform.localPosition = Vector3.zero;
            heldCan.transform.localRotation = Quaternion.identity; CansGripped++;
            Play(canPullSound, .75f);
        }
        void ReleaseCan()
        {
            canReleased = true;
            if (!heldCan || !player) return;
            if(!aimLocked) lockedThrowPoint = player.transform.position + Vector3.up * .85f;
            heldCan.transform.SetParent(null, true);
            heldCan.GetComponent<VendingCanProjectile>().Launch(lockedThrowPoint,
                Mathf.Clamp(Vector3.Distance(heldCan.transform.position, lockedThrowPoint) / 13f, .38f, .85f), canDamage, transform, player);
            heldCan = null; CansThrown++; Play(throwSound, .75f);
        }
        void StepSound(float interval) { footstepTime -= Time.deltaTime; if (footstepTime <= 0) { Play(footstepSound, .32f); footstepTime = interval; } }
        void Play(AudioClip clip, float volume) { if (voice && clip) voice.PlayOneShot(clip, volume); }
        void OnDamaged(TrainingEnemy enemy, int amount)
        {
            if (!enemy.IsAlive || State == VendingMonsterState.Dormant) return;
            Vector3 direction = HitDirection();
            float impact = Mathf.Clamp(enemy.LastHitImpact, .3f, 2f);
            // Heavy appliance: a charge barely moves, a wind-up gives a little, everything else is shoved back.
            float weight = State == VendingMonsterState.Charging ? .2f : State == VendingMonsterState.ChargeWindup || State == VendingMonsterState.Awakening ? .55f : 1f;
            knockback += direction * knockbackSpeed * impact * weight;
            Vector3 axis = Vector3.Cross(Vector3.up, direction);
            if (axis.sqrMagnitude > .001f) tiltAxis = axis.normalized;
            tiltVelocity += hitTilt * 15f * impact * Mathf.Lerp(.5f, 1f, weight);
            squash = Mathf.Max(squash, .09f * impact);
            if (weight >= 1) staggerUntil = Time.time + staggerTime * Mathf.Min(impact, 1.6f);
            // A hit during the charge wind-up delays the charge a little instead of cancelling it.
            if (State == VendingMonsterState.ChargeWindup) stateTime = Mathf.Max(0, stateTime - .07f);
            Vector3 coins = dispenserSocket ? dispenserSocket.position : Health.AimPoint;
            VendingMonsterDeath.SpillCoins(coins, direction, impact > 1.2f ? 5 : 2);
            Play(impactSound, .22f);
        }

        Vector3 HitDirection()
        {
            Vector3 direction = Health ? Health.LastHitDirection : Vector3.zero;
            if (direction.sqrMagnitude < .01f && player) direction = transform.position - player.transform.position;
            direction.y = 0;
            return direction.sqrMagnitude > .001f ? direction.normalized : -transform.forward;
        }

        void UpdateKnockback()
        {
            if (knockback.sqrMagnitude < .0025f) { knockback = Vector3.zero; return; }
            Move(knockback * Time.deltaTime);
            knockback = Vector3.MoveTowards(knockback, Vector3.zero, 20f * Time.deltaTime);
        }

        /// <summary>Runs after the Animator: rocks the whole visual on its feet and lets the arms lag behind the jolt.</summary>
        void ApplyHitPose()
        {
            if (!animator || State == VendingMonsterState.Dead) return;
            float dt = Time.deltaTime;
            if (dt > 0)
            {
                // Under-damped spring (about 2.9 Hz): the cabinet tips away, swings back past upright, settles.
                const float omega = 18f, damping = .32f;
                tiltVelocity += (-omega * omega * tilt - 2 * damping * omega * tiltVelocity) * dt;
                tilt = Mathf.Clamp(tilt + tiltVelocity * dt, -hitTilt * 1.8f, hitTilt * 2.4f);
                squash = Mathf.MoveTowards(squash, 0, .6f * dt);
            }
            Transform visual = animator.transform;
            if (visual == transform) return;
            Vector3 localAxis = Quaternion.Inverse(transform.rotation) * tiltAxis;
            visual.localRotation = Quaternion.AngleAxis(tilt, localAxis);
            // Impact squash: shorter and wider for a moment, then back.
            visual.localScale = new Vector3(1 + squash * .55f, 1 - squash, 1 + squash * .55f);
            if (Mathf.Abs(tilt) < .05f) return;
            if (rig && State != VendingMonsterState.Dormant)
            {
                // The arms hang on for a moment while the body is shoved: they swing the opposite way, a bit later.
                foreach (var arm in new[] { rig.leftArm, rig.rightArm })
                    if (arm != null && arm.upper) arm.upper.rotation = Quaternion.AngleAxis(-tilt * 1.7f, tiltAxis) * arm.upper.rotation;
                foreach (var arm in new[] { rig.leftArm, rig.rightArm })
                    if (arm != null && arm.lower) arm.lower.rotation = Quaternion.AngleAxis(-tilt * 1.1f, tiltAxis) * arm.lower.rotation;
            }
        }

        void OnDefeated(TrainingEnemy _)
        {
            State = VendingMonsterState.Dead; Health.CanBeTargeted = false;
            if (warning) warning.enabled = false;
            if (heldCan) Destroy(heldCan);
            foreach (var can in FindObjectsByType<VendingCanProjectile>(FindObjectsSortMode.None))
                if (can.transform.IsChildOf(transform)) Destroy(can.gameObject);
            if (body) body.enabled = false;
            if (voice) voice.Stop();
            knockback = Vector3.zero;
            // A separate component plays the death, so it also runs while this behaviour is disabled.
            var death = GetComponent<VendingMonsterDeath>();
            if (!death) death = gameObject.AddComponent<VendingMonsterDeath>();
            death.Play(this, HitDirection(), Mathf.Clamp(Health ? Health.LastHitImpact : 1, .6f, 2f));
        }
        void OnDestroy() { if (Health) { Health.Defeated -= OnDefeated; Health.Damaged -= OnDamaged; } if (heldCan) Destroy(heldCan); }
        void OnDrawGizmosSelected() { Gizmos.color = new Color(.1f, .85f, .7f, .7f); Gizmos.DrawWireSphere(transform.position, detectionRadius); }
    }
}
