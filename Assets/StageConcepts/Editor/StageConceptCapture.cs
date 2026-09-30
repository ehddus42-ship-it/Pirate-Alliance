using System;
using System.IO;
using System.Linq;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.StageConcepts.Editor
{
    public static class StageConceptCapture
    {
        public const string Output = "Documentation/StageConcepts/Previews";
        [MenuItem("AC Roguelike/Stage Concepts/Capture Four Themes")]
        public static void CaptureMenu() { Debug.Log(CaptureAll()); }

        public static string CaptureAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Capture in Edit Mode.");
            Directory.CreateDirectory(Output);
            for (int theme=0;theme<4;theme++) CaptureTheme(theme);
            return "Saved eight review images in " + Output;
        }

        public static string CaptureSharedGallery()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Capture in Edit Mode.");
            var scene = EditorSceneManager.OpenPreviewScene(AcRoguelike.Liminal.Editor.LiminalMapBuilder.GalleryPath);
            var target = new RenderTexture(1920,1200,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                foreach(var cam in scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<Camera>(true))) cam.enabled=false;
                foreach(var canvas in scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<Canvas>(true))) canvas.gameObject.SetActive(false);
                var cameraGo=new GameObject("Gallery review camera",typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraGo,scene);
                var camera=cameraGo.GetComponent<Camera>(); camera.enabled=false; camera.scene=scene; camera.cameraType=CameraType.Game;
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.02f,.03f,.04f);
                camera.nearClipPlane=.1f; camera.farClipPlane=1100; camera.allowHDR=true; camera.aspect=1.6f;
                camera.orthographic=true; camera.orthographicSize=180;
                var data=camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing=true; data.volumeLayerMask=1; data.allowXRRendering=false;
                target.Create(); camera.targetTexture=target;
                Position(camera,new Vector3(208,0,140),650,67,0);
                Save(camera,target,Output+"/Shared_Gallery.png");
                return "Saved shared room gallery overview.";
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation=async; target.Release(); Object.DestroyImmediate(target); EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        public static string CaptureTheme(int theme)
        {
            string key = StageConceptBuilder.Keys[theme];
            var scene = EditorSceneManager.OpenPreviewScene(StageConceptBuilder.Root + "/Scenes/StageConcept_" + key + ".unity");
            var target = new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var light = root.GetComponent<Light>();
                    if (light && light.type == LightType.Directional || root.GetComponent<Volume>()) continue;
                    Object.DestroyImmediate(root);
                }
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(StageConceptBuilder.Root + "/Prefabs/Rooms/" + key + "_01.prefab");
                var room = ((GameObject)PrefabUtility.InstantiatePrefab(asset,scene)).GetComponent<LiminalRoom>();
                room.SetGates(false,false);
                var cameraGo = new GameObject("Review camera",typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraGo,scene);
                var camera = cameraGo.GetComponent<Camera>(); camera.enabled = false; camera.scene = scene;
                camera.cameraType = CameraType.Game; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = theme == 2 ? new Color(.13f,.12f,.1f) : new Color(.015f,.027f,.038f);
                camera.nearClipPlane=.1f; camera.farClipPlane=300; camera.allowHDR=true; camera.aspect=1.6f;
                var data=camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing=true; data.volumeLayerMask=1; data.allowXRRendering=false;
                data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                target.Create(); camera.targetTexture=target;
                var playerAsset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Astraia/AstraiaPlayer.prefab");
                var player=(GameObject)PrefabUtility.InstantiatePrefab(playerAsset,scene);
                player.transform.position=new Vector3(0,.05f,13); player.transform.rotation=Quaternion.Euler(0,35,0);
                var animator = player.GetComponentInChildren<Animator>();
                if (animator && animator.runtimeAnimatorController)
                {
                    var idle = animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name.IndexOf("idle",StringComparison.OrdinalIgnoreCase)>=0);
                    if (idle) idle.SampleAnimation(animator.gameObject,.25f);
                }
                camera.fieldOfView=44;
                Position(camera,new Vector3(0,2.5f,18),54,48,32);
                Save(camera,target,Output+"/"+key+"_Overview.png");
                camera.fieldOfView=36;
                Position(camera,new Vector3(0,.6f,13),24,55,35);
                Save(camera,target,Output+"/"+key+"_Gameplay.png");
                return key + " captured";
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation=async;
                target.Release(); Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void Position(Camera camera,Vector3 focus,float distance,float pitch,float yaw)
        { var q=Quaternion.Euler(pitch,yaw,0); camera.transform.SetPositionAndRotation(focus+q*Vector3.back*distance,q); }
        static void Save(Camera camera,RenderTexture target,string path)
        {
            var previous=RenderTexture.active;
            Texture2D image=null;
            try
            {
                var request=new UniversalRenderPipeline.SingleCameraRequest { destination=target };
                if (!RenderPipeline.SupportsRenderRequest(camera,request)) throw new InvalidOperationException("URP capture unavailable.");
                RenderPipeline.SubmitRenderRequest(camera,request); RenderPipeline.SubmitRenderRequest(camera,request);
                RenderTexture.active=target; image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0,false); image.Apply(false,false);
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally { RenderTexture.active=previous; if(image) Object.DestroyImmediate(image); }
        }
    }
}
