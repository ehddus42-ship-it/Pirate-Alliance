using UnityEngine;

namespace AcRoguelike
{
    /// <summary>
    /// Katana basic attack in the style of an iaido shrine maiden (fast three-hit combo, crimson / sakura slash arcs,
    /// heavy finisher). PlayerCombat keeps owning input, combo timing and animation; this component handles the
    /// sword, the short lunge toward the target, the hit test (arc in front of the player), damage, knockback
    /// direction and all hit feedback. Each resolved swing with a target is registered on TalismanCaster as a cast,
    /// so systems that count casts (validation, the support skill) keep working.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MeleeSlash : MonoBehaviour
    {
        [System.Serializable]
        public struct Swing
        {
            public int damage;
            public float range, halfAngle, impact, hitStop, shake;
            public float arcFrom, arcTo, arcRoll, arcRadius;
            public bool finisher;
        }

        public Swing[] swings =
        {
            // Right-to-left horizontal cut.
            new Swing { damage = 22, range = 2.5f, halfAngle = 70, impact = 1f, hitStop = .055f, shake = .05f,
                arcFrom = 75, arcTo = -95, arcRoll = 12, arcRadius = 1.9f },
            // Rising backhand.
            new Swing { damage = 24, range = 2.5f, halfAngle = 70, impact = 1.05f, hitStop = .06f, shake = .06f,
                arcFrom = -80, arcTo = 90, arcRoll = -38, arcRadius = 2.0f },
            // Iai finisher: a wide draw-cut in a full crescent, launches enemies.
            new Swing { damage = 44, range = 3.3f, halfAngle = 115, impact = 1.9f, hitStop = .11f, shake = .16f,
                arcFrom = -150, arcTo = 150, arcRoll = 4, arcRadius = 2.7f, finisher = true },
        };
        [Tooltip("Targets closer than this are reached with a short dash at the start of a swing.")]
        public float lungeRange = 6f;
        public float lungeStop = 1.35f;
        public float lungeSpeed = 16f;
        public float katanaLength = 1.05f;
        public GameObject katanaModel;

        public int HitCount { get; private set; }
        public TrainingEnemy LastHitTarget { get; private set; }

        PlayerCombat combat;
        PlayerMotor motor;
        TalismanCaster caster;
        CharacterController body;
        Transform katana, tip, hand, hip;
        TrailRenderer trail;
        Vector3 lungeTarget;
        float lungeUntil, sheatheAt;
        bool drawn;

        void Awake()
        {
            combat = GetComponent<PlayerCombat>();
            motor = GetComponent<PlayerMotor>();
            caster = GetComponent<TalismanCaster>();
            body = GetComponent<CharacterController>();
            if (caster) caster.castRange = Mathf.Min(caster.castRange, lungeRange + .5f);
            // Snappy katana cadence (the talisman throw was slower).
            if (combat)
            {
                combat.attackDurations = new[] { .44f, .48f, .66f };
                combat.hitTimes = new[] { .15f, .17f, .30f };
                combat.attackMovementMultiplier = .08f;
            }
            if (!katanaModel) katanaModel = Resources.Load<GameObject>("AstraiaKatana");
        }

        void Start() => EnsureKatana();

        /// <summary>Called by PlayerCombat when a swing starts.</summary>
        public void BeginSwing(int index, TrainingEnemy target, Vector3 direction)
        {
            EnsureKatana();
            Draw(true);
            if (trail) { trail.Clear(); trail.emitting = true; }
            lungeUntil = 0;
            if (target && target.IsAlive)
            {
                Vector3 to = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
                if (to.magnitude > lungeStop && to.magnitude < lungeRange)
                {
                    lungeTarget = target.transform.position - to.normalized * lungeStop;
                    lungeUntil = Time.time + (combat ? combat.hitTimes[Mathf.Clamp(index, 0, 2)] : .15f) * .9f;
                }
            }
        }

        /// <summary>Called by PlayerCombat at the swing's hit time. Returns true when at least one enemy was cut.</summary>
        public bool ResolveSwing(int index, TrainingEnemy preferred, Vector3 direction)
        {
            var swing = swings[Mathf.Clamp(index, 0, swings.Length - 1)];
            direction = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (direction.sqrMagnitude < .01f) direction = transform.forward;
            direction.Normalize();
            Vector3 chest = transform.position + Vector3.up * 1.05f;

            // Slash arc first, so even a miss reads as a cut.
            HitFeedback.SlashArc(chest, direction, swing.arcFrom, swing.arcTo, swing.arcRadius, swing.arcRoll,
                swing.finisher ? .9f : .62f, swing.finisher ? HitFeedback.Sakura * 2.4f : HitFeedback.Sakura * 1.8f,
                swing.finisher ? HitFeedback.Violet * 1.6f : HitFeedback.Crimson * 1.4f, swing.finisher ? .09f : .06f, swing.finisher ? .3f : .2f);
            HitFeedback.SlashArc(chest, direction, swing.arcFrom * .8f, swing.arcTo * .8f, swing.arcRadius * .78f, swing.arcRoll + 8,
                .32f, Color.white * 1.6f, HitFeedback.Sakura, swing.finisher ? .08f : .05f, .12f);
            if (swing.finisher)
            {
                HitFeedback.SlashArc(transform.position + Vector3.up * .08f, direction, -180, 180, swing.range, 0, .5f,
                    HitFeedback.Crimson * 1.5f, HitFeedback.Violet, .12f, .35f);
                HitFeedback.Petals(chest, 16);
            }

            bool hitAny = false;
            TrainingEnemy first = null;
            foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
            {
                if (!enemy.IsAlive || !enemy.CanBeTargeted) continue;
                Vector3 to = Vector3.ProjectOnPlane(enemy.transform.position - transform.position, Vector3.up);
                float reach = swing.range + EnemyRadius(enemy);
                bool inArc = to.magnitude <= reach && (to.magnitude < .6f || Vector3.Angle(direction, to) <= swing.halfAngle);
                if (!inArc && enemy != preferred) continue;
                if (!inArc && to.magnitude > reach + .6f) continue; // the lunge target may sit just outside the arc
                Vector3 push = to.sqrMagnitude > .01f ? to.normalized : direction;
                enemy.LastHitDirection = push;
                enemy.LastHitImpact = swing.impact;
                Vector3 point = enemy.AimPoint - push * .25f;
                int damage = swing.damage + Random.Range(-2, 3);
                bool lethal = damage >= enemy.Health;
                enemy.TakeDamage(damage);
                HitFeedback.Hit(enemy, point, push, damage, swing.finisher || lethal);
                hitAny = true;
                if (!first || enemy == preferred) first = enemy;
            }
            if (hitAny)
            {
                HitCount++;
                LastHitTarget = first;
                HitFeedback.HitStop(swing.hitStop, swing.finisher ? .04f : .08f);
                HitFeedback.Shake(swing.shake, swing.finisher ? .22f : .12f);
            }
            if (caster && first) caster.RegisterMeleeHit(first);
            return hitAny;
        }

        /// <summary>Called when a swing ends or is cancelled.</summary>
        public void EndSwing()
        {
            lungeUntil = 0;
            if (trail) trail.emitting = false;
            sheatheAt = Time.time + 1.4f;
        }

        static float EnemyRadius(TrainingEnemy enemy)
        {
            var controller = enemy.GetComponent<CharacterController>();
            return controller ? controller.radius : .45f;
        }

        void Update()
        {
            if (lungeUntil > Time.time && body && body.enabled && !(motor && motor.IsDashing))
            {
                Vector3 delta = Vector3.ProjectOnPlane(lungeTarget - transform.position, Vector3.up);
                float step = lungeSpeed * Time.deltaTime;
                if (delta.magnitude <= step) { body.Move(delta); lungeUntil = 0; }
                else body.Move(delta.normalized * step);
            }
            if (drawn && !(combat && combat.IsAttacking) && Time.time > sheatheAt) Draw(false);
        }

        void LateUpdate()
        {
            if (!katana) return;
            // Hold the sword after the animation pose is applied: grip in the right hand, blade continuing
            // the forearm; sheathed it hangs at the left hip, edge up, pointing back.
            if (drawn && hand)
            {
                var elbow = hand.parent;
                Vector3 along = elbow ? (hand.position - elbow.position).normalized : transform.forward;
                Vector3 up = Vector3.Cross(along, transform.right);
                if (up.sqrMagnitude < .01f) up = Vector3.up;
                katana.SetPositionAndRotation(hand.position + along * .05f, Quaternion.LookRotation(along, up));
            }
            else if (hip)
            {
                Vector3 back = -transform.forward;
                Vector3 side = -transform.right;
                Vector3 dir = (back * .85f + Vector3.down * .35f).normalized;
                katana.SetPositionAndRotation(hip.position + side * .2f + transform.forward * .22f, Quaternion.LookRotation(dir, Vector3.up));
            }
        }

        void Draw(bool draw)
        {
            if (draw == drawn) return;
            drawn = draw;
            if (draw && katana)
                HitFeedback.Petals(katana.position, 4);
        }

        void EnsureKatana()
        {
            if (katana) return;
            var animator = motor ? motor.animator : GetComponentInChildren<Animator>();
            if (animator && animator.isHuman)
            {
                hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                hip = animator.GetBoneTransform(HumanBodyBones.Hips);
            }
            if (!hand) hand = transform;
            if (!hip) hip = transform;
            katana = new GameObject("Katana").transform;
            katana.SetParent(transform, false);
            // Model space (Meshy GLB after import): pommel (-0.95, 0.58, 0), tip (0.95, -0.65, 0), grip near the pommel.
            Vector3 pommel = new Vector3(-.949f, .578f, 0), tipPoint = new Vector3(.952f, -.646f, 0);
            Vector3 grip = Vector3.Lerp(pommel, tipPoint, .1f);
            float length = Vector3.Distance(pommel, tipPoint);
            var holder = new GameObject("Model").transform;
            holder.SetParent(katana, false);
            holder.localRotation = Quaternion.FromToRotation((tipPoint - pommel).normalized, Vector3.forward);
            holder.localScale = Vector3.one * (katanaLength / length);
            holder.localPosition = -(holder.localRotation * grip) * (katanaLength / length);
            if (katanaModel)
            {
                var model = Instantiate(katanaModel, holder);
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
                foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            else
            {
                // Fallback blade if the model is missing.
                var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(blade.GetComponent<Collider>());
                blade.transform.SetParent(katana, false);
                blade.transform.localScale = new Vector3(.03f, .05f, katanaLength * .78f);
                blade.transform.localPosition = new Vector3(0, 0, katanaLength * .5f);
            }
            tip = new GameObject("Tip").transform;
            tip.SetParent(katana, false);
            tip.localPosition = new Vector3(0, 0, katanaLength * .9f);
            trail = tip.gameObject.AddComponent<TrailRenderer>();
            trail.time = .12f;
            trail.minVertexDistance = .03f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0, .28f), new Keyframe(1, 0));
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(HitFeedback.Sakura, .35f), new GradientColorKey(HitFeedback.Crimson, 1) },
                alphaKeys = new[] { new GradientAlphaKey(.95f, 0), new GradientAlphaKey(.55f, .5f), new GradientAlphaKey(0, 1) }
            };
            trail.sharedMaterial = HitFeedback.Additive;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.emitting = false;
        }
    }
}
