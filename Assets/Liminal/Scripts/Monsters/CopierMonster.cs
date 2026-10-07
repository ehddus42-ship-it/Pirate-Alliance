using UnityEngine;

namespace AcRoguelike.Liminal
{
    public enum CopierState { Hop, PaperFan, PaperSnipe, Recover }

    /// <summary>
    /// Photocopier monster. It moves like a slime, but heavy and clumsy, never springy: it sinks onto its base,
    /// heaves up into a low, short hop with almost no stretch, and lands with a crash that raises a ring of dust
    /// and shakes the floor (camera). It rattles for a moment, then gathers itself for the next hop.
    /// Attacks:
    /// - Paper fan: the lid rattles and an attack glint flashes before it machine-guns sheets across the fan.
    /// - Paper snipe: aimed single sheets, one at a time, each with a short aim line and a recoil kick.
    /// </summary>
    public sealed class CopierMonster : LiminalPropMonster
    {
        [Header("Movement")]
        public float hopDistance = 1.35f;
        public float hopHeight = .38f;
        public float preferredDistance = 6f;
        [Header("Paper fan")]
        public int fanSheets = 15;
        public float fanHalfAngle = 42f;
        public float fanRange = 9f;
        public float fanWindup = .75f, fanDuration = .55f;
        public int fanDamage = 9;
        [Header("Paper snipe")]
        public int snipeSheets = 5;
        public float snipeInterval = .42f;
        public float snipeRange = 12f;
        public int snipeDamage = 11;
        public float attackCooldown = 2.2f;

        public CopierState State { get; private set; } = CopierState.Hop;
        public int SheetsFired { get; private set; }
        public int Landings { get; private set; }

        float stateTime, hopTime, nextAttack, rattle, kick, recoil;
        Vector3 rattleApplied;
        Quaternion rattleTurn = Quaternion.identity;
        int hopPhase, fired, pattern;
        float sweepSign = 1;
        Vector3 hopDirection, aimForward;
        const float Crouch = .24f, Air = .34f, Settle = .3f;

        protected override int MaxHealth(int s) => 70 + s * 16;
        protected override float HitTilt => 13;
        protected override float Weight => State == CopierState.PaperFan ? .6f : 1f;

        protected override void OnSetup()
        {
            displayName = "복사기";
            nextAttack = Time.time + 1.4f + Random.value * .8f;
            hopTime = Random.value * .4f;
        }

        protected override void Think(float dt, Vector3 to, float distance)
        {
            stateTime += dt;
            // Last frame's shake is removed before the states pose the body again, so it never accumulates.
            poseOffset -= rattleApplied;
            rattleApplied = Vector3.zero;
            poseRotation *= Quaternion.Inverse(rattleTurn);
            rattleTurn = Quaternion.identity;
            rattle = Mathf.MoveTowards(rattle, 0, dt * 2.5f);
            kick = Mathf.MoveTowards(kick, 0, dt * 6f);
            // Firing pushes the whole copier back for real: it stays where the recoil left it.
            if (recoil > 0)
            {
                MoveBody(-transform.forward * recoil * dt);
                recoil = Mathf.MoveTowards(recoil, 0, dt * 7f);
            }
            bool staggered = Time.time < staggerUntil;
            switch (State)
            {
                case CopierState.Hop:
                    IsWindingUp = false;
                    if (!staggered && Time.time >= nextAttack && hopPhase == 0 && distance < 13 && CanSeePlayer(.9f))
                    {
                        aimForward = to.normalized;
                        Enter(pattern++ % 2 == 0 ? CopierState.PaperFan : CopierState.PaperSnipe);
                        break;
                    }
                    Hop(dt, to, distance, staggered);
                    break;
                case CopierState.PaperFan:
                    Face(aimForward, 200);
                    if (stateTime < fanWindup)
                    {
                        // The cabinet shudders before the projectile burst; only its attack glint marks the release.
                        IsWindingUp = true;
                        rattle = Mathf.Max(rattle, stateTime / fanWindup);
                        AttackCue(stateTime / fanWindup);
                        poseOffset = Vector3.down * .05f * (stateTime / fanWindup);
                        break;
                    }
                    IsWindingUp = false;
                    HideTelegraph();
                    // Sweep through three paper groups. Fixed gaps remain clear throughout the burst.
                    float t = (stateTime - fanWindup) / fanDuration;
                    int due = Mathf.Min(fanSheets, Mathf.FloorToInt(t * fanSheets) + 1);
                    while (fired < due)
                    {
                        int slot = sweepSign > 0 ? fired : fanSheets - 1 - fired;
                        float angle = EnemyFanPattern.Angle(slot, fanSheets, fanHalfAngle);
                        Fire(Quaternion.Euler(0, angle, 0) * aimForward, 10.5f, fanRange, fanDamage);
                        fired++;
                        kick = 1;
                        recoil = Mathf.Max(recoil, 1.3f);
                    }
                    if (t >= 1.15f) Enter(CopierState.Recover);
                    break;
                case CopierState.PaperSnipe:
                    // One sheet at a time: turn, a brief attack glint, recoil, repeat.
                    Face(to, 260);
                    float cycle = stateTime - .2f;
                    int shot = Mathf.FloorToInt(cycle / snipeInterval);
                    float within = cycle - shot * snipeInterval;
                    if (cycle >= 0 && shot < snipeSheets)
                    {
                        IsWindingUp = within < snipeInterval * .6f;
                        if (within < snipeInterval * .6f)
                            AttackCue(within / (snipeInterval * .6f));
                        if (fired <= shot && within >= snipeInterval * .6f)
                        {
                            HideTelegraph();
                            var motor = player.GetComponent<PlayerMotor>();
                            Vector3 lead = player.transform.position + (motor ? motor.PlanarVelocity * .25f : Vector3.zero) - transform.position;
                            Fire(Vector3.ProjectOnPlane(lead, Vector3.up).normalized, 13f, snipeRange, snipeDamage);
                            fired = shot + 1;
                            kick = 1.4f;
                            recoil = Mathf.Max(recoil, 2.4f);
                        }
                    }
                    if (shot >= snipeSheets) { IsWindingUp = false; HideTelegraph(); Enter(CopierState.Recover); }
                    break;
                case CopierState.Recover:
                    IsWindingUp = false;
                    poseOffset = Vector3.Lerp(poseOffset, Vector3.zero, dt * 8);
                    if (stateTime >= .55f) { nextAttack = Time.time + attackCooldown; Enter(CopierState.Hop); }
                    break;
            }
            ApplyRattle();
        }

        void Hop(float dt, Vector3 to, float distance, bool staggered)
        {
            hopTime += dt;
            if (hopPhase == 0)
            {
                // Gathering: sink and spread onto the base. Pick where to go when the crouch is complete.
                float c = Mathf.Clamp01(hopTime / Crouch);
                float sink = Mathf.Sin(c * Mathf.PI * .5f);
                poseScale = new Vector3(1 + .06f * sink, 1 - .1f * sink, 1 + .06f * sink);
                poseOffset = Vector3.zero;
                poseRotation = Quaternion.Euler(-4 * sink, 0, 0);
                Face(to, 120);
                if (hopTime >= Crouch && !staggered)
                {
                    Vector3 want = distance > preferredDistance + 1 ? to : distance < preferredDistance - 2 ? -to : Quaternion.Euler(0, Random.value < .5f ? 80 : -80, 0) * to;
                    hopDirection = Steer(want, hopDistance + .4f);
                    hopPhase = 1; hopTime = 0;
                }
            }
            else if (hopPhase == 1)
            {
                // A low, heavy heave: barely any stretch, the front tips up, then down as it comes in.
                float a = Mathf.Clamp01(hopTime / Air);
                poseOffset = Vector3.up * (hopHeight * 4 * a * (1 - a));
                poseScale = new Vector3(.98f, 1.04f - .04f * a, .98f);
                poseRotation = Quaternion.Euler(Mathf.Lerp(-7, 9, a), 0, Mathf.Sin(a * Mathf.PI) * 3 * sweepSign);
                if (hopDirection.sqrMagnitude > .01f) MoveBody(hopDirection * (hopDistance / Air) * dt);
                if (hopTime >= Air) { hopPhase = 2; hopTime = 0; Land(); }
            }
            else
            {
                // Crash and settle: a hard squash that springs back with a wobble, plus a rattle.
                float s = Mathf.Clamp01(hopTime / Settle);
                float squashNow = .2f * Mathf.Exp(-s * 5f) * Mathf.Cos(s * 14f);
                poseScale = new Vector3(1 + squashNow * .5f, 1 - squashNow, 1 + squashNow * .5f);
                poseOffset = Vector3.zero;
                poseRotation = Quaternion.Euler(9 * (1 - s) * (1 - s), 0, 0);
                if (hopTime >= Settle) { hopPhase = 0; hopTime = 0; }
            }
        }

        void Land()
        {
            Landings++;
            rattle = .6f;
            Thud(transform.position + transform.forward * .2f, 1f);
            // A couple of loose sheets puff out of the tray on landing.
            if (Random.value < .35f && player)
                PaperProjectile.Spawn(transform.position + Vector3.up * .9f, Quaternion.Euler(0, Random.Range(-60f, 60f), 0) * transform.forward, 2.5f, 1.6f, 0, transform, null);
        }

        void ApplyRattle()
        {
            if (rattle <= 0 && kick <= 0) return;
            float r = rattle * rattle;
            rattleTurn = Quaternion.Euler(Mathf.Sin(Time.time * 61) * 2.2f * r - kick * 5f, Mathf.Sin(Time.time * 47) * 1.5f * r, Mathf.Sin(Time.time * 53) * 2.4f * r);
            poseRotation *= rattleTurn;
            // A transient offset (removed next frame); the actual push back is the body recoil above.
            rattleApplied = new Vector3(Mathf.Sin(Time.time * 71) * .012f * r, 0, -kick * .05f);
            poseOffset += rattleApplied;
        }

        void Fire(Vector3 direction, float speed, float range, int damage)
        {
            // Sheets leave from the front output slot at waist height.
            Vector3 slot = transform.position + Vector3.up * .78f + transform.forward * .5f;
            PaperProjectile.Spawn(slot, direction, speed, range, damage, transform, player);
            SheetsFired++;
        }

        void Enter(CopierState state)
        {
            State = state;
            stateTime = 0;
            fired = 0;
            if (state == CopierState.Hop) { hopPhase = 0; hopTime = 0; }
            if (state != CopierState.PaperFan && state != CopierState.PaperSnipe) HideTelegraph();
            if (state == CopierState.PaperFan || state == CopierState.PaperSnipe)
            {
                sweepSign = Random.value < .5f ? -1 : 1;
                poseScale = Vector3.one; poseRotation = Quaternion.identity;
            }
        }

        protected override void OnHit(Vector3 direction, float impact)
        {
            rattle = Mathf.Max(rattle, .5f);
            // Loose sheets burst out when it is struck.
            for (int i = 0; i < 2; i++)
                PaperProjectile.Spawn(transform.position + Vector3.up * .95f, Quaternion.Euler(0, Random.Range(-70f, 70f), 0) * direction, 3f, 1.8f, 0, transform, null);
        }

        protected override void OnDeathStart(Vector3 direction)
        {
            for (int i = 0; i < 9; i++)
                PaperProjectile.Spawn(transform.position + Vector3.up * 1f, Quaternion.Euler(0, Random.Range(-180f, 180f), 0) * Vector3.forward, Random.Range(2f, 4.5f), Random.Range(1.5f, 3f), 0, transform, null);
        }

        protected override float LyingHalfDepth => .45f;

        /// <remarks>The concept-map prop prefab turns the copier's front to -Z; the monster turns it back to face forward.</remarks>
        public static CopierMonster Create(Vector3 position, Quaternion rotation, Transform parent)
        {
            var library = LiminalMonsterLibrary.Instance;
            return LiminalMonsterKit.Build<CopierMonster>("Copier Monster", library ? library.photocopier : null, position, rotation, parent,
                .52f, 1.25f, new Vector3(.92f, 1.2f, .9f), out _, modelYaw: 180);
        }
    }
}
