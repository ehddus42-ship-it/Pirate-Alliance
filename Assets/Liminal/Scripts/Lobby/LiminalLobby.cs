using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// Active lobby: the Korea Hunter Association's gate control zone, a plaza in Seoul built at runtime.
    /// It has barricades, association banners, a blue booth, the skyline with N Seoul Tower, and a huge violet
    /// gate. Everyone in it is a Meshy-made association official (<see cref="LobbyNpc"/>): the agent at the booth,
    /// staff chatting and walking their rounds, and gate guards on watch. The hunter walks it in everyday clothes:
    /// - talk to the association agent to buy permanent upgrades with magic stones;
    /// - walk into the gate to start a run.
    /// There are no menu buttons until the player interacts with something.
    /// </summary>
    public sealed class LiminalLobby : MonoBehaviour
    {
        public const string PlayerCasualPath = "LiminalLobby/player_casual/player_casual";
        public const string PlayerCasualTexture = "LiminalLobby/player_casual/player_casual_albedo";
        // Association officials (Tools/LiminalLobby/meshy_lobby.py, OFFICIALS).
        public const string AgentCharacter = "association_agent";
        public const string ClerkCharacter = "association_clerk";
        public const string OfficerCharacter = "association_officer";
        public const string DirectorCharacter = "association_director";
        public const string GuardCharacter = "association_guard";

        public Vector3 SpawnPoint => transform.TransformPoint(new Vector3(0, .05f, -9));
        public bool WindowOpen => window;
        public Interactable Nearest { get; private set; }
        public LobbyNpc Agent => agent;
        public IReadOnlyList<LobbyNpc> Officials => officials;

        public sealed class Interactable
        {
            public string label;
            public Transform anchor;
            public float radius;
            public Action use;
        }

        LiminalRunDirector run;
        LiminalHud hud;
        readonly List<Interactable> interactables = new List<Interactable>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        LobbyNpc agent;
        readonly List<LobbyNpc> officials = new List<LobbyNpc>();
        bool greeted;
        Transform gateCore;
        Transform[] gateRings;
        Light gateLight;
        float sparkClock;
        GameObject window;
        GateFade fade;
        bool entering;
        PlayerMotor motor;
        Animator combatAnimator;
        GameObject combatModel, casualModel;
        Color previousAmbient;

        // ---- construction ----------------------------------------------------------------------------------
        public static LiminalLobby Build(LiminalRunDirector director, Vector3 origin, LiminalHud hud)
        {
            var go = new GameObject("HunterLobby");
            go.transform.SetParent(director.transform, false);
            go.transform.position = origin;
            var lobby = go.AddComponent<LiminalLobby>();
            lobby.run = director;
            lobby.hud = hud;
            lobby.Construct();
            return lobby;
        }

        void Construct()
        {
            var stone = Mat(LobbyTextures.Paving(), new Color(.8f, .79f, .76f), .25f, new Vector2(11, 9));
            var asphalt = Mat(null, new Color(.2f, .21f, .24f), .15f);
            var metal = Mat(null, new Color(.3f, .32f, .36f), .55f, default, .6f);
            var stripe = Mat(LobbyTextures.Stripes(), Color.white, .3f, new Vector2(3, 1));
            var navy = Mat(LobbyTextures.Banner(), Color.white, .2f);
            if (navy.HasProperty("_Cull")) navy.SetFloat("_Cull", 0);
            var canopy = Mat(null, new Color(.16f, .36f, .78f), .3f);
            var white = Mat(null, new Color(.93f, .94f, .96f), .35f);
            var dark = Mat(null, new Color(.07f, .08f, .1f), .4f);
            var windows = Mat(LobbyTextures.Windows(), Color.white, .5f, new Vector2(2, 4));
            if (windows.HasProperty("_EmissionColor"))
            {
                windows.EnableKeyword("_EMISSION");
                windows.SetTexture("_EmissionMap", windows.mainTexture);
                windows.SetColor("_EmissionColor", new Color(.55f, .55f, .5f));
            }

            // Ground: paved plaza with an asphalt road beyond.
            Block("Plaza", new Vector3(0, -.05f, 0), new Vector3(44, .1f, 36), stone, true);
            Block("Road", new Vector3(0, -.06f, 0), new Vector3(120, .1f, 110), asphalt, true);
            // Invisible walls keep the player on the plaza.
            foreach (var (p, s) in new[] { (new Vector3(0, 1.5f, 18.2f), new Vector3(44, 3, .4f)), (new Vector3(0, 1.5f, -18.2f), new Vector3(44, 3, .4f)),
                                           (new Vector3(22.2f, 1.5f, 0), new Vector3(.4f, 3, 36)), (new Vector3(-22.2f, 1.5f, 0), new Vector3(.4f, 3, 36)) })
                Block("Boundary", p, s, null, true);

            BuildGate(metal, dark);

            // The association emblem inlaid in the middle of the plaza, and street lamps around it.
            var emblem = Quad("FloorEmblem", new Vector3(0, .012f, -2.5f), new Vector3(7, 7, 1), Decal(LobbyTextures.Emblem()));
            emblem.localRotation = Quaternion.Euler(90, 0, 0);
            var lampMat = Mat(null, new Color(1f, .93f, .78f), .8f, default, 0, new Color(2.4f, 2.1f, 1.6f));
            foreach (var (x, z) in new[] { (-7.5f, -6f), (7.5f, -6f), (-7.5f, 4.5f), (7.5f, 4.5f) })
            {
                Cylinder("LampPost", new Vector3(x, 2.1f, z), new Vector3(.14f, 2.1f, .14f), metal);
                Block("LampHead", new Vector3(x, 4.25f, z), new Vector3(.5f, .18f, .5f), lampMat, false);
                Block("LampBase", new Vector3(x, .15f, z), new Vector3(.4f, .3f, .4f), dark, true);
            }

            // Barricades in an arc in front of the gate, with the way in left open in the middle.
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 4; i++)
                {
                    float x = side * (2.6f + i * 1.9f), z = 8.6f + i * i * .18f;
                    var b = Block("Barricade", new Vector3(x, .45f, z), new Vector3(1.8f, .9f, .32f), stripe, true);
                    b.localRotation = Quaternion.Euler(0, side * i * 6f, 0);
                    Block("BarricadeFoot", new Vector3(x, .06f, z), new Vector3(1.9f, .12f, .6f), dark, false);
                }
            // Association guards on watch beside the opening, facing the plaza.
            int guardIndex = 0;
            foreach (float x in new[] { -3.8f, 3.8f, -7.2f, 7.2f })
            {
                var guard = Official(GuardCharacter, new[] { "idle", "look" }, new Vector3(x, 0, 7.2f + Mathf.Abs(x) * .05f), 180 + (x < 0 ? 8 : -8), 1.82f, "Guard");
                LobbyRoutine.Station(guard, "look", 11 + guardIndex++);
            }

            // Association booth with the agent (left), a rest corner (right).
            BuildBooth(canopy, white, metal);
            Prop(Library?.waitingBench, new Vector3(10.5f, 0, -1.5f), -90);
            Prop(Library?.waitingBench, new Vector3(10.5f, 0, 2.4f), -90);
            Prop(Library?.vendingMachine, new Vector3(13.2f, 0, -5.5f), -90);
            Prop(Library?.waterDispenser, new Vector3(13.2f, 0, -3.6f), -90);
            Prop(Library?.planter, new Vector3(-13.5f, 0, -8), 0);
            Prop(Library?.planter, new Vector3(13.5f, 0, 8), 0);
            Prop(Library?.planter, new Vector3(-13.5f, 0, 8.5f), 0);
            Prop(Library?.trashBin, new Vector3(8.8f, 0, -6.5f), 0);
            Prop(Library?.cautionSign, new Vector3(-1.9f, 0, 7.6f), 20);
            Prop(Library?.cautionSign, new Vector3(1.9f, 0, 7.6f), -20);
            Prop(Library?.directoryKiosk, new Vector3(3.5f, 0, -12), 180);
            Prop(Library?.foldingBarrier, new Vector3(-16, 0, 12), 30);
            Prop(Library?.foldingBarrier, new Vector3(16, 0, 12), -30);

            // Association banners down both sides.
            foreach (var (x, z) in new[] { (-17f, -10f), (-17f, 0f), (-17f, 10f), (17f, -10f), (17f, 0f), (17f, 10f) })
            {
                Block("BannerPole", new Vector3(x, 2.4f, z), new Vector3(.12f, 4.8f, .12f), metal, true);
                var banner = Quad("Banner", new Vector3(x - Mathf.Sign(x) * .05f, 3.1f, z + .65f), new Vector3(1.1f, 2.4f, 1), navy);
                banner.localRotation = Quaternion.Euler(0, x < 0 ? 90 : -90, 0);
            }

            // Skyline beyond the plaza, with N Seoul Tower on its hill.
            var random = new System.Random(2026);
            for (int i = 0; i < 26; i++)
            {
                float angle = Mathf.Lerp(-80, 80, i / 25f) * Mathf.Deg2Rad;
                float distance = 46 + (float)random.NextDouble() * 22;
                float h = 16 + (float)random.NextDouble() * (i % 3 == 0 ? 46 : 26);
                float w = 7 + (float)random.NextDouble() * 7;
                var pos = new Vector3(Mathf.Sin(angle) * distance, h * .5f - .1f, Mathf.Cos(angle) * distance + 6);
                var building = Block("Building", pos, new Vector3(w, h, w * .8f), windows, false);
                building.localRotation = Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0);
            }
            Block("Namsan", new Vector3(22, -6, 74), new Vector3(46, 16, 30), Mat(null, new Color(.22f, .34f, .26f), .1f), false).localRotation = Quaternion.Euler(0, 0, 8);
            var towerMat = Mat(null, new Color(.86f, .87f, .9f), .5f);
            Cylinder("Tower", new Vector3(20, 14, 74), new Vector3(1.6f, 14, 1.6f), towerMat);
            Cylinder("TowerDeck", new Vector3(20, 27, 74), new Vector3(6, 1.6f, 6), towerMat);
            Cylinder("TowerCap", new Vector3(20, 28.6f, 74), new Vector3(4, 1, 4), Mat(null, new Color(.7f, .2f, .25f), .5f));
            Cylinder("Spire", new Vector3(20, 33, 74), new Vector3(.35f, 5, .35f), towerMat);

            // The agent at the booth, clipboard in hand as in the concept art.
            // She stands in front of the booth, not under its roof, so the overhead camera always sees her.
            agent = Official(AgentCharacter, new[] { "idle", "talk", "bow" }, new Vector3(-8.9f, 0, 1.8f), 90, 1.65f, "AssociationAgent");
            var boardMat = Mat(null, new Color(.13f, .15f, .2f), .35f);
            var clipMat = Mat(null, new Color(.72f, .66f, .5f), .6f, default, .8f);
            HeldBoard.Attach(agent, true, new Vector3(.23f, .31f, .012f), boardMat, clipMat, Decal(LobbyTextures.Emblem()), owned);
            interactables.Add(new Interactable { label = "대화", anchor = agent.transform, radius = 2.6f, use = OpenUpgrades });

            // Ambient staff: a senior official briefing a junior by the rest corner, and a clerk walking his rounds
            // between the booth and the entrance (he stops for the player and takes calls at some stops).
            var officer = Official(OfficerCharacter, new[] { "idle", "chat", "walk" }, new Vector3(8.9f, 0, 6.9f), 60, 1.6f, "Officer");
            var director = Official(DirectorCharacter, new[] { "idle", "talk" }, new Vector3(10.1f, 0, 7.6f), 240, 1.75f, "Director");
            LobbyRoutine.Chat(officer, director, "chat", false, 21);
            LobbyRoutine.Chat(director, officer, "talk", true, 22);
            var clerk = Official(ClerkCharacter, new[] { "idle", "walk", "phone" }, new Vector3(-6.5f, 0, -1f), 180, 1.76f, "Clerk");
            HeldBoard.Attach(clerk, true, new Vector3(.24f, .32f, .02f), Mat(null, new Color(.16f, .22f, .42f), .3f), clipMat, null, owned);
            LobbyRoutine.Patrol(clerk, new[] { new Vector3(-6.5f, 0, -1f), new Vector3(-4.5f, 0, -10f), new Vector3(6f, 0, -9.8f), new Vector3(6.5f, 0, -.5f) }, "phone", 1.15f, 31);

            // The gate entry trigger.
            var gateMark = new GameObject("GateEntry").transform;
            gateMark.SetParent(transform, false);
            gateMark.localPosition = new Vector3(0, 0, 10.2f);
            interactables.Add(new Interactable { label = "게이트 진입", anchor = gateMark, radius = 2.8f, use = OpenGate });

            // Full-screen fade for the gate transition. It lives on the HUD canvas, so it keeps running after
            // the lobby is switched off for the run.
            if (hud && hud.Canvas) fade = GateFade.Create(hud.Canvas);
        }

        static LiminalMonsterLibrary Library => LiminalMonsterLibrary.Instance;

        void BuildGate(Material metal, Material dark)
        {
            var gate = new GameObject("Gate").transform;
            gate.SetParent(transform, false);
            gate.localPosition = new Vector3(0, 0, 13.5f);
            // Scaffold truss around the rift, as in the concept art.
            foreach (float x in new[] { -4.6f, 4.6f })
            {
                Block("TrussPillar", gate.localPosition + new Vector3(x, 3.6f, 0), new Vector3(.45f, 7.2f, .45f), metal, true);
                for (int k = 0; k < 4; k++)
                    Block("TrussBrace", gate.localPosition + new Vector3(x, .9f + k * 1.7f, 0), new Vector3(.08f, 2.1f, .08f), metal, false).localRotation = Quaternion.Euler(0, 0, (k % 2 == 0 ? 35 : -35));
                Block("TrussLight", gate.localPosition + new Vector3(x, 7.4f, -.3f), new Vector3(.6f, .4f, .4f), Mat(null, new Color(1, .95f, .8f), .8f, default, 0, new Color(2.2f, 2f, 1.6f)), false);
            }
            Block("TrussBeam", gate.localPosition + new Vector3(0, 7.3f, 0), new Vector3(9.7f, .45f, .45f), metal, true);
            Block("GatePlinth", gate.localPosition + new Vector3(0, .12f, 0), new Vector3(9.6f, .24f, 2.2f), dark, true);
            // The rift: a dark core inside layered, counter-rotating violet swirls, with a glow on the floor.
            gateCore = new GameObject("Rift").transform;
            gateCore.SetParent(gate, false);
            gateCore.localPosition = new Vector3(0, 3.6f, 0);
            Disc(gateCore, "Core", 3.3f, new Color(.08f, .02f, .2f, .97f), new Color(.45f, .22f, 1f, .9f), false, 0);
            gateRings = new Transform[4];
            for (int i = 0; i < gateRings.Length; i++)
            {
                gateRings[i] = Disc(gateCore, "Swirl" + i, 3.5f - i * .55f, new Color(.6f, .4f, 1f, 0), new Color(.75f, .55f, 1f, .75f - i * .1f), true, i * 1.7f + 1);
                gateRings[i].localPosition = Vector3.back * (.02f + i * .02f);
            }
            var floorGlow = Disc(gate, "FloorGlow", 5f, new Color(.5f, .3f, 1f, .5f), new Color(.5f, .3f, 1f, 0), true, 0);
            floorGlow.localPosition = new Vector3(0, .26f, -1.5f);
            floorGlow.localRotation = Quaternion.Euler(90, 0, 0);
            var lightGo = new GameObject("GateLight");
            lightGo.transform.SetParent(gate, false);
            lightGo.transform.localPosition = new Vector3(0, 3.6f, -2.5f);
            gateLight = lightGo.AddComponent<Light>();
            gateLight.type = LightType.Point;
            gateLight.color = new Color(.62f, .45f, 1f);
            gateLight.range = 16;
            gateLight.intensity = 5;
        }

        void BuildBooth(Material canopy, Material white, Material metal)
        {
            Vector3 c = new Vector3(-11.6f, 0, 1.8f);
            foreach (var (x, z) in new[] { (-1.6f, -1.6f), (1.6f, -1.6f), (-1.6f, 1.6f), (1.6f, 1.6f) })
                Block("TentPole", c + new Vector3(x, 1.3f, z), new Vector3(.08f, 2.6f, .08f), metal, false);
            Block("TentRoof", c + new Vector3(0, 2.65f, 0), new Vector3(3.6f, .12f, 3.6f), canopy, false);
            Block("TentPeak", c + new Vector3(0, 2.95f, 0), new Vector3(2.4f, .5f, 2.4f), canopy, false).localRotation = Quaternion.Euler(0, 45, 0);
            var valanceMat = Mat(LobbyTextures.Banner(), Color.white, .2f);
            if (valanceMat.HasProperty("_Cull")) valanceMat.SetFloat("_Cull", 0);
            var valance = Quad("TentValance", c + new Vector3(1.82f, 2.35f, 0), new Vector3(3.6f, .5f, 1), valanceMat);
            valance.localRotation = Quaternion.Euler(0, -90, 0);
            Block("Desk", c + new Vector3(.4f, .4f, 0), new Vector3(.7f, .8f, 2.4f), white, true);
            Block("DeskTop", c + new Vector3(.4f, .82f, 0), new Vector3(.8f, .05f, 2.5f), Mat(null, new Color(.12f, .17f, .33f), .5f), false);
            Block("Laptop", c + new Vector3(.4f, .95f, .5f), new Vector3(.35f, .02f, .5f), Mat(null, new Color(.15f, .15f, .17f), .6f), false);
        }

        LobbyNpc Official(string character, string[] clips, Vector3 localPosition, float yaw, float height, string objectName)
        {
            var npc = LobbyNpc.Create(transform, character, clips, localPosition, yaw, height, owned, objectName);
            officials.Add(npc);
            return npc;
        }

        // ---- per frame ---------------------------------------------------------------------------------------
        void Update()
        {
            float t = Time.time;
            if (gateRings != null)
                for (int i = 0; i < gateRings.Length; i++)
                {
                    gateRings[i].localRotation = Quaternion.Euler(0, 0, t * (i % 2 == 0 ? 40 : -55) * (1 + i * .3f));
                    gateRings[i].localScale = Vector3.one * (1 + .04f * Mathf.Sin(t * 2.3f + i));
                }
            if (gateLight) gateLight.intensity = 4.5f + Mathf.Sin(t * 3.1f) * .8f + Mathf.Sin(t * 7.7f) * .3f;
            sparkClock -= Time.deltaTime;
            if (gateCore && sparkClock <= 0)
            {
                sparkClock = .35f;
                float a = UnityEngine.Random.value * Mathf.PI * 2;
                HitFeedback.Sparks(gateCore.position + new Vector3(Mathf.Cos(a) * 3.2f, Mathf.Sin(a) * 3.2f, -.2f), new Vector3(Mathf.Cos(a), Mathf.Sin(a), -.3f), 3, .6f);
            }
            if (run == null || run.Phase != LiminalRunPhase.Lobby || entering) return;
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            bool interact = (keyboard != null && keyboard.eKey.wasPressedThisFrame) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            bool back = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
            if (window)
            {
                if (back || interact) CloseWindow();
                return;
            }
            Nearest = null;
            if (run.player)
            {
                float best = float.MaxValue;
                foreach (var i in interactables)
                {
                    Vector3 d = run.player.position - i.anchor.position; d.y = 0;
                    if (d.magnitude <= i.radius && d.magnitude < best) { best = d.magnitude; Nearest = i; }
                }
            }
            if (hud) hud.ShowPrompt("E", Nearest != null ? Nearest.label : null);
            if (agent) agent.LookAt(Nearest != null && Nearest.anchor == agent.transform && run.player ? run.player.position : (Vector3?)null);
            if (interact && Nearest != null) Nearest.use();
        }

        // ---- entering and leaving -------------------------------------------------------------------------
        public void Enter(Transform player)
        {
            gameObject.SetActive(true);
            entering = false;
            greeted = false;
            CloseWindow();
            previousAmbient = RenderSettings.ambientLight;
            RenderSettings.ambientLight = new Color(.62f, .66f, .76f);
            motor = player ? player.GetComponent<PlayerMotor>() : null;
            SetOutfit(player, true);
            if (motor)
            {
                motor.ResetAt(SpawnPoint);
                if (motor.visual) motor.visual.rotation = transform.rotation;
            }
            var cameraRig = FindFirstObjectByType<IsometricFollowCamera>();
            if (cameraRig) cameraRig.Snap();
        }

        public void Leave(Transform player)
        {
            CloseWindow();
            if (hud) hud.ShowPrompt(null, null);
            SetOutfit(player, false);
            RenderSettings.ambientLight = previousAmbient;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Everyday clothes in the lobby, dungeon outfit in gates (the concept's "gap" between the two). The
        /// casual model is a separate rigged Humanoid that plays the player's own locomotion controller.
        /// </summary>
        void SetOutfit(Transform player, bool casual)
        {
            if (!player) return;
            var m = player.GetComponent<PlayerMotor>();
            var melee = player.GetComponent<MeleeSlash>();
            if (melee) melee.SetWeaponVisible(!casual);
            if (!m) return;
            if (!combatAnimator) { combatAnimator = m.animator; combatModel = combatAnimator ? combatAnimator.gameObject : null; }
            if (casual && !casualModel) casualModel = CreateCasual(m);
            if (casualModel && combatModel)
            {
                combatModel.SetActive(!casual);
                casualModel.SetActive(casual);
                m.animator = casual ? casualModel.GetComponent<Animator>() : combatAnimator;
            }
        }

        GameObject CreateCasual(PlayerMotor m)
        {
            var source = Resources.Load<GameObject>(PlayerCasualPath);
            if (!source || !m.visual || !combatModel) return null;
            var go = Instantiate(source, m.visual);
            go.name = "CasualOutfit";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            // Match the combat model's height so the camera and interactions feel the same.
            float target = LobbyNpc.Height(combatModel.transform, 1.62f);
            LobbyNpc.FitHeight(go.transform, target);
            LobbyNpc.Skin(go, PlayerCasualTexture, owned);
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.runtimeAnimatorController = combatAnimator ? combatAnimator.runtimeAnimatorController : null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            go.SetActive(false);
            return go;
        }

        // ---- windows ------------------------------------------------------------------------------------------
        void OpenUpgrades()
        {
            if (!hud || !hud.Canvas) return;
            if (agent)
            {
                agent.Talk(true);
                // A formal bow the first time the hunter walks up in this visit, then she talks.
                if (!greeted) { greeted = true; agent.PlayOnce("bow"); }
            }
            ShowWindow(new Vector2(760, 470));
            var content = window.transform.Find("Window") as RectTransform;
            var font = hud.Font;
            HunterUi.Text("Org", content, font, "KOREA HUNTER ASSOCIATION · 능력 개발부", 12, HunterUi.Gold, new Vector2(30, -22), new Vector2(560, 18), FontStyles.Bold);
            HunterUi.Title(content, font, "한서윤 요원", new Vector2(30, -42), 700, 26);
            HunterUi.Text("Line", content, font, "어서 오세요, 헌터님. 오늘은 어떤 훈련을 받으시겠어요?", 16, HunterUi.Muted, new Vector2(30, -92), new Vector2(700, 24));
            HunterUi.Text("Wallet", content, font, $"보유 마석  <color=#a99bff>{HunterProgress.Currency:N0}</color>", 17, HunterUi.Cream, new Vector2(470, -24), new Vector2(260, 24), FontStyles.Bold, TextAlignmentOptions.TopRight);
            for (int i = 0; i < HunterProgress.Count; i++)
            {
                var upgrade = (HunterProgress.Upgrade)i;
                var info = HunterProgress.Describe(upgrade);
                int level = HunterProgress.Level(upgrade), cost = HunterProgress.Cost(upgrade);
                float y = -132 - i * 72;
                var row = HunterUi.Fill("Row" + i, content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, y), new Vector2(700, 62), new Color(.09f, .13f, .27f, 1)).rectTransform;
                HunterUi.Frame(row, HunterUi.GoldDim, 1);
                HunterUi.Text("Name", row, font, info.name, 19, HunterUi.Cream, new Vector2(16, -8), new Vector2(200, 26), FontStyles.Bold);
                HunterUi.Text("Effect", row, font, info.effect, 14, HunterUi.Muted, new Vector2(16, -36), new Vector2(420, 20));
                for (int k = 0; k < info.maxLevel; k++)
                    HunterUi.Fill("Pip", row, new Vector2(0, 1), new Vector2(0, 1), new Vector2(230 + k * 18, -16), new Vector2(12, 12), k < level ? HunterUi.Gold : new Color(.25f, .3f, .45f, 1));
                string label = cost < 0 ? "MAX" : $"강화   {cost:N0}";
                var button = HunterUi.Button("Buy" + i, row, font, label, new Vector2(530, -11), new Vector2(156, 40), () =>
                {
                    if (HunterProgress.TryBuy(upgrade))
                    {
                        HitFeedback.Play(HitFeedback.Sfx.JustDodge, .5f, 1.4f);
                        HitFeedback.ScreenFlash(new Color(HunterUi.Gold.r, HunterUi.Gold.g, HunterUi.Gold.b, .18f), .25f);
                        OpenUpgrades();
                    }
                    else HitFeedback.Play(HitFeedback.Sfx.Thud, .5f, 1.3f);
                }, 16);
                if (cost < 0 || HunterProgress.Currency < cost) button.GetComponent<Image>().color = new Color(.2f, .22f, .3f, 1);
            }
            HunterUi.Text("Close", content, font, "E · ESC  닫기", 13, HunterUi.Muted, new Vector2(30, -440), new Vector2(700, 20), FontStyles.Bold, TextAlignmentOptions.TopRight);
        }

        void OpenGate()
        {
            if (!hud || !hud.Canvas) return;
            ShowWindow(new Vector2(560, 360));
            var content = window.transform.Find("Window") as RectTransform;
            var font = hud.Font;
            HunterUi.Text("Org", content, font, "GATE CONTROL · 서울 용산 03", 12, HunterUi.Gold, new Vector2(30, -22), new Vector2(500, 18), FontStyles.Bold);
            HunterUi.Title(content, font, "게이트 진입", new Vector2(30, -42), 500, 26);
            var stage = run && run.stages != null && run.stages.Length > 0 ? run.stages[0] : null;
            string[] rows = { "등급", "B", "유형", "리미널 스페이스", "구성", $"일반 {Mathf.Max(1, (run && run.stages != null ? run.stages.Length : 4) - 1)} · 보스 1", "첫 구역", stage ? stage.title : "-" };
            for (int i = 0; i < rows.Length; i += 2)
            {
                float y = -100 - i / 2 * 36;
                HunterUi.Text("Key", content, font, rows[i], 15, HunterUi.Muted, new Vector2(40, y), new Vector2(120, 24), FontStyles.Bold);
                HunterUi.Text("Value", content, font, rows[i + 1], 18, HunterUi.Cream, new Vector2(170, y - 2), new Vector2(340, 26), FontStyles.Bold);
            }
            HunterUi.Button("Enter", content, font, "진입", new Vector2(40, -270), new Vector2(230, 56), BeginGateEntry);
            HunterUi.Button("Cancel", content, font, "취소", new Vector2(290, -270), new Vector2(230, 56), CloseWindow);
        }

        void ShowWindow(Vector2 size)
        {
            CloseWindow(keepTalking: true);
            if (hud) hud.ShowPrompt(null, null);
            if (motor) motor.enabled = false;
            window = new GameObject("LobbyWindow", typeof(RectTransform), typeof(Image));
            window.transform.SetParent(hud.Canvas, false);
            window.GetComponent<Image>().color = new Color(.03f, .04f, .09f, .55f);
            var r = window.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            HunterUi.Window("Window", window.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, size, hud.Font, null, true);
            if (fade) fade.transform.SetAsLastSibling();
        }

        void CloseWindow() => CloseWindow(false);

        void CloseWindow(bool keepTalking)
        {
            if (window) Destroy(window);
            window = null;
            if (!keepTalking && agent) agent.Talk(false);
            if (motor && run && run.Phase == LiminalRunPhase.Lobby && !entering) motor.enabled = true;
        }

        void BeginGateEntry()
        {
            CloseWindow();
            entering = true;
            if (motor) motor.enabled = false;
            HitFeedback.Play(HitFeedback.Sfx.JustDodge, .9f, .6f);
            // Into the rift: violet in, the run starts behind the curtain, violet out.
            void Go() { entering = false; run.EnterDungeon(); }
            if (fade) fade.Play(Go); else Go();
        }

        // ---- building helpers ---------------------------------------------------------------------------------
        Material Mat(Texture texture, Color color, float smoothness, Vector2 tiling = default, float metallic = 0, Color emission = default)
        {
            var m = LiminalMonsterKit.Lit(color, smoothness, metallic);
            if (texture)
            {
                m.mainTexture = texture;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
                if (tiling != default) { m.mainTextureScale = tiling; if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", tiling); }
            }
            if (emission != default && m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission); }
            owned.Add(m);
            return m;
        }

        /// <summary>Unlit, alpha-blended floor decal material.</summary>
        Material Decal(Texture texture)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", 0);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_Cull", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.mainTexture = texture;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            m.SetColor("_BaseColor", Color.white);
            m.renderQueue = 2990;
            owned.Add(m);
            return m;
        }

        Transform Block(string name, Vector3 localPosition, Vector3 size, Material material, bool collide)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (material) r.sharedMaterial = material; else r.enabled = false;
            if (!collide) Destroy(go.GetComponent<Collider>());
            return go.transform;
        }

        Transform Cylinder(string name, Vector3 localPosition, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(go.GetComponent<Collider>());
            return go.transform;
        }

        Transform Quad(string name, Vector3 localPosition, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(go.GetComponent<Collider>());
            return go.transform;
        }

        void Prop(GameObject prefab, Vector3 localPosition, float yaw)
        {
            if (!prefab) return;
            var go = Instantiate(prefab, transform);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
        }

        /// <summary>A flat disc facing -Z (toward the plaza) with a radial colour gradient; additive or alpha-blended.</summary>
        Transform Disc(Transform parent, string name, float radius, Color inner, Color outer, bool additive, float wobble)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            const int segments = 64, rings = 6;
            var mesh = new Mesh { name = name };
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            for (int r = 0; r <= rings; r++)
                for (int s = 0; s <= segments; s++)
                {
                    float u = r / (float)rings, a = s / (float)segments * Mathf.PI * 2;
                    // A swirl: the radius wobbles with angle and ring, so the bands read as twisting energy.
                    float rr = radius * u * (1 + (wobble > 0 ? .06f * Mathf.Sin(a * 3 + u * wobble * 4) : 0));
                    v.Add(new Vector3(Mathf.Cos(a + u * wobble) * rr, Mathf.Sin(a + u * wobble) * rr, 0));
                    var col = Color.Lerp(inner, outer, u);
                    if (wobble > 0) col.a *= Mathf.Clamp01(Mathf.Sin(u * Mathf.PI) * 1.6f) * (.6f + .4f * Mathf.Sin(a * 5 + wobble));
                    c.Add(col);
                }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = r * (segments + 1) + s, b = a + 1, cc = a + segments + 1, d = cc + 1;
                    t.Add(a); t.Add(cc); t.Add(b); t.Add(b); t.Add(cc); t.Add(d);
                }
            mesh.SetVertices(v); mesh.SetColors(c); mesh.SetTriangles(t, 0); mesh.RecalculateBounds();
            owned.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = additive ? HitFeedback.Additive : HitFeedback.Blended;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        void OnDestroy() { foreach (var o in owned) if (o) Destroy(o); }
    }

    /// <summary>Violet full-screen curtain for walking into a gate: fades in, runs the midpoint action, fades out.</summary>
    public sealed class GateFade : MonoBehaviour
    {
        Image image;
        Action midpoint;
        float started = -1;
        bool fired;
        static readonly Color Violet = new Color(.35f, .2f, .7f, 1);

        public static GateFade Create(Transform canvas)
        {
            var img = HunterUi.Fill("GateFade", canvas, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, new Color(Violet.r, Violet.g, Violet.b, 0));
            var r = img.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            img.transform.SetAsLastSibling();
            var fade = img.gameObject.AddComponent<GateFade>();
            fade.image = img;
            img.enabled = false;
            return fade;
        }

        public void Play(Action atMidpoint)
        {
            midpoint = atMidpoint;
            started = Time.unscaledTime;
            fired = false;
            image.enabled = true;
            transform.SetAsLastSibling();
        }

        void Update()
        {
            if (started < 0) return;
            float t = Time.unscaledTime - started;
            float a = t < .55f ? t / .55f : 1 - Mathf.Clamp01((t - .65f) / .6f);
            image.color = new Color(Violet.r, Violet.g, Violet.b, a);
            if (!fired && t >= .6f) { fired = true; midpoint?.Invoke(); }
            if (t >= 1.25f) { started = -1; image.enabled = false; }
        }
    }
}
