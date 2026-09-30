using UnityEngine;
using UnityEditor;
using System.Linq;
using AcRoguelike.Liminal;
public static class InspectVendingRig
{
 public static string Main()
 {
  var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Liminal/Prefabs/Enemies/VendingMachineMonster.prefab");
  var go=Object.Instantiate(prefab);var rig=go.GetComponentInChildren<VendingMonsterRig>();var ani=go.GetComponentInChildren<Animator>();ani.enabled=false;
  rig.RestoreRestPose();string s="REST\n";foreach(var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>()){var m=new Mesh();skin.BakeMesh(m);s+=skin.name+" "+m.bounds+"\n";Object.DestroyImmediate(m);}
  rig.Sample(VendingMonsterState.Idle,.3f);
  s+="cabinet="+rig.cabinet.position+"\n";
  foreach(var l in new[]{rig.leftArm,rig.rightArm,rig.leftLeg,rig.rightLeg})s+=l.upper.name+" "+l.upper.position+" => "+l.lower.position+" => "+l.end.position+" scale "+l.upper.lossyScale+"\n";
  foreach(var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>())
  {
   var m=new Mesh();skin.BakeMesh(m);s+=skin.name+" renderer="+skin.transform.position+" baked="+m.bounds+" mesh="+skin.sharedMesh.bounds+" root="+skin.rootBone.name+" rootpos="+skin.rootBone.position+"\n";
   s+=string.Join(",",skin.bones.Take(6).Select(t=>t.name+" "+t.position));s+="\n";Object.DestroyImmediate(m);
  }
  Object.DestroyImmediate(go);return s;
 }
}
