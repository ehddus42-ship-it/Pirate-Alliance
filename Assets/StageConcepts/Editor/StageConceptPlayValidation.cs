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
                        Require(run.Rooms.Count==5 && run.stages.Length==1,"Expected five rooms and one theme per scene.");
                        Require(run.CurrentStage.stageId=="concept_"+StageConceptBuilder.Keys[theme].ToLowerInvariant(),"Wrong scene theme loaded.");
                        Require(run.ActiveRoomIndex==0 && run.ClearedRoomCount==1,"Arrival room did not activate and clear.");
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
                        wantedRoom=1; EnterRoom(); break;
                    case "AwaitRoom":
                        if(run.ActiveRoomIndex!=wantedRoom) return;
                        var room=run.Rooms[wantedRoom];
                        if(room.kind==LiminalRoomKind.Combat)
                        {
                            Require(run.LivingEnemyCount==3,"Combat room must spawn three enemies.");
                            Require(room.entranceGate.activeSelf && room.exitGate.activeSelf,"Combat gates did not lock.");
                            Require(!run.TryUseExit(),"Uncleared room allowed stage completion.");
                            foreach(var enemy in room.GetComponentsInChildren<TrainingEnemy>()) enemy.TakeDamage(enemy.maxHealth+1);
                            report.combatRoomsCleared++;
                        }
                        Next("CheckClear"); break;
                    case "CheckClear":
                        Require(run.LivingEnemyCount==0,"Defeated enemies remain in room counter.");
                        Require(!run.Rooms[wantedRoom].entranceGate.activeSelf,"Entrance gate remains locked.");
                        if(wantedRoom<4)
                        {
                            Require(!run.Rooms[wantedRoom].exitGate.activeSelf,"Exit gate remains locked.");
                            wantedRoom++; EnterRoom();
                        }
                        else
                        {
                            Require(run.ClearedRoomCount==5,"Not all five rooms cleared.");
                            motor.ResetAt(run.Rooms[4].exit.position-Vector3.forward*2.5f+Vector3.up*.05f);
                            Next("Exit");
                        }
                        break;
                    case "Exit":
                        Require(run.ExitAvailable && run.TryUseExit(),"Cleared stage exit unavailable.");
                        Require(run.Phase==LiminalRunPhase.Victory,"Standalone theme must end in Victory.");
                        report.completedThemes++; report.checks.Add(StageConceptBuilder.Keys[theme]+": scene loads, actual movement works, three combat rooms spawn enemies and unlock, five-room route ends in Victory.");
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
