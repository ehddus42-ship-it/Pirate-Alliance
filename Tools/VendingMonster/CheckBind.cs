using UnityEngine;using UnityEditor;using System.Linq;
public static class CheckBind
{
 public static string Main()
 {
  var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Liminal/Art/VendingMonster/vending_monster_rig.glb");var o=Object.Instantiate(source);string text="";
  foreach(var skin in o.GetComponentsInChildren<SkinnedMeshRenderer>())
  {
   var mesh=new Mesh();skin.BakeMesh(mesh);text+=skin.name+" rest="+mesh.bounds+" original="+skin.sharedMesh.bounds+"\n";
   for(int i=0;i<skin.bones.Length;i++)
   {
    var m=skin.transform.worldToLocalMatrix*skin.bones[i].localToWorldMatrix*skin.sharedMesh.bindposes[i];float max=0;
    for(int j=0;j<16;j++)max=Mathf.Max(max,Mathf.Abs(m[j]-Matrix4x4.identity[j]));
    if(max>.001f)text+=skin.bones[i].name+" bind error="+max+"\n";
   }
   Object.DestroyImmediate(mesh);
  }
  Object.DestroyImmediate(o);return text;
 }
}
