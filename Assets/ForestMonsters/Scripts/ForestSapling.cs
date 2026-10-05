using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.Forest
{
    public enum ForestSaplingState { Thrown, Dormant, Chasing, Exploding, Finished }

    [RequireComponent(typeof(TrainingEnemy), typeof(CharacterController))]
    public sealed class ForestSapling : MonoBehaviour
    {
        public float armDelay = 2.6f, chaseDuration = 4.2f, explosionWindup = .8f, explosionRadius = 2.55f;
        public int explosionDamage = 23;
        public ForestSaplingState State { get; private set; }
        public float StateTime => elapsed;
        public int DamageAttempts { get; private set; }
        public bool Exploded { get; private set; }
        public bool DestroyedByPlayer { get; private set; }
        public Vector3 LandingNormal { get; private set; }
        public Vector3 LandingPoint { get; private set; }
        public string LandingCollider { get; private set; }
        public bool Finished => State == ForestSaplingState.Finished;
        public TrainingEnemy Health { get; private set; }
        TrainingEnemy owner; LiminalPlayerHealth target; LiminalRoom room;
        CharacterController body; CharacterObstacleSlide movement; Transform pose;
        Vector3 launch, landing; float elapsed, stepPhase; bool hadRoom;
        Telegraph warning;
        readonly RaycastHit[] hits = new RaycastHit[96]; readonly Collider[] overlaps = new Collider[96];
        const float FlightTime = .85f;

        public static ForestSapling Throw(Vector3 start, Vector3 end, TrainingEnemy owner, LiminalPlayerHealth target, LiminalRoom room, int stage)
        {
            var prefab = Resources.Load<GameObject>("ForestMonsters/VolatileSapling");
            if (!prefab) { Debug.LogError("Missing forest sapling prefab: Resources/ForestMonsters/VolatileSapling"); return null; }
            var go = Instantiate(prefab, start, Quaternion.identity, room ? room.transform : owner.transform.parent);
            var sapling = go.GetComponent<ForestSapling>();
            if (!sapling) { Debug.LogError("VolatileSapling prefab needs ForestSapling.", go); Destroy(go); return null; }
            sapling.Initialize(start, end, owner, target, room, stage); return sapling;
        }

        public void Initialize(Vector3 start, Vector3 end, TrainingEnemy source, LiminalPlayerHealth player, LiminalRoom ownerRoom, int stage)
        {
            owner = source; target = player; room = ownerRoom; hadRoom = room; launch = start; landing = end;
            Health = GetComponent<TrainingEnemy>(); Health.Configure(18 + stage * 3, false); Health.deferDeathVisuals = true;
            Health.CanBeTargeted = false; Health.IgnoreDamage = true; Health.Defeated += OnKilled;
            body = GetComponent<CharacterController>(); body.enabled = false;
            pose = transform.Find("Visual/Pose");
            movement = new CharacterObstacleSlide(body, p => !room || room.Contains(p, .45f));
            warning = Telegraph.Create(transform, "Sapling warning");
            State = ForestSaplingState.Thrown; elapsed = 0;
            if (owner) owner.Defeated += OnOwnerDeath; if (target) target.Died += Cancel;
        }

        void Update()
        {
            if (Finished || !Health) return;
            if (!ForestAttackUtility.Alive(owner, target, room, hadRoom)) { Cancel(); return; }
            float dt = Time.deltaTime; if (dt <= 0) return; elapsed += dt;
            if (State == ForestSaplingState.Thrown)
            {
                float t = Mathf.Clamp01(elapsed / FlightTime);
                Vector3 destination = Vector3.Lerp(launch, landing, t) + Vector3.up * (2f * Mathf.Sin(t * Mathf.PI));
                Vector3 delta = destination - transform.position;
                if (delta.sqrMagnitude > .0001f && ForestAttackUtility.Sweep(gameObject.scene.GetPhysicsScene(), room,
                    transform.position + Vector3.up * .3f, .24f, delta.normalized, delta.magnitude, null, hits, overlaps, out var hit, true))
                {
                    Vector3 stop = transform.position + delta.normalized * Mathf.Max(0, hit.distance - .04f);
                    LandingNormal = hit.normal; LandingPoint = hit.point; LandingCollider = hit.collider ? hit.collider.name : "room boundary";
                    // The surface point is authoritative. Sphere center offsets and diagonal approach lengths
                    // are unsuitable for estimating the top of a small obstacle.
                    stop.y = hit.normal.y > .6f ? hit.point.y + .04f : landing.y;
                    landing = stop; Land(); return;
                }
                transform.position = destination;
                if (pose) pose.localRotation = Quaternion.Euler(-360 * t, 0, Mathf.Sin(t * Mathf.PI) * 20);
                if (t >= 1) Land();
                return;
            }
            if (pose) { pose.localRotation = Quaternion.identity; pose.localScale = Vector3.one; pose.localPosition = Vector3.zero; }
            if (State == ForestSaplingState.Dormant)
            {
                if (pose)
                {
                    float pulse = Mathf.Sin(elapsed * 5) * .035f;
                    pose.localScale = new Vector3(1 - pulse * .4f, 1 + pulse, 1 - pulse * .4f);
                    pose.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(elapsed * 4) * 5);
                }
                if (elapsed >= armDelay) { State = ForestSaplingState.Chasing; elapsed = 0; }
            }
            else if (State == ForestSaplingState.Chasing)
            {
                Vector3 to = target.transform.position - transform.position; to.y = 0;
                var direction = ForestAttackUtility.Flat(to);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 240 * dt);
                Vector3 before = transform.position;
                movement.Move(direction * (4.1f * dt) + Vector3.down * (4 * dt));
                float travelled = Vector3.ProjectOnPlane(transform.position - before, Vector3.up).magnitude;
                stepPhase += travelled * 14;
                if (pose)
                {
                    pose.localPosition = Vector3.up * (Mathf.Abs(Mathf.Sin(stepPhase)) * .14f);
                    pose.localRotation = Quaternion.Euler(12, 0, Mathf.Sin(stepPhase) * 9);
                }
                if (to.magnitude <= 1.05f || elapsed >= chaseDuration || (elapsed > .4f && travelled < dt * .08f))
                { State = ForestSaplingState.Exploding; elapsed = 0; landing = transform.position; }
            }
            else if (State == ForestSaplingState.Exploding)
            {
                warning.Circle(landing, explosionRadius, elapsed / explosionWindup);
                if (pose)
                {
                    float swell = Mathf.Clamp01(elapsed / explosionWindup);
                    pose.localScale = Vector3.one * (1 + swell * .3f);
                    pose.localRotation = Quaternion.Euler(0, Mathf.Sin(elapsed * 45) * 7 * swell, Mathf.Sin(elapsed * 60) * 5 * swell);
                }
                if (elapsed >= explosionWindup) Explode();
            }
        }
        void Land()
        {
            // Resolve support at the final XZ, including the landing endpoint when a frame spans the end
            // of the arc. This also guards starts embedded in scenery and irregular rock edge normals.
            Vector3 supportStart = landing + Vector3.up * 1.1f;
            int count = gameObject.scene.GetPhysicsScene().Raycast(supportStart, Vector3.down, hits, 2.5f, ~0, QueryTriggerInteraction.Ignore);
            float support = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (!hit.collider || hit.normal.y < .6f || hit.collider.GetComponentInParent<TrainingEnemy>() || hit.collider.GetComponentInParent<LiminalPlayerHealth>()) continue;
                if (hit.point.y > support)
                {
                    support = hit.point.y; LandingNormal = hit.normal; LandingPoint = hit.point; LandingCollider = hit.collider.name;
                }
            }
            if (!float.IsNegativeInfinity(support)) landing.y = support + .04f;
            transform.position = landing; State = ForestSaplingState.Dormant; elapsed = 0;
            body.enabled = true; Health.CanBeTargeted = true; Health.IgnoreDamage = false;
            if (pose) pose.localRotation = Quaternion.identity;
            ForestVfx.Burst(room ? room.transform : null, landing, new Color(.54f, .71f, .25f, .65f), .75f, 15);
        }
        void Explode()
        {
            if (Finished || !Health.IsAlive) return;
            Exploded = true; warning.Release();
            ForestVfx.Burst(room ? room.transform : null, landing, new Color(.86f, 1, .28f, .85f), explosionRadius, 65);
            HitFeedback.Dust(landing, 1.3f);
            Vector3 offset = target.transform.position - landing; offset.y = 0;
            if (offset.magnitude <= explosionRadius && ForestAttackUtility.HasLineOfSight(gameObject, landing + Vector3.up * .6f, target, hits))
            { DamageAttempts++; target.TakeDamage(explosionDamage); }
            Cancel();
        }
        void OnKilled(TrainingEnemy _)
        {
            if (Finished) return; DestroyedByPlayer = true;
            ForestVfx.Burst(room ? room.transform : null, transform.position, new Color(.49f, .77f, .44f, .65f), .7f, 16);
            Cancel();
        }
        void OnOwnerDeath(TrainingEnemy _) => Cancel();
        public void Cancel()
        {
            if (Finished) return; State = ForestSaplingState.Finished;
            if (warning) warning.Hide(); Unsubscribe(); gameObject.SetActive(false); Destroy(gameObject);
        }
        void Unsubscribe()
        {
            if (owner) owner.Defeated -= OnOwnerDeath; if (target) target.Died -= Cancel; if (Health) Health.Defeated -= OnKilled;
        }
        void OnDisable() { if (Health && !Finished) Cancel(); }
        void OnDestroy() => Unsubscribe();
    }
}
