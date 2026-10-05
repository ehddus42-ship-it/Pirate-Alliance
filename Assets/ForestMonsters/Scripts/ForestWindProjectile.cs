using AcRoguelike.Liminal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.Forest
{
    public sealed class ForestWindProjectile : MonoBehaviour
    {
        public float Speed => 6.4f;
        public float Radius => .68f;
        public float MaximumTravel => 15f;
        public float Travelled { get; private set; }
        public int DamageAttempts { get; private set; }
        public bool HitPlayer { get; private set; }
        public bool Finished { get; private set; }
        public Vector3 Direction { get; private set; }
        TrainingEnemy owner; LiminalPlayerHealth target; LiminalRoom room;
        bool hadRoom; int damage;
        readonly RaycastHit[] hits = new RaycastHit[96]; readonly Collider[] overlaps = new Collider[96];

        public static ForestWindProjectile Fire(Vector3 ground, Vector3 direction, TrainingEnemy owner, LiminalPlayerHealth target, LiminalRoom room, int damage = 17)
        {
            var go = new GameObject("Lunar butterfly • leaf cyclone");
            go.transform.SetParent(room ? room.transform : owner.transform.parent, true);
            go.transform.position = ground + Vector3.up * .8f;
            var shot = go.AddComponent<ForestWindProjectile>(); shot.owner = owner; shot.target = target; shot.room = room; shot.hadRoom = room;
            shot.damage = damage; shot.Direction = ForestAttackUtility.Flat(direction);
            go.AddComponent<ForestWindVisual>();
            if (owner) owner.Defeated += shot.OnOwnerDeath; if (target) target.Died += shot.Cancel;
            return shot;
        }
        void Update()
        {
            if (Finished) return;
            if (!ForestAttackUtility.Alive(owner, target, room, hadRoom)) { Cancel(); return; }
            float dt = Time.deltaTime; if (dt <= 0) return;
            float step = Mathf.Min(Speed * dt, MaximumTravel - Travelled);
            if (ForestAttackUtility.Sweep(gameObject.scene.GetPhysicsScene(), room, transform.position, Radius, Direction, step, target, hits, overlaps, out var impact))
            {
                transform.position += Direction * Mathf.Max(0, impact.distance); Travelled += Mathf.Max(0, impact.distance);
                if (impact.player) { DamageAttempts++; HitPlayer = impact.player.TakeDamage(damage); }
                ForestVfx.Burst(room ? room.transform : null, transform.position - Vector3.up * .75f, new Color(.5f, 1, .78f, .8f), 1.4f);
                Cancel(); return;
            }
            transform.position += Direction * step; Travelled += step;
            if (Travelled >= MaximumTravel - .001f) Cancel();
        }
        void OnOwnerDeath(TrainingEnemy _) => Cancel();
        public void Cancel()
        {
            if (Finished) return; Finished = true; Unsubscribe(); gameObject.SetActive(false); Destroy(gameObject);
        }
        void Unsubscribe() { if (owner) owner.Defeated -= OnOwnerDeath; if (target) target.Died -= Cancel; }
        void OnDisable() { if (!Finished) Cancel(); }
        void OnDestroy() => Unsubscribe();
    }
}
