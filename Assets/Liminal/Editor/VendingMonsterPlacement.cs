using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.Liminal.Editor
{
    public static class VendingMonsterPlacement
    {
        const string LegacyName = "MeshySlot__vending_machine";
        const string PlacementName = "VendingMachineAmbush";
        const string ReportPath = "Documentation/Liminal/Concepts/VendingMachine/stage-placement.json";
        [Serializable] public sealed class Placement
        {
            public string room;
            public Vector3 localPosition;
            public float propYaw, monsterYaw;
        }
        [Serializable] public sealed class Report
        {
            public string status = "passed";
            public List<Placement> replacements = new List<Placement>();
            public int authoredMonsters, runPreviewMonsters;
        }

        public static GameObject Create(Transform parent, Vector3 localPosition, Quaternion propRotation)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VendingMonsterBuilder.PrefabPath);
            if (!prefab) throw new InvalidOperationException("Build the vending monster before placing it.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = PlacementName;
            instance.transform.localPosition = localPosition;
            // The original appliance face points along -Z; the monster's combat forward is +Z.
            instance.transform.localRotation = propRotation * Quaternion.Euler(0, 180, 0);
            instance.transform.localScale = Vector3.one;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            return instance;
        }

        [MenuItem("AC Roguelike/Liminal/Vending Monster/Replace Authored Vending Machines")]
        public static string ReplaceAuthoredMachines()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before replacing appliances.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scene changes before placement.");
            var report = File.Exists(ReportPath) ? JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath)) : new Report();
            report.authoredMonsters = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { LiminalMapBuilder.RoomFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var slots = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == LegacyName).ToArray();
                    foreach (var slot in slots)
                    {
                        var position = slot.localPosition;
                        var rotation = slot.localRotation;
                        var monster = Create(slot.parent, position, rotation);
                        if (Vector3.Distance(position, monster.transform.localPosition) > .0001f)
                            throw new InvalidOperationException("Placement moved from its authored location: " + path);
                        report.replacements.Add(new Placement { room = path, localPosition = position,
                            propYaw = rotation.eulerAngles.y, monsterYaw = monster.transform.localEulerAngles.y });
                        Object.DestroyImmediate(slot.gameObject);
                    }
                    if (slots.Length > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                    report.authoredMonsters += root.GetComponentsInChildren<VendingMonster>(true).Length;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var scene = EditorSceneManager.OpenScene(LiminalMapBuilder.RunPath, OpenSceneMode.Single);
            var director = Object.FindFirstObjectByType<LiminalRunDirector>();
            if (!director) throw new InvalidOperationException("Run director is missing.");
            director.GenerateFirstStagePreview();
            report.runPreviewMonsters = scene.GetRootGameObjects().Sum(r => r.GetComponentsInChildren<VendingMonster>(true).Length);
            if (report.runPreviewMonsters < 1) throw new InvalidOperationException("Stage 1 preview has no authored ambush.");
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            return JsonUtility.ToJson(report, true);
        }
    }
}
