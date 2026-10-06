using AcRoguelike.Liminal;
using UnityEngine;

namespace AcRoguelike.GameTheme
{
    public enum GameVoxelState { Approach, Windup, Attack, Recover }

    /// <summary>Three arcade silhouettes, each with one readable combat verb and a fixed attack commitment.</summary>
    public sealed class GameVoxelMonster : LiminalPropMonster
    {
        public GameVoxelRole role;
        public bool champion;
        public GameVoxelState State { get; private set; }
        public int Attacks { get; private set; }
        public int Shots { get; private set; }
        public Vector3 LockedDirection => aim;
        public const float ChargeLength = 6f, ChargeHalfWidth = .95f;
        public const float SlamRadius = 3.7f, SlamHalfAngle = 65f;

        Transform jaw, leftLimb, rightLimb, crown;
        Vector3 leftRest, rightRest, crownRest;
        Vector3 aim, attackStart;
        float elapsed, nextAttack, stepClock, travelled;
        int volley;
        bool connected;
        GameThemeRoom themeRoom;
#if UNITY_EDITOR
        Vector3 lastPreferred, lastSteered;
        float lastMoved;
        public string MovementDiagnostics => $"preferred={lastPreferred:F3} steered={lastSteered:F3} moved={lastMoved:F5} "
            + (themeRoom && player ? themeRoom.DiagnoseNavigation(transform.position, player.transform.position) : "no target or theme room");
#endif

        protected override int MaxHealth(int s)
        {
            int value = role == GameVoxelRole.PixelMaw ? 70 : role == GameVoxelRole.BitSentry ? 58 : 150;
            return Mathf.RoundToInt((value + s * 12) * (champion ? 2.2f : 1));
        }
        protected override float Weight => State == GameVoxelState.Attack ? .2f : role == GameVoxelRole.StackGuardian ? .6f : 1;
        protected override float HitTilt => role == GameVoxelRole.StackGuardian ? 8 : 14;
        protected override float LyingHalfDepth => role == GameVoxelRole.StackGuardian ? .48f : .42f;

        protected override void OnSetup()
        {
            // A fixed minimum step silently discards walking at high frame rates (including batch validation).
            // Keep locomotion frame-rate independent, including prefabs authored before this setting was added.
            if (body) body.minMoveDistance = 0;
            displayName = role == GameVoxelRole.PixelMaw ? "픽셀 아귀" : role == GameVoxelRole.BitSentry ? "비트 파수꾼" : "스택 수호자";
            jaw = pose.Find("Jaw");
            leftLimb = pose.Find("LeftLimb");
            rightLimb = pose.Find("RightLimb");
            crown = pose.Find("Crown");
            if (!pose.Find("FaceAnchor"))
            {
                var face = new GameObject("FaceAnchor").transform;
                face.SetParent(pose, false);
                // Eye-cell centres/front faces from GameThemeMonsterBuilder, including each Model offset.
                face.localPosition = role == GameVoxelRole.PixelMaw ? new Vector3(0, 6 * .18f, 4.5f * .18f)
                    : role == GameVoxelRole.BitSentry ? new Vector3(0, 5 * .16f + .08f, 3.5f * .16f)
                    : new Vector3(.5f * .2f, 11 * .2f + .1f, 2.5f * .2f);
            }
            if (leftLimb) leftRest = leftLimb.localPosition;
            if (rightLimb) rightRest = rightLimb.localPosition;
            if (crown) crownRest = crown.localPosition;
            State = GameVoxelState.Approach;
            themeRoom = room ? room.GetComponent<GameThemeRoom>() : null;
            nextAttack = Time.time + 1.5f + Random.value * .7f;
        }

        protected override void Think(float dt, Vector3 to, float distance)
        {
            elapsed += dt;
            stepClock += dt;
            switch (State)
            {
                case GameVoxelState.Approach:
                    Approach(dt, to, distance);
                    break;
                case GameVoxelState.Windup:
                    Windup(to);
                    break;
                case GameVoxelState.Attack:
                    Attack(dt);
                    break;
                case GameVoxelState.Recover:
                    poseRotation = Quaternion.Slerp(poseRotation, Quaternion.identity, dt * 8);
                    poseScale = Vector3.Lerp(poseScale, Vector3.one, dt * 8);
                    poseOffset = Vector3.Lerp(poseOffset, Vector3.zero, dt * 8);
                    ResetLimbs(dt * 7);
                    if (elapsed >= (role == GameVoxelRole.StackGuardian ? 1.05f : .75f))
                    {
                        nextAttack = Time.time + (role == GameVoxelRole.BitSentry ? 1.6f : .8f);
                        Enter(GameVoxelState.Approach);
                    }
                    break;
            }
        }

        void Approach(float dt, Vector3 to, float distance)
        {
            Face(to, role == GameVoxelRole.StackGuardian ? 100 : 220);
            float gait = Mathf.Sin(stepClock * (role == GameVoxelRole.StackGuardian ? 5f : 10f));
            poseOffset = Vector3.up * Mathf.Abs(gait) * .065f;
            poseRotation = Quaternion.Euler(0, 0, gait * (role == GameVoxelRole.StackGuardian ? 2 : 4));
            poseScale = Vector3.one;
            ResetLimbs(dt * 7);
            if (leftLimb) leftLimb.localRotation = Quaternion.Euler(gait * 9, 0, 0);
            if (rightLimb) rightLimb.localRotation = Quaternion.Euler(-gait * 9, 0, 0);
            if (Time.time < staggerUntil) return;
            bool visible = CanSeePlayer();
            float range = role == GameVoxelRole.PixelMaw ? 6.5f : role == GameVoxelRole.BitSentry ? 12.5f : 3.5f;
            if (distance <= range && Time.time >= nextAttack && visible)
            {
                aim = to.sqrMagnitude > .001f ? to.normalized : transform.forward;
                Enter(GameVoxelState.Windup);
                return;
            }
            float stop = role == GameVoxelRole.BitSentry ? 7 : role == GameVoxelRole.StackGuardian ? 2.5f : 2.2f;
            Vector3 preferred = distance > stop || !visible ? to.normalized : Vector3.zero;
            if (role == GameVoxelRole.BitSentry && visible && distance < 4) preferred = -to.normalized;
            if (role == GameVoxelRole.BitSentry && visible && distance >= 4 && distance <= 9)
                preferred = Vector3.Cross(Vector3.up, to.normalized) * (Mathf.Sin(stepClock * .5f) > 0 ? 1 : -1);
            if (themeRoom && (distance > stop || !visible) && !Clear(to.normalized, distance))
            {
                Vector3 route = themeRoom.RouteDirection(transform.position, player.transform.position);
                if (route.sqrMagnitude > .001f) preferred = route;
            }
            Vector3 steered = Steer(preferred);
            float moved = MoveBody(steered * (role == GameVoxelRole.StackGuardian ? 1.15f : 1.9f) * dt);
#if UNITY_EDITOR
            lastPreferred = preferred; lastSteered = steered; lastMoved = moved;
#endif
        }

        void Windup(Vector3 to)
        {
            IsWindingUp = true;
            float duration = role == GameVoxelRole.StackGuardian ? 1.05f : .85f;
            float progress = Mathf.Clamp01(elapsed / duration);
            // Tracking ends half way through the warning. The last half is a promised safe dodge window.
            if (progress < .5f && to.sqrMagnitude > .01f) aim = to.normalized;
            Face(aim, 540);
            poseOffset = Vector3.zero;
            if (role == GameVoxelRole.PixelMaw)
            {
                TelegraphLine(transform.position, aim, ChargeLength + .6f, ChargeHalfWidth, progress);
                poseScale = new Vector3(1 + progress * .12f, 1 - progress * .2f, 1);
                poseRotation = Quaternion.Euler(-12 * progress, 0, 0);
                if (jaw) jaw.localRotation = Quaternion.Euler(36 * progress, 0, 0);
            }
            else if (role == GameVoxelRole.BitSentry)
            {
                AttackCue(progress);
                if (leftLimb) leftLimb.localRotation = Quaternion.Euler(-25 * progress, 0, 0);
                if (rightLimb) rightLimb.localRotation = Quaternion.Euler(-25 * progress, 0, 0);
                if (crown) crown.localPosition = crownRest + Vector3.up * (.2f * progress);
                poseScale = new Vector3(1 + .08f * progress, 1 - .08f * progress, 1);
            }
            else
            {
                TelegraphFan(transform.position, aim, SlamRadius, SlamHalfAngle, progress);
                poseRotation = Quaternion.Euler(-15 * progress, 0, -8 * progress);
                if (rightLimb) rightLimb.localRotation = Quaternion.Euler(-115 * progress, 0, -18 * progress);
                if (leftLimb) leftLimb.localRotation = Quaternion.Euler(-50 * progress, 0, 10 * progress);
            }
            if (progress < 1) return;
            HideTelegraph();
            attackStart = transform.position;
            connected = false;
            travelled = 0;
            volley = 0;
            Attacks++;
            Enter(GameVoxelState.Attack);
        }

        void Attack(float dt)
        {
            if (role == GameVoxelRole.PixelMaw)
            {
                Vector3 before = transform.position;
                float wanted = Mathf.Min(ChargeLength - travelled, 10.5f * dt);
                float moved = MoveBody(aim * wanted);
                travelled += moved;
                poseScale = new Vector3(.92f, 1.08f, 1.12f);
                poseRotation = Quaternion.Euler(Mathf.Min(360, travelled / ChargeLength * 360), 0, 0);
                // The rotating mesh pivots around its centre, so it never sinks under the floor.
                Vector3 center = Vector3.up * .8f;
                poseOffset = center - poseRotation * center;
                if (jaw) jaw.localRotation = Quaternion.Euler(12, 0, 0);
                if (!connected && SegmentDistance(player.transform.position, before, transform.position) < ChargeHalfWidth && CanSeePlayer())
                {
                    player.TakeDamage(champion ? 20 : 13);
                    connected = true;
                }
                if (travelled >= ChargeLength - .01f || elapsed >= .65f || (wanted > .01f && moved < wanted * .2f))
                { Thud(transform.position, .65f); Enter(GameVoxelState.Recover); }
            }
            else if (role == GameVoxelRole.BitSentry)
            {
                if (volley < 3 && elapsed >= volley * .16f)
                {
                    Vector3 direction = Quaternion.Euler(0, (volley - 1) * 10, 0) * aim;
                    // Spawn inside the body rather than beyond a nearby wall; swept collision covers the muzzle.
                    GamePixelBolt.Fire(transform.position + Vector3.up * .85f, direction, Health, player, room, champion ? 15 : 9);
                    volley++; Shots++;
                    poseOffset = Vector3.back * .07f;
                    HitFeedback.Sparks(transform.position + Vector3.up * .85f + aim * .7f, aim, 4, .35f);
                }
                if (elapsed >= .55f) Enter(GameVoxelState.Recover);
            }
            else
            {
                float swing = Mathf.Clamp01(elapsed / .18f);
                if (rightLimb) rightLimb.localRotation = Quaternion.Euler(Mathf.Lerp(-115, 48, swing), 0, Mathf.Lerp(-18, 12, swing));
                poseRotation = Quaternion.Euler(Mathf.Lerp(-15, 22, swing), 0, 0);
                if (!connected && swing >= 1)
                {
                    connected = true;
                    Vector3 offset = player.transform.position - attackStart;
                    offset.y = 0;
                    if (offset.magnitude <= SlamRadius && Vector3.Angle(aim, offset) <= SlamHalfAngle && CanSeePlayer())
                        player.TakeDamage(champion ? 25 : 19);
                    Thud(attackStart + aim * 2.1f, 1.45f);
                    HitFeedback.Sparks(attackStart + aim * 2.1f + Vector3.up * .08f, Vector3.up, 10, .7f);
                }
                if (elapsed >= .4f) Enter(GameVoxelState.Recover);
            }
        }

        public static float SegmentDistance(Vector3 point, Vector3 a, Vector3 b)
        {
            point.y = a.y = b.y = 0;
            Vector3 delta = b - a;
            float t = delta.sqrMagnitude > .00001f ? Mathf.Clamp01(Vector3.Dot(point - a, delta) / delta.sqrMagnitude) : 0;
            return Vector3.Distance(point, a + delta * t);
        }

        void ResetLimbs(float amount)
        {
            if (jaw) jaw.localRotation = Quaternion.Slerp(jaw.localRotation, Quaternion.identity, amount);
            if (leftLimb) { leftLimb.localPosition = leftRest; leftLimb.localRotation = Quaternion.Slerp(leftLimb.localRotation, Quaternion.identity, amount); }
            if (rightLimb) { rightLimb.localPosition = rightRest; rightLimb.localRotation = Quaternion.Slerp(rightLimb.localRotation, Quaternion.identity, amount); }
            if (crown) crown.localPosition = Vector3.Lerp(crown.localPosition, crownRest, amount);
        }

        void Enter(GameVoxelState next) { State = next; elapsed = 0; IsWindingUp = next == GameVoxelState.Windup; }
        protected override void OnDeathStart(Vector3 direction)
        {
            HitFeedback.Sparks(transform.position + Vector3.up, direction, 14, .9f);
            IsWindingUp = false;
        }

        public static GameVoxelMonster Create(GameVoxelRole role, Vector3 position, Quaternion rotation, Transform parent, bool champion = false)
        {
            var library = GameVoxelMonsterLibrary.Instance;
            var prefab = library ? library.Prefab(role) : null;
            if (!prefab) { Debug.LogError("Game monster library is missing. Run GameThemeMonsterBuilder.BuildAll before packaging the stage."); return null; }
            var monster = Instantiate(prefab, position, rotation, parent).GetComponent<GameVoxelMonster>();
            monster.champion = champion;
            return monster;
        }
    }
}
