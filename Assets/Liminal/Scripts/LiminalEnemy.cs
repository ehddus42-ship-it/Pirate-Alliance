using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>Grounded pursuer with a readable, dodgeable wind-up; no pre-baked navigation required.</summary>
    [RequireComponent(typeof(TrainingEnemy))]
    public sealed class LiminalEnemy : MonoBehaviour
    {
        public bool isBoss;
        public string displayName = "잔상";
        public float moveSpeed = 1.6f;
        public float attackInterval = 2.7f;
        public int damage = 12;
        public TrainingEnemy Health { get; private set; }
        public bool IsWindingUp => windingUp;
        LiminalPlayerHealth player;
        LiminalRoom room;
        CharacterController body;
        LineRenderer warning;
        Material warningMaterial;
        Transform visual;
        Vector3 attackPoint, attackForward;
        float nextAttack, attackAt, attackRadius, windupDuration;
        float animationTime;
        bool windingUp, lineAttack;
        int pattern;

        public void Initialize(LiminalPlayerHealth target, LiminalRoom owner, int stage, bool boss)
        {
            player = target;
            room = owner;
            isBoss = boss;
            displayName = boss ? "관리자 · 마지막 대기실" : "남겨진 잔상";
            Health = GetComponent<TrainingEnemy>();
            body = GetComponent<CharacterController>();
            if (!body) body = gameObject.AddComponent<CharacterController>();
            body.center = new Vector3(0, 1, 0);
            body.height = 2;
            body.radius = boss ? .9f : .45f;
            body.stepOffset = .25f;
            Health.Configure(boss ? 850 : 60 + stage * 18, false);
            Health.Defeated += OnDefeated;
            moveSpeed = boss ? 1.1f : 1.5f + stage * .12f;
            damage = boss ? 24 : 12 + stage * 3;
            nextAttack = Time.time + 1.2f + (transform.GetSiblingIndex() % 4) * .35f;
            visual = transform.Find("Visual");
            MakeWarning();
        }

        void Update()
        {
            if (!Health || !Health.IsAlive || !player || !player.IsAlive || !room || Time.deltaTime <= 0) return;
            animationTime += Time.deltaTime;
            if (visual)
            {
                visual.localPosition = Vector3.up * (.025f + Mathf.Sin(animationTime * 2) * .035f);
                visual.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(animationTime * 1.4f) * 2);
            }
            if (windingUp)
            {
                float progress = 1 - Mathf.Clamp01((attackAt - Time.time) / windupDuration);
                warning.widthMultiplier = .05f + progress * .11f;
                warning.startColor = warning.endColor = Color.Lerp(new Color(1, .7f, .26f), new Color(1, .2f, .13f), progress);
                if (Time.time >= attackAt) ResolveAttack();
                return;
            }
            Vector3 towards = player.transform.position - transform.position;
            towards.y = 0;
            float distance = towards.magnitude;
            if (Time.time >= nextAttack && distance < (isBoss ? 15 : 7))
            { BeginAttack(); return; }
            if (distance > (isBoss ? 5 : 2.4f))
            {
                Vector3 direction = Steer(towards.normalized);
                Vector3 next = transform.position + direction * (moveSpeed * Time.deltaTime);
                if (room.Contains(next, 1.2f)) body.Move(direction * (moveSpeed * Time.deltaTime) + Vector3.down * (4 * Time.deltaTime));
            }
            if (towards.sqrMagnitude > .1f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(towards), Time.deltaTime * 3);
        }

        Vector3 Steer(Vector3 preferred)
        {
            if (ClearDirection(preferred)) return preferred;
            for (int angle = 35; angle <= 140; angle += 35)
            {
                Vector3 a = Quaternion.Euler(0, angle, 0) * preferred;
                if (ClearDirection(a)) return a;
                Vector3 b = Quaternion.Euler(0, -angle, 0) * preferred;
                if (ClearDirection(b)) return b;
            }
            return Vector3.zero;
        }

        bool ClearDirection(Vector3 direction)
        {
            foreach (var hit in Physics.SphereCastAll(transform.position + Vector3.up, body.radius * .8f,
                direction, .9f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
                if (hit.collider.GetComponentInParent<LiminalPlayerHealth>()) continue;
                return false;
            }
            return true;
        }

        void BeginAttack()
        {
            windingUp = true;
            bool enraged = isBoss && Health.Health < Health.maxHealth / 2;
            windupDuration = isBoss ? (enraged ? 1.05f : 1.45f) : 1.15f;
            attackAt = Time.time + windupDuration;
            lineAttack = isBoss && pattern++ % 2 == 1;
            attackPoint = lineAttack ? transform.position : player.transform.position;
            attackPoint.y = .065f;
            attackForward = Vector3.ProjectOnPlane(player.transform.position - transform.position, Vector3.up).normalized;
            if (attackForward.sqrMagnitude < .1f) attackForward = transform.forward;
            attackRadius = isBoss ? (enraged ? 3.8f : 3) : 1.65f;
            DrawWarning();
            warning.enabled = true;
        }

        void DrawWarning()
        {
            if (lineAttack)
            {
                Vector3 right = Vector3.Cross(Vector3.up, attackForward) * 1.6f;
                warning.positionCount = 5;
                warning.SetPositions(new[] { attackPoint - right, attackPoint + right,
                    attackPoint + attackForward * 18 + right, attackPoint + attackForward * 18 - right, attackPoint - right });
                return;
            }
            warning.positionCount = 65;
            for (int i = 0; i <= 64; i++)
            {
                float a = i / 64f * Mathf.PI * 2;
                warning.SetPosition(i, attackPoint + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * attackRadius);
            }
        }

        void ResolveAttack()
        {
            windingUp = false;
            warning.enabled = false;
            Vector3 delta = player.transform.position - attackPoint;
            delta.y = 0;
            bool hits = delta.sqrMagnitude < attackRadius * attackRadius;
            if (lineAttack)
            {
                float along = Vector3.Dot(delta, attackForward);
                float side = Mathf.Abs(Vector3.Dot(delta, Vector3.Cross(Vector3.up, attackForward)));
                hits = along >= 0 && along < 18 && side < 1.6f;
            }
            if (hits) player.TakeDamage(damage);
            bool enraged = isBoss && Health.Health < Health.maxHealth / 2;
            nextAttack = Time.time + (isBoss ? (enraged ? 1.25f : 2.1f) : attackInterval);
        }

        void MakeWarning()
        {
            var go = new GameObject("AttackTelegraph");
            go.layer = 2;
            go.transform.SetParent(transform, false);
            warning = go.AddComponent<LineRenderer>();
            warning.useWorldSpace = true;
            warning.numCornerVertices = 3;
            warning.numCapVertices = 3;
            warning.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            warning.receiveShadows = false;
            warningMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
            warningMaterial.color = new Color(1, .45f, .15f);
            warning.sharedMaterial = warningMaterial;
            warning.enabled = false;
        }

        void OnDefeated(TrainingEnemy _)
        {
            if (warning) warning.enabled = false;
            if (body) body.enabled = false;
        }
        void OnDestroy()
        {
            if (Health) Health.Defeated -= OnDefeated;
            if (warningMaterial) Destroy(warningMaterial);
        }

        public static GameObject CreateSilhouette(Vector3 position, Transform parent, bool boss)
        {
            var root = new GameObject(boss ? "TheAttendant_Boss" : "WaitingEcho");
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            Material coat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            coat.color = new Color(.075f, .105f, .11f);
            Material porcelain = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            porcelain.color = new Color(.72f, .76f, .69f);
            float scale = boss ? 1.6f : 1;
            Part("Uniform", PrimitiveType.Cylinder, new Vector3(0, 1.05f, 0), new Vector3(.75f, .85f, .5f), coat);
            Part("FeaturelessFace", PrimitiveType.Sphere, new Vector3(0, 2.02f, .06f), new Vector3(.48f, .64f, .4f), porcelain);
            Part("LeftSleeve", PrimitiveType.Capsule, new Vector3(-.53f, 1.12f, 0), new Vector3(.22f, .62f, .23f), coat);
            Part("RightSleeve", PrimitiveType.Capsule, new Vector3(.53f, 1.12f, 0), new Vector3(.22f, .62f, .23f), coat);
            Part("IDBadge", PrimitiveType.Cube, new Vector3(.17f, 1.5f, .26f), new Vector3(.15f, .2f, .04f), porcelain);
            if (boss)
                for (int i = -2; i <= 2; i++)
                    Part("SilentKey_" + (i + 2), PrimitiveType.Cube, new Vector3(i * .22f, 2.6f + Mathf.Abs(i) * -.08f, 0),
                        new Vector3(.07f, .65f, .12f), porcelain);
            visual.localScale = Vector3.one * scale;
            var anchor = new GameObject("AimAnchor").transform;
            anchor.SetParent(root.transform, false);
            anchor.localPosition = Vector3.up * (1.2f * scale);
            var health = root.AddComponent<TrainingEnemy>();
            health.aimAnchor = anchor;
            root.AddComponent<LiminalEnemyVisualCleanup>().ownedMaterials = new[] { coat, porcelain };
            return root;

            void Part(string name, PrimitiveType type, Vector3 p, Vector3 s, Material mat)
            {
                var part = GameObject.CreatePrimitive(type);
                part.name = name;
                part.transform.SetParent(visual, false);
                part.transform.localPosition = p;
                part.transform.localScale = s;
                var collider = part.GetComponent<Collider>();
                collider.enabled = false;
                Object.Destroy(collider);
                part.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }
    }

}
