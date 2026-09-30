using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>Shared helpers for the support skill: Meshy model fitting, voxel fallbacks, voxel bursts and rings.</summary>
    static class SupportVoxels
    {
        static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        static Material lineMaterial;

        public static Material Mat(Color color, float glow = 0)
        {
            var key = new Color(color.r, color.g, color.b, glow);
            if (materials.TryGetValue(key, out var found) && found) return found;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.SetColor("_BaseColor", color);
            material.color = color;
            material.SetFloat("_Smoothness", .25f);
            if (glow > 0)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor("_EmissionColor", color * glow);
            }
            materials[key] = material;
            return material;
        }

        public static Material LineMaterial()
        {
            if (!lineMaterial) lineMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
            return lineMaterial;
        }

        /// <summary>Instantiates a Meshy model (or builds the fallback) centred on the parent and scaled so its largest side is `size`.</summary>
        public static Transform Model(GameObject source, Transform parent, float size, float yaw, System.Action<Transform> fallback)
        {
            var holder = new GameObject("Model").transform;
            holder.SetParent(parent, false);
            Transform content;
            if (source)
            {
                content = Object.Instantiate(source, holder).transform;
                foreach (var collider in content.GetComponentsInChildren<Collider>(true)) Object.Destroy(collider);
            }
            else
            {
                content = new GameObject("Voxels").transform;
                content.SetParent(holder, false);
                fallback(content);
            }
            foreach (var renderer in content.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = false;
            }
            content.localRotation = Quaternion.Euler(0, yaw, 0);
            var renderers = content.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                if (largest > 1e-4f)
                {
                    float scale = size / largest;
                    content.localScale *= scale;
                    content.position -= (bounds.center - holder.position) * scale;
                }
            }
            return holder;
        }

        public static Transform Cube(Transform parent, Vector3 position, float size, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(cube.GetComponent<Collider>());
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = Vector3.one * size;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube.transform;
        }

        public static void Burst(Vector3 center, int count, float speed, float size, Color[] colors, float glow)
        {
            var root = new GameObject("Voxel Burst").AddComponent<SupportVoxelBurst>();
            root.transform.position = center;
            for (int i = 0; i < count; i++)
            {
                var cube = Cube(root.transform, Vector3.zero, size * Random.Range(.6f, 1.3f), Mat(colors[i % colors.Length], glow));
                Vector3 direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 1.3f + .2f;
                root.Add(cube, direction.normalized * speed * Random.Range(.5f, 1.1f));
            }
        }

        public static LineRenderer Ring(Transform parent, Color color, float width)
        {
            var go = new GameObject("Ring");
            go.layer = 2;
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = 48;
            line.widthMultiplier = width;
            line.sharedMaterial = LineMaterial();
            line.startColor = line.endColor = color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        public static void SetRing(LineRenderer line, Vector3 center, float radius)
        {
            for (int i = 0; i < line.positionCount; i++)
            {
                float a = i * Mathf.PI * 2 / line.positionCount;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, .06f, Mathf.Sin(a) * radius));
            }
        }

        public static Light Glow(Transform parent, Color color, float intensity, float range)
        {
            var light = new GameObject("Glow").AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        // ---- procedural voxel fallbacks (used when a Meshy model is missing) -------------------------------
        public static void MeteorVoxels(Transform root)
        {
            var rock = Mat(new Color(.2f, .18f, .24f));
            var dark = Mat(new Color(.34f, .22f, .38f));
            var lava = Mat(new Color(1f, .45f, .08f), 3f);
            for (int x = -3; x <= 3; x++)
                for (int y = -3; y <= 3; y++)
                    for (int z = -3; z <= 3; z++)
                    {
                        float d = Mathf.Sqrt(x * x + y * y + z * z);
                        if (d > 3.3f || d < 2.2f) continue;
                        bool crack = (x + y * 2 + z * 3) % 5 == 0;
                        Cube(root, new Vector3(x, y, z), 1, crack ? lava : (x + z) % 2 == 0 ? rock : dark);
                    }
            for (int i = 0; i < 6; i++) Cube(root, new Vector3(0, 3 + i, -1 - i * .4f), 1.2f - i * .15f, lava);
        }

        public static readonly string[] OneUpGlyphs =
        {
            ".#..#..#.###.",
            "##..#..#.#..#",
            ".#..#..#.###.",
            ".#..#..#.#...",
            "###..##..#...",
        };

        public static void OneUpVoxels(Transform root)
        {
            var green = Mat(new Color(.45f, 1f, .35f), 1.6f);
            var outline = Mat(new Color(.08f, .35f, .1f));
            for (int row = 0; row < OneUpGlyphs.Length; row++)
                for (int col = 0; col < OneUpGlyphs[row].Length; col++)
                {
                    if (OneUpGlyphs[row][col] != '#') continue;
                    Vector3 p = new Vector3(col - 6, 4 - row, 0);
                    Cube(root, p, 1, green);
                    Cube(root, p + new Vector3(0, 0, .6f), 1, outline);
                }
        }

        public static void ChomperVoxels(Transform root)
        {
            var yellow = Mat(new Color(1f, .86f, .16f), 1.2f);
            var black = Mat(Color.black);
            for (int x = -3; x <= 3; x++)
                for (int y = -3; y <= 3; y++)
                    for (int z = -3; z <= 3; z++)
                    {
                        if (x * x + y * y + z * z > 10.5f) continue;
                        if (z > 0 && Mathf.Abs(y) < z * .8f) continue; // mouth opens toward +z
                        Cube(root, new Vector3(x, y, z), 1, yellow);
                    }
            Cube(root, new Vector3(2.6f, 1.6f, 1), .8f, black);
            Cube(root, new Vector3(-2.6f, 1.6f, 1), .8f, black);
        }
    }

    sealed class SupportVoxelBurst : MonoBehaviour
    {
        readonly List<(Transform cube, Vector3 velocity, float size)> parts = new List<(Transform, Vector3, float)>();
        float age;

        public void Add(Transform cube, Vector3 velocity) => parts.Add((cube, velocity, cube.localScale.x));

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            float life = 1 - age / 1.1f;
            for (int i = 0; i < parts.Count; i++)
            {
                var (cube, velocity, size) = parts[i];
                if (!cube) continue;
                velocity += Physics.gravity * dt;
                Vector3 p = cube.position + velocity * dt;
                if (p.y < .05f) { p.y = .05f; velocity = new Vector3(velocity.x * .5f, -velocity.y * .3f, velocity.z * .5f); }
                cube.position = p;
                cube.Rotate(velocity * 40 * dt, Space.World);
                cube.localScale = Vector3.one * size * Mathf.Clamp01(life * 1.6f);
                parts[i] = (cube, velocity, size);
            }
            if (life <= 0) Destroy(gameObject);
        }
    }

    /// <summary>Voxel meteor: a ground warning ring, a spinning fall with a voxel fire trail, then damage and a voxel blast.</summary>
    sealed class SupportMeteor : MonoBehaviour
    {
        SupportCharacterSkill owner;
        Vector3 target, start;
        float fallTime, radius, age, trailClock;
        int damage;
        Transform model;
        LineRenderer warning, inner;
        bool landed;
        static readonly Color Orange = new Color(1f, .45f, .1f);

        public void Launch(SupportCharacterSkill skill, Vector3 point, float duration, float blastRadius, int blastDamage, GameObject source)
        {
            owner = skill;
            target = point;
            fallTime = Mathf.Max(.2f, duration);
            radius = blastRadius;
            damage = blastDamage;
            start = target + new Vector3(-4.5f, 17f, -6.5f);
            transform.position = start;
            model = SupportVoxels.Model(source, transform, 2.6f, 0, SupportVoxels.MeteorVoxels);
            SupportVoxels.Glow(transform, Orange, 6, 7);
            var marks = new GameObject("Meteor Warning").transform;
            marks.SetParent(transform, false);
            warning = SupportVoxels.Ring(marks, Orange, .12f);
            inner = SupportVoxels.Ring(marks, new Color(1f, .8f, .3f), .06f);
            SupportVoxels.SetRing(warning, target, radius);
        }

        void Update()
        {
            if (landed) return;
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / fallTime);
            transform.position = Vector3.Lerp(start, target + Vector3.up * .9f, t * t);
            model.Rotate(new Vector3(260, 140, 90) * Time.deltaTime, Space.Self);
            if (inner) SupportVoxels.SetRing(inner, target, radius * t);
            trailClock += Time.deltaTime;
            if (trailClock > .035f)
            {
                trailClock = 0;
                SupportVoxels.Burst(transform.position, 2, .8f, .32f, new[] { Orange, new Color(1f, .8f, .2f) }, 3f);
            }
            if (t >= 1) Land();
        }

        void Land()
        {
            landed = true;
            foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
            {
                if (!enemy.IsAlive) continue;
                Vector3 offset = enemy.transform.position - target;
                offset.y = 0;
                if (offset.magnitude <= radius) enemy.TakeDamage(damage);
            }
            SupportVoxels.Burst(target + Vector3.up * .4f, 34, 9f, .38f,
                new[] { Orange, new Color(.25f, .2f, .3f), new Color(1f, .82f, .25f), new Color(.4f, .26f, .45f) }, 2.2f);
            var shock = new GameObject("Meteor Shockwave").AddComponent<SupportShockwave>();
            shock.Play(target, radius);
            if (warning) Destroy(warning.transform.parent.gameObject);
            if (owner) owner.OnMeteorLanded();
            // The rock stays half buried for a moment, then shrinks away.
            var sink = gameObject.AddComponent<SupportSink>();
            sink.Play(model, .9f);
        }
    }

    sealed class SupportSink : MonoBehaviour
    {
        Transform model;
        float duration, age;
        Vector3 scale;
        Light glow;

        public void Play(Transform target, float time)
        {
            model = target;
            duration = time;
            scale = model.localScale;
            glow = GetComponentInChildren<Light>();
        }

        void Update()
        {
            age += Time.deltaTime;
            float k = 1 - Mathf.Clamp01(age / duration);
            if (model) model.localScale = scale * (.3f + .7f * k);
            transform.position += Vector3.down * Time.deltaTime * .6f;
            if (glow) glow.intensity = 14 * k * k;
            if (age >= duration) Destroy(gameObject);
        }
    }

    sealed class SupportShockwave : MonoBehaviour
    {
        LineRenderer ring;
        Light flash;
        Vector3 center;
        float radius, age;

        public void Play(Vector3 point, float maxRadius)
        {
            center = point;
            radius = maxRadius;
            ring = SupportVoxels.Ring(transform, new Color(1f, .72f, .3f), .3f);
            flash = SupportVoxels.Glow(transform, new Color(1f, .6f, .25f), 16, 12);
            flash.transform.position = point + Vector3.up * 1.5f;
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / .45f);
            SupportVoxels.SetRing(ring, center, Mathf.Lerp(.3f, radius * 1.15f, 1 - (1 - t) * (1 - t)));
            ring.widthMultiplier = .3f * (1 - t) + .02f;
            flash.intensity = 16 * (1 - t);
            if (t >= 1) Destroy(gameObject);
        }
    }

    /// <summary>Voxel 1UP that floats over the player's head, pops in, bobs and blinks during its last second.</summary>
    sealed class SupportOneUp : MonoBehaviour
    {
        Transform player, model;
        float duration, age;
        Light glow;

        public void Show(Transform follow, float time, GameObject source)
        {
            player = follow;
            duration = time;
            model = SupportVoxels.Model(source, transform, 1.5f, 0, SupportVoxels.OneUpVoxels);
            glow = SupportVoxels.Glow(transform, new Color(.45f, 1f, .35f), 2.5f, 4);
            SupportVoxels.Burst(follow.position + Vector3.up * 2.5f, 14, 3.5f, .16f, new[] { new Color(.45f, 1f, .35f), new Color(1f, .9f, .4f) }, 2f);
            Update();
        }

        void Update()
        {
            if (!player) { Destroy(gameObject); return; }
            age += Time.deltaTime;
            float pop = age < .35f ? 1 + Mathf.Sin(age / .35f * Mathf.PI) * .35f : 1;
            float grow = Mathf.Clamp01(age / .12f);
            transform.position = player.position + Vector3.up * (2.55f + Mathf.Sin(age * 3.2f) * .12f);
            var camera = Camera.main;
            // Full billboard: the readable side of the letters (-Z) faces the quarter-view camera.
            if (camera) transform.rotation = Quaternion.LookRotation(transform.position - camera.transform.position, Vector3.up);
            transform.localScale = Vector3.one * pop * grow;
            bool blink = duration - age < 1f && Mathf.Repeat(age * 8, 1) < .45f;
            if (model) model.gameObject.SetActive(!blink);
            if (glow) glow.intensity = blink ? .6f : 2.5f;
            if (age >= duration)
            {
                SupportVoxels.Burst(transform.position, 10, 2.5f, .14f, new[] { new Color(.45f, 1f, .35f) }, 2f);
                Destroy(gameObject);
            }
        }
    }

    /// <summary>Voxel chomper fired with each talisman while 1UP is active: homes on the target, chomps, deals bonus damage.</summary>
    sealed class SupportChomper : MonoBehaviour
    {
        TrainingEnemy target;
        Vector3 direction;
        int damage;
        float age, pelletClock;
        Transform model;
        const float Speed = 12.5f, Lifetime = 2.4f;
        static readonly Color Yellow = new Color(1f, .86f, .16f);

        public void Launch(Vector3 origin, Vector3 forward, TrainingEnemy enemy, int bonusDamage, GameObject source)
        {
            transform.position = origin;
            direction = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
            target = enemy;
            damage = bonusDamage;
            // Meshy chompers face the glTF front (+Z in Unity after import); the fallback opens toward +Z too.
            model = SupportVoxels.Model(source, transform, .78f, 0, SupportVoxels.ChomperVoxels);
            SupportVoxels.Glow(transform, Yellow, 1.6f, 2.5f);
            transform.rotation = Quaternion.LookRotation(direction);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (target && target.IsAlive)
            {
                Vector3 to = target.AimPoint - transform.position;
                if (to.magnitude < .55f) { Hit(); return; }
                direction = Vector3.RotateTowards(direction, to.normalized, 9f * dt, 0);
            }
            transform.position += direction * Speed * dt;
            transform.rotation = Quaternion.LookRotation(direction);
            // Chomp: squash the mouth axis like a two-frame sprite.
            float chomp = Mathf.Abs(Mathf.Sin(age * 18f));
            if (model) model.localScale = new Vector3(1, .78f + .22f * chomp, 1);
            pelletClock += dt;
            if (pelletClock > .06f)
            {
                pelletClock = 0;
                SupportVoxels.Burst(transform.position - direction * .3f, 1, .2f, .1f, new[] { new Color(1f, .95f, .7f) }, 2.5f);
            }
            if (age >= Lifetime || transform.position.y < -1) Destroy(gameObject);
        }

        void Hit()
        {
            target.TakeDamage(damage);
            SupportVoxels.Burst(transform.position, 12, 4f, .14f, new[] { Yellow, Color.white }, 2.2f);
            Destroy(gameObject);
        }
    }
}
