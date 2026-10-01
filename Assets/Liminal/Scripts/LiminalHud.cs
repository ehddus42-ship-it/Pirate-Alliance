using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// Run overlay in the "Hunter Association system window" style (<see cref="HunterUi"/>). Play-time
    /// sentences (toasts, objectives, control hints, skill callouts) are gone. What remains:
    /// - the hunter status window (health, dash charge, counter state);
    /// - the gate indicator (stage and room);
    /// - the remaining-enemy count;
    /// - the boss bar;
    /// - the just-dodge stamp;
    /// - the interaction key chip;
    /// - menu windows (augments, pause, results).
    /// </summary>
    public sealed class LiminalHud : MonoBehaviour
    {
        LiminalRunDirector run;
        TMP_FontAsset font;
        Font ownedSourceFont;
        GameObject canvasObject, modal, combatGroup, lobbyGroup, bossGroup;
        RectTransform modalContent, promptRoot, stamp;
        TextMeshProUGUI gateLabel, roomLabel, healthLabel, enemyLabel, bossName, bossValue, counterLabel, currencyLabel, lobbyTitle, promptLabel, promptKey;
        Image healthFill, healthGhost, bossFill, dashFill;
        CanvasGroup stampGroup;
        float stampAt = -10, ghost = 1;
        string configurationError;
        PlayerMotor motor;
        MeleeSlash melee;
        LiminalPlayerHealth subscribed;

        public TMP_FontAsset Font => font;
        public Transform Canvas => canvasObject ? canvasObject.transform : null;

        public void Initialize(LiminalRunDirector director, Font sourceFont)
        {
            run = director;
            font = HunterUi.CreateFont(sourceFont, out ownedSourceFont);
            canvasObject = new GameObject("HunterHUD", typeof(RectTransform), typeof(UnityEngine.Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<UnityEngine.Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = .5f;
            if (!FindFirstObjectByType<EventSystem>())
            {
                var events = new GameObject("LiminalEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(canvasObject.transform, false);
            }
            var root = canvasObject.transform;

            // Hunter status (top left).
            combatGroup = HunterUi.Rect("Combat", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1280, 720)).gameObject;
            Stretch(combatGroup.GetComponent<RectTransform>());
            var status = HunterUi.Window("HunterStatus", combatGroup.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -20), new Vector2(330, 96), font);
            HunterUi.Text("Rank", status, font, "HUNTER", 12, HunterUi.Gold, new Vector2(16, -10), new Vector2(120, 18), FontStyles.Bold);
            HunterUi.Text("Name", status, font, "아스트라이아", 19, HunterUi.Cream, new Vector2(16, -26), new Vector2(200, 28), FontStyles.Bold);
            counterLabel = HunterUi.Text("Counter", status, font, "", 13, HunterUi.Gate, new Vector2(200, -12), new Vector2(116, 20), FontStyles.Bold, TextAlignmentOptions.TopRight);
            var track = HunterUi.Fill("HealthTrack", status, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -60), new Vector2(232, 10), new Color(.05f, .07f, .14f, 1));
            healthGhost = HunterUi.Fill("HealthGhost", track.rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), Vector2.zero, new Vector2(232, 10), new Color(1, .9f, .75f, .55f));
            healthFill = HunterUi.Fill("HealthFill", track.rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), Vector2.zero, new Vector2(232, 10), HunterUi.Gold);
            for (int i = 1; i < 10; i++) HunterUi.Fill("Notch", track.rectTransform, new Vector2(i / 10f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1.5f, 10), new Color(.07f, .1f, .21f, .9f));
            healthLabel = HunterUi.Text("Health", status, font, "", 15, HunterUi.Cream, new Vector2(254, -55), new Vector2(66, 22), FontStyles.Bold, TextAlignmentOptions.TopRight);
            HunterUi.Text("DashLabel", status, font, "DASH", 11, HunterUi.Muted, new Vector2(16, -76), new Vector2(40, 16), FontStyles.Bold);
            var dashTrack = HunterUi.Fill("DashTrack", status, new Vector2(0, 1), new Vector2(0, 1), new Vector2(56, -80), new Vector2(110, 5), new Color(.05f, .07f, .14f, 1));
            dashFill = HunterUi.Fill("DashFill", dashTrack.rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), Vector2.zero, new Vector2(110, 5), HunterUi.Gate);

            // Gate indicator (top centre) and remaining enemies (top right).
            var gate = HunterUi.Window("Gate", combatGroup.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -20), new Vector2(300, 52), font, null, true);
            gateLabel = HunterUi.Text("GateLabel", gate, font, "", 13, HunterUi.Gold, new Vector2(0, -7), new Vector2(300, 18), FontStyles.Bold, TextAlignmentOptions.Top);
            roomLabel = HunterUi.Text("RoomLabel", gate, font, "", 17, HunterUi.Cream, new Vector2(0, -25), new Vector2(300, 24), FontStyles.Bold, TextAlignmentOptions.Top);
            var count = HunterUi.Window("Hostiles", combatGroup.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -20), new Vector2(118, 52), font, null, true);
            HunterUi.Text("HostileTag", count, font, "HOSTILE", 11, HunterUi.Danger, new Vector2(12, -8), new Vector2(94, 16), FontStyles.Bold);
            enemyLabel = HunterUi.Text("HostileCount", count, font, "", 22, HunterUi.Cream, new Vector2(12, -22), new Vector2(94, 28), FontStyles.Bold, TextAlignmentOptions.TopRight);

            // Boss bar (bottom centre).
            var boss = HunterUi.Window("Boss", combatGroup.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 26), new Vector2(720, 56), font, null, true);
            bossGroup = boss.gameObject;
            bossName = HunterUi.Text("BossName", boss, font, "", 16, HunterUi.Gold, new Vector2(16, -7), new Vector2(500, 22), FontStyles.Bold);
            bossValue = HunterUi.Text("BossValue", boss, font, "", 13, HunterUi.Muted, new Vector2(500, -9), new Vector2(204, 20), FontStyles.Normal, TextAlignmentOptions.TopRight);
            var bossTrack = HunterUi.Fill("BossTrack", boss, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -34), new Vector2(688, 10), new Color(.05f, .07f, .14f, 1));
            bossFill = HunterUi.Fill("BossFill", bossTrack.rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), Vector2.zero, new Vector2(688, 10), HunterUi.Danger);
            bossGroup.SetActive(false);

            // Lobby header: location and currency.
            lobbyGroup = HunterUi.Rect("Lobby", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1280, 720)).gameObject;
            Stretch(lobbyGroup.GetComponent<RectTransform>());
            var place = HunterUi.Window("Place", lobbyGroup.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -20), new Vector2(330, 74), font);
            HunterUi.Text("Org", place, font, "KOREA HUNTER ASSOCIATION", 11, HunterUi.Gold, new Vector2(16, -10), new Vector2(300, 16), FontStyles.Bold);
            lobbyTitle = HunterUi.Text("PlaceName", place, font, "게이트 관리 구역", 19, HunterUi.Cream, new Vector2(16, -28), new Vector2(300, 28), FontStyles.Bold);
            var wallet = HunterUi.Window("Wallet", lobbyGroup.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -20), new Vector2(170, 52), font, null, true);
            HunterUi.Text("CurrencyTag", wallet, font, "마석", 12, HunterUi.Gate, new Vector2(14, -8), new Vector2(60, 16), FontStyles.Bold);
            currencyLabel = HunterUi.Text("Currency", wallet, font, "", 22, HunterUi.Cream, new Vector2(14, -22), new Vector2(142, 28), FontStyles.Bold, TextAlignmentOptions.TopRight);
            lobbyGroup.SetActive(false);

            // Interaction key chip (bottom centre, above the boss bar).
            promptRoot = HunterUi.KeyChip(root, font, "E", "", new Vector2(.5f, 0), new Vector2(0, 110));
            promptKey = promptRoot.Find("Key/KeyLabel").GetComponent<TextMeshProUGUI>();
            promptLabel = promptRoot.Find("Plate/Label").GetComponent<TextMeshProUGUI>();
            promptRoot.gameObject.SetActive(false);

            // Just-dodge stamp.
            stamp = HunterUi.Rect("JustDodge", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 120), new Vector2(520, 120));
            stampGroup = stamp.gameObject.AddComponent<CanvasGroup>();
            // A translucent system-window band keeps the stamp legible over bright floors.
            HunterUi.Fill("Band", stamp, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -2), new Vector2(560, 96), new Color(.07f, .1f, .21f, .72f));
            HunterUi.Fill("BandTop", stamp, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 46), new Vector2(560, 1.2f), HunterUi.GoldDim);
            HunterUi.Fill("BandBottom", stamp, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -50), new Vector2(560, 1.2f), HunterUi.GoldDim);
            HunterUi.Fill("Slash", stamp, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(460, 3), HunterUi.Gate).rectTransform.localRotation = Quaternion.Euler(0, 0, 8);
            HunterUi.Fill("Slash2", stamp, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -10), new Vector2(380, 1.5f), HunterUi.Gold).rectTransform.localRotation = Quaternion.Euler(0, 0, 8);
            HunterUi.Text("Just", stamp, font, "<i>JUST DODGE</i>", 46, HunterUi.Cream, new Vector2(0, -18), new Vector2(520, 60), FontStyles.Bold, TextAlignmentOptions.Center);
            HunterUi.Text("Sub", stamp, font, "COUNTER READY", 14, HunterUi.Gold, new Vector2(0, -78), new Vector2(520, 22), FontStyles.Bold, TextAlignmentOptions.Center);
            stampGroup.alpha = 0;

            modal = new GameObject("SystemWindow", typeof(RectTransform), typeof(Image));
            modal.transform.SetParent(root, false);
            modal.GetComponent<Image>().color = new Color(.03f, .04f, .09f, .82f);
            Stretch(modal.GetComponent<RectTransform>());
            modalContent = HunterUi.Window("Content", modal.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(960, 480), font, null, true);
            modal.SetActive(false);
        }

        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        /// <summary>Play-time messages are no longer shown; kept so existing callers still compile.</summary>
        public void Notify(string message, float duration) { }

        public void ShowPrompt(string key, string label)
        {
            if (!promptRoot) return;
            bool show = !string.IsNullOrEmpty(label);
            promptRoot.gameObject.SetActive(show);
            if (!show) return;
            promptKey.text = key;
            promptLabel.text = label;
        }

        public void SetLocation(string name) { if (lobbyTitle) lobbyTitle.text = name; }

        void Update()
        {
            if (!run) return;
            Bind();
            bool lobby = run.Phase == LiminalRunPhase.Lobby;
            if (combatGroup) combatGroup.SetActive(!lobby && run.CurrentStage);
            if (lobbyGroup) lobbyGroup.SetActive(lobby);
            if (currencyLabel) currencyLabel.text = HunterProgress.Currency.ToString("N0");
            UpdateStamp();
            if (lobby || !run.CurrentStage) return;
            if (run.ExitAvailable) ShowPrompt("E", "다음 구역");
            else if (promptLabel && promptLabel.text == "다음 구역") ShowPrompt("E", null);
            gateLabel.text = $"GATE  {run.StageIndex + 1:00}";
            roomLabel.text = $"{run.CurrentStage.title}   {Mathf.Max(0, run.ActiveRoomIndex + 1):00} / {run.Rooms.Count:00}";
            enemyLabel.text = run.LivingEnemyCount.ToString();
            if (run.PlayerHealth)
            {
                float h = run.PlayerHealth.maximumHealth > 0 ? (float)run.PlayerHealth.Health / run.PlayerHealth.maximumHealth : 0;
                healthFill.rectTransform.sizeDelta = new Vector2(232 * h, 10);
                healthFill.color = h < .3f ? Color.Lerp(HunterUi.Danger, HunterUi.Cream, Mathf.PingPong(Time.unscaledTime * 3, 1) * .3f) : HunterUi.Gold;
                // The lost part lingers as a pale ghost, then drains.
                ghost = Mathf.Max(h, Mathf.MoveTowards(ghost, h, Time.unscaledDeltaTime * .5f));
                healthGhost.rectTransform.sizeDelta = new Vector2(232 * ghost, 10);
                healthLabel.text = $"{run.PlayerHealth.Health}";
            }
            if (motor) dashFill.rectTransform.sizeDelta = new Vector2(110 * (1 - Mathf.Clamp01(motor.CooldownRemaining / Mathf.Max(.01f, motor.dashCooldown))), 5);
            counterLabel.text = melee && melee.CounterActive ? "COUNTER" : "";
            bossGroup.SetActive(false);
            if (run.CurrentStage.isBossStage && run.LivingEnemyCount > 0)
                foreach (var signal in FindObjectsByType<TrafficLightBoss>(FindObjectsSortMode.None))
                    if (signal.enabled && signal.Health && signal.Health.IsAlive && signal.State != TrafficLightBossState.Dormant)
                    {
                        bossGroup.SetActive(true);
                        bossName.text = signal.displayName + (signal.Enraged ? "   <color=#ff6b5f>ENRAGED</color>" : "");
                        bossValue.text = $"{signal.Health.Health} / {signal.Health.maxHealth}";
                        bossFill.rectTransform.sizeDelta = new Vector2(688f * signal.Health.Health / Mathf.Max(1, signal.Health.maxHealth), 10);
                        break;
                    }
            if (run.Phase == LiminalRunPhase.AugmentChoice && Keyboard.current != null)
            {
                if (Keyboard.current.digit1Key.wasPressedThisFrame) run.SelectAugment(0);
                else if (Keyboard.current.digit2Key.wasPressedThisFrame) run.SelectAugment(1);
                else if (Keyboard.current.digit3Key.wasPressedThisFrame) run.SelectAugment(2);
            }
        }

        void Bind()
        {
            if (run.PlayerHealth && subscribed != run.PlayerHealth)
            {
                if (subscribed) subscribed.JustDodged -= OnJustDodged;
                subscribed = run.PlayerHealth;
                subscribed.JustDodged += OnJustDodged;
                motor = subscribed.GetComponent<PlayerMotor>();
                melee = subscribed.GetComponent<MeleeSlash>();
            }
        }

        void OnJustDodged() => stampAt = Time.unscaledTime;

        void UpdateStamp()
        {
            float t = Time.unscaledTime - stampAt;
            if (t > .9f) { stampGroup.alpha = 0; return; }
            // Slams in big, settles, holds, fades.
            float s = t < .08f ? Mathf.Lerp(1.6f, 1f, t / .08f) : 1 + .03f * Mathf.Sin(t * 30) * Mathf.Exp(-t * 6);
            stamp.localScale = Vector3.one * s;
            stampGroup.alpha = t < .6f ? 1 : 1 - (t - .6f) / .3f;
        }

        public void ShowConfigurationError(string error) { configurationError = error; RefreshPhase(); }

        public void RefreshPhase()
        {
            if (!modal) return;
            bool show = run.Phase != LiminalRunPhase.Exploring && run.Phase != LiminalRunPhase.Lobby;
            modal.SetActive(show);
            if (!show) return;
            ShowPrompt(null, null);
            foreach (Transform child in modalContent)
                if (child.name != "FrameTop" && child.name != "FrameBottom" && child.name != "FrameLeft" && child.name != "FrameRight" && child.name != "Tick")
                { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            if (run.Phase == LiminalRunPhase.AugmentChoice)
            {
                Heading("게이트 구역 정리 완료", "증강 선택");
                Choice(0, "잔향", "공격 속도 +19%", 0);
                Choice(1, "도깨비불", "적중 시 불꽃 +2", 1);
                Choice(2, "굳은 매듭", "최대 체력 +25\n체력 25 회복", 2);
                HunterUi.Text("Keys", modalContent, font, "1 · 2 · 3", 14, HunterUi.Muted, new Vector2(0, -440), new Vector2(960, 22), FontStyles.Bold, TextAlignmentOptions.Center);
            }
            else if (run.Phase == LiminalRunPhase.NextStageChoice)
            {
                var next = run.stages[run.StageIndex + 1];
                Heading("다음 게이트", next.isBossStage ? "BOSS" : $"GATE {run.StageIndex + 2:00}");
                HunterUi.Text("Route", modalContent, font, $"{run.CurrentStage.title}   →   {next.title}", 26, HunterUi.Cream, new Vector2(0, -190), new Vector2(960, 40), FontStyles.Bold, TextAlignmentOptions.Center);
                HunterUi.Button("Continue", modalContent, font, next.isBossStage ? "보스 게이트 진입" : "다음 게이트 진입", new Vector2(300, -330), new Vector2(360, 60), run.ContinueToNextStage);
            }
            else if (run.Phase == LiminalRunPhase.Paused)
            {
                Heading("일시 정지", "PAUSE");
                HunterUi.Text("Augments", modalContent, font, run.AugmentHistory, 19, HunterUi.Cream, new Vector2(70, -150), new Vector2(820, 140), FontStyles.Normal, TextAlignmentOptions.Top);
                HunterUi.Button("Resume", modalContent, font, "계속", new Vector2(300, -310), new Vector2(360, 56), run.Resume);
                HunterUi.Button("Return", modalContent, font, "로비로 귀환", new Vector2(300, -380), new Vector2(360, 48), run.ReturnToLobby);
            }
            else if (run.Phase == LiminalRunPhase.Victory || run.Phase == LiminalRunPhase.Defeat)
            {
                bool won = run.Phase == LiminalRunPhase.Victory;
                Heading(won ? "게이트 클리어" : "헌터 후송", won ? "CLEAR" : "RETREAT");
                HunterUi.Text("Reward", modalContent, font, $"획득 마석   <color=#a99bff>+{run.PendingReward:N0}</color>", 26, HunterUi.Cream, new Vector2(0, -150), new Vector2(960, 40), FontStyles.Bold, TextAlignmentOptions.Center);
                HunterUi.Text("Result", modalContent, font, run.AugmentHistory, 17, HunterUi.Muted, new Vector2(70, -205), new Vector2(820, 100), FontStyles.Normal, TextAlignmentOptions.Top);
                HunterUi.Button("Return", modalContent, font, "로비로 귀환", new Vector2(150, -350), new Vector2(300, 64), run.ReturnToLobby);
                HunterUi.Button("Retry", modalContent, font, "바로 재도전", new Vector2(510, -350), new Vector2(300, 64), () => { run.ReturnToLobby(); run.EnterDungeon(); });
            }
            else
            {
                Heading("설정 오류", "CONFIGURATION");
                HunterUi.Text("Error", modalContent, font, configurationError ?? "", 18, HunterUi.Cream, new Vector2(50, -160), new Vector2(860, 220));
            }
        }

        void Heading(string title, string tag)
        {
            HunterUi.Text("Tag", modalContent, font, tag, 13, HunterUi.Gold, new Vector2(40, -30), new Vector2(880, 18), FontStyles.Bold);
            HunterUi.Title(modalContent, font, title, new Vector2(40, -50), 880, 30);
        }

        void Choice(int column, string title, string description, int option)
        {
            var card = HunterUi.Button("Augment_" + option, modalContent, font, "", new Vector2(40 + column * 300, -150), new Vector2(280, 270), () => run.SelectAugment(option));
            HunterUi.Text("Index", card, font, $"0{option + 1}", 14, HunterUi.Gold, new Vector2(20, -18), new Vector2(60, 20), FontStyles.Bold);
            HunterUi.Title(card, font, title, new Vector2(20, -42), 240, 24);
            HunterUi.Text("Description", card, font, description, 18, HunterUi.Cream, new Vector2(20, -110), new Vector2(240, 140));
        }

        void OnDestroy()
        {
            if (subscribed) subscribed.JustDodged -= OnJustDodged;
            if (ownedSourceFont)
            {
                if (font)
                {
                    foreach (var atlas in font.atlasTextures) if (atlas) Destroy(atlas);
                    if (font.material) Destroy(font.material);
                    Destroy(font);
                }
                Destroy(ownedSourceFont);
            }
        }
    }
}
