using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace AcRoguelike.Liminal
{
    /// <summary>Runtime overlay. The only route choice is the next stage; upgrades stay in the augment choice.</summary>
    public sealed class LiminalHud : MonoBehaviour
    {
        LiminalRunDirector run;
        TMP_FontAsset font;
        Font ownedSourceFont;
        bool ownsFont;
        GameObject canvasObject, modal;
        RectTransform modalContent;
        TextMeshProUGUI stageLabel, roomLabel, healthLabel, objectiveLabel, toastLabel, bossLabel;
        UnityEngine.UI.Image healthFill;
        float toastUntil;
        string configurationError;
        static readonly Color Paper = new Color(.89f, .91f, .82f);
        static readonly Color Accent = new Color(.72f, .86f, .55f);

        public void Initialize(LiminalRunDirector director, Font sourceFont)
        {
            run = director;
            if (!sourceFont)
            {
                ownedSourceFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 30);
                sourceFont = ownedSourceFont;
            }
            if (sourceFont)
            {
                font = TMP_FontAsset.CreateFontAsset(sourceFont, 40, 5,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                ownsFont = true;
            }
            else font = TMP_Settings.defaultFontAsset;
            canvasObject = new GameObject("LiminalHUD", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = .5f;
            if (!FindFirstObjectByType<EventSystem>())
            {
                var events = new GameObject("LiminalEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(canvasObject.transform, false);
            }

            var header = Box("Status", canvasObject.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -22), new Vector2(390, 128), new Color(.045f, .075f, .075f, .92f));
            stageLabel = Text("Stage", header, "", 23, Accent, new Vector2(16, -12), new Vector2(358, 40));
            roomLabel = Text("Room", header, "", 17, Paper, new Vector2(16, -48), new Vector2(358, 28));
            var track = Box("HealthTrack", header, Vector2.zero, Vector2.zero, new Vector2(16, 19), new Vector2(252, 9), new Color(.22f, .26f, .23f));
            healthFill = Box("HealthFill", track, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(252, 9), Accent).GetComponent<UnityEngine.UI.Image>();
            healthLabel = Text("Health", header, "", 16, Paper, new Vector2(279, -86), new Vector2(97, 28));
            objectiveLabel = Text("Objective", canvasObject.transform, "", 18, Paper, new Vector2(-28, -30), new Vector2(500, 85));
            SetCorner(objectiveLabel.rectTransform, new Vector2(1, 1), new Vector2(1, 1));
            objectiveLabel.alignment = TextAlignmentOptions.TopRight;
            var controls = Text("Controls", canvasObject.transform,
                "WASD 이동 · CTRL 걷기 · 좌클릭/J 카타나 4연격 · SPACE/SHIFT 대시 · Q 지원 스킬 · E 출구 · ESC 일시정지", 16,
                Paper, new Vector2(0, 20), new Vector2(1200, 32));
            SetCorner(controls.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, 0));
            controls.alignment = TextAlignmentOptions.Center;
            toastLabel = Text("RoomMessage", canvasObject.transform, "", 24, Paper, new Vector2(0, -164), new Vector2(960, 92));
            SetCorner(toastLabel.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1));
            toastLabel.alignment = TextAlignmentOptions.Center;
            bossLabel = Text("BossHealth", canvasObject.transform, "", 21, new Color(1, .72f, .5f), new Vector2(0, 66), new Vector2(950, 40));
            SetCorner(bossLabel.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, 0));
            bossLabel.alignment = TextAlignmentOptions.Center;

            modal = new GameObject("StageTransition", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            modal.transform.SetParent(canvasObject.transform, false);
            var shade = modal.GetComponent<UnityEngine.UI.Image>();
            shade.color = new Color(.02f, .035f, .04f, .9f);
            var mr = modal.GetComponent<RectTransform>();
            mr.anchorMin = Vector2.zero; mr.anchorMax = Vector2.one; mr.offsetMin = mr.offsetMax = Vector2.zero;
            modalContent = Box("Content", modal.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero,
                new Vector2(980, 490), new Color(.065f, .095f, .09f, 1));
            modal.SetActive(false);
        }

        void Update()
        {
            if (!run || !run.CurrentStage) return;
            stageLabel.text = $"{run.StageIndex + 1:00}  {run.CurrentStage.title}";
            roomLabel.text = $"{run.CurrentRoomName}   {Mathf.Max(0, run.ActiveRoomIndex + 1):00}/{run.Rooms.Count:00}";
            if (run.PlayerHealth)
            {
                healthLabel.text = $"{run.PlayerHealth.Health} / {run.PlayerHealth.maximumHealth}";
                healthFill.rectTransform.sizeDelta = new Vector2(252f * run.PlayerHealth.Health / run.PlayerHealth.maximumHealth, 9);
            }
            objectiveLabel.text = run.ExitAvailable ? "[E] 다음 구역으로 이동"
                : run.LivingEnemyCount > 0 ? $"잔상 {run.LivingEnemyCount}개 · 정리하면 문이 열려\n바닥 예고선 밖으로 회피해"
                : "열린 문을 따라 다음 공간으로\n" + $"SEED {run.seed}";
            if (Time.unscaledTime > toastUntil) toastLabel.text = "";
            bossLabel.text = "";
            if (run.CurrentStage.isBossStage && run.LivingEnemyCount > 0)
                foreach (var enemy in FindObjectsByType<LiminalEnemy>(FindObjectsSortMode.None))
                    if (enemy.isBoss && enemy.Health && enemy.Health.IsAlive)
                    {
                        bossLabel.text = $"관리자    {enemy.Health.Health} / {enemy.Health.maxHealth}" +
                            (enemy.Health.Health < enemy.Health.maxHealth / 2 ? "    ·    두 번째 호출" : "");
                        break;
                    }
            if (run.CurrentStage.isBossStage && run.LivingEnemyCount > 0)
                foreach (var signal in FindObjectsByType<TrafficLightBoss>(FindObjectsSortMode.None))
                    if (signal.enabled && signal.Health && signal.Health.IsAlive && signal.State != TrafficLightBossState.Dormant)
                    {
                        bossLabel.text = $"{signal.displayName}    {signal.Health.Health} / {signal.Health.maxHealth}" + (signal.Enraged ? "    ·    점멸" : "");
                        break;
                    }
            if (run.Phase == LiminalRunPhase.AugmentChoice && Keyboard.current != null)
            {
                if (Keyboard.current.digit1Key.wasPressedThisFrame) run.SelectAugment(0);
                else if (Keyboard.current.digit2Key.wasPressedThisFrame) run.SelectAugment(1);
                else if (Keyboard.current.digit3Key.wasPressedThisFrame) run.SelectAugment(2);
            }
        }

        public void Notify(string message, float duration)
        { toastLabel.text = message; toastUntil = Time.unscaledTime + duration; }

        public void ShowConfigurationError(string error) { configurationError = error; RefreshPhase(); }

        public void RefreshPhase()
        {
            if (!modal) return;
            bool show = run.Phase != LiminalRunPhase.Exploring;
            modal.SetActive(show);
            if (!show) return;
            foreach (Transform child in modalContent) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            if (run.Phase == LiminalRunPhase.AugmentChoice)
            {
                Heading("스테이지 완료", "잠깐 숨을 고르고, 증강 하나를 골라.");
                Choice(0, "01  잔향", "부적 시전 간격 -16%\n\n같은 동작이 더 빠르게\n공간에 되돌아와.", 0);
                Choice(1, "02  도깨비불", "부적 적중 시 불꽃 +2\n\n남은 불씨가\n오래 자리를 지켜.", 1);
                Choice(2, "03  굳은 매듭", "최대 체력 +25 · 체력 25 회복\n\n다음 문을 지나갈\n여유를 챙겨.", 2);
                Text("Hint", modalContent, "카드를 클릭하거나 숫자 1 · 2 · 3", 16, Paper, new Vector2(32, -437), new Vector2(916, 30)).alignment = TextAlignmentOptions.Center;
            }
            else if (run.Phase == LiminalRunPhase.NextStageChoice)
            {
                var next = run.stages[run.StageIndex + 1];
                Heading("다음 스테이지 선택", "이어지는 길은 하나야. 준비됐으면 다음 문을 열어.");
                Text("Route", modalContent, $"{run.CurrentStage.title}  →  {next.title}\n\n{next.subtitle}", 25, Paper, new Vector2(70, -166), new Vector2(840, 160)).alignment = TextAlignmentOptions.Center;
                Button("Continue", modalContent, next.isBossStage ? "보스 방으로 이동" : "다음 스테이지로 이동", new Vector2(280, -346), new Vector2(420, 66), run.ContinueToNextStage);
            }
            else if (run.Phase == LiminalRunPhase.Paused)
            {
                Heading("잠시 멈춤", "문은 기다리고 있어.");
                Text("Augments", modalContent, run.AugmentHistory, 21, Paper, new Vector2(70, -150), new Vector2(840, 165)).alignment = TextAlignmentOptions.Center;
                Button("Resume", modalContent, "계속 탐방하기", new Vector2(280, -328), new Vector2(420, 62), run.Resume);
                Button("Restart", modalContent, "같은 배치로 다시 시작", new Vector2(280, -406), new Vector2(420, 52), () => run.StartNewRun(run.seed));
            }
            else if (run.Phase == LiminalRunPhase.Victory || run.Phase == LiminalRunPhase.Defeat)
            {
                bool won = run.Phase == LiminalRunPhase.Victory;
                Heading(won ? "마지막 문을 통과했어" : "이번 탐방은 여기까지", won ? "그런데, 이곳의 불은 여전히 켜져 있어." : "같은 배치로 다시 살펴볼 수 있어.");
                Text("Result", modalContent, $"SEED {run.seed}\n\n{run.AugmentHistory}", 21, Paper, new Vector2(70, -150), new Vector2(840, 165)).alignment = TextAlignmentOptions.Center;
                Button("Restart", modalContent, "같은 배치로 다시 시작", new Vector2(180, -357), new Vector2(300, 70), () => run.StartNewRun(run.seed));
                Button("NewRun", modalContent, "새로운 배치 탐방", new Vector2(500, -357), new Vector2(300, 70), () => run.StartNewRun(unchecked(run.seed + 104729)));
            }
            else
            {
                Heading("맵 설정을 확인해 줘", "LiminalRunDirector의 플레이어와 스테이지 연결이 필요해.");
                Text("Error", modalContent, configurationError ?? "설정을 불러오지 못했어.", 20, Paper, new Vector2(50, -175), new Vector2(880, 225));
            }
        }

        void Heading(string title, string subtitle)
        {
            Text("Title", modalContent, title, 35, Accent, new Vector2(32, -36), new Vector2(916, 60)).alignment = TextAlignmentOptions.Center;
            Text("Subtitle", modalContent, subtitle, 20, Paper, new Vector2(32, -101), new Vector2(916, 46)).alignment = TextAlignmentOptions.Center;
        }

        void Choice(int column, string title, string description, int option)
        {
            var card = Button("Augment_" + option, modalContent, "", new Vector2(35 + column * 307, -175), new Vector2(296, 238), () => run.SelectAugment(option));
            Text("Name", card, title, 25, Accent, new Vector2(20, -21), new Vector2(256, 42));
            Text("Description", card, description, 19, Paper, new Vector2(20, -80), new Vector2(256, 144));
        }

        RectTransform Button(string name, Transform parent, string label, Vector2 pos, Vector2 size, Action action)
        {
            var rect = Box(name, parent, new Vector2(0, 1), new Vector2(0, 1), pos, size, new Color(.14f, .2f, .17f));
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            rect.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            button.targetGraphic = rect.GetComponent<UnityEngine.UI.Image>();
            var colors = button.colors;
            colors.highlightedColor = new Color(.72f, .89f, .66f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(.48f, .66f, .44f);
            button.colors = colors;
            button.onClick.AddListener(() => action());
            if (!string.IsNullOrEmpty(label))
                Text("Label", rect, label, 21, Paper, Vector2.zero, size).alignment = TextAlignmentOptions.Center;
            return rect;
        }

        static RectTransform Box(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            SetCorner(rect, anchor, pivot); rect.anchoredPosition = pos; rect.sizeDelta = size;
            var image = go.GetComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false;
            return rect;
        }

        TextMeshProUGUI Text(string name, Transform parent, string value, int size, Color color, Vector2 pos, Vector2 dimensions)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = size; text.text = value; text.color = color;
            text.raycastTarget = false; text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.margin = new Vector4(1, 0, 1, 0);
            SetCorner(text.rectTransform, new Vector2(0, 1), new Vector2(0, 1));
            text.rectTransform.anchoredPosition = pos; text.rectTransform.sizeDelta = dimensions;
            return text;
        }

        static void SetCorner(RectTransform rect, Vector2 anchor, Vector2 pivot)
        { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; }

        void OnDestroy()
        {
            if (ownsFont && font)
            {
                foreach (var atlas in font.atlasTextures) if (atlas) Destroy(atlas);
                if (font.material) Destroy(font.material);
                Destroy(font);
            }
            if (ownedSourceFont) Destroy(ownedSourceFont);
        }
    }
}
