using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// A photocopied sheet spat by the copier monster. It flies flat and fast, but flutters like paper:
    /// - it rocks and rolls around its flight direction;
    /// - it slips side to side and bobs a little;
    /// - near the end of its range it slows down, its flutter grows, and it sails down to the floor, lies there
    ///   for a moment and fades.
    /// The flight is swept against the player and against walls. A wall stops it, and it drops at the wall.
    /// </summary>
    public sealed class PaperProjectile : MonoBehaviour
    {
        Vector3 direction, side, origin;
        float speed, range, travelled, age, seed, groundY;
        int damage;
        Transform owner;
        LiminalPlayerHealth target;
        bool spent, landed;
        float landedAt;

        public static PaperProjectile Spawn(Vector3 position, Vector3 direction, float speed, float range, int damage,
            Transform owner, LiminalPlayerHealth target)
        {
            var go = new GameObject("Paper");
            go.layer = 2;
            go.transform.position = position;
            var sheet = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(sheet.GetComponent<Collider>());
            sheet.name = "Sheet";
            sheet.transform.SetParent(go.transform, false);
            // A4 proportions, lying flat in the flight plane.
            sheet.transform.localScale = new Vector3(.3f, .42f, 1);
            sheet.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var renderer = sheet.GetComponent<Renderer>();
            renderer.sharedMaterial = LiminalMonsterKit.PaperMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var paper = go.AddComponent<PaperProjectile>();
            paper.direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            paper.side = Vector3.Cross(Vector3.up, paper.direction);
            paper.origin = position;
            paper.speed = speed;
            paper.range = range;
            paper.damage = damage;
            paper.owner = owner;
            paper.target = target;
            paper.seed = Random.value * 100;
            paper.groundY = owner ? owner.position.y : position.y - 1;
            go.transform.rotation = Quaternion.LookRotation(paper.direction);
            return paper;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            age += dt;
            if (landed)
            {
                float t = (Time.time - landedAt) / .9f;
                transform.localScale = Vector3.one * Mathf.Clamp01(1.4f - t);
                if (t >= 1.4f) Destroy(gameObject);
                return;
            }
            // Fast and nearly flat for most of the range, then the sheet loses speed and flutters down.
            float progress = travelled / range;
            float slow = progress < .7f ? 1 : Mathf.Lerp(1, .25f, (progress - .7f) / .3f);
            float flutter = Mathf.Lerp(.35f, 1.25f, Mathf.Clamp01((progress - .45f) / .55f));
            float step = speed * slow * dt;
            travelled += step;
            Vector3 previous = transform.position;
            float sway = Mathf.Sin(age * 9f + seed) * .35f * flutter;
            float bob = Mathf.Sin(age * 13f + seed * 1.7f) * .08f * flutter;
            float drop = progress < .8f ? 0 : (progress - .8f) / .2f * 1.0f;
            Vector3 along = origin + direction * travelled;
            Vector3 next = along + side * sway + Vector3.up * (bob - drop);
            if (spent || travelled >= range || next.y <= groundY + .03f) { Land(next); return; }
            if (Blocked(previous, next)) { spent = true; Land(previous); return; }
            transform.position = next;
            // Rocking and rolling around the flight direction, plus a slow spin: unmistakably paper.
            float roll = Mathf.Sin(age * 11f + seed) * 38f * flutter + sway * 30f;
            float pitch = Mathf.Sin(age * 7f + seed * .5f) * 24f * flutter;
            float spin = age * 140f * flutter;
            transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(pitch, spin, roll);
            if (!spent && target && target.IsAlive && HitsPlayer(previous, next))
            {
                spent = true;
                target.TakeDamage(damage);
                HitFeedback.Sparks(next, direction, 6, .7f);
                Land(next);
            }
        }

        bool HitsPlayer(Vector3 a, Vector3 b)
        {
            Vector3 p = target.transform.position + Vector3.up * .9f;
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
            Vector3 closest = a + ab * t;
            Vector3 flat = closest - p;
            return new Vector2(flat.x, flat.z).magnitude < .5f && Mathf.Abs(flat.y) < 1.05f;
        }

        bool Blocked(Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            if (ab.sqrMagnitude < 1e-6f) return false;
            foreach (var hit in Physics.SphereCastAll(a, .08f, ab.normalized, ab.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (owner && (hit.transform == owner || hit.transform.IsChildOf(owner))) continue;
                if (hit.collider.GetComponentInParent<LiminalPlayerHealth>()) continue;
                if (hit.collider.GetComponentInParent<LiminalPropMonster>()) continue;
                if (hit.collider.GetComponentInParent<PaperProjectile>()) continue;
                return true;
            }
            return false;
        }

        void Land(Vector3 at)
        {
            landed = true;
            landedAt = Time.time;
            at.y = groundY + .012f;
            transform.position = at;
            transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Quaternion.Euler(Random.Range(-3f, 3f), 0, Random.Range(-3f, 3f));
        }
    }
}
