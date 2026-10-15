using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// Support character "꼬끼오", the mysterious skewer seller in a chicken costume. Passive support skill
    /// "헌터 야작 식당": every room enemy that goes down has a 33 % chance to leave a chicken skewer where its health hit
    /// zero (not where the body slides to). Walking into it heals 3 % of the hunter's maximum health. Skewers nobody
    /// picks up stay on the floor until the next stage (or the lobby) clears them.
    /// Works only while 꼬끼오 is the party's support (HunterLoadout.SupportId); the bottom-left HUD card shows his
    /// illustration on a blue operator monitor, like a support talking to the hunter over a comms link.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KokkioSupport : MonoBehaviour
    {
        public const string Id = SupportCharacterCatalog.Kokkio;

        [Header("Skill: 헌터 야작 식당")]
        [Range(0, 1)] public float dropChance = .33f;
        [Tooltip("Share of the hunter's maximum health one skewer restores.")]
        [Range(0, 1)] public float healFraction = .03f;
        [Tooltip("Horizontal distance (m) at which the hunter picks a skewer up.")]
        public float pickupRadius = 1.05f;
        public Texture2D portrait;

        public bool Selected => HunterLoadout.SupportId == Id
            && (!run || !run.Roster || !run.Roster.CurrentMovementOnly);
        public int Drops { get; private set; }
        public int Pickups { get; private set; }
        public int HealthRestored { get; private set; }
        public IReadOnlyList<KokkioSkewer> Skewers => skewers;

        readonly List<KokkioSkewer> skewers = new List<KokkioSkewer>();
        LiminalRunDirector run;
        LiminalHud hud;
        bool subscribed;

        // HUD
        RectTransform panel, monitor;
        TextMeshProUGUI fieldLabel, healLabel;
        RawImage scanlines;
        Texture2D scanlineTexture;
        float healShownUntil, pulse;
        static readonly Color Lcd = new Color(.33f, .74f, 1f);
        static readonly Color LcdDeep = new Color(.02f, .09f, .2f, 1);

        void Awake()
        {
            run = GetComponent<LiminalRunDirector>();
            if (!run) run = FindFirstObjectByType<LiminalRunDirector>();
            if (!portrait) portrait = Resources.Load<Texture2D>("SupportSkill/support_kokkio_portrait");
        }

        void OnEnable() => Subscribe(true);
        void OnDisable() => Subscribe(false);

        void OnDestroy()
        {
            ClearSkewers();
            if (scanlineTexture) Destroy(scanlineTexture);
        }

        void Subscribe(bool on)
        {
            if (!run || subscribed == on) return;
            subscribed = on;
            if (on) { run.EnemyKilled += OnEnemyKilled; run.StageChanged += ClearSkewers; }
            else { run.EnemyKilled -= OnEnemyKilled; run.StageChanged -= ClearSkewers; }
        }

        void Update()
        {
            if (!hud && run) hud = run.GetComponent<LiminalHud>();
            if (!panel && hud && hud.Canvas && hud.Font) BuildHud();
            bool selected = Selected;
            if (panel && panel.gameObject.activeSelf != selected) panel.gameObject.SetActive(selected);
            // Leaving the gate (or switching the support) takes the food off the floor.
            if (!selected || !run || run.Phase == LiminalRunPhase.Lobby) { if (skewers.Count > 0) ClearSkewers(); }
            else if (run.Phase == LiminalRunPhase.Exploring) Collect();
            RefreshHud();
        }

        void OnEnemyKilled(TrainingEnemy enemy)
        {
            if (!enemy || !Selected || !run || run.Phase == LiminalRunPhase.Lobby) return;
            if (Random.value >= dropChance) return;
            Drop(enemy.transform.position);
        }

        /// <summary>Puts a skewer on the floor at `point` (public for play tests).</summary>
        public KokkioSkewer Drop(Vector3 point)
        {
            // The floor under the spot where the enemy went down; the enemy's own height when nothing is found.
            if (Physics.Raycast(point + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)
                && !hit.collider.GetComponentInParent<TrainingEnemy>())
                point = hit.point;
            var skewer = KokkioSkewer.Create(point);
            skewers.Add(skewer);
            Drops++;
            pulse = 1;
            return skewer;
        }

        void Collect()
        {
            var player = run.player;
            var health = run.PlayerHealth;
            if (!player || !health || !health.IsAlive) return;
            for (int i = skewers.Count - 1; i >= 0; i--)
            {
                var skewer = skewers[i];
                if (!skewer) { skewers.RemoveAt(i); continue; }
                Vector3 offset = skewer.transform.position - player.position;
                if (Mathf.Abs(offset.y) > 2f) continue;
                offset.y = 0;
                if (offset.magnitude > pickupRadius) continue;
                int amount = Mathf.Max(1, Mathf.RoundToInt(health.maximumHealth * healFraction));
                int restored = health.Heal(amount);
                HealthRestored += restored;
                Pickups++;
                skewer.Consume();
                skewers.RemoveAt(i);
                healShownUntil = Time.time + 1.6f;
                if (healLabel) healLabel.text = $"회복 +{amount}";
                pulse = 1;
            }
        }

        public void ClearSkewers()
        {
            foreach (var skewer in skewers) if (skewer) Destroy(skewer.gameObject);
            skewers.Clear();
        }

        // ---- HUD ------------------------------------------------------------------------------------------
        void BuildHud()
        {
            var font = hud.Font;
            panel = HunterUi.Window("KokkioSupportCard", hud.Canvas, Vector2.zero, Vector2.zero, new Vector2(22, 58), new Vector2(338, 164), font, null, true);
            // Behind the lobby and gate windows, like the rest of the run HUD.
            panel.SetAsFirstSibling();

            // The operator monitor: a blue LCD screen with the illustration, scanlines and a bezel.
            var bezel = HunterUi.Fill("MonitorBezel", panel, new Vector2(0, 0), new Vector2(0, 0), new Vector2(8, 8), new Vector2(142, 150), new Color(.05f, .06f, .09f, 1)).rectTransform;
            monitor = HunterUi.Fill("Monitor", bezel, new Vector2(0, 0), new Vector2(0, 0), new Vector2(4, 4), new Vector2(134, 142), LcdDeep).rectTransform;
            monitor.gameObject.AddComponent<RectMask2D>();
            if (portrait)
            {
                var art = HunterUi.Rect("Illustration", monitor, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(134, 142));
                var image = art.gameObject.AddComponent<RawImage>();
                image.texture = portrait;
                image.raycastTarget = false;
                image.color = new Color(.82f, .93f, 1f);
                image.uvRect = UpperBody(portrait, 134f / 142f);
            }
            scanlineTexture = ScanlineTexture();
            var lines = HunterUi.Rect("Scanlines", monitor, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(134, 142));
            scanlines = lines.gameObject.AddComponent<RawImage>();
            scanlines.texture = scanlineTexture;
            scanlines.uvRect = new Rect(0, 0, 1, 142f / 3f);
            scanlines.raycastTarget = false;
            var nameplate = HunterUi.Fill("Nameplate", monitor, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(134, 24), new Color(.02f, .05f, .12f, .9f)).rectTransform;
            HunterUi.Text("Role", nameplate, font, "SUPPORT", 11, Lcd, new Vector2(6, 0), new Vector2(70, 24), FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            HunterUi.Text("Name", nameplate, font, "꼬끼오", 14, HunterUi.Cream, new Vector2(56, 0), new Vector2(74, 24), FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            HunterUi.Fill("LiveLamp", bezel, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -6), new Vector2(6, 6), new Color(.35f, 1f, .55f));

            HunterUi.Text("Skill", panel, font, "헌터 야작 식당", 17, HunterUi.Gold, new Vector2(160, -10), new Vector2(172, 28), FontStyles.Bold);
            HunterUi.Text("Effect", panel, font, "처치 시 33% 닭꼬치 드랍\n획득 시 최대 체력 3% 회복", 12, HunterUi.Cream, new Vector2(160, -40), new Vector2(172, 40));
            var chip = HunterUi.Fill("Passive", panel, new Vector2(0, 0), new Vector2(0, 0), new Vector2(160, 44), new Vector2(72, 22), new Color(.1f, .26f, .45f, 1)).rectTransform;
            HunterUi.Frame(chip, Lcd, 1);
            HunterUi.Text("PassiveLabel", chip, font, "PASSIVE", 11, Lcd, Vector2.zero, new Vector2(72, 22), FontStyles.Bold, TextAlignmentOptions.Center);
            fieldLabel = HunterUi.Text("Field", panel, font, "", 12, HunterUi.Muted, new Vector2(160, -128), new Vector2(172, 22));
            healLabel = HunterUi.Text("Heal", panel, font, "", 15, new Color(.55f, 1f, .6f), new Vector2(240, -100), new Vector2(92, 24), FontStyles.Bold, TextAlignmentOptions.TopRight);
            panel.gameObject.SetActive(Selected);
        }

        void RefreshHud()
        {
            if (!panel || !panel.gameObject.activeSelf) return;
            int onField = 0;
            foreach (var skewer in skewers) if (skewer) onField++;
            fieldLabel.text = onField > 0 ? $"필드의 닭꼬치  {onField}개" : "닭꼬치 대기 중";
            healLabel.gameObject.SetActive(Time.time < healShownUntil);
            pulse = Mathf.MoveTowards(pulse, 0, Time.unscaledDeltaTime * 2.5f);
            monitor.localScale = Vector3.one * (1 + .05f * Mathf.Sin(pulse * Mathf.PI));
            // A slow roll of the scanlines keeps the screen alive.
            scanlines.uvRect = new Rect(0, Time.unscaledTime * .6f, 1, 142f / 3f);
        }

        /// <summary>UV window over the upper body (head and shoulders) with the given aspect.</summary>
        static Rect UpperBody(Texture texture, float aspect)
        {
            float h = .62f;
            float w = Mathf.Min(1, h * texture.height * aspect / Mathf.Max(1, texture.width));
            return new Rect(Mathf.Clamp01(.39f - w * .5f), 1 - h, w, h);
        }

        static Texture2D ScanlineTexture()
        {
            var texture = new Texture2D(1, 3, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            texture.SetPixel(0, 0, new Color(.1f, .45f, .9f, .22f));
            texture.SetPixel(0, 1, new Color(0, 0, 0, 0));
            texture.SetPixel(0, 2, new Color(.25f, .6f, 1f, .08f));
            texture.Apply();
            return texture;
        }
    }

    /// <summary>A chicken skewer from 꼬끼오's stall, floating above the floor until the hunter walks into it.</summary>
    public sealed class KokkioSkewer : MonoBehaviour
    {
        static readonly Color Glaze = new Color(.62f, .27f, .1f);
        static readonly Color Leek = new Color(.62f, .82f, .36f);
        static readonly Color Stick = new Color(.86f, .74f, .52f);
        static readonly Color Warm = new Color(1f, .74f, .38f);
        Transform body;
        Vector3 basePosition;
        float age;

        public static KokkioSkewer Create(Vector3 point)
        {
            var skewer = new GameObject("Support Skill / Kokkio Skewer").AddComponent<KokkioSkewer>();
            skewer.transform.position = point;
            skewer.basePosition = point;
            skewer.Build();
            return skewer;
        }

        void Build()
        {
            body = new GameObject("Skewer").transform;
            body.SetParent(transform, false);
            body.localRotation = Quaternion.Euler(0, 0, 24);
            var stick = SupportVoxels.Mat(Stick);
            var glaze = SupportVoxels.Mat(Glaze, .35f);
            var leek = SupportVoxels.Mat(Leek, .2f);
            var rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(rod.GetComponent<Collider>());
            rod.transform.SetParent(body, false);
            rod.transform.localScale = new Vector3(.035f, .36f, .035f);
            rod.GetComponent<Renderer>().sharedMaterial = stick;
            // Negima: chicken, leek, chicken, leek, chicken from the tip down.
            for (int i = 0; i < 5; i++)
            {
                bool meat = i % 2 == 0;
                var piece = SupportVoxels.Cube(body, new Vector3(0, .26f - i * .11f, 0), meat ? .15f : .1f, meat ? glaze : leek);
                piece.localRotation = Quaternion.Euler(0, 20 * i, 8 * (i - 2));
            }
            SupportVoxels.Glow(transform, Warm, 1.6f, 2.2f);
            var ring = SupportVoxels.Ring(transform, new Color(Warm.r, Warm.g, Warm.b, .7f), .04f);
            SupportVoxels.SetRing(ring, basePosition, .45f);
            SupportVoxels.Burst(basePosition + Vector3.up * .5f, 8, 2.4f, .07f, new[] { Warm, Glaze }, 2f);
        }

        void Update()
        {
            age += Time.deltaTime;
            float pop = Mathf.Clamp01(age / .25f);
            body.localScale = Vector3.one * (pop * (1 + .12f * Mathf.Sin(Mathf.Min(1, age / .25f) * Mathf.PI)));
            body.position = basePosition + Vector3.up * (.62f + Mathf.Sin(age * 3f) * .07f);
            body.Rotate(Vector3.up, 80 * Time.deltaTime, Space.World);
        }

        /// <summary>Picked up: a warm burst and a soft chime, then gone.</summary>
        public void Consume()
        {
            SupportVoxels.Burst(body.position, 14, 3.4f, .08f, new[] { Warm, Leek, Glaze }, 3f);
            HitFeedback.Sparks(body.position, Vector3.up, 8, .7f);
            HitFeedback.Play(HitFeedback.Sfx.JustDodge, .35f, 1.7f);
            Destroy(gameObject);
        }
    }
}
