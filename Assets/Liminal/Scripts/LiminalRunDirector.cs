using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AcRoguelike.Liminal
{
    public enum LiminalRunPhase { Exploring, AugmentChoice, NextStageChoice, Victory, Defeat, Paused, InvalidConfiguration, Lobby }

    [DisallowMultipleComponent]
    public sealed class LiminalRunDirector : MonoBehaviour
    {
        public LiminalStageDefinition[] stages = new LiminalStageDefinition[0];
        [Tooltip("Optional override. Otherwise loads the catalog at Resources/GateMissions.")]
        public GateMissionCatalog missionCatalog;
        public Transform player;
        public Font hudFont;
        public int seed = 73029;
        [Tooltip("Start in the walkable hunter lobby (talk to the agent, use the gate) instead of straight in a gate.")]
        public bool startInLobby = true;
        /// <summary>Magic stones earned in the current run, paid out on returning to the lobby.</summary>
        public int PendingReward { get; private set; }
        public LiminalLobby Lobby => lobby;
        public GateMissionDefinition ActiveMission { get; private set; }
        public string LastMissionError { get; private set; }
        public IReadOnlyList<GateMissionDefinition> AvailableMissions
        {
            get
            {
                if (!missionCatalog) missionCatalog = Resources.Load<GateMissionCatalog>("GateMissions");
                return missionCatalog ? missionCatalog.Missions : Array.Empty<GateMissionDefinition>();
            }
        }
        public int StageIndex { get; private set; }
        public int ActiveRoomIndex { get; private set; }
        public int LivingEnemyCount => living.Count;
        public LiminalRunPhase Phase { get; private set; }
        public IReadOnlyList<LiminalRoom> Rooms => rooms;
        public LiminalPlayerHealth PlayerHealth => health;
        public LiminalStageDefinition CurrentStage => stages != null && StageIndex >= 0 && StageIndex < stages.Length ? stages[StageIndex] : null;
        public string CurrentRoomName => ActiveRoomIndex >= 0 && ActiveRoomIndex < rooms.Count ? rooms[ActiveRoomIndex].displayName : "";
        public int ClearedRoomCount { get; private set; }
        public bool ExitAvailable => Phase == LiminalRunPhase.Exploring && AllRoomsCleared() && NearExit();
        public string AugmentHistory { get; private set; } = "증강 없음";
        public event Action StageChanged;
        public event Action RoomChanged;

        readonly List<LiminalRoom> rooms = new List<LiminalRoom>();
        readonly HashSet<TrainingEnemy> living = new HashSet<TrainingEnemy>();
        bool[] visited = new bool[0];
        bool[] cleared = new bool[0];
        Transform generated;
        PlayerMotor motor;
        TalismanCaster caster;
        LiminalPlayerHealth health;
        LiminalHud hud;
        LiminalLobby lobby;
        PlayerCombat combat;
        int runsStarted;
        static readonly Vector3 LobbyOffset = new Vector3(0, 0, -420);
        float originalMoveSpeed, originalWalkSpeed, originalCooldown;
        int originalFlames;
        bool initialized;
        LiminalStageDefinition[] originalStages;

        void Start()
        {
            // Preserve this scene's authored route. Directly played theme/test scenes keep their own stages.
            originalStages = stages == null ? Array.Empty<LiminalStageDefinition>() : (LiminalStageDefinition[])stages.Clone();
            if (!player)
            {
                var found = FindFirstObjectByType<PlayerMotor>();
                if (found) player = found.transform;
            }
            if (player)
            {
                motor = player.GetComponent<PlayerMotor>();
                caster = player.GetComponent<TalismanCaster>();
                combat = player.GetComponent<PlayerCombat>();
                health = player.GetComponent<LiminalPlayerHealth>() ?? player.gameObject.AddComponent<LiminalPlayerHealth>();
                health.Died += OnPlayerDied;
            }
            if (motor) { originalMoveSpeed = motor.moveSpeed; originalWalkSpeed = motor.walkSpeed; }
            if (caster) { originalCooldown = caster.cooldown; originalFlames = caster.flameCount; caster.holdToCast = true; caster.requireLineOfSight = true; }
            hud = gameObject.AddComponent<LiminalHud>();
            hud.Initialize(this, hudFont);
            initialized = true;
            if (startInLobby) EnterLobby();
            else StartNewRun(seed);
        }

        public void StartNewRun(int newSeed)
        {
            if (!initialized) return;
            seed = newSeed;
            StageIndex = 0;
            PendingReward = 0;
            AugmentHistory = "증강 없음";
            if (motor) { motor.moveSpeed = originalMoveSpeed; motor.walkSpeed = originalWalkSpeed; }
            if (caster) { caster.cooldown = originalCooldown; caster.flameCount = originalFlames; }
            if (health) health.ResetHealth();
            LoadStage(0);
        }

        void Update()
        {
            if (Phase == LiminalRunPhase.Lobby) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (Phase == LiminalRunPhase.Exploring) SetPhase(LiminalRunPhase.Paused);
                else if (Phase == LiminalRunPhase.Paused) Resume();
            }
            if (Phase != LiminalRunPhase.Exploring || !player || rooms.Count == 0) return;
            int next = ActiveRoomIndex + 1;
            if (next < rooms.Count && (ActiveRoomIndex < 0 || cleared[ActiveRoomIndex]) && rooms[next].Contains(player.position, 1.3f)) ActivateRoom(next);
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) TryUseExit();
            if (player.position.y < -5)
            {
                // Recovery also covers moving props in the authoring scene and leaving an accidental floor gap.
                health.TakeDamage(10);
                if (health.IsAlive) PlacePlayer(rooms[Mathf.Max(0, ActiveRoomIndex)]);
            }
        }

        void LoadStage(int index)
        {
            try
            {
                if (!player || !motor || !caster || stages == null || index < 0 || index >= stages.Length || !stages[index])
                    throw new InvalidOperationException("플레이어와 스테이지 설정을 확인해 줘.");
                StageIndex = index;
                ClearProjectiles();
                BuildRoute(index, false);
                RenderSettings.ambientLight = CurrentStage.ambientColor;
                SetPhase(LiminalRunPhase.Exploring);
                PlacePlayer(rooms[0]);
                ActivateRoom(0);
                StageChanged?.Invoke();
                hud.Notify(CurrentStage.title + "\n" + CurrentStage.subtitle, 6);
                // Never reveal a destination from selection alone or from a failed route build.
                HunterProgress.DiscoverStage(CurrentStage.stageId);
                if (ActiveMission != null) HunterProgress.DiscoverDestination(ActiveMission.destinationId);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
                SetPhase(LiminalRunPhase.InvalidConfiguration);
                if (hud) hud.ShowConfigurationError(ex.Message);
            }
        }

        /// <summary>Creates editable prefab instances in the scene without spawning enemies or starting play.</summary>
        [ContextMenu("Generate First Stage Preview")]
        public void GenerateFirstStagePreview() => GeneratePreview(0);
        public void GeneratePreview(int stageIndex = 0)
        {
            if (Application.isPlaying) return;
            if (stages == null || stageIndex < 0 || stageIndex >= stages.Length || !stages[stageIndex])
                throw new InvalidOperationException("프리뷰 스테이지가 없어.");
            BuildRoute(stageIndex, true);
        }

        void BuildRoute(int index, bool preview)
        {
            LiminalRoom[] selection = stages[index].ChooseRoute(seed, index);
            foreach (var enemy in living) if (enemy) enemy.Defeated -= EnemyDefeated;
            living.Clear();
            rooms.Clear();
            generated = transform.Find("GeneratedRoute");
            if (generated)
            {
                generated.gameObject.SetActive(false);
                generated.name = "RetiredRoute";
                if (Application.isPlaying) Destroy(generated.gameObject); else DestroyImmediate(generated.gameObject);
            }
            generated = new GameObject("GeneratedRoute").transform;
            generated.SetParent(transform, false);
            Vector3 attachPosition = transform.position;
            Quaternion attachRotation = transform.rotation;
            for (int i = 0; i < selection.Length; i++)
            {
                LiminalRoom room;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    room = ((GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(selection[i].gameObject, generated)).GetComponent<LiminalRoom>();
                else
#endif
                    room = Instantiate(selection[i], generated);
                room.name = $"{i + 1:00}_{room.roomId}_{room.displayName}";
                room.transform.rotation = attachRotation * Quaternion.Inverse(room.entry.rotation) * room.transform.rotation;
                room.transform.position += attachPosition - room.entry.position;
                attachPosition = room.exit.position;
                attachRotation = room.exit.rotation;
                rooms.Add(room);
                if (!preview)
                    foreach (var ambush in room.GetComponentsInChildren<VendingMonster>(true))
                    {
                        ambush.Initialize(health, room, index);
                        // Keep authored appliances dormant until the player enters their room.
                        ambush.enabled = false;
                    }
                // Boss rooms that do not author their own boss get the traffic light boss.
                if (!preview && room.kind == LiminalRoomKind.Boss && room.GetComponentsInChildren<TrafficLightBoss>(true).Length == 0)
                    PlaceBoss(room);
                if (!preview)
                    foreach (var signal in room.GetComponentsInChildren<TrafficLightBoss>(true))
                    {
                        signal.Initialize(health, room, index);
                        signal.enabled = false;
                    }
                bool combat = room.kind == LiminalRoomKind.Combat || room.kind == LiminalRoomKind.Boss;
                room.SetGates(false, !preview && (combat || i == selection.Length - 1));
            }
            visited = new bool[rooms.Count];
            cleared = new bool[rooms.Count];
            ActiveRoomIndex = -1;
            ClearedRoomCount = 0;
            Physics.SyncTransforms();
        }

        void PlacePlayer(LiminalRoom room)
        {
            Vector3 spawn = room.playerSpawn ? room.playerSpawn.position : room.entry.position + room.entry.forward * 3;
            motor.ResetAt(spawn + Vector3.up * .05f);
            var cameraRig = FindFirstObjectByType<IsometricFollowCamera>();
            if (cameraRig) cameraRig.Snap();
        }

        void ActivateRoom(int index)
        {
            if (visited[index]) return;
            visited[index] = true;
            ActiveRoomIndex = index;
            LiminalRoom room = rooms[index];
            bool combat = room.kind == LiminalRoomKind.Combat || room.kind == LiminalRoomKind.Boss;
            foreach (var ambush in room.GetComponentsInChildren<VendingMonster>(true))
            {
                ambush.enabled = true;
                Register(ambush.Health, combat);
            }
            // A room that authors its own boss replaces the generic silhouette boss.
            bool authoredBoss = false;
            foreach (var signal in room.GetComponentsInChildren<TrafficLightBoss>(true))
            {
                signal.enabled = true;
                Register(signal.Health, combat);
                if (combat && signal.Health.IsAlive)
                {
                    authoredBoss = true;
                }
            }
            if (combat)
            {
                room.SetGates(true, true);
                bool boss = room.kind == LiminalRoomKind.Boss;
                var gameTheme = room.GetComponent<AcRoguelike.GameTheme.GameThemeRoom>();
                if (!authoredBoss)
                {
                    if (gameTheme) SpawnGameMonsters(room, gameTheme, boss);
                    else SpawnOfficeMonsters(room, index, boss);
                }
                hud.Notify(gameTheme ? room.displayName + "\n복셀 몬스터가 나타났어. 예고선을 피하고 빈틈을 노려."
                    : boss ? "교차로의 신호등이 깨어나고 있어.\n바닥의 예고선을 보고 회피해."
                    : room.displayName + "\n사무용품들이 깨어났어. 모두 정리하면 문이 열려.", 4);
            }
            else MarkRoomCleared(index);
            RoomChanged?.Invoke();
        }

        /// <summary>
        /// The room's own copier and locker props wake up as monsters where they stand. Each spawn marker adds a
        /// copier or a locker, and CRT monitor turrets are scattered around the room: on the floor, tipped over,
        /// or on top of furniture.
        /// </summary>
        void SpawnOfficeMonsters(LiminalRoom room, int roomIndex, bool boss)
        {
            var random = new System.Random(seed * 31 + StageIndex * 977 + roomIndex * 131);
            var spawned = new List<LiminalPropMonster>();
            foreach (var slot in room.GetComponentsInChildren<Transform>(true))
            {
                if (!slot.gameObject.activeInHierarchy) continue;
                bool copier = slot.name == "MeshySlot__photocopier", locker = slot.name == "MeshySlot__lockers";
                if (!copier && !locker) continue;
                Vector3 p = slot.position;
                p.y = room.transform.position.y + .05f;
                slot.gameObject.SetActive(false);
                spawned.Add(copier ? CopierMonster.Create(p, slot.rotation, room.transform) : LockerMonster.Create(p, slot.rotation, room.transform));
            }
            int markers = room.enemySpawns == null ? 0 : room.enemySpawns.Length;
            int count = boss ? 4 : Mathf.Max(1, markers);
            for (int i = 0; i < count; i++)
            {
                Transform marker = i < markers ? room.enemySpawns[i] : null;
                Vector3 p = marker ? marker.position : room.transform.TransformPoint(room.localBounds.center + Vector3.forward * (3 + i * 2.5f));
                p.y = room.transform.position.y + .05f;
                Quaternion facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(room.entry.position - p, Vector3.up).normalized + Vector3.forward * .001f);
                bool locker = (i + roomIndex + random.Next(2)) % 2 == 1;
                spawned.Add(locker ? LockerMonster.Create(p, facing, room.transform) : CopierMonster.Create(p, facing, room.transform));
            }
            if (!boss)
            {
                int monitors = 2 + markers / 2 + (StageIndex >= 2 ? 1 : 0);
                foreach (var placement in MonitorPlacements(room, monitors, random, spawned))
                {
                    var monitor = MonitorTurret.Create(placement.position, Quaternion.Euler(0, (float)random.NextDouble() * 360, 0), room.transform);
                    spawned.Add(monitor);
                    monitor.Setup(health, room, StageIndex);
                    monitor.Place(placement.tilt);
                    Register(monitor.Health);
                }
            }
            foreach (var monster in spawned)
            {
                if (monster is MonitorTurret) continue;
                monster.Setup(health, room, StageIndex);
                Register(monster.Health);
            }
        }

        void SpawnGameMonsters(LiminalRoom room, AcRoguelike.GameTheme.GameThemeRoom theme, bool boss)
        {
            int count = Mathf.Max(1, room.enemySpawns == null ? 0 : room.enemySpawns.Length);
            for (int i = 0; i < count; i++)
            {
                Transform marker = room.enemySpawns != null && i < room.enemySpawns.Length ? room.enemySpawns[i] : null;
                Vector3 position = marker ? marker.position : room.transform.TransformPoint(room.localBounds.center + Vector3.forward * (i * 2));
                position.y = room.transform.position.y + .05f;
                Vector3 look = (room.entry ? room.entry.position : room.transform.position) - position;
                look.y = 0;
                Quaternion facing = Quaternion.LookRotation(look.sqrMagnitude > .01f ? look : room.transform.forward);
                var monster = AcRoguelike.GameTheme.GameVoxelMonster.Create(theme.RoleAt(i, boss), position, facing, room.transform, boss && i == 0);
                if (!monster) continue;
                monster.Setup(health, room, StageIndex);
                Register(monster.Health);
            }
        }

        void Register(TrainingEnemy enemy, bool countsForRoomClear = true)
        {
            if (!enemy || !enemy.IsAlive) return;
            if (ActiveMission != null)
            {
                var modifier = enemy.GetComponent<GateMissionEnemyModifier>() ?? enemy.gameObject.AddComponent<GateMissionEnemyModifier>();
                modifier.Apply(enemy, ActiveMission, health, this);
            }
            if (!countsForRoomClear || !living.Add(enemy)) return;
            enemy.Defeated += EnemyDefeated;
        }

        struct MonitorPlacement { public Vector3 position; public Quaternion tilt; }

        /// <summary>
        /// Scattered, not arranged: random open spots in the room (away from the entry). A spot on top of low
        /// furniture is used as is. Each monitor gets a careless tilt: slightly crooked, tipped back or forward,
        /// or lying on its side.
        /// </summary>
        List<MonitorPlacement> MonitorPlacements(LiminalRoom room, int count, System.Random random, List<LiminalPropMonster> others)
        {
            var result = new List<MonitorPlacement>();
            float floor = room.transform.position.y;
            var b = room.localBounds;
            for (int attempt = 0; attempt < count * 30 && result.Count < count; attempt++)
            {
                var local = new Vector3(Mathf.Lerp(b.min.x + 2.5f, b.max.x - 2.5f, (float)random.NextDouble()), 0,
                                        Mathf.Lerp(b.min.z + 4f, b.max.z - 2.5f, (float)random.NextDouble()));
                Vector3 world = room.transform.TransformPoint(local);
                if (Vector3.Distance(world, room.entry.position) < 6f) continue;
                if (room.playerSpawn && Vector3.Distance(world, room.playerSpawn.position) < 5f) continue;
                if (!Physics.Raycast(new Vector3(world.x, floor + 3f, world.z), Vector3.down, out var hit, 3.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.point.y - floor > 1.35f || hit.collider.GetComponentInParent<LiminalPropMonster>()) continue;
                Vector3 spot = hit.point + Vector3.up * .02f;
                if (Physics.CheckSphere(spot + Vector3.up * .42f, .3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                bool crowded = false;
                foreach (var m in others) if (m && Vector3.Distance(m.transform.position, spot) < 2.4f) { crowded = true; break; }
                foreach (var placed in result) if (Vector3.Distance(placed.position, spot) < 3f) { crowded = true; break; }
                if (crowded) continue;
                double roll = random.NextDouble();
                Quaternion tilt = roll < .45 ? Quaternion.Euler(Range(random, -7, 7), 0, Range(random, -9, 9))
                    : roll < .75 ? Quaternion.Euler(Range(random, -24, -12) * (random.Next(2) == 0 ? 1 : -1), 0, Range(random, -6, 6))
                    : Quaternion.Euler(0, 0, random.Next(2) == 0 ? 88 : -88);
                result.Add(new MonitorPlacement { position = spot, tilt = tilt });
            }
            return result;
        }

        static float Range(System.Random random, float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());

        void PlaceBoss(LiminalRoom room)
        {
            var library = LiminalMonsterLibrary.Instance;
            if (!library || !library.trafficLightBoss) return;
            Transform marker = room.enemySpawns != null && room.enemySpawns.Length > 0 ? room.enemySpawns[0] : null;
            Vector3 p = marker ? marker.position : room.transform.TransformPoint(room.localBounds.center);
            p.y = room.transform.position.y;
            Vector3 toEntry = Vector3.ProjectOnPlane(room.entry.position - p, Vector3.up);
            var boss = Instantiate(library.trafficLightBoss, p, toEntry.sqrMagnitude > .01f ? Quaternion.LookRotation(toEntry) : room.transform.rotation, room.transform);
            boss.name = "TrafficLightBoss";
        }

        void EnemyDefeated(TrainingEnemy enemy)
        {
            enemy.Defeated -= EnemyDefeated;
            living.Remove(enemy);
            if (living.Count == 0 && ActiveRoomIndex >= 0)
            {
                MarkRoomCleared(ActiveRoomIndex);
                hud.Notify(rooms[ActiveRoomIndex].kind == LiminalRoomKind.Boss
                    ? "마지막 문이 응답했어.\n출구로 이동해 E를 눌러 줘." : "공간이 조용해졌어.\n열린 문으로 계속 이동해.", 3.5f);
            }
        }

        void MarkRoomCleared(int index)
        {
            if (cleared[index]) return;
            cleared[index] = true;
            ClearedRoomCount++;
            var kind = rooms[index].kind;
            PendingReward += kind == LiminalRoomKind.Boss ? 150 : kind == LiminalRoomKind.Combat ? 15 : 5;
            rooms[index].SetGates(false, index == rooms.Count - 1);
        }

        bool AllRoomsCleared()
        {
            if (cleared.Length == 0) return false;
            foreach (bool done in cleared) if (!done) return false;
            return true;
        }

        bool NearExit()
        {
            if (!player || rooms.Count == 0) return false;
            Vector3 delta = player.position - rooms[rooms.Count - 1].exit.position;
            delta.y = 0;
            return delta.sqrMagnitude <= 25;
        }

        public bool TryUseExit()
        {
            if (!ExitAvailable) return false;
            PendingReward += 40;
            SetPhase(StageIndex >= stages.Length - 1 ? LiminalRunPhase.Victory : LiminalRunPhase.AugmentChoice);
            return true;
        }

        public void SelectAugment(int option)
        {
            if (Phase != LiminalRunPhase.AugmentChoice || option < 0 || option > 2) return;
            string title;
            if (option == 0) { caster.cooldown *= .84f; title = "잔향 · 시전 간격 -16%"; }
            else if (option == 1) { caster.flameCount += 2; title = "도깨비불 · 불꽃 +2"; }
            else { health.IncreaseMaximum(25); title = "굳은 매듭 · 최대 체력 +25"; }
            AugmentHistory = AugmentHistory == "증강 없음" ? title : AugmentHistory + "\n" + title;
            SetPhase(LiminalRunPhase.NextStageChoice);
        }

        public void ContinueToNextStage()
        {
            if (Phase != LiminalRunPhase.NextStageChoice) return;
            LoadStage(StageIndex + 1);
        }

        public void Resume() { if (Phase == LiminalRunPhase.Paused) SetPhase(LiminalRunPhase.Exploring); }
        void OnPlayerDied() => SetPhase(LiminalRunPhase.Defeat);

        void SetPhase(LiminalRunPhase phase)
        {
            Phase = phase;
            bool exploring = phase == LiminalRunPhase.Exploring, inLobby = phase == LiminalRunPhase.Lobby;
            Time.timeScale = exploring || inLobby ? 1 : 0;
            if (motor) motor.enabled = exploring || inLobby;
            if (caster) caster.enabled = exploring;
            // No sword swings in the lobby: everyday clothes, everyday walk.
            if (combat) { if (!exploring) combat.CancelAttack(); combat.enabled = exploring; }
            if (hud) hud.RefreshPhase();
        }

        // ---- lobby ---------------------------------------------------------------------------------------
        /// <summary>
        /// The hunter lobby: earnings are paid out, the dungeon route is cleared, and the player walks the
        /// association plaza in everyday clothes until they use the gate.
        /// </summary>
        public void EnterLobby()
        {
            if (!initialized) return;
            if (PendingReward > 0) HunterProgress.Earn(PendingReward);
            PendingReward = 0;
            ClearRoute();
            ActiveMission = null;
            stages = originalStages == null ? Array.Empty<LiminalStageDefinition>() : (LiminalStageDefinition[])originalStages.Clone();
            if (health) health.ResetHealth();
            if (!lobby) lobby = LiminalLobby.Build(this, transform.position + LobbyOffset, hud);
            SetPhase(LiminalRunPhase.Lobby);
            lobby.Enter(player);
            RoomChanged?.Invoke();
        }

        public void ReturnToLobby() => EnterLobby();

        /// <summary>Leaves the lobby through the gate: combat outfit, permanent upgrades, a fresh run.</summary>
        public void EnterDungeon()
        {
            if (!initialized || Phase != LiminalRunPhase.Lobby) return;
            // Legacy callers still enter the scene's original route even if the mission catalog is absent.
            ActiveMission = null;
            BeginDungeonRun();
        }

        /// <summary>Starts the selected contract using the same room generator as the original campaign.</summary>
        public bool EnterMission(string missionId)
        {
            LastMissionError = null;
            if (!initialized || Phase != LiminalRunPhase.Lobby) return false;
            GateMissionDefinition selected = null;
            foreach (var mission in AvailableMissions)
                if (mission != null && mission.id == missionId) { selected = mission; break; }
            if (selected == null || !selected.IsAvailable)
            {
                LastMissionError = "미션의 이동 경로를 불러올 수 없어.";
                return false;
            }
            // Validate every stage before leaving the lobby. A missing room must not consume discovery.
            try
            {
                for (int i = 0; i < selected.stages.Length; i++) selected.stages[i].ChooseRoute(seed, i);
            }
            catch (Exception ex)
            {
                LastMissionError = "미션 경로 설정을 확인해 줘. " + ex.Message;
                Debug.LogWarning(LastMissionError, this);
                return false;
            }
            ActiveMission = selected;
            stages = (LiminalStageDefinition[])selected.stages.Clone();
            BeginDungeonRun();
            return Phase == LiminalRunPhase.Exploring;
        }

        /// <summary>Retries the active contract instead of silently reverting to the original campaign.</summary>
        public void RetryMission()
        {
            string missionId = ActiveMission?.id;
            EnterLobby();
            if (string.IsNullOrEmpty(missionId)) EnterDungeon();
            else EnterMission(missionId);
        }

        void BeginDungeonRun()
        {
            if (lobby) lobby.Leave(player);
            if (player) HunterProgress.Apply(player.gameObject);
            int runSeed = runsStarted++ == 0 ? seed : unchecked(seed + 104729 * runsStarted);
            StartNewRun(runSeed);
        }

        void ClearRoute()
        {
            foreach (var enemy in living) if (enemy) enemy.Defeated -= EnemyDefeated;
            living.Clear();
            rooms.Clear();
            visited = new bool[0];
            cleared = new bool[0];
            ActiveRoomIndex = -1;
            ClearedRoomCount = 0;
            ClearProjectiles();
            var route = generated ? generated : transform.Find("GeneratedRoute");
            if (route)
            {
                route.gameObject.SetActive(false);
                route.name = "RetiredRoute";
                Destroy(route.gameObject);
            }
            generated = null;
        }

        void ClearProjectiles()
        {
            foreach (var p in FindObjectsByType<PaperProjectile>(FindObjectsSortMode.None)) Destroy(p.gameObject);
            foreach (var p in FindObjectsByType<BinaryProjectile>(FindObjectsSortMode.None)) Destroy(p.gameObject);
            foreach (var p in FindObjectsByType<VendingCanProjectile>(FindObjectsSortMode.None)) Destroy(p.gameObject);
            foreach (var p in FindObjectsByType<TrafficLightCarProjectile>(FindObjectsSortMode.None)) Destroy(p.gameObject);
            foreach (var p in FindObjectsByType<TalismanProjectile>(FindObjectsSortMode.None)) Destroy(p.gameObject);
            foreach (var p in FindObjectsByType<SpiritFlame>(FindObjectsSortMode.None)) Destroy(p.gameObject);
        }

        void OnDestroy()
        {
            if (!Application.isPlaying) return;
            Time.timeScale = 1;
            if (health) health.Died -= OnPlayerDied;
            foreach (var enemy in living) if (enemy) enemy.Defeated -= EnemyDefeated;
        }
    }
}
