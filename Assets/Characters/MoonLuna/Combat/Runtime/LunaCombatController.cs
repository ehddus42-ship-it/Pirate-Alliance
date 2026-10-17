using System.Collections.Generic;
using AcRoguelike;
using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MoonLuna
{
    /// <summary>The battle body's ranged kit. The shared motor still owns every movement and dodge.</summary>
    [DisallowMultipleComponent]
    public sealed class LunaCombatController : MonoBehaviour, IHunterSkill
    {
        [Header("Basic attack (provisional tuning)")]
        [Min(.05f)] public float attackInterval = .55f;
        [Min(.1f)] public float comboResetTime = 1.3f;
        public int[] attackDamage = { 18, 24, 32 };
        [Range(0, 1)] public float criticalChance = .1f;
        [Min(1)] public float criticalMultiplier = 1.5f;
        public bool holdToAttack = true;
        [Min(0)] public float castBusyTime = .08f;

        [Header("Projectiles")]
        [Min(.1f)] public float projectileSpeed = 18f;
        [Min(.1f)] public float projectileRange = 18f;
        [Min(.1f)] public float projectileLifetime = 3f;
        [Min(.01f)] public float projectileRadius = .18f;
        [Min(.1f)] public float projectileHeight = 1.15f;
        [Min(0)] public float explosionRadius = 1.6f;
        [Min(0)] public float homingDegreesPerSecond = 540f;
        [Range(-1, 1)] public float aimConeDot = .5f;
        public LayerMask collisionMask = ~0;
        [Min(1)] public int maximumProjectiles = 48;

        [Header("Moonlight — E / right stick press")]
        [Min(.1f)] public float moonlightDuration = 8f;
        [Min(.1f)] public float moonlightCooldown = 14f;
        [Min(.1f)] public float moonlightRadius = 6f;
        [Min(.05f)] public float moonlightInterval = .8f;
        [Min(1)] public int moonlightDamage = 12;

        [Header("Golden eyes — R / gamepad X")]
        [Min(.1f)] public float ultimateDuration = 10f;
        [Min(.1f)] public float ultimateCooldown = 24f;
        [Min(1)] public float awakenedAttackSpeed = 2f;

        PlayerMotor motor;
        HunterRoster roster;
        LiminalPlayerHealth health;
        TalismanCaster caster;
        MeleeSlash melee;
        IMeleeHitHooks hitHooks;
        LiminalRunDirector director, subscribedDirector;
        LunaCombatVfx fx;
        InputAction attack, moonlight, ultimate;
        Transform projectileRoot;
        readonly List<Shot> shots = new List<Shot>();
        readonly RaycastHit[] sweepHits = new RaycastHit[64];
        readonly Collider[] overlapHits = new Collider[64];
        readonly RaycastHit[] sightHits = new RaycastHit[32];
        readonly List<TrainingEnemy> targetScan = new List<TrainingEnemy>(), blastScan = new List<TrainingEnemy>();
        float nextMoonlightSearchAt;
        int labelSeconds = -1;
        string awakenedLabel;
        float attackReadyAt, moonlightReadyAt, ultimateReadyAt, lastAttackAt = -100;
        float moonlightUntil, awakenedUntil, nextMoonlightShotAt, busyUntil;
        int nextPhase;
        bool moonlightFx, awakenedFx;
        TrainingEnemy moonlightTarget, lastBasicHitTarget;

        // A slot keeps one visual of each used phase/form. Reuse never carries damage/targets/age forward.
        sealed class Shot
        {
            public GameObject root;
            public readonly GameObject[] visuals = new GameObject[6];
            public bool active, homing, awakened, supportReported, basic;
            public int phase, damage;
            public float age, travelled;
            public Vector3 direction;
            public TrainingEnemy target;
            public readonly HashSet<TrainingEnemy> hit = new HashSet<TrainingEnemy>();
        }

        public string Label
        {
            get
            {
                if (!IsAwakened) return "ULT · R";
                // The HUD reads this every frame; build the text once per second.
                int seconds = Mathf.CeilToInt(UltimateRemaining);
                if (seconds != labelSeconds) { labelSeconds = seconds; awakenedLabel = "각성 " + seconds; }
                return awakenedLabel;
            }
        }
        public bool Busy => isActiveAndEnabled && Time.time < busyUntil;
        public float CooldownRemaining => Mathf.Max(0, ultimateReadyAt - Time.time);
        public float AttackCooldownRemaining => Mathf.Max(0, attackReadyAt - Time.time);
        public float MoonlightCooldownRemaining => Mathf.Max(0, moonlightReadyAt - Time.time);
        public float UltimateRemaining => Mathf.Max(0, awakenedUntil - Time.time);
        public float MoonlightRemaining => Mathf.Max(0, moonlightUntil - Time.time);
        public bool IsAwakened => isActiveAndEnabled && UltimateRemaining > 0;
        public bool IsMoonlightActive => isActiveAndEnabled && MoonlightRemaining > 0;
        public Vector3 MoonlightCenter => motor ? motor.transform.position : transform.position;
        public TrainingEnemy MoonlightTarget => moonlightTarget;
        public InputAction AttackAction => attack;
        public InputAction MoonlightAction => moonlight;
        public InputAction UltimateAction => ultimate;
        public int AttackCount { get; private set; }
        public int HitCount { get; private set; }
        public int ProjectileCount { get; private set; }
        public int MoonlightCount { get; private set; }
        public int UltimateCount { get; private set; }
        public int LastAttackPhase { get; private set; } = -1;
        public TrainingEnemy LastHitTarget { get; private set; }
        public TrainingEnemy LastBasicHitTarget => lastBasicHitTarget;
        public int ActiveProjectileCount
        {
            get { int count = 0; foreach (var shot in shots) if (shot.active) count++; return count; }
        }
        public bool CombatAvailable => isActiveAndEnabled && motor && motor.isActiveAndEnabled
            && (!health || health.IsAlive) && (!director || director.Phase == LiminalRunPhase.Exploring)
            && (!roster || (roster.CurrentVisual == gameObject && roster.CurrentCustomCombat
                && !roster.CurrentMovementOnly && !roster.InLobby));
        public bool CanAct => CombatAvailable && Time.timeScale > 0
            && !motor.IsDashing && !motor.IsHeld && !motor.IsLaunched;

        void Awake()
        {
            Bind();
            attack = new InputAction("LunaAttack", InputActionType.Button);
            attack.AddBinding("<Mouse>/leftButton");
            attack.AddBinding("<Keyboard>/j");
            attack.AddBinding("<Gamepad>/rightShoulder");
            attack.AddBinding("<Gamepad>/rightTrigger");
            moonlight = new InputAction("LunaMoonlight", InputActionType.Button);
            moonlight.AddBinding("<Keyboard>/e");
            moonlight.AddBinding("<Gamepad>/rightStickPress");
            ultimate = new InputAction("LunaUltimate", InputActionType.Button);
            ultimate.AddBinding("<Keyboard>/r");
            ultimate.AddBinding("<Gamepad>/buttonWest");
        }

        void Bind()
        {
            if (!motor) motor = GetComponentInParent<PlayerMotor>();
            if (!roster) roster = GetComponentInParent<HunterRoster>();
            if (!health) health = GetComponentInParent<LiminalPlayerHealth>();
            if (!caster) caster = GetComponentInParent<TalismanCaster>();
            if (!melee) melee = GetComponentInParent<MeleeSlash>();
            if (hitHooks == null && motor) hitHooks = motor.GetComponent<IMeleeHitHooks>();
            if (!fx) fx = GetComponent<LunaCombatVfx>();
            if (!director) director = FindFirstObjectByType<LiminalRunDirector>();
            if (isActiveAndEnabled && director != subscribedDirector)
            {
                Unsubscribe();
                subscribedDirector = director;
                if (subscribedDirector)
                {
                    subscribedDirector.RoomChanged += CancelActions;
                    subscribedDirector.StageChanged += CancelActions;
                }
            }
        }

        void OnEnable()
        {
            Bind();
            attack?.Enable(); moonlight?.Enable(); ultimate?.Enable();
        }

        void OnDisable()
        {
            attack?.Disable(); moonlight?.Disable(); ultimate?.Disable();
            CancelActions();
            Unsubscribe();
        }

        void OnDestroy()
        {
            Unsubscribe();
            attack?.Dispose(); moonlight?.Dispose(); ultimate?.Dispose();
            if (projectileRoot) Destroy(projectileRoot.gameObject);
        }

        void Unsubscribe()
        {
            if (subscribedDirector)
            {
                subscribedDirector.RoomChanged -= CancelActions;
                subscribedDirector.StageChanged -= CancelActions;
            }
            subscribedDirector = null;
        }

        void Update()
        {
            Bind();
            // Pause and choice screens freeze the kit. Leaving combat, dying or changing the body cancels it.
            bool terminal = director && (director.Phase == LiminalRunPhase.Lobby
                || director.Phase == LiminalRunPhase.Victory || director.Phase == LiminalRunPhase.Defeat
                || director.Phase == LiminalRunPhase.InvalidConfiguration);
            if (terminal || (health && !health.IsAlive)
                || (roster && (roster.CurrentVisual != gameObject || roster.CurrentMovementOnly || !roster.CurrentCustomCombat)))
            {
                if (moonlightFx || awakenedFx || ActiveProjectileCount > 0) CancelActions();
                return;
            }
            if (!CombatAvailable || Time.deltaTime <= 0) return;
            if (moonlightFx && !IsMoonlightActive)
            {
                moonlightFx = false; moonlightTarget = null;
                if (fx) fx.SetMoonlight(false, 0);
            }
            if (awakenedFx && !IsAwakened)
            {
                awakenedFx = false; nextPhase = 0;
                if (fx) fx.SetAwakened(false);
            }
            if (Application.isFocused)
            {
                if (ultimate.WasPressedThisFrame()) TryUltimate();
                if (moonlight.WasPressedThisFrame()) TryMoonlight();
                bool mouseOnUi = EventSystem.current && EventSystem.current.IsPointerOverGameObject()
                    && attack.activeControl != null && attack.activeControl.device is Mouse;
                if (!mouseOnUi && (attack.WasPressedThisFrame() || (holdToAttack && attack.IsPressed()))) TryAttack();
            }
            if (IsMoonlightActive)
            {
                UpdateMoonlightTarget();
                if (CanAct && moonlightTarget && Time.time >= nextMoonlightShotAt)
                {
                    nextMoonlightShotAt = Time.time + Mathf.Max(.05f, moonlightInterval);
                    Fire(1, IsAwakened, moonlightDamage, moonlightTarget, true, false);
                }
            }
            for (int i = 0; i < shots.Count && CombatAvailable && Time.timeScale > 0; i++)
                if (shots[i].active) Advance(shots[i], Time.deltaTime);
        }

        public bool TryAttack()
        {
            Bind();
            if (!CanAct || Time.time < attackReadyAt) return false;
            if (Time.time - lastAttackAt > Mathf.Max(.1f, comboResetTime)) nextPhase = 0;
            bool awakened = IsAwakened;
            int phase = awakened ? 2 : nextPhase;
            Vector3 direction = AimDirection();
            if (IsMoonlightActive) UpdateMoonlightTarget();
            TrainingEnemy target = IsMoonlightActive && Valid(moonlightTarget) ? moonlightTarget
                : FindTarget(MoonlightCenter, projectileRange, direction, aimConeDot);
            int damage = attackDamage != null && attackDamage.Length > phase ? attackDamage[phase] : 18 + phase * 7;
            if (!Fire(phase, awakened, damage, target, IsMoonlightActive, true)) return false;
            LastAttackPhase = phase;
            AttackCount++;
            lastAttackAt = Time.time;
            nextPhase = awakened ? 0 : (phase + 1) % 3;
            float cooldownScale = caster ? Mathf.Clamp(caster.cooldown / .7f, .3f, 2f) : 1;
            attackReadyAt = Time.time + Mathf.Max(.05f, attackInterval) * cooldownScale
                / (awakened ? Mathf.Max(1, awakenedAttackSpeed) : 1);
            busyUntil = Time.time + Mathf.Max(0, castBusyTime);
            return true;
        }

        public bool TryMoonlight()
        {
            Bind();
            if (!CanAct || IsMoonlightActive || Time.time < moonlightReadyAt) return false;
            moonlightUntil = Time.time + Mathf.Max(.1f, moonlightDuration);
            moonlightReadyAt = Time.time + Mathf.Max(.1f, moonlightCooldown);
            nextMoonlightShotAt = Time.time;
            moonlightTarget = null;
            nextMoonlightSearchAt = 0;
            UpdateMoonlightTarget();
            moonlightFx = true;
            MoonlightCount++;
            if (fx) { fx.moonlightRadius = moonlightRadius; fx.SetMoonlight(true, moonlightDuration); fx.PlayCast(false); }
            return true;
        }

        public bool TryUltimate()
        {
            Bind();
            if (!CanAct || IsAwakened || Time.time < ultimateReadyAt) return false;
            awakenedUntil = Time.time + Mathf.Max(.1f, ultimateDuration);
            ultimateReadyAt = Time.time + Mathf.Max(.1f, ultimateCooldown);
            awakenedFx = true;
            // An existing basic-attack recovery also benefits from the new attack speed.
            attackReadyAt = Time.time + AttackCooldownRemaining / Mathf.Max(1, awakenedAttackSpeed);
            UltimateCount++;
            if (fx) { fx.SetAwakened(true); fx.PlayCast(true); }
            return true;
        }

        /// <summary>Returns every owned projectile, releases both buffs and preserves cooldowns across swaps.</summary>
        public void CancelActions()
        {
            moonlightUntil = awakenedUntil = busyUntil = 0;
            moonlightFx = awakenedFx = false;
            moonlightTarget = null;
            nextPhase = 0;
            lastBasicHitTarget = null;
            lastAttackAt = -100;
            foreach (var shot in shots) Return(shot);
            if (fx) { fx.SetMoonlight(false, 0); fx.SetAwakened(false); fx.ClearTransientEffects(); }
        }

        void UpdateMoonlightTarget()
        {
            Vector3 origin = MoonlightCenter + Vector3.up * projectileHeight;
            // The field remembers the last basic-attack victim ahead of a nearer unmarked enemy.
            if (Valid(lastBasicHitTarget) && (lastBasicHitTarget.AimPoint - origin).sqrMagnitude <= moonlightRadius * moonlightRadius
                && CanSee(origin, lastBasicHitTarget.AimPoint, lastBasicHitTarget))
            {
                moonlightTarget = lastBasicHitTarget;
                return;
            }
            if (!Valid(moonlightTarget) || (moonlightTarget.AimPoint - origin).sqrMagnitude > moonlightRadius * moonlightRadius
                || !CanSee(origin, moonlightTarget.AimPoint, moonlightTarget))
            {
                // With nobody in reach, look again a few times a second instead of every frame.
                if (Valid(moonlightTarget) || Time.time >= nextMoonlightSearchAt)
                {
                    moonlightTarget = FindTarget(MoonlightCenter, moonlightRadius, Vector3.zero, -1);
                    if (!moonlightTarget) nextMoonlightSearchAt = Time.time + .1f;
                }
                else moonlightTarget = null;
            }
        }

        static bool Valid(TrainingEnemy enemy) => enemy && enemy.isActiveAndEnabled && enemy.IsAlive
            && enemy.CanBeTargeted && !enemy.IgnoreDamage;

        Vector3 AimDirection()
        {
            Vector3 direction = motor ? Vector3.ProjectOnPlane(motor.AimPoint - motor.transform.position, Vector3.up) : transform.forward;
            if (direction.sqrMagnitude < .01f) direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            return direction.sqrMagnitude > .01f ? direction.normalized : Vector3.forward;
        }

        TrainingEnemy FindTarget(Vector3 center, float range, Vector3 direction, float minimumDot)
        {
            TrainingEnemy best = null;
            float score = float.PositiveInfinity;
            Vector3 origin = center + Vector3.up * projectileHeight;
            TrainingEnemy.CollectActive(targetScan);
            foreach (var enemy in targetScan)
            {
                if (!Valid(enemy)) continue;
                Vector3 offset = enemy.AimPoint - origin;
                if (offset.sqrMagnitude > range * range) continue;
                Vector3 planar = Vector3.ProjectOnPlane(offset, Vector3.up);
                float alignment = direction.sqrMagnitude > .01f && planar.sqrMagnitude > .01f
                    ? Vector3.Dot(direction.normalized, planar.normalized) : 1;
                float candidate = offset.sqrMagnitude * (1 + (1 - alignment) * 2);
                if (alignment < minimumDot || candidate >= score || !CanSee(origin, enemy.AimPoint, enemy)) continue;
                best = enemy; score = candidate;
            }
            return best;
        }

        bool OwnCollider(Collider collider) => !collider || (motor && collider.transform.IsChildOf(motor.transform))
            || (projectileRoot && collider.transform.IsChildOf(projectileRoot));

        bool CanSee(Vector3 from, Vector3 to, TrainingEnemy target)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < .0001f) return true;
            float length = delta.magnitude;
            Vector3 direction = delta / length;
            int count = Physics.RaycastNonAlloc(from, direction, sightHits, length, collisionMask, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = count == sightHits.Length
                ? Physics.RaycastAll(from, direction, length, collisionMask, QueryTriggerInteraction.Ignore)
                : sightHits;
            if (hits != sightHits) count = hits.Length;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (OwnCollider(hit.collider)) continue;
                var other = hit.collider.GetComponentInParent<TrainingEnemy>();
                if (other == target || (other && !other.IsAlive)) continue;
                return false;
            }
            return true;
        }

        bool Fire(int phase, bool awakened, int damage, TrainingEnemy target, bool homing, bool basic)
        {
            Shot shot = null;
            foreach (var candidate in shots) if (!candidate.active && candidate.root) { shot = candidate; break; }
            if (shot == null)
            {
                if (shots.Count >= Mathf.Max(1, maximumProjectiles)) return false;
                if (!projectileRoot)
                {
                    var container = new GameObject("Luna Combat Projectiles");
                    SceneManager.MoveGameObjectToScene(container, gameObject.scene);
                    projectileRoot = container.transform;
                }
                shot = new Shot { root = new GameObject("Luna Projectile") };
                shot.root.transform.SetParent(projectileRoot, false);
                shot.root.SetActive(false);
                shots.Add(shot);
            }
            shot.active = true; shot.phase = phase; shot.awakened = awakened; shot.damage = Mathf.Max(1, damage);
            shot.homing = homing; shot.basic = basic; shot.supportReported = false;
            shot.age = shot.travelled = 0; shot.target = target; shot.hit.Clear();
            Vector3 forward = AimDirection();
            Vector3 chest = MoonlightCenter + Vector3.up * projectileHeight;
            Vector3 origin = chest + forward * .4f;
            Vector3 toward = Valid(target) ? target.AimPoint - origin : forward;
            shot.direction = toward.sqrMagnitude > .0001f ? toward.normalized : forward;
            shot.root.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(shot.direction));
            int visualIndex = phase + (awakened ? 3 : 0);
            for (int i = 0; i < shot.visuals.Length; i++) if (shot.visuals[i]) shot.visuals[i].SetActive(i == visualIndex);
            if (!shot.visuals[visualIndex] && fx)
                shot.visuals[visualIndex] = fx.CreateProjectileVisual(phase, awakened, shot.root.transform);
            shot.root.SetActive(true);
            ProjectileCount++;
            // Do not place a muzzle on the far side of a nearby wall or enemy.
            if (FindImpact(chest, forward, .4f, out var closeEnemy, out var closePoint))
            {
                Impact(shot, closePoint, closeEnemy);
                Return(shot);
            }
            return true;
        }

        void Advance(Shot shot, float dt)
        {
            if (!shot.root) { shot.active = false; return; }
            if (shot.homing && Valid(shot.target))
            {
                Vector3 towards = shot.target.AimPoint - shot.root.transform.position;
                if (towards.sqrMagnitude > .0001f)
                    shot.direction = Vector3.RotateTowards(shot.direction, towards.normalized,
                        Mathf.Max(0, homingDegreesPerSecond) * Mathf.Deg2Rad * dt, 0).normalized;
            }
            shot.age += dt;
            float distance = Mathf.Min(ProjectileTuning.ScaleSpeed(Mathf.Max(.1f, projectileSpeed)) * dt,
                Mathf.Max(0, projectileRange - shot.travelled));
            Vector3 from = shot.root.transform.position;
            if (FindImpact(from, shot.direction, distance, out var enemy, out var point))
            {
                Impact(shot, point, enemy);
                Return(shot);
                return;
            }
            shot.travelled += distance;
            shot.root.transform.SetPositionAndRotation(from + shot.direction * distance, Quaternion.LookRotation(shot.direction));
            if (shot.age >= Mathf.Max(.1f, projectileLifetime) || shot.travelled >= projectileRange)
            {
                if (shot.phase == 2) Impact(shot, shot.root.transform.position, null);
                Return(shot);
            }
        }

        bool FindImpact(Vector3 from, Vector3 direction, float distance, out TrainingEnemy enemy, out Vector3 point)
        {
            enemy = null; point = from;
            float nearest = float.PositiveInfinity;
            int count = Physics.SphereCastNonAlloc(from, Mathf.Max(.01f, projectileRadius), direction,
                sweepHits, distance, collisionMask, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = count == sweepHits.Length
                ? Physics.SphereCastAll(from, Mathf.Max(.01f, projectileRadius), direction, distance, collisionMask, QueryTriggerInteraction.Ignore)
                : sweepHits;
            if (hits != sweepHits) count = hits.Length;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (OwnCollider(hit.collider) || hit.distance >= nearest) continue;
                var candidate = hit.collider.GetComponentInParent<TrainingEnemy>();
                if (candidate && !candidate.IsAlive) continue;
                nearest = hit.distance; enemy = candidate; point = from + direction * hit.distance;
            }
            // SphereCast does not report colliders containing the cast origin.
            int overlaps = Physics.OverlapSphereNonAlloc(from, Mathf.Max(.01f, projectileRadius), overlapHits,
                collisionMask, QueryTriggerInteraction.Ignore);
            Collider[] touching = overlaps == overlapHits.Length
                ? Physics.OverlapSphere(from, Mathf.Max(.01f, projectileRadius), collisionMask, QueryTriggerInteraction.Ignore) : overlapHits;
            if (touching != overlapHits) overlaps = touching.Length;
            for (int i = 0; i < overlaps; i++)
            {
                var collider = touching[i];
                if (OwnCollider(collider)) continue;
                var candidate = collider.GetComponentInParent<TrainingEnemy>();
                if (candidate && !candidate.IsAlive) continue;
                enemy = candidate; point = from; return true;
            }
            return !float.IsPositiveInfinity(nearest);
        }

        void Impact(Shot shot, Vector3 at, TrainingEnemy direct)
        {
            if (!CombatAvailable) return;
            if (fx) fx.PlayImpact(at, shot.phase, shot.awakened);
            if (Valid(direct)) Damage(shot, direct);
            if (shot.phase != 2 || explosionRadius <= 0 || !CombatAvailable || Time.timeScale <= 0) return;
            TrainingEnemy.CollectActive(blastScan);
            foreach (var enemy in blastScan)
            {
                if (!CombatAvailable || Time.timeScale <= 0) break;
                if (!Valid(enemy) || shot.hit.Contains(enemy)) continue;
                var body = enemy.GetComponent<CharacterController>();
                float radius = explosionRadius + (body ? body.radius : .25f);
                if ((enemy.AimPoint - at).sqrMagnitude > radius * radius || !CanSee(at, enemy.AimPoint, enemy)) continue;
                Damage(shot, enemy);
            }
        }

        void Damage(Shot shot, TrainingEnemy enemy)
        {
            if (!Valid(enemy) || !shot.hit.Add(enemy)) return;
            float multiplier = melee ? Mathf.Max(.1f, melee.damageMultiplier) : 1;
            multiplier *= hitHooks != null ? hitHooks.DamageMultiplier(enemy, shot.basic && shot.phase == 2) : 1;
            float chance = hitHooks != null ? hitHooks.CritChance(criticalChance) : criticalChance;
            bool critical = Random.value < Mathf.Clamp01(chance);
            if (critical) multiplier *= hitHooks != null ? hitHooks.CritMultiplier(criticalMultiplier) : criticalMultiplier;
            int amount = Mathf.Max(1, Mathf.RoundToInt(shot.damage * multiplier));
            Vector3 direction = Vector3.ProjectOnPlane(shot.direction, Vector3.up).normalized;
            int beforeHealth = enemy.Health, beforeHits = enemy.HitCount;
            enemy.LastHitDirection = direction;
            enemy.LastHitImpact = shot.phase == 2 ? 1.3f : 1;
            enemy.TakeDamage(amount);
            if (!enemy || enemy.HitCount == beforeHits) return;
            HitCount++;
            LastHitTarget = enemy;
            int dealt = Mathf.Max(1, beforeHealth - enemy.Health);
            // Keep shared damage readability without introducing the sword kit's petals and slash sparks.
            HitFeedback.Flash(enemy);
            HitFeedback.DamageNumber(enemy.AimPoint + Vector3.up * .25f, dealt, shot.phase == 2, critical);
            HitFeedback.Play(shot.phase == 2 ? HitFeedback.Sfx.HeavyHit : HitFeedback.Sfx.Hit, .5f, 1.2f);
            if (critical) hitHooks?.OnCrit(enemy);
            if (shot.basic && !shot.supportReported)
            {
                shot.supportReported = true;
                lastBasicHitTarget = enemy;
                if (IsMoonlightActive) UpdateMoonlightTarget();
                if (caster) caster.RegisterMeleeHit(enemy);
                hitHooks?.OnCutLanded(enemy, shot.phase == 2);
            }
        }

        static void Return(Shot shot)
        {
            shot.active = false; shot.target = null; shot.hit.Clear();
            if (shot.root) shot.root.SetActive(false);
        }
    }
}
