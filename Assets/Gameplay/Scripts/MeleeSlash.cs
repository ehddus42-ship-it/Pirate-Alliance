using System.Collections;
using UnityEngine;

namespace AcRoguelike
{
    /// <summary>
    /// Katana basic attack modelled on Yae Sakura (Honkai Impact 3rd, A-rank): a fast six-hit, full-body combo:
    /// three quick stepping cuts (ta-ta-tak), a pirouette cut, a low dash through the target cutting twice and
    /// an iai draw finisher that cuts twice. PlayerCombat owns input, combo timing and the Animator; this component holds the
    /// sword, carries the player by the motion's own steps (root motion) and a short dash toward the target, tests
    /// the hit arc, deals damage and plays the hit feedback. Motions are retargetable Humanoid clips (see
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
            [Tooltip("Dash hit: afterimages and a dust burst.")]
            public bool dash;
        }

        // Clip timing measured from the motion capture (sword-tip speed peaks), at the baked playback speed.
        public static readonly float[] Durations = { .225f, .213f, .258f, .345f, .506f, .655f };
        public static readonly float[] HitTimes = { .125f, .138f, .142f, .207f, .235f, .172f };

        // Arcs follow the measured sword-tip path around the hips during each cut (0 = toward the target, + = right;
        // roll lifts the right end).
        public Swing[] swings =
        {
            // 1-3: ta-ta-tak. Three quick stepping cuts from one flurry: low sweep, rising backhand, cut back across.
            new Swing { damage = 12, range = 2.6f, halfAngle = 80, impact = .8f, hitStop = .035f, shake = .03f,
                arcFrom = 84, arcTo = -88, arcRoll = -8, arcRadius = 1.8f },
            new Swing { damage = 12, range = 2.6f, halfAngle = 80, impact = .8f, hitStop = .035f, shake = .03f,
                arcFrom = -157, arcTo = 68, arcRoll = 12, arcRadius = 1.8f },
            new Swing { damage = 14, range = 2.7f, halfAngle = 80, impact = .95f, hitStop = .04f, shake = .04f,
                arcFrom = 128, arcTo = -67, arcRoll = -10, arcRadius = 1.9f },
            // 4: pirouette: one full turn with the blade out.
            new Swing { damage = 18, range = 2.8f, halfAngle = 180, impact = 1.1f, hitStop = .05f, shake = .06f,
                arcFrom = -153, arcTo = 190, arcRoll = 6, arcRadius = 2.1f },
            // 5: low dash through the target: a descending cut on the way in, then a low backhand.
            new Swing { damage = 15, range = 2.8f, halfAngle = 75, impact = 1.05f, hitStop = .045f, shake = .055f,
                arcFrom = 33, arcTo = -96, arcRoll = 38, arcRadius = 1.9f, dash = true,
                secondHitDelay = .218f, secondDamage = 17, secondArcFrom = -97, secondArcTo = 13, secondArcRoll = 20 },
            // 6: iai draw across the front, then a heavy diagonal cut that launches enemies.
            new Swing { damage = 22, range = 3.2f, halfAngle = 100, impact = 1.3f, hitStop = .07f, shake = .09f,
                arcFrom = -123, arcTo = 97, arcRoll = -10, arcRadius = 2.6f, finisher = true, dash = true,
                secondHitDelay = .276f, secondDamage = 38, secondArcFrom = 79, secondArcTo = -66, secondArcRoll = 35 },
        };
        [Tooltip("Targets closer than this are reached with a short dash at the start of a swing.")]
        public float lungeRange = 6f;
        public float lungeStop = 1.4f;
        public float lungeSpeed = 28f;
        [Tooltip("Scale of the motion's own steps (root motion) while attacking.")]
        public float rootMotionScale = 1f;
        [Range(0, 1), Tooltip("How much of the root motion is turned toward the attack direction.")]
        public float rootMotionSteer = .75f;
        [Tooltip("Root motion never carries the player closer than this to the target.")]
        public float minTargetGap = .95f;
        [Tooltip("Sword-tip speed (m/s) that switches the swing trail on, and the lower speed that switches it off.")]
        public float trailOnSpeed = 12f, trailOffSpeed = 7f;
        public float katanaLength = 1.05f;
        [Range(0, 1)] public float gripCurl = 1f;
        public GameObject katanaModel;

        public int HitCount { get; private set; }
        /// <summary>Counter window after a just dodge: cuts deal more damage and land as heavy hits.</summary>
        public bool CounterActive => Time.unscaledTime < counterUntil;
        public float counterMultiplier = 1.5f;
        [Tooltip("Permanent damage multiplier (lobby upgrades).")]
        public float damageMultiplier = 1f;
        [Tooltip("Base crit chance per target hit (0..1). Augments can change it.")]
        [Range(0, 1)] public float critChance = .1f;
        [Tooltip("Base crit damage multiplier. Augments can change it.")]
        public float critMultiplier = 1.5f;
        /// <summary>Hits that landed as crits this run (for tests and HUD).</summary>
        public int CritCount { get; private set; }
        float counterUntil;
        IMeleeHitHooks hooks;
        bool hooksSearched;

        /// <summary>Run modifiers (augments) on the player, looked up once; call after adding or removing them.</summary>
        public void RefreshHooks() => hooksSearched = false;

        IMeleeHitHooks Hooks
        {
            get
            {
                if (!hooksSearched) { hooks = GetComponent<IMeleeHitHooks>(); hooksSearched = true; }
                return hooks;
            }
        }

        bool weaponHidden;

        /// <summary>Hides the katana (lobby outfit) or shows it again.</summary>
        public void SetWeaponVisible(bool visible)
        {
            weaponHidden = !visible;
            EnsureKatana();
            if (katana) katana.gameObject.SetActive(visible);
            if (!visible) { CancelSecondHit(); EndSwing(); }
        }

        public void GrantCounter(float seconds) => counterUntil = Mathf.Max(counterUntil, Time.unscaledTime + seconds);
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
        SkinnedMeshRenderer[] skins;
        Vector3 lungeTarget, lastTip;
        float lungeUntil, sheatheAt, swingClock, nextGhost;
        int swingIndex = -1;
        bool drawn, tipValid;
        TrainingEnemy swingTarget;
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
                combat.animationBlend = .06f;
                combat.returnBlend = .22f;
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
            swingIndex = index;
            swingTarget = target;
            swingClock = 0;
            nextGhost = 0;
            lungeUntil = 0;
            var swing = swings[Mathf.Clamp(index, 0, swings.Length - 1)];
            if (swing.dash) HitFeedback.Dust(transform.position, .7f);
            HitFeedback.Play(HitFeedback.Sfx.Swing, swing.finisher ? .8f : .55f, swing.finisher ? .85f : 1.1f);
            if (target && target.IsAlive)
            {
                Vector3 to = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
                if (to.magnitude > lungeStop && to.magnitude < lungeRange)
                {
                    lungeTarget = target.transform.position - to.normalized * lungeStop;
                    // A fast dash in; the fast opening cuts may land before it ends, the arc range covers the rest.
                    lungeUntil = Time.time + Mathf.Min(.3f, (to.magnitude - lungeStop) / Mathf.Max(1, lungeSpeed));
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

        bool Cut(Swing swing, int baseDamage, float from, float to, float roll, TrainingEnemy preferred, Vector3 direction, bool second)
        {
            Vector3 chest = transform.position + Vector3.up * 1.05f;
            bool counter = CounterActive;
            var hooks = Hooks;
            baseDamage = Mathf.RoundToInt(baseDamage * Mathf.Max(.1f, damageMultiplier));
            // The combo's final cut: the finisher's second cut, or the finisher itself when it has only one.
            bool comboFinal = swing.finisher && (second || swing.secondHitDelay <= 0);
            bool heavy = counter || comboFinal;
            if (counter) baseDamage = Mathf.RoundToInt(baseDamage * (hooks != null ? hooks.CounterMultiplier(counterMultiplier) : counterMultiplier));
            bool launch = swing.finisher && second;
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
                HitFeedback.Petals(chest, 30);
                HitFeedback.ScreenFlash(new Color(1f, .62f, .82f, .32f), .2f);
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
                float multiplier = hooks != null ? hooks.DamageMultiplier(enemy, comboFinal) : 1f;
                float chance = hooks != null ? hooks.CritChance(critChance) : critChance;
                bool crit = chance > 0 && Random.value < chance;
                if (crit) multiplier *= hooks != null ? hooks.CritMultiplier(critMultiplier) : critMultiplier;
                int damage = Mathf.Max(1, Mathf.RoundToInt((baseDamage + Random.Range(-2, 3)) * multiplier));
                bool lethal = damage >= enemy.Health;
                enemy.TakeDamage(damage);
                HitFeedback.Hit(enemy, point, push, damage, heavy || lethal || crit, crit);
                if (crit)
                {
                    CritCount++;
                    hooks?.OnCrit(enemy);
                }
                // The cut itself, drawn across the target along the swing's screen tilt.
                if (heavy) HitFeedback.CrossSlash(point, 1.1f);
                else HitFeedback.SlashLine(point, ScreenAngle(direction, from, to, roll), 2.1f, .1f, HitFeedback.Sakura * 2.2f, .18f);
                hitAny = true;
                if (!first || enemy == preferred) first = enemy;
            }
            if (hitAny)
            {
                HitCount++;
                LastHitTarget = first;
                hooks?.OnCutLanded(first, comboFinal);
                HitFeedback.HitStop(heavy ? swing.hitStop * 1.6f : swing.hitStop, heavy ? .04f : .08f);
                HitFeedback.Shake(heavy ? swing.shake * 1.8f : swing.shake, heavy ? .24f : .12f);
            }
            // A swing's second cut belongs to the same swing, so only the first cut counts as a cast.
            if (caster && first && !second) caster.RegisterMeleeHit(first);
            return hitAny;
        }

        /// <summary>Called when a swing ends or is cancelled (the iai's second cut still lands if the swing completed).</summary>
        public void EndSwing()
        {
            lungeUntil = 0;
            swingIndex = -1;
            swingTarget = null;
            if (trail) trail.emitting = false;
            sheatheAt = Time.time + 1.6f;
        }

        /// <summary>
        /// The combo clips keep the motion's horizontal travel as root motion (MeleeRootMotion forwards it here), so
        /// the steps and the dash move the CharacterController and the feet stay planted. Travel stops short of the
        /// target and is skipped while the lunge or a dodge moves the player.
        /// </summary>
        internal void ApplyRootMotion(Vector3 delta)
        {
            if (swingIndex < 0 || !(combat && combat.IsAttacking) || !body || !body.enabled) return;
            if ((motor && motor.IsDashing) || lungeUntil > Time.time) return;
            delta = Vector3.ProjectOnPlane(delta, Vector3.up) * rootMotionScale;
            if (delta.sqrMagnitude < 1e-8f) return;
            // The captured steps wander with the body turn; steer most of them toward the attack so a dash closes in.
            Vector3 attack = combat.AttackDirection.sqrMagnitude > .01f ? combat.AttackDirection.normalized : Body.forward;
            delta = Vector3.Lerp(delta.normalized, attack, rootMotionSteer).normalized * delta.magnitude;
            if (swingTarget && swingTarget.IsAlive)
            {
                Vector3 to = Vector3.ProjectOnPlane(swingTarget.transform.position - transform.position, Vector3.up);
                float gap = minTargetGap + EnemyRadius(swingTarget);
                if (to.sqrMagnitude > .0001f)
                {
                    Vector3 toward = to.normalized;
                    float approach = Vector3.Dot(delta, toward);
                    float room = Mathf.Max(0, to.magnitude - gap);
                    if (approach > room) delta -= toward * (approach - room);
                }
            }
            // Keep a little downward push so the move never reads as leaving the ground.
            MoveBody(delta + Vector3.down * .02f);
        }

        /// <summary>Screen tilt of a cut: level for a flat sweep, steep for a diagonal one, mirrored by the sweep side.</summary>
        float ScreenAngle(Vector3 direction, float from, float to, float roll)
        {
            var camera = Camera.main;
            float sweep = Mathf.Sign(to - from);
            float tilt = Mathf.Clamp(roll, -50, 50) + 8 * sweep;
            if (!camera) return tilt;
            // Seen from the side the cut runs across the screen; seen along its direction it reads as a diagonal.
            float side = Vector3.Dot(camera.transform.right, direction);
            return tilt * (side >= 0 ? 1 : -1);
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
            if (swingIndex >= 0)
            {
                swingClock += Time.deltaTime * (combat ? combat.AnimationSpeed : 1);
                var swing = swings[Mathf.Clamp(swingIndex, 0, swings.Length - 1)];
                float hit = HitTimes[Mathf.Clamp(swingIndex, 0, HitTimes.Length - 1)];
                float end = hit + Mathf.Max(.1f, swing.secondHitDelay + .08f);
                // Afterimages: dense through the dash, the draw and any lunge; a faint trail on every other cut.
                bool strong = swing.dash || lungeUntil > Time.time;
                if (swingClock > hit - .2f && swingClock < end && Time.time >= nextGhost)
                {
                    nextGhost = Time.time + (strong ? .03f : .06f);
                    if (skins == null) skins = Body.GetComponentsInChildren<SkinnedMeshRenderer>();
                    HitFeedback.Afterimage(skins, (swing.finisher ? HitFeedback.Violet : HitFeedback.Sakura) * (strong ? .55f : .28f), strong ? .28f : .18f);
                }
            }
            if (lungeUntil > Time.time && body && body.enabled)
            {
                Vector3 delta = Vector3.ProjectOnPlane(lungeTarget - transform.position, Vector3.up);
                float step = lungeSpeed * Time.deltaTime;
                if (delta.magnitude <= step) { MoveBody(delta + Vector3.down * .02f); lungeUntil = 0; }
                else MoveBody(delta.normalized * step + Vector3.down * .02f);
            }
            if (drawn && !(combat && combat.IsAttacking) && secondHit == null && Time.time > sheatheAt) Draw(false);
        }

        void MoveBody(Vector3 displacement)
        {
            if (motor) motor.MoveWithSliding(displacement);
            else body.Move(displacement);
        }

        void LateUpdate()
        {
            if (!katana || weaponHidden) return;
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
                UpdateTrail();
            }
            else if (hip)
            {
                tipValid = false;
                // Sheathed at the left hip: edge up, tip pointing back and slightly down.
                Vector3 back = -Body.forward, side = -Body.right;
                Vector3 dir = (back * .85f + Vector3.down * .3f + side * .1f).normalized;
                katana.SetPositionAndRotation(hip.position + side * .22f + Body.forward * .3f + Vector3.up * .02f,
                    Quaternion.LookRotation(dir, Vector3.up));
            }
        }

        /// <summary>The trail shows only while the blade actually cuts (fast tip), never as a constant glow on the tip.</summary>
        void UpdateTrail()
        {
            if (!trail || !tip) return;
            Vector3 position = tip.position;
            float dt = Time.deltaTime;
            float speed = tipValid && dt > 1e-5f ? Vector3.Distance(position, lastTip) / dt : 0;
            lastTip = position;
            tipValid = true;
            bool swinging = swingIndex >= 0 && combat && combat.IsAttacking;
            if (!swinging) { trail.emitting = false; return; }
            if (trail.emitting) { if (speed < trailOffSpeed) trail.emitting = false; }
            else if (speed > trailOnSpeed) trail.emitting = true;
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
            if (animator && !animator.GetComponent<MeleeRootMotion>()) animator.gameObject.AddComponent<MeleeRootMotion>().owner = this;
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
            trail.time = .09f;
            trail.minVertexDistance = .02f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0, .42f), new Keyframe(.4f, .22f), new Keyframe(1, 0));
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

    /// <summary>Forwards the Animator's root motion to MeleeSlash (lives next to the Animator).</summary>
    [DisallowMultipleComponent]
    sealed class MeleeRootMotion : MonoBehaviour
    {
        public MeleeSlash owner;
        Animator animator;

        void Awake() => animator = GetComponent<Animator>();

        void OnAnimatorMove()
        {
            if (owner && animator) owner.ApplyRootMotion(animator.deltaPosition);
        }
    }
}
