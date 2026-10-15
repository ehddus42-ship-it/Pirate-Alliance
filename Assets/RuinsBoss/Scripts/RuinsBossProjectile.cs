using AcRoguelike.Liminal;
using AcRoguelike.Ruins;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.RuinsBoss
{
    public enum RuinsBossProjectileKind { Electric, HomingMissile, Artillery }

    /// <summary>Committed electric lanes, gently guided side missiles and warned overhead artillery.</summary>
    [DefaultExecutionOrder(30)]
    public sealed class RuinsBossProjectile : MonoBehaviour
    {
        public const float HomingSeconds = .9f, HomingDegreesPerSecond = 10f;
        public const float ArtilleryWarningSeconds = 1.8f / ProjectileTuning.SpeedMultiplier, ArtilleryRadius = 2.1f;
        public const float MaximumTravel = 28f;
        const float MaximumAge = 9f / ProjectileTuning.SpeedMultiplier;
        public RuinsBossProjectileKind Kind { get; private set; }
        public bool Finished { get; private set; }
        public bool HitPlayer => Kind == RuinsBossProjectileKind.Artillery && !ReferenceEquals(impact, null) ? impact.HitPlayer : hitPlayer;
        public int DamageAttempts => Kind == RuinsBossProjectileKind.Artillery && !ReferenceEquals(impact, null) ? impact.DamageAttempts : damageAttempts;
        public int Damage { get; private set; }
        public float Speed { get; private set; }
        public float Travelled { get; private set; }
        public float Age => age;
        public float CollisionRadius => Kind == RuinsBossProjectileKind.Electric ? .19f : .25f;
        public Vector3 Direction => direction;
        public Vector3 LockedPoint { get; private set; }
        public bool WarningVisible => impact && impact.WarningVisible;
        public bool UsesMeshyMissile { get; private set; }
        public RuinsBossImpact Impact => impact;

        TrainingEnemy owner;
        LiminalPlayerHealth target;
        LiminalRoom room;
        RuinsStormBoss ownerBoss;
        RuinsWarMachine ownerMachine;
        bool hadRoom, hadBrain, preserveImpact, hitPlayer;
        int damageAttempts;
        Vector3 direction, launchOrigin;
        float age, firedAt;
        Transform visual;
        LineRenderer tail;
        RuinsBossImpact impact;
        readonly RaycastHit[] hits = new RaycastHit[128];
        readonly Collider[] overlaps = new Collider[128];
        static Material electricCore;

        public static RuinsBossProjectile FireElectric(Vector3 position, Vector3 direction, TrainingEnemy owner,
            LiminalPlayerHealth target, LiminalRoom room, int damage, float speed = 7.5f)
            => Create(position, direction, owner, target, room, damage, speed, RuinsBossProjectileKind.Electric);

        public static RuinsBossProjectile FireHoming(Vector3 position, Vector3 direction, TrainingEnemy owner,
            LiminalPlayerHealth target, LiminalRoom room, int damage, float speed = 6.2f)
            => Create(position, direction, owner, target, room, damage, speed, RuinsBossProjectileKind.HomingMissile);

        public static RuinsBossProjectile LaunchArtillery(Vector3 origin, Vector3 lockedPoint, TrainingEnemy owner,
            LiminalPlayerHealth target, LiminalRoom room, int damage)
        {
            if (room)
            {
                Vector3 local = room.transform.InverseTransformPoint(lockedPoint);
                local.x = Mathf.Clamp(local.x, room.localBounds.min.x + ArtilleryRadius, room.localBounds.max.x - ArtilleryRadius);
                local.z = Mathf.Clamp(local.z, room.localBounds.min.z + ArtilleryRadius, room.localBounds.max.z - ArtilleryRadius);
                lockedPoint = room.transform.TransformPoint(local);
            }
            var shot = Create(origin, Vector3.up, owner, target, room, damage, 0, RuinsBossProjectileKind.Artillery);
            shot.launchOrigin = origin;
            shot.LockedPoint = lockedPoint;
            float top = Mathf.Max(origin.y, lockedPoint.y) + 6;
            shot.Speed = ProjectileTuning.ScaleSpeed(2 * (top - origin.y) / .65f);
            shot.impact = RuinsBossImpact.Create(lockedPoint, ArtilleryRadius, ArtilleryWarningSeconds, owner, target, room, damage);
            return shot;
        }

        static RuinsBossProjectile Create(Vector3 position, Vector3 direction, TrainingEnemy owner,
            LiminalPlayerHealth target, LiminalRoom room, int damage, float speed, RuinsBossProjectileKind kind)
        {
            var go = new GameObject("Ruins boss " + kind);
            go.transform.SetParent(room ? room.transform : owner ? owner.transform.parent : null, true);
            go.transform.position = position;
            var shot = go.AddComponent<RuinsBossProjectile>();
            shot.owner = owner; shot.target = target; shot.room = room; shot.hadRoom = room;
            if (owner)
            {
                shot.ownerBoss = owner.GetComponent<RuinsStormBoss>();
                shot.ownerMachine = owner.GetComponent<RuinsWarMachine>();
                shot.hadBrain = shot.ownerBoss || shot.ownerMachine;
            }
            shot.firedAt = Time.time;
            shot.Kind = kind; shot.Damage = Mathf.Max(1, damage);
            shot.Speed = kind == RuinsBossProjectileKind.Artillery ? 0 : ProjectileTuning.ScaleSpeed(Mathf.Max(.1f, speed));
            if (kind != RuinsBossProjectileKind.Artillery) direction.y = 0;
            shot.direction = direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;
            go.transform.rotation = Quaternion.LookRotation(shot.direction);
            shot.BuildVisual();
            if (owner) owner.Defeated += shot.OwnerDefeated;
            if (target) target.Died += shot.Cancel;
            return shot;
        }

        void BuildVisual()
        {
            visual = new GameObject("Projectile visual").transform;
            visual.SetParent(transform, false);
            if (Kind == RuinsBossProjectileKind.Electric)
            {
                if (!electricCore) electricCore = RuinsVisual.Material("Ruins electric plasma", new Color(.25f, .86f, 1), .1f, .6f, true);
                RuinsVisual.Part(visual, "White electric core", PrimitiveType.Sphere, Vector3.zero,
                    new Vector3(.30f, .30f, .36f), electricCore);
                tail = Line(visual, "Crackling arc", 9, .035f, new Color(.28f, .88f, 1, .95f));
            }
            else
            {
                var prefab = Resources.Load<GameObject>("RuinsBoss/MissileVisual");
                if (prefab)
                {
                    var missile = Instantiate(prefab, visual);
                    missile.name = "Meshy missile";
                    foreach (var collider in missile.GetComponentsInChildren<Collider>(true))
                    { collider.enabled = false; Destroy(collider); }
                    UsesMeshyMissile = true;
                }
                else
                {
                    RuinsVisual.Part(visual, "Missile casing", PrimitiveType.Capsule, Vector3.zero,
                        new Vector3(.22f, .42f, .22f), RuinsVisual.Iron).localRotation = Quaternion.Euler(90, 0, 0);
                    RuinsVisual.Part(visual, "Missile nose", PrimitiveType.Sphere, Vector3.forward * .36f,
                        new Vector3(.19f, .19f, .26f), RuinsVisual.Brass);
                }
                RuinsVisual.Part(visual, "Exhaust throat", PrimitiveType.Sphere, Vector3.back * .44f,
                    new Vector3(.14f, .14f, .28f), RuinsVisual.Amber);
                tail = Line(visual, "Missile exhaust", 7, .10f, new Color(1, .52f, .12f, .8f));
            }
            AnimateVisual();
        }

        static LineRenderer Line(Transform parent, string name, int count, float width, Color color)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false; line.positionCount = count; line.widthMultiplier = width;
            line.widthCurve = AnimationCurve.Linear(0, 1, 1, 0);
            line.startColor = color; line.endColor = new Color(color.r, color.g, color.b, 0);
            line.sharedMaterial = HitFeedback.Additive;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        void Update()
        {
            if (Finished) return;
            if (!owner || !owner.IsAlive || !owner.isActiveAndEnabled || !target || !target.IsAlive || !target.isActiveAndEnabled
                || (hadBrain && !ownerBoss && !ownerMachine)
                || (ownerBoss && (ownerBoss.EncounterCancelled || !ownerBoss.isActiveAndEnabled))
                || (ownerMachine && (ownerMachine.EncounterCancelled || !ownerMachine.isActiveAndEnabled))
                || (hadRoom && !room) || (room && !room.gameObject.activeInHierarchy)) { Cancel(); return; }
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            if (Kind == RuinsBossProjectileKind.Artillery)
            {
                // Use the same scaled clock as the warning; birth-frame Update order cannot
                // detonate the target while the visual missile is still in mid-air.
                float previousAge = age;
                age = Mathf.Max(0, Time.time - firedAt);
                FlyArtillery(age - previousAge);
                if (!impact || impact.Finished || impact.Exploded)
                {
                    if (impact)
                    {
                        preserveImpact = impact.Exploded;
                    }
                    Cancel();
                }
                else if (age > ArtilleryWarningSeconds + .5f) Cancel();
            }
            else
            {
                // Small substeps keep the gentle curved guidance and sweep stable across frame rates.
                float remaining = Mathf.Min(dt, Mathf.Max(0, MaximumAge - age));
                while (remaining > .00001f && !Finished)
                {
                    float step = Mathf.Min(remaining, 1f / 60f);
                    if (Kind == RuinsBossProjectileKind.HomingMissile && age < HomingSeconds)
                    {
                        Vector3 desired = target.transform.position - transform.position; desired.y = 0;
                        if (desired.sqrMagnitude > .01f)
                            direction = Vector3.RotateTowards(direction, desired.normalized,
                                HomingDegreesPerSecond * Mathf.Deg2Rad * Mathf.Min(step, HomingSeconds - age), 0).normalized;
                    }
                    age += step; remaining -= step;
                    Advance(Mathf.Min(Speed * step, MaximumTravel - Travelled));
                }
                if (!Finished && (age >= MaximumAge || Travelled >= MaximumTravel - .0001f)) Cancel();
            }
            if (!Finished) AnimateVisual();
        }

        void Advance(float distance)
        {
            if (distance <= .000001f) { Cancel(); return; }
            if (room && !room.Contains(transform.position)) { Cancel(); return; }
            transform.rotation = Quaternion.LookRotation(direction);
            if (RuinsSweep.Cast(gameObject.scene.GetPhysicsScene(), room, transform.position, CollisionRadius, direction,
                distance, target, hits, overlaps, out var hit))
            {
                transform.position += direction * Mathf.Max(0, hit.distance);
                Travelled += Mathf.Max(0, hit.distance);
                if (hit.player) { damageAttempts++; hitPlayer = hit.player.TakeDamage(Damage); }
                HitFeedback.Sparks(transform.position, hit.normal, Kind == RuinsBossProjectileKind.Electric ? 4 : 10,
                    Kind == RuinsBossProjectileKind.Electric ? .3f : .6f);
                Cancel();
                return;
            }
            transform.position += direction * distance; Travelled += distance;
            if (Travelled >= MaximumTravel - .0001f) Cancel();
        }

        void FlyArtillery(float elapsed)
        {
            Vector3 before = transform.position;
            float top = Mathf.Max(launchOrigin.y, LockedPoint.y) + 6;
            // Retain the arc and phase proportions while extending flight and impact together.
            float flightAge = age * ProjectileTuning.SpeedMultiplier;
            if (flightAge < .65f)
            {
                float t = Mathf.Clamp01(flightAge / .65f);
                transform.position = Vector3.Lerp(launchOrigin, new Vector3(launchOrigin.x, top, launchOrigin.z), 1 - (1 - t) * (1 - t));
            }
            else if (flightAge < 1.05f)
            {
                float t = Mathf.SmoothStep(0, 1, (flightAge - .65f) / .4f);
                transform.position = Vector3.Lerp(new Vector3(launchOrigin.x, top, launchOrigin.z),
                    new Vector3(LockedPoint.x, top, LockedPoint.z), t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .7f);
            }
            else
            {
                float t = Mathf.Clamp01((flightAge - 1.05f) / .75f);
                transform.position = Vector3.Lerp(new Vector3(LockedPoint.x, top, LockedPoint.z), LockedPoint + Vector3.up * .12f, t * t);
            }
            Vector3 travel = transform.position - before;
            if (elapsed > 0) Speed = travel.magnitude / elapsed;
            Travelled += travel.magnitude;
            if (travel.sqrMagnitude > .000001f) { direction = travel.normalized; transform.rotation = Quaternion.LookRotation(direction); }
            if (age >= ArtilleryWarningSeconds && visual) visual.gameObject.SetActive(false);
        }

        void AnimateVisual()
        {
            if (!tail) return;
            for (int i = 0; i < tail.positionCount; i++)
            {
                float t = i / (tail.positionCount - 1f);
                float wave = Kind == RuinsBossProjectileKind.Electric ? .085f : .028f;
                tail.SetPosition(i, new Vector3(Mathf.Sin(age * 68 + i * 4.7f) * wave * t,
                    Mathf.Cos(age * 49 + i * 3.1f) * wave * t, -(Kind == RuinsBossProjectileKind.Electric ? .12f : .43f) - t * .85f));
            }
        }

        void OwnerDefeated(TrainingEnemy _) => Cancel();
        public void Cancel()
        {
            if (Finished) return;
            Finished = true;
            if (impact && !preserveImpact) impact.Cancel();
            Unsubscribe();
            gameObject.SetActive(false); Destroy(gameObject);
        }
        void Unsubscribe()
        {
            if (owner) owner.Defeated -= OwnerDefeated;
            if (target) target.Died -= Cancel;
        }
        void OnDisable() { if (!Finished) Cancel(); }
        void OnDestroy() { Unsubscribe(); if (impact && !preserveImpact) impact.Cancel(); }
    }
}
