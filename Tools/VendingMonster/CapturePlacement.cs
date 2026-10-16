using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using AcRoguelike.Liminal;
using Object=UnityEngine.Object;

public static class CaptureVendingPlacement
{
    public static string Main()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play Mode first.");
        const string output="Documentation/Liminal/Concepts/VendingMachine";
        var scene=EditorSceneManager.NewPreviewScene();
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Liminal/Prefabs/Rooms/01_Arrival_TicketHall.prefab"));
        SceneManager.MoveGameObjectToScene(root,scene);
        var monster=root.GetComponentInChildren<VendingMonster>();monster.animator.enabled=false;
        var camera=new GameObject("PlacementCapture").AddComponent<Camera>();camera.transform.SetParent(root.transform,false);camera.scene=scene;
        camera.transform.position=monster.transform.position+monster.transform.forward*5.2f+monster.transform.right*3.1f+Vector3.up*2.7f;
        camera.transform.LookAt(monster.transform.position+Vector3.up*1.35f);
        camera.fieldOfView=48;camera.nearClipPlane=.1f;camera.farClipPlane=100;
        var rt=new RenderTexture(1280,960,24,RenderTextureFormat.ARGB32);camera.targetTexture=rt;
        var baked=new List<(SkinnedMeshRenderer skin,Mesh mesh,GameObject go)>();
        foreach(var r in monster.limbRenderers)
        {
            var skin=(SkinnedMeshRenderer)r;var mesh=new Mesh();var go=new GameObject("CaptureSkin");go.transform.SetParent(skin.transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
            skin.enabled=false;baked.Add((skin,mesh,go));
        }
        bool oldAsync=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
        try
        {
            foreach(var state in new[]{VendingMonsterState.Dormant,VendingMonsterState.Idle})
            {
                monster.rig.Sample(state,.3f);
                foreach(var entry in baked){entry.go.SetActive(state!=VendingMonsterState.Dormant);entry.skin.BakeMesh(entry.mesh);entry.mesh.RecalculateBounds();}
                for(int i=0;i<3;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                var old=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(1280,960,TextureFormat.RGB24,false);
                tex.ReadPixels(new Rect(0,0,1280,960),0,0);tex.Apply();File.WriteAllBytes(output+"/stage1_"+state.ToString().ToLowerInvariant()+".png",tex.EncodeToPNG());
                RenderTexture.active=old;Object.DestroyImmediate(tex);
            }
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation=oldAsync;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);
            foreach(var entry in baked)Object.DestroyImmediate(entry.mesh);
            Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);
        }
        return output;
    }
}
