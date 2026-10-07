using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcRoguelike;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike.StageConcepts.Editor
{
    /// <summary>Opt-in integration run through the four real scenes and their existing gameplay APIs.</summary>
    [InitializeOnLoad]
    public static class StageConceptPlayValidation
    {
        const string ActiveKey = "StageConcepts.PlayValidation.Active";
        const string ReportKey = "StageConcepts.PlayValidation.Report";
        const string Path = "Documentation/StageConcepts/play-validation.json";
        [Serializable] public sealed class Report
        {
            public string status = "running", utc;
            public int completedThemes, combatRoomsCleared, physicalMovementChecks;
            public List<string> checks = new List<string>();
            public List<string> errors = new List<string>();
        }
        static Report report;
        static int theme, wantedRoom;
        static string state = "AwaitRun";
        static double stateStart, nextTick;
        static Vector3 movementStart;
        static LiminalRunDirector run;
        static PlayerMotor motor;

        static StageConceptPlayValidation()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
            if (SessionState.GetBool(ActiveKey,false))
            {
                report=JsonUtility.FromJson<Report>(SessionState.GetString(ReportKey,"{}"));
                theme=report.completedThemes; stateStart=EditorApplication.timeSinceStartup;
            }
            EditorApplication.playModeStateChanged += mode =>
            {
                if(mode==PlayModeStateChange.EnteredEditMode && SessionState.GetBool(ActiveKey,false)) Finish("Play mode ended before validation finished.");
            };
        }

        [MenuItem("AC Roguelike/Stage Concepts/Validate Play Through All Themes")]
        public static void StartMenu() { Debug.Log(Start()); }
        public static string Start()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop play mode first.");
            for(int i=0;i<SceneManager.sceneCount;i++) if(SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scene changes first.");
            report=new Report { utc=DateTime.UtcNow.ToString("O") }; theme=0; wantedRoom=0; run=null;
            Save(); SessionState.SetBool(ActiveKey,true); Next("AwaitRun");
            EditorSceneManager.OpenScene(StageConceptBuilder.Root+"/Scenes/StageConcept_Forest.unity");
            EditorApplication.isPlaying=true;
            return "Play validation started. Report: "+Path;
        }
        static void Tick()
        {
            if(!SessionState.GetBool(ActiveKey,false) || EditorApplication.isCompiling) return;
            double now=EditorApplication.timeSinceStartup;
            if(now<nextTick) return;
            nextTick=now+.08;
            if(now-stateStart>50) { Finish("Timed out at "+state+" for theme "+theme); return; }
            if(!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                switch(state)
                {
                    case "AwaitRun":
                        run=UnityEngine.Object.FindFirstObjectByType<LiminalRunDirector>();
                        if(!run || run.Rooms.Count==0) return;
                        Require(run.Phase==LiminalRunPhase.Exploring,"Run did not enter Exploring.");
                        Require(run.Rooms.Count==4 && run.stages.Length==1,"Expected four rooms and one theme per scene.");
                        Require(run.CurrentStage.stageId=="concept_"+StageConceptBuilder.Keys[theme].ToLowerInvariant(),"Wrong scene theme loaded.");
                        Require(run.ActiveRoomIndex==0 && run.ClearedRoomCount==0 && run.Rooms[0].kind==LiminalRoomKind.Combat && run.LivingEnemyCount>0,
                            "The first random combat room did not activate with living enemies.");
                        Require(run.Rooms[3].roomId==run.CurrentStage.endRoom.roomId && run.Rooms.All(room=>room.roomId!=run.CurrentStage.startRoom.roomId),
                            "The fixed final room changed or the removed arrival returned.");
                        run.PlayerHealth.GrantInvulnerability(60);
                        motor=run.player.GetComponent<PlayerMotor>(); Require(motor,"Player motor missing.");
                        movementStart=motor.transform.position;
                        var forward=Vector3.ProjectOnPlane(motor.viewCamera.transform.forward,Vector3.up).normalized;
                        var right=Vector3.ProjectOnPlane(motor.viewCamera.transform.right,Vector3.up).normalized;
                        motor.SetAutomationInput(new Vector2(Vector3.Dot(Vector3.forward,right),Vector3.Dot(Vector3.forward,forward)),movementStart+Vector3.forward*10);
                        Next("Walk"); break;
                    case "Walk":
                        if(motor.transform.position.z-movementStart.z<2.2f) return;
                        motor.SetAutomationInput(Vector2.zero,motor.transform.position+Vector3.forward*10);
                        Require(motor.transform.position.z-movementStart.z>2,"CharacterController failed actual forward movement.");
                        Require(Mathf.Abs(motor.transform.position.y)<.4f,"Player fell through the floor.");
                        report.physicalMovementChecks++;
                        wantedRoom=0; Next("AwaitRoom"); break;
                    case "AwaitRoom":
                        if(run.ActiveRoomIndex!=wantedRoom) return;
                        var room=run.Rooms[wantedRoom];
                        if(room.kind==LiminalRoomKind.Combat || room.kind==LiminalRoomKind.Boss)
                        {
                            Require(run.LivingEnemyCount>0,"Combat room must spawn enemies.");
                            Require(room.entranceGate.activeSelf && room.exitGate.activeSelf,"Combat gates did not lock.");
                            Require(!run.TryUseExit(),"Uncleared room allowed stage completion.");
                            foreach(var enemy in room.GetComponentsInChildren<TrainingEnemy>()) enemy.TakeDamage(enemy.maxHealth+1);
                            report.combatRoomsCleared++;
                        }
                        Next("CheckClear"); break;
                    case "CheckClear":
                        Require(run.LivingEnemyCount==0,"Defeated enemies remain in room counter.");
                        Require(!run.Rooms[wantedRoom].entranceGate.activeSelf,"Entrance gate remains locked.");
                        if(wantedRoom<run.Rooms.Count-1)
                        {
                            Require(!run.Rooms[wantedRoom].exitGate.activeSelf,"Exit gate remains locked.");
                            wantedRoom++; EnterRoom();
                        }
                        else
                        {
                            Require(run.ClearedRoomCount==4,"Not all four rooms cleared.");
                            var finalRoom=run.Rooms[run.Rooms.Count-1];
                            motor.ResetAt(finalRoom.exit.position-finalRoom.exit.forward*2.5f+Vector3.up*.05f);
                            Next("Exit");
                        }
                        break;
                    case "Exit":
                        Require(run.ExitAvailable && run.TryUseExit(),"Cleared stage exit unavailable.");
                        Require(run.Phase==LiminalRunPhase.Victory,"Standalone theme must end in Victory.");
                        report.completedThemes++; report.checks.Add(StageConceptBuilder.Keys[theme]+": scene loads directly into random combat, actual movement works, combat gates unlock, four-room route with fixed final room ends in Victory.");
                        Save();
                        if(++theme==4) Finish(null);
                        else { run=null; Time.timeScale=1; SceneManager.LoadScene("StageConcept_"+StageConceptBuilder.Keys[theme]); Next("AwaitRun"); }
                        break;
                }
            }
            catch(Exception ex) { Finish(ex.ToString()); }
        }
        static void EnterRoom() { motor.ResetAt(run.Rooms[wantedRoom].playerSpawn.position+Vector3.up*.05f); Next("AwaitRoom"); }
        static void Next(string next) { state=next; stateStart=EditorApplication.timeSinceStartup; }
        static void Require(bool ok,string message) { if(!ok) throw new InvalidOperationException(message); }
        static void OnLog(string message,string stack,LogType type)
        {
            if(SessionState.GetBool(ActiveKey,false) && (type==LogType.Exception || type==LogType.Error || type==LogType.Assert))
                report.errors.Add(message);
        }
        static void Finish(string error)
        {
            SessionState.SetBool(ActiveKey,false);
            if(error!=null) report.errors.Add(error);
            report.status=report.errors.Count==0 ? "passed" : "failed"; Save();
            if(motor) motor.ReleaseAutomation(); Time.timeScale=1;
            EditorApplication.isPlaying=false;
            Debug.Log("STAGE_CONCEPT_PLAY_VALIDATION: "+report.status+" ("+Path+")");
        }
        static void Save()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            string json=JsonUtility.ToJson(report,true); File.WriteAllText(Path,json); SessionState.SetString(ReportKey,json);
        }
    }
}
