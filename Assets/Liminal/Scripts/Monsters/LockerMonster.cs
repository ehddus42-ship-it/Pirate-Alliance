using UnityEngine;

namespace AcRoguelike.Liminal
{
    public enum LockerState { Walk, GrabWindup, GrabReach, GrabHold, GrabRetract, TumbleWindup, Tumble, Recover }

    /// <summary>
    /// Locker-bank monster.
    /// - Walk: it shuffles like a heavy cabinet being walked across the floor, pivoting on one front corner,
    ///   then the other.
    /// - Grab: the middle door rattles, bangs open, and tentacles lash out. A caught player is lifted, swung
    ///   aside and hurled away. A dash dodges the grab; a miss slaps the floor.
    /// - Tumble charge: it falls flat on its front, then keeps flipping end over end along the charge line,
    ///   slamming its broad back and front faces into the floor in turn (BOOM, BOOM, BOOM, BOOM). It lands
    ///   back upright. Every slam shakes the floor and hurts anyone under it.
    /// </summary>
    public sealed class LockerMonster : LiminalPropMonster
    {
        [Header("Walk")]
        public float walkSpeed = 1.15f;
        [Header("Grab")]
        public float grabRange = 5.2f;
        public int throwDamage = 18;
        public float throwSpeed = 11f;
        [Header("Tumble charge")]
        public int slamDamage = 16;
        public float attackCooldown = 2f;

        public LockerState State { get; private set; } = LockerState.Walk;
        public int Grabs { get; private set; }
        public int Throws { get; private set; }
        public int Slams { get; private set; }

        float stateTime, nextAttack, stepClock, doorOpen;
        int pattern;
        Bounds box;
        Transform door, interior;
        Tentacle[] tentacles;
        Vector3 grabTarget, throwDirection;
        bool holding, missed;
        PlayerMotor heldMotor;
        Material fleshMaterial, doorMaterial, darkMaterial, eyeMaterial;

        // Tumble: rigid pose of the body relative to the root (point' = rotation * point + offset).
        Quaternion tumbleRotation = Quaternion.identity;
        Vector3 tumbleOffset;
        Quaternion stepStartRotation;
        Vector3 stepStartOffset, stepPivot;
        float stepAngle, stepDuration, stepTime;
        int step;
        bool rising;
        // A slab tumbles end over end in quarter turns, each about the leading edge that is on the floor:
        // fall onto the front face, rise onto its end, fall onto the back face, rise onto its end, and so on.
        // Eight quarter turns (720°) bring it back upright. Falls (even steps) are the heavy slams.
        const int TumbleSteps = 8;
        const float FallTime = .3f, RiseTime = .2f;

        protected override int MaxHealth(int s) => 110 + s * 22;
        protected override float HitTilt => 9;
        protected override float Weight => State == LockerState.Tumble ? .1f : State == LockerState.GrabHold ? .4f : .8f;
        protected override bool CanMove => State != LockerState.Tumble;
        protected override float LyingHalfDepth => box.size.z * .5f;

        protected override void OnSetup()
        {
            displayName = "관물대";
            nextAttack = Time.time + 1.6f + Random.value;
            box = LiminalMonsterKit.LocalBounds(pose.Find("Model"), pose);
            BuildDoorAndTentacles();
        }

        void BuildDoorAndTentacles()
        {
            fleshMaterial = LiminalMonsterKit.Lit(new Color(.33f, .07f, .12f), .78f);
            doorMaterial = LiminalMonsterKit.Lit(new Color(.86f, .89f, .86f), .45f, .2f);
            darkMaterial = LiminalMonsterKit.Lit(new Color(.02f, .015f, .02f), 0);
            eyeMaterial = LiminalMonsterKit.Emissive(new Color(1f, .15f, .1f));
            owned.Add(fleshMaterial); owned.Add(doorMaterial); owned.Add(darkMaterial); owned.Add(eyeMaterial);
            float w = box.size.x / 3f, h = box.size.y * .82f, front = box.max.z;
            Vector3 center = new Vector3(box.center.x, box.min.y + box.size.y * .5f, front);
            // A black opening over the middle door, the real door panel on a hinge in front of it, and two eyes.
            interior = Quad("Opening", darkMaterial, center + Vector3.forward * .004f, new Vector3(w * .92f, h, 1));
            var hinge = new GameObject("DoorHinge").transform;
            hinge.SetParent(pose, false);
            hinge.localPosition = new Vector3(center.x - w * .46f, center.y, front + .014f);
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.name = "Door";
            panel.transform.SetParent(hinge, false);
            panel.transform.localScale = new Vector3(w * .92f, h, .022f);
            panel.transform.localPosition = new Vector3(w * .46f, 0, 0);
            panel.GetComponent<Renderer>().sharedMaterial = doorMaterial;
            door = hinge;
            for (int i = -1; i <= 1; i += 2)
            {
                var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(eye.GetComponent<Collider>());
                eye.name = "Eye";
                eye.transform.SetParent(interior, false);
                eye.transform.localPosition = new Vector3(i * .17f, .3f, -.02f);
                eye.transform.localScale = new Vector3(.12f, .05f, .02f) * 1.0f;
                eye.GetComponent<Renderer>().sharedMaterial = eyeMaterial;
            }
            interior.gameObject.SetActive(false);
            door.gameObject.SetActive(false);
            tentacles = new Tentacle[3];
            for (int i = 0; i < tentacles.Length; i++)
            {
                tentacles[i] = Tentacle.Create(transform, fleshMaterial, i * 2.1f);
                tentacles[i].rootRadius = i == 1 ? .12f : .09f;
            }
        }

        Transform Quad(string name, Material material, Vector3 localPosition, Vector3 scale)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.name = name;
            quad.transform.SetParent(pose, false);
            quad.transform.localPosition = localPosition;
            // Quads face -Z; turn it to face out of the front.
            quad.transform.localRotation = Quaternion.Euler(0, 180, 0);
            quad.transform.localScale = scale;
            quad.GetComponent<Renderer>().sharedMaterial = material;
            return quad.transform;
        }

        protected override void Think(float dt, Vector3 to, float distance)
        {
            stateTime += dt;
            bool staggered = Time.time < staggerUntil;
            switch (State)
            {
                case LockerState.Walk:
                    IsWindingUp = false;
                    if (!staggered && Time.time >= nextAttack && CanSeePlayer(1.2f))
                    {
                        if (distance < grabRange && pattern % 2 == 0) { pattern++; Enter(LockerState.GrabWindup); break; }
                        if (distance > 3f && distance < 13f) { pattern++; Enter(LockerState.TumbleWindup); break; }
                    }
                    Walk(dt, to, distance, staggered);
                    break;
                case LockerState.GrabWindup:
                    // The door rattles in its frame, harder and harder, while a reach line points at the player.
                    IsWindingUp = true;
                    Face(to, 160);
                    float w = Mathf.Clamp01(stateTime / .7f);
                    door.gameObject.SetActive(true);
                    door.localRotation = Quaternion.Euler(0, -Mathf.Abs(Mathf.Sin(stateTime * 38f)) * 7f * w, 0);
                    poseRotation = Quaternion.Euler(Mathf.Sin(stateTime * 45f) * 1.5f * w, 0, Mathf.Sin(stateTime * 39f) * 1.8f * w);
                    TelegraphLine(transform.position + transform.forward * .4f, (to.sqrMagnitude > .01f ? to.normalized : transform.forward),
                        Mathf.Min(grabRange + .6f, distance + .8f), .45f, w);
                    if (stateTime >= .7f)
                    {
                        grabTarget = player.transform.position + Vector3.up * .9f;
                        Vector3 reach = Vector3.ProjectOnPlane(grabTarget - transform.position, Vector3.up);
                        if (reach.magnitude > grabRange) grabTarget = transform.position + reach.normalized * grabRange + Vector3.up * .9f;
                        HideTelegraph();
                        interior.gameObject.SetActive(true);
                        Thud(transform.position + transform.forward * .6f, .4f);
                        Enter(LockerState.GrabReach);
                    }
                    break;
                case LockerState.GrabReach:
                    IsWindingUp = false;
                    doorOpen = Mathf.MoveTowards(doorOpen, 1, dt / .14f);
                    float e = Mathf.Clamp01(stateTime / .3f);
                    ShapeTentacles(grabTarget, e * e * (3 - 2 * e), 0);
                    if (e >= 1)
                    {
                        var motor = player.GetComponent<PlayerMotor>();
                        Vector3 offset = player.transform.position + Vector3.up * .9f - grabTarget;
                        bool caught = motor && !motor.IsDashing && !motor.IsLaunched && offset.magnitude < 1.05f;
                        if (caught)
                        {
                            heldMotor = motor;
                            motor.SetHeld(true);
                            holding = true;
                            Grabs++;
                            HitFeedback.Shake(.05f, .12f);
                            throwDirection = PickThrowDirection();
                            Enter(LockerState.GrabHold);
                        }
                        else
                        {
                            missed = true;
                            Thud(grabTarget, .7f);
                            Enter(LockerState.GrabRetract);
                        }
                    }
                    break;
                case LockerState.GrabHold:
                {
                    // Lift the player up in front of the locker, swing them out to the side, then hurl them.
                    float t = Mathf.Clamp01(stateTime / 1.05f);
                    Vector3 front = transform.position + transform.forward * 1.3f;
                    Vector3 lifted = front + Vector3.up * 2.4f;
                    Vector3 swing = transform.position + throwDirection * 1.8f + Vector3.up * 2.1f;
                    Vector3 hold = t < .45f ? Vector3.Lerp(grabTarget, lifted, Mathf.SmoothStep(0, 1, t / .45f))
                                            : Vector3.Lerp(lifted, swing, Mathf.SmoothStep(0, 1, (t - .45f) / .55f));
                    hold += new Vector3(Mathf.Sin(stateTime * 31f), Mathf.Sin(stateTime * 23f), 0) * .05f;
                    ShapeTentacles(hold, 1, 0);
                    if (heldMotor) heldMotor.transform.position = hold - Vector3.up * .9f;
                    if (t >= 1)
                    {
                        Release(throwDirection * throwSpeed + Vector3.up * 5.5f);
                        if (player) player.TakeDamage(throwDamage);
                        Throws++;
                        HitFeedback.Shake(.09f, .2f);
                        Enter(LockerState.GrabRetract);
                    }
                    break;
                }
                case LockerState.GrabRetract:
                {
                    float r = 1 - Mathf.Clamp01(stateTime / .45f);
                    Vector3 last = missed ? grabTarget : transform.position + throwDirection * 1.8f + Vector3.up * 2.1f;
                    ShapeTentacles(last, r, missed ? .4f : 0);
                    if (r <= 0)
                    {
                        doorOpen = Mathf.MoveTowards(doorOpen, 0, dt / .16f);
                        if (doorOpen <= 0)
                        {
                            interior.gameObject.SetActive(false);
                            door.gameObject.SetActive(false);
                            Thud(transform.position + transform.forward * .6f, .35f);
                            missed = false;
                            Enter(LockerState.Recover);
                        }
                    }
                    break;
                }
                case LockerState.TumbleWindup:
                {
                    // Rocks back on its heels, gathering itself, with a long telegraph along the charge line.
                    IsWindingUp = true;
                    Face(to, 140);
                    float k = Mathf.Clamp01(stateTime / .75f);
                    poseRotation = Quaternion.Euler(-9 * Mathf.SmoothStep(0, 1, k), 0, Mathf.Sin(stateTime * 30) * k);
                    poseOffset = new Vector3(0, 0, -.05f * k);
                    TelegraphLine(transform.position, transform.forward, (box.size.y + box.size.z) * 4, box.size.x * .5f + .2f, k);
                    if (stateTime >= .75f) { HideTelegraph(); BeginTumble(); Enter(LockerState.Tumble); }
                    break;
                }
                case LockerState.Tumble:
                    IsWindingUp = false;
                    UpdateTumble(dt);
                    break;
                case LockerState.Recover:
                    IsWindingUp = false;
                    poseRotation = Quaternion.Slerp(poseRotation, Quaternion.identity, dt * 8);
                    poseOffset = Vector3.Lerp(poseOffset, Vector3.zero, dt * 8);
                    if (stateTime >= .6f) { nextAttack = Time.time + attackCooldown; Enter(LockerState.Walk); }
                    break;
            }
            if (door && door.gameObject.activeSelf && State != LockerState.GrabWindup)
                door.localRotation = Quaternion.Euler(0, -105f * (1 - (1 - doorOpen) * (1 - doorOpen)), 0);
        }

        void Walk(float dt, Vector3 to, float distance, bool staggered)
        {
            // "Walking" a heavy cabinet: pivot on one front corner, then the other, lurching forward each time.
            stepClock += dt / .5f;
            float phase = stepClock % 1f;
            float side = Mathf.FloorToInt(stepClock) % 2 == 0 ? 1 : -1;
            float lurch = Mathf.Sin(phase * Mathf.PI);
            poseRotation = Quaternion.Euler(-2.5f * lurch, 11f * side * lurch, -4f * side * lurch);
            poseOffset = new Vector3(box.size.x * .5f * side * (1 - Mathf.Cos(11f * Mathf.Deg2Rad * lurch)), .02f * lurch, 0);
            Face(to, 70);
            if (!staggered && distance > 2.2f)
            {
                Vector3 direction = Steer(to);
                MoveBody(direction * walkSpeed * (.35f + 1.3f * lurch) * dt);
            }
            if (phase + dt / .5f >= 1f) Thud(transform.position + transform.right * side * box.size.x * .4f, .3f);
        }

        void ShapeTentacles(Vector3 target, float extension, float slump)
        {
            Vector3 root = pose.TransformPoint(new Vector3(box.center.x, box.min.y + box.size.y * .55f, box.max.z - .05f));
            for (int i = 0; i < tentacles.Length; i++)
            {
                // The outer two reach around the sides of the grip point, the middle one goes straight for it.
                Vector3 spread = transform.right * ((i - 1) * .35f) + Vector3.up * ((i == 1 ? .15f : -.1f) - slump);
                Vector3 start = root + transform.right * ((i - 1) * .12f) + Vector3.up * ((i - 1) * .18f);
                tentacles[i].Shape(start, transform.forward, target + spread, extension * (i == 1 ? 1 : .94f), Time.time + i);
            }
        }

        void HideTentacles() { if (tentacles != null) foreach (var t in tentacles) if (t) t.Hide(); }

        Vector3 PickThrowDirection()
        {
            // Hurl the player sideways (away from walls if possible), angled slightly away from the locker.
            Vector3 best = transform.right;
            float bestRoom = -1;
            foreach (float sign in new[] { 1f, -1f })
            {
                Vector3 d = (transform.right * sign + transform.forward * .35f).normalized;
                float roomAhead = 0;
                for (float r = 1; r <= 7; r += 1)
                    if (!room || room.Contains(transform.position + d * r, 1.2f)) roomAhead = r; else break;
                if (roomAhead > bestRoom) { bestRoom = roomAhead; best = d; }
            }
            return best;
        }

        void Release(Vector3 velocity)
        {
            if (!holding) return;
            holding = false;
            if (heldMotor)
            {
                heldMotor.Launch(velocity);
                var m = heldMotor;
                System.Action landed = null;
                landed = () => { m.Landed -= landed; HitFeedback.Dust(m.transform.position, 1.2f); HitFeedback.Shake(.07f, .14f); };
                m.Landed += landed;
            }
            heldMotor = null;
        }

        // ---- tumble charge ---------------------------------------------------------------------------------
        void BeginTumble()
        {
            tumbleRotation = poseRotation = Quaternion.identity;
            tumbleOffset = poseOffset = Vector3.zero;
            step = 0;
            rising = false;
            StartStep();
        }

        void StartStep()
        {
            stepStartRotation = tumbleRotation;
            stepStartOffset = tumbleOffset;
            stepTime = 0;
            if (rising)
            {
                stepAngle = 0; stepDuration = .35f;
                return;
            }
            stepAngle = 90;
            stepDuration = step % 2 == 0 ? FallTime : RiseTime;
            // Pivot: the leading edge resting on the floor (bottom corners furthest along the charge).
            float minY = float.MaxValue, maxZ = float.MinValue;
            var corners = Corners();
            foreach (var c in corners) minY = Mathf.Min(minY, c.y);
            foreach (var c in corners) if (c.y < minY + .03f) maxZ = Mathf.Max(maxZ, c.z);
            stepPivot = new Vector3(0, minY, maxZ);
            // Stop early if the next landing would leave the room or crash into something.
            Vector3 landing = Rotated(Quaternion.AngleAxis(stepAngle, Vector3.right), stepPivot, stepStartRotation, stepStartOffset, box.center);
            Vector3 world = transform.TransformPoint(new Vector3(landing.x, 0, landing.z));
            if ((room && !room.Contains(world, .8f)) || Blocked(world))
            {
                rising = true; stepAngle = 0; stepDuration = .35f;
            }
        }

        bool Blocked(Vector3 world)
        {
            var half = new Vector3(box.size.x * .45f, .4f, box.size.y * .45f);
            foreach (var c in Physics.OverlapBox(world + Vector3.up * .6f, half, transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
                if (c.GetComponentInParent<LiminalPlayerHealth>() || c.GetComponentInParent<LiminalPropMonster>()) continue;
                return true;
            }
            return false;
        }

        void UpdateTumble(float dt)
        {
            stepTime += dt;
            float t = Mathf.Clamp01(stepTime / stepDuration);
            if (rising)
            {
                // Blocked mid-charge: heave back upright where it lies.
                float s = Mathf.SmoothStep(0, 1, t);
                Vector3 centerNow = stepStartRotation * box.center + stepStartOffset;
                tumbleRotation = Quaternion.Slerp(stepStartRotation, Quaternion.identity, s);
                Vector3 target = new Vector3(centerNow.x, 0, centerNow.z) - new Vector3(box.center.x, 0, box.center.z);
                tumbleOffset = Vector3.Lerp(stepStartOffset, target, s);
            }
            else
            {
                // Falls accelerate into the floor; rises decelerate as it comes up onto its end.
                float angle = stepAngle * (step % 2 == 0 ? Mathf.Pow(t, 1.8f) : 1 - Mathf.Pow(1 - t, 1.6f));
                var q = Quaternion.AngleAxis(angle, Vector3.right);
                tumbleRotation = q * stepStartRotation;
                tumbleOffset = q * (stepStartOffset - stepPivot) + stepPivot;
            }
            poseRotation = tumbleRotation;
            poseOffset = tumbleOffset;
            if (t < 1) return;
            if (rising) { FinishTumble(); return; }
            Slam(step % 2 == 0, step == TumbleSteps - 1);
            step++;
            if (step >= TumbleSteps) FinishTumble();
            else StartStep();
        }

        void Slam(bool face, bool last)
        {
            if (!face && !last)
            {
                // Rising onto its end: just a knock, the slam comes when it falls.
                Thud(transform.TransformPoint(new Vector3(tumbleOffset.x, 0, tumbleOffset.z + box.center.z)), .45f);
                return;
            }
            Slams++;
            var corners = Corners();
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var c in corners) { minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x); minZ = Mathf.Min(minZ, c.z); maxZ = Mathf.Max(maxZ, c.z); }
            Vector3 centerLocal = new Vector3((minX + maxX) * .5f, 0, (minZ + maxZ) * .5f);
            Vector3 world = transform.TransformPoint(centerLocal);
            Thud(world, last ? 1.1f : 1.7f);
            HitFeedback.Sparks(world + Vector3.up * .1f, Vector3.up, 6, .9f);
            if (!player) return;
            Vector3 p = transform.InverseTransformPoint(player.transform.position);
            const float margin = .35f;
            if (p.x > minX - margin && p.x < maxX + margin && p.z > minZ - margin && p.z < maxZ + margin)
            {
                if (player.TakeDamage(slamDamage))
                {
                    var motor = player.GetComponent<PlayerMotor>();
                    Vector3 away = Vector3.ProjectOnPlane(player.transform.position - world, Vector3.up);
                    if (away.sqrMagnitude < .01f) away = transform.forward;
                    if (motor && !motor.IsHeld) motor.Launch(away.normalized * 6.5f + Vector3.up * 3.2f);
                }
            }
        }

        void FinishTumble()
        {
            // Move the root under the body so collision and the next attack start where the locker stands now.
            Vector3 local = new Vector3(tumbleOffset.x, 0, tumbleOffset.z);
            Vector3 world = transform.TransformPoint(local);
            bool was = body.enabled;
            body.enabled = false;
            transform.position = new Vector3(world.x, transform.position.y, world.z);
            body.enabled = was;
            tumbleRotation = Quaternion.identity;
            tumbleOffset = Vector3.zero;
            poseRotation = Quaternion.identity;
            poseOffset = Vector3.zero;
            Enter(LockerState.Recover);
        }

        Vector3[] Corners()
        {
            var result = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3((i & 1) == 0 ? box.min.x : box.max.x, (i & 2) == 0 ? box.min.y : box.max.y, (i & 4) == 0 ? box.min.z : box.max.z);
                result[i] = tumbleRotation * c + tumbleOffset;
            }
            return result;
        }

        static Vector3 Rotated(Quaternion q, Vector3 pivot, Quaternion rotation, Vector3 offset, Vector3 point)
            => q * (rotation * point + offset - pivot) + pivot;

        void Enter(LockerState state)
        {
            State = state;
            stateTime = 0;
            if (state == LockerState.Walk || state == LockerState.Recover) HideTentacles();
        }

        protected override void OnDeathStart(Vector3 direction)
        {
            Release(direction * 4f + Vector3.up * 3f);
            HideTentacles();
            if (door) door.gameObject.SetActive(false);
            if (interior) interior.gameObject.SetActive(false);
            poseRotation = Quaternion.identity;
            poseOffset = Vector3.zero;
            if (pose) { pose.localRotation = Quaternion.identity; pose.localPosition = Vector3.zero; }
        }

        protected override void OnDestroy()
        {
            Release(Vector3.up * 2);
            base.OnDestroy();
        }

        public static LockerMonster Create(Vector3 position, Quaternion rotation, Transform parent)
        {
            var library = LiminalMonsterLibrary.Instance;
            return LiminalMonsterKit.Build<LockerMonster>("Locker Monster", library ? library.lockers : null, position, rotation, parent,
                .62f, 2f, new Vector3(1.2f, 2f, .7f), out _);
        }
    }
}
