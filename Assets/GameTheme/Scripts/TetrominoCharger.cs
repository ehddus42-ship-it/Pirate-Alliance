using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.GameTheme
{
    /// <summary>A targetable Z block. Tracks briefly, commits its warning, then makes one straight charge.</summary>
    public sealed class TetrominoCharger : MonoBehaviour
    {
        public const float WindupDuration = 1f, AimLockTime = .6f, ChargeSpeed = 13f, ChargeLength = 10f;
        public TrainingEnemy Health { get; private set; }
        public bool WindingUp => !Finished && !Charging;
        public bool Charging { get; private set; }
        public bool Finished { get; private set; }
        public bool HitPlayer { get; private set; }
        public int DamageAttempts { get; private set; }
        public int Damage => 14;
        public Vector3 LockedDirection => direction;
        public float Travelled { get; private set; }

        TrainingEnemy owner;
        LiminalPlayerHealth target;
        LiminalRoom room;
        bool hadRoom;
        CharacterController body;
        Telegraph warning;
        Transform visual;
        LiminalPlayerHealth controllerContact;
        Vector3 direction;
        float elapsed;
        readonly RaycastHit[] hits = new RaycastHit[128];
        readonly Collider[] overlaps = new Collider[128];

        public static TetrominoCharger Spawn(Vector3 position, Quaternion rotation, TrainingEnemy owner,
            LiminalPlayerHealth target, LiminalRoom room)
        {
            var go = new GameObject("Z Block Charger");
            go.transform.SetParent(room ? room.transform : owner ? owner.transform.parent : null, true);
            go.transform.SetPositionAndRotation(position, rotation);
            var charger = go.AddComponent<TetrominoCharger>();
            charger.owner = owner; charger.target = target; charger.room = room; charger.hadRoom = room;
            charger.body = go.AddComponent<CharacterController>();
            charger.body.radius = .5f; charger.body.height = 1.2f; charger.body.center = Vector3.up * .6f;
            charger.body.skinWidth = .025f; charger.body.minMoveDistance = 0; charger.body.stepOffset = .18f;
            charger.visual = TetrominoVisual.Create(go.transform, TetrominoShape.Z, .48f);
            charger.visual.localPosition = Vector3.up * .65f;
            charger.Health = go.AddComponent<TrainingEnemy>();
            charger.Health.aimAnchor = charger.visual;
            charger.Health.Configure(48, false);
            charger.Health.deferDeathVisuals = true;
            charger.Health.Defeated += charger.Defeated;
            charger.direction = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up).normalized;
            if (charger.direction.sqrMagnitude < .01f) charger.direction = Vector3.forward;
            charger.warning = Telegraph.Create(go.transform, "Z Charge Warning");
            if (owner) owner.Defeated += charger.Defeated;
            if (target) target.Died += charger.Finish;
            return charger;
        }

        void Update()
        {
            if (Finished) return;
            if (!owner || !owner.IsAlive || !Health || !Health.IsAlive || !target || !target.IsAlive || (hadRoom && !room)
                || (room && !room.Contains(transform.position)))
            { Finish(); return; }
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            float before = elapsed;
            elapsed += dt;
            if (!Charging)
            {
                body.Move(Vector3.down * (4 * dt));
                if (before < AimLockTime)
                {
                    Vector3 to = target.transform.position - transform.position; to.y = 0;
                    if (to.sqrMagnitude > .01f) direction = to.normalized;
                    transform.rotation = Quaternion.LookRotation(direction);
                }
                visual.localPosition = Vector3.up * (.65f + Mathf.Sin(elapsed * 15) * .045f);
                visual.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(elapsed * 18) * 5);
                warning.Line(transform.position + Vector3.up * .035f, direction, ChargeLength, .8f,
                    Mathf.Clamp01(elapsed / WindupDuration));
                if (elapsed < WindupDuration) return;
                Charging = true;
                warning.Release();
                visual.localRotation = Quaternion.identity;
                // Only spend the portion of this frame after the wind-up completed.
                dt = Mathf.Max(0, elapsed - WindupDuration);
            }
            Charge(dt);
        }

        void Charge(float dt)
        {
            float distance = Mathf.Min(ChargeLength - Travelled, ChargeSpeed * dt);
            if (distance <= 0) return;
            // Use the controller's contact envelope. An inset query could miss the player
            // just as CharacterController.Move stops at it, incorrectly ending a harmless charge.
            float radius = body.radius + body.skinWidth;
            bool collision = TetrominoSweep.Cast(gameObject.scene.GetPhysicsScene(), room,
                transform.TransformPoint(body.center), radius, direction, distance, target, hits, overlaps, out var impact);
            float step = collision ? Mathf.Max(0, impact.distance - .015f) : distance;
            Vector3 before = transform.position;
            controllerContact = null;
            body.Move(direction * step + Vector3.down * (4 * dt));
            float moved = Vector3.ProjectOnPlane(transform.position - before, Vector3.up).magnitude;
            Travelled += moved;
            visual.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(elapsed * 22) * 8);
            // Controller contacts are authoritative even when its skin stops the movement
            // before the query's mathematical surface. A nearer swept wall still wins.
            if (controllerContact && (!collision || impact.player))
            {
                DamageAttempts++;
                HitPlayer = controllerContact.TakeDamage(Damage);
                Finish();
            }
            else if (collision)
            {
                // A nearer controller obstruction must not allow damage beyond it.
                if (impact.player && moved + .08f >= step)
                { DamageAttempts++; HitPlayer = impact.player.TakeDamage(Damage); }
                Finish();
            }
            else if (Travelled >= ChargeLength - .02f || moved < distance * .2f || elapsed > WindupDuration + 2)
                Finish();
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!Charging || Finished || !target) return;
            var player = hit.collider.GetComponentInParent<LiminalPlayerHealth>();
            if (player == target) controllerContact = player;
        }

        void Defeated(TrainingEnemy _) => Finish();
        void Finish()
        {
            if (Finished) return;
            Finished = true;
            Charging = false;
            if (Health) Health.CanBeTargeted = false;
            if (warning) warning.Hide();
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
        void OnDestroy()
        {
            if (owner) owner.Defeated -= Defeated;
            if (Health) Health.Defeated -= Defeated;
            if (target) target.Died -= Finish;
        }
    }
}
