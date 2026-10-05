using AcRoguelike.Liminal;
using UnityEngine;

namespace AcRoguelike.Forest
{
    public enum ForestWormState { BurrowWarning, Emerge, Crawl, ChargeWarning, Charge, Recover, Dive }

    /// <summary>A moonlit earthworm: ground eruption followed by a fully committed, wriggling charge.</summary>
    public sealed class ForestWorm : LiminalPropMonster
    {
        [Header("Eruption")]
        public float eruptionWarning = 1.05f;
        public float eruptionRadius = 1.65f;
        public int eruptionDamage = 16;
        [Header("Charge")]
        public float chargeWarning = .8f;
        public float chargeDistance = 8.5f;
        public float chargeSpeed = 8.2f;
        public int chargeDamage = 18;
        public float crawlSpeed = 1.3f;

        public ForestWormState State { get; private set; } = ForestWormState.BurrowWarning;
        public float StateTime => stateTime;
        public int Eruptions { get; private set; }
        public int Charges { get; private set; }
        public int ChargeHits { get; private set; }
        public bool Underground => State == ForestWormState.BurrowWarning;
        public bool BodyAnimationReady => rig != null && rig.IsReady;

        ForestSoftBodyRig rig;
        Bounds box;
        Vector3 chargeDirection;
        float stateTime, clock, undergroundDepth, travelled, dustClock;
        bool hitThisCharge, eruptionHit;

        protected override int MaxHealth(int s) => 100 + s * 20;
        protected override float HitTilt => 5;
        protected override float Weight => State == ForestWormState.Charge || State == ForestWormState.Emerge ? .22f : .75f;
        protected override bool CanMove => State == ForestWormState.Crawl || State == ForestWormState.Charge;
        protected override float LyingHalfDepth => Mathf.Max(.25f, box.extents.x);

        protected override void OnSetup()
        {
            displayName = "달빛 지렁이";
            box = pose && pose.Find("Model") ? LiminalMonsterKit.LocalBounds(pose.Find("Model"), pose)
                : new Bounds(Vector3.up * .45f, new Vector3(.95f, .9f, 3));
            rig?.Dispose();
            rig = new ForestSoftBodyRig(pose ? pose.Find("Model") : null, pose, box);
            undergroundDepth = Mathf.Max(1.2f, box.max.y + .4f);
            stateTime = 0;
            State = ForestWormState.BurrowWarning;
            poseOffset = Vector3.down * undergroundDepth;
            poseScale = Vector3.one;
            poseRotation = Quaternion.identity;
            SetBuried(true);
        }

        protected override void Think(float dt, Vector3 to, float distance)
        {
            stateTime += dt;
            clock += dt * (State == ForestWormState.Charge ? 16f : 7f);
            IsWindingUp = State == ForestWormState.BurrowWarning || State == ForestWormState.ChargeWarning;
            float strength = .6f;
            switch (State)
            {
                case ForestWormState.BurrowWarning:
                    telegraph.Circle(transform.position, eruptionRadius, Mathf.Clamp01(stateTime / Mathf.Max(.3f, eruptionWarning)));
                    poseOffset = Vector3.down * undergroundDepth;
                    if (stateTime >= eruptionWarning)
                    {
                        Face(to, 10000);
                        HideTelegraph();
                        SetBuried(false);
                        ForestWormSoilBurst.Spawn(transform.position, eruptionRadius * .72f, transform.parent);
                        Eruptions++;
                        eruptionHit = false;
                        Enter(ForestWormState.Emerge);
                        Thud(transform.position, .9f);
                    }
                    break;
                case ForestWormState.Emerge:
                {
                    float t = Mathf.Clamp01(stateTime / .58f);
                    float rise = 1 - Mathf.Pow(1 - t, 3);
                    poseOffset = Vector3.up * (-undergroundDepth * (1 - rise) + Mathf.Sin(t * Mathf.PI) * .45f);
                    poseRotation = Quaternion.Euler(-18 * Mathf.Sin(t * Mathf.PI), 0, 0);
                    poseScale = new Vector3(1 - Mathf.Sin(t * Mathf.PI) * .12f, 1 + Mathf.Sin(t * Mathf.PI) * .3f, 1);
                    strength = .85f;
                    if (!eruptionHit && stateTime >= .17f)
                    {
                        eruptionHit = true;
                        if (distance <= eruptionRadius + PlayerRadius && CanSeePlayer(.65f)) player.TakeDamage(eruptionDamage);
                    }
                    if (t >= 1) { poseOffset = Vector3.zero; poseScale = Vector3.one; Enter(ForestWormState.Crawl); }
                    break;
                }
                case ForestWormState.Crawl:
                    poseRotation = Quaternion.Slerp(poseRotation, Quaternion.identity, dt * 9);
                    poseScale = Vector3.Lerp(poseScale, new Vector3(1, .86f, 1), dt * 6);
                    poseOffset = Vector3.zero;
                    if (Time.time >= staggerUntil)
                    {
                        Vector3 direction = Steer(to, 1.2f);
                        Face(direction, 180);
                        if (distance > 3.5f && direction.sqrMagnitude > .01f)
                            MoveBody(direction * (crawlSpeed * (.7f + .3f * Mathf.Sin(clock)) * dt));
                        if (stateTime >= 1.15f && distance < 14 && CanSeePlayer(.7f))
                        {
                            chargeDirection = to.sqrMagnitude > .01f ? to.normalized : transform.forward;
                            transform.rotation = Quaternion.LookRotation(chargeDirection);
                            Enter(ForestWormState.ChargeWarning);
                        }
                        // A blocked approach returns underground instead of idling behind scenery forever.
                        else if (stateTime >= 5f) Enter(ForestWormState.Dive);
                    }
                    break;
                case ForestWormState.ChargeWarning:
                {
                    float t = Mathf.Clamp01(stateTime / Mathf.Max(.3f, chargeWarning));
                    strength = .35f + t * .45f;
                    poseScale = new Vector3(1 + t * .12f, .86f - t * .16f, 1 - t * .12f);
                    poseOffset = Vector3.zero;
                    TelegraphLine(transform.position - chargeDirection * ChargeHalfLength, chargeDirection,
                        chargeDistance + ChargeHalfLength * 2, ChargeHalfWidth, t);
                    if (t >= 1)
                    {
                        HideTelegraph();
                        hitThisCharge = false;
                        travelled = dustClock = 0;
                        Charges++;
                        Enter(ForestWormState.Charge);
                    }
                    break;
                }
                case ForestWormState.Charge:
                {
                    strength = 1.1f;
                    poseScale = Vector3.Lerp(poseScale, new Vector3(1, .7f, 1.08f), dt * 14);
                    poseRotation = Quaternion.identity;
                    float wanted = Mathf.Min(chargeSpeed * dt, chargeDistance - travelled);
                    Vector3 before = transform.position;
                    if (wanted <= 0 || !Clear(chargeDirection, wanted + ChargeHalfLength + .12f)
                        || (room && !room.Contains(before + chargeDirection * wanted, 1.1f)))
                    { Thud(transform.position + chargeDirection * .4f, .45f); Enter(ForestWormState.Recover); break; }
                    float moved = MoveBody(chargeDirection * wanted);
                    travelled += moved;
                    // Sweep the body's full length so a long worm cannot pass through the player between frames.
                    Vector3 start = before - chargeDirection * ChargeHalfLength;
                    Vector3 end = transform.position + chargeDirection * ChargeHalfLength;
                    if (!hitThisCharge && DistanceToSegment(player.transform.position, start, end) <= ChargeHalfWidth + PlayerRadius)
                    {
                        hitThisCharge = true;
                        if (player.TakeDamage(chargeDamage)) ChargeHits++;
                    }
                    dustClock += dt;
                    if (dustClock >= .14f) { dustClock = 0; HitFeedback.Dust(before - chargeDirection * .6f, .28f); }
                    if (moved < wanted * .3f || travelled >= chargeDistance - .01f || stateTime >= 1.6f)
                        Enter(ForestWormState.Recover);
                    break;
                }
                case ForestWormState.Recover:
                    poseScale = Vector3.Lerp(poseScale, Vector3.one, dt * 9);
                    poseOffset = Vector3.zero;
                    strength = .35f;
                    if (stateTime >= 1.1f) Enter(ForestWormState.Dive);
                    break;
                case ForestWormState.Dive:
                {
                    float t = Mathf.Clamp01(stateTime / .65f);
                    poseOffset = Vector3.down * (t * t * undergroundDepth);
                    poseRotation = Quaternion.Euler(t * 18, 0, 0);
                    if (t >= .55f) SetBuried(true);
                    if (t >= 1)
                    {
                        HitFeedback.Dust(transform.position, .8f);
                        ChooseBurrowPosition();
                        poseScale = Vector3.one;
                        poseRotation = Quaternion.identity;
                        Enter(ForestWormState.BurrowWarning);
                    }
                    break;
                }
            }
            rig?.Worm(clock, strength, State == ForestWormState.ChargeWarning ? Mathf.Clamp01(stateTime / Mathf.Max(.3f, chargeWarning)) : 0);
        }

        float ChargeHalfWidth => Mathf.Max(body ? body.radius + .12f : .6f, box.extents.x + .16f);
        float ChargeHalfLength => box.extents.z * 1.08f;
        float PlayerRadius
        {
            get { var controller = player ? player.GetComponent<CharacterController>() : null; return controller ? controller.radius : .3f; }
        }

        void SetBuried(bool buried)
        {
            if (body) body.enabled = !buried;
            if (Health) { Health.CanBeTargeted = !buried; Health.IgnoreDamage = buried; }
        }

        void ChooseBurrowPosition()
        {
            if (!player) return;
            for (int i = 0; i < 10; i++)
            {
                float angle = (Eruptions * 113 + i * 137.5f) * Mathf.Deg2Rad;
                Vector3 candidate = player.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (2.7f + i * .16f);
                candidate.y = transform.position.y;
                if (room && !room.Contains(candidate, 1.4f)) continue;
                bool blocked = false;
                foreach (var c in Physics.OverlapCapsule(candidate + Vector3.up * .65f, candidate + Vector3.up * 1.15f,
                             .58f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
                    if (c.GetComponentInParent<LiminalPlayerHealth>()) continue;
                    blocked = true; break;
                }
                if (blocked) continue;
                transform.position = candidate;
                return;
            }
            // No safe nearby ground: rise again at the previous valid position, with the full warning.
        }

        static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            point.y = a.y = b.y = 0;
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0;
            return Vector3.Distance(point, a + ab * t);
        }

        void Enter(ForestWormState state)
        {
            State = state;
            stateTime = 0;
            IsWindingUp = state == ForestWormState.BurrowWarning || state == ForestWormState.ChargeWarning;
        }

        void OnDisable()
        {
            if (telegraph) telegraph.Hide();
            IsWindingUp = false;
        }
        protected override void OnDeathStart(Vector3 direction)
        {
            if (Health) { Health.IgnoreDamage = false; Health.CanBeTargeted = false; }
            poseOffset = Vector3.zero;
            poseRotation = Quaternion.identity;
            poseScale = Vector3.one;
            if (pose) { pose.localPosition = Vector3.zero; pose.localRotation = Quaternion.identity; pose.localScale = Vector3.one; }
        }
        protected override void OnDestroy()
        {
            rig?.Dispose();
            base.OnDestroy();
        }
    }
}
