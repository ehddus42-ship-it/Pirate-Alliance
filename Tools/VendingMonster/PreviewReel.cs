using System;using System.IO;using System.Linq;using System.Collections.Generic;
using UnityEngine;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;using UnityEngine.SceneManagement;
using UnityEditor;using UnityEditor.SceneManagement;using AcRoguelike.Liminal;using AcRoguelike.Liminal.Editor;
using Object=UnityEngine.Object;
public static class VendingPreviewReel
{
 public static string Main()
 {
  if(EditorApplication.isPlaying)throw new Exception("Stop play mode before capturing.");
  const string output="Temp/VendingMonsterFrames";Directory.CreateDirectory(output);
  bool asyncShaders=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
  var scene=EditorSceneManager.NewPreviewScene();var root=new GameObject("Reel");SceneManager.MoveGameObjectToScene(root,scene);
  var creature=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(VendingMonsterBuilder.PrefabPath),root.transform);
  var monster=creature.GetComponent<VendingMonster>();monster.animator.enabled=false;
  var camera=new GameObject("ReelCamera").AddComponent<Camera>();camera.transform.SetParent(root.transform,false);camera.scene=scene;
  camera.transform.position=new Vector3(4.6f,2.65f,7.7f);camera.transform.LookAt(new Vector3(0,1.45f,0));camera.orthographic=true;camera.orthographicSize=2.13f;
  camera.nearClipPlane=.1f;camera.farClipPlane=50;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.095f,.105f,.092f);
  Material floorMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMat.color=new Color(.34f,.35f,.29f);
  var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(root.transform,false);floor.transform.position=new Vector3(0,-.075f,0);floor.transform.localScale=new Vector3(200,.1f,200);floor.GetComponent<Renderer>().sharedMaterial=floorMat;
  void Light(string n,Vector3 angles,float intensity,Color color){var light=new GameObject(n).AddComponent<Light>();light.transform.SetParent(root.transform,false);light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(angles);light.intensity=intensity;light.color=color;light.shadows=LightShadows.Soft;}
  Light("FluorescentKey",new Vector3(45,-30,0),1.15f,new Color(.86f,.93f,.8f));Light("Fill",new Vector3(35,130,0),.55f,new Color(.71f,.83f,.85f));
  var rt=new RenderTexture(720,960,24,RenderTextureFormat.ARGB32);camera.targetTexture=rt;
  var sequence=new[]{(VendingMonsterState.Dormant,.7f),(VendingMonsterState.Awakening,3.2f),(VendingMonsterState.Idle,.65f),(VendingMonsterState.ChargeWindup,.95f),
    (VendingMonsterState.Charging,.76f),(VendingMonsterState.ChargeRecover,1.05f),(VendingMonsterState.CanThrow,2.65f),(VendingMonsterState.Idle,.65f)};
  int frame=0;var staticSkins=new List<(SkinnedMeshRenderer skin,Mesh mesh,GameObject go)>();
  foreach(var r in monster.limbRenderers){var skin=(SkinnedMeshRenderer)r;var mesh=new Mesh();var o=new GameObject("BakedPose");o.transform.SetParent(skin.transform,false);o.AddComponent<MeshFilter>().sharedMesh=mesh;o.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;staticSkins.Add((skin,mesh,o));}
  var can=Object.Instantiate(monster.canPrefab,root.transform);can.SetActive(false);
  try
  {
   foreach(var section in sequence)
   {
    monster.rig.Sample(VendingMonsterState.CanThrow,VendingMonsterRig.CanReleaseTime);Vector3 release=monster.canGrip.position;Quaternion releaseRot=monster.canGrip.rotation;
    int frames=Mathf.CeilToInt(section.Item2*24);
    for(int i=0;i<frames;i++)
    {
     float t=(float)i/24;monster.rig.Sample(section.Item1,t);
     foreach(var entry in staticSkins){entry.go.SetActive(section.Item1!=VendingMonsterState.Dormant);entry.skin.BakeMesh(entry.mesh);entry.mesh.RecalculateBounds();}
     can.SetActive(section.Item1==VendingMonsterState.CanThrow&&t>=VendingMonsterRig.CanGrabTime&&t<VendingMonsterRig.CanReleaseTime+.72f);
     if(can.activeSelf)
     {
      if(t<VendingMonsterRig.CanReleaseTime)can.transform.SetPositionAndRotation(monster.canGrip.position,monster.canGrip.rotation);
      else {float dt=t-VendingMonsterRig.CanReleaseTime;Vector3 velocity=(new Vector3(0,.7f,6)-release)/.7f-Physics.gravity*.35f;can.transform.position=release+velocity*dt+Physics.gravity*(.5f*dt*dt);can.transform.rotation=releaseRot*Quaternion.Euler(570*dt,170*dt,95*dt);}
     }
     for(int j=0;j<(frame==0?3:1);j++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
     var old=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(720,960,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,720,960),0,0);tex.Apply();
     File.WriteAllBytes(output+"/frame_"+frame.ToString("D4")+".png",tex.EncodeToPNG());RenderTexture.active=old;Object.DestroyImmediate(tex);frame++;
     if(frame%24==0)File.WriteAllText(output+"/progress.txt",frame+" frames; "+section.Item1);
    }
   }
   File.WriteAllText(output+"/progress.txt","complete "+frame+" frames");return "Rendered "+frame+" frames at 24fps.";
  }
  finally{ShaderUtil.allowAsyncCompilation=asyncShaders;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);foreach(var entry in staticSkins)Object.DestroyImmediate(entry.mesh);Object.DestroyImmediate(floorMat);Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
 }
}
