using System;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    public enum TrafficLampColor { Off, Red, Yellow, Green }

    /// <summary>Giant wire-limbed traffic signal. Its lamps change constantly and announce each attack.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(TrainingEnemy), typeof(CharacterController))]
    public sealed class TrafficLightBoss : MonoBehaviour
    {
        [Header("Encounter")]
        [Min(1)] public float detectionRadius = 26f;
        public bool requireLineOfSight;
        public string displayName = "교차로의 신호등";
        [Header("Movement")]
        public float walkSpeed = 2.2f;
        public float keepDistance = 9.5f;
        [Tooltip("Attacks start only inside this range; farther away the boss walks closer first.")] public float attackRange = 13f;
        [Header("Pattern 1: red light field")]
        public int fieldDamage = 28;
        public float safeRadius = 3.2f;
        public int safeZoneCount = 3;
        [Header("Pattern 2: thrown cars")]
        public int carDamage = 26;
        public float carSpeed = 17f;
        public float attackCooldown = 1.5f;
        [Header("Lamps")]
        public float redTime = .9f, greenTime = .9f, yellowTime = .4f;
        [Header("Arena and camera")]
        public Vector2 arenaSize = new Vector2(28, 36);
        public Vector3 arenaCenter;
        public bool arenaCenterSet;
        public float cameraDistance = 24f, cameraPitch = 62f, cameraFocusLift = 6.5f;
        [Header("Rig and reusable assets")]
        public Animator animator;
        public TrafficLightBossRig rig;
        public Transform carSocket;
        public GameObject[] carPrefabs = new GameObject[0];
        public Renderer[] lampLenses = new Renderer[3];
        public Material[] lensLit = new Material[3];
        public Light glow;
        public Renderer[] limbRenderers;
        public LineRenderer warning;
        public TrafficLightRedField field;
        public AudioSource voice;
        public AudioClip emergeSound, stepSound, lampSound, slamSound, carRiseSound, throwSound, impactSound;
        public TrafficLightBossState State { get; private set; } = TrafficLightBossState.Dormant;
        public TrainingEnemy Health { get; private set; }
        public TrafficLampColor Lamp { get; private set; } = TrafficLampColor.Off;
        public int LampChanges { get; private set; }
        public int FieldCount { get; private set; }
        public int FieldHits { get; private set; }
        public int CarsThrown { get; private set; }
        public int CarVariantsUsed => variantMask;
        public float StateTime => stateTime;
        public bool Enraged => Health && Health.Health < Health.maxHealth / 2;
        public LiminalPlayerHealth Target => player;
        public event Action<TrafficLightBossState> StateChanged;

        LiminalPlayerHealth player;
        LiminalRoom room;
        CharacterController body;
        CharacterObstacleSlide movement;
        IsometricFollowCamera followCamera;
        float originalPitch = -1;
        float stateTime, nextAttack, footstepTime, lampTimer, nextFieldTick, carPopTime;
        int pattern, lastHits, carIndex = -1, variantMask;
        bool initialized, fieldShown, fieldJudged, carSpawned, carReleased, aimLocked, repeatThrow;
        Vector3 lockedTarget;
        Quaternion arenaRotation = Quaternion.identity;
        GameObject heldCar;
        readonly System.Collections.Generic.List<int> carBag = new System.Collections.Generic.List<int>();

        void Awake()
        {
            TelegraphOverlay.Attach(warning, TrafficLightBossRig.CarReleaseTime - TrafficLightBossRig.CarAimTime);
            Health = GetComponent<TrainingEnemy>(); body = GetComponent<CharacterController>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            Health.Defeated += OnDefeated;
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
            if (!room && !arenaCenterSet) arenaCenter = transform.position;
            Health.Configure(1200 + stage * 50, false);
            Health.CanBeTargeted = false;
            initialized = true; lastHits = 0; pattern = 0; repeatThrow = false;
            SetState(TrafficLightBossState.Dormant);
        }

        void Update()
        {
            if (!initialized || !Health || !Health.IsAlive || State == TrafficLightBossState.Dead || Time.deltaTime <= 0) return;
            if (!player) player = FindFirstObjectByType<LiminalPlayerHealth>();
            if (!player || !player.IsAlive) { if (warning) warning.enabled = false; return; }
            stateTime += Time.deltaTime;
            Vector3 delta = player.transform.position - transform.position; delta.y = 0;
            float distance = delta.magnitude;
            UpdateLamps();
            if (State == TrafficLightBossState.Dormant)
            {
                if (Health.HitCount > lastHits || distance <= detectionRadius && (!requireLineOfSight || CanSeePlayer())) Activate();
                lastHits = Health.HitCount;
                return;
            }
            switch (State)
            {
                case TrafficLightBossState.Awakening:
                    SetBodyHeight(Mathf.Lerp(TrafficLightBossRig.DormantHeight, TrafficLightBossRig.StandingHeight, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.6f, 3f, stateTime))));
                    if (stateTime >= TrafficLightBossRig.AwakenDuration) { nextAttack = Time.time + 1.1f; SetState(TrafficLightBossState.Idle); }
                    break;
                case TrafficLightBossState.Idle:
                case TrafficLightBossState.Walk:
                    TurnTo(delta, 70);
                    if (Time.time >= nextAttack && distance <= attackRange) { BeginNextAttack(); break; }
                    if (distance > keepDistance)
                    {
                        if (State != TrafficLightBossState.Walk) SetState(TrafficLightBossState.Walk);
                        Vector3 before = transform.position;
                        Move(Steer(delta.normalized) * walkSpeed * Time.deltaTime);
                        if (animator) animator.speed = Mathf.Clamp01(Vector3.ProjectOnPlane(transform.position - before, Vector3.up).magnitude / (walkSpeed * Time.deltaTime));
                        StepSound(TrafficLightBossRig.WalkDuration * .5f);
                    }
                    else if (State != TrafficLightBossState.Idle) SetState(TrafficLightBossState.Idle);
                    break;
                case TrafficLightBossState.FieldCast:
                    UpdateField(delta);
                    break;
                case TrafficLightBossState.CarThrow:
                    UpdateCarThrow(delta);
                    break;
                case TrafficLightBossState.Recovery:
                    if (stateTime >= TrafficLightBossRig.RecoveryDuration) SetState(TrafficLightBossState.Idle);
                    break;
            }
        }

        void LateUpdate()
        {
            if (!Health || !Health.IsAlive || Time.deltaTime <= 0) return;
            if (State == TrafficLightBossState.CarThrow)
            {
                // Event fallback also runs after Animator evaluation, covering large frame steps.
                if (!carSpawned && stateTime >= TrafficLightBossRig.CarSpawnTime) SpawnCar();
                if (!carReleased && stateTime >= TrafficLightBossRig.CarReleaseTime) ReleaseCar();
                if (heldCar && carSocket)
                {
                    float pop = Mathf.SmoothStep(.15f, 1, (Time.time - carPopTime) / .3f);
                    heldCar.transform.localScale = Vector3.one * pop;
                    heldCar.transform.SetPositionAndRotation(carSocket.position, Quaternion.Euler(0, transform.eulerAngles.y, 0));
                }
            }
            AssistCamera();
        }

        public void SpawnCarFromAnimation() { if (State == TrafficLightBossState.CarThrow && !carSpawned) SpawnCar(); }
        public void ReleaseCarFromAnimation() { if (State == TrafficLightBossState.CarThrow && !carReleased) ReleaseCar(); }

        /// <summary>Starts one attack immediately from Idle or Walk; used by encounter scripts and validation.</summary>
        public bool StartAttack(TrafficLightBossState attack)
        {
            if ((State != TrafficLightBossState.Idle && State != TrafficLightBossState.Walk) || (attack != TrafficLightBossState.FieldCast && attack != TrafficLightBossState.CarThrow)) return false;
            SetState(attack); return true;
        }

        public bool Activate()
        {
            if (State != TrafficLightBossState.Dormant || !Health || !Health.IsAlive) return false;
            Health.CanBeTargeted = true;
            SetState(TrafficLightBossState.Awakening); Play(emergeSound, 1);
            return true;
        }

        // ---- Pattern 1 -----------------------------------------------------------------------------------------
        void UpdateField(Vector3 delta)
        {
            if (stateTime < .6f) TurnTo(delta, 60);
            if (!fieldShown && stateTime >= .45f) ShowField();
            if (!fieldJudged && stateTime >= TrafficLightBossRig.FieldJudgeTime)
            {
                fieldJudged = true; nextFieldTick = stateTime;
                if (field) field.Judge();
                SetLamp(TrafficLampColor.Green); Play(slamSound, 1);
            }
            if (fieldJudged && stateTime < TrafficLightBossRig.FieldJudgeTime + .9f && stateTime >= nextFieldTick)
            {
                nextFieldTick = stateTime + .45f;
                if (field && !field.IsSafe(player.transform.position) && player.TakeDamage(Enraged ? fieldDamage + 6 : fieldDamage)) FieldHits++;
            }
            if (fieldShown && stateTime >= TrafficLightBossRig.FieldJudgeTime + .95f && field && field.Phase != RedFieldPhase.Hidden) field.Hide();
            if (stateTime >= TrafficLightBossRig.FieldDuration) FinishAttack();
        }

        void ShowField()
        {
            fieldShown = true; FieldCount++;
            if (!field) return;
            GetArena(out Vector3 center, out Quaternion rotation, out Vector2 size);
            float radius = Enraged ? safeRadius * .85f : safeRadius;
            int count = Enraged ? Mathf.Max(2, safeZoneCount - 1) : safeZoneCount;
            field.Show(center, rotation, size, ChooseSafeZones(center, rotation, size, radius, count), radius, .7f);
        }

        /// <summary>One circle is always reachable before the judgement; the others are spread across the room.</summary>
        Vector3[] ChooseSafeZones(Vector3 center, Quaternion rotation, Vector2 size, float radius, int count)
        {
            var result = new System.Collections.Generic.List<Vector3>();
            float halfX = size.x * .5f - radius - 1, halfZ = size.y * .5f - radius - 1;
            Vector3 playerLocal = Quaternion.Inverse(rotation) * (player.transform.position - center);
            float angle = UnityEngine.Random.value * Mathf.PI * 2, reach = UnityEngine.Random.Range(3.5f, 7.5f);
            Vector3 near = playerLocal + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * reach;
            near.x = Mathf.Clamp(near.x, -halfX, halfX); near.z = Mathf.Clamp(near.z, -halfZ, halfZ);
            result.Add(near);
            for (int attempt = 0; attempt < 60 && result.Count < count; attempt++)
            {
                var p = new Vector3(UnityEngine.Random.Range(-halfX, halfX), 0, UnityEngine.Random.Range(-halfZ, halfZ));
                bool apart = true;
                foreach (var other in result) if ((other - p).sqrMagnitude < (radius * 2 + 2) * (radius * 2 + 2)) apart = false;
                if (apart) result.Add(p);
            }
            for (int i = 0; i < result.Count; i++) { var w = center + rotation * result[i]; w.y = transform.position.y; result[i] = w; }
            return result.ToArray();
        }

        // ---- Pattern 2 -----------------------------------------------------------------------------------------
        void UpdateCarThrow(Vector3 delta)
        {
            if (stateTime < TrafficLightBossRig.CarAimTime) TurnTo(delta, 80);
            if (!aimLocked && stateTime >= TrafficLightBossRig.CarAimTime)
            { lockedTarget = player.transform.position; aimLocked = true; if (warning) warning.enabled = true; }
            if (aimLocked && !carReleased) DrawCarLine();
            if (carReleased && warning) warning.enabled = false;
            if (stateTime >= TrafficLightBossRig.ThrowDuration) FinishAttack();
        }

        Vector3 ReleasePoint() { var p = transform.TransformPoint(new Vector3(.6f, 0, 5.2f)); p.y = transform.position.y; return p; }

        void DrawCarLine()
        {
            if (!warning) return;
            Vector3 start = ReleasePoint(), heading = Vector3.ProjectOnPlane(lockedTarget - start, Vector3.up);
            heading = heading.sqrMagnitude > .01f ? heading.normalized : transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, heading) * 1.3f, end = start + heading * 60, lift = Vector3.up * .08f;
            warning.positionCount = 5;
            warning.SetPositions(new[] { start - side + lift, start + side + lift, end + side + lift, end - side + lift, start - side + lift });
            warning.widthMultiplier = Mathf.Lerp(.06f, .28f, Mathf.InverseLerp(TrafficLightBossRig.CarAimTime, TrafficLightBossRig.CarReleaseTime, stateTime));
        }

        void SpawnCar()
        {
            carSpawned = true;
            if (carPrefabs == null || carPrefabs.Length == 0 || !carSocket) return;
            carIndex = NextCarIndex();
            variantMask |= 1 << carIndex;
            heldCar = Instantiate(carPrefabs[carIndex], carSocket.position, transform.rotation);
            carPopTime = Time.time; Play(carRiseSound, 1);
        }

        // Shuffled bag: every model appears once per cycle and never twice in a row.
        int NextCarIndex()
        {
            if (carBag.Count == 0)
            {
                for (int i = 0; i < carPrefabs.Length; i++) carBag.Add(i);
                for (int i = carBag.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (carBag[i], carBag[j]) = (carBag[j], carBag[i]); }
                if (carBag.Count > 1 && carBag[carBag.Count - 1] == carIndex) (carBag[0], carBag[carBag.Count - 1]) = (carBag[carBag.Count - 1], carBag[0]);
            }
            int next = carBag[carBag.Count - 1]; carBag.RemoveAt(carBag.Count - 1);
            return next;
        }

        void ReleaseCar()
        {
            carReleased = true;
            if (!heldCar || !player) return;
            if (!aimLocked) lockedTarget = player.transform.position;
            Vector3 origin = ReleasePoint(); origin.y = heldCar.transform.position.y;
            heldCar.transform.position = origin; heldCar.transform.localScale = Vector3.one;
            var car = heldCar.GetComponent<TrafficLightCarProjectile>();
            car.Launch(lockedTarget - origin, carSpeed, Enraged ? carDamage + 6 : carDamage, this, player, transform.position.y);
            heldCar = null; CarsThrown++; Play(throwSound, 1);
        }

        // ---- Lamps ---------------------------------------------------------------------------------------------
        void UpdateLamps()
        {
            switch (State)
            {
                case TrafficLightBossState.Dormant: SetLamp(TrafficLampColor.Off); return;
                case TrafficLightBossState.Awakening:
                    if (stateTime % .16f < Time.deltaTime) SetLamp((TrafficLampColor)(1 + UnityEngine.Random.Range(0, 3)));
                    return;
                case TrafficLightBossState.FieldCast:
                    if (!fieldJudged) SetLamp(TrafficLampColor.Red);
                    return;
                case TrafficLightBossState.CarThrow:
                    SetLamp(stateTime < TrafficLightBossRig.CarReleaseTime ? TrafficLampColor.Yellow : stateTime < TrafficLightBossRig.CarReleaseTime + .6f ? TrafficLampColor.Green : TrafficLampColor.Red);
                    return;
            }
            // Red, green, yellow, repeat: the order the lamps cycle in the street.
            lampTimer -= Time.deltaTime;
            if (lampTimer > 0) return;
            TrafficLampColor next = Lamp == TrafficLampColor.Red ? TrafficLampColor.Green : Lamp == TrafficLampColor.Green ? TrafficLampColor.Yellow : TrafficLampColor.Red;
            SetLamp(next); lampTimer = next == TrafficLampColor.Red ? redTime : next == TrafficLampColor.Green ? greenTime : yellowTime;
        }

        public void SetLamp(TrafficLampColor color, bool force = false)
        {
            if (color == Lamp && !force) return;
            Lamp = color; if (!force) LampChanges++;
            // Unlit lenses show the dark baked glass; only the active lamp gets a glowing overlay.
            for (int i = 0; i < lampLenses.Length; i++)
            {
                if (!lampLenses[i]) continue;
                bool lit = (int)color == i + 1 && i < lensLit.Length && lensLit[i];
                lampLenses[i].enabled = lit;
                if (lit) lampLenses[i].sharedMaterial = lensLit[i];
            }
            if (glow)
            {
                glow.enabled = color != TrafficLampColor.Off;
                glow.color = color == TrafficLampColor.Red ? new Color(1, .1f, .05f) : color == TrafficLampColor.Yellow ? new Color(1, .68f, .1f) : new Color(.15f, 1, .3f);
            }
            if (!force && color != TrafficLampColor.Off && State != TrafficLightBossState.Awakening) Play(lampSound, .55f);
        }

        // ---- Shared -------------------------------------------------------------------------------------------
        void BeginNextAttack()
        {
            bool car = repeatThrow || pattern++ % 2 == 1;
            repeatThrow = false;
            SetState(car ? TrafficLightBossState.CarThrow : TrafficLightBossState.FieldCast);
        }

        void SetState(TrafficLightBossState state)
        {
            State = state; stateTime = 0; footstepTime = 0;
            if (warning) warning.enabled = false;
            if (state == TrafficLightBossState.Dormant)
            {
                foreach (var r in limbRenderers) if (r) r.enabled = false;
                SetBodyHeight(TrafficLightBossRig.DormantHeight);
                SetLamp(TrafficLampColor.Off, true);
            }
            else if (state == TrafficLightBossState.Awakening) { foreach (var r in limbRenderers) if (r) r.enabled = true; }
            else if (state == TrafficLightBossState.FieldCast) { fieldShown = false; fieldJudged = false; }
            else if (state == TrafficLightBossState.CarThrow) { carSpawned = false; carReleased = false; aimLocked = false; }
            if (state == TrafficLightBossState.FieldCast || state == TrafficLightBossState.CarThrow) lampTimer = 0;
            if (animator)
            {
                animator.speed = 1;
                if (state == TrafficLightBossState.Dormant || state == TrafficLightBossState.Awakening) animator.Play(state.ToString(), 0, 0);
                else animator.CrossFadeInFixedTime(state.ToString(), .15f, 0, 0);
            }
            StateChanged?.Invoke(state);
        }

        void FinishAttack()
        {
            bool enragedDouble = Enraged && State == TrafficLightBossState.CarThrow && !repeatThrow && UnityEngine.Random.value < .5f;
            repeatThrow = enragedDouble;
            nextAttack = Time.time + (enragedDouble ? .2f : Enraged ? attackCooldown * .7f : attackCooldown);
            if (field && field.Phase != RedFieldPhase.Hidden) field.Hide();
            SetState(TrafficLightBossState.Recovery);
        }

        public void GetArena(out Vector3 center, out Quaternion rotation, out Vector2 size)
        {
            if (room)
            {
                rotation = Quaternion.Euler(0, room.transform.eulerAngles.y, 0);
                center = room.transform.TransformPoint(room.localBounds.center); center.y = transform.position.y;
                size = new Vector2(room.localBounds.size.x, room.localBounds.size.z);
            }
            else { rotation = arenaRotation; center = arenaCenter; size = arenaSize; }
        }

        public bool ArenaContains(Vector3 world, float inset)
        {
            if (room) return room.Contains(world, inset);
            Vector3 local = Quaternion.Inverse(arenaRotation) * (world - arenaCenter);
            return Mathf.Abs(local.x) <= arenaSize.x * .5f - inset && Mathf.Abs(local.z) <= arenaSize.y * .5f - inset;
        }

        // A 12 m boss does not fit the normal 55 degree view, so the camera steeply looks down at the player-boss midpoint.
        void AssistCamera()
        {
            if (State == TrafficLightBossState.Dormant || State == TrafficLightBossState.Dead || !player) return;
            if (!followCamera) followCamera = FindFirstObjectByType<IsometricFollowCamera>();
            if (!followCamera) return;
            if (originalPitch < 0) originalPitch = followCamera.pitch;
            followCamera.distance = Mathf.MoveTowards(followCamera.distance, Mathf.Max(followCamera.distance, cameraDistance), 10 * Time.deltaTime);
            followCamera.pitch = Mathf.MoveTowards(followCamera.pitch, cameraPitch, 14 * Time.deltaTime);
            Vector3 toBoss = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(transform.position - player.transform.position, Vector3.up) * .5f, 9);
            // The farther the boss stands, the more its head needs the view lifted.
            float lift = Mathf.Lerp(cameraFocusLift * .3f, cameraFocusLift, Mathf.InverseLerp(3, 10, toBoss.magnitude * 2));
            followCamera.focusOffset = Vector3.MoveTowards(followCamera.focusOffset, toBoss + Vector3.up * lift, 6 * Time.deltaTime);
        }

        void SetBodyHeight(float height) { body.height = height; body.center = Vector3.up * (height * .5f); }
        void TurnTo(Vector3 direction, float degrees)
        { if (direction.sqrMagnitude > .001f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), degrees * Time.deltaTime); }
        void Move(Vector3 step)
        {
            if (room && !room.Contains(transform.position + step, 2.6f)) step = Vector3.zero;
            if (movement == null) movement = new CharacterObstacleSlide(body, CanOccupy);
            movement.Move(step + Vector3.down * (3 * Time.deltaTime));
        }

        bool CanOccupy(Vector3 position) => !room || room.Contains(position, 2.6f);

        Vector3 Steer(Vector3 direction)
        {
            for (int i = 0; i < 7; i++)
            {
                float angle = i == 0 ? 0 : (i % 2 == 0 ? -1 : 1) * ((i + 1) / 2) * 30;
                Vector3 candidate = Quaternion.Euler(0, angle, 0) * direction;
                bool blocked = false;
                foreach (var hit in Physics.SphereCastAll(transform.position + Vector3.up * 2.6f, body.radius * .9f, candidate, 2.5f, ~0, QueryTriggerInteraction.Ignore))
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
            Vector3 origin = transform.position + Vector3.up * 6;
            Vector3 to = player.transform.position + Vector3.up * .85f - origin;
            foreach (var hit in Physics.RaycastAll(origin, to.normalized, to.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == transform || hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<LiminalPlayerHealth>()) continue;
                return false;
            }
            return true;
        }

        void StepSound(float interval) { footstepTime -= Time.deltaTime; if (footstepTime <= 0) { Play(stepSound, .6f); footstepTime = interval; } }
        void Play(AudioClip clip, float volume) { if (voice && clip) voice.PlayOneShot(clip, volume); }

        void OnDefeated(TrainingEnemy _)
        {
            State = TrafficLightBossState.Dead; Health.CanBeTargeted = false;
            if (warning) warning.enabled = false;
            if (field) field.Hide();
            if (heldCar) Destroy(heldCar);
            foreach (var car in FindObjectsByType<TrafficLightCarProjectile>(FindObjectsSortMode.None)) Destroy(car.gameObject);
            SetLamp(TrafficLampColor.Off);
            if (glow) glow.enabled = false;
            if (body) body.enabled = false;
            if (voice) voice.Stop();
            ReleaseCamera();
        }

        void ReleaseCamera()
        {
            if (!followCamera) return;
            followCamera.focusOffset = Vector3.zero;
            if (originalPitch > 0) followCamera.pitch = originalPitch;
        }
        void OnDestroy() { if (Health) Health.Defeated -= OnDefeated; if (heldCar) Destroy(heldCar); ReleaseCamera(); }
        void OnDrawGizmosSelected() { Gizmos.color = new Color(1, .2f, .15f, .7f); Gizmos.DrawWireSphere(transform.position, detectionRadius); }
    }
}
