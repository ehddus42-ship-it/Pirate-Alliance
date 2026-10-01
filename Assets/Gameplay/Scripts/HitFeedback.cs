using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike
{
    /// <summary>
    /// Melee hit feel: hit stop, camera shake, crescent slash arcs, hit sparks, sakura petals, white hit flash,
    /// damage numbers and landing dust. Everything is procedural (meshes + URP particle shaders), so it needs no
    /// prefabs and works in every scene. Colors follow the katana's crimson / sakura pink / violet flame palette.
    /// </summary>
    public static class HitFeedback
    {
        public static readonly Color Crimson = new Color(1f, .16f, .32f);
        public static readonly Color Sakura = new Color(1f, .58f, .8f);
        public static readonly Color Violet = new Color(.62f, .3f, 1f);
        public static readonly Color Spark = new Color(1f, .9f, .62f);

        static Material additive, blended;
        static Runner runner;
        static Font numberFont;
        static float stopScale = -1;
        static Coroutine stopRoutine;
        static bool slowMotion;
        static AudioSource sfxSource;
        static readonly Dictionary<Sfx, AudioClip> sfxClips = new Dictionary<Sfx, AudioClip>();

        public enum Sfx { Swing, Hit, HeavyHit, JustDodge, Hurt, Thud }

        sealed class Runner : MonoBehaviour { }

        static Runner Host
        {
            get
            {
                if (!runner)
                {
                    var go = new GameObject("HitFeedback") { hideFlags = HideFlags.HideAndDontSave };
                    Object.DontDestroyOnLoad(go);
                    runner = go.AddComponent<Runner>();
                }
                return runner;
            }
        }

        public static Material Additive
        {
            get
            {
                if (!additive) additive = Particle(true);
                return additive;
            }
        }

        public static Material Blended
        {
            get
            {
                if (!blended) blended = Particle(false);
                return blended;
            }
        }

        static Material Particle(bool add)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(shader);
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", add ? 2 : 0);
            m.SetFloat("_SrcBlend", add ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", add ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_Cull", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetColor("_BaseColor", Color.white);
            m.renderQueue = 3100;
            return m;
        }

        // ---- time and camera -----------------------------------------------------------------------------
        /// <summary>Briefly slows the game for impact. Never overrides a pause (time scale 0 or a phase change).</summary>
        public static void HitStop(float seconds, float scale = .06f)
        {
            if (seconds <= 0 || slowMotion) return;
            if (stopScale < 0 && Mathf.Abs(Time.timeScale - 1) > .001f) return;
            if (stopRoutine != null) Host.StopCoroutine(stopRoutine);
            stopRoutine = Host.StartCoroutine(HitStopRoutine(seconds, scale));
        }

        /// <summary>Ends a running hit stop at once (a dash must never be slowed by the hit it cancels).</summary>
        public static void CancelHitStop()
        {
            if (stopScale < 0 || slowMotion) return;
            if (stopRoutine != null && runner) runner.StopCoroutine(stopRoutine);
            stopRoutine = null;
            if (Mathf.Abs(Time.timeScale - stopScale) < .0001f) Time.timeScale = 1;
            stopScale = -1;
        }

        /// <summary>
        /// Just-dodge slow motion: the whole world drops to `scale` for `seconds` of real time, then eases back.
        /// Hit stops are ignored while it runs, and a new dash does not cancel it.
        /// </summary>
        public static void SlowMotion(float seconds, float scale)
        {
            if (seconds <= 0) return;
            if (stopScale < 0 && Mathf.Abs(Time.timeScale - 1) > .001f) return; // paused
            if (stopRoutine != null) Host.StopCoroutine(stopRoutine);
            slowMotion = true;
            stopRoutine = Host.StartCoroutine(HitStopRoutine(seconds, scale));
        }

        public static bool InSlowMotion => slowMotion;

        static IEnumerator HitStopRoutine(float seconds, float scale)
        {
            stopScale = scale;
            Time.timeScale = scale;
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end)
            {
                if (Mathf.Abs(Time.timeScale - stopScale) > .0001f) { stopScale = -1; slowMotion = false; yield break; } // paused meanwhile
                // Ease back in during the last third so the release does not pop.
                float left = (end - Time.unscaledTime) / seconds;
                if (left < .35f) Time.timeScale = stopScale = Mathf.Lerp(1, scale, left / .35f);
                yield return null;
            }
            if (Mathf.Abs(Time.timeScale - stopScale) < .0001f) Time.timeScale = 1;
            stopScale = -1;
            slowMotion = false;
        }

        public static void Shake(float amplitude, float duration)
        {
            var camera = Camera.main;
            var follow = camera ? camera.GetComponent<IsometricFollowCamera>() : null;
            if (follow) follow.AddShake(amplitude, duration);
        }

        // ---- slash arcs ----------------------------------------------------------------------------------
        /// <summary>
        /// Crescent slash: a ring segment around `center` facing `forward`, swept from angle `from` to `to`
        /// (degrees, 0 = forward, positive = to the character's right), tilted by `roll` around forward.
        /// </summary>
        public static void SlashArc(Vector3 center, Vector3 forward, float from, float to, float radius, float roll,
            float width, Color core, Color edge, float sweep = .07f, float life = .22f)
        {
            var go = new GameObject("Slash Arc");
            go.transform.position = center;
            forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            go.transform.rotation = Quaternion.LookRotation(forward) * Quaternion.Euler(0, 0, roll);
            var arc = go.AddComponent<SlashArcEffect>();
            arc.Play(from, to, radius, width, core, edge, sweep, life);
        }

        // ---- hit effects ----------------------------------------------------------------------------------
        public static void Hit(TrainingEnemy enemy, Vector3 point, Vector3 direction, int damage, bool heavy)
        {
            Sparks(point, direction, heavy ? 22 : 13, heavy ? 1.5f : 1f);
            Petals(point, heavy ? 12 : 6);
            Ring(point, direction, heavy ? 1.6f : 1.0f);
            if (enemy)
            {
                Flash(enemy);
                // The struck body shudders through the hit stop: the impact frame reads as a real collision.
                var body = enemy.transform.Find("Visual");
                Jitter(body ? body : enemy.transform.childCount > 0 ? enemy.transform.GetChild(0) : null, heavy ? .1f : .06f, heavy ? .2f : .13f);
            }
            DamageNumber(point + Vector3.up * .5f, damage, heavy);
            Play(heavy ? Sfx.HeavyHit : Sfx.Hit, heavy ? .9f : .7f);
        }

        /// <summary>Shakes a transform's local position for a moment of real time (works during hit stop).</summary>
        public static void Jitter(Transform target, float amplitude, float seconds)
        {
            if (!target) return;
            var jitter = target.GetComponent<ImpactJitter>();
            if (!jitter) jitter = target.gameObject.AddComponent<ImpactJitter>();
            jitter.Play(amplitude, seconds);
        }

        /// <summary>Tints every renderer under `root` for a moment (player hurt flash, generic objects).</summary>
        public static void FlashObject(GameObject root, Color color, float seconds = .12f)
        {
            if (!root) return;
            var flash = root.GetComponent<HitFlash>();
            if (!flash) flash = root.AddComponent<HitFlash>();
            flash.Trigger(color, seconds);
        }

        // ---- sound ----------------------------------------------------------------------------------------
        /// <summary>Procedurally synthesised one-shots, so the hit feel needs no audio assets.</summary>
        public static void Play(Sfx kind, float volume = 1, float pitch = 1)
        {
            if (!sfxSource)
            {
                sfxSource = Host.gameObject.AddComponent<AudioSource>();
                sfxSource.playOnAwake = false;
                sfxSource.spatialBlend = 0;
                sfxSource.ignoreListenerPause = false;
            }
            if (!sfxClips.TryGetValue(kind, out var clip) || !clip) sfxClips[kind] = clip = Synthesize(kind);
            sfxSource.pitch = pitch * Random.Range(.95f, 1.05f);
            sfxSource.PlayOneShot(clip, volume);
        }

        static AudioClip Synthesize(Sfx kind)
        {
            const int rate = 44100;
            float length = kind switch { Sfx.Swing => .2f, Sfx.Hit => .16f, Sfx.HeavyHit => .4f, Sfx.JustDodge => .9f, Sfx.Hurt => .3f, _ => .35f };
            int n = Mathf.CeilToInt(length * rate);
            var data = new float[n];
            var random = new System.Random((int)kind * 7919 + 13);
            float low = 0, band = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, u = t / length;
                float noise = (float)(random.NextDouble() * 2 - 1);
                float v = 0;
                switch (kind)
                {
                    case Sfx.Swing:
                    {
                        // Air whoosh: noise through a band that sweeps up, swelling then cut off.
                        float cutoff = Mathf.Lerp(.05f, .35f, u);
                        low += cutoff * (noise - low); band += cutoff * (low - band);
                        v = (low - band) * 3.2f * Mathf.Sin(Mathf.PI * Mathf.Pow(u, .7f));
                        break;
                    }
                    case Sfx.Hit:
                    {
                        // Slash impact: a bright crack, then a short low thump.
                        float crack = noise * Mathf.Exp(-t * 90f);
                        float thump = Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(180, 70, u) * t) * Mathf.Exp(-t * 28f);
                        v = crack * .7f + thump * .8f;
                        break;
                    }
                    case Sfx.HeavyHit:
                    {
                        float crack = noise * Mathf.Exp(-t * 45f);
                        float thump = Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(120, 45, u) * t) * Mathf.Exp(-t * 11f);
                        float ring = Mathf.Sin(2 * Mathf.PI * 1250 * t) * Mathf.Exp(-t * 9f) * .25f;
                        v = crack * .6f + thump + ring;
                        break;
                    }
                    case Sfx.JustDodge:
                    {
                        // Glassy time-stop chime over a reversed swell of air.
                        float chime = (Mathf.Sin(2 * Mathf.PI * 1320 * t) + .6f * Mathf.Sin(2 * Mathf.PI * 1980 * t) + .3f * Mathf.Sin(2 * Mathf.PI * 2640 * t))
                                      * Mathf.Exp(-t * 4.5f) * Mathf.Clamp01(t * 60f);
                        low += .08f * (noise - low);
                        float swell = low * Mathf.Pow(Mathf.Clamp01(1 - u * 3f), 2) * 2.5f;
                        v = chime * .35f + swell;
                        break;
                    }
                    case Sfx.Hurt:
                    {
                        float thud = Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(140, 60, u) * t) * Mathf.Exp(-t * 14f);
                        v = Mathf.Clamp(thud * 1.6f, -1, 1) * .8f + noise * Mathf.Exp(-t * 60f) * .5f;
                        break;
                    }
                    default:
                    {
                        float thud = Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(80, 38, u) * t) * Mathf.Exp(-t * 9f);
                        low += .05f * (noise - low);
                        v = thud + low * Mathf.Exp(-t * 12f) * 2f;
                        break;
                    }
                }
                data[i] = Mathf.Clamp(v, -1, 1) * .85f;
            }
            var clip = AudioClip.Create("Sfx " + kind, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static void Sparks(Vector3 point, Vector3 direction, int count, float scale)
        {
            var go = new GameObject("Hit Sparks");
            go.transform.position = point;
            go.AddComponent<ShardBurst>().Play(count, direction, scale, true);
        }

        public static void Petals(Vector3 point, int count)
        {
            var go = new GameObject("Sakura Petals");
            go.transform.position = point;
            go.AddComponent<ShardBurst>().Play(count, Vector3.up, 1, false);
        }

        public static void Ring(Vector3 point, Vector3 direction, float size)
        {
            var go = new GameObject("Hit Ring");
            go.transform.position = point;
            var face = Camera.main ? point - Camera.main.transform.position : direction;
            go.transform.rotation = Quaternion.LookRotation(face);
            go.AddComponent<RingEffect>().Play(size, Sakura, .16f);
        }

        public static void Dust(Vector3 point, float scale)
        {
            var go = new GameObject("Landing Dust");
            go.transform.position = point + Vector3.up * .05f;
            go.transform.rotation = Quaternion.LookRotation(Vector3.up);
            go.AddComponent<RingEffect>().Play(1.8f * scale, new Color(.85f, .8f, .7f, .55f), .35f, false);
        }

        /// <summary>Turns every renderer of the enemy white for a moment.</summary>
        public static void Flash(TrainingEnemy enemy)
        {
            var flash = enemy.GetComponent<HitFlash>();
            if (!flash) flash = enemy.gameObject.AddComponent<HitFlash>();
            flash.Trigger();
        }

        public static void DamageNumber(Vector3 point, int amount, bool heavy)
        {
            if (!numberFont) numberFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Damage " + amount);
            go.transform.position = point + new Vector3(Random.Range(-.35f, .35f), 0, Random.Range(-.2f, .2f));
            var text = go.AddComponent<TextMesh>();
            text.text = amount.ToString();
            text.font = numberFont;
            text.fontSize = 64;
            text.characterSize = heavy ? .052f : .038f;
            text.anchor = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
            text.color = heavy ? new Color(1f, .45f, .62f) : new Color(1f, .93f, .82f);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = numberFont.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<FloatingNumber>().Play(heavy);
        }

        // ---- anime cut accents -----------------------------------------------------------------------------
        /// <summary>
        /// Razor-thin cut streak through `point`, facing the camera: snaps open along the cut, then thins away.
        /// `angle` turns it around the view axis (0 = level on screen, positive = counter-clockwise).
        /// </summary>
        public static void SlashLine(Vector3 point, float angle, float length, float width, Color color, float life = .2f, float delay = 0)
        {
            var go = new GameObject("Slash Line");
            go.transform.position = point;
            go.AddComponent<LineFlash>().Play(angle, length, width, color, life, delay);
        }

        /// <summary>Two crossing streaks over a target, the second a moment later: the finisher's X cut.</summary>
        public static void CrossSlash(Vector3 point, float size)
        {
            SlashLine(point, 38, 2.6f * size, .16f * size, Sakura * 2.4f, .26f);
            SlashLine(point, -42, 2.6f * size, .16f * size, Color.white * 2.2f, .26f, .06f);
            SlashLine(point, 38, 3.4f * size, .05f * size, Violet * 2f, .34f);
        }

        /// <summary>Full-screen additive flash (a camera-facing quad just in front of the near plane).</summary>
        public static void ScreenFlash(Color color, float life)
        {
            var camera = Camera.main;
            if (!camera) return;
            var go = new GameObject("Screen Flash");
            go.transform.SetParent(camera.transform, false);
            go.AddComponent<ScreenFlashEffect>().Play(camera, color, life);
        }

        /// <summary>Glowing snapshot of the character's current pose that fades in place (dash and draw afterimages).</summary>
        public static void Afterimage(SkinnedMeshRenderer[] skins, Color color, float life)
        {
            if (skins == null) return;
            foreach (var skin in skins)
            {
                if (!skin || !skin.enabled || !skin.gameObject.activeInHierarchy || !skin.sharedMesh) continue;
                var go = new GameObject("Afterimage");
                go.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                go.AddComponent<AfterimageEffect>().Play(skin, color, life);
            }
        }

        // ---- mesh helpers ---------------------------------------------------------------------------------
        internal static Mesh Quad()
        {
            var mesh = new Mesh { name = "Feedback Quad" };
            mesh.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            return mesh;
        }

        static Mesh quad;
        internal static Mesh SharedQuad => quad ? quad : quad = Quad();
    }

    /// <summary>Crescent mesh that sweeps open, then thins and fades. Bright outer edge, transparent inner edge.</summary>
    sealed class SlashArcEffect : MonoBehaviour
    {
        Mesh mesh;
        MeshRenderer meshRenderer;
        float from, to, radius, width, sweep, life, age;
        Color core, edge;
        const int Segments = 40;
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();

        public void Play(float a, float b, float r, float w, Color c, Color e, float sweepTime, float lifeTime)
        {
            from = a; to = b; radius = r; width = w; core = c; edge = e; sweep = sweepTime; life = lifeTime;
            mesh = new Mesh { name = "Slash Arc" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = HitFeedback.Additive;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var triangles = new int[Segments * 12];
            for (int i = 0; i < Segments; i++)
            {
                int k = i * 3, n = k + 3, t = i * 12;
                // inner->mid and mid->outer strips
                triangles[t] = k; triangles[t + 1] = n; triangles[t + 2] = k + 1;
                triangles[t + 3] = k + 1; triangles[t + 4] = n; triangles[t + 5] = n + 1;
                triangles[t + 6] = k + 1; triangles[t + 7] = n + 1; triangles[t + 8] = k + 2;
                triangles[t + 9] = k + 2; triangles[t + 10] = n + 1; triangles[t + 11] = n + 2;
            }
            Rebuild();
            mesh.triangles = triangles;
        }

        void Rebuild()
        {
            float open = Mathf.Clamp01(age / Mathf.Max(.001f, sweep));
            float fade = Mathf.Clamp01((age - sweep) / Mathf.Max(.001f, life));
            float grow = 1 + .12f * fade;
            vertices.Clear();
            colors.Clear();
            for (int i = 0; i <= Segments; i++)
            {
                float u = i / (float)Segments;
                float along = Mathf.Lerp(from, Mathf.Lerp(from, to, open), u);
                float rad = along * Mathf.Deg2Rad;
                // Crescent: thickest in the middle of the swept part, pointed at both ends.
                float thickness = width * Mathf.Sin(u * Mathf.PI) * (1 - .7f * fade);
                Vector3 dir = new Vector3(Mathf.Sin(rad), 0, Mathf.Cos(rad));
                float outer = radius * grow;
                // The leading tip is hottest; older parts of the swing cool toward the edge color.
                float heat = Mathf.Lerp(.35f, 1f, u) * (1 - fade);
                vertices.Add(dir * (outer - thickness));
                vertices.Add(dir * (outer - thickness * .35f));
                vertices.Add(dir * outer);
                colors.Add(new Color(edge.r, edge.g, edge.b, 0));
                colors.Add(new Color(edge.r, edge.g, edge.b, .8f * heat));
                colors.Add(new Color(core.r, core.g, core.b, heat));
            }
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.RecalculateBounds();
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= sweep + life) { Destroy(mesh); Destroy(gameObject); return; }
            Rebuild();
        }
    }

    /// <summary>Spark streaks (additive, stretched along their velocity) or sakura petals (tumbling, drifting).</summary>
    sealed class ShardBurst : MonoBehaviour
    {
        struct Shard { public Transform t; public Vector3 v; public float size, spin; }
        readonly List<Shard> shards = new List<Shard>();
        bool sparks;
        float age, life;
        MaterialPropertyBlock block;

        public void Play(int count, Vector3 direction, float scale, bool spark)
        {
            sparks = spark;
            life = spark ? .32f : 1.1f;
            block = new MaterialPropertyBlock();
            direction = direction.sqrMagnitude > .01f ? direction.normalized : Vector3.up;
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Shard");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = HitFeedback.SharedQuad;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = spark ? HitFeedback.Additive : HitFeedback.Blended;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Color c = spark ? Color.Lerp(HitFeedback.Spark, HitFeedback.Sakura, Random.value * .5f)
                    : Color.Lerp(HitFeedback.Sakura, Color.white, Random.value * .45f);
                block.SetColor("_BaseColor", c * (spark ? 2.2f : 1f));
                r.SetPropertyBlock(block);
                Vector3 v = spark
                    ? (direction + Random.insideUnitSphere * .9f).normalized * Random.Range(6f, 11f) * scale
                    : (Random.insideUnitSphere + Vector3.up * .6f) * Random.Range(1.2f, 2.6f);
                shards.Add(new Shard { t = go.transform, v = v, size = spark ? Random.Range(.05f, .09f) * scale : Random.Range(.09f, .15f), spin = Random.Range(-720f, 720f) });
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            float k = 1 - Mathf.Clamp01(age / life);
            var camera = Camera.main;
            for (int i = 0; i < shards.Count; i++)
            {
                var s = shards[i];
                if (!s.t) continue;
                if (sparks)
                {
                    s.v *= Mathf.Exp(-7f * dt);
                    s.v += Physics.gravity * (.25f * dt);
                    s.t.position += s.v * dt;
                    // Stretch along velocity, face the camera.
                    Vector3 view = camera ? (s.t.position - camera.transform.position).normalized : Vector3.forward;
                    if (s.v.sqrMagnitude > .001f) s.t.rotation = Quaternion.LookRotation(view, Vector3.Cross(view, s.v));
                    s.t.localScale = new Vector3(s.size * (1 + s.v.magnitude * .12f), s.size * .35f, 1) * k;
                }
                else
                {
                    s.v += (Vector3.down * 2.2f - s.v * 1.6f) * dt;
                    s.t.position += (s.v + new Vector3(Mathf.Sin(age * 6 + i), 0, Mathf.Cos(age * 5 + i)) * .35f) * dt;
                    s.t.Rotate(new Vector3(s.spin * .7f, s.spin, s.spin * .4f) * dt, Space.Self);
                    s.t.localScale = new Vector3(s.size, s.size * .62f, 1) * Mathf.Clamp01(k * 2.5f);
                }
                shards[i] = s;
            }
            if (age >= life) Destroy(gameObject);
        }
    }

    /// <summary>Expanding ring quad drawn procedurally as a line loop.</summary>
    sealed class RingEffect : MonoBehaviour
    {
        LineRenderer line;
        float size, life, age;
        Color color;

        public void Play(float ringSize, Color ringColor, float lifeTime, bool glow = true)
        {
            size = ringSize; color = ringColor; life = lifeTime;
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 36;
            line.sharedMaterial = glow ? HitFeedback.Additive : HitFeedback.Blended;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.alignment = LineAlignment.TransformZ;
            Update();
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / life);
            float r = size * (.25f + .75f * (1 - (1 - t) * (1 - t)));
            for (int i = 0; i < line.positionCount; i++)
            {
                float a = i * Mathf.PI * 2 / line.positionCount;
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0));
            }
            line.widthMultiplier = .14f * size * (1 - t) + .01f;
            var c = new Color(color.r, color.g, color.b, color.a * (1 - t));
            line.startColor = line.endColor = c;
            if (t >= 1) Destroy(gameObject);
        }
    }

    /// <summary>White flash on every renderer of a hit enemy (property block, restored afterwards).</summary>
    sealed class HitFlash : MonoBehaviour
    {
        Renderer[] renderers;
        MaterialPropertyBlock block;
        float until;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");

        Color tint = new Color(1f, .93f, .96f), glow = new Color(1f, .7f, .85f) * 1.5f;

        public void Trigger() => Trigger(new Color(1f, .93f, .96f), .09f);

        public void Trigger(Color color, float seconds)
        {
            tint = color;
            glow = color * 1.5f;
            if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
            block ??= new MaterialPropertyBlock();
            // Real time, so the flash does not linger through a hit stop.
            until = Time.unscaledTime + seconds;
            enabled = true;
            Apply(true);
        }

        void Update()
        {
            if (Time.unscaledTime < until) return;
            Apply(false);
            enabled = false;
        }

        void Apply(bool on)
        {
            foreach (var r in renderers)
            {
                if (!r || r is LineRenderer || r is TrailRenderer) continue;
                if (on)
                {
                    block.Clear();
                    block.SetColor(BaseColor, tint);
                    block.SetColor(Emission, glow);
                    r.SetPropertyBlock(block);
                }
                else r.SetPropertyBlock(null);
            }
        }
    }

    /// <summary>Damage number that pops, rises and fades while facing the camera.</summary>
    sealed class FloatingNumber : MonoBehaviour
    {
        TextMesh text;
        float age;
        bool heavy;
        Color color;
        Vector3 start;

        public void Play(bool big)
        {
            heavy = big;
            text = GetComponent<TextMesh>();
            color = text.color;
            start = transform.position;
        }

        void Update()
        {
            age += Time.unscaledDeltaTime;
            float t = age / .75f;
            var camera = Camera.main;
            if (camera) transform.rotation = camera.transform.rotation;
            float pop = age < .1f ? 1 + 1.2f * (1 - age / .1f) : 1;
            transform.localScale = Vector3.one * pop * (heavy ? 1.15f : 1f);
            transform.position = start + Vector3.up * (.9f * (1 - (1 - Mathf.Min(t, 1)) * (1 - Mathf.Min(t, 1))));
            text.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(1.6f - t * 1.6f));
            if (t >= 1) Destroy(gameObject);
        }
    }

    /// <summary>Camera-facing streak quad: opens along its length, then its width collapses.</summary>
    sealed class LineFlash : MonoBehaviour
    {
        Transform quad;
        MeshRenderer quadRenderer;
        MaterialPropertyBlock block;
        float angle, length, width, life, delay, age;
        Color color;

        public void Play(float a, float l, float w, Color c, float lifeTime, float wait)
        {
            angle = a; length = l; width = w; color = c; life = lifeTime; delay = wait;
            var go = new GameObject("Quad");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = HitFeedback.SharedQuad;
            quadRenderer = go.AddComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = HitFeedback.Additive;
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quad = go.transform;
            block = new MaterialPropertyBlock();
            Update();
        }

        void Update()
        {
            age += Time.unscaledDeltaTime;
            float t = (age - delay) / life;
            quadRenderer.enabled = t >= 0;
            if (t < 0) return;
            if (t >= 1) { Destroy(gameObject); return; }
            var camera = Camera.main;
            if (camera) transform.rotation = camera.transform.rotation * Quaternion.Euler(0, 0, angle);
            float open = Mathf.Clamp01(t / .18f);
            float thin = t < .18f ? 1 : 1 - (t - .18f) / .82f;
            quad.localScale = new Vector3(length * (1 - (1 - open) * (1 - open)) * (1 + .15f * t), width * thin * thin, 1);
            block.SetColor("_BaseColor", color * Mathf.Lerp(1.4f, .6f, t));
            quadRenderer.SetPropertyBlock(block);
        }
    }

    /// <summary>Additive quad covering the view, fading out quickly.</summary>
    sealed class ScreenFlashEffect : MonoBehaviour
    {
        MeshRenderer quadRenderer;
        MaterialPropertyBlock block;
        Color color;
        float life, age;

        public void Play(Camera camera, Color c, float lifeTime)
        {
            color = c; life = lifeTime;
            float distance = camera.nearClipPlane + .05f;
            transform.localPosition = Vector3.forward * distance;
            transform.localRotation = Quaternion.identity;
            float h = camera.orthographic ? camera.orthographicSize * 2 : 2 * distance * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            transform.localScale = new Vector3(h * camera.aspect * 1.2f, h * 1.2f, 1);
            gameObject.AddComponent<MeshFilter>().sharedMesh = HitFeedback.SharedQuad;
            quadRenderer = gameObject.AddComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = HitFeedback.Additive;
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            block = new MaterialPropertyBlock();
            Update();
        }

        void Update()
        {
            age += Time.unscaledDeltaTime;
            float t = age / life;
            if (t >= 1) { Destroy(gameObject); return; }
            block.SetColor("_BaseColor", new Color(color.r, color.g, color.b, color.a * (1 - t) * (1 - t)));
            quadRenderer.SetPropertyBlock(block);
        }
    }

    /// <summary>Baked copy of a skinned mesh in its current pose, glowing and fading.</summary>
    sealed class AfterimageEffect : MonoBehaviour
    {
        Mesh mesh;
        MeshRenderer meshRenderer;
        MaterialPropertyBlock block;
        Color color;
        float life, age;

        public void Play(SkinnedMeshRenderer skin, Color c, float lifeTime)
        {
            color = c; life = lifeTime;
            mesh = new Mesh { name = "Afterimage" };
            skin.BakeMesh(mesh, true);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            var materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < materials.Length; i++) materials[i] = HitFeedback.Additive;
            meshRenderer.sharedMaterials = materials;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            block = new MaterialPropertyBlock();
            Update();
        }

        void Update()
        {
            age += Time.unscaledDeltaTime;
            float t = age / life;
            if (t >= 1) { Destroy(mesh); Destroy(gameObject); return; }
            block.SetColor("_BaseColor", color * ((1 - t) * (1 - t)));
            meshRenderer.SetPropertyBlock(block);
            transform.localScale = Vector3.one * (1 + .04f * t);
        }
    }

    /// <summary>Real-time positional shudder of a struck body, decaying to rest.</summary>
    sealed class ImpactJitter : MonoBehaviour
    {
        float amplitude, until, duration;
        Vector3 offset;

        public void Play(float amount, float seconds)
        {
            amplitude = Mathf.Max(amplitude * Mathf.Clamp01((until - Time.unscaledTime) / Mathf.Max(.001f, duration)), amount);
            duration = seconds;
            until = Time.unscaledTime + seconds;
            enabled = true;
        }

        void LateUpdate()
        {
            transform.localPosition -= offset;
            float left = (until - Time.unscaledTime) / Mathf.Max(.001f, duration);
            if (left <= 0) { offset = Vector3.zero; enabled = false; return; }
            float a = amplitude * left * left;
            offset = new Vector3(Random.Range(-a, a), Random.Range(-a, a) * .35f, Random.Range(-a, a));
            transform.localPosition += offset;
        }

        void OnDisable() { transform.localPosition -= offset; offset = Vector3.zero; }
    }
}
