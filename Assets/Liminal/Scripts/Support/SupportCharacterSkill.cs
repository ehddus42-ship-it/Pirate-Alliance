using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// Support character "유니" (a gamer girl in a bunny hoodie). She does not fight; her active skill borrows objects from
    /// games she played: one voxel meteor falls on the nearest enemy, then a voxel 1UP floats over the player for five
    /// seconds, and every talisman cast during that time also fires a voxel chomper for extra damage. Casting grants
    /// brief invulnerability (support skills work like a bomb). The bottom-left HUD card shows her illustration, the
    /// skill cooldown, the key and the 1UP time left. Models come from Resources/SupportSkill (Meshy voxel GLBs) with
    /// procedural voxel fallbacks, so the skill works even before the models are imported.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupportCharacterSkill : MonoBehaviour
    {
        [Header("Skill")]
        public string characterName = "유니";
        public string skillName = "보너스 스테이지!";
        public float cooldown = 16f;
        public float meteorFallTime = .85f;
        public float meteorRadius = 3.4f;
        public int meteorDamage = 90;
        public float oneUpDuration = 5f;
        public int chomperDamage = 18;
        public float castInvulnerability = 1.3f;
        public float meteorSearchRange = 18f;

        [Header("Resources (optional overrides)")]
        public Texture2D portrait;
        public GameObject meteorModel, oneUpModel, chomperModel;

        public float CooldownRemaining => Mathf.Max(0, readyAt - Time.time);
        public float OneUpRemaining => Mathf.Max(0, oneUpUntil - Time.time);
        public bool OneUpActive => OneUpRemaining > 0;
        public int Activations { get; private set; }
        public int ChompersFired { get; private set; }

        LiminalRunDirector run;
        TalismanCaster caster;
        InputAction action;
        float readyAt, oneUpUntil, portraitPulse;
        int lastCastCount;
        SupportOneUp activeOneUp;

        // HUD
        TMP_FontAsset font;
        RectTransform panel, portraitFrame;
        Image cooldownShade, slotBorder, buffFill;
        TextMeshProUGUI cooldownLabel, buffLabel, stateLabel;
        float hudSearchUntil;
        static readonly Color Panel = new Color(.045f, .075f, .075f, .92f);
        static readonly Color Paper = new Color(.89f, .91f, .82f);
        static readonly Color Accent = new Color(.72f, .86f, .55f);
        static readonly Color Fluorescent = new Color(.97f, .92f, .62f);
        static readonly Color Wallpaper = new Color(.78f, .71f, .43f);
        static Sprite white;

        void Awake()
        {
            run = GetComponent<LiminalRunDirector>();
            if (!run) run = FindFirstObjectByType<LiminalRunDirector>();
            action = new InputAction("SupportSkill", InputActionType.Button);
            action.AddBinding("<Keyboard>/q");
            action.AddBinding("<Gamepad>/leftShoulder");
            if (!portrait) portrait = Resources.Load<Texture2D>("SupportSkill/support_yuni_portrait");
            if (!meteorModel) meteorModel = Resources.Load<GameObject>("SupportSkill/voxel_meteor");
            if (!oneUpModel) oneUpModel = Resources.Load<GameObject>("SupportSkill/voxel_1up");
            if (!chomperModel) chomperModel = Resources.Load<GameObject>("SupportSkill/voxel_chomper");
            hudSearchUntil = Time.unscaledTime + 1.5f;
        }

        void OnEnable() => action?.Enable();
        void OnDisable() => action?.Disable();
        void OnDestroy()
        {
            action?.Dispose();
            if (font) Destroy(font);
        }

        void Update()
        {
            if (!panel) TryBuildHud();
            var player = run ? run.player : null;
            if (player && (!caster || caster.transform != player))
            {
                caster = player.GetComponent<TalismanCaster>();
                lastCastCount = caster ? caster.CastCount : 0;
            }
            bool playing = run && run.Phase == LiminalRunPhase.Exploring && Time.timeScale > 0 && player
                           && (!run.PlayerHealth || run.PlayerHealth.IsAlive);
            if (playing && action.WasPressedThisFrame()) TryActivate();
            if (caster)
            {
                if (caster.CastCount != lastCastCount && OneUpActive && playing)
                    for (int i = lastCastCount; i < caster.CastCount; i++) FireChomper();
                lastCastCount = caster.CastCount;
            }
            RefreshHud();
        }

        /// <summary>Starts the skill if it is ready. Public for automated play tests.</summary>
        public bool TryActivate()
        {
            if (!run || !run.player || CooldownRemaining > 0) return false;
            Activations++;
            readyAt = Time.time + cooldown;
            portraitPulse = 1;
            if (run.PlayerHealth) run.PlayerHealth.GrantInvulnerability(castInvulnerability);
            Vector3 target = MeteorTarget(run.player);
            var meteor = new GameObject("Support Skill / Voxel Meteor").AddComponent<SupportMeteor>();
            meteor.Launch(this, target, meteorFallTime, meteorRadius, meteorDamage, meteorModel);
            var hud = run.GetComponent<LiminalHud>();
            if (hud) hud.Notify($"{characterName}: \"{skillName}\"\n운석 낙하 → 1UP {oneUpDuration:0}초 · 공격마다 추가 투사체", 2.6f);
            return true;
        }

        internal void OnMeteorLanded()
        {
            if (!run || !run.player) return;
            oneUpUntil = Time.time + oneUpDuration;
            if (activeOneUp) Destroy(activeOneUp.gameObject);
            activeOneUp = new GameObject("Support Skill / Voxel 1UP").AddComponent<SupportOneUp>();
            activeOneUp.Show(run.player, oneUpDuration, oneUpModel);
        }

        Vector3 MeteorTarget(Transform player)
        {
            TrainingEnemy best = null;
            float bestDistance = meteorSearchRange * meteorSearchRange;
            foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
            {
                if (!enemy.IsAlive || !enemy.CanBeTargeted) continue;
                float d = (enemy.transform.position - player.position).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = enemy; }
            }
            if (best) return new Vector3(best.transform.position.x, player.position.y, best.transform.position.z);
            Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up);
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            return player.position + forward.normalized * 5f;
        }

        void FireChomper()
        {
            Transform player = run.player;
            Vector3 origin = caster && caster.castOrigin ? caster.castOrigin.position : player.position + Vector3.up * 1.1f;
            TrainingEnemy target = caster && caster.LastTarget && caster.LastTarget.IsAlive ? caster.LastTarget : null;
            Vector3 side = Vector3.Cross(Vector3.up, player.forward).normalized;
            var chomper = new GameObject("Support Skill / Voxel Chomper").AddComponent<SupportChomper>();
            chomper.Launch(origin + side * (ChompersFired % 2 == 0 ? .45f : -.45f), player.forward, target, chomperDamage, chomperModel);
            ChompersFired++;
        }

        // ---- HUD --------------------------------------------------------------------------------------------
        void TryBuildHud()
        {
            Canvas canvas = null;
            var hudRoot = run ? run.transform.Find("LiminalHUD") : null;
            if (hudRoot) canvas = hudRoot.GetComponent<Canvas>();
            if (!canvas && Time.unscaledTime < hudSearchUntil) return;
            if (!canvas)
            {
                var go = new GameObject("SupportSkillHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                go.transform.SetParent(transform, false);
                canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 99;
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720);
                scaler.matchWidthOrHeight = .5f;
            }
            BuildHud(canvas.transform);
        }

        void BuildHud(Transform canvas)
        {
            if (!white) white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f));
            Font source = run && run.hudFont ? run.hudFont : null;
            font = source ? TMP_FontAsset.CreateFontAsset(source, 40, 5, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                1024, 1024, AtlasPopulationMode.Dynamic, true) : TMP_Settings.defaultFontAsset;

            // Card: dark HUD panel with a fluorescent tube on top, like the ceiling lights of the stage.
            panel = Node("SupportSkillCard", canvas, new Vector2(22, 58), new Vector2(338, 164), Vector2.zero);
            Img(panel, Panel);
            var tube = Node("Fluorescent", panel, new Vector2(0, 161), new Vector2(338, 3), Vector2.zero);
            Img(tube, Fluorescent);

            // Illustration window: yellow liminal wallpaper behind the character, clipped to the frame.
            portraitFrame = Node("Portrait", panel, new Vector2(8, 8), new Vector2(142, 150), Vector2.zero);
            var wall = portraitFrame.gameObject.AddComponent<RawImage>();
            wall.texture = WallpaperTexture();
            wall.uvRect = new Rect(0, 0, 3, 1);
            portraitFrame.gameObject.AddComponent<RectMask2D>();
            if (portrait)
            {
                var art = Node("Illustration", portraitFrame, new Vector2(-50, -140), new Vector2(250, 313), Vector2.zero);
                art.gameObject.AddComponent<RawImage>().texture = portrait;
            }
            var edge = Node("PortraitEdge", portraitFrame, Vector2.zero, new Vector2(142, 4), Vector2.zero);
            Img(edge, Accent);
            var nameplate = Node("Nameplate", portraitFrame, new Vector2(0, 4), new Vector2(142, 24), Vector2.zero);
            Img(nameplate, new Color(.03f, .05f, .05f, .82f));
            Label(nameplate, "SUPPORT", 12, Accent, new Vector2(8, 0), new Vector2(70, 24), TextAlignmentOptions.MidlineLeft);
            Label(nameplate, characterName, 15, Paper, new Vector2(62, 0), new Vector2(74, 24), TextAlignmentOptions.MidlineRight);

            // Skill info and slot.
            Label(panel, skillName, 17, Accent, new Vector2(160, 126), new Vector2(172, 28), TextAlignmentOptions.MidlineLeft);
            Label(panel, "운석 1회 · 1UP 동안\n공격마다 추가 투사체", 12, Paper, new Vector2(160, 94), new Vector2(172, 34), TextAlignmentOptions.TopLeft);

            var slot = Node("SkillSlot", panel, new Vector2(160, 16), new Vector2(72, 72), Vector2.zero);
            slotBorder = Img(slot, Accent);
            var inner = Node("Inner", slot, new Vector2(3, 3), new Vector2(66, 66), Vector2.zero);
            Img(inner, new Color(.02f, .03f, .035f, 1));
            var icon = Node("Icon", inner, new Vector2(13, 5), new Vector2(40, 40), Vector2.zero);
            var iconImage = icon.gameObject.AddComponent<RawImage>();
            iconImage.texture = IconTexture();
            Label(inner, "1UP", 12, new Color(.55f, 1f, .45f), new Vector2(0, 47), new Vector2(66, 18), TextAlignmentOptions.Center);
            var shade = Node("Cooldown", inner, Vector2.zero, new Vector2(66, 66), Vector2.zero);
            cooldownShade = Img(shade, new Color(0, 0, 0, .72f));
            cooldownShade.type = Image.Type.Filled;
            cooldownShade.fillMethod = Image.FillMethod.Radial360;
            cooldownShade.fillOrigin = (int)Image.Origin360.Top;
            cooldownShade.fillClockwise = false;
            cooldownLabel = Label(inner, "", 22, Paper, Vector2.zero, new Vector2(66, 66), TextAlignmentOptions.Center);
            var key = Node("Key", slot, new Vector2(50, -8), new Vector2(28, 22), Vector2.zero);
            Img(key, Fluorescent);
            Label(key, "Q", 15, new Color(.06f, .07f, .06f), Vector2.zero, new Vector2(28, 22), TextAlignmentOptions.Center);

            stateLabel = Label(panel, "", 13, Paper, new Vector2(242, 60), new Vector2(92, 26), TextAlignmentOptions.MidlineLeft);
            var track = Node("OneUpTrack", panel, new Vector2(242, 26), new Vector2(88, 8), Vector2.zero);
            Img(track, new Color(.22f, .26f, .23f));
            var fill = Node("OneUpFill", track, Vector2.zero, new Vector2(88, 8), Vector2.zero);
            buffFill = Img(fill, new Color(.55f, 1f, .45f));
            buffFill.type = Image.Type.Filled;
            buffFill.fillMethod = Image.FillMethod.Horizontal;
            buffLabel = Label(panel, "", 12, Paper, new Vector2(242, 36), new Vector2(92, 20), TextAlignmentOptions.MidlineLeft);
        }

        void RefreshHud()
        {
            if (!panel) return;
            float remaining = CooldownRemaining;
            cooldownShade.fillAmount = cooldown > 0 ? remaining / cooldown : 0;
            cooldownLabel.text = remaining > 0 ? (remaining >= 1 ? Mathf.CeilToInt(remaining).ToString() : remaining.ToString("0.0")) : "";
            bool ready = remaining <= 0;
            float pulse = ready ? .75f + .25f * Mathf.Sin(Time.unscaledTime * 5f) : .35f;
            slotBorder.color = ready ? Color.Lerp(Accent, Fluorescent, pulse - .5f) : new Color(.3f, .36f, .32f);
            float left = OneUpRemaining;
            buffFill.fillAmount = oneUpDuration > 0 ? left / oneUpDuration : 0;
            buffLabel.text = left > 0 ? $"1UP {left:0.0}s" : "";
            stateLabel.text = left > 0 ? "추가 투사체" : ready ? "준비 완료" : "재충전";
            stateLabel.color = left > 0 ? new Color(.55f, 1f, .45f) : ready ? Accent : Paper;
            portraitPulse = Mathf.MoveTowards(portraitPulse, 0, Time.unscaledDeltaTime * 2.5f);
            portraitFrame.localScale = Vector3.one * (1 + .06f * Mathf.Sin(portraitPulse * Mathf.PI));
        }

        RectTransform Node(string name, Transform parent, Vector2 position, Vector2 size, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static Image Img(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = white;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        TextMeshProUGUI Label(RectTransform parent, string text, float size, Color color, Vector2 position, Vector2 box, TextAlignmentOptions alignment)
        {
            var rect = Node("Text", parent, position, box, Vector2.zero);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font) label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.enableWordWrapping = true;
            return label;
        }

        static Texture2D WallpaperTexture()
        {
            // Faded backrooms wallpaper: vertical stripes with a faint diamond motif.
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float stripe = (x % 16) < 2 ? .9f : 1f;
                    float diamond = Mathf.Abs((x % 16) - 8) + Mathf.Abs((y % 16) - 8) == 6 ? .93f : 1f;
                    float shade = .88f + .12f * y / 31f;
                    texture.SetPixel(x, y, Wallpaper * (stripe * diamond * shade));
                }
            texture.Apply();
            return texture;
        }

        static Texture2D IconTexture()
        {
            // 16 x 16 pixel chomper facing right with a pellet, point filtered for an 8-bit look.
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var yellow = new Color(1f, .86f, .16f);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float dx = x - 6.5f, dy = y - 7.5f;
                    bool body = dx * dx + dy * dy <= 36;
                    bool mouth = dx > 0 && Mathf.Abs(dy) < dx * .75f;
                    bool eye = x == 6 && y == 11;
                    bool pellet = (x == 13 || x == 14) && (y == 7 || y == 8);
                    texture.SetPixel(x, y, eye ? Color.black : body && !mouth ? yellow : pellet ? Fluorescent : Color.clear);
                }
            texture.Apply();
            return texture;
        }
    }
}
