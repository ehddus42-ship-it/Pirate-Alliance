using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// The monitor turret's shot: a line of glowing binary digits, like "1010111000010101".
    /// - The digits lie flat, one after another along the flight direction, so the whole string streaks across
    ///   the floor as a single line. It does not fly as upright text.
    /// - The head digit is white-hot and the tail fades to dark green. Bits keep flickering between 0 and 1.
    /// - A faint scanline glow runs under the string.
    /// - When it hits the player or a wall, the string breaks up: the digits scatter, tumble and fade.
    /// </summary>
    public sealed class BinaryProjectile : MonoBehaviour
    {
        public const int Length = 16;
        const float Spacing = .2f, GlyphHalfWidth = .085f, GlyphHalfLength = .115f;
        static readonly Color Head = new Color(.85f, 1f, .9f), Body = new Color(.25f, 1f, .45f), Tail = new Color(0f, .35f, .12f, 0f);

        GlyphStrip strip;
        Mesh glowMesh;
        int[] bits = new int[Length];
        Vector3 direction, side, origin;
        float speed, range, travelled, flicker;
        int damage;
        Transform owner;
        LiminalPlayerHealth target;
        bool broken;
        float brokenAt;
        Vector3[] shardPositions, shardVelocities;
        float[] shardSpin;

        public static BinaryProjectile Spawn(Vector3 position, Vector3 direction, float speed, float range, int damage, Transform owner, LiminalPlayerHealth target)
        {
            var go = new GameObject("Binary Stream") { layer = 2 };
            go.transform.position = Vector3.zero;
            var p = go.AddComponent<BinaryProjectile>();
            p.direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            p.side = Vector3.Cross(Vector3.up, p.direction);
            p.origin = position;
            p.speed = ProjectileTuning.ScaleSpeed(speed); p.range = range; p.damage = damage; p.owner = owner; p.target = target;
            for (int i = 0; i < Length; i++) p.bits[i] = Random.value < .5f ? 0 : 1;
            p.strip = new GlyphStrip(Length, "Binary Stream");
            go.AddComponent<MeshFilter>().sharedMesh = p.strip.mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = LiminalMonsterKit.GlyphMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // The scanline glow under the digits.
            var glow = new GameObject("Glow") { layer = 2 };
            glow.transform.SetParent(go.transform, false);
            p.glowMesh = new Mesh { name = "Binary Glow" };
            glow.AddComponent<MeshFilter>().sharedMesh = p.glowMesh;
            var gr = glow.AddComponent<MeshRenderer>();
            gr.sharedMaterial = LiminalMonsterKit.GlyphGlowMaterial;
            gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            p.Layout();
            return p;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            if (broken) { Shatter(dt); return; }
            flicker += dt;
            if (flicker > .05f)
            {
                flicker = 0;
                bits[Random.Range(0, Length)] ^= 1;
                if (Random.value < .5f) bits[Random.Range(0, Length)] ^= 1;
            }
            Vector3 headBefore = origin + direction * travelled;
            travelled += speed * dt;
            Vector3 head = origin + direction * travelled;
            if (target && target.IsAlive && HitsPlayer(headBefore, head))
            {
                target.TakeDamage(damage);
                HitFeedback.Sparks(head, direction, 8, .8f);
                Break();
                return;
            }
            if (Blocked(headBefore, head) || travelled >= range + Length * Spacing) { Break(); return; }
            Layout();
        }

        /// <summary>Lays the digits flat along the flight line, head first; digits behind the start stay hidden.</summary>
        void Layout()
        {
            Vector3 right = side * GlyphHalfWidth, along = direction * GlyphHalfLength;
            for (int i = 0; i < Length; i++)
            {
                float behind = i * Spacing;
                float d = travelled - behind;
                float u = i / (Length - 1f);
                Color c = i == 0 ? Head : Color.Lerp(Body, Tail, u * u);
                // Each digit reads along the flight: its "up" points forward, so the string runs as a line.
                Vector3 center = origin + direction * d + Vector3.up * (.012f * Mathf.Sin(Time.time * 30 + i));
                if (d < 0 || d > range) c.a = 0;
                strip.Set(i, center, right, along, bits[i], c);
            }
            strip.Apply();
            Vector3 headPos = origin + direction * travelled, tailPos = origin + direction * Mathf.Max(0, travelled - Length * Spacing);
            Vector3 w = side * .16f, below = Vector3.down * .02f;
            glowMesh.vertices = new[] { tailPos - w + below, tailPos + w + below, headPos + w + below, headPos - w + below };
            glowMesh.colors = new[] { new Color(0, .4f, .15f, 0), new Color(0, .4f, .15f, 0), new Color(.2f, 1f, .45f, .35f), new Color(.2f, 1f, .45f, .35f) };
            glowMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            glowMesh.RecalculateBounds();
        }

        bool HitsPlayer(Vector3 a, Vector3 b)
        {
            Vector3 p = target.transform.position;
            Vector3 ab = b - a; ab.y = 0;
            Vector3 ap = p - a; ap.y = 0;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude) : 0;
            Vector3 closest = ap - ab * t;
            return closest.magnitude < .48f && Mathf.Abs(origin.y - (p.y + .8f)) < 1.2f;
        }

        bool Blocked(Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            if (ab.sqrMagnitude < 1e-6f) return false;
            foreach (var hit in Physics.SphereCastAll(a, .06f, ab.normalized, ab.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (owner && (hit.transform == owner || hit.transform.IsChildOf(owner))) continue;
                if (hit.collider.GetComponentInParent<LiminalPlayerHealth>()) continue;
                if (hit.collider.GetComponentInParent<LiminalPropMonster>()) continue;
                return true;
            }
            return false;
        }

        void Break()
        {
            broken = true;
            brokenAt = Time.time;
            shardPositions = new Vector3[Length];
            shardVelocities = new Vector3[Length];
            shardSpin = new float[Length];
            for (int i = 0; i < Length; i++)
            {
                shardPositions[i] = origin + direction * (travelled - i * Spacing);
                shardVelocities[i] = -direction * Random.Range(.5f, 2.5f) + side * Random.Range(-2.2f, 2.2f) + Vector3.up * Random.Range(1f, 3.2f);
                shardSpin[i] = Random.Range(-720f, 720f);
            }
            if (glowMesh) glowMesh.Clear();
        }

        void Shatter(float dt)
        {
            float age = Time.time - brokenAt;
            float fade = 1 - age / .45f;
            if (fade <= 0) { Destroy(gameObject); return; }
            for (int i = 0; i < Length; i++)
            {
                shardVelocities[i] += Vector3.down * 9f * dt;
                shardPositions[i] += shardVelocities[i] * dt;
                var q = Quaternion.AngleAxis(shardSpin[i] * age, side);
                float d = travelled - i * Spacing;
                Color c = Color.Lerp(Head, Body, .4f);
                c.a = d < 0 ? 0 : fade;
                strip.Set(i, shardPositions[i], q * (side * GlyphHalfWidth), q * (direction * GlyphHalfLength), Random.value < .1f ? 1 - bits[i] : bits[i], c);
            }
            strip.Apply();
        }

        void OnDestroy()
        {
            if (strip != null && strip.mesh) Destroy(strip.mesh);
            if (glowMesh) Destroy(glowMesh);
        }
    }
}
