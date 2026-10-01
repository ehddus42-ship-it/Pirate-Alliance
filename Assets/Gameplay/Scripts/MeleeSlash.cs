using System.Collections;
using UnityEngine;

namespace AcRoguelike
{
    /// <summary>
    /// Katana basic attack modelled on Yae Sakura (Honkai Impact 3rd, A-rank): a four-hit combo of a descending
    /// cut, a rising follow-up, a spinning cut and an iai draw finisher that cuts twice. PlayerCombat owns input,
    /// combo timing and the Animator; this component holds the sword, dashes toward the target, tests the hit arc,
    /// deals damage and plays the hit feedback. Motions are retargetable Humanoid clips (see
    /// Documentation/PlayerMelee), and the sword grip is derived from humanoid hand and finger bones, so nothing here
    /// depends on the current character model. Every hit with a target is registered on TalismanCaster as a cast,
    /// so cast-based systems (validation, the support skill) keep working.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MeleeSlash : MonoBehaviour
    {
        [System.Serializable]
        public struct Swing
        {
            public int damage;
            public float range, halfAngle, impact, hitStop, shake;
            [Tooltip("Slash arc: start/end angle (0 = forward, + = right), roll around forward, radius.")]
            public float arcFrom, arcTo, arcRoll, arcRadius;
            public bool finisher;
            [Tooltip("Optional second cut of the same swing (seconds after the first, before attack speed scaling).")]
            public float secondHitDelay;
            public int secondDamage;
            public float secondArcFrom, secondArcTo, secondArcRoll;
        }

        // Clip timing measured from the motion capture (sword-tip speed peaks), at the baked playback speed.
        public static readonly float[] Durations = { .615f, .70f, .58f, 1.33f };
        public static readonly float[] HitTimes = { .37f, .33f, .37f, .50f };

        public Swing[] swings =
        {
            // 1: descending diagonal cut, right to left.
            new Swing { damage = 18, range = 2.6f, halfAngle = 75, impact = 1f, hitStop = .05f, shake = .045f,
                arcFrom = 125, arcTo = -85, arcRoll = 22, arcRadius = 1.9f },
            // 2: flatter follow-up cut, right to left.
            new Swing { damage = 20, range = 2.6f, halfAngle = 75, impact = 1.05f, hitStop = .055f, shake = .05f,
                arcFrom = 50, arcTo = -100, arcRoll = -8, arcRadius = 2.0f },
            // 3: spinning cut around the whole body.
            new Swing { damage = 24, range = 2.8f, halfAngle = 180, impact = 1.2f, hitStop = .06f, shake = .07f,
                arcFrom = -180, arcTo = 180, arcRoll = 5, arcRadius = 2.2f },
            // 4: iai draw-cut, then a heavy descending cut that launches enemies.
            new Swing { damage = 26, range = 3.2f, halfAngle = 100, impact = 1.3f, hitStop = .07f, shake = .09f,
                arcFrom = -100, arcTo = 110, arcRoll = 4, arcRadius = 2.6f, finisher = true,
                secondHitDelay = .33f, secondDamage = 42, secondArcFrom = 160, secondArcTo = -30, secondArcRoll = 38 },
        };
        [Tooltip("Targets closer than this are reached with a short dash at the start of a swing.")]
        public float lungeRange = 6f;
        public float lungeStop = 1.4f;
        public float lungeSpeed = 17f;
        public float katanaLength = 1.05f;
        [Range(0, 1)] public float gripCurl = 1f;
        public GameObject katanaModel;

        public int HitCount { get; private set; }
        public TrainingEnemy LastHitTarget { get; private set; }

        PlayerCombat combat;
        PlayerMotor motor;
        TalismanCaster caster;
        CharacterController body;
        Animator animator;
        Transform katana, tip, hand, forearm, hip;
        Transform[][] fingers;
        Transform indexRoot, littleRoot, thumbRoot;
        TrailRenderer trail;
        Vector3 lungeTarget;
        float lungeUntil, sheatheAt;
        bool drawn;
        Coroutine secondHit;

        void Awake()
        {
            combat = GetComponent<PlayerCombat>();
            motor = GetComponent<PlayerMotor>();
            caster = GetComponent<TalismanCaster>();
            body = GetComponent<CharacterController>();
            if (caster) caster.castRange = Mathf.Min(caster.castRange, lungeRange + .5f);
            if (combat)
            {
                combat.attackDurations = (float[])Durations.Clone();
                combat.hitTimes = (float[])HitTimes.Clone();
                combat.attackMovementMultiplier = .05f;
                combat.comboQueueWindow = .3f;
            }
            if (!katanaModel) katanaModel = Resources.Load<GameObject>("AstraiaKatana");
        }

        void Start() => EnsureKatana();

        /// <summary>Called by PlayerCombat when a swing starts.</summary>
        public void BeginSwing(int index, TrainingEnemy target, Vector3 direction)
        {
            EnsureKatana();
            CancelSecondHit();
            Draw(true);
            if (trail) { trail.Clear(); trail.emitting = true; }
            lungeUntil = 0;
            if (target && target.IsAlive)
            {
                Vector3 to = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
                if (to.magnitude > lungeStop && to.magnitude < lungeRange)
                {
                    lungeTarget = target.transform.position - to.normalized * lungeStop;
                    float hit = combat && combat.hitTimes != null && index < combat.hitTimes.Length ? combat.hitTimes[index] : .3f;
                    lungeUntil = Time.time + hit * .85f;
                }
            }
        }

        /// <summary>Called by PlayerCombat at the swing's hit time. Returns true when at least one enemy was cut.</summary>
        public bool ResolveSwing(int index, TrainingEnemy preferred, Vector3 direction)
        {
            var swing = swings[Mathf.Clamp(index, 0, swings.Length - 1)];
            direction = Flat(direction);
            bool hit = Cut(swing, swing.damage, swing.arcFrom, swing.arcTo, swing.arcRoll, preferred, direction, false);
            if (swing.secondHitDelay > 0)
            {
                float scale = combat ? 1f / Mathf.Max(.1f, combat.AnimationSpeed) : 1;
                secondHit = StartCoroutine(SecondCut(swing, preferred, direction, swing.secondHitDelay * scale));
            }
            return hit;
        }

        IEnumerator SecondCut(Swing swing, TrainingEnemy preferred, Vector3 direction, float delay)
        {
            yield return new WaitForSeconds(delay);
            secondHit = null;
            Cut(swing, swing.secondDamage, swing.secondArcFrom, swing.secondArcTo, swing.secondArcRoll, preferred, direction, true);
        }

        void CancelSecondHit()
        {
            if (secondHit != null) StopCoroutine(secondHit);
            secondHit = null;
        }

        bool Cut(Swing swing, int baseDamage, float from, float to, float roll, TrainingEnemy preferred, Vector3 direction, bool launch)
        {
            Vector3 chest = transform.position + Vector3.up * 1.05f;
            bool heavy = swing.finisher && (launch || swing.secondHitDelay <= 0);
            // The arc plays even on a miss, so every swing reads as a cut.
            HitFeedback.SlashArc(chest, direction, from, to, swing.arcRadius, roll, heavy ? .95f : .62f,
                heavy ? HitFeedback.Sakura * 2.6f : HitFeedback.Sakura * 1.9f,
                heavy ? HitFeedback.Violet * 1.7f : HitFeedback.Crimson * 1.4f, heavy ? .09f : .06f, heavy ? .32f : .2f);
            HitFeedback.SlashArc(chest, direction, from * .82f, to * .82f, swing.arcRadius * .78f, roll + 8, .3f,
                Color.white * 1.7f, HitFeedback.Sakura, heavy ? .08f : .05f, .12f);
            if (heavy)
            {
                HitFeedback.SlashArc(transform.position + Vector3.up * .08f, direction, -180, 180, swing.range, 0, .5f,
                    HitFeedback.Crimson * 1.6f, HitFeedback.Violet, .12f, .38f);
                HitFeedback.Petals(chest, 18);
            }

            bool hitAny = false;
            TrainingEnemy first = null;
            float impact = launch ? swing.impact * 1.5f : swing.impact;
            foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
            {
                if (!enemy.IsAlive || !enemy.CanBeTargeted) continue;
                Vector3 offset = Vector3.ProjectOnPlane(enemy.transform.position - transform.position, Vector3.up);
                float reach = swing.range + EnemyRadius(enemy);
                bool inArc = offset.magnitude <= reach && (offset.magnitude < .6f || Vector3.Angle(direction, offset) <= swing.halfAngle);
                if (!inArc && !(enemy == preferred && offset.magnitude <= reach + .6f)) continue;
                Vector3 push = offset.sqrMagnitude > .01f ? offset.normalized : direction;
                enemy.LastHitDirection = push;
                enemy.LastHitImpact = impact;
                Vector3 point = enemy.AimPoint - push * .25f;
                int damage = baseDamage + Random.Range(-2, 3);
                bool lethal = damage >= enemy.Health;
                enemy.TakeDamage(damage);
                HitFeedback.Hit(enemy, point, push, damage, heavy || lethal);
                hitAny = true;
                if (!first || enemy == preferred) first = enemy;
            }
            if (hitAny)
            {
                HitCount++;
                LastHitTarget = first;
                HitFeedback.HitStop(heavy ? swing.hitStop * 1.6f : swing.hitStop, heavy ? .04f : .08f);
                HitFeedback.Shake(heavy ? swing.shake * 1.8f : swing.shake, heavy ? .24f : .12f);
            }
            // The iai's second cut belongs to the same swing, so only the first cut counts as a cast.
            if (caster && first && !launch) caster.RegisterMeleeHit(first);
            return hitAny;
        }

        /// <summary>Called when a swing ends or is cancelled (the iai's second cut still lands if the swing completed).</summary>
        public void EndSwing()
        {
            lungeUntil = 0;
            if (trail) trail.emitting = false;
            sheatheAt = Time.time + 1.6f;
        }

        // PlayerMotor turns the visual child, not the root.
        Transform Body => motor && motor.visual ? motor.visual : transform;

        Vector3 Flat(Vector3 direction)
        {
            direction = Vector3.ProjectOnPlane(direction, Vector3.up);
            return direction.sqrMagnitude > .01f ? direction.normalized : Body.forward;
        }

        static float EnemyRadius(TrainingEnemy enemy)
        {
            var controller = enemy.GetComponent<CharacterController>();
            return controller ? controller.radius : .45f;
        }

        void Update()
        {
            if (motor && motor.IsDashing) { lungeUntil = 0; CancelSecondHit(); }
            if (lungeUntil > Time.time && body && body.enabled)
            {
                Vector3 delta = Vector3.ProjectOnPlane(lungeTarget - transform.position, Vector3.up);
                float step = lungeSpeed * Time.deltaTime;
                if (delta.magnitude <= step) { body.Move(delta); lungeUntil = 0; }
                else body.Move(delta.normalized * step);
            }
            if (drawn && !(combat && combat.IsAttacking) && secondHit == null && Time.time > sheatheAt) Draw(false);
        }

        void LateUpdate()
        {
            if (!katana) return;
            if (drawn && hand)
            {
                // Grip from the humanoid hand: fingers point from the wrist to the knuckles, the blade leaves the fist
                // on the thumb side (index knuckle side), blended a little with the forearm so it follows the swing.
                Vector3 along = forearm ? (hand.position - forearm.position).normalized : Body.forward;
                Vector3 fingerDir = along, radial = Vector3.Cross(Vector3.up, along).normalized;
                if (indexRoot && littleRoot)
                {
                    Vector3 knuckles = (indexRoot.position + littleRoot.position) * .5f;
                    fingerDir = (knuckles - hand.position).normalized;
                    radial = (indexRoot.position - littleRoot.position).normalized;
                }
                Vector3 palm = Vector3.Cross(fingerDir, radial).normalized;
                if (thumbRoot && Vector3.Dot(palm, thumbRoot.position - hand.position) < 0) palm = -palm;
                CurlFingers(fingerDir, palm);
                Vector3 blade = (radial * .8f + along * .2f).normalized;
                Vector3 grip = hand.position + fingerDir * .055f + palm * .02f;
                katana.SetPositionAndRotation(grip, Quaternion.LookRotation(blade, fingerDir));
            }
            else if (hip)
            {
                // Sheathed at the left hip: edge up, tip pointing back and slightly down.
                Vector3 back = -Body.forward, side = -Body.right;
                Vector3 dir = (back * .85f + Vector3.down * .3f + side * .1f).normalized;
                katana.SetPositionAndRotation(hip.position + side * .22f + Body.forward * .3f + Vector3.up * .02f,
                    Quaternion.LookRotation(dir, Vector3.up));
            }
        }

        /// <summary>Closes the right hand around the grip after the animation pose, for any humanoid with finger bones.</summary>
        void CurlFingers(Vector3 fingerDir, Vector3 palm)
        {
            if (fingers == null || gripCurl <= 0) return;
            Vector3 axis = Vector3.Cross(fingerDir, palm).normalized;
            float[] angles = { 62, 78, 52 };
            foreach (var chain in fingers)
                for (int j = 0; j < chain.Length; j++)
                    if (chain[j]) chain[j].rotation = Quaternion.AngleAxis(angles[Mathf.Min(j, 2)] * gripCurl, axis) * chain[j].rotation;
        }

        void Draw(bool draw)
        {
            if (draw == drawn) return;
            drawn = draw;
            if (draw && katana) HitFeedback.Petals(katana.position, 5);
        }

        void EnsureKatana()
        {
            if (katana) return;
            animator = motor ? motor.animator : GetComponentInChildren<Animator>();
            if (animator && animator.isHuman)
            {
                hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                forearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                hip = animator.GetBoneTransform(HumanBodyBones.Hips);
                indexRoot = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
                littleRoot = animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
                thumbRoot = animator.GetBoneTransform(HumanBodyBones.RightThumbProximal);
                fingers = new[]
                {
                    Chain(HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal),
                    Chain(HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal),
                    Chain(HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal),
                    Chain(HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal),
                };
            }
            if (!hand) hand = transform;
            if (!hip) hip = transform;
            katana = new GameObject("Katana").transform;
            katana.SetParent(transform, false);
            // Model space (Meshy GLB after import): pommel (-0.95, 0.58, 0), tip (0.95, -0.65, 0), grip near the pommel.
            Vector3 pommel = new Vector3(-.949f, .578f, 0), tipPoint = new Vector3(.952f, -.646f, 0);
            Vector3 gripPoint = Vector3.Lerp(pommel, tipPoint, .1f);
            float length = Vector3.Distance(pommel, tipPoint);
            var holder = new GameObject("Model").transform;
            holder.SetParent(katana, false);
            holder.localRotation = Quaternion.FromToRotation((tipPoint - pommel).normalized, Vector3.forward);
            holder.localScale = Vector3.one * (katanaLength / length);
            holder.localPosition = -(holder.localRotation * gripPoint) * (katanaLength / length);
            if (katanaModel)
            {
                var model = Instantiate(katanaModel, holder);
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
                foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            else
            {
                var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(blade.GetComponent<Collider>());
                blade.transform.SetParent(katana, false);
                blade.transform.localScale = new Vector3(.03f, .05f, katanaLength * .78f);
                blade.transform.localPosition = new Vector3(0, 0, katanaLength * .5f);
            }
            tip = new GameObject("Tip").transform;
            tip.SetParent(katana, false);
            tip.localPosition = new Vector3(0, 0, katanaLength * .92f);
            trail = tip.gameObject.AddComponent<TrailRenderer>();
            trail.time = .13f;
            trail.minVertexDistance = .03f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0, .3f), new Keyframe(1, 0));
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(HitFeedback.Sakura, .35f), new GradientColorKey(HitFeedback.Crimson, 1) },
                alphaKeys = new[] { new GradientAlphaKey(.95f, 0), new GradientAlphaKey(.55f, .5f), new GradientAlphaKey(0, 1) }
            };
            trail.sharedMaterial = HitFeedback.Additive;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.emitting = false;
        }

        Transform[] Chain(params HumanBodyBones[] bones)
        {
            var chain = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; i++) chain[i] = animator.GetBoneTransform(bones[i]);
            return chain;
        }
    }
}
