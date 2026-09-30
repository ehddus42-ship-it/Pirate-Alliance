using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AcRoguelike.Liminal
{
    public enum LiminalRunPhase { Exploring, AugmentChoice, NextStageChoice, Victory, Defeat, Paused, InvalidConfiguration }

    [DisallowMultipleComponent]
    public sealed class LiminalRunDirector : MonoBehaviour
    {
        public LiminalStageDefinition[] stages = new LiminalStageDefinition[0];
        public Transform player;
        public GameObject enemyPrefab;
        public Font hudFont;
        public int seed = 73029;
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
        float originalMoveSpeed, originalWalkSpeed, originalCooldown;
        int originalFlames;
        bool initialized;

        void Start()
        {
            if (!player)
            {
                var found = FindFirstObjectByType<PlayerMotor>();
                if (found) player = found.transform;
            }
            if (player)
            {
                motor = player.GetComponent<PlayerMotor>();
                caster = player.GetComponent<TalismanCaster>();
                health = player.GetComponent<LiminalPlayerHealth>() ?? player.gameObject.AddComponent<LiminalPlayerHealth>();
                health.Died += OnPlayerDied;
            }
            if (motor) { originalMoveSpeed = motor.moveSpeed; originalWalkSpeed = motor.walkSpeed; }
            if (caster) { originalCooldown = caster.cooldown; originalFlames = caster.flameCount; caster.holdToCast = true; caster.requireLineOfSight = true; }
            hud = gameObject.AddComponent<LiminalHud>();
            hud.Initialize(this, hudFont);
            initialized = true;
            StartNewRun(seed);
        }

        public void StartNewRun(int newSeed)
        {
            if (!initialized) return;
            seed = newSeed;
            StageIndex = 0;
            AugmentHistory = "증강 없음";
            if (motor) { motor.moveSpeed = originalMoveSpeed; motor.walkSpeed = originalWalkSpeed; }
            if (caster) { caster.cooldown = originalCooldown; caster.flameCount = originalFlames; }
            if (health) health.ResetHealth();
            LoadStage(0);
        }

        void Update()
        {
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
                if (!player || !motor || !caster || stages == null || index >= stages.Length || !stages[index])
                    throw new InvalidOperationException("플레이어와 스테이지 설정을 확인해 줘.");
                StageIndex = index;
                foreach (var projectile in FindObjectsByType<TalismanProjectile>(FindObjectsSortMode.None)) Destroy(projectile.gameObject);
                foreach (var flame in FindObjectsByType<SpiritFlame>(FindObjectsSortMode.None)) Destroy(flame.gameObject);
                foreach (var can in FindObjectsByType<VendingCanProjectile>(FindObjectsSortMode.None)) Destroy(can.gameObject);
                BuildRoute(index, false);
                RenderSettings.ambientLight = CurrentStage.ambientColor;
                SetPhase(LiminalRunPhase.Exploring);
                PlacePlayer(rooms[0]);
                ActivateRoom(0);
                StageChanged?.Invoke();
                hud.Notify(CurrentStage.title + "\n" + CurrentStage.subtitle, 6);
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
                if (combat && ambush.Health.IsAlive)
                {
                    ambush.Health.Defeated += EnemyDefeated;
                    living.Add(ambush.Health);
                }
            }
            // A room that authors its own boss replaces the generic silhouette boss.
            bool authoredBoss = false;
            foreach (var signal in room.GetComponentsInChildren<TrafficLightBoss>(true))
            {
                signal.enabled = true;
                if (combat && signal.Health.IsAlive)
                {
                    signal.Health.Defeated += EnemyDefeated;
                    living.Add(signal.Health);
                    authoredBoss = true;
                }
            }
            if (combat)
            {
                room.SetGates(true, true);
                bool boss = room.kind == LiminalRoomKind.Boss;
                int count = authoredBoss ? 0 : boss ? 1 : Mathf.Max(1, room.enemySpawns == null ? 0 : room.enemySpawns.Length);
                for (int i = 0; i < count; i++)
                {
                    Transform marker = room.enemySpawns != null && i < room.enemySpawns.Length ? room.enemySpawns[i] : null;
                    Vector3 p = marker ? marker.position : room.transform.TransformPoint(room.localBounds.center + Vector3.forward * 3);
                    if (!marker) p.y = room.transform.position.y + .05f;
                    GameObject go = enemyPrefab ? Instantiate(enemyPrefab, p, room.transform.rotation, room.transform)
                        : LiminalEnemy.CreateSilhouette(p, room.transform, boss);
                    var target = go.GetComponent<TrainingEnemy>() ?? go.AddComponent<TrainingEnemy>();
                    var enemy = go.GetComponent<LiminalEnemy>() ?? go.AddComponent<LiminalEnemy>();
                    enemy.Initialize(health, room, StageIndex, boss);
                    target.Defeated += EnemyDefeated;
                    living.Add(target);
                }
                hud.Notify(boss ? "관리자가 기다리고 있어.\n바닥의 예고선을 보고 회피해." : room.displayName + "\n잔상을 정리하면 문이 열려.", 4);
            }
            else MarkRoomCleared(index);
            RoomChanged?.Invoke();
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
            bool exploring = phase == LiminalRunPhase.Exploring;
            Time.timeScale = exploring ? 1 : 0;
            if (motor) motor.enabled = exploring;
            if (caster) caster.enabled = exploring;
            if (hud) hud.RefreshPhase();
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
