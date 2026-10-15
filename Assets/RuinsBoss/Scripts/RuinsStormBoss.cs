using System.Collections.Generic;
using AcRoguelike.Liminal;
using UnityEngine;

namespace AcRoguelike.RuinsBoss
{
    public enum RuinsBossState { Idle, Windup, Attack, Recovery, Awakening, Dead }
    public enum RuinsBossVisualKind { StormSovereign, IronRam, SiegeWalker, MissileTurret }

    /// <summary>Stationary conductor. Every pattern keeps a committed escape lane and a recovery window.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(CharacterController))]
    public sealed class RuinsStormBoss : LiminalPropMonster
    {
        public RuinsBossRig visualRig;
        public RuinsWarMachine leftMachine, rightMachine;
        public Transform turret, muzzle;
        public int maxHealth = 2000, bulletDamage = 11, artilleryDamage = 20;
        public float warningDuration = 1.35f, recoveryDuration = 1.65f;
        public float artilleryInterval = 10.5f, activationDuration = 1.6f;
        public float safeHalfAngle = 18f;
        public RuinsBossState State { get; private set; }
        public float StateTime => elapsed;
        public int Pattern { get; private set; }
        public int PatternsStarted { get; private set; }
        public int PatternsCompleted { get; private set; }
        public int ElectricShots { get; private set; }
        public int ArtilleryShots { get; private set; }
        public int MachinesActivated { get; private set; }
        public Vector3 LockedSafeDirection => safeDirection;
        public bool EncounterCancelled { get; private set; }
        public int LiveProjectiles { get { int count = 0; foreach (var p in projectiles) if (p && !p.Finished) count++; return count; } }
        protected override int MaxHealth(int stageIndex) => maxHealth;
        protected override float Weight => .18f;
        protected override float HitTilt => 5f;
        protected override float KnockbackSpeed => 0;
        protected override bool CanMove => false;
        protected override float LyingHalfDepth => .7f;

        readonly List<RuinsBossProjectile> projectiles = new List<RuinsBossProjectile>();
        readonly Transform[] launchPorts = new Transform[2];
        LiminalPlayerHealth subscribedPlayer;
        RuinsWarMachine waking;
        RuinsBossRig turretRig;
        Vector3 safeDirection = Vector3.back;
        readonly Vector3[] artilleryPoints = new Vector3[3];
        float elapsed, encounterTime, nextPattern, artilleryClock, artilleryVolleyTime;
        int nextPatternIndex, wave, artilleryVolley;
        bool initialized, activationCommitted;

        public static RuinsStormBoss Create(Vector3 position, Quaternion rotation, Transform parent)
        {
            var prefab = Resources.Load<GameObject>("RuinsBoss/StormSovereign");
            if (!prefab) return null;
            return Instantiate(prefab, position, rotation, parent).GetComponent<RuinsStormBoss>();
        }

        public void ConfigureEncounter(RuinsWarMachine left, RuinsWarMachine right, Transform rearTurret)
        {
            leftMachine = left; rightMachine = right; turret = rearTurret;
            if (leftMachine) leftMachine.InitializeDormant();
            if (rightMachine) rightMachine.InitializeDormant();
        }

        protected override void OnSetup()
        {
            initialized = true; EncounterCancelled = false;
            displayName = "폭풍의 군주";
            Health.CanBeTargeted = true;
            visualRig = visualRig ? visualRig : GetComponentInChildren<RuinsBossRig>(true);
            if (visualRig) { visualRig.kind = RuinsBossVisualKind.StormSovereign; visualRig.Initialize(); visualRig.SetDormant(false); }
            if (subscribedPlayer) subscribedPlayer.Died -= CancelEncounter;
            subscribedPlayer = player;
            if (subscribedPlayer) subscribedPlayer.Died += CancelEncounter;
            if (turret)
            {
                turretRig = turret.GetComponentInChildren<RuinsBossRig>();
                foreach (var part in turret.GetComponentsInChildren<Transform>(true))
                {
                    if (part.name == "LaunchPortA") launchPorts[0] = part;
                    else if (part.name == "LaunchPortB") launchPorts[1] = part;
                }
            }
            encounterTime = 0; nextPattern = 1.5f; artilleryClock = Mathf.Max(7, artilleryInterval * .75f);
            artilleryVolley = 3; nextPatternIndex = 0;
            Enter(RuinsBossState.Idle);
        }

        protected override void Think(float dt, Vector3 toPlayer, float distance)
        {
            if (!initialized || EncounterCancelled) return;
            encounterTime += dt; elapsed += dt;
            projectiles.RemoveAll(p => !p || p.Finished);
            UpdateArtillery(dt);
            switch (State)
            {
                case RuinsBossState.Idle:
                    Face(toPlayer, 65);
                    if (TryAwakening()) break;
                    if (encounterTime >= nextPattern) StartPattern(nextPatternIndex++ % 3);
                    break;
                case RuinsBossState.Windup:
                    DrawWarning(Mathf.Clamp01(elapsed / Mathf.Max(.1f, warningDuration)));
                    if (elapsed >= warningDuration) { HideWarnings(); wave = 0; Enter(RuinsBossState.Attack); }
                    break;
                case RuinsBossState.Attack:
                    UpdatePattern();
                    break;
                case RuinsBossState.Recovery:
                    if (elapsed >= recoveryDuration) { nextPattern = encounterTime + .4f; Enter(RuinsBossState.Idle); }
                    break;
                case RuinsBossState.Awakening:
                    if (!activationCommitted && elapsed >= activationDuration * .78f)
                    {
                        activationCommitted = true;
                        if (waking && waking.Activate()) MachinesActivated++;
                    }
                    if (elapsed >= activationDuration) { waking = null; nextPattern = encounterTime + .8f; Enter(RuinsBossState.Idle); }
                    break;
            }
            if (visualRig) visualRig.SetState(State, elapsed);
        }

        bool TryAwakening()
        {
            float hp = Health.Health / (float)Mathf.Max(1, Health.maxHealth);
            RuinsWarMachine candidate = null;
            if (leftMachine && leftMachine.IsDormant && (PatternsCompleted >= 1 || hp <= .8f || encounterTime >= 12)) candidate = leftMachine;
            else if ((!leftMachine || !leftMachine.IsDormant) && rightMachine && rightMachine.IsDormant &&
                (hp <= .5f || encounterTime >= 32)) candidate = rightMachine;
            if (!candidate) return false;
            waking = candidate; activationCommitted = false;
            HideWarnings(); Enter(RuinsBossState.Awakening);
            if (visualRig) visualRig.BeginActivation(candidate.transform, activationDuration);
            if (candidate.visualRig) candidate.visualRig.BeginActivation(transform, activationDuration);
            return true;
        }

        /// <summary>0: rings, 1: twin spiral, 2: alternating fans. Clear lanes are locked at windup start.</summary>
        public bool StartPattern(int requestedPattern)
        {
            if (!initialized || EncounterCancelled || !Health || !Health.IsAlive || !player || !player.IsAlive ||
                State != RuinsBossState.Idle || requestedPattern < 0 || requestedPattern > 2) return false;
            Pattern = requestedPattern;
            safeDirection = Vector3.ProjectOnPlane(player.transform.position - transform.position, Vector3.up).normalized;
            if (safeDirection.sqrMagnitude < .01f) safeDirection = transform.forward;
            Face(safeDirection, 10000);
            PatternsStarted++; wave = 0;
            Enter(RuinsBossState.Windup); DrawWarning(0);
            if (visualRig) visualRig.SetState(State, 0);
            return true;
        }

        Vector3 ElectricOrigin => muzzle ? new Vector3(muzzle.position.x, transform.position.y + 1.05f, muzzle.position.z) : transform.position + Vector3.up * 1.05f;
        bool SafeDirection(Vector3 direction) => Vector3.Angle(safeDirection, direction) <= Mathf.Clamp(safeHalfAngle, 14, 35);

        void DrawWarning(float progress)
        {
            AttackCue(progress);
            IsWindingUp = true;
        }

        void UpdatePattern()
        {
            if (Pattern == 0)
            {
                while (wave < 3 && elapsed >= wave * .72f)
                {
                    float stagger = wave % 2 == 0 ? 0 : 7;
                    for (int i = 0; i < 18; i++) FireElectric(Quaternion.Euler(0, i * 20 + stagger, 0) * safeDirection, 6.1f + wave * .45f);
                    wave++;
                }
                if (elapsed >= 2.15f) FinishPattern();
            }
            else if (Pattern == 1)
            {
                while (wave < 16 && elapsed >= wave * .18f)
                {
                    float angle = 50 + wave * 13;
                    FireElectric(Quaternion.Euler(0, angle, 0) * safeDirection, 6.8f);
                    FireElectric(Quaternion.Euler(0, angle + 180, 0) * safeDirection, 6.8f);
                    wave++;
                }
                if (elapsed >= 3.2f) FinishPattern();
            }
            else
            {
                while (wave < 4 && elapsed >= wave * .43f)
                {
                    for (int i = -5; i <= 5; i++)
                        FireElectric(Quaternion.Euler(0, i * 13 + (wave % 2 == 0 ? -4 : 4), 0) * safeDirection, 7.1f);
                    wave++;
                }
                if (elapsed >= 1.95f) FinishPattern();
            }
        }

        void FireElectric(Vector3 direction, float speed)
        {
            if (SafeDirection(direction)) return;
            // Filter the final direction so later rings and spiral/fan offsets cannot fill these lanes.
            if (EnemyFanPattern.IsRadialGap(Vector3.SignedAngle(safeDirection, direction, Vector3.up))) return;
            var shot = RuinsBossProjectile.FireElectric(ElectricOrigin, direction, Health, player, room, bulletDamage, speed);
            if (shot) { projectiles.Add(shot); ElectricShots++; }
        }

        void FinishPattern() { PatternsCompleted++; Enter(RuinsBossState.Recovery); }

        void UpdateArtillery(float dt)
        {
            if (!turret || !player || !player.IsAlive) return;
            artilleryClock -= dt;
            if (artilleryVolley >= 3 && artilleryClock <= .35f)
                AttackAnticipation.Show(turret, Mathf.Clamp01(1 - artilleryClock / .35f));
            if (artilleryVolley >= 3 && artilleryClock <= 0)
            {
                Vector3 center = player.transform.position;
                Vector3 side = Vector3.Cross(Vector3.up, safeDirection).normalized;
                // Three fixed impact points leave large gaps. Nothing tracks after the volley is committed.
                artilleryPoints[0] = ClampPoint(center);
                artilleryPoints[1] = ClampPoint(center + side * 5.8f + safeDirection * 1.5f);
                artilleryPoints[2] = ClampPoint(center - side * 5.8f + safeDirection * 1.5f);
                artilleryVolley = 0; artilleryVolleyTime = 0;
                artilleryClock = Mathf.Max(6, artilleryInterval);
            }
            if (artilleryVolley >= 3) return;
            artilleryVolleyTime += dt;
            while (artilleryVolley < 3 && artilleryVolleyTime >= artilleryVolley * .28f)
            {
                var port = launchPorts[artilleryVolley % launchPorts.Length];
                Vector3 origin = port ? port.position : turret.position + Vector3.up * 2.8f;
                var shot = RuinsBossProjectile.LaunchArtillery(origin, artilleryPoints[artilleryVolley], Health, player, room, artilleryDamage);
                if (shot) { projectiles.Add(shot); ArtilleryShots++; }
                if (turretRig) turretRig.PulseWeapon();
                HitFeedback.Sparks(origin, port ? port.forward : Vector3.up, 4, .3f);
                artilleryVolley++;
            }
        }

        Vector3 ClampPoint(Vector3 point)
        {
            if (!room) { point.y = transform.position.y; return point; }
            Vector3 local = room.transform.InverseTransformPoint(point);
            local.x = Mathf.Clamp(local.x, room.localBounds.min.x + 2.4f, room.localBounds.max.x - 2.4f);
            local.z = Mathf.Clamp(local.z, room.localBounds.min.z + 2.4f, room.localBounds.max.z - 2.4f);
            local.y = .03f;
            return room.transform.TransformPoint(local);
        }

        void Enter(RuinsBossState state)
        {
            State = state; elapsed = 0; IsWindingUp = state == RuinsBossState.Windup || state == RuinsBossState.Awakening;
        }
        void HideWarnings()
        {
            if (telegraph) telegraph.Hide();
            AttackAnticipation.Hide(transform);
            IsWindingUp = false;
        }

        public void CancelEncounter()
        {
            EncounterCancelled = true;
            HideWarnings();
            AttackAnticipation.Hide(turret);
            foreach (var projectile in projectiles) if (projectile) projectile.Cancel();
            projectiles.Clear(); artilleryVolley = 3;
            if (leftMachine) leftMachine.CancelEncounter();
            if (rightMachine) rightMachine.CancelEncounter();
            if (visualRig) visualRig.CancelEffects();
            if (turretRig) { turretRig.CancelEffects(); turretRig.SetDormant(true); }
        }
        protected override void OnHit(Vector3 direction, float impact) { if (visualRig) visualRig.PlayHit(); }
        protected override void OnDeathStart(Vector3 direction)
        {
            CancelEncounter(); Health.CanBeTargeted = false; Enter(RuinsBossState.Dead);
            if (visualRig) visualRig.PlayDeath();
            HitFeedback.Sparks(transform.position + Vector3.up * 2, direction, 18, 1.1f);
        }
        void OnDisable() { if (initialized) CancelEncounter(); }
        protected override void OnDestroy()
        {
            if (subscribedPlayer) subscribedPlayer.Died -= CancelEncounter;
            CancelEncounter(); base.OnDestroy();
        }
    }
}
