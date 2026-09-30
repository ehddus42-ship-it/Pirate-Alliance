using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.Liminal.Editor
{
    public static partial class LiminalMapBuilder
    {
        // Geometry is authored in metres at the original footprint. ExpandRoomFootprint moves
        // furniture without deforming it and expands only architecture, never the room root.
        public const float StandardRoomExpansion = 1.3f;
        public const float BossRoomExpansion = 1.5f;

        [MenuItem("AC Roguelike/Liminal/Rebuild Backrooms Revision")]
        public static void RebuildBackroomsRevision()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before rebuilding rooms.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the open scene before rebuilding rooms.");
            // Preserve all shipped assets before the first rebuild in this revision. Custom
            // variations are never included in the overwrite list used by Build.
            const string backup = "Temp/BackroomsRevisionBackup";
            if (!Directory.Exists(backup))
            {
                Directory.CreateDirectory(backup);
                foreach (string folder in new[] { RoomFolder, PropFolder, Root + "/Scenes", Root + "/Stages", Root + "/Materials" })
                foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    string dest = Path.Combine(backup, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(path, dest, false);
                }
            }
            Build(true);
        }

        static void PrepareBackroomsMaterials()
        {
            BackroomsMat("Backrooms_Carpet", new Color(.61f,.47f,.23f), .015f, "carpet", .85f);
            BackroomsMat("Backrooms_CarpetInset", new Color(.46f,.35f,.17f), .015f, "carpet", .85f);
            var wallpaper = BackroomsMat("Backrooms_Wallpaper", new Color(.86f,.73f,.35f), .035f, "concrete", .7f);
            var shader = Shader.Find("Liminal/Backrooms Wallpaper");
            if (shader)
            {
                wallpaper.shader = shader;
                wallpaper.SetColor("_PatternColor", new Color(.49f,.37f,.13f));
                wallpaper.SetFloat("_PatternScale", 1.5f);
                wallpaper.SetFloat("_PatternStrength", .32f);
            }
            BackroomsMat("Backrooms_Trim", new Color(.40f,.29f,.12f), .10f);
            BackroomsMat("Backrooms_Ceiling", new Color(.91f,.87f,.69f), .04f, "concrete", 1f);
            BackroomsMat("Poolroom_Porcelain", new Color(.89f,.95f,.91f), .36f, "tile", 1.5f);
            BackroomsMat("Poolroom_Aqua", new Color(.23f,.71f,.74f), .43f, "tile", 1.5f);
            BackroomsMat("Mall_Coral", new Color(.72f,.34f,.25f), .18f);
            BackroomsMat("Mall_Cream", new Color(.89f,.81f,.65f), .3f, "tile", .7f);
            BackroomsMat("Transit_Grey", new Color(.60f,.64f,.65f), .09f, "concrete", .6f);
            BackroomsMat("Transit_Yellow", new Color(.93f,.65f,.11f), .12f);
            BackroomsMat("Exit_Jade", new Color(.20f,.67f,.46f), .18f);
            foreach (string key in new[] { "Light_Warm", "Light_Cold" })
            {
                var m = Mats[key];
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", key == "Light_Warm" ? new Color(2.1f,1.83f,1.12f) : new Color(1.35f,2.1f,2.15f));
                EditorUtility.SetDirty(m);
            }
        }

        static Material BackroomsMat(string name, Color tint, float smoothness, string texture = null, float repeat = .5f)
        {
            var mat = Mat(name, tint, smoothness, texture);
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_WorldScale")) mat.SetFloat("_WorldScale", repeat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void CreateBackroomsGrade()
        {
            if(!UniversalRenderPipeline.asset || !UniversalRenderPipeline.asset.supportsHDR)
                throw new InvalidOperationException("The liminal lighting profile requires the existing HDR URP pipeline.");
            // Full-room gallery views sit farther away than the play camera. Keep their
            // architectural shadows instead of dropping them at the template's 50m cutoff.
            UniversalRenderPipeline.asset.shadowDistance=150;
            UniversalRenderPipeline.asset.mainLightShadowmapResolution=4096;
            EditorUtility.SetDirty(UniversalRenderPipeline.asset);
            string path=Root+"/Materials/BackroomsLighting.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if(!profile){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,path);}
            T Component<T>() where T:VolumeComponent
            {
                if(profile.TryGet<T>(out var found))return found;
                var created=profile.Add<T>();created.name=typeof(T).Name;
                AssetDatabase.AddObjectToAsset(created,profile);return created;
            }
            var mapping=Component<Tonemapping>();mapping.mode.Override(TonemappingMode.Neutral);
            var bloom=Component<Bloom>();bloom.threshold.Override(1.15f);bloom.intensity.Override(.16f);bloom.scatter.Override(.45f);bloom.highQualityFiltering.Override(true);
            var color=Component<ColorAdjustments>();color.contrast.Override(4f);color.saturation.Override(-5f);
            foreach(var component in profile.components)EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);
            var volume=Group("Liminal architectural colour grade").AddComponent<Volume>();
            volume.isGlobal=true;volume.priority=10;volume.sharedProfile=profile;
        }

        static void YellowShell(float width = 20, float length = 28, float height = 4.6f)
        {
            Shell("Backrooms_Carpet", "Backrooms_Wallpaper", width, length, height);
            foreach (var node in architecture.Cast<Transform>().ToArray())
            {
                if (node.name == "Continuous skirting") node.GetComponent<Renderer>().sharedMaterial = Mats["Backrooms_Trim"];
                if (node.name == "East wall upper trim" || node.name == "East pilaster")
                    node.GetComponent<Renderer>().sharedMaterial = Mats["Backrooms_Ceiling"];
            }
            // Only the outer strips of the dropped ceiling remain: the play area stays visible.
            foreach (int side in new[] {-1,1})
            {
                if(side>0)Box("Far acoustic ceiling border", new Vector3(width*.5f-.72f,height-.1f,length*.5f), new Vector3(1.15f,.18f,length-.4f), "Backrooms_Ceiling", null, false);
                Box("Wallpaper dado trim", new Vector3(side*(width*.5f-.32f),.78f,length*.5f),new Vector3(.035f,.07f,length-.35f),"Backrooms_Trim",null,false);
            }
        }

        static void YellowWall(Vector3 pos, Vector2 footprint, float height = 2.65f)
        {
            Box("Yellow wallpaper return",pos+Vector3.up*height*.5f,new Vector3(footprint.x,height,footprint.y),"Backrooms_Wallpaper");
            Box("Return wall top cap",pos+Vector3.up*(height+.045f),new Vector3(footprint.x+.08f,.09f,footprint.y+.08f),"Backrooms_Ceiling",null,false);
            Box("Return wall dark skirting",pos+Vector3.up*.10f,new Vector3(footprint.x+.055f,.2f,footprint.y+.055f),"Backrooms_Trim",null,false);
        }

        static void YellowColumn(float x, float z, float height = 3.5f, float width = 1.05f)
        {
            Box("Backrooms square pillar",new Vector3(x,height*.5f,z),new Vector3(width,height,width),"Backrooms_Wallpaper");
            Box("Pillar carpet skirting",new Vector3(x,.11f,z),new Vector3(width+.07f,.22f,width+.07f),"Backrooms_Trim",null,false);
            Box("Acoustic pillar capital",new Vector3(x,height+.06f,z),new Vector3(width+.22f,.12f,width+.22f),"Backrooms_Ceiling",null,false);
        }

        static void CeilingPanel(float x, float z, bool cold = false, float height = 3.8f, bool unlit = false)
        {
            var t = Group("Backrooms recessed light",lighting).transform;
            t.localPosition = new Vector3(x,height,z);
            Box("Acoustic frame", Vector3.zero,new Vector3(2.65f,.1f,1.15f),"Backrooms_Ceiling",t,false);
            Box("Recess shadow",new Vector3(0,.055f,0),new Vector3(2.43f,.016f,.95f),"Rubber",t,false);
            Box("Prismatic diffuser seen from cutaway",new Vector3(0,.069f,0),new Vector3(2.29f,.014f,.80f),unlit?"Porcelain":cold?"Light_Cold":"Light_Warm",t,false);
            Box("Underside diffuser",new Vector3(0,-.061f,0),new Vector3(2.29f,.018f,.8f),unlit?"Porcelain":cold?"Light_Cold":"Light_Warm",t,false);
            for (int k=-3;k<=3;k++) Box("Diffuser louvre",new Vector3(k*.3f,.08f,0),new Vector3(.022f,.015f,.8f),"Backrooms_Ceiling",t,false);
            if (!unlit)
            {
                var lamp=Group("Soft fluorescent pool",t).AddComponent<Light>();
                lamp.type=LightType.Point;lamp.color=cold?new Color(.72f,.94f,1):new Color(1,.91f,.65f);
                lamp.intensity=cold?19:23;lamp.range=10.5f;lamp.shadows=LightShadows.None;lamp.transform.localPosition=new Vector3(0,-.3f,0);
            }
            foreach(var r in t.GetComponentsInChildren<Renderer>())r.shadowCastingMode=ShadowCastingMode.Off;
        }

        static void BackroomsArrival()
        {
            YellowShell();
            // A large, immediately readable corner of yellow walls before any environmental text.
            YellowWall(new Vector3(-6.6f,0,9.4f),new Vector2(6.1f,.35f),2.55f);
            YellowWall(new Vector3(-3.7f,0,6.9f),new Vector2(.35f,4.7f),2.55f);
            YellowWall(new Vector3(6.5f,0,20.7f),new Vector2(6.3f,.35f),3.2f);
            YellowColumn(6.6f,8.0f);YellowColumn(-6.2f,20.4f);
            Prop("reception_desk",new Vector3(6.4f,0,24.5f));
            Prop("waiting_bench",new Vector3(-6.5f,0,15.4f),270);
            Prop("waiting_bench",new Vector3(-6.5f,0,19.4f),270);
            Prop("vending_machine",new Vector3(8.6f,0,15.0f),90);
            Prop("backrooms_workstation",new Vector3(-6.2f,0,25f));
            for(int i=0;i<4;i++) {CeilingPanel(-5.8f,4+i*6);CeilingPanel(5.8f,4+i*6);}
            Box("Reception carpet island",new Vector3(5.7f,.006f,24.2f),new Vector3(7,.011f,5.0f),"Backrooms_CarpetInset",null,false);
            Sign("01",new Vector3(7.3f,3.05f,20.43f),2.2f,"Backrooms_Trim",.85f,0,null,1.35f);
        }

        static void BackroomsMaze()
        {
            YellowShell();
            // Each return is a visible large shape. The 8m middle is continuously traversable.
            float[] zs={6.0f,13.3f,21.0f};
            for(int i=0;i<zs.Length;i++)
            {
                float z=zs[i];int side=i%2==0?1:-1;
                YellowWall(new Vector3(side*7.0f,0,z),new Vector2(5.7f,.38f),side==1?3.1f:2.15f);
                YellowWall(new Vector3(side*4.3f,0,z+1.65f),new Vector2(.38f,3.3f),side==1?3.1f:2.15f);
                YellowColumn(-side*7.7f,z+1.6f,3.6f);
            }
            for(int i=0;i<5;i++){CeilingPanel(-6.1f,3.8f+i*5);CeilingPanel(6.1f,3.8f+i*5,false,3.8f,i==3);}
            Prop("water_dispenser",new Vector3(-8.3f,0,24.5f));
            Prop("plastic_chair",new Vector3(7.4f,0,17f),90);
            Box("Carpet faded cross seam",new Vector3(0,.004f,18),new Vector3(19.2f,.007f,.045f),"Backrooms_CarpetInset",null,false);
        }

        static void BackroomsOffice()
        {
            YellowShell();
            for(int side=-1;side<=1;side+=2)
            {
                for(int row=0;row<3;row++)
                {
                    float z=6.5f+row*7.0f;
                    Prop("backrooms_workstation",new Vector3(side*6.8f,0,z),side==1?90:270);
                    Prop("plastic_chair",new Vector3(side*5.2f,0,z),side==1?270:90);
                    CeilingPanel(side*6.4f,z);
                }
                YellowColumn(side*4.2f,24f,3.4f);
            }
            YellowWall(new Vector3(7,0,3.5f),new Vector2(5.1f,.34f),2.1f);
            Prop("photocopier",new Vector3(-7.6f,0,25.6f));
            Prop("water_dispenser",new Vector3(8.5f,0,25.8f));
        }

        static void BackroomsCopyCourt()
        {
            YellowShell();
            // A single empty sunken-looking carpet rectangle, framed by familiar office objects.
            Box("Dark carpet waiting island",new Vector3(0,.006f,15),new Vector3(8,.012f,11.5f),"Backrooms_CarpetInset",null,false);
            for(int side=-1;side<=1;side+=2)
            {
                YellowWall(new Vector3(side*7.1f,0,8.0f),new Vector2(5.5f,.35f),side<0?2.1f:3.1f);
                YellowColumn(side*5.1f,22.5f,3.5f);
                Prop("waiting_bench",new Vector3(side*6.3f,0,15.5f),side==1?90:270);
                Prop("photocopier",new Vector3(side*7.4f,0,24.4f));
                for(int i=0;i<4;i++)CeilingPanel(side*5.8f,4.5f+i*6.1f);
            }
            Prop("backrooms_workstation",new Vector3(7.1f,0,5),90);
        }

        static void BackroomsPillarHall()
        {
            YellowShell();
            for(int side=-1;side<=1;side+=2)
            {
                for(int row=0;row<4;row++)
                {
                    float z=5.5f+row*5.8f;
                    YellowColumn(side*5.4f,z,side<0?2.7f:3.8f,1.2f);
                    CeilingPanel(side*7.1f,z+1.5f);
                }
                YellowWall(new Vector3(side*8.2f,0,17),new Vector2(.35f,9),3.1f);
            }
            // One coarse, recognizable furniture cluster; no hunt for a rotated individual chair.
            for(int i=0;i<4;i++)Prop("waiting_bench",new Vector3(-7f,0,5.2f+i*5.8f),270);
            Prop("backrooms_workstation",new Vector3(7,0,25.4f));
        }

        static void BackroomsBoss()
        {
            YellowShell(28,36,7);
            // Wide combat floor is empty. Perimeter columns and fans carry the impossible scale.
            foreach(int side in new[]{-1,1})
            {
                for(int row=0;row<5;row++)
                {
                    float z=5.4f+row*6.2f;
                    YellowColumn(side*11.5f,z,side<0?3.6f:6.5f,1.55f);
                    CeilingPanel(side*8.7f,z,true,5.0f);
                    if(row<4)Prop("waiting_bench",new Vector3(side*12.0f,0,z+2.9f),side==1?90:270);
                }
                // An inner colonnade remains in the actual 20m play-camera framing. The
                // open central span is still over 15m wide after expansion.
                foreach(float z in new[]{12.5f,20.0f,27.5f})
                {
                    YellowColumn(side*5.8f,z,side<0?2.65f:4.4f,1.05f);
                    CeilingPanel(side*6.6f,z+1.7f,false,4.8f);
                }
                YellowWall(new Vector3(side*9.0f,0,33.8f),new Vector2(9.3f,.45f),6.3f);
                Prop("industrial_fan",new Vector3(side*8.7f,2.05f,33.4f));
                CeilingPanel(side*4.8f,31.2f,true,5.4f);
                Box("Long carpet arena border",new Vector3(side*7.15f,.007f,20),new Vector3(.12f,.012f,24),"Backrooms_Trim",null,false);
            }
            Box("Open boss floor / uninterrupted carpet",new Vector3(0,.004f,20),new Vector3(14,.007f,24),"Backrooms_CarpetInset",null,false);
            Sign("00",new Vector3(0,5.9f,35.35f),3.8f,"Backrooms_Trim",1.5f,0,null,2.0f);
            // Mid-height panel behind the battle gives scale without hiding the telegraphs.
            Box("Monumental portal header",new Vector3(0,6.3f,33.8f),new Vector3(8.2f,.45f,.65f),"Backrooms_Ceiling",null,false);
            Prop("reception_desk",new Vector3(-8.5f,0,3.7f));
        }

        static void ApplyBackroomsIdentity(GameObject root, int index)
        {
            var room=root.GetComponent<LiminalRoom>();
            string[] names={"노란 대합실","마지막 버스","노란 미로","무인 사무실","푸른 수영장","분수 홀","빈 푸드코트","빈 좌석 홀","수하물 수취장","00번 승강장","관리 통로","끝없는 노란 홀","무인 세탁실","복사기 대기실","빈 주차장","공중전화 광장","닫힌 상점가","물의 아치 회랑","실내 정원","노란 기둥 홀"};
            room.displayName=names[index];
            bool pool=index==4||index==5||index==17;
            bool mall=index==6||index==7||index==16;
            bool transit=index==8||index==9||index==14||index==15;
            foreach(var renderer in architecture.GetComponentsInChildren<Renderer>())
            {
                string matName=renderer.sharedMaterial?renderer.sharedMaterial.name:"";
                if(pool)
                {
                    if(matName=="PoolTiles"||matName=="Plaster_Cold"||matName=="Porcelain")renderer.sharedMaterial=Mats["Poolroom_Porcelain"];
                    if(renderer.name.Contains("coping")||renderer.name.Contains("column base"))renderer.sharedMaterial=Mats["Poolroom_Aqua"];
                }
                else if(mall)
                {
                    if(matName=="MallTiles")renderer.sharedMaterial=Mats["Mall_Cream"];
                    if(matName=="Plaster_Warm")renderer.sharedMaterial=Mats["Mall_Coral"];
                }
                else if(transit&& (matName=="Plaster_Cold"||matName=="Concrete"||matName=="Terrazzo"))renderer.sharedMaterial=Mats["Transit_Grey"];
            }
            if(pool)
            {
                for(int i=0;i<3;i++)
                {
                    // Arches sit along the pools, outside the dry 7m battle corridor.
                    PlacePoolArch(new Vector3(6.25f,0,5.5f+i*7.5f));
                }
                for(int i=0;i<3;i++)
                    Box("Large aqua tile band",new Vector3(9.67f,1.0f,5.5f+i*7.5f),new Vector3(.06f,1.25f,7.4f),"Poolroom_Aqua",null,false);
                foreach(var renderer in props.GetComponentsInChildren<Renderer>())
                    if(renderer.sharedMaterial&&renderer.sharedMaterial.name=="PoolTiles")renderer.sharedMaterial=Mats["Poolroom_Porcelain"];
            }
            if(mall)
            {
                foreach(int side in new[]{-1,1})
                {
                    Box("Coral shopfront fascia",new Vector3(side*7.2f,3.25f,26.0f),new Vector3(5.4f,.56f,.45f),"Mall_Coral",null,false);
                    for(int i=0;i<6;i++) Box("Mall checker tile",new Vector3(side*3.75f,.02f,4+i*3.8f),new Vector3(.9f,.015f,1.8f),"Mall_Coral",null,false);
                }
            }
            if(transit)
            {
                foreach(int side in new[]{-1,1})
                    Box("Continuous yellow wayfinding edge",new Vector3(side*3.6f,.013f,14),new Vector3(.14f,.02f,23),"Transit_Yellow",null,false);
            }
            // A glowing frame is the universal exit cue; no prose is needed to recognize it.
            float length=index==11?36:28;
            foreach(int side in new[]{-1,1})
                Box("Jade exit beacon",new Vector3(side*4.13f,1.65f,length-.40f),new Vector3(.12f,3.3f,.08f),"Light_Cold",null,false);
            Box("Jade exit threshold",new Vector3(0,.02f,length-.6f),new Vector3(8.0f,.025f,.18f),"Exit_Jade",null,false);
            foreach(var lamp in root.GetComponentsInChildren<Light>())
            {
                if(lamp.type!=LightType.Point)continue;
                lamp.range=Mathf.Max(lamp.range,10.5f);
                if(!lamp.transform.parent.name.StartsWith("Backrooms"))lamp.intensity*=1.18f;
            }
            if(index==11)
            {
                room.enemySpawns[0].localPosition=new Vector3(0,.05f,15.2f);
                root.GetComponent<LiminalAmbience>().mood=LiminalAmbience.Mood.Office;
            }
        }

        static void PlacePoolArch(Vector3 position)
        {
            Prop("poolroom_arch",position);
        }

        static void ConfigurePoolArchCollision(Transform holder, Vector3 size)
        {
            // A bounding box across the whole arch would seal its opening. Only the feet block.
            var body=holder.GetComponent<BoxCollider>();if(body)Object.DestroyImmediate(body);
            foreach(var child in holder.Cast<Transform>().Where(t=>t.name=="Arch pier collision").ToArray())Object.DestroyImmediate(child.gameObject);
            foreach(int side in new[]{-1,1})
            {
                var foot=Group("Arch pier collision",holder).AddComponent<BoxCollider>();
                foot.center=new Vector3(side*size.x*.40f,size.y*.5f,0);
                foot.size=new Vector3(size.x*.20f,size.y,size.z);
            }
        }

        static void ExpandRoomFootprint(GameObject root, int index)
        {
            float ratio=index==11?BossRoomExpansion:StandardRoomExpansion;
            Vector3 plane=new Vector3(ratio,1,ratio);
            // Nested architecture is intentionally structural. Furniture and luminaires are
            // top-level anchors under Props / Lighting and retain their metre-scale dimensions.
            foreach(Transform child in architecture)
            {
                child.localPosition=Vector3.Scale(child.localPosition,plane);
                child.localScale=Vector3.Scale(child.localScale,plane);
            }
            foreach(var group in new[]{props,lighting,sockets})
                foreach(Transform child in group)child.localPosition=Vector3.Scale(child.localPosition,plane);
            var room=root.GetComponent<LiminalRoom>();
            room.playerSpawn.localPosition=Vector3.Scale(room.playerSpawn.localPosition,plane);
            foreach(var spawn in room.enemySpawns)spawn.localPosition=Vector3.Scale(spawn.localPosition,plane);
            foreach(var gate in new[]{room.entranceGate,room.exitGate})
            {
                gate.transform.localPosition=Vector3.Scale(gate.transform.localPosition,plane);
                gate.transform.localScale=new Vector3(ratio,1,1);
            }
            room.localBounds=new Bounds(Vector3.Scale(room.localBounds.center,plane),Vector3.Scale(room.localBounds.size,plane));
            var ambience=root.GetComponent<LiminalAmbience>();
            ambience.localCenter=Vector3.Scale(ambience.localCenter,plane);
            ambience.halfExtents=Vector3.Scale(ambience.halfExtents,plane);
            root.transform.localScale=Vector3.one;
        }
    }
}
