using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.GameTheme
{
    public enum GameTetrominoBossState
    {
        Approach, VolleyWindup, Volley, BarWindup, BarThrow, SummonWindup, Summon, Recovery, Dead
    }

    /// <summary>Drop Keeper: a handheld block-game boss with two earbud hands and three committed attacks.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(CharacterController))]
    public sealed class GameTetrominoBoss : LiminalPropMonster
    {
        public float moveSpeed = 2f;
        public float attackRange = 14f;
        public int volleyDamage = 15, barDamage = 24;
        public GameBossCableRig cableRig;
        public GameTetrominoBossState State { get; private set; }
        public float StateTime => elapsed;
        public Vector3 LockedDirection => aim;
        public int Attacks { get; private set; }
        public int Volleys { get; private set; }
        public int Shots { get; private set; }
        public int BarsThrown { get; private set; }
        public int SummonWaves { get; private set; }
        public int SummonedChargers { get; private set; }
        public const float VolleyWarning = 1.2f, BarWarning = 1.9f, SummonWarning = 1.4f;

        readonly Vector3[] summonPositions = new Vector3[3];
        readonly Collider[] overlaps = new Collider[32];
        Vector3 aim = Vector3.forward;
        Transform heldBar;
        float elapsed, nextAttack, stepClock;
        int pattern, volley;
        bool released, initialized;
        GameThemeRoom themeRoom;
        LiminalPlayerHealth subscribedPlayer;

        protected override int MaxHealth(int index) => 1000 + index * 65;
        protected override float Weight => State == GameTetrominoBossState.Approach || State == GameTetrominoBossState.Recovery ? .5f : .18f;
        protected override float KnockbackSpeed => 1.3f;
        protected override float HitTilt => 7;
        protected override float LyingHalfDepth => .55f;

        protected override void OnSetup()
        {
            displayName = "드롭 키퍼";
            initialized = true;
            if (subscribedPlayer) subscribedPlayer.Died -= CancelAttack;
            subscribedPlayer = player;
            if (subscribedPlayer) subscribedPlayer.Died += CancelAttack;
            if (body) body.minMoveDistance = 0;
            if (pose)
            {
                cableRig = cableRig ? cableRig : pose.GetComponent<GameBossCableRig>();
                if (!cableRig) cableRig = pose.gameObject.AddComponent<GameBossCableRig>();
                cableRig.Initialize();
            }
            themeRoom = room ? room.GetComponent<GameThemeRoom>() : null;
            if (!Health.aimAnchor)
            {
                var anchor = new GameObject("BossAimPoint").transform;
                anchor.SetParent(transform, false);
                anchor.localPosition = Vector3.up * 1.65f;
                Health.aimAnchor = anchor;
            }
            Health.visibleRenderers = GetComponentsInChildren<Renderer>();
            pattern = 0;
            nextAttack = Time.time + 1.6f;
            Enter(GameTetrominoBossState.Approach);
        }

        protected override void Think(float dt, Vector3 toPlayer, float distance)
        {
            elapsed += dt;
            stepClock += dt;
            if (State != GameTetrominoBossState.Approach) MoveBody(Vector3.zero);
            switch (State)
            {
                case GameTetrominoBossState.Approach:
                    Approach(dt, toPlayer, distance);
                    break;
                case GameTetrominoBossState.VolleyWindup:
                case GameTetrominoBossState.BarWindup:
                case GameTetrominoBossState.SummonWindup:
                    Windup(toPlayer);
                    break;
                case GameTetrominoBossState.Volley:
                    FireVolley();
                    break;
                case GameTetrominoBossState.BarThrow:
                    ThrowBar();
                    break;
                case GameTetrominoBossState.Summon:
                    Summon();
                    break;
                case GameTetrominoBossState.Recovery:
                    RelaxPose(dt);
                    if (elapsed >= 1.15f)
                    {
                        nextAttack = Time.time + 1f;
                        Enter(GameTetrominoBossState.Approach);
                    }
                    break;
            }
            if (cableRig) cableRig.SetMotion(State, elapsed);
        }

        void Approach(float dt, Vector3 to, float distance)
        {
            Face(to, 100);
            float gait = Mathf.Sin(stepClock * 5.5f);
            poseOffset = Vector3.up * (.08f + Mathf.Abs(gait) * .075f);
            poseRotation = Quaternion.Euler(gait * 2, 0, gait * 3.5f);
            poseScale = new Vector3(1 + Mathf.Abs(gait) * .015f, 1 - Mathf.Abs(gait) * .018f, 1);
            if (Time.time < staggerUntil) return;
            bool canSee = CanSeePlayer(1.35f);
            if (distance <= attackRange && canSee && Time.time >= nextAttack)
            {
                if (StartAttack(pattern)) pattern = (pattern + 1) % 3;
                return;
            }
            Vector3 preferred = distance > 10.5f || !canSee ? to.normalized : distance < 6.5f ? -to.normalized : Vector3.zero;
            if (themeRoom && (distance > 10.5f || !canSee) && !Clear(to.normalized, distance))
            {
                Vector3 route = themeRoom.RouteDirection(transform.position, player.transform.position);
                if (route.sqrMagnitude > .001f) preferred = route;
            }
            MoveBody(Steer(preferred, 1.6f) * (moveSpeed * dt));
        }

        /// <summary>Starts the numbered pattern (0 volley, 1 spinning I, 2 three Z chargers) from idle.</summary>
        public bool StartAttack(int attack)
        {
            if (!initialized || !Health || !Health.IsAlive || !player || !player.IsAlive || State != GameTetrominoBossState.Approach || attack < 0 || attack > 2) return false;
            Vector3 to = player.transform.position - transform.position;
            to.y = 0;
            aim = to.sqrMagnitude > .001f ? to.normalized : transform.forward;
            if (attack == 2 && !PrepareSummonPositions())
            {
                nextAttack = Time.time + .6f;
                return false;
            }
            Enter(attack == 0 ? GameTetrominoBossState.VolleyWindup : attack == 1 ? GameTetrominoBossState.BarWindup : GameTetrominoBossState.SummonWindup);
            if (attack == 1) CreateHeldBar();
            return true;
        }

        void Windup(Vector3 to)
        {
            float duration = State == GameTetrominoBossState.VolleyWindup ? VolleyWarning : State == GameTetrominoBossState.BarWindup ? BarWarning : SummonWarning;
            float progress = Mathf.Clamp01(elapsed / duration);
            // The last 0.7 / 0.9 seconds are fully committed; aim cannot chase a late dodge.
            float trackingTime = State == GameTetrominoBossState.BarWindup ? 1f : .5f;
            if (elapsed < trackingTime && to.sqrMagnitude > .001f) aim = to.normalized;
            Face(aim, 240);
            poseOffset = Vector3.up * (.08f + Mathf.Sin(progress * Mathf.PI) * .08f);
            poseScale = new Vector3(1 + progress * .04f, 1 - progress * .055f, 1);
            poseRotation = Quaternion.Euler(-7 * progress, 0, Mathf.Sin(elapsed * 5) * 2);
            AttackCue(progress);
            if (State == GameTetrominoBossState.BarWindup)
            {
                if (heldBar)
                {
                    heldBar.localScale = Vector3.one * Mathf.SmoothStep(.05f, 1, progress * 4);
                    heldBar.localRotation = Quaternion.Euler(0, elapsed * 630, 0);
                }
            }
            if (progress < 1) return;
            HideTelegraph();
            Attacks++;
            Enter(State == GameTetrominoBossState.VolleyWindup ? GameTetrominoBossState.Volley : State == GameTetrominoBossState.BarWindup ? GameTetrominoBossState.BarThrow : GameTetrominoBossState.Summon);
        }

        void FireVolley()
        {
            // All three fans share the committed direction. The projectile itself switches from slow drop to fast drop.
            while (volley < 3 && elapsed >= volley * .25f)
            {
                for (int shot = 0; shot < 3; shot++)
                {
                    Vector3 direction = Quaternion.Euler(0, (shot - 1) * 18 + (volley == 1 ? 5 : -2), 0) * aim;
                    TetrominoShape shape = (TetrominoShape)(1 + (volley * 3 + shot) % 5);
                    TetrominoProjectile.Fire(transform.position + Vector3.up * 1.05f, direction, Health, player, room, volleyDamage, shape, false);
                    Shots++;
                }
                volley++;
                Volleys++;
                HitFeedback.Sparks(transform.position + Vector3.up * 1.1f + aim, aim, 5, .4f);
            }
            poseOffset = -Vector3.forward * (Mathf.Exp(-Mathf.Repeat(elapsed, .25f) * 15) * .1f) + Vector3.up * .08f;
            if (elapsed >= .85f) Enter(GameTetrominoBossState.Recovery);
        }

        void ThrowBar()
        {
            poseRotation = Quaternion.Euler(Mathf.Lerp(-7, 12, Mathf.Clamp01(elapsed / .28f)), 0, 0);
            if (!released && elapsed >= .18f)
            {
                released = true;
                DestroyHeldBar();
                TetrominoProjectile.Fire(transform.position + Vector3.up * 1.05f, aim, Health, player, room, barDamage, TetrominoShape.I, true);
                BarsThrown++;
                HitFeedback.Sparks(transform.position + Vector3.up * 1.2f + aim, aim, 9, .65f);
            }
            if (elapsed >= .7f) Enter(GameTetrominoBossState.Recovery);
        }

        void Summon()
        {
            if (!released && elapsed >= .2f)
            {
                released = true;
                for (int i = 0; i < summonPositions.Length; i++)
                {
                    // A moving obstacle may enter a reserved spot during preparation. Never spawn inside it.
                    if (!SpawnPositionClear(summonPositions[i])) continue;
                    var charger = TetrominoCharger.Spawn(summonPositions[i], Quaternion.LookRotation(aim), Health, player, room);
                    if (charger) SummonedChargers++;
                    HitFeedback.Dust(summonPositions[i], .7f);
                }
                SummonWaves++;
                Thud(transform.position, .7f);
            }
            poseOffset = Vector3.up * (.08f + Mathf.Sin(Mathf.Clamp01(elapsed / .65f) * Mathf.PI) * .12f);
            if (elapsed >= .75f) Enter(GameTetrominoBossState.Recovery);
        }

        bool PrepareSummonPositions()
        {
            for (int slot = 0; slot < summonPositions.Length; slot++)
            {
                bool found = false;
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    int search = attempt % 8;
                    int offset = search == 0 ? 0 : (search + 1) / 2 * (search % 2 == 1 ? 1 : -1);
                    float angle = (slot - 1) * 55 + offset * 12;
                    float radius = 3.3f + attempt / 8 * 1.2f;
                    Vector3 position = transform.position + Quaternion.Euler(0, angle, 0) * aim * radius;
                    bool separated = true;
                    for (int previous = 0; previous < slot; previous++)
                        if ((position - summonPositions[previous]).sqrMagnitude < 2.5f * 2.5f) separated = false;
                    if (!separated || !SpawnPositionClear(position)) continue;
                    summonPositions[slot] = position;
                    found = true;
                    break;
                }
                if (!found) return false;
            }
            return true;
        }

        bool SpawnPositionClear(Vector3 position)
        {
            if (room && !room.Contains(position, 1.5f)) return false;
            if (player && Vector3.ProjectOnPlane(position - player.transform.position, Vector3.up).sqrMagnitude < 2.5f * 2.5f) return false;
            int count = gameObject.scene.GetPhysicsScene().OverlapSphere(position + Vector3.up * .8f, .7f, overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count >= overlaps.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var hit = overlaps[i];
                if (!hit || hit.transform.IsChildOf(transform)) continue;
                return false;
            }
            return true;
        }

        void CreateHeldBar()
        {
            DestroyHeldBar();
            Transform hand = cableRig && cableRig.rightHand ? cableRig.rightHand : pose;
            if (!hand) return;
            heldBar = TetrominoVisual.Create(hand, TetrominoShape.I, .52f);
            heldBar.name = "Held I Block";
            heldBar.localPosition = new Vector3(0, .25f, .12f);
            heldBar.localScale = Vector3.one * .05f;
        }

        void DestroyHeldBar()
        {
            if (heldBar) Destroy(heldBar.gameObject);
            heldBar = null;
        }

        void RelaxPose(float dt)
        {
            float blend = 1 - Mathf.Exp(-7 * dt);
            poseOffset = Vector3.Lerp(poseOffset, Vector3.up * .08f, blend);
            poseRotation = Quaternion.Slerp(poseRotation, Quaternion.identity, blend);
            poseScale = Vector3.Lerp(poseScale, Vector3.one, blend);
        }

        void Enter(GameTetrominoBossState next)
        {
            State = next;
            elapsed = 0;
            released = false;
            volley = 0;
            IsWindingUp = next == GameTetrominoBossState.VolleyWindup || next == GameTetrominoBossState.BarWindup || next == GameTetrominoBossState.SummonWindup;
            if (cableRig) cableRig.SetMotion(next, 0);
        }

        public void CancelAttack()
        {
            DestroyHeldBar();
            if (telegraph) telegraph.Hide();
            AttackAnticipation.Hide(transform);
            if (State != GameTetrominoBossState.Dead) Enter(GameTetrominoBossState.Approach);
            nextAttack = Time.time + 1.5f;
        }

        protected override void OnDeathStart(Vector3 direction)
        {
            CancelAttack();
            Enter(GameTetrominoBossState.Dead);
            HitFeedback.Sparks(transform.position + Vector3.up * 1.5f, direction, 18, 1.2f);
        }

        void OnDisable()
        {
            if (initialized) CancelAttack();
        }

        protected override void OnDestroy()
        {
            if (subscribedPlayer) subscribedPlayer.Died -= CancelAttack;
            DestroyHeldBar();
            base.OnDestroy();
        }

        public static GameTetrominoBoss Create(Vector3 position, Quaternion rotation, Transform parent)
        {
            var prefab = Resources.Load<GameObject>("GameTheme/TetrominoBoss");
            if (!prefab) { Debug.LogError("Game-theme boss prefab is missing: GameTheme/TetrominoBoss."); return null; }
            return Instantiate(prefab, position, rotation, parent).GetComponent<GameTetrominoBoss>();
        }
    }
}
