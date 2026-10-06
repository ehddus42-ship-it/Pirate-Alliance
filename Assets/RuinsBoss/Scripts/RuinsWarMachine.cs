using System.Collections.Generic;
using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.RuinsBoss
{
    public enum RuinsWarMachineKind { IronRam, SiegeWalker }

    /// <summary>Dormant side machine, awakened individually by its conductor. A charge never retargets after its warning.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(CharacterController))]
    public sealed class RuinsWarMachine : LiminalPropMonster
    {
        public RuinsWarMachineKind kind;
        public RuinsBossRig visualRig;
        public int maxHealth = 450, chargeDamage = 24, missileDamage = 15;
        public float chargeSpeed = 9.5f, chargeDistance = 9f, chargeWarning = 1.25f;
        public float missileWarning = 1.05f, recoveryDuration = 1.6f, walkSpeed = 1.4f;
        public RuinsBossState State { get; private set; }
        public float StateTime => elapsed;
        public bool IsDormant { get; private set; } = true;
        public bool EncounterCancelled { get; private set; }
        public int Attacks { get; private set; }
        public int Shots { get; private set; }
        public int DamageAttempts { get; private set; }
        public int ActivationCount { get; private set; }
        public int Pattern { get; private set; }
        public float Travelled { get; private set; }
        public Vector3 LockedDirection => aim;
        public float WarningDuration => Pattern == 0 ? chargeWarning : missileWarning;
        protected override int MaxHealth(int stageIndex) => maxHealth;
        protected override float Weight => IsDormant ? 0 : State == RuinsBossState.Attack ? .18f : .4f;
        protected override float HitTilt => 5f;
        protected override float KnockbackSpeed => IsDormant ? 0 : .6f;
        protected override bool CanMove => !IsDormant && !EncounterCancelled;
        protected override float LyingHalfDepth => 1.1f;

        readonly List<RuinsBossProjectile> projectiles = new List<RuinsBossProjectile>();
        readonly RaycastHit[] hits = new RaycastHit[128];
        LiminalPlayerHealth subscribedPlayer, controllerContact;
        Collider[] colliders;
        Vector3 aim = Vector3.forward;
        float elapsed, cooldown;
        int nextPattern, volley;
        bool initialized, connected, movingCharge;

        public static RuinsWarMachine Create(RuinsWarMachineKind kind, Vector3 position, Quaternion rotation, Transform parent)
        {
            var prefab = Resources.Load<GameObject>("RuinsBoss/" + kind);
            if (!prefab) return null;
            var machine = Instantiate(prefab, position, rotation, parent).GetComponent<RuinsWarMachine>();
            if (machine) machine.kind = kind;
            return machine;
        }

        protected override void OnSetup()
        {
            initialized = true; EncounterCancelled = false;
            displayName = kind == RuinsWarMachineKind.IronRam ? "철갑 파쇄기" : "공성 보행병기";
            colliders = GetComponentsInChildren<Collider>(true);
            if (body) body.minMoveDistance = 0;
            visualRig = visualRig ? visualRig : GetComponentInChildren<RuinsBossRig>(true);
            if (visualRig) { visualRig.kind = kind == RuinsWarMachineKind.IronRam ? RuinsBossVisualKind.IronRam : RuinsBossVisualKind.SiegeWalker; visualRig.Initialize(); }
            if (subscribedPlayer) subscribedPlayer.Died -= CancelEncounter;
            subscribedPlayer = player;
            if (subscribedPlayer) subscribedPlayer.Died += CancelEncounter;
            nextPattern = kind == RuinsWarMachineKind.IronRam ? 0 : 1;
            InitializeDormant();
        }

        public void InitializeDormant()
        {
            IsDormant = true;
            var health = Health ? Health : GetComponent<TrainingEnemy>();
            if (health) { health.CanBeTargeted = false; health.IgnoreDamage = true; }
            if (colliders == null) colliders = GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders) if (collider) collider.enabled = false;
            Enter(RuinsBossState.Idle);
            if (visualRig) visualRig.SetDormant(true);
        }

        public bool Activate()
        {
            if (!initialized || !IsDormant || EncounterCancelled || !Health || !Health.IsAlive || !player || !player.IsAlive) return false;
            IsDormant = false; ActivationCount++;
            Health.CanBeTargeted = true; Health.IgnoreDamage = false;
            // Imported decoration colliders are removed by the builder. Only the body is a live blocker.
            if (body) body.enabled = true;
            cooldown = 1.4f; Enter(RuinsBossState.Idle);
            if (visualRig) { visualRig.SetDormant(false); visualRig.PulseWeapon(); }
            HitFeedback.Sparks(transform.position + Vector3.up * 1.8f, Vector3.up, 10, .8f);
            return true;
        }

        protected override void Think(float dt, Vector3 toPlayer, float distance)
        {
            if (EncounterCancelled || IsDormant) return;
            elapsed += dt;
            projectiles.RemoveAll(p => !p || p.Finished);
            float speed01 = 0;
            switch (State)
            {
                case RuinsBossState.Idle:
                    cooldown -= dt;
                    Face(toPlayer, 75);
                    if (distance > 11.5f)
                    {
                        float moved = MoveBody(Steer(toPlayer, 2f) * (walkSpeed * dt));
                        speed01 = moved / Mathf.Max(.001f, walkSpeed * dt);
                    }
                    else MoveBody(Vector3.zero);
                    if (cooldown <= 0 && distance < 22 && CanSeePlayer()) StartAttack(nextPattern++ % 2);
                    break;
                case RuinsBossState.Windup:
                    MoveBody(Vector3.zero);
                    if (elapsed <= WarningDuration * .28f && toPlayer.sqrMagnitude > .001f) aim = toPlayer.normalized;
                    Face(aim, 300);
                    DrawWarning(Mathf.Clamp01(elapsed / Mathf.Max(.1f, WarningDuration)));
                    if (elapsed >= WarningDuration)
                    {
                        HideTelegraph(); connected = false; Travelled = 0; volley = 0; Attacks++;
                        Enter(RuinsBossState.Attack);
                    }
                    break;
                case RuinsBossState.Attack:
                    if (Pattern == 0) { Charge(dt); speed01 = 1; }
                    else
                    {
                        MoveBody(Vector3.zero);
                        while (volley < 2 && elapsed >= volley * .42f)
                        {
                            Vector3 direction = Quaternion.Euler(0, volley == 0 ? -7 : 7, 0) * aim;
                            var shot = RuinsBossProjectile.FireHoming(transform.position + Vector3.up * 1.1f, direction, Health, player, room, missileDamage, 6.2f);
                            if (shot) { projectiles.Add(shot); Shots++; }
                            volley++;
                            if (visualRig) visualRig.PulseWeapon();
                        }
                        if (elapsed >= .95f) Enter(RuinsBossState.Recovery);
                    }
                    break;
                case RuinsBossState.Recovery:
                    MoveBody(Vector3.zero);
                    if (elapsed >= recoveryDuration) { cooldown = 1.5f; Enter(RuinsBossState.Idle); }
                    break;
            }
            if (visualRig) visualRig.SetState(State, elapsed, speed01);
        }

        public bool StartAttack(int pattern = 0)
        {
            if (!initialized || IsDormant || EncounterCancelled || State != RuinsBossState.Idle || !Health || !Health.IsAlive ||
                !player || !player.IsAlive || pattern < 0 || pattern > 1) return false;
            Pattern = pattern;
            aim = Vector3.ProjectOnPlane(player.transform.position - transform.position, Vector3.up).normalized;
            if (aim.sqrMagnitude < .01f) aim = transform.forward;
            Travelled = 0; connected = false; volley = 0;
            Enter(RuinsBossState.Windup); DrawWarning(0);
            if (visualRig) visualRig.SetState(State, 0);
            return true;
        }

        void DrawWarning(float progress)
        {
            if (Pattern == 0) TelegraphLine(transform.position, aim, chargeDistance + (body ? body.radius : 1), (body ? body.radius : 1) + .15f, progress);
            else AttackCue(progress);
        }

        void Charge(float dt)
        {
            float wanted = Mathf.Min(Mathf.Max(0, chargeDistance - Travelled), chargeSpeed * dt);
            if (wanted <= .001f) { Enter(RuinsBossState.Recovery); return; }
            bool collision = Sweep(aim, wanted, out var impact, out var hitPlayer);
            float step = collision ? Mathf.Max(0, impact - .015f) : wanted;
            controllerContact = null; movingCharge = true;
            float moved = MoveBody(aim * step);
            movingCharge = false;
            Travelled += moved;
            if (!connected && ((controllerContact && (!collision || hitPlayer)) ||
                (hitPlayer && moved + .08f >= step)))
            {
                connected = true; DamageAttempts++; player.TakeDamage(chargeDamage);
                Thud(transform.position + aim, .7f);
            }
            if (collision || controllerContact || Travelled >= chargeDistance - .02f || moved < wanted * .2f || elapsed > chargeDistance / Mathf.Max(1, chargeSpeed) + .5f)
            {
                if (collision && !hitPlayer) Thud(transform.position + aim, .85f);
                Enter(RuinsBossState.Recovery);
            }
        }

        bool Sweep(Vector3 direction, float distance, out float impact, out bool hitPlayer)
        {
            impact = distance; hitPlayer = false;
            if (!body) return false;
            float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
            float radius = (body.radius + body.skinWidth) * scale;
            float half = Mathf.Max(radius, body.height * Mathf.Abs(transform.lossyScale.y) * .5f);
            Vector3 center = transform.TransformPoint(body.center), extent = Vector3.up * (half - radius);
            int count = gameObject.scene.GetPhysicsScene().CapsuleCast(center - extent, center + extent, radius,
                direction, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            bool found = count >= hits.Length;
            if (found) impact = 0;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (!hit.collider || hit.collider == body || hit.transform.IsChildOf(transform)) continue;
                var target = hit.collider.GetComponentInParent<LiminalPlayerHealth>();
                if (!target && Mathf.Abs(hit.normal.y) > .55f) continue;
                if (hit.distance > impact + .001f) continue;
                // A wall at the same distance wins, so no target behind a wall can be damaged.
                bool targetHit = target == player;
                if (found && Mathf.Abs(hit.distance - impact) <= .001f && !hitPlayer && targetHit) continue;
                found = true; impact = hit.distance; hitPlayer = targetHit;
            }
            return found;
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (movingCharge && !connected && hit.collider.GetComponentInParent<LiminalPlayerHealth>() == player) controllerContact = player;
        }
        void Enter(RuinsBossState next) { State = next; elapsed = 0; IsWindingUp = next == RuinsBossState.Windup; }
        public void CancelEncounter()
        {
            EncounterCancelled = true; IsWindingUp = false;
            if (telegraph) telegraph.Hide();
            AttackAnticipation.Hide(transform);
            foreach (var projectile in projectiles) if (projectile) projectile.Cancel();
            projectiles.Clear();
            if (Health) { Health.CanBeTargeted = false; Health.IgnoreDamage = true; }
            if (body) body.enabled = false;
            if (visualRig) { visualRig.CancelEffects(); visualRig.SetDormant(true); }
        }
        protected override void OnHit(Vector3 direction, float impact) { if (!IsDormant && visualRig) visualRig.PlayHit(); }
        protected override void OnDeathStart(Vector3 direction)
        {
            CancelEncounter(); Enter(RuinsBossState.Dead);
            if (visualRig) visualRig.PlayDeath();
            HitFeedback.Sparks(transform.position + Vector3.up * 1.5f, direction, 14, .85f);
        }
        void OnDisable() { if (initialized) CancelEncounter(); }
        protected override void OnDestroy()
        {
            if (subscribedPlayer) subscribedPlayer.Died -= CancelEncounter;
            CancelEncounter(); base.OnDestroy();
        }
    }
}
