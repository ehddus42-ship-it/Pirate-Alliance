using System.Collections.Generic;
using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.Ruins
{
    public enum RuinsMonsterKind { ScrapBulwark, PenitentHusk, CarrionDrone, OssuaryMedusa, MourningMatron }
    public enum RuinsMonsterState { Approach, Windup, Attack, Recovery, Dead }

    /// <summary>Ruins creatures with separate movement, committed warnings and recovery windows.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(CharacterController))]
    public sealed class RuinsMonster : LiminalPropMonster
    {
        public RuinsMonsterKind kind;
        public RuinsMonsterRig visualRig;
        public RuinsMonsterState State { get; private set; }
        public float StateTime => elapsed;
        public int Pattern => pattern;
        public Vector3 LockedDirection => aim;
        public int Attacks { get; private set; }
        public int Shots { get; private set; }
        public int AreaStrikes { get; private set; }
        public int DamageAttempts { get; private set; }
        public float MovementSpeed { get; private set; }
        public const float HuskReach = 3.2f, HuskHalfAngle = 27f;
        public const float LungeDistance = 2.6f, LungeReach = 1.35f, LungeHalfWidth = .85f;
        public const float TentacleRadius = 3.4f, TentacleHalfAngle = 72f, PulseRadius = 1.15f;
        public const float MatronSweepRadius = 4.3f, MatronHalfAngle = 75f;
        public const float MatronLungeDistance = 5.2f, MatronLungeReach = 1.4f, MatronLungeHalfWidth = 1f;
        public float WarningDuration => kind == RuinsMonsterKind.ScrapBulwark ? 1.05f :
            kind == RuinsMonsterKind.PenitentHusk ? .9f : kind == RuinsMonsterKind.CarrionDrone ? .85f :
            kind == RuinsMonsterKind.MourningMatron ? (pattern == 0 ? 1.2f : 1.3f) : pattern == 0 ? 1.25f : 1f;

        readonly List<RuinsProjectile> projectiles = new List<RuinsProjectile>();
        readonly RuinsGroundPulse[] pulses = new RuinsGroundPulse[3];
        readonly Vector3[] pulseCenters = new Vector3[3];
        readonly RaycastHit[] routeHits = new RaycastHit[64];
        RuinsMonsterNavigation navigation;
        LiminalPlayerHealth subscribedPlayer;
        Vector3 aim = Vector3.forward, attackStart, frameStart;
        float elapsed, nextAttack, moved, travelled;
        int pattern, nextPattern, volley, orbitSign;
        bool connected, initialized;

        protected override int MaxHealth(int stageIndex)
        {
            int health = kind == RuinsMonsterKind.ScrapBulwark ? 150 : kind == RuinsMonsterKind.PenitentHusk ? 105 :
                kind == RuinsMonsterKind.CarrionDrone ? 65 : kind == RuinsMonsterKind.MourningMatron ? 190 : 125;
            return health + stageIndex * 12;
        }
        protected override float Weight => kind == RuinsMonsterKind.ScrapBulwark ? .45f :
            kind == RuinsMonsterKind.OssuaryMedusa || kind == RuinsMonsterKind.MourningMatron ? .7f :
            State == RuinsMonsterState.Attack ? .65f : 1;
        protected override float HitTilt => kind == RuinsMonsterKind.ScrapBulwark ? 7 : 12;
        protected override float KnockbackSpeed => kind == RuinsMonsterKind.ScrapBulwark ? 1.3f : 2.5f;
        protected override float LyingHalfDepth => kind == RuinsMonsterKind.ScrapBulwark ? .7f : .5f;
        float WalkSpeed => kind == RuinsMonsterKind.ScrapBulwark ? 1.15f :
            kind == RuinsMonsterKind.PenitentHusk ? 2.45f : kind == RuinsMonsterKind.CarrionDrone ? 2.7f : 1.65f;
        float RecoveryDuration => kind == RuinsMonsterKind.ScrapBulwark ? 1.15f :
            kind == RuinsMonsterKind.PenitentHusk ? 1.05f : kind == RuinsMonsterKind.CarrionDrone ? .9f :
            kind == RuinsMonsterKind.MourningMatron ? 1.25f : 1.15f;

        protected override void OnSetup()
        {
            initialized = true;
            if (string.IsNullOrEmpty(displayName) || displayName == "사무용품")
                displayName = kind == RuinsMonsterKind.ScrapBulwark ? "고철 방벽" : kind == RuinsMonsterKind.PenitentHusk ? "속죄의 잔해" :
                    kind == RuinsMonsterKind.CarrionDrone ? "사체 수색기" : kind == RuinsMonsterKind.MourningMatron ? "애곡의 모체" : "납골 해파리";
            if (body) body.minMoveDistance = 0;
            navigation = RuinsMonsterNavigation.ForRoom(room);
            visualRig = visualRig ? visualRig : GetComponentInChildren<RuinsMonsterRig>(true);
            if (visualRig) { visualRig.kind = kind; visualRig.Initialize(); }
            if (subscribedPlayer) subscribedPlayer.Died -= CancelAttack;
            subscribedPlayer = player;
            if (subscribedPlayer) subscribedPlayer.Died += CancelAttack;
            if (!Health.aimAnchor)
            {
                var anchor = new GameObject("AimPoint").transform;
                anchor.SetParent(transform, false); anchor.localPosition = Vector3.up * 1.15f;
                Health.aimAnchor = anchor;
            }
            Health.visibleRenderers = GetComponentsInChildren<Renderer>(true);
            orbitSign = (Mathf.RoundToInt(transform.position.x * 5 + transform.position.z) & 1) == 0 ? 1 : -1;
            nextPattern = 0;
            nextAttack = Time.time + 1.15f + (int)kind * .18f;
            Enter(RuinsMonsterState.Approach);
        }

        protected override void Think(float dt, Vector3 toPlayer, float distance)
        {
            elapsed += dt; moved = 0; frameStart = transform.position;
            for (int i = projectiles.Count - 1; i >= 0; i--) if (!projectiles[i] || projectiles[i].Finished) projectiles.RemoveAt(i);
            switch (State)
            {
                case RuinsMonsterState.Approach: Approach(dt, toPlayer, distance); break;
                case RuinsMonsterState.Windup: MoveBody(Vector3.zero); Windup(toPlayer); break;
                case RuinsMonsterState.Attack: Attack(dt); break;
                case RuinsMonsterState.Recovery:
                    MoveBody(Vector3.zero);
                    poseOffset = Vector3.Lerp(poseOffset, Vector3.zero, dt * 7);
                    poseRotation = Quaternion.Slerp(poseRotation, Quaternion.identity, dt * 7);
                    if (elapsed >= RecoveryDuration)
                    {
                        nextAttack = Time.time + (kind == RuinsMonsterKind.CarrionDrone ? 1.05f : .55f);
                        Enter(RuinsMonsterState.Approach);
                    }
                    break;
            }
            MovementSpeed = Vector3.ProjectOnPlane(transform.position - frameStart, Vector3.up).magnitude / Mathf.Max(.0001f, dt);
            if (visualRig) visualRig.SetMotion(State, elapsed, Mathf.Clamp01(MovementSpeed / WalkSpeed), pattern);
        }

        void Approach(float dt, Vector3 to, float distance)
        {
            poseOffset = Vector3.zero; poseRotation = Quaternion.identity; poseScale = Vector3.one;
            Face(to, kind == RuinsMonsterKind.ScrapBulwark ? 100 : 200);
            if (Time.time < staggerUntil) { MoveBody(Vector3.zero); return; }
            bool visible = CanSeePlayer(1.05f);
            float range = kind == RuinsMonsterKind.PenitentHusk ? 3.8f : kind == RuinsMonsterKind.MourningMatron ?
                (nextPattern == 0 ? 4.1f : 6.4f) : kind == RuinsMonsterKind.OssuaryMedusa ? 11 : 14;
            if (visible && distance <= range && Time.time >= nextAttack)
            {
                int wanted = kind == RuinsMonsterKind.OssuaryMedusa ? (distance < 3.3f ? 1 : 0) :
                    kind == RuinsMonsterKind.PenitentHusk || kind == RuinsMonsterKind.MourningMatron ? nextPattern : 0;
                if (StartAttack(wanted)) nextPattern = 1 - wanted;
                return;
            }
            float stop = kind == RuinsMonsterKind.PenitentHusk ? 2.35f : kind == RuinsMonsterKind.MourningMatron ? 3.2f :
                kind == RuinsMonsterKind.ScrapBulwark ? 8 : 7;
            Vector3 preferred = distance > stop || !visible ? to.normalized : Vector3.zero;
            float speed = WalkSpeed;
            if (kind == RuinsMonsterKind.CarrionDrone && visible)
            {
                if (distance < 4.5f) { preferred = -to.normalized; speed = 3.8f; }
                else if (distance < 10.5f) preferred = Vector3.Cross(Vector3.up, to.normalized) * orbitSign;
            }
            else if (kind == RuinsMonsterKind.OssuaryMedusa && visible && distance >= 3.3f && distance < 6)
                preferred = -to.normalized;
            if (navigation && (distance > stop || !visible) && RouteBlocked(to.normalized, Mathf.Min(distance, 12)))
            {
                Vector3 route = navigation.Direction(transform.position, player.transform.position);
                if (route.sqrMagnitude > .001f) preferred = route;
            }
            moved = MoveBody(Steer(preferred, kind == RuinsMonsterKind.ScrapBulwark ? 1.4f : 1.1f) * (speed * dt));
            if (kind == RuinsMonsterKind.CarrionDrone && preferred.sqrMagnitude > .1f && moved < speed * dt * .2f && elapsed > .7f)
            { orbitSign = -orbitSign; elapsed = 0; }
        }

        bool RouteBlocked(Vector3 direction, float distance)
        {
            if (!body || direction.sqrMagnitude < .001f || distance <= .001f) return false;
            Vector3 scale = body.transform.lossyScale;
            float widthScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float radius = Mathf.Max(.01f, (body.radius - body.skinWidth) * widthScale);
            float halfHeight = Mathf.Max(radius, body.height * Mathf.Abs(scale.y) * .5f - body.skinWidth);
            Vector3 center = transform.position + body.transform.TransformVector(body.center);
            Vector3 offset = Vector3.up * (halfHeight - radius);
            int count = gameObject.scene.GetPhysicsScene().CapsuleCast(center - offset, center + offset, radius,
                direction, routeHits, distance, ~0, QueryTriggerInteraction.Ignore);
            if (count >= routeHits.Length) return true;
            for (int i = 0; i < count; i++)
            {
                var hit = routeHits[i]; var other = hit.collider;
                if (!other || other == body || other.transform.IsChildOf(transform) ||
                    other.GetComponentInParent<TrainingEnemy>() || other.GetComponentInParent<LiminalPlayerHealth>()) continue;
                if (Mathf.Abs(hit.normal.y) <= .4f) return true;
            }
            return false;
        }

        /// <summary>Starts a committed attack while idle. Husk, Matron and Medusa expose a second pattern as 1.</summary>
        public bool StartAttack(int requestedPattern = 0)
        {
            if (!initialized || !Health || !Health.IsAlive || !player || !player.IsAlive || State != RuinsMonsterState.Approach ||
                requestedPattern < 0 || requestedPattern > 1) return false;
            if ((kind == RuinsMonsterKind.ScrapBulwark || kind == RuinsMonsterKind.CarrionDrone) && requestedPattern != 0) return false;
            pattern = requestedPattern;
            Vector3 to = player.transform.position - transform.position; to.y = 0;
            aim = to.sqrMagnitude > .001f ? to.normalized : transform.forward;
            attackStart = transform.position;
            volley = 0; travelled = 0; connected = false;
            Enter(RuinsMonsterState.Windup);
            if (kind == RuinsMonsterKind.OssuaryMedusa && pattern == 0)
            {
                PreparePulseCenters();
                for (int i = 0; i < pulses.Length; i++)
                {
                    if (pulses[i]) pulses[i].Cancel();
                    pulses[i] = RuinsGroundPulse.Create(pulseCenters[i], PulseRadius, WarningDuration, Health, player, room, 16);
                    AreaStrikes++;
                }
            }
            // Commit the attack cue immediately, including externally started attacks.
            DrawWarning(0);
            if (visualRig) visualRig.SetMotion(State, 0, 0, pattern);
            return true;
        }

        void Windup(Vector3 to)
        {
            float progress = Mathf.Clamp01(elapsed / WarningDuration);
            // The final two thirds are fixed, so the warning promises a stable dodge direction.
            if (elapsed < WarningDuration * .33f && to.sqrMagnitude > .001f && !(kind == RuinsMonsterKind.OssuaryMedusa && pattern == 0))
                aim = to.normalized;
            Face(aim, 540);
            DrawWarning(progress);
            if (progress < 1) return;
            HideTelegraph();
            attackStart = transform.position; connected = false; travelled = 0;
            Attacks++;
            Enter(RuinsMonsterState.Attack);
        }

        void DrawWarning(float progress)
        {
            if (kind == RuinsMonsterKind.ScrapBulwark || kind == RuinsMonsterKind.CarrionDrone)
                AttackCue(progress);
            else if (kind == RuinsMonsterKind.PenitentHusk)
            {
                if (pattern == 0) TelegraphFan(transform.position, aim, HuskReach, HuskHalfAngle, progress);
                else TelegraphLine(transform.position, aim, LungeDistance + LungeReach, LungeHalfWidth, progress);
            }
            else if (kind == RuinsMonsterKind.MourningMatron)
            {
                if (pattern == 0) TelegraphFan(transform.position, aim, MatronSweepRadius, MatronHalfAngle, progress);
                else TelegraphLine(transform.position, aim, MatronLungeDistance + MatronLungeReach, MatronLungeHalfWidth, progress);
            }
            else if (pattern == 1) TelegraphFan(transform.position, aim, TentacleRadius, TentacleHalfAngle, progress);
            else AttackCue(progress);
        }

        void Attack(float dt)
        {
            if (kind == RuinsMonsterKind.ScrapBulwark)
            {
                MoveBody(Vector3.zero);
                while (volley < 3 && elapsed >= volley * .18f) { Fire(aim, false, 12); volley++; }
                if (elapsed >= .65f) Enter(RuinsMonsterState.Recovery);
            }
            else if (kind == RuinsMonsterKind.CarrionDrone)
            {
                MoveBody(Vector3.zero);
                if (volley == 0)
                {
                    for (int i = 0; i < 3; i++)
                        Fire(Quaternion.Euler(0, EnemyFanPattern.Angle(i, 3, 32f), 0) * aim, true, 9);
                    volley = 3;
                }
                if (elapsed >= .35f) Enter(RuinsMonsterState.Recovery);
            }
            else if (kind == RuinsMonsterKind.PenitentHusk || kind == RuinsMonsterKind.MourningMatron)
            {
                bool matron = kind == RuinsMonsterKind.MourningMatron;
                if (pattern == 0)
                {
                    MoveBody(Vector3.zero);
                    if (!connected && elapsed >= (matron ? .35f : .28f))
                        StrikeFan(matron ? MatronSweepRadius : HuskReach, matron ? MatronHalfAngle : HuskHalfAngle, matron ? 23 : 19);
                    if (elapsed >= (matron ? .8f : .65f)) Enter(RuinsMonsterState.Recovery);
                }
                else
                {
                    Vector3 before = transform.position;
                    float limit = matron ? MatronLungeDistance : LungeDistance;
                    float wanted = Mathf.Min(Mathf.Max(0, limit - travelled), (matron ? 7.5f : 6.5f) * dt);
                    moved = MoveBody(aim * wanted);
                    // Consume the requested path budget, including a bent slide around a glancing obstacle.
                    // An endpoint distance would under-count that bend and extend the total lunge.
                    travelled += wanted;
                    Vector3 strikeEnd = transform.position + aim * (matron ? MatronLungeReach : LungeReach);
                    float reach = matron ? MatronLungeReach : LungeReach, halfWidth = matron ? MatronLungeHalfWidth : LungeHalfWidth;
                    if (!connected && InsideLine(player.transform.position, attackStart, aim, limit + reach, halfWidth) &&
                        SegmentDistance(player.transform.position, before, strikeEnd) <= halfWidth && CanSeePlayer())
                    { connected = true; DamageAttempts++; player.TakeDamage(matron ? 25 : 20); }
                    if (travelled >= limit - .01f || elapsed >= (matron ? .8f : .5f) || (wanted > .001f && moved < wanted * .2f))
                        Enter(RuinsMonsterState.Recovery);
                }
            }
            else
            {
                MoveBody(Vector3.zero);
                if (pattern == 1 && !connected && elapsed >= .3f) StrikeFan(TentacleRadius, TentacleHalfAngle, 17);
                if (elapsed >= (pattern == 0 ? .3f : .65f)) Enter(RuinsMonsterState.Recovery);
            }
        }

        void Fire(Vector3 direction, bool energy, int damage)
        {
            // Emission begins inside the body's footprint. A nearby wall cannot be skipped by a long muzzle offset.
            var shot = RuinsProjectile.Fire(transform.position + Vector3.up * 1.05f, direction, Health, player, room, damage, energy);
            if (shot) { projectiles.Add(shot); Shots++; }
        }

        void StrikeFan(float radius, float halfAngle, int damage)
        {
            connected = true;
            Vector3 offset = player.transform.position - attackStart; offset.y = 0;
            if (offset.magnitude <= radius && Vector3.Angle(aim, offset) <= halfAngle && CanSeePlayer())
            { DamageAttempts++; player.TakeDamage(damage); }
            HitFeedback.Sparks(attackStart + aim * (radius * .6f) + Vector3.up * .5f, aim, 6, .45f);
        }

        void PreparePulseCenters()
        {
            Vector3 center = player.transform.position;
            if (room)
            {
                Vector3 local = room.transform.InverseTransformPoint(center);
                local.x = Mathf.Clamp(local.x, room.localBounds.min.x + 4.1f, room.localBounds.max.x - 4.1f);
                local.z = Mathf.Clamp(local.z, room.localBounds.min.z + 4.1f, room.localBounds.max.z - 4.1f);
                local.y = .05f;
                center = room.transform.TransformPoint(local);
            }
            else center.y = transform.position.y;
            Vector3 side = Vector3.Cross(Vector3.up, aim);
            pulseCenters[0] = center;
            pulseCenters[1] = center + side * 2.7f;
            pulseCenters[2] = center - side * 2.7f;
        }

        static float SegmentDistance(Vector3 point, Vector3 a, Vector3 b)
        {
            point.y = a.y = b.y = 0;
            Vector3 step = b - a;
            float t = step.sqrMagnitude > .00001f ? Mathf.Clamp01(Vector3.Dot(point - a, step) / step.sqrMagnitude) : 0;
            return Vector3.Distance(point, a + step * t);
        }

        static bool InsideLine(Vector3 point, Vector3 origin, Vector3 forward, float length, float halfWidth)
        {
            Vector3 offset = point - origin; offset.y = 0;
            float along = Vector3.Dot(offset, forward);
            return along >= 0 && along <= length &&
                Mathf.Abs(Vector3.Dot(offset, Vector3.Cross(Vector3.up, forward))) <= halfWidth;
        }

        void Enter(RuinsMonsterState next)
        {
            State = next; elapsed = 0; IsWindingUp = next == RuinsMonsterState.Windup;
            if (next == RuinsMonsterState.Recovery) { MovementSpeed = 0; poseOffset = Vector3.zero; }
        }

        public void CancelAttack()
        {
            CancelCombatArtifacts();
            if (State == RuinsMonsterState.Dead) return;
            Enter(RuinsMonsterState.Approach); nextAttack = Time.time + 1;
            if (visualRig) visualRig.SetMotion(State, 0, 0, pattern);
        }

        void CancelCombatArtifacts()
        {
            if (telegraph) telegraph.Hide();
            AttackAnticipation.Hide(transform);
            for (int i = 0; i < pulses.Length; i++) { if (pulses[i]) pulses[i].Cancel(); pulses[i] = null; }
            foreach (var projectile in projectiles) if (projectile) projectile.Cancel();
            projectiles.Clear(); IsWindingUp = false; MovementSpeed = 0;
        }

        protected override void OnHit(Vector3 direction, float impact)
        {
            if (visualRig) visualRig.PlayHit();
        }

        protected override void OnDeathStart(Vector3 direction)
        {
            CancelCombatArtifacts(); Enter(RuinsMonsterState.Dead);
            if (visualRig) visualRig.PlayDeath();
            HitFeedback.Sparks(transform.position + Vector3.up, direction, 9, .65f);
        }

        void OnDisable() => CancelCombatArtifacts();
        protected override void OnDestroy()
        {
            if (subscribedPlayer) subscribedPlayer.Died -= CancelAttack;
            CancelCombatArtifacts(); base.OnDestroy();
        }

        public static RuinsMonster Create(RuinsMonsterKind kind, Vector3 position, Quaternion rotation, Transform parent)
        {
            var prefab = Resources.Load<GameObject>("RuinsMonsters/" + kind);
            if (!prefab) return null;
            var root = Instantiate(prefab, position, rotation, parent);
            var monster = root.GetComponent<RuinsMonster>();
            if (!monster) { Destroy(root); return null; }
            monster.kind = kind;
            return monster;
        }
    }
}
