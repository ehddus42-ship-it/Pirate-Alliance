#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoonLuna.Editor
{
    /// <summary>Regenerates only Luna's combat assets, in a private preview scene; never saves the user's open scene.</summary>
    public static class LunaCombatSetup
    {
        public const string Root="Assets/Characters/MoonLuna/Combat";
        public const string LibraryPath=Root+"/LunaCombatFxLibrary.asset";
        static readonly Color Cream=new Color(.98f,.94f,.89f);
        static readonly Color Lavender=new Color(.74f,.78f,.93f);
        static readonly Color Indigo=new Color(.25f,.27f,.43f);
        static readonly Color Gold=new Color(1,.69f,.22f);
        static UnityEngine.SceneManagement.Scene preview;

        [MenuItem("Tools/Moon Luna/Combat/Install Lunar Effects and Projectiles")]
        public static void Install()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before installing combat assets.");
            foreach(var dir in new[]{"Materials","Meshes","Prefabs"}) Folder(Root+"/"+dir);
            ImportTexture("LunaMoonSurface"); ImportTexture("LunaSpectralWisp"); ImportTexture("LunaGoldenEyeFlame"); ImportTexture("LunaEyeAfterimage");
            var lib=AssetDatabase.LoadAssetAtPath<LunaCombatFxLibrary>(LibraryPath);
            if(!lib) { lib=ScriptableObject.CreateInstance<LunaCombatFxLibrary>(); AssetDatabase.CreateAsset(lib,LibraryPath); }
            var moon=Material("LunarIvory","MoonLuna/Combat/Moon Surface","LunaMoonSurface",Cream,1.15f);
            moon.SetColor("_Rim",new Color(1.7f,.88f,.23f)); Save(moon);
            var awakened=Material("LunarMadness","MoonLuna/Combat/Moon Surface","LunaMoonSurface",new Color(1,.83f,.46f),1.75f);
            awakened.SetColor("_Rim",new Color(3,1.3f,.12f)); Save(awakened);
            var wisp=Material("LavenderVapor","MoonLuna/Combat/Spectral","LunaSpectralWisp",Color.white,1.8f);
            var goldVapor=Material("GoldenVapor","MoonLuna/Combat/Spectral","LunaSpectralWisp",new Color(1.5f,.82f,.26f),2.4f);
            goldVapor.SetFloat("_Colorize",.85f); Save(goldVapor);
            var flame=Material("GoldenEyeFire","MoonLuna/Combat/Spectral","LunaGoldenEyeFlame",Color.white,2.2f);
            var eyeEcho=Material("LunarEyeAfterimage","MoonLuna/Combat/Spectral","LunaEyeAfterimage",Color.white,1.1f);
            eyeEcho.SetFloat("_Colorize",1f); eyeEcho.SetFloat("_Flow",.06f); Save(eyeEcho);
            var dust=Material("MoonDust","MoonLuna/Combat/Spectral","LunaGoldenEyeFlame",new Color(.8f,.82f,1),1.2f);
            lib.phaseMeshes=new[]{MoonMesh(0),MoonMesh(1),MoonMesh(2)};
            lib.moonMaterial=moon; lib.awakenedMoonMaterial=awakened; lib.eyeFlameMaterial=eyeEcho; lib.wispMaterial=wisp;
            preview=EditorSceneManager.NewPreviewScene();
            try
            {
                for(int phase=0;phase<3;phase++)
                    lib.projectiles[phase]=Projectile(new[]{"01_Crescent","02_HalfMoon","03_FullMoon"}[phase],phase,false,lib,moon,wisp,dust);
                lib.awakenedProjectile=Projectile("04_AwakenedFullMoon",2,true,lib,awakened,goldVapor,flame);
                lib.smallImpact=Impact("Impact_MoonShard",false,false,lib,moon,wisp,dust);
                lib.fullMoonImpact=Impact("Impact_FullMoonBloom",true,false,lib,moon,wisp,dust);
                lib.awakenedImpact=Impact("Impact_MadnessBloom",true,true,lib,awakened,goldVapor,flame);
                lib.moonlightField=Field(wisp,dust);
                lib.moonlightCast=Cast("Cast_Moonlight",false,lib,moon,wisp,dust);
                lib.ultimateCast=Cast("Cast_LunarRelease",true,lib,awakened,goldVapor,flame);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            Save(lib);
            var battle=PrefabUtility.LoadPrefabContents(LunaStageSetup.BattlePrefab);
            try { ApplyTo(battle); PrefabUtility.SaveAsPrefabAsset(battle,LunaStageSetup.BattlePrefab); }
            finally { PrefabUtility.UnloadPrefabContents(battle); }
            LunaStageSetup.InstallUnifiedHunter();
            Debug.Log("Moon Luna: textured lunar projectiles, moonlight field, ultimate and eye fire installed.");
        }
        public static void ApplyTo(GameObject instance)
        {
            var lib=AssetDatabase.LoadAssetAtPath<LunaCombatFxLibrary>(LibraryPath);
            if(!lib) return;
            var fx=instance.GetComponent<LunaCombatVfx>() ?? instance.AddComponent<LunaCombatVfx>();
            fx.library=lib;
            if(!instance.GetComponent<LunaCombatController>()) instance.AddComponent<LunaCombatController>();
        }
        static void Folder(string path)
        {
            if(AssetDatabase.IsValidFolder(path)) return;
            Folder(Path.GetDirectoryName(path).Replace('\\','/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));
        }
        static void Save(UnityEngine.Object obj) { EditorUtility.SetDirty(obj); AssetDatabase.SaveAssetIfDirty(obj); }
        static void ImportTexture(string name)
        {
            string path=Root+"/Textures/"+name+".png";
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var imp=(TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType=TextureImporterType.Default;
            imp.alphaSource=TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency=true;
            imp.sRGBTexture=true; imp.mipmapEnabled=true; imp.maxTextureSize=1024;
            imp.wrapMode=TextureWrapMode.Clamp; imp.filterMode=FilterMode.Trilinear;
            imp.textureCompression=TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
        }
        static Material Material(string name,string shader,string texture,Color tint,float intensity)
        {
            string path=Root+"/Materials/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            var s=Shader.Find(shader); if(!s) throw new InvalidOperationException("Missing shader: "+shader);
            if(!mat) { mat=new Material(s); AssetDatabase.CreateAsset(mat,path); }
            mat.shader=s; mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/"+texture+".png"));
            mat.SetColor("_Tint",tint); mat.SetFloat("_Intensity",intensity);
            Save(mat); return mat;
        }
        static GameObject Object(string name,Transform parent=null)
        {
            var go=new GameObject(name);
            if(parent) go.transform.SetParent(parent,false);
            else UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,preview);
            return go;
        }
        static GameObject Prefab(GameObject go)
        {
            foreach(var ps in go.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var asset=PrefabUtility.SaveAsPrefabAsset(go,Root+"/Prefabs/"+go.name+".prefab");
            UnityEngine.Object.DestroyImmediate(go); return asset;
        }
        static GameObject Projectile(string name,int phase,bool awakened,LunaCombatFxLibrary lib,Material moon,Material vapor,Material dust)
        {
            var root=Object("Luna_"+name);
            var body=Object("CrateredLunarBody",root.transform);
            body.AddComponent<MeshFilter>().sharedMesh=lib.phaseMeshes[phase];
            var mr=body.AddComponent<MeshRenderer>(); mr.sharedMaterial=moon; mr.shadowCastingMode=ShadowCastingMode.Off; mr.receiveShadows=false;
            float scale=awakened?.78f:new[]{.62f,.67f,.78f}[phase];
            body.transform.localScale=Vector3.one*scale;
            var visual=root.AddComponent<LunaProjectileVisual>(); visual.moon=body.transform; visual.rollSpeed=phase==0?20:8;
            var ps=Particles(root,"VaporWake",vapor,awakened?48:32,.42f,awakened?.56f:.43f,.12f,.12f,0,true,new Color(.76f,.8f,1,.46f));
            var main=ps.main; main.simulationSpace=ParticleSystemSimulationSpace.World;
            var velocity=ps.velocityOverLifetime; velocity.enabled=true; velocity.space=ParticleSystemSimulationSpace.World;
            velocity.y=new ParticleSystem.MinMaxCurve(.1f,.28f);
            ps=Particles(root,"LunarEmbers",dust,24,.34f,.07f,.25f,.19f,0,true,awakened?Gold:Lavender);
            main=ps.main; main.simulationSpace=ParticleSystemSimulationSpace.World;
            ps=Particles(root,"SoftCorona",vapor,7,.27f,scale*1.9f,0,.02f,0,true,new Color(.7f,.65f,1,.19f));
            return Prefab(root);
        }
        static GameObject Impact(string name,bool full,bool awakened,LunaCombatFxLibrary lib,Material moon,Material vapor,Material dust)
        {
            var root=Object("Luna_"+name);
            Particles(root,"VeilBloom",vapor,0,.8f,full?1.8f:.65f,full?1.5f:.6f,.12f,full?14:6,false,new Color(.72f,.68f,1,full?.65f:.5f));
            var shards=Particles(root,"LunarShards",moon,0,.65f,full?.15f:.10f,full?3.1f:1.5f,.1f,full?16:7,false,Cream);
            var pr=shards.GetComponent<ParticleSystemRenderer>(); pr.renderMode=ParticleSystemRenderMode.Mesh; pr.mesh=lib.phaseMeshes[0];
            var rotation=shards.rotationOverLifetime; rotation.enabled=true; rotation.z=new ParticleSystem.MinMaxCurve(-7,7);
            Particles(root,"GlintAsh",dust,0,1.1f,awakened?.15f:.09f,full?2.8f:1.3f,.15f,full?35:14,false,awakened?Gold:Lavender);
            if(full)
            {
                var ground=Particles(root,"LowRollingVapor",vapor,0,1.3f,3.2f,.18f,.5f,5,false,new Color(.48f,.42f,1,.38f));
                ground.GetComponent<ParticleSystemRenderer>().renderMode=ParticleSystemRenderMode.HorizontalBillboard;
                var main=ground.main; main.startRotation= new ParticleSystem.MinMaxCurve(0,6.28f);
            }
            return Prefab(root);
        }
        static GameObject Field(Material vapor,Material dust)
        {
            var root=Object("Luna_MoonlightSanctuary");
            var ps=Particles(root,"RollingMoonFog",vapor,10,1.9f,2.9f,.1f,4.5f,0,true,new Color(.51f,.56f,1,.30f));
            Disc(ps,4.5f); ps.GetComponent<ParticleSystemRenderer>().renderMode=ParticleSystemRenderMode.HorizontalBillboard;
            var main=ps.main; main.simulationSpace=ParticleSystemSimulationSpace.Local;
            ps=Particles(root,"RisingLunarBreath",vapor,15,1.4f,.75f,.3f,4.0f,0,true,new Color(.7f,.72f,1,.35f));
            Disc(ps,4); var velocity=ps.velocityOverLifetime; velocity.enabled=true; velocity.y=.38f; velocity.space=ParticleSystemSimulationSpace.World;
            ps=Particles(root,"SilverRain",dust,32,1.8f,.075f,.1f,4.4f,0,true,Lavender);
            Disc(ps,4.4f); ps.transform.localPosition=Vector3.up*2;
            velocity=ps.velocityOverLifetime; velocity.enabled=true; velocity.y=-.72f; velocity.space=ParticleSystemSimulationSpace.World;
            return Prefab(root);
        }
        static GameObject Cast(string name,bool ultimate,LunaCombatFxLibrary lib,Material moon,Material vapor,Material dust)
        {
            var root=Object("Luna_"+name);
            var ps=Particles(root,"MoonriseVeil",vapor,0,1.4f,ultimate?1.8f:1.3f,.5f,.6f,ultimate?24:14,false,new Color(.74f,.66f,1,.5f));
            var velocity=ps.velocityOverLifetime; velocity.enabled=true; velocity.y=ultimate?1.2f:.6f; velocity.space=ParticleSystemSimulationSpace.World;
            ps=Particles(root,"LunarPetals",moon,0,1.25f,.12f,ultimate?2.6f:1.6f,.3f,ultimate?30:12,false,Cream);
            var pr=ps.GetComponent<ParticleSystemRenderer>(); pr.renderMode=ParticleSystemRenderMode.Mesh; pr.mesh=lib.phaseMeshes[0];
            var rotation=ps.rotationOverLifetime; rotation.enabled=true; rotation.z=new ParticleSystem.MinMaxCurve(-6,6);
            ps=Particles(root,"FallingMadness",dust,0,1.6f,ultimate?.13f:.07f,ultimate?2.4f:1.2f,.4f,ultimate?56:25,false,ultimate?Gold:Lavender);
            var main=ps.main; main.gravityModifier=.12f;
            return Prefab(root);
        }
        static void Disc(ParticleSystem ps,float radius)
        {
            var shape=ps.shape; shape.shapeType=ParticleSystemShapeType.Circle; shape.radius=radius;
            shape.radiusThickness=1; shape.rotation=new Vector3(-90,0,0);
        }
        static ParticleSystem Particles(GameObject root,string name,Material mat,float rate,float lifetime,float size,float speed,float radius,int count,bool loop,Color color)
        {
            var go=Object(name,root.transform); var ps=go.AddComponent<ParticleSystem>();
            ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main; main.duration=2; main.loop=loop; main.playOnAwake=true; main.startLifetime=new ParticleSystem.MinMaxCurve(lifetime*.7f,lifetime);
            main.startSpeed=new ParticleSystem.MinMaxCurve(speed*.5f,speed); main.startSize=new ParticleSystem.MinMaxCurve(size*.7f,size*1.2f);
            main.startColor=color; main.startRotation=new ParticleSystem.MinMaxCurve(0,6.28f); main.maxParticles=loop?180:100;
            main.simulationSpace=ParticleSystemSimulationSpace.World; main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            var emission=ps.emission; emission.rateOverTime=rate;
            if(count>0) emission.SetBursts(new[]{new ParticleSystem.Burst(0,(short)count)});
            var shape=ps.shape; shape.enabled=true; shape.shapeType=ParticleSystemShapeType.Sphere; shape.radius=radius;
            var noise=ps.noise; noise.enabled=true; noise.strength=.12f; noise.frequency=.75f; noise.scrollSpeed=.4f; noise.quality=ParticleSystemNoiseQuality.Medium;
            var col=ps.colorOverLifetime; col.enabled=true;
            var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.65f,.58f,.86f),1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(.55f,.65f),new GradientAlphaKey(0,1)});
            col.color=gradient;
            var sz=ps.sizeOverLifetime; sz.enabled=true;
            sz.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.45f),new Keyframe(.25f,1),new Keyframe(1,1.35f)));
            var renderer=go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=mat; renderer.renderMode=ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false; renderer.maxParticleSize=4;
            return ps;
        }
        static Mesh MoonMesh(int phase)
        {
            var vertices=new List<Vector3>(); var uv=new List<Vector2>(); var colors=new List<Color>(); var triangles=new List<int>();
            const int rows=48, columns=10;
            // A closed, bevelled lunar solid: the lit terminator is curved for the crescent and straight for the half moon.
            for(int side=0;side<2;side++)
                for(int row=0;row<=rows;row++)
                {
                    float y=Mathf.Lerp(-.9999f,.9999f,row/(float)rows); float edge=Mathf.Sqrt(1-y*y);
                    float left=phase==0?edge*.42f:phase==1?0:-edge;
                    for(int col=0;col<=columns;col++)
                    {
                        float u=col/(float)columns, x=Mathf.Lerp(left,edge,u);
                        float bulge=Mathf.Sqrt(Mathf.Max(0,1-x*x-y*y));
                        vertices.Add(new Vector3(x*.5f,y*.5f,(side==0?1:-1)*(.018f+bulge*.045f)));
                        uv.Add(new Vector2(.5f+x*.40f,.5f+y*.40f));
                        colors.Add(new Color(side==0?1:.4f,(col==0||col==columns)?1:0,0,1));
                    }
                }
            int stride=columns+1, faceCount=(rows+1)*stride;
            for(int side=0;side<2;side++) for(int row=0;row<rows;row++) for(int col=0;col<columns;col++)
            {
                int a=side*faceCount+row*stride+col,b=a+1,c=a+stride,d=c+1;
                if(side==0) triangles.AddRange(new[]{a,b,c,b,d,c}); else triangles.AddRange(new[]{a,c,b,b,c,d});
            }
            for(int row=0;row<rows;row++) foreach(int col in new[]{0,columns})
            {
                int a=row*stride+col,b=a+stride,c=a+faceCount,d=b+faceCount;
                triangles.AddRange(new[]{a,c,b,b,c,d});
            }
            foreach(int row in new[]{0,rows}) for(int col=0;col<columns;col++)
            {
                int a=row*stride+col,b=a+1,c=a+faceCount,d=b+faceCount;
                triangles.AddRange(new[]{a,b,c,b,d,c});
            }
            string name=new[]{"Crescent","HalfMoon","FullMoon"}[phase]; string path=Root+"/Meshes/"+name+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh) { mesh=new Mesh(); AssetDatabase.CreateAsset(mesh,path); } else mesh.Clear();
            mesh.name="Luna_"+name; mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetColors(colors); mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); Save(mesh); return mesh;
        }
    }
}
#endif
