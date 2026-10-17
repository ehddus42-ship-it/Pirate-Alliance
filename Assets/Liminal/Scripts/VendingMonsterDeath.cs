using System.Collections;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// Vending monster death: the killing hit throws the machine back, its arms and legs shrivel back into the
    /// cabinet in the air, and it crashes onto its back as a plain vending machine: a heavy bounce, dust, sparks and
    /// spilled cans. It lies there for a moment, then sinks into the floor and is hidden.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VendingMonsterDeath : MonoBehaviour
    {
        public bool Playing { get; private set; }
        public bool Finished { get; private set; }

        static Material coinMaterial, canMaterial;

        public void Play(VendingMonster monster, Vector3 direction, float impact)
        {
            if (Playing) return;
            Playing = true;
            StartCoroutine(Run(monster, direction, impact));
        }

        IEnumerator Run(VendingMonster monster, Vector3 direction, float impact)
        {
            var health = monster ? monster.Health : GetComponent<AcRoguelike.TrainingEnemy>();
            var animator = monster ? monster.animator : null;
            var rig = monster ? monster.rig : null;
            Transform visual = animator && animator.transform != transform ? animator.transform : null;
            // Freeze the creature pose; from here the death is driven by hand.
            if (animator) animator.enabled = false;
            if (visual) { visual.localRotation = Quaternion.identity; visual.localScale = Vector3.one; }

            var limbs = rig ? new[] { rig.leftArm.upper, rig.rightArm.upper, rig.leftLeg.upper, rig.rightLeg.upper } : new Transform[0];
            var limbScales = new Vector3[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) if (limbs[i]) limbScales[i] = limbs[i].localScale;
            float lift = rig ? rig.cabinetLift : 1.16f;
            Vector3 visualStart = visual ? visual.localPosition : Vector3.zero;

            direction.y = 0;
            direction = direction.sqrMagnitude > .001f ? direction.normalized : -transform.forward;
            float ground = transform.position.y;
            // Heavy: a short, low flight. The finisher's launch carries it further.
            Vector3 velocity = direction * (3.4f + 2.2f * impact) + Vector3.up * (4.6f + 1.2f * impact);
            Quaternion start = transform.rotation;
            // Tip over backwards (top away from the attacker): it lands on its back, front panel up.
            Vector3 axis = Vector3.Cross(Vector3.up, direction);
            Quaternion lying = Quaternion.AngleAxis(90f, axis) * Quaternion.LookRotation(-direction);
            const float backDepth = .42f; // half the cabinet depth, so the back rests on the floor
            AcRoguelike.HitFeedback.Sparks(health ? health.AimPoint : transform.position + Vector3.up * 2, direction, 18, 1.4f);
            SpillCoins((monster && monster.dispenserSocket ? monster.dispenserSocket.position : transform.position + Vector3.up * 2), direction, 8);

            float air = 0;
            int bounces = 0;
            while (bounces < 2 && air < 3f)
            {
                float dt = Time.deltaTime;
                air += dt;
                // Limbs shrivel back in during the first half second; the cabinet settles onto where the legs were.
                float shrink = Mathf.SmoothStep(0, 1, air / .45f);
                for (int i = 0; i < limbs.Length; i++) if (limbs[i]) limbs[i].localScale = limbScales[i] * Mathf.Lerp(1, .01f, shrink);
                if (visual) visual.localPosition = visualStart + Vector3.down * (lift * shrink);
                if (shrink >= 1 && monster) foreach (var r in monster.limbRenderers) if (r) r.enabled = false;

                velocity.y -= 22f * dt;
                Vector3 p = transform.position + velocity * dt;
                float tip = Mathf.Clamp01(air / .55f);
                tip = tip * tip * (3 - 2 * tip);
                transform.rotation = Quaternion.Slerp(start, lying, tip);
                float floor = ground + backDepth * tip;
                if (p.y <= floor && velocity.y < 0)
                {
                    p.y = floor;
                    bounces++;
                    velocity = new Vector3(velocity.x * .3f, bounces == 1 ? 2.1f : 0, velocity.z * .3f);
                    Vector3 at = new Vector3(p.x, ground, p.z) + direction * 1.2f;
                    AcRoguelike.HitFeedback.Dust(at, bounces == 1 ? 1.7f : 1f);
                    AcRoguelike.HitFeedback.Shake(bounces == 1 ? .14f : .06f, bounces == 1 ? .22f : .12f);
                    if (bounces == 1)
                    {
                        AcRoguelike.HitFeedback.Sparks(at + Vector3.up * .3f, Vector3.up, 16, 1.2f);
                        SpillCans(at + Vector3.up * .5f, direction, 6);
                    }
                    if (monster && monster.voice && monster.impactSound) monster.voice.PlayOneShot(monster.impactSound, bounces == 1 ? 1f : .5f);
                }
                transform.position = p;
                yield return null;
            }
            transform.rotation = lying;
            if (monster) foreach (var r in monster.limbRenderers) if (r) r.enabled = false;
            yield return new WaitForSeconds(1.1f);
            for (float t = 0; t < .8f; t += Time.deltaTime)
            {
                transform.position += Vector3.down * (1.2f * Time.deltaTime);
                yield return null;
            }
            AcRoguelike.HitFeedback.Dust(new Vector3(transform.position.x, ground, transform.position.z) + direction * 1.2f, .8f);
            if (health) health.HideNow();
            // Leave the visual hierarchy as authored (hidden), in case the room resets the monster.
            for (int i = 0; i < limbs.Length; i++) if (limbs[i]) limbs[i].localScale = limbScales[i];
            if (visual) visual.localPosition = visualStart;
            Playing = false;
            Finished = true;
        }

        /// <summary>A few coins pop out of the coin slot: small gold discs that bounce and vanish.</summary>
        public static void SpillCoins(Vector3 point, Vector3 direction, int count)
        {
            if (!coinMaterial) coinMaterial = Lit(new Color(1f, .78f, .25f), .9f);
            for (int i = 0; i < count; i++)
                Debris(PrimitiveType.Cylinder, new Vector3(.09f, .008f, .09f), coinMaterial, point,
                    (direction * Random.Range(1.2f, 2.6f) + Vector3.up * Random.Range(2.4f, 4.2f) + Random.insideUnitSphere * 1.1f), 1.3f);
        }

        /// <summary>Drink cans tumbling out of the broken machine.</summary>
        public static void SpillCans(Vector3 point, Vector3 direction, int count)
        {
            if (!canMaterial) canMaterial = Lit(new Color(.85f, .18f, .2f), .55f);
            for (int i = 0; i < count; i++)
                Debris(PrimitiveType.Cylinder, new Vector3(.12f, .12f, .12f), canMaterial, point + Random.insideUnitSphere * .4f,
                    (direction * Random.Range(.5f, 2.5f) + Vector3.up * Random.Range(1.5f, 3.2f) + Random.insideUnitSphere * 1.6f), 2.4f);
        }

        static void Debris(PrimitiveType type, Vector3 scale, Material material, Vector3 point, Vector3 velocity, float life)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = "Vending Debris";
            go.layer = 2; // Ignore Raycast: never blocks sight lines or projectiles' checks
            go.transform.position = point;
            go.transform.rotation = Random.rotation;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var body = go.AddComponent<Rigidbody>();
            body.mass = .05f;
            body.linearVelocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 18f;
            Object.Destroy(go, life);
        }

        static Material Lit(Color color, float metallic)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", .7f);
            return m;
        }
    }
}
