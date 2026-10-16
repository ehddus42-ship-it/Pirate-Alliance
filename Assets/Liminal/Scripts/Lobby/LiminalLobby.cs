using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// Active lobby: the Korea Hunter Association's gate control zone, a plaza in Seoul built at runtime.
    /// It has barricades, association banners, a blue booth, the skyline with N Seoul Tower, and a burning blue
    /// gate. Association staff and visiting hunters (<see cref="LobbyNpc"/>) consult the medic, check equipment,
    /// prepare for expeditions and take breaks while guards keep watch. The hunter walks it in everyday clothes:
    /// - talk to the association agent to buy permanent upgrades with magic stones;
    /// - talk to the loadout officer at the blue LCD terminal to pick the hunter and the support character;
    /// - walk into the gate to start a run.
    /// There are no menu buttons until the player interacts with something.
    /// </summary>
    public sealed class LiminalLobby : MonoBehaviour
    {
        public const string PlayerCasualPath = "LiminalLobby/player_casual/player_casual";
        public const string PlayerCasualTexture = "LiminalLobby/player_casual/player_casual_albedo";
        public const string PlayerCasualCharacter = "player_casual";
        public const float CasualHeight = 1.62f;
        // Association officials (Tools/LiminalLobby/meshy_lobby.py, OFFICIALS).
        public const string AgentCharacter = "association_agent";
        public const string ClerkCharacter = "association_clerk";
        public const string OfficerCharacter = "association_officer";
        public const string DirectorCharacter = "association_director";
        public const string GuardCharacter = "association_guard";
        public const string MedicCharacter = "field_medic";
        public const string EngineerCharacter = "gate_engineer";
        public const string RookieCharacter = "rookie_hunter";
        public const string VeteranCharacter = "veteran_hunter";
        // The loadout officer runs the party terminal: a gate engineer on comms duty, like an operator.
        public static readonly Vector3 LoadoutOfficerPosition = new Vector3(-10.3f, 0, 8.2f);
        public static readonly Vector3 LoadoutTerminalPosition = new Vector3(-11.6f, 0, 9.4f);

        public Vector3 SpawnPoint => transform.TransformPoint(new Vector3(0, .05f, -9));
        public bool WindowOpen => window;
        public Interactable Nearest { get; private set; }
        public LobbyNpc Agent => agent;
        public LobbyNpc LoadoutOfficer => loadoutOfficer;
        public bool LoadoutOpen => loadoutOpen;
        public IReadOnlyList<LobbyNpc> Officials => officials;
        public BlueFireGate GateFire { get; private set; }

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
        LobbyNpc agent, loadoutOfficer;
        readonly List<LobbyNpc> officials = new List<LobbyNpc>();
        bool loadoutOpen, casualWorn, officerGreeted;
        readonly List<LoadoutCard> hunterCards = new List<LoadoutCard>();
        readonly List<LoadoutCard> supportCards = new List<LoadoutCard>();
        TextMeshProUGUI loadoutCurrent;
        bool greeted;
        GameObject window;
        GateFade fade;
        bool entering;
        PlayerMotor motor;
        Animator combatAnimator;
        GameObject combatModel, casualModel;
        readonly List<GameObject> hiddenCombatParts = new List<GameObject>();
        Color previousAmbient;
        bool missionBoardOpen;
        string selectedMissionId;
        readonly List<UpgradeRow> upgradeRows = new List<UpgradeRow>();
        readonly List<MissionCard> missionCards = new List<MissionCard>();
        TextMeshProUGUI upgradeWallet, missionCode, missionDestination, missionBriefing, missionObjective,
            missionDifficulty, missionRoute, missionGimmick, missionGimmickDescription, missionDeparture;
        UnityEngine.UI.Button missionEnter, missionCancel;

        sealed class UpgradeRow
        {
            public HunterProgress.Upgrade upgrade;
            public TextMeshProUGUI price;
            public UnityEngine.UI.Button button;
            public UnityEngine.UI.Image[] pips;
        }

        sealed class LoadoutCard
        {
            public string id;
            public int index;
            public RectTransform root;
            public UnityEngine.UI.Image background, accent;
            public GameObject tag;
            public UnityEngine.UI.Button button;
        }

        sealed class MissionCard
        {
            public string id;
            public RectTransform root;
            public UnityEngine.UI.Image accent;
            public UnityEngine.UI.Button button;
        }

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
            var asphalt = LobbyRecoveryGround.CreateRoadMaterial(owned);
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
            LobbyRecoveryGround.Build(transform, owned);
            LobbyRestoredStreets.Build(transform, owned);
            LobbySymbolFixtures.Build(transform, owned);
            LobbyRecoveredBlock.Build(transform, owned);
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
                var guard = Official(GuardCharacter, new[] { "idle", "look", "greet" }, new Vector3(x, 0, 7.2f + Mathf.Abs(x) * .05f), 180 + (x < 0 ? 8 : -8), 1.82f, "Guard");
                LobbyRoutine.Station(guard, "look", 11 + guardIndex++);
            }

            // Association booth with the agent (left), a rest corner (right).
            BuildBooth(canopy, white, metal);
            Prop(Library?.waitingBench, new Vector3(10.5f, 0, -1.5f), -90);
            Prop(Library?.waitingBench, new Vector3(10.5f, 0, 2.4f), -90);
            Prop(Library?.planter, new Vector3(-13.5f, 0, -8), 0);
            Prop(Library?.planter, new Vector3(13.5f, 0, 8), 0);
            Prop(Library?.planter, new Vector3(-13.5f, 0, 8.5f), 0);
            Prop(Library?.trashBin, new Vector3(8.8f, 0, -6.5f), 0);
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

            // The loadout officer at the party terminal (upper left of the plaza): picks the hunter and the support.
            BuildLoadoutTerminal(LoadoutTerminalPosition, white, metal);
            loadoutOfficer = Official(EngineerCharacter, new[] { "idle", "talk", "greet" }, LoadoutOfficerPosition, 135, 1.79f, "LoadoutOfficer");
            interactables.Add(new Interactable { label = "헌터 편성", anchor = loadoutOfficer.transform, radius = 2.6f, use = OpenLoadout });

            // Ambient staff: a senior official briefing a junior by the rest corner, and a clerk walking his rounds
            // around the emblem, clear of the lamps and of the spawn point (he stops for the player and takes calls).
            // 0.82 m/s is the stride speed of the walk clip, so his feet do not skate.
            var officer = Official(OfficerCharacter, new[] { "idle", "chat", "walk", "greet" }, new Vector3(8.9f, 0, 6.9f), 60, 1.6f, "Officer");
            var director = Official(DirectorCharacter, new[] { "idle", "talk", "greet" }, new Vector3(10.1f, 0, 7.6f), 240, 1.75f, "Director");
            LobbyRoutine.ChatPair(director, "talk", officer, "chat", 21);
            var clerk = Official(ClerkCharacter, new[] { "idle", "walk", "phone", "greet" }, new Vector3(-6.5f, 0, -1f), 180, 1.76f, "Clerk");
            // The folder is in his right hand: the left one holds the phone during calls.
            HeldBoard.Attach(clerk, false, new Vector3(.24f, .32f, .02f), Mat(null, new Color(.16f, .22f, .42f), .3f), clipMat, null, owned);
            LobbyRoutine.Patrol(clerk, new[] { new Vector3(-6.5f, 0, -1f), new Vector3(-5.2f, 0, -7f), new Vector3(5.4f, 0, -7f), new Vector3(6.4f, 0, -.5f) }, "phone", .82f, 31);
            LobbyHandProp.Attach(clerk, LobbyHandProp.Kind.Phone, true, owned);
            BuildAmbientLife();

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

        void BuildAmbientLife()
        {
            LobbyActivityStations.Build(transform, owned);
            var medic = Official(MedicCharacter, new[] { "idle", "walk", "talk", "greet", "inspect", "listen" },
                LobbyActivityStations.MedicPosition, 90, 1.66f, "FieldMedic");
            var visitor = Official(RookieCharacter, new[] { "idle", "walk", "talk", "greet", "warmup", "breath" },
                LobbyActivityStations.PatientPosition, 270, 1.73f, "MedicalVisitor");
            LobbyRoutine.ChatPair(medic, new[] { "inspect", "talk", "listen" }, visitor, new[] { "talk", "breath" }, 47);

            var engineer = Official(EngineerCharacter, new[] { "idle", "walk", "talk", "greet", "phone", "inspect" },
                LobbyActivityStations.EngineerPosition, 0, 1.79f, "GateEngineer");
            LobbyRoutine.Station(engineer, new[] { "inspect", "phone", "talk" }, 59);
            LobbyHandProp.Attach(engineer, LobbyHandProp.Kind.Phone, true, owned);

            var rookie = Official(RookieCharacter, new[] { "idle", "walk", "talk", "greet", "warmup", "breath" },
                LobbyActivityStations.RookiePosition, 0, 1.77f, "RookieHunter");
            LobbyRoutine.Station(rookie, new[] { "warmup", "breath", "talk" }, 71);

            var veteran = Official(VeteranCharacter, new[] { "idle", "walk", "talk", "greet", "drink", "look" },
                LobbyActivityStations.VeteranPosition, 90, 1.71f, "VeteranHunter");
            LobbyRoutine.Station(veteran, new[] { "look", "drink", "talk" }, 83);
            LobbyHandProp.Attach(veteran, LobbyHandProp.Kind.TakeawayCup, true, owned);
        }

        void BuildGate(Material metal, Material dark)
        {
            var gate = new GameObject("Gate").transform;
            gate.SetParent(transform, false);
            gate.localPosition = new Vector3(0, 0, 13.5f);
            var casing = Mat(null, new Color(.12f, .18f, .23f), .38f, default, .55f);
            var reinforcement = Mat(null, new Color(.46f, .54f, .58f), .5f, default, .65f);
            var blueLens = Mat(null, new Color(.1f, .53f, .9f), .65f, default, .15f, new Color(.06f, .85f, 2.2f));
            // Repaired containment posts frame the fire without a crossbar hiding its rising crown.
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * 4.35f;
                Block("ContainmentFoot", gate.localPosition + new Vector3(x, .22f, .22f), new Vector3(1.05f, .44f, 1.5f), dark, true);
                Block("ContainmentPost", gate.localPosition + new Vector3(x, 2.75f, .32f), new Vector3(.36f, 5.5f, .42f), casing, true);
                Block("ContainmentBrace", gate.localPosition + new Vector3(x + side * .33f, 2.0f, .58f), new Vector3(.16f, 4.2f, .25f), metal, false).localRotation = Quaternion.Euler(0, 0, side * 9);
                foreach (float y in new[] { .9f, 2.9f, 4.9f })
                {
                    Block("BoltedRepairSleeve", gate.localPosition + new Vector3(x, y, .32f), new Vector3(.46f, .26f, .52f), reinforcement, false);
                    foreach (float bolt in new[] { -.12f, .12f })
                        Block("AnchorBolt", gate.localPosition + new Vector3(x + bolt, y, .045f), new Vector3(.04f, .055f, .026f), dark, false);
                }
                foreach (float y in new[] { 1.25f, 4.25f })
                {
                    Block("EmitterMount", gate.localPosition + new Vector3(x - side * .22f, y, -.07f), new Vector3(.62f, .68f, .30f), casing, false);
                    Cylinder("EmitterHousing", gate.localPosition + new Vector3(x - side * .22f, y, -.27f), new Vector3(.48f, .1f, .48f), metal).localRotation = Quaternion.Euler(90, 0, 0);
                    Cylinder("BlueEmitterLens", gate.localPosition + new Vector3(x - side * .22f, y, -.38f), new Vector3(.32f, .012f, .32f), blueLens).localRotation = Quaternion.Euler(90, 0, 0);
                }
            }
            Block("GatePlinth", gate.localPosition + new Vector3(0, .12f, 0), new Vector3(9.6f, .24f, 2.2f), dark, true);
            Block("GateThreshold", gate.localPosition + new Vector3(0, .246f, -1.02f), new Vector3(5.5f, .015f, .11f), blueLens, false);
            GateFire = BlueFireGate.Build(gate, owned);
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
            if (run == null || run.Phase != LiminalRunPhase.Lobby || entering) return;
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            bool interact = (keyboard != null && keyboard.eKey.wasPressedThisFrame) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            bool back = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
            if (window)
            {
                FitWindow();
                // Submit belongs to the focused button; gamepad A must not also close the dialog.
                if (back || (!missionBoardOpen && keyboard != null && keyboard.eKey.wasPressedThisFrame)) CloseWindow();
                return;
            }
            // With no lobby window to close, ESC asks whether to quit the game.
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { run.HandleEscape(); return; }
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
            bool atAgent = agent && Nearest != null && Nearest.anchor == agent.transform && run.player;
            if (agent) agent.LookAt(atAgent ? run.player.position : (Vector3?)null);
            // The agent greets the hunter with a formal bow the first time they walk up in this visit.
            if (atAgent && !greeted) { greeted = true; agent.PlayOnce("bow", null, LobbyRoutine.GreetSeconds); }
            bool atOfficer = loadoutOfficer && Nearest != null && Nearest.anchor == loadoutOfficer.transform && run.player;
            if (loadoutOfficer) loadoutOfficer.LookAt(atOfficer ? run.player.position : (Vector3?)null);
            if (atOfficer && !officerGreeted) { officerGreeted = true; loadoutOfficer.PlayOnce("greet", null, LobbyRoutine.GreetSeconds); }
            if (interact && Nearest != null) Nearest.use();
        }

        // ---- entering and leaving -------------------------------------------------------------------------
        public void Enter(Transform player)
        {
            gameObject.SetActive(true);
            entering = false;
            greeted = officerGreeted = false;
            CloseWindow();
            previousAmbient = RenderSettings.ambientLight;
            RenderSettings.ambientLight = new Color(.62f, .66f, .76f);
            motor = player ? player.GetComponent<PlayerMotor>() : null;
            // The first hunter walks the plaza in everyday clothes; another picked hunter keeps her gate outfit.
            var roster = run ? run.Roster : null;
            if (!roster || roster.CurrentUsesSceneModel) SetOutfit(player, true);
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
            if (casualWorn) SetOutfit(player, false);
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
                if (casualModel.transform.IsChildOf(combatModel.transform))
                {
                    // The combat Animator sits on motor.visual itself, which also parents the casual model: switching
                    // that GameObject off would hide the casual outfit too. Hide its other children (the combat mesh
                    // and armature) and stop its Animator instead.
                    if (casual)
                    {
                        hiddenCombatParts.Clear();
                        foreach (Transform child in combatModel.transform)
                            if (child != casualModel.transform && child.gameObject.activeSelf)
                            {
                                hiddenCombatParts.Add(child.gameObject);
                                child.gameObject.SetActive(false);
                            }
                    }
                    else
                    {
                        foreach (var part in hiddenCombatParts) if (part) part.SetActive(true);
                        hiddenCombatParts.Clear();
                    }
                    if (combatAnimator) combatAnimator.enabled = !casual;
                }
                else combatModel.SetActive(!casual);
                casualModel.SetActive(casual);
                // With its own locomotion the casual model is driven by CasualLocomotion, so the motor gets no
                // Animator (it would write combat parameters and torso twist into it).
                m.animator = casual ? (casualModel.GetComponent<CasualLocomotion>() ? null : casualModel.GetComponent<Animator>()) : combatAnimator;
            }
            casualWorn = casual;
        }

        GameObject CreateCasual(PlayerMotor m)
        {
            var source = Resources.Load<GameObject>(PlayerCasualPath);
            if (!source || !m.visual || !combatModel) return null;
            // The casual outfit is the 1.62 m the character was made at, the same scale as the association
            // officials (1.6-1.82 m); matching the taller combat model made her tower over them.
            const float target = CasualHeight;
            var go = Instantiate(source, m.visual);
            go.name = "CasualOutfit";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            LobbyNpc.FitHeight(go.transform, target);
            LobbyNpc.Skin(go, PlayerCasualTexture, owned);
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // Its own idle/walk/run (the combat controller's katana locomotion looked wrong in casual clothes).
            // Fallback when the clips are missing: the combat controller, as before.
            if (!CasualLocomotion.Attach(go, m, PlayerCasualCharacter))
                animator.runtimeAnimatorController = combatAnimator ? combatAnimator.runtimeAnimatorController : null;
            go.SetActive(false);
            return go;
        }

        // ---- windows ------------------------------------------------------------------------------------------
        void OpenUpgrades()
        {
            if (!hud || !hud.Canvas) return;
            if (agent) agent.Talk(true);
            ShowWindow(new Vector2(820, 564));
            var content = window.transform.Find("Window") as RectTransform;
            var font = hud.Font;
            HunterUi.Text("Org", content, font, "헌터 협회 · 능력 개발부", 13, HunterUi.Gold, new Vector2(30, -20), new Vector2(430, 26), FontStyles.Bold);
            HunterUi.Title(content, font, "한서윤 요원 · 영구 강화", new Vector2(30, -51), 760, 26);
            upgradeWallet = HunterUi.Text("Wallet", content, font, "", 17, HunterUi.Cream, new Vector2(490, -20), new Vector2(300, 32), FontStyles.Bold, TextAlignmentOptions.TopRight);
            for (int i = 0; i < HunterProgress.Count; i++)
            {
                var upgrade = (HunterProgress.Upgrade)i;
                var info = HunterProgress.Describe(upgrade);
                float y = -124 - i * 88;
                var row = HunterUi.Fill("Row" + i, content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, y), new Vector2(760, 78), new Color(.09f, .13f, .27f, 1)).rectTransform;
                HunterUi.Frame(row, HunterUi.GoldDim, 1);
                HunterUi.Text("Name", row, font, info.name, 19, HunterUi.Cream, new Vector2(16, -9), new Vector2(214, 32), FontStyles.Bold);
                HunterUi.Text("Effect", row, font, info.effect, 14, HunterUi.Muted, new Vector2(16, -42), new Vector2(520, 28));
                var view = new UpgradeRow { upgrade = upgrade, pips = new UnityEngine.UI.Image[info.maxLevel] };
                for (int k = 0; k < info.maxLevel; k++)
                    view.pips[k] = HunterUi.Fill("Pip", row, new Vector2(0, 1), new Vector2(0, 1), new Vector2(252 + k * 20, -19), new Vector2(12, 12), HunterUi.GoldDim);
                var button = HunterUi.Button("Buy" + i, row, font, "강화", new Vector2(558, -17), new Vector2(186, 44), () =>
                {
                    if (HunterProgress.TryBuy(upgrade))
                    {
                        HitFeedback.Play(HitFeedback.Sfx.JustDodge, .5f, 1.4f);
                        HitFeedback.ScreenFlash(new Color(HunterUi.Gold.r, HunterUi.Gold.g, HunterUi.Gold.b, .18f), .25f);
                        RefreshUpgrades();
                    }
                    else HitFeedback.Play(HitFeedback.Sfx.Thud, .5f, 1.3f);
                }, 16);
                view.button = button.GetComponent<UnityEngine.UI.Button>();
                view.price = button.Find("Label").GetComponent<TextMeshProUGUI>();
                upgradeRows.Add(view);
            }
            HunterUi.Text("Hint", content, font, "강화 효과는 다음 게이트 진입부터 적용돼.", 14, HunterUi.Muted, new Vector2(30, -500), new Vector2(490, 30));
            HunterUi.Button("Close", content, font, "닫기  [ESC]", new Vector2(590, -491), new Vector2(200, 44), CloseWindow, 16);
            RefreshUpgrades();
        }

        void RefreshUpgrades()
        {
            if (upgradeWallet) upgradeWallet.text = $"보유 마석  <color=#a99bff>{HunterProgress.Currency:N0}</color>";
            foreach (var row in upgradeRows)
            {
                int level = HunterProgress.Level(row.upgrade), cost = HunterProgress.Cost(row.upgrade);
                row.price.text = cost < 0 ? "강화 완료" : $"강화   {cost:N0}";
                row.button.interactable = cost >= 0 && HunterProgress.Currency >= cost;
                for (int k = 0; k < row.pips.Length; k++) row.pips[k].color = k < level ? HunterUi.Gold : new Color(.25f, .3f, .45f, 1);
            }
        }

        void OpenGate()
        {
            if (!hud || !hud.Canvas) return;
            ShowWindow(new Vector2(1080, 644));
            missionBoardOpen = true;
            var content = window.transform.Find("Window") as RectTransform;
            var font = hud.Font;
            HunterUi.Text("Org", content, font, "헌터 협회 · 게이트 관제", 13, HunterUi.Gold, new Vector2(28, -18), new Vector2(620, 26), FontStyles.Bold);
            HunterUi.Title(content, font, "출동 미션 선택", new Vector2(28, -47), 1024, 28);
            HunterUi.Text("DiscoveryHint", content, font, "미탐사 목적지는 ???로 표시돼. 직접 진입하면 정보가 기록돼.", 14, HunterUi.Muted, new Vector2(28, -107), new Vector2(1024, 28));
            var missions = run.AvailableMissions;
            bool compactCards = missions.Count > 5;
            float cardHeight = compactCards ? 62 : 76;
            float cardStride = compactCards ? 70 : 84;
            for (int i = 0; i < missions.Count; i++)
            {
                var mission = missions[i];
                string id = mission.id;
                var card = HunterUi.Button("Mission_" + id, content, font, "", new Vector2(28, -139 - i * cardStride), new Vector2(308, cardHeight), () => SelectMission(id));
                var view = new MissionCard { id = id, root = card, button = card.GetComponent<UnityEngine.UI.Button>() };
                view.accent = HunterUi.Fill("Selection", card, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(4, cardHeight), HunterUi.Gold);
                HunterUi.Text("Code", card, font, mission.code + "  /  " + mission.title, 11, HunterUi.Gold, new Vector2(16, -5), new Vector2(276, 20), FontStyles.Bold);
                HunterUi.Text("Destination", card, font, mission.DisplayDestination, compactCards ? 18 : 20, HunterUi.Cream, new Vector2(16, compactCards ? -20 : -22), new Vector2(276, compactCards ? 26 : 32), FontStyles.Bold);
                HunterUi.Text("Intel", card, font, $"{mission.difficultyName}  ·  {mission.gimmickName}", compactCards ? 11 : 12, HunterUi.Muted, new Vector2(16, compactCards ? -42 : -53), new Vector2(276, compactCards ? 19 : 22));
                card.gameObject.AddComponent<GateMissionCardFocus>().Selected = () => SelectMission(id);
                missionCards.Add(view);
            }
            var detail = HunterUi.Window("MissionDetails", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(356, -139), new Vector2(696, 412), font);
            missionCode = HunterUi.Text("Code", detail, font, "", 13, HunterUi.Gold, new Vector2(24, -14), new Vector2(648, 26), FontStyles.Bold);
            missionDestination = HunterUi.Text("Destination", detail, font, "", 30, HunterUi.Cream, new Vector2(24, -44), new Vector2(648, 48), FontStyles.Bold);
            missionBriefing = HunterUi.Text("Briefing", detail, font, "", 15, HunterUi.Muted, new Vector2(24, -100), new Vector2(648, 54));
            HunterUi.Fill("Rule", detail, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -162), new Vector2(648, 1), HunterUi.GoldDim);
            missionDifficulty = HunterUi.Text("Difficulty", detail, font, "", 17, HunterUi.Gold, new Vector2(24, -178), new Vector2(320, 32), FontStyles.Bold);
            missionRoute = HunterUi.Text("Route", detail, font, "", 16, HunterUi.Cream, new Vector2(358, -178), new Vector2(314, 32), FontStyles.Normal, TextAlignmentOptions.TopRight);
            missionObjective = HunterUi.Text("Objective", detail, font, "", 15, HunterUi.Cream, new Vector2(24, -223), new Vector2(648, 52));
            missionGimmick = HunterUi.Text("Gimmick", detail, font, "", 17, HunterUi.Gate, new Vector2(24, -292), new Vector2(648, 32), FontStyles.Bold);
            missionGimmickDescription = HunterUi.Text("GimmickDescription", detail, font, "", 15, HunterUi.Cream, new Vector2(24, -336), new Vector2(648, 60));
            missionDeparture = HunterUi.Text("Departure", content, font, "", 14, HunterUi.Muted, new Vector2(28, -575), new Vector2(308, 48));
            missionEnter = HunterUi.Button("Enter", content, font, "선택한 미션으로 진입", new Vector2(356, -575), new Vector2(446, 48), BeginGateEntry, 18).GetComponent<UnityEngine.UI.Button>();
            missionCancel = HunterUi.Button("Cancel", content, font, "닫기  [ESC]", new Vector2(824, -575), new Vector2(228, 48), CloseWindow, 16).GetComponent<UnityEngine.UI.Button>();
            if (missions.Count == 0)
            {
                missionDestination.text = "진입 가능한 미션이 없어";
                missionBriefing.text = "게이트 정보를 확인할 수 없어. 잠시 후 다시 확인해 줘.";
                missionEnter.interactable = false;
                return;
            }
            if (!missionCards.Exists(c => c.id == selectedMissionId)) selectedMissionId = missions[0].id;
            SelectMission(selectedMissionId);
            for (int i = 0; i < missionCards.Count; i++)
                missionCards[i].button.navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = missionCards[Mathf.Max(0, i - 1)].button,
                    selectOnDown = i + 1 < missionCards.Count ? missionCards[i + 1].button : missionEnter,
                    selectOnRight = missionEnter };
            missionCancel.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = missionEnter, selectOnUp = missionEnter };
            var selected = missionCards.Find(c => c.id == selectedMissionId);
            if (EventSystem.current && selected != null) EventSystem.current.SetSelectedGameObject(selected.root.gameObject);
        }

        void SelectMission(string id)
        {
            if (!missionBoardOpen) return;
            GateMissionDefinition selected = null;
            foreach (var mission in run.AvailableMissions) if (mission.id == id) { selected = mission; break; }
            if (selected == null) return;
            selectedMissionId = id;
            foreach (var card in missionCards)
            {
                bool active = card.id == id;
                card.accent.color = active ? HunterUi.Gold : Color.clear;
                card.root.GetComponent<UnityEngine.UI.Image>().color = active ? new Color(.22f, .27f, .46f, 1) : new Color(.10f, .14f, .28f, 1);
            }
            missionCode.text = selected.code + "  /  " + selected.title + (selected.IsDiscovered ? "  /  탐사 기록 있음" : "  /  미탐사");
            missionDestination.text = selected.DisplayDestination;
            missionBriefing.color = HunterUi.Muted;
            missionBriefing.text = selected.IsDiscovered ? selected.briefing : "아직 탐사 기록이 없는 목적지야. 아래의 위험 정보를 확인하고 출동해 줘.";
            missionDifficulty.text = $"난이도  {selected.difficultyName}  {selected.difficulty}/3";
            missionRoute.text = $"{selected.StageCount}개 구역  ·  총 {selected.RoomCount}개 방";
            missionObjective.text = $"임무  {selected.objective}\n적 체력 {selected.healthMultiplier:0.##}배";
            missionGimmick.text = "적 특수 기믹  /  " + selected.gimmickName;
            missionGimmickDescription.text = selected.gimmickDescription;
            missionDeparture.text = $"선택한 목적지\n{selected.code}  ·  {selected.DisplayDestination}";
            missionEnter.interactable = selected.IsAvailable;
            var currentCard = missionCards.Find(c => c.id == id);
            missionEnter.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = currentCard?.button,
                selectOnUp = currentCard?.button, selectOnDown = missionCancel, selectOnRight = missionCancel };
        }

        // ---- loadout (헌터 · 지원 캐릭터 편성) -----------------------------------------------------------------
        void OpenLoadout()
        {
            if (!hud || !hud.Canvas) return;
            if (loadoutOfficer) loadoutOfficer.Talk(true);
            ShowWindow(new Vector2(1040, 640));
            loadoutOpen = true;
            var content = window.transform.Find("Window") as RectTransform;
            var font = hud.Font;
            HunterUi.Text("Org", content, font, "헌터 협회 · 편성 관리부", 13, HunterUi.Gold, new Vector2(30, -20), new Vector2(430, 26), FontStyles.Bold);
            HunterUi.Title(content, font, "헌터 · 지원 캐릭터 편성", new Vector2(30, -51), 980, 26);
            HunterUi.Text("Hint", content, font, "카드를 고르면 바로 편성돼. 다음 게이트부터 함께 출동해.", 14, HunterUi.Muted, new Vector2(30, -100), new Vector2(980, 26));
            HunterUi.Text("HunterHeader", content, font, "HUNTER  ·  플레이 캐릭터", 14, HunterUi.Gold, new Vector2(30, -134), new Vector2(470, 24), FontStyles.Bold);
            HunterUi.Text("SupportHeader", content, font, "SUPPORT  ·  지원 캐릭터", 14, HunterUi.Gold, new Vector2(540, -134), new Vector2(470, 24), FontStyles.Bold);
            HunterUi.Fill("Divider", content, new Vector2(0, 1), new Vector2(0, 1), new Vector2(519, -134), new Vector2(1.2f, 432), HunterUi.GoldDim);

            var roster = run.Roster;
            int hunters = roster ? roster.Count : 0;
            bool compactHunters = hunters > 2;
            int hunterColumns = compactHunters ? 2 : Mathf.Max(1, hunters);
            float hunterWidth = CardWidth(hunterColumns);
            for (int i = 0; i < hunters; i++)
            {
                int index = i;
                string role = i == 0 ? "플레이어블 · 기본 헌터" : "플레이어블";
                var position = new Vector2(30 + (i % hunterColumns) * (hunterWidth + 15),
                    -166 - (i / hunterColumns) * (CompactLoadoutCardHeight + 10));
                var card = LoadoutCardView("Hunter_" + roster.IdAt(i), content, font, position, hunterWidth,
                    roster.NameAt(i), role, roster.SummaryAt(i), roster.PortraitAt(i), .5f, () => ChangeHunter(index), compactHunters);
                card.id = roster.IdAt(i);
                card.index = i;
                hunterCards.Add(card);
            }
            var supports = SupportCharacterCatalog.All;
            float supportWidth = CardWidth(supports.Count);
            for (int i = 0; i < supports.Count; i++)
            {
                var info = supports[i];
                string id = info.id;
                var card = LoadoutCardView("Support_" + id, content, font, new Vector2(540 + i * (supportWidth + 15), -166), supportWidth,
                    info.name, info.kind + "  ·  " + info.skillName, info.summary, info.Portrait, id == SupportCharacterCatalog.Kokkio ? .39f : .5f,
                    () => ChangeSupport(id));
                card.id = id;
                card.index = i;
                supportCards.Add(card);
            }
            loadoutCurrent = HunterUi.Text("Current", content, font, "", 15, HunterUi.Cream, new Vector2(30, -588), new Vector2(700, 30), FontStyles.Bold);
            var close = HunterUi.Button("Close", content, font, "편성 완료  [ESC]", new Vector2(810, -578), new Vector2(200, 44), CloseWindow, 16);
            RefreshLoadout();
            LoadoutCard focus = null;
            foreach (var card in hunterCards) if (roster && card.index == roster.SelectedIndex) focus = card;
            if (focus == null && hunterCards.Count > 0) focus = hunterCards[0];
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(focus != null ? focus.root.gameObject : close.gameObject);
        }

        const float CompactLoadoutCardHeight = 195;

        static float CardWidth(int count) => Mathf.Min(225, (470 - (count - 1) * 15f) / count);

        LoadoutCard LoadoutCardView(string name, RectTransform parent, TMP_FontAsset font, Vector2 position, float width, string title,
            string role, string summary, Texture2D portrait, float focusX, Action pick, bool compact = false)
        {
            float height = compact ? CompactLoadoutCardHeight : 400;
            float art = compact ? 90 : 250;
            var root = HunterUi.Button(name, parent, font, "", position, new Vector2(width, height), pick);
            var view = new LoadoutCard { root = root, button = root.GetComponent<UnityEngine.UI.Button>(), background = root.GetComponent<UnityEngine.UI.Image>() };
            var frame = HunterUi.Fill("Art", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -8), new Vector2(width - 16, art), new Color(.04f, .06f, .13f, 1)).rectTransform;
            frame.gameObject.AddComponent<RectMask2D>();
            if (portrait)
            {
                var image = HunterUi.Rect("Illustration", frame, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(width - 16, art)).gameObject.AddComponent<RawImage>();
                image.texture = portrait;
                image.raycastTarget = false;
                image.uvRect = PortraitWindow(portrait, (width - 16) / art, focusX);
            }
            else
            {
                // No illustration yet: the hunter's initial as a name plate.
                HunterUi.Text("Initial", frame, font, string.IsNullOrEmpty(title) ? "?" : title.Substring(0, 1), compact ? 36 : 96, HunterUi.GoldDim,
                    new Vector2(0, compact ? -4 : -50), new Vector2(width - 16, compact ? 56 : 130), FontStyles.Bold, TextAlignmentOptions.Center);
                HunterUi.Text("Pending", frame, font, "일러스트 준비 중", compact ? 10 : 12, HunterUi.Muted,
                    new Vector2(0, compact ? -62 : -200), new Vector2(width - 16, compact ? 20 : 24), FontStyles.Normal, TextAlignmentOptions.Center);
            }
            view.accent = HunterUi.Fill("Selection", root, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(width, 4), HunterUi.Gold);
            var tag = HunterUi.Fill("InParty", root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-14, -14), new Vector2(70, 24), HunterUi.Gold).rectTransform;
            HunterUi.Text("InPartyLabel", tag, font, "편성 중", 12, HunterUi.NavyDeep, Vector2.zero, new Vector2(70, 24), FontStyles.Bold, TextAlignmentOptions.Center);
            view.tag = tag.gameObject;
            HunterUi.Text("Name", root, font, title, compact ? 17 : 22, HunterUi.Cream,
                new Vector2(14, compact ? -102 : -266), new Vector2(width - 28, compact ? 28 : 32), FontStyles.Bold);
            HunterUi.Text("Role", root, font, role, compact ? 11 : 12, HunterUi.Gold,
                new Vector2(14, compact ? -130 : -300), new Vector2(width - 28, compact ? 18 : 22), FontStyles.Bold);
            HunterUi.Text("Summary", root, font, summary ?? "", compact ? 12 : 13, HunterUi.Muted,
                new Vector2(14, compact ? -151 : -324), new Vector2(width - 28, compact ? 38 : 68));
            return view;
        }

        /// <summary>UV window over the illustration's upper part (head to hips) with the card's aspect.</summary>
        static Rect PortraitWindow(Texture texture, float aspect, float focusX)
        {
            float h = .64f;
            float w = h * texture.height * aspect / Mathf.Max(1, texture.width);
            if (w > 1) { h /= w; w = 1; }
            return new Rect(Mathf.Clamp(focusX - w * .5f, 0, 1 - w), 1 - h, w, h);
        }

        void RefreshLoadout()
        {
            var roster = run ? run.Roster : null;
            foreach (var card in hunterCards) Highlight(card, roster && card.index == roster.SelectedIndex);
            string support = HunterLoadout.SupportId;
            foreach (var card in supportCards) Highlight(card, card.id == support);
            if (loadoutCurrent)
            {
                string hunterName = roster ? roster.NameAt(roster.SelectedIndex) : HunterRoster.FirstHunterName;
                var supportInfo = SupportCharacterCatalog.Selected;
                loadoutCurrent.text = "현재 편성   " + hunterName + "  +  " + (supportInfo != null ? supportInfo.name : "-");
            }
        }

        static void Highlight(LoadoutCard card, bool active)
        {
            card.accent.color = active ? HunterUi.Gold : Color.clear;
            card.background.color = active ? new Color(.22f, .27f, .46f, 1) : new Color(.10f, .14f, .28f, 1);
            card.tag.SetActive(active);
        }

        void ChangeHunter(int index)
        {
            var roster = run ? run.Roster : null;
            if (!loadoutOpen || !roster || !run.player || index == roster.SelectedIndex) { RefreshLoadout(); return; }
            // Everyday clothes belong to the first hunter: take them off before another hunter steps in, and put
            // them back on when she returns.
            if (casualWorn) SetOutfit(run.player, false);
            roster.Choose(index);
            if (roster.CurrentUsesSceneModel) SetOutfit(run.player, true);
            HitFeedback.ScreenFlash(new Color(HunterUi.Gold.r, HunterUi.Gold.g, HunterUi.Gold.b, .14f), .25f);
            RefreshLoadout();
        }

        void ChangeSupport(string id)
        {
            if (!loadoutOpen) return;
            if (id != HunterLoadout.SupportId)
            {
                HunterLoadout.SupportId = id;
                HitFeedback.Play(HitFeedback.Sfx.JustDodge, .45f, 1.5f);
                HitFeedback.ScreenFlash(new Color(HunterUi.Gold.r, HunterUi.Gold.g, HunterUi.Gold.b, .14f), .25f);
            }
            RefreshLoadout();
        }

        /// <summary>A console desk with two blue LCD monitors, like an operator's comms station, screens facing the plaza.</summary>
        void BuildLoadoutTerminal(Vector3 c, Material white, Material metal)
        {
            var dark = Mat(null, new Color(.06f, .07f, .1f), .5f);
            var top = Mat(null, new Color(.12f, .17f, .33f), .5f);
            Block("TerminalDesk", c + new Vector3(0, .42f, 0), new Vector3(2.1f, .84f, .7f), white, true);
            Block("TerminalTop", c + new Vector3(0, .865f, 0), new Vector3(2.2f, .05f, .8f), top, false);
            Block("TerminalKeyboard", c + new Vector3(0, .9f, -.18f), new Vector3(.7f, .025f, .22f), dark, false);
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -.52f : .52f;
                Block("MonitorStand", c + new Vector3(x, 1.05f, .16f), new Vector3(.07f, .36f, .07f), metal, false);
                Block("MonitorBezel", c + new Vector3(x, 1.47f, .16f), new Vector3(.94f, .58f, .06f), dark, false);
                Quad("MonitorScreen", c + new Vector3(x, 1.47f, .125f), new Vector3(.86f, .5f, 1), Lcd(LcdTexture(i)));
            }
            // A blue status strip over the station, readable from across the plaza.
            Block("TerminalSign", c + new Vector3(0, 1.98f, .2f), new Vector3(2f, .2f, .05f), Lcd(LcdTexture(2)), false);
            Block("SignPost", c + new Vector3(-1.02f, 1.2f, .25f), new Vector3(.06f, 1.7f, .06f), metal, false);
            Block("SignPost", c + new Vector3(1.02f, 1.2f, .25f), new Vector3(.06f, 1.7f, .06f), metal, false);
        }

        Material Lcd(Texture texture)
        {
            var m = Mat(texture, Color.white, .7f);
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", texture);
                m.SetColor("_EmissionColor", new Color(1.3f, 1.4f, 1.6f));
            }
            return m;
        }

        /// <summary>Blue LCD screens: 0 = hunter roster, 1 = support link, 2 = the status strip.</summary>
        Texture2D LcdTexture(int variant)
        {
            int w = variant == 2 ? 80 : 64, h = variant == 2 ? 8 : 40;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            owned.Add(t);
            var back = new Color(.02f, .08f, .2f);
            var line = new Color(.25f, .65f, 1f);
            var bright = new Color(.6f, .9f, 1f);
            var random = new System.Random(17 + variant);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    t.SetPixel(x, y, y % 2 == 0 ? back : back * 1.25f);
            if (variant == 2)
            {
                for (int x = 2; x < w - 5; x += 6)
                    for (int y = 2; y < 6; y++)
                        for (int k = 0; k < 4; k++) t.SetPixel(x + k, y, random.Next(3) == 0 ? bright : line);
            }
            else
            {
                for (int x = 0; x < w; x++) for (int y = h - 5; y < h - 2; y++) t.SetPixel(x, y, line);
                int cards = variant == 0 ? 2 : 1;
                for (int c = 0; c < cards; c++)
                {
                    int x0 = 3 + c * 15;
                    for (int x = x0; x < x0 + 12; x++)
                        for (int y = 5; y < 31; y++)
                        {
                            bool edge = x == x0 || x == x0 + 11 || y == 5 || y == 30;
                            float dx = (x - (x0 + 5.5f)) / 3.2f, dy = (y - 22f) / 3.6f;
                            bool head = dx * dx + dy * dy <= 1;
                            bool body = y < 18 && Mathf.Abs(x - (x0 + 5.5f)) < 4.5f - (18 - y) * .1f;
                            if (edge) t.SetPixel(x, y, line);
                            else if (head || body) t.SetPixel(x, y, bright * .8f);
                        }
                }
                for (int row = 0; row < 5; row++)
                {
                    int x0 = 6 + cards * 15, length = 8 + random.Next(w - x0 - 12);
                    for (int x = x0; x < x0 + length; x++) t.SetPixel(x, 27 - row * 5, row == 0 ? bright : line);
                }
            }
            t.Apply();
            return t;
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
            FitWindow();
            if (fade) fade.transform.SetAsLastSibling();
        }

        void FitWindow()
        {
            if (!window) return;
            var available = window.GetComponent<RectTransform>().rect.size;
            var panel = window.transform.Find("Window") as RectTransform;
            if (!panel || available.x <= 0 || available.y <= 0) return;
            float scale = Mathf.Min(1, (available.x - 32) / panel.sizeDelta.x, (available.y - 32) / panel.sizeDelta.y);
            panel.localScale = Vector3.one * Mathf.Max(.1f, scale);
        }

        void CloseWindow() => CloseWindow(false);

        void CloseWindow(bool keepTalking)
        {
            if (window) { window.SetActive(false); Destroy(window); }
            window = null;
            missionBoardOpen = false;
            loadoutOpen = false;
            upgradeRows.Clear();
            missionCards.Clear();
            hunterCards.Clear();
            supportCards.Clear();
            if (!keepTalking && agent) agent.Talk(false);
            if (!keepTalking && loadoutOfficer) loadoutOfficer.Talk(false);
            if (motor && run && run.Phase == LiminalRunPhase.Lobby && !entering) motor.enabled = true;
        }

        void BeginGateEntry()
        {
            if (entering || !missionBoardOpen || string.IsNullOrEmpty(selectedMissionId)) return;
            string missionId = selectedMissionId;
            CloseWindow();
            entering = true;
            if (motor) motor.enabled = false;
            HitFeedback.Play(HitFeedback.Sfx.JustDodge, .9f, .6f);
            // Into the rift: violet in, the run starts behind the curtain, violet out.
            void Go()
            {
                entering = false;
                if (!run.EnterMission(missionId) && run.Phase == LiminalRunPhase.Lobby)
                {
                    if (motor) motor.enabled = true;
                    OpenGate();
                    if (missionBriefing)
                    {
                        missionBriefing.text = run.LastMissionError ?? "미션에 진입할 수 없어. 다른 미션을 선택해 줘.";
                        missionBriefing.color = HunterUi.Danger;
                    }
                }
            }
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

        void OnDestroy() { foreach (var o in owned) if (o) Destroy(o); }
    }

    public sealed class GateMissionCardFocus : MonoBehaviour, ISelectHandler
    {
        public Action Selected;
        public void OnSelect(BaseEventData eventData) => Selected?.Invoke();
    }

    /// <summary>Deep blue gate transition. Runs the mission action once at the opaque midpoint.</summary>
    public sealed class GateFade : MonoBehaviour
    {
        Image image;
        Action midpoint;
        float started = -1;
        bool fired;
        static readonly Color GateBlue = new Color(.025f, .12f, .32f, 1);

        public static GateFade Create(Transform canvas)
        {
            var img = HunterUi.Fill("GateFade", canvas, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, new Color(GateBlue.r, GateBlue.g, GateBlue.b, 0));
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
            image.color = new Color(GateBlue.r, GateBlue.g, GateBlue.b, a);
            if (!fired && t >= .6f) { fired = true; midpoint?.Invoke(); }
            if (t >= 1.25f) { started = -1; image.enabled = false; }
        }
    }
}
