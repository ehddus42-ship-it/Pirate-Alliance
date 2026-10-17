using System;
using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    [CreateAssetMenu(menuName = "Liminal/Stage Definition", fileName = "Stage")]
    public sealed class LiminalStageDefinition : ScriptableObject
    {
        public string stageId;
        public string title;
        [TextArea] public string subtitle;
        [Tooltip("원본 도착 방. 제작용으로 보존하며 플레이 경로에는 포함하지 않습니다.")]
        public LiminalRoom startRoom;
        public LiminalRoom endRoom;
        public LiminalRoom[] roomPool = new LiminalRoom[0];
        [Min(0)] public int middleRoomCount = 3;
        public bool isBossStage;
        public Color ambientColor = new Color(.42f, .46f, .48f);

        /// <summary>Shuffle bags provide seeded variety without repeats until the pool is exhausted.</summary>
        public LiminalRoom[] ChooseRoute(int runSeed, int stageIndex)
        {
            if (!endRoom) throw new InvalidOperationException($"{name}: 종료 방이 필요해.");
            var route = new List<LiminalRoom>();
            var pool = new List<LiminalRoom>();
            if (roomPool != null)
                foreach (var room in roomPool)
                    if (room && room != startRoom && room != endRoom && room.kind != LiminalRoomKind.Arrival
                        && room.kind != LiminalRoomKind.Boss && !pool.Contains(room)) pool.Add(room);
            if (middleRoomCount > 0 && pool.Count == 0)
                throw new InvalidOperationException($"{name}: 중간 방 후보가 없어.");
            var random = new System.Random(unchecked(runSeed * 397 ^ stageIndex * 7919));
            int remaining = Mathf.Max(0, middleRoomCount);
            while (remaining > 0)
            {
                var bag = new List<LiminalRoom>(pool);
                for (int i = bag.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    (bag[i], bag[j]) = (bag[j], bag[i]);
                }
                // Depart directly into combat; exploration variations can still appear later in the run.
                if (route.Count == 0 && bag[0].kind != LiminalRoomKind.Combat)
                {
                    int firstCombat = bag.FindIndex(room => room.kind == LiminalRoomKind.Combat);
                    if (firstCombat > 0) (bag[0], bag[firstCombat]) = (bag[firstCombat], bag[0]);
                }
                if (route.Count > 0 && bag.Count > 1 && route[route.Count - 1] == bag[0])
                    (bag[0], bag[1]) = (bag[1], bag[0]);
                foreach (var room in bag)
                {
                    route.Add(room);
                    if (--remaining == 0) break;
                }
            }
            route.Add(endRoom);
            foreach (var room in route)
                if (!room.entry || !room.exit)
                    throw new InvalidOperationException($"{room.name}: Entry/Exit 소켓이 필요해.");
            return route.ToArray();
        }
    }
}
