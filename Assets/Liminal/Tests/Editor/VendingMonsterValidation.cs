using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using AcRoguelike.Liminal.Editor;
using Object=UnityEngine.Object;

namespace AcRoguelike.Liminal.EditorTests
{
    [InitializeOnLoad]
    public static class VendingMonsterValidation
    {
        const string Active="VendingMonster.Validation.Active",ReportPath="Documentation/Liminal/Concepts/VendingMachine/validation.json";
        [Serializable] public class Report
        {
            public string status="running",utc;
            public int limbTriangles,bodyTriangles,animationClips,bones,charges,cansGripped,cansThrown;
            public List<string> checks=new List<string>(),errors=new List<string>();
        }
        static Report report;
        static VendingMonster monster;static LiminalPlayerHealth player;static GameObject wall;
        static int phase,healthBefore;static double phaseStart,totalStart;static float frozenTime;static Vector3 frozenPosition;
        static VendingCanProjectile wallTestCan;
        static bool armStageChecked;
        static VendingMonsterValidation(){EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Changed;}
        static void Changed(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Active,false))
            {report=JsonUtility.FromJson<Report>(SessionState.GetString(Active+".report","{}"));phase=0;phaseStart=totalStart=EditorApplication.timeSinceStartup;}
            if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool(Active,false))Finish(false,"Play mode ended early.");
        }
        public static string Start()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit play mode first.");
            report=new Report{utc=DateTime.UtcNow.ToString("O")};
            try{Structure();}catch(Exception ex){report.status="failed";report.errors.Add(ex.ToString());Save();return JsonUtility.ToJson(report,true);}
            EditorSceneManager.OpenScene(VendingMonsterBuilder.DemoPath);
            SessionState.SetString(Active+".report",JsonUtility.ToJson(report));SessionState.SetBool(Active,true);EditorApplication.isPlaying=true;
            return "Vending monster integration validation running.";
        }
        static void Check(bool result,string description)
        {if(!result)throw new Exception(description);if(!report.checks.Contains(description))report.checks.Add(description);}
        static void Structure()
        {
            var p=AssetDatabase.LoadAssetAtPath<GameObject>(VendingMonsterBuilder.PrefabPath);Check(p,"Reusable monster prefab exists.");
            var m=p.GetComponent<VendingMonster>();Check(m&&m.canPrefab&&m.rig&&m.animator,"Monster, rig, Animator and can references are assigned.");
            var skins=p.GetComponentsInChildren<SkinnedMeshRenderer>(true);Check(skins.Length==4,"Exactly four independently skinned limb meshes.");
            report.limbTriangles=skins.Sum(s=>s.sharedMesh.triangles.Length/3);Check(report.limbTriangles<=18000,"Limb triangle budget <= 18,000.");
            report.bodyTriangles=p.GetComponentsInChildren<MeshFilter>(true).Sum(f=>f.sharedMesh?f.sharedMesh.triangles.Length/3:0);
            Check(report.bodyTriangles<=20000,"Cabinet triangle budget <= 20,000.");
            report.bones=skins.SelectMany(s=>s.bones).Distinct().Count();Check(report.bones==46,"46 reusable skeleton joints, including 30 finger joints.");
            foreach(var skin in skins)
            {
                Check(skin.sharedMesh.boneWeights.All(w=>Mathf.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1)<.001f),"Normalized skin weights (maximum four influences).");
                var texture=skin.sharedMaterial.GetTexture("normalTexture");Check(texture&&texture.width==2048&&texture.height==2048,"High-to-low baked 2K normal maps are bound to game materials.");
            }
            var clips=m.animator.runtimeAnimatorController.animationClips.Distinct().ToArray();report.animationClips=clips.Length;
            Check(clips.Length==9,"Nine editable motion clips are connected.");
            var throwing=clips.Single(c=>c.name=="CanThrow");var events=AnimationUtility.GetAnimationEvents(throwing);
            Check(events.Any(e=>e.functionName=="GrabCan")&&events.Any(e=>e.functionName=="ReleaseCan"),"Can grab and release are animation events.");
            Check(!m.limbRenderers.Any(r=>r.enabled),"Prefab starts with all limbs hidden inside dormant appliance.");
        }
        static void Go(int next){phase=next;phaseStart=EditorApplication.timeSinceStartup;}
        static void Place(Vector3 position)
        {
            var cc=player.GetComponent<CharacterController>();bool was=cc&&cc.enabled;if(cc)cc.enabled=false;
            player.transform.position=position;if(cc)cc.enabled=was;Physics.SyncTransforms();
        }
        static void Tick()
        {
            if(!SessionState.GetBool(Active,false)||!EditorApplication.isPlaying||report==null)return;
            if(EditorApplication.timeSinceStartup-totalStart>80){Finish(false,"Integration validation exceeded 80 seconds at phase "+phase);return;}
            try
            {
                double elapsed=EditorApplication.timeSinceStartup-phaseStart;
                switch(phase)
                {
                    case 0:
                        monster=Object.FindFirstObjectByType<VendingMonster>();player=Object.FindFirstObjectByType<LiminalPlayerHealth>();
                        if(!monster||!player||!monster.Health)break;
                        var motor=player.GetComponent<PlayerMotor>();if(motor)motor.enabled=false;
                        var caster=player.GetComponent<TalismanCaster>();if(caster)caster.enabled=false;
                        var combat=player.GetComponent<PlayerCombat>();if(combat)combat.enabled=false;
                        player.IncreaseMaximum(1000);Place(new Vector3(0,.04f,-6));Go(1);break;
                    case 1:
                        if(elapsed<.6)break;
                        Check(monster.State==VendingMonsterState.Dormant&&!monster.Health.CanBeTargeted,"Outside detection radius: dormant and excluded from auto-targeting.");
                        wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="ValidationWall";wall.transform.position=new Vector3(0,1.7f,1);wall.transform.localScale=new Vector3(5,3.4f,.3f);
                        Place(new Vector3(0,.04f,-1));Go(2);break;
                    case 2:
                        if(elapsed<.6)break;
                        Check(monster.State==VendingMonsterState.Dormant,"Nearby player behind wall does not trigger awakening.");
                        Object.Destroy(wall);Go(3);break;
                    case 3:
                        if(monster.State!=VendingMonsterState.Awakening)break;
                        Check(monster.Health.CanBeTargeted,"Visible nearby player triggers awakening and targetability.");Go(4);break;
                    case 4:
                        if(monster.State==VendingMonsterState.Awakening&&monster.StateTime>1.05f&&monster.StateTime<1.3f)
                        {
                            Check(monster.rig.rightArm.upper.localScale.x>.55f&&monster.rig.rightLeg.upper.localScale.x<.1f,"Arms emerge before the legs.");armStageChecked=true;
                        }
                        if(monster.State==VendingMonsterState.Awakening)break;
                        Check(armStageChecked,"Staged emergence observed during real Animator playback.");
                        Check(monster.GetComponent<CharacterController>().height>3,"Collision body expands with the raised cabinet.");
                        healthBefore=player.Health;Go(5);break;
                    case 5:
                        if(monster.State==VendingMonsterState.ChargeWindup)Check(monster.warning.enabled,"Charge has visible ground telegraph and anticipation.");
                        if(monster.State!=VendingMonsterState.ChargeRecover)break;
                        Check(monster.ChargeCount==1,"Charge executes after the anticipation.");
                        Check(player.Health<healthBefore,"Charging body deals damage to the player.");
                        Place(monster.transform.position+monster.transform.forward*5);healthBefore=player.Health;Go(6);break;
                    case 6:
                        if(monster.CansGripped<1)break;
                        Check(monster.canGrip.GetComponentsInChildren<VendingCanProjectile>().Length==1,"Can is parented to the gripping hand during extraction.");
                        Check(!monster.warning.enabled,"Can windup does not reveal the projectile trajectory.");
                        Go(7);break;
                    case 7:
                        if(monster.CansThrown<1)break;
                        var projectile=Object.FindObjectsByType<VendingCanProjectile>(FindObjectsSortMode.None).FirstOrDefault(p=>p.transform.parent==null);
                        Check(projectile,"Can detaches into an independent ballistic projectile.");
                        frozenTime=monster.StateTime;frozenPosition=projectile.transform.position;Time.timeScale=0;Go(8);break;
                    case 8:
                        if(elapsed<.35)break;
                        Check(Mathf.Abs(monster.StateTime-frozenTime)<.001f,"Pause freezes monster animation and attack timing.");
                        var pausedCan=Object.FindObjectsByType<VendingCanProjectile>(FindObjectsSortMode.None).FirstOrDefault(p=>p.transform.parent==null);
                        Check(pausedCan&&Vector3.Distance(pausedCan.transform.position,frozenPosition)<.001f,"Pause freezes ballistic can movement.");
                        Time.timeScale=1;Go(9);break;
                    case 9:
                        if(player.Health>=healthBefore&&elapsed<ProjectileTuning.ScaleFlightDuration(2))break;
                        Check(player.Health<healthBefore,"Thrown can hits the player with swept collision.");
                        Check(monster.CansThrown==1&&monster.CansGripped==1,"Animation events and frame-step fallback do not duplicate cans.");
                        report.charges=monster.ChargeCount;report.cansGripped=monster.CansGripped;report.cansThrown=monster.CansThrown;
                        monster.enabled=false;monster.animator.enabled=false;
                        Place(new Vector3(0,.04f,5));healthBefore=player.Health;
                        wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(0,2.5f,2.5f);wall.transform.localScale=new Vector3(4,5,.2f);Physics.SyncTransforms();
                        wallTestCan=Object.Instantiate(monster.canPrefab,new Vector3(0,1,0),Quaternion.identity).GetComponent<VendingCanProjectile>();
                        wallTestCan.Launch(player.transform.position+Vector3.up*.85f,1.2f,20,monster.transform,player);
                        Go(10);break;
                    case 10:
                        if(elapsed<ProjectileTuning.ScaleFlightDuration(1.5f))break;
                        Check(!wallTestCan&&player.Health==healthBefore,"Thrown can is blocked by a solid wall without damaging the player.");
                        monster.Health.TakeDamage(monster.Health.maxHealth);Go(11);break;
                    case 11:
                        if(elapsed<.1)break;
                        Check(monster.State==VendingMonsterState.Dead&&!monster.warning.enabled,"Defeat cancels attacks and telegraphs.");
                        var death=monster.GetComponent<VendingMonsterDeath>();
                        Check(!monster.GetComponent<CharacterController>().enabled&&death&&death.Playing,"Defeat disables collision and plays the knock-back death.");
                        Go(12);break;
                    case 12:
                        var dying=monster.GetComponent<VendingMonsterDeath>();
                        if(dying&&!dying.Finished&&elapsed<5)break;
                        Check(dying&&dying.Finished&&!monster.limbRenderers.Any(r=>r.enabled)&&!monster.Health.visibleRenderers.Any(r=>r&&r.enabled),
                            "Death ends with the limbs retracted and the machine hidden.");
                        Finish(true,null);break;
                }
            }
            catch(Exception ex){Finish(false,"Phase "+phase+": "+ex.Message);}
        }
        static void Save(){Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));File.WriteAllText(ReportPath,JsonUtility.ToJson(report,true));}
        static void Finish(bool success,string error)
        {
            if(report==null)report=new Report();report.status=success?"passed":"failed";if(error!=null)report.errors.Add(error);
            Save();SessionState.SetBool(Active,false);Time.timeScale=1;Debug.Log("VENDING_MONSTER_VALIDATION "+report.status+(error!=null?": "+error:""));
            if(EditorApplication.isPlaying)EditorApplication.isPlaying=false;
        }
    }
}
