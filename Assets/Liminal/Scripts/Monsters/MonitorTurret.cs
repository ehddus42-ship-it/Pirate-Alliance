using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// An abandoned CRT monitor lying around the room (on the floor, tipped over, or on top of furniture) that
    /// works as a turret.
    /// - Its screen scrolls dim green binary.
    /// - It swivels toward the player in short servo jerks.
    /// - Before each shot the screen flares and an aim line appears. Then it fires a flat line of binary digits
    ///   (<see cref="BinaryProjectile"/>) at the player.
    /// </summary>
    public sealed class MonitorTurret : LiminalPropMonster
    {
        public float fireInterval = 2.8f;
        public float telegraphTime = .6f;
        public float projectileSpeed = 9.5f;
        public float range = 16f;
        public int damage = 10;
        public float servoStep = 22f;

        public int Shots { get; private set; }

        Quaternion rest = Quaternion.identity;
        float nextShot, servoClock, aimYaw, shotHeight;
        bool aiming;
        Vector3 aimDirection;
        GlyphStrip screen;
        MeshRenderer screenRenderer;
        Transform screenTransform;
        int[] screenBits;
        float screenScroll;
        const int ScreenColumns = 9, ScreenRows = 7;

        protected override int MaxHealth(int s) => 36 + s * 8;
        protected override float HitTilt => 16;
        protected override bool CanMove => false;
        protected override float KnockbackSpeed => 0;
        protected override float LyingHalfDepth => .12f;

        protected override void OnSetup()
        {
            displayName = "모니터";
            nextShot = Time.time + 1f + Random.value * fireInterval;
            aimYaw = transform.eulerAngles.y;
            BuildScreen();
        }

        /// <summary>Random liminal placement: tilted, tipped onto its side, or face up, but always able to see.</summary>
        public void Place(Quaternion tilt) { rest = tilt; poseRotation = tilt; }

        void BuildScreen()
        {
            var model = pose.Find("Model");
            var b = LiminalMonsterKit.LocalBounds(model, pose);
            // The CRT glass sits in the upper front of the housing.
            screenTransform = new GameObject("Screen").transform;
            screenTransform.SetParent(pose, false);
            screenTransform.localPosition = new Vector3(b.center.x, b.min.y + b.size.y * .6f, b.max.z + .006f);
            screen = new GlyphStrip(ScreenColumns * ScreenRows, "Monitor Screen");
            screenBits = new int[ScreenColumns * ScreenRows];
            for (int i = 0; i < screenBits.Length; i++) screenBits[i] = Random.value < .5f ? 0 : 1;
            screenTransform.gameObject.AddComponent<MeshFilter>().sharedMesh = screen.mesh;
            screenRenderer = screenTransform.gameObject.AddComponent<MeshRenderer>();
            screenRenderer.sharedMaterial = LiminalMonsterKit.GlyphMaterial;
            screenRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            screenWidth = b.size.x * .62f;
            screenHeight = b.size.y * .46f;
            shotHeight = .55f;
            DrawScreen(.35f);
        }

        float screenWidth, screenHeight;

        void DrawScreen(float brightness)
        {
            float cw = screenWidth / ScreenColumns, ch = screenHeight / ScreenRows;
            for (int r = 0; r < ScreenRows; r++)
                for (int c = 0; c < ScreenColumns; c++)
                {
                    int i = r * ScreenColumns + c;
                    float row = Mathf.Repeat(r - screenScroll, ScreenRows);
                    var center = new Vector3((c + .5f) * cw - screenWidth * .5f, screenHeight * .5f - (row + .5f) * ch, 0);
                    // The newest row (top) is brightest, older rows dim like phosphor afterglow.
                    float age = row / ScreenRows;
                    var color = new Color(.25f, 1f, .45f, brightness * (1 - .75f * age));
                    // The glass faces +Z, so the digits read from in front of the monitor.
                    screen.Set(i, center, Vector3.left * cw * .38f, Vector3.up * ch * .42f, screenBits[i], color);
                }
            screen.Apply();
        }

        protected override void Think(float dt, Vector3 to, float distance)
        {
            screenScroll += dt * (aiming ? 9f : 1.6f);
            if (Random.value < dt * 12) screenBits[Random.Range(0, screenBits.Length)] ^= 1;
            // Servo: snap toward the player in short jerks rather than turning smoothly.
            servoClock -= dt;
            if (servoClock <= 0 && to.sqrMagnitude > .1f && !aiming)
            {
                servoClock = .28f;
                float want = Quaternion.LookRotation(to).eulerAngles.y;
                aimYaw = Mathf.MoveTowardsAngle(aimYaw, want, servoStep);
            }
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0, aimYaw, 0), 420 * dt);
            float flare = 0;
            if (!aiming && Time.time >= nextShot && distance < range && CanSeePlayer(.6f))
            {
                aiming = true;
                nextShot = Time.time + telegraphTime;
                aimDirection = to.normalized;
                aimYaw = Quaternion.LookRotation(to).eulerAngles.y;
            }
            if (aiming)
            {
                IsWindingUp = true;
                float p = 1 - Mathf.Clamp01((nextShot - Time.time) / telegraphTime);
                // The aim locks a little before the shot, so a sidestep at the end dodges it.
                if (p < .55f && to.sqrMagnitude > .1f) aimDirection = to.normalized;
                flare = p;
                TelegraphLine(transform.position, aimDirection, Mathf.Min(range, distance + 2), .14f, p);
                poseRotation = rest * Quaternion.Euler(Mathf.Sin(Time.time * 70) * 1.5f * p, 0, 0);
                if (p >= 1)
                {
                    aiming = false;
                    IsWindingUp = false;
                    HideTelegraph();
                    Vector3 start = transform.position + aimDirection * .35f;
                    start.y = transform.position.y + shotHeight;
                    if (room) start.y = room.transform.position.y + .8f;
                    BinaryProjectile.Spawn(start, aimDirection, projectileSpeed, range, damage, transform, player);
                    Shots++;
                    HitFeedback.Sparks(screenTransform.position, aimDirection, 5, .5f);
                    nextShot = Time.time + fireInterval * Random.Range(.85f, 1.15f);
                    poseRotation = rest;
                }
            }
            DrawScreen(.35f + .9f * flare * flare + (Mathf.Sin(Time.time * 50) > .96f ? .2f : 0));
        }

        protected override void OnHit(Vector3 direction, float impact)
        {
            for (int i = 0; i < 6; i++) screenBits[Random.Range(0, screenBits.Length)] ^= 1;
        }

        protected override void OnDeathStart(Vector3 direction)
        {
            HitFeedback.Sparks(screenTransform ? screenTransform.position : transform.position, direction, 16, 1f);
            if (screenRenderer) screenRenderer.enabled = false;
            aiming = false;
        }

        protected override void OnDestroy()
        {
            if (screen != null && screen.mesh) Destroy(screen.mesh);
            base.OnDestroy();
        }

        public static MonitorTurret Create(Vector3 position, Quaternion rotation, Transform parent)
        {
            // The Meshy CRT is about 1.9 units tall, centred on its origin, screen toward +Z; scale it to ~0.5 m.
            const float scale = .26f;
            return LiminalMonsterKit.Build<MonitorTurret>("Monitor Turret", LiminalMonsterLibrary.Monitor, position, rotation, parent,
                .28f, .5f, new Vector3(.42f, .4f, .42f), out _, 0, scale, Vector3.up * (.951f * scale));
        }
    }
}
