using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.Liminal.Editor
{
    /// <summary>One four-room run, drawing from the original room variations and ending at the authored boss.</summary>
    public static class LiminalSessionBuilder
    {
        public const string SessionStagePath = LiminalMapBuilder.Root + "/Stages/Stage_Liminal.asset";

        public static LiminalStageDefinition EnsureSessionStage()
        {
            var originals = Enumerable.Range(1, 4).Select(i => AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(
                LiminalMapBuilder.Root + "/Stages/Stage_" + i.ToString("00") + ".asset")).ToArray();
            if (originals.Any(s => !s) || !originals[0].startRoom || !originals[3].endRoom)
                throw new InvalidOperationException("리미널 원본 방 구성을 먼저 생성해 줘.");
            var stage = AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(SessionStagePath);
            if (!stage)
            {
                stage = ScriptableObject.CreateInstance<LiminalStageDefinition>();
                AssetDatabase.CreateAsset(stage, SessionStagePath);
            }
            var pool = new List<LiminalRoom>();
            foreach (var room in originals.Take(3).SelectMany(s => s.roomPool ?? Array.Empty<LiminalRoom>())
                .Concat(stage.roomPool ?? Array.Empty<LiminalRoom>()))
                if (room && room.kind != LiminalRoomKind.Boss && room != originals[0].startRoom
                    && room != originals[3].endRoom && !pool.Contains(room)) pool.Add(room);
            stage.stageId = originals[0].stageId;
            stage.title = "경계 공간";
            stage.subtitle = "매번 달라지는 세 공간을 지나 마지막 출발 대합실로";
            stage.startRoom = originals[0].startRoom;
            stage.endRoom = originals[3].endRoom;
            stage.roomPool = pool.ToArray();
            stage.middleRoomCount = 3;
            stage.isBossStage = true;
            stage.ambientColor = originals[0].ambientColor;
            EditorUtility.SetDirty(stage);
            return stage;
        }

        public static void UpdateCatalog(LiminalStageDefinition stage)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GateMissionCatalog>("Assets/Liminal/Resources/GateMissions.asset");
            if (!catalog) return;
            var mission = catalog.missions.FirstOrDefault(m => m != null && m.id == "liminal_campaign");
            if (mission == null) return;
            mission.title = "경계 소탕";
            mission.briefing = "매번 달라지는 세 공간을 돌파해. 마지막 출발 대합실의 보스를 쓰러뜨리고 귀환해.";
            mission.gimmickDescription = "기존 공간 중 세 곳이 중복 없이 나타나. 마지막 보스 방은 항상 같아.";
            mission.objective = "세 구간 돌파 · 최종 보스 격파 · 출구 도달";
            mission.stages = new[] { stage };
            EditorUtility.SetDirty(catalog);
        }

        // Narrow migration entry point; leaves the room prefabs and original category assets intact.
        [MenuItem("AC Roguelike/Liminal/Apply Four-Room Session")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            var scene = SceneManager.GetSceneByPath(LiminalMapBuilder.RunPath);
            bool alreadyLoaded = scene.IsValid() && scene.isLoaded;
            if (alreadyLoaded && scene.isDirty) throw new InvalidOperationException("Save the run scene before updating its route.");
            var stage = EnsureSessionStage();
            UpdateCatalog(stage);
            AssetDatabase.SaveAssets();
            if (!alreadyLoaded) scene = EditorSceneManager.OpenScene(LiminalMapBuilder.RunPath, OpenSceneMode.Additive);
            try
            {
                var director = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LiminalRunDirector>(true)).Single();
                director.stages = new[] { stage };
                director.GeneratePreview();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the liminal run route.");
            }
            finally { if (!alreadyLoaded) EditorSceneManager.CloseScene(scene, true); }
            Debug.Log("LIMINAL_SESSION_UPDATED: 1 stage, 4 rooms, " + stage.roomPool.Length + " random candidates, fixed boss.");
        }
    }
}
