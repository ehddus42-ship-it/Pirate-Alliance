using AcRoguelike.Liminal;
using UnityEngine;

namespace AcRoguelike.Forest
{
    public enum ForestFrogState { Rest, Crouch, Leap, Land, TongueWindup, TongueExtend, TonguePull, TongueRetract, Recover }

    /// <summary>A hopping forest frog. Its tongue has a fixed warning, swept hit test and a short, collision-safe tether.</summary>
    public sealed class ForestFrog : LiminalPropMonster
    {
        [Header("Hop")]
        public float hopDistance = 2.3f;
        public float hopHeight = .8f;
        public float hopDuration = .52f;
        [Header("Tongue")]
        public float tongueRange = 7.2f;
        public float tongueWindup = .8f;
        public float tongueSpeed = 24;
        public float pullSpeed = 11;
        public float maximumPullSeconds = .85f;
        public float attackCooldown = 3.1f;
        public int tongueDamage = 12;

        public ForestFrogState State { get; private set; }
        public float StateTime => stateTime;
        public int Hops { get; private set; }
        public int TongueShots { get; private set; }
        public int Grabs { get; private set; }
        public int Releases { get; private set; }
        public bool IsPulling => heldMotor && State == ForestFrogState.TonguePull;
        public bool TongueVisible => tongue && tongue.enabled;
        public bool BodyAnimationReady => rig != null && rig.IsReady;

        Bounds box;
        ForestSoftBodyRig rig;
        MeshRenderer tongue;
        Mesh tongueMesh;
        Transform tongueTip;
        Material tongueMaterial;
        readonly Vector3[] tonguePoints = new Vector3[18];
        readonly Vector3[] tongueVertices = new Vector3[18 * 9];
        readonly Vector3[] tongueNormals = new Vector3[18 * 9];
        float stateTime, nextAttack, nextHop, animationClock, hopLength, travelled;
        float crouch, extension, throat;
        Vector3 hopDirection, shotDirection, shotOrigin, tipPosition, retractFrom;
        PlayerMotor heldMotor;
        CharacterController heldController;
        ForestFrogTetherGuard tether;
        LiminalPlayerHealth subscribedPlayer;
        bool initialized;

        protected override int MaxHealth(int s) => 90 + s * 18;
        protected override float HitTilt => 7;
        protected override float Weight => IsPulling ? .25f : State == ForestFrogState.Leap ? .35f : .85f;
        protected override bool CanMove => State == ForestFrogState.Rest || State == ForestFrogState.Leap;
        protected override float LyingHalfDepth => Mathf.Max(.3f, box.extents.y);

        protected override void OnSetup()
        {
            CancelTongue();
            if (subscribedPlayer) subscribedPlayer.Died -= PlayerDied;
            subscribedPlayer = player;
            if (subscribedPlayer) subscribedPlayer.Died += PlayerDied;
            displayName = "이끼 개구리";
            box = pose && pose.Find("Model") ? LiminalMonsterKit.LocalBounds(pose.Find("Model"), pose)
                : new Bounds(Vector3.up * .7f, new Vector3(2.2f, 1.4f, 1.8f));
            ForestAttackCueAnchors.Prepare(transform);
            rig?.Dispose();
            rig = new ForestSoftBodyRig(pose ? pose.Find("Model") : null, pose, box);
            if (!tongue) BuildTongue();
            State = ForestFrogState.Rest;
            stateTime = 0;
            nextAttack = Time.time + 1.3f + Random.value * .7f;
            nextHop = Time.time + .4f;
            initialized = true;
        }

        void BuildTongue()
        {
            tongueMaterial = LiminalMonsterKit.Lit(new Color(.7f, .2f, .29f), .82f);
            owned.Add(tongueMaterial);
            var go = new GameObject("Tongue — wet tapered muscle");
            go.transform.SetParent(transform, false);
            tongue = go.AddComponent<MeshRenderer>();
            tongue.sharedMaterial = tongueMaterial;
            tongueMesh = new Mesh { name = "Frog tongue — 18 rings, 8 sides" };
            tongueMesh.MarkDynamic();
            owned.Add(tongueMesh);
            var uv = new Vector2[tongueVertices.Length];
            var triangles = new int[17 * 8 * 6];
            int index = 0;
            for (int ring = 0; ring < 18; ring++)
                for (int side = 0; side <= 8; side++)
                {
                    uv[ring * 9 + side] = new Vector2(side / 8f, ring / 17f);
                    if (ring == 17 || side == 8) continue;
                    int a = ring * 9 + side, b = a + 9;
                    triangles[index++] = a; triangles[index++] = a + 1; triangles[index++] = b;
                    triangles[index++] = a + 1; triangles[index++] = b + 1; triangles[index++] = b;
                }
            tongueMesh.vertices = tongueVertices; tongueMesh.uv = uv; tongueMesh.triangles = triangles;
            go.AddComponent<MeshFilter>().sharedMesh = tongueMesh;
            tongue.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tongue.receiveShadows = true;
            tongue.enabled = false;
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "Tongue adhesive tip";
            Destroy(tip.GetComponent<Collider>());
            tongueTip = tip.transform;
            tongueTip.SetParent(transform, false);
            tongueTip.localScale = new Vector3(.24f, .18f, .3f);
            tip.GetComponent<Renderer>().sharedMaterial = tongueMaterial;
            tip.SetActive(false);
        }

        protected override void Think(float dt, Vector3 to, float distance)
        {
            stateTime += dt;
            animationClock += dt;
            IsWindingUp = State == ForestFrogState.TongueWindup;
            switch (State)
            {
                case ForestFrogState.Rest:
                    PoseGround(dt);
                    Face(to, 190);
                    if (Time.time < staggerUntil) break;
                    if (distance <= tongueRange + .3f && Time.time >= nextAttack && CanSeePlayer())
                    {
                        shotDirection = to.sqrMagnitude > .01f ? to.normalized : transform.forward;
                        transform.rotation = Quaternion.LookRotation(shotDirection);
                        Enter(ForestFrogState.TongueWindup);
                    }
                    else if (Time.time >= nextHop)
                    {
                        Vector3 desired = distance < 2.4f ? -to.normalized : distance < 5.3f
                            ? Quaternion.Euler(0, Hops % 2 == 0 ? 72 : -72, 0) * to.normalized : to.normalized;
                        hopDirection = Steer(desired, 1.3f);
                        hopLength = Mathf.Min(hopDistance, Mathf.Max(.5f, distance - 3));
                        if (distance < 5.3f) hopLength = hopDistance * .72f;
                        if (hopDirection.sqrMagnitude < .01f) { nextHop = Time.time + .7f; break; }
                        Face(hopDirection, 720);
                        Enter(ForestFrogState.Crouch);
                    }
                    break;
                case ForestFrogState.Crouch:
                    crouch = Mathf.SmoothStep(0, 1, stateTime / .26f);
                    extension = 0;
                    poseOffset = Vector3.zero;
                    poseRotation = Quaternion.Euler(-8 * crouch, 0, 0);
                    Face(hopDirection, 540);
                    if (stateTime >= .26f)
                    {
                        Hops++;
                        HitFeedback.Dust(transform.position - hopDirection * .35f, .35f);
                        Enter(ForestFrogState.Leap);
                    }
                    break;
                case ForestFrogState.Leap:
                {
                    float t = Mathf.Clamp01(stateTime / Mathf.Max(.1f, hopDuration));
                    crouch = 0;
                    extension = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 1.15f));
                    poseOffset = Vector3.up * (Mathf.Sin(t * Mathf.PI) * hopHeight);
                    poseRotation = Quaternion.Euler(Mathf.Lerp(-15, 18, t), 0, 0);
                    float wanted = hopLength * Mathf.Min(dt, Mathf.Max(0, hopDuration - (stateTime - dt))) / Mathf.Max(.1f, hopDuration);
                    if (!Clear(hopDirection, wanted + .1f)) wanted = 0;
                    if (wanted > 0) MoveBody(hopDirection * wanted);
                    if (t >= 1) { Thud(transform.position, .35f); Enter(ForestFrogState.Land); }
                    break;
                }
                case ForestFrogState.Land:
                    poseOffset = Vector3.zero;
                    poseRotation = Quaternion.identity;
                    extension = 0;
                    crouch = Mathf.Sin(Mathf.Clamp01(stateTime / .28f) * Mathf.PI) * .9f;
                    if (stateTime >= .28f) { nextHop = Time.time + .85f; Enter(ForestFrogState.Rest); }
                    break;
                case ForestFrogState.TongueWindup:
                {
                    float t = Mathf.Clamp01(stateTime / Mathf.Max(.2f, tongueWindup));
                    // Direction is locked from the first warning frame: moving sideways is a valid response.
                    TelegraphLine(transform.position, shotDirection, tongueRange + box.extents.z, .42f, t);
                    poseOffset = Vector3.zero;
                    poseRotation = Quaternion.Euler(-5 * t, 0, 0);
                    crouch = .2f;
                    throat = t * (.85f + .15f * Mathf.Sin(stateTime * 30));
                    if (t >= 1)
                    {
                        HideTelegraph();
                        shotOrigin = Mouth;
                        Vector3 aimed = shotOrigin + shotDirection * tongueRange;
                        aimed.y = player.transform.position.y + .85f;
                        shotDirection = (aimed - shotOrigin).normalized;
                        tipPosition = shotOrigin;
                        travelled = 0;
                        TongueShots++;
                        Enter(ForestFrogState.TongueExtend);
                    }
                    break;
                }
                case ForestFrogState.TongueExtend:
                    throat = Mathf.MoveTowards(throat, 0, dt * 8);
                    poseRotation = Quaternion.Euler(4, 0, 0);
                    ExtendTongue(dt);
                    DrawTongue(tipPosition, .025f);
                    break;
                case ForestFrogState.TonguePull:
                    Pull(dt);
                    if (heldMotor) tipPosition = heldMotor.transform.position + Vector3.up * .85f;
                    DrawTongue(tipPosition, .012f);
                    break;
                case ForestFrogState.TongueRetract:
                {
                    float t = Mathf.Clamp01(stateTime / .27f);
                    tipPosition = Vector3.Lerp(retractFrom, Mouth, t * t * (3 - 2 * t));
                    DrawTongue(tipPosition, Mathf.Sin(t * Mathf.PI) * .12f);
                    if (t >= 1) { HideTongue(); Enter(ForestFrogState.Recover); }
                    break;
                }
                case ForestFrogState.Recover:
                    PoseGround(dt);
                    if (stateTime >= .65f)
                    {
                        nextAttack = Time.time + attackCooldown;
                        nextHop = Time.time + .3f;
                        Enter(ForestFrogState.Rest);
                    }
                    break;
            }
            rig?.Frog(animationClock, crouch, extension, throat);
        }

        void PoseGround(float dt)
        {
            crouch = Mathf.MoveTowards(crouch, 0, dt * 4);
            extension = Mathf.MoveTowards(extension, 0, dt * 5);
            throat = Mathf.MoveTowards(throat, 0, dt * 4);
            poseOffset = Vector3.Lerp(poseOffset, Vector3.zero, dt * 15);
            poseRotation = Quaternion.Slerp(poseRotation, Quaternion.identity, dt * 12);
        }

        Vector3 Mouth => transform.TransformPoint(new Vector3(box.center.x, box.min.y + box.size.y * .44f,
            box.max.z - box.size.z * .08f));

        void ExtendTongue(float dt)
        {
            float step = Mathf.Min(tongueSpeed * dt, tongueRange - travelled);
            float nearest = step + .001f;
            Collider contact = null;
            // A tongue may begin inside a very close player's capsule or a wall; casts alone miss that case.
            foreach (var overlap in Physics.OverlapSphere(tipPosition, .18f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (overlap.transform == transform || overlap.transform.IsChildOf(transform)) continue;
                bool isPlayer = overlap.GetComponentInParent<LiminalPlayerHealth>() == player;
                if (!contact || !isPlayer) { nearest = 0; contact = overlap; }
                if (!isPlayer) break;
            }
            foreach (var hit in Physics.SphereCastAll(tipPosition, .18f, shotDirection, step, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider || hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
                if (hit.distance < nearest) { nearest = hit.distance; contact = hit.collider; }
            }
            tipPosition += shotDirection * Mathf.Min(step, nearest);
            travelled += step;
            if (contact)
            {
                var hitPlayer = contact.GetComponentInParent<LiminalPlayerHealth>();
                if (hitPlayer == player && TryCatch()) return;
                BeginRetract();
            }
            else if (travelled >= tongueRange || stateTime > .6f)
                BeginRetract();
        }

        bool TryCatch()
        {
            var motor = player ? player.GetComponent<PlayerMotor>() : null;
            if (!motor || !motor.enabled || !player.IsAlive || motor.IsHeld || motor.IsLaunched) return false;
            if (player.IsEvading) { player.NotifyDodged(); return false; }
            var guard = player.GetComponent<ForestFrogTetherGuard>();
            if (!guard) guard = player.gameObject.AddComponent<ForestFrogTetherGuard>();
            if (!guard.Available || !player.TakeDamage(tongueDamage) || !player.IsAlive) return false;
            if (!guard.Acquire(this, motor)) return false;
            heldMotor = motor;
            heldController = motor.GetComponent<CharacterController>();
            tether = guard;
            motor.SetHeld(true);
            Grabs++;
            HitFeedback.Shake(.04f, .12f);
            Enter(ForestFrogState.TonguePull);
            return true;
        }

        void Pull(float dt)
        {
            if (!heldMotor || !heldMotor.IsHeld || !heldMotor.isActiveAndEnabled || !player || !player.IsAlive
                || stateTime >= Mathf.Clamp(maximumPullSeconds, .1f, 1.2f))
            { BeginRetract(); return; }
            // The input action remains enabled during SetHeld, so a fresh dash press can break the tether.
            if (heldMotor.DashAction != null && heldMotor.DashAction.WasPressedThisFrame())
            {
                player.GrantInvulnerability(.22f);
                heldMotor.ResetDashCooldown();
                BeginRetract();
                return;
            }
            Vector3 front = transform.position + transform.forward * (Mathf.Max(box.max.z, body ? body.radius : .65f) + .65f);
            front.y = heldMotor.transform.position.y;
            Vector3 delta = front - heldMotor.transform.position;
            delta.y = 0;
            if (delta.magnitude <= .15f) { BeginRetract(); return; }
            Vector3 step = delta.normalized * Mathf.Min(delta.magnitude, pullSpeed * dt);
            float permitted = SafePullDistance(step);
            if (permitted <= .005f) { BeginRetract(); return; }
            Vector3 next = heldMotor.transform.position + step.normalized * permitted;
            if (room && !room.Contains(next, .55f)) { BeginRetract(); return; }
            heldMotor.transform.position = next;
            if (permitted + .015f < step.magnitude || delta.magnitude <= permitted + .15f) BeginRetract();
        }

        float SafePullDistance(Vector3 step)
        {
            if (!heldController) return 0;
            Transform target = heldMotor.transform;
            Vector3 scale = target.lossyScale;
            float radius = heldController.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float height = Mathf.Max(radius * 2, heldController.height * Mathf.Abs(scale.y));
            Vector3 center = target.TransformPoint(heldController.center);
            Vector3 a = center + Vector3.up * (height * .5f - radius);
            Vector3 b = center - Vector3.up * (height * .5f - radius);
            float limit = step.magnitude;
            foreach (var overlap in Physics.OverlapCapsule(a, b, Mathf.Max(.05f, radius - .03f), ~0, QueryTriggerInteraction.Ignore))
            {
                if (overlap.transform == target || overlap.transform.IsChildOf(target)) continue;
                return 0;
            }
            foreach (var hit in Physics.CapsuleCastAll(a, b, Mathf.Max(.05f, radius - .025f), step.normalized,
                         limit + .07f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider || hit.transform == target || hit.transform.IsChildOf(target)) continue;
                // The floor is not a horizontal obstruction. Walls, other creatures and the frog all stop a pull.
                if (hit.normal.y > .65f) continue;
                limit = Mathf.Min(limit, Mathf.Max(0, hit.distance - .07f));
            }
            return limit;
        }

        void DrawTongue(Vector3 end, float slack)
        {
            if (!tongue) return;
            Vector3 origin = Mouth;
            Vector3 along = end - origin;
            Vector3 side = Vector3.Cross(Vector3.up, along.normalized);
            for (int i = 0; i < tonguePoints.Length; i++)
            {
                float t = i / (float)(tonguePoints.Length - 1);
                tonguePoints[i] = Vector3.Lerp(origin, end, t)
                    + side * (Mathf.Sin(t * Mathf.PI * 3 - stateTime * 20) * Mathf.Sin(t * Mathf.PI) * slack)
                    - Vector3.up * (Mathf.Sin(t * Mathf.PI) * slack * .8f);
            }
            for (int ring = 0; ring < tonguePoints.Length; ring++)
            {
                Vector3 axis = ring + 1 < tonguePoints.Length ? tonguePoints[ring + 1] - tonguePoints[ring]
                    : tonguePoints[ring] - tonguePoints[ring - 1];
                axis = axis.sqrMagnitude > .000001f ? axis.normalized : transform.forward;
                Vector3 right = Vector3.Cross(Vector3.up, axis).normalized;
                if (right.sqrMagnitude < .001f) right = transform.right;
                Vector3 up = Vector3.Cross(axis, right).normalized;
                float radius = Mathf.Lerp(.085f, .055f, ring / 17f);
                for (int sideIndex = 0; sideIndex <= 8; sideIndex++)
                {
                    float angle = sideIndex * Mathf.PI / 4;
                    Vector3 normal = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    int index = ring * 9 + sideIndex;
                    tongueVertices[index] = transform.InverseTransformPoint(tonguePoints[ring] + normal * radius);
                    tongueNormals[index] = transform.InverseTransformDirection(normal);
                }
            }
            tongueMesh.vertices = tongueVertices;
            tongueMesh.normals = tongueNormals;
            tongueMesh.RecalculateBounds();
            tongue.enabled = true;
            tongueTip.gameObject.SetActive(true);
            tongueTip.position = end;
            if (along.sqrMagnitude > .001f) tongueTip.rotation = Quaternion.LookRotation(along);
        }

        void BeginRetract()
        {
            retractFrom = heldMotor ? heldMotor.transform.position + Vector3.up * .85f : tipPosition;
            ReleasePlayer();
            Enter(ForestFrogState.TongueRetract);
        }

        void ReleasePlayer()
        {
            if (heldMotor)
            {
                if (heldMotor.IsHeld) heldMotor.SetHeld(false);
                Releases++;
            }
            heldMotor = null;
            heldController = null;
            if (tether) tether.Release(this);
            tether = null;
        }

        void HideTongue()
        {
            if (tongue) tongue.enabled = false;
            if (tongueTip) tongueTip.gameObject.SetActive(false);
        }

        void Enter(ForestFrogState state)
        {
            if (State == ForestFrogState.TonguePull && state != ForestFrogState.TonguePull) ReleasePlayer();
            State = state;
            stateTime = 0;
            IsWindingUp = state == ForestFrogState.TongueWindup;
        }

        /// <summary>Safe interruption entry point for death, phase changes, disable and the player-side tether guard.</summary>
        public void CancelTongue()
        {
            ReleasePlayer();
            HideTongue();
            if (telegraph) telegraph.Hide();
            AttackAnticipation.Hide(transform);
            IsWindingUp = false;
            if (initialized) { State = ForestFrogState.Recover; stateTime = 0; }
        }

        void PlayerDied() => CancelTongue();
        void OnDisable() => CancelTongue();
        protected override void OnHit(Vector3 direction, float impact)
        {
            if (State == ForestFrogState.TongueWindup && impact >= 1.25f) CancelTongue();
        }
        protected override void OnDeathStart(Vector3 direction)
        {
            CancelTongue();
            poseOffset = Vector3.zero;
            poseRotation = Quaternion.identity;
            if (pose) { pose.localPosition = Vector3.zero; pose.localRotation = Quaternion.identity; }
            rig?.Frog(animationClock, .2f, 0, 0);
        }
        protected override void OnDestroy()
        {
            CancelTongue();
            if (subscribedPlayer) subscribedPlayer.Died -= PlayerDied;
            rig?.Dispose();
            base.OnDestroy();
        }
    }

    /// <summary>One tether per player, plus two seconds of immunity to another frog's grab after release.</summary>
    public sealed class ForestFrogTetherGuard : MonoBehaviour
    {
        ForestFrog owner;
        PlayerMotor motor;
        float protectedUntil;
        public bool Available => !owner && Time.time >= protectedUntil;

        public bool Acquire(ForestFrog frog, PlayerMotor target)
        {
            if (!Available || !target || target.IsHeld || target.IsLaunched) return false;
            owner = frog;
            motor = target;
            return true;
        }
        public void Release(ForestFrog frog)
        {
            if (owner != frog) return;
            owner = null;
            motor = null;
            protectedUntil = Time.time + 2.2f;
        }
        void Update()
        {
            // Runs even when time is paused: leaving gameplay must never strand a disabled player controller.
            if (owner && (!owner.isActiveAndEnabled || !motor || !motor.isActiveAndEnabled
                || !owner.Health || !owner.Health.IsAlive || !motor.IsHeld)) owner.CancelTongue();
        }
        void OnDisable() { if (owner) owner.CancelTongue(); }
        void OnDestroy() { if (owner) owner.CancelTongue(); }
    }
}
