using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcRoguelike;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.Liminal.Editor
{
    /// <summary>Initial authoring kit. Existing room prefabs and scenes are preserved by BuildMissing.</summary>
    public static partial class LiminalMapBuilder
    {
        public const string Root = "Assets/Liminal";
        public const string RoomFolder = Root + "/Prefabs/Rooms";
        public const string PropFolder = Root + "/Prefabs/Props";
        public const string GalleryPath = Root + "/Scenes/LiminalRoomGallery.unity";
        public const string PropGalleryPath = Root + "/Scenes/LiminalPropGallery.unity";
        public const string RunPath = Root + "/Scenes/LiminalRun.unity";
        public static readonly string[] RoomIds = {
            "01_Arrival_TicketHall", "02_Arrival_LastBus", "03_Office_RepeatingDoors",
            "04_Office_FluorescentCourt", "05_Pool_ReflectionGallery", "06_Pool_DryFountain",
            "07_Mall_ClosedFoodCourt", "08_Mall_MisplacedChairs", "09_Transit_BaggageClaim",
            "10_Transit_PlatformZero", "11_Threshold_ServicePassage", "12_Boss_DepartureConcourse",
            "13_Service_Laundromat", "14_Office_CopyWaiting", "15_Transit_EmptyParking",
            "16_Transit_PayphoneCourt", "17_Mall_ShutterArcade", "18_Pool_BentColonnade",
            "19_Arrival_IndoorGarden", "20_Mall_ChairArchive"
        };
        public static readonly string[] RoomTitles = {
            "ARRIVALS / The number never changes", "LAST BUS / Please remain seated",
            "ADMINISTRATION / All doors lead to 04", "NIGHT OFFICE / Nobody clocked out",
            "REFLECTION HALL / No swimming", "DRY FOUNTAIN / Water without a source",
            "FOOD COURT / Service resumes shortly", "DINING HALL / A seat for nobody",
            "BAGGAGE CLAIM / An arrival with no passengers", "PLATFORM 00 / Next service: --:--",
            "SERVICE ACCESS / Almost outside", "DEPARTURES / The last attendant",
            "LAUNDRY / A cycle that never ends", "COPY ROOM / Please wait for your copy",
            "PARKING / Every bay is 04", "PUBLIC TELEPHONES / No incoming calls",
            "SHOPPING ARCADE / Opening soon", "COLONNADE / The corner beyond the water",
            "INDOOR GARDEN / Nothing grows", "SEATING STORAGE / More chairs than visitors"
        };
        public static readonly string[] RoomDisplayNames={"도착 대합실","마지막 버스","반복되는 행정 복도","야간 사무실","반사의 수영장","마른 분수","닫힌 푸드코트","아무도 앉지 않는 자리","수하물 수취장","00번 승강장","관리 통로","마지막 출발 대합실","끝나지 않는 세탁실","복사를 기다리는 방","빈 주차장","공중전화 광장","개점 전 상점가","꺾이는 물의 회랑","실내 정원","의자 보관실"};

        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        static Transform architecture, props, lighting, gameplay, sockets;
        static Font signageFont;
        static Material signageMaterial;
        static int detailCounter;
        static readonly Color Ink = new Color(.09f,.16f,.17f);
        static readonly Color Cream = new Color(.76f,.75f,.61f);
        static readonly Color Teal = new Color(.10f,.38f,.36f);

        [MenuItem("AC Roguelike/Liminal/Build Missing Rooms and Scenes")]
        public static void BuildMissing()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(false);
        }

        // Automation entry point: fails rather than silently replacing an unsaved scene.
        public static void BuildForAutomation()
        {
            for (int i=0;i<SceneManager.sceneCount;i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the open scene before building the liminal kit.");
            Build(false);
        }

        [MenuItem("AC Roguelike/Liminal/Rebuild Authored Kit (Overwrite)")]
        public static void RebuildWithConfirmation()
        {
            if (!EditorUtility.DisplayDialog("Replace authored liminal rooms?",
                "This replaces the 20 shipped room prefabs and both liminal scenes. Custom variation prefabs are preserved. Save a copy of any hand-edited shipped room before continuing.","Replace shipped kit","Cancel")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(true);
        }

        static void Build(bool overwrite)
        {
            foreach (var p in new[]{RoomFolder,PropFolder,Root+"/Scenes",Root+"/Materials",Root+"/Textures",Root+"/Stages",Root+"/Art/Meshy"}) Directory.CreateDirectory(p);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            PrepareMaterials();
            for(int i=0;i<RoomIds.Length;i++)
            {
                string path=RoomFolder+"/"+RoomIds[i]+".prefab";
                if(!overwrite && AssetDatabase.LoadAssetAtPath<GameObject>(path)) continue;
                var go=BuildRoom(i);
                PrefabUtility.SaveAsPrefabAsset(go,path);
                Object.DestroyImmediate(go);
            }
            AssetDatabase.SaveAssets();
            CreatePropPrefabs(overwrite);
            CreateStages(overwrite);
            if(overwrite || !File.Exists(GalleryPath)) CreateGallery();
            if(overwrite || !File.Exists(PropGalleryPath)) CreatePropGallery();
            if(overwrite || !File.Exists(RunPath)) CreateRun();
            var scenes=EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(s=>s.path==RunPath || s.path==GalleryPath || s.path==PropGalleryPath);
            scenes.Insert(0,new EditorBuildSettingsScene(RunPath,true));
            scenes.Add(new EditorBuildSettingsScene(GalleryPath,true));
            scenes.Add(new EditorBuildSettingsScene(PropGalleryPath,true));
            EditorBuildSettings.scenes=scenes.ToArray();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(GalleryPath);
            FrameGallery();
            Debug.Log("LIMINAL_KIT_READY: 20 room variations, 4 stages, editable gallery and run scenes. " + ValidateAll("Documentation/Liminal/backrooms-authoring-validation.txt"));
        }

        static void PrepareMaterials()
        {
            Mats.Clear();
            Mat("Plaster_Warm",new Color(.68f,.67f,.53f),.03f);
            Mat("Plaster_Cold",new Color(.51f,.66f,.63f),.08f);
            Mat("Paint_Teal",Teal,.22f);
            Mat("Paint_Mustard",new Color(.59f,.43f,.16f),.16f);
            Mat("Porcelain",new Color(.83f,.87f,.78f),.48f);
            Mat("Terrazzo",new Color(.58f,.61f,.53f),.38f,"terrazzo");
            Mat("PoolTiles",new Color(.57f,.78f,.72f),.5f,"tile");
            Mat("OfficeCarpet",new Color(.30f,.35f,.30f),.02f,"carpet");
            Mat("MallTiles",new Color(.71f,.66f,.53f),.43f,"tile");
            Mat("Concrete",new Color(.37f,.42f,.40f),.02f,"concrete");
            Mat("Rubber",new Color(.075f,.095f,.092f),.1f);
            Mat("Metal",new Color(.28f,.34f,.33f),.6f,null,.72f);
            Mat("Chrome",new Color(.55f,.65f,.61f),.7f,null,.8f);
            Mat("Wood",new Color(.34f,.22f,.11f),.13f,"wood");
            Mat("Seat_Orange",new Color(.70f,.30f,.12f),.35f);
            Mat("Seat_Teal",new Color(.10f,.39f,.37f),.28f);
            Mat("Seat_White",new Color(.74f,.76f,.67f),.28f);
            Mat("Shadow",new Color(.14f,.18f,.16f),.01f);
            Mat("Soil",new Color(.12f,.115f,.08f),0);
            Mat("Foliage",new Color(.20f,.30f,.16f),.02f);
            Mat("Glass_Dark",new Color(.045f,.14f,.14f),.84f,null,.28f);
            Mat("Sign_Dark",Ink,.16f);
            Mat("Light_Warm",new Color(.94f,.86f,.60f),.2f,null,0,new Color(1.3f,1.12f,.73f));
            Mat("Light_Cold",new Color(.69f,.94f,.86f),.2f,null,0,new Color(.7f,1.35f,1.17f));
            Mat("Light_Red",new Color(.71f,.20f,.09f),.2f,null,0,new Color(1.25f,.16f,.05f));
            string waterPath=Root+"/Materials/QuietWater.mat";
            var water=AssetDatabase.LoadAssetAtPath<Material>(waterPath);
            if(!water){water=new Material(Shader.Find("Liminal/Quiet Water") ?? Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(water,waterPath);}
            Mats["Water"]=water;
            PrepareBackroomsMaterials();
            signageFont=AssetDatabase.LoadAssetAtPath<Font>(Root+"/Art/Fonts/NotoSansKR-Regular.ttf") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            string fontMaterialPath=Root+"/Materials/SignageFont.mat";
            signageMaterial=AssetDatabase.LoadAssetAtPath<Material>(fontMaterialPath);
            signageFont.RequestCharactersInTexture("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 /:.>-+_()?",48,FontStyle.Normal);
            if(!signageMaterial){signageMaterial=new Material(Shader.Find("Liminal/World Text"));AssetDatabase.CreateAsset(signageMaterial,fontMaterialPath);}
            signageMaterial.shader=Shader.Find("Liminal/World Text");
            signageMaterial.mainTexture=signageFont.material.mainTexture;
        }

        static Material Mat(string name,Color color,float smooth,string textureKind=null,float metallic=0,Color? emission=null)
        {
            string path=Root+"/Materials/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m)
            {
                m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name=name;
                m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",smooth);m.SetFloat("_Metallic",metallic);
                if(textureKind!=null)
                {
                    m.SetTexture("_BaseMap",Texture(textureKind));m.SetTextureScale("_BaseMap",textureKind=="wood"?new Vector2(1,4):new Vector2(10,14));
                    string normalPath=Root+"/Textures/Surface_"+textureKind+"_Normal.png";
                    var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                    if(normal){var importer=AssetImporter.GetAtPath(normalPath) as TextureImporter;if(importer){importer.textureType=TextureImporterType.NormalMap;importer.SaveAndReimport();}m.SetTexture("_BumpMap",normal);m.SetFloat("_BumpScale",.24f);m.EnableKeyword("_NORMALMAP");}
                    var worldShader=Shader.Find("Liminal/Architectural Surface");
                    if(worldShader){m.shader=worldShader;m.SetFloat("_WorldScale",.5f);}
                }
                if(emission.HasValue){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",emission.Value);}
                AssetDatabase.CreateAsset(m,path);
            }
            Mats[name]=m;return m;
        }

        static Texture2D Texture(string kind)
        {
            string path=Root+"/Textures/Surface_"+kind+".png";
            var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(existing)return existing;
            const int n=128;var t=new Texture2D(n,n,TextureFormat.RGBA32,false);var px=new Color[n*n];var random=new System.Random(8107);
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                float v=.92f+(float)random.NextDouble()*.08f;
                if(kind=="tile") { if(x<2 || y<2)v=.52f; else if(x<4||y<4)v=.83f; }
                if(kind=="terrazzo" && random.NextDouble()<.075) v=.48f+(float)random.NextDouble()*.35f;
                if(kind=="carpet")v=.69f+(float)random.NextDouble()*.29f+Mathf.Sin(x*1.57f)*.04f;
                if(kind=="wood")v=.70f+Mathf.Sin(x*.24f+Mathf.Sin(y*.07f)*1.2f)*.13f+(float)random.NextDouble()*.08f;
                if(kind=="concrete")v=.80f+Mathf.PerlinNoise(x*.09f,y*.09f)*.16f;
                px[y*n+x]=new Color(v,v,v,1);
            }
            t.SetPixels(px);t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.wrapMode=TextureWrapMode.Repeat;importer.filterMode=FilterMode.Bilinear;importer.mipmapEnabled=true;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static GameObject Group(string name,Transform parent=null)
        {
            var go=new GameObject(name);if(parent)go.transform.SetParent(parent,false);return go;
        }
        static GameObject Box(string name,Vector3 pos,Vector3 size,string material,Transform parent=null,bool collision=true)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent?parent:architecture,false);go.transform.localPosition=pos;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=Mats[material];
            if(!collision)Object.DestroyImmediate(go.GetComponent<Collider>());go.isStatic=true;return go;
        }
        static GameObject Shape(string name,PrimitiveType type,Vector3 pos,Vector3 size,string material,Transform parent=null,bool collision=false)
        {
            var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent?parent:props,false);go.transform.localPosition=pos;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=Mats[material];
            if(!collision)Object.DestroyImmediate(go.GetComponent<Collider>());
            else if(type==PrimitiveType.Cylinder){Object.DestroyImmediate(go.GetComponent<Collider>());var c=go.AddComponent<MeshCollider>();c.sharedMesh=go.GetComponent<MeshFilter>().sharedMesh;}
            go.isStatic=true;return go;
        }
        static Transform Point(string name,Transform parent,Vector3 position)
        {var t=Group(name,parent).transform;t.localPosition=position;return t;}

        static GameObject BuildRoom(int index)
        {
            detailCounter=0;
            var root=Group(RoomIds[index]);
            var atlas=root.AddComponent<LiminalSignageAtlas>();atlas.font=signageFont;atlas.material=signageMaterial;
            architecture=Group("Architecture",root.transform).transform;props=Group("Props",root.transform).transform;
            lighting=Group("Lighting",root.transform).transform;gameplay=Group("Gameplay",root.transform).transform;sockets=Group("Sockets",root.transform).transform;
            var room=root.AddComponent<LiminalRoom>();room.roomId=RoomIds[index];room.displayName=RoomDisplayNames[index];
            float width=index==11?28:20,length=index==11?36:28;
            room.kind=index<2?LiminalRoomKind.Arrival:index==10?LiminalRoomKind.Threshold:index==11?LiminalRoomKind.Boss:(index==4||index==7||index==17?LiminalRoomKind.Exploration:LiminalRoomKind.Combat);
            room.entry=Point("Entry (+Z)",sockets,Vector3.zero);room.exit=Point("Exit (+Z)",sockets,new Vector3(0,0,length));
            room.playerSpawn=Point("Player Spawn",gameplay,new Vector3(0,.06f,3.5f));
            room.localBounds=new Bounds(new Vector3(0,2,length*.5f),new Vector3(width,8,length));
            var ambience=root.AddComponent<LiminalAmbience>();ambience.localCenter=new Vector3(0,1,length*.5f);ambience.halfExtents=new Vector3(width*.5f,5,length*.5f);
            ambience.mood=index==4||index==5||index==17?LiminalAmbience.Mood.Pool:index==8||index==9||index==10||index==11||index==12||index==14?LiminalAmbience.Mood.Service:LiminalAmbience.Mood.Office;
            var spawnRoot=Group("EnemySpawns",gameplay).transform;
            Vector3[] enemyPositions=index==11?new[]{new Vector3(0,.05f,23)}:new[]{new Vector3(-2.7f,.05f,11),new Vector3(2.7f,.05f,14),new Vector3(-1.9f,.05f,20)};
            if(index==17)enemyPositions[0]=new Vector3(-.7f,.05f,11.5f);
            room.enemySpawns=enemyPositions.Select((v,i)=>Point("Enemy "+(i+1).ToString("00"),spawnRoot,v)).ToArray();
            room.entranceGate=Gate("Entrance Gate",.5f,width);room.exitGate=Gate("Exit Gate",length-.5f,width);
            room.entranceGate.SetActive(false);room.exitGate.SetActive(false);
            switch(index)
            {
                case 0:BackroomsArrival();break;case 1:Arrival(true);break;
                case 2:BackroomsMaze();break;case 3:BackroomsOffice();break;
                case 4:Pool(false);break;case 5:Pool(true);break;
                case 6:Mall(false);break;case 7:Mall(true);break;
                case 8:Transit(false);break;case 9:Transit(true);break;
                case 10:Service();break;case 11:BackroomsBoss();break;
                case 12:Laundry();break;case 13:BackroomsCopyCourt();break;case 14:Parking();break;
                case 15:PhoneCourt();break;case 16:Arcade();break;case 17:BentPool();break;
                case 18:IndoorGarden();break;case 19:BackroomsPillarHall();break;
            }
            if(index!=11 && index!=0 && index!=2 && index!=3 && index!=13 && index!=19)ExtraProps(index);
            FloorArrow(new Vector3(0,.02f,2),"Paint_Teal");FloorArrow(new Vector3(0,.02f,length-3),"Paint_Teal");
            ApplyBackroomsIdentity(root, index);
            ExpandRoomFootprint(root, index);
            return root;
        }

        static GameObject Gate(string name,float z,float width)
        {
            var go=Group(name,gameplay);go.transform.localPosition=new Vector3(0,0,z);
            var collider=go.AddComponent<BoxCollider>();collider.center=new Vector3(0,1.3f,0);collider.size=new Vector3(8,2.6f,.3f);
            for(int i=-7;i<=7;i++) Box("Closed shutter slat",new Vector3(i*.5f,1.2f,0),new Vector3(.065f,2.4f,.08f),"Metal",go.transform,false);
            Box("Gate caution stripe",new Vector3(0,1.15f,0),new Vector3(7.8f,.12f,.1f),"Light_Red",go.transform,false);
            return go;
        }

        static void Shell(string floor,string wall,float width=20,float length=28,float height=4.6f)
        {
            Box("Continuous walkable floor",new Vector3(0,-.16f,length*.5f),new Vector3(width,.32f,length),floor);
            // The near side is cut down for the actual isometric camera; the far side retains enclosure.
            Box("West wall / camera cutaway",new Vector3(-width*.5f+.15f,.52f,length*.5f),new Vector3(.3f,1.04f,length),wall);
            Box("East wall / full height",new Vector3(width*.5f-.15f,height*.5f,length*.5f),new Vector3(.3f,height,length),wall);
            float wing=(width-8)*.5f;
            foreach(int side in new[]{-1,1})
            {
                Box("Entry wall wing",new Vector3(side*(4+wing*.5f),.6f,.15f),new Vector3(wing,1.2f,.3f),wall);
                Box("Exit wall wing",new Vector3(side*(4+wing*.5f),height*.5f,length-.15f),new Vector3(wing,height,.3f),wall);
                Box("Continuous skirting",new Vector3(side*(width*.5f-.32f),.11f,length*.5f),new Vector3(.08f,.22f,length),"Paint_Teal");
                Box("Threshold jamb",new Vector3(side*4.05f,2.1f,length-.2f),new Vector3(.17f,4.2f,.44f),"Porcelain");
                Box("Threshold corner guard",new Vector3(side*4.05f,.65f,length-.46f),new Vector3(.2f,1.3f,.06f),"Metal");
            }
            Box("Exit lintel",new Vector3(0,4.1f,length-.15f),new Vector3(8.2f,.25f,.45f),"Porcelain");
            Box("Threshold inset",new Vector3(0,.008f,length-.45f),new Vector3(8,.018f,.45f),"Metal",null,false);
            for(float z=5;z<length-2;z+=6)
            {
                Box("East wall upper trim",new Vector3(width*.5f-.32f,3.15f,z),new Vector3(.07f,.11f,5.9f),"Porcelain",null,false);
                Box("East pilaster",new Vector3(width*.5f-.39f,2.2f,z-2.9f),new Vector3(.25f,4.4f,.24f),"Porcelain");
            }
            Sign("EXIT  /  CONTINUE",new Vector3(0,3.5f,length-.49f),4.2f,"Light_Cold",.23f);
        }

        static void Fluorescent(float x,float z,bool cold=false,float height=4.2f,bool dark=false)
        {
            var group=Group("Fluorescent "+(++detailCounter).ToString("00"),lighting).transform;
            group.localPosition=new Vector3(x,height,z);
            Box("Open ceiling frame",Vector3.zero,new Vector3(3.8f,.10f,.57f),"Porcelain",group,false);
            Box("Recess",new Vector3(0,-.085f,0),new Vector3(3.5f,.05f,.61f),"Rubber",group,false);
            for(int k=-1;k<=1;k+=2)Box("Fluorescent diffuser",new Vector3(0,-.13f,k*.19f),new Vector3(3.35f,.08f,.11f),dark?"Porcelain":cold?"Light_Cold":"Light_Warm",group,false);
            for(int k=-1;k<=1;k+=2)Box("Visible cutaway diffuser",new Vector3(0,.057f,k*.17f),new Vector3(3.35f,.018f,.11f),dark?"Porcelain":cold?"Light_Cold":"Light_Warm",group,false);
            for(int k=-1;k<=1;k+=2)Box("Suspension rod",new Vector3(k*1.5f,.48f,0),new Vector3(.026f,.85f,.026f),"Metal",group,false);
            if(!dark)
            {
                var light=Group("Pool of light",group).AddComponent<Light>();light.type=LightType.Point;light.color=cold?new Color(.74f,.93f,.90f):new Color(1f,.91f,.68f);light.intensity=cold?16f:19f;light.range=8.3f;light.shadows=LightShadows.None;light.transform.localPosition=new Vector3(0,-.4f,0);
            }
            foreach(var renderer in group.GetComponentsInChildren<Renderer>())renderer.shadowCastingMode=ShadowCastingMode.Off;
        }

        static void Sign(string text,Vector3 pos,float width,string material="Sign_Dark",float textSize=.28f,float yaw=0,Transform parent=null,float height=.67f)
        {
            var t=Group("Sign / "+text.Replace('\n',' '),parent?parent:props).transform;t.localPosition=pos;t.localRotation=Quaternion.Euler(0,yaw,0);
            Box("Sign panel",Vector3.zero,new Vector3(width,height,.12f),material,t,false);
            var go=Group("Lettering",t);go.transform.localPosition=new Vector3(0,0,-.072f);
            var mesh=go.AddComponent<TextMesh>();mesh.text=text;mesh.font=signageFont;mesh.fontSize=48;mesh.characterSize=textSize;mesh.anchor=TextAnchor.MiddleCenter;mesh.alignment=TextAlignment.Center;mesh.color=material.StartsWith("Light")?Ink:new Color(.82f,.87f,.76f);
            go.GetComponent<MeshRenderer>().sharedMaterial=signageMaterial;
            FitSignText(t);
            // High signs can be seen from either direction; a second front face avoids reversed lettering.
            if(width>2 && pos.y>2.7f)
            {
                var back=Object.Instantiate(go,t);back.name="Lettering / reverse side";back.transform.localPosition=new Vector3(0,0,.072f);back.transform.localRotation=Quaternion.Euler(0,180,0);
            }
            foreach(var renderer in t.GetComponentsInChildren<Renderer>())renderer.shadowCastingMode=ShadowCastingMode.Off;
        }

        static void FitSignText(Transform sign)
        {
            var panel=sign.Find("Sign panel");if(!panel)return;
            foreach(var tm in sign.GetComponentsInChildren<TextMesh>())
            {
                tm.transform.localScale=Vector3.one;
                var renderer=tm.GetComponent<MeshRenderer>();var b=renderer.localBounds;
                float scale=Mathf.Min(1,(panel.localScale.x-.22f)/Mathf.Max(.001f,b.size.x),(panel.localScale.y-.14f)/Mathf.Max(.001f,b.size.y));
                tm.transform.localScale=Vector3.one*Mathf.Max(.01f,scale);
            }
        }

        /// <summary>Visual review corrections only; no room architecture or user placement is rebuilt.</summary>
        public static void PolishExistingKit()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before applying authoring polish.");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save open scene changes before applying polish.");
            string original=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            PrepareMaterials();
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{RoomFolder,PropFolder}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    PolishHierarchy(root.transform);
                    var room=root.GetComponent<LiminalRoom>();
                    if(room&&room.roomId==RoomIds[17]&&room.enemySpawns.Length>0)room.enemySpawns[0].localPosition=new Vector3(-.7f,.05f,11.5f);
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
            }
            RefreshMeshyModels();
            foreach(string path in new[]{GalleryPath,PropGalleryPath,RunPath})
            {
                if(!File.Exists(path))continue;
                var scene=EditorSceneManager.OpenScene(path);
                foreach(var root in scene.GetRootGameObjects())PolishHierarchy(root.transform);
                RenderSettings.ambientLight=new Color(.25f,.28f,.245f);
                foreach(var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                    if(light.type==LightType.Directional){light.intensity=.32f;light.color=new Color(.92f,.95f,.86f);}
                EditorSceneManager.SaveScene(scene,path);
            }
            for(int i=0;i<4;i++)
            {
                var stage=AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(Root+"/Stages/Stage_"+(i+1).ToString("00")+".asset");
                if(stage){stage.ambientColor=i==0?new Color(.25f,.28f,.245f):new Color(.22f,.28f,.27f);EditorUtility.SetDirty(stage);}
            }
            AssetDatabase.SaveAssets();
            if(!string.IsNullOrEmpty(original)&&File.Exists(original))EditorSceneManager.OpenScene(original);
            Debug.Log("LIMINAL_VISUAL_POLISH_COMPLETE / signs fitted and depth-tested, fluorescent contrast, model scale and one spawn corrected.");
        }

        static void PolishHierarchy(Transform root)
        {
            if(root.GetComponentInChildren<TextMesh>(true))
            {
                var atlas=root.GetComponent<LiminalSignageAtlas>();if(!atlas)atlas=root.gameObject.AddComponent<LiminalSignageAtlas>();atlas.font=signageFont;atlas.material=signageMaterial;
            }
            foreach(var sign in root.GetComponentsInChildren<Transform>(true).Where(t=>t.Find("Sign panel")).ToArray())
            {
                var panel=sign.Find("Sign panel");
                if(sign.name.Contains("NOW SERVING"))panel.localScale=new Vector3(panel.localScale.x,.27f,panel.localScale.z);
                if(sign.name.Contains("DEPARTED"))panel.localScale=new Vector3(panel.localScale.x,.46f,panel.localScale.z);
                foreach(var tm in sign.GetComponentsInChildren<TextMesh>()){tm.GetComponent<MeshRenderer>().sharedMaterial=signageMaterial;tm.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;}
                FitSignText(sign);
                var front=sign.Find("Lettering");
                if(front && panel.localScale.x>2 && sign.localPosition.y>2.7f && !sign.Find("Lettering / reverse side"))
                {var back=Object.Instantiate(front.gameObject,sign).transform;back.name="Lettering / reverse side";back.localPosition=new Vector3(0,0,.072f);back.localRotation=Quaternion.Euler(0,180,0);}
                panel.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            }
            foreach(var fixture in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Fluorescent ")).ToArray())
            {
                var frame=fixture.Find("Open ceiling frame");if(frame){frame.GetComponent<Renderer>().sharedMaterial=Mats["Porcelain"];frame.localScale=new Vector3(3.8f,.10f,.57f);}
                var light=fixture.GetComponentInChildren<Light>();
                bool cold=light&&light.color.b>light.color.r*.85f;
                if(light){light.intensity=cold?16:19;light.color=cold?new Color(.74f,.93f,.90f):new Color(1f,.91f,.68f);light.range=8.3f;}
                if(!fixture.Find("Visible cutaway diffuser"))for(int k=-1;k<=1;k+=2)Box("Visible cutaway diffuser",new Vector3(0,.057f,k*.17f),new Vector3(3.35f,.018f,.11f),!light?"Porcelain":cold?"Light_Cold":"Light_Warm",fixture,false);
                foreach(var renderer in fixture.GetComponentsInChildren<Renderer>())renderer.shadowCastingMode=ShadowCastingMode.Off;
            }
        }

        static void FloorArrow(Vector3 p,string material)
        {
            Box("Faded route marking",p,new Vector3(.09f,.008f,1.1f),material,null,false);
            foreach(int side in new[]{-1,1}){var g=Box("Route arrow",p+new Vector3(side*.18f,0,.36f),new Vector3(.075f,.008f,.5f),material,null,false);g.transform.localRotation=Quaternion.Euler(0,side*-45,0);}
        }

        static void Column(float x,float z,string material="Porcelain",float height=4.4f)
        {
            Box("Square structural column",new Vector3(x,height*.5f,z),new Vector3(.82f,height,.82f),material);
            Box("Column kick plate",new Vector3(x,.22f,z),new Vector3(.89f,.44f,.89f),"Paint_Teal");
            Box("Column capital",new Vector3(x,height-.08f,z),new Vector3(1.07f,.18f,1.07f),material,null,false);
        }

        static void Door(float x,float z,string number,float yaw=0)
        {
            var t=Group("Administrative door "+number,architecture).transform;t.localPosition=new Vector3(x,0,z);t.localRotation=Quaternion.Euler(0,yaw,0);
            Box("Door frame",new Vector3(0,1.4f,0),new Vector3(1.95f,2.8f,.18f),"Porcelain",t);
            Box("Sealed door",new Vector3(0,1.35f,-.12f),new Vector3(1.68f,2.63f,.11f),"Paint_Teal",t);
            Box("Wired glass",new Vector3(0,1.91f,-.19f),new Vector3(.66f,.64f,.02f),"Glass_Dark",t,false);
            Box("Push plate",new Vector3(.57f,1.12f,-.20f),new Vector3(.12f,.34f,.026f),"Chrome",t,false);
            Box("Kick plate",new Vector3(0,.22f,-.20f),new Vector3(1.50f,.30f,.025f),"Metal",t,false);
            Sign(number,new Vector3(0,3.04f,0),1,"Sign_Dark",.27f,0,t);
        }

        static void Arrival(bool lastBus)
        {
            Shell("Terrazzo","Plaster_Warm");
            for(int side=-1;side<=1;side+=2)
            {
                for(int row=0;row<3;row++) Prop("waiting_bench",new Vector3(side*6.3f,0,8+row*5),side==1?90:270);
                Column(side*7.9f,5);Column(side*7.9f,23);
            }
            Prop("vending_machine",new Vector3(8.7f,0,24),90);
            Prop("directory_kiosk",new Vector3(-6.8f,0,23),15);
            Prop("planter",new Vector3(-8.5f,0,3.5f));Prop("planter",new Vector3(8.5f,0,3.5f));
            for(int i=0;i<3;i++)Fluorescent(0,6+i*8,false,4.5f,i==2&&lastBus);
            Sign(lastBus?"LAST BUS   -- : --":"ARRIVALS   /   TAKE A NUMBER",new Vector3(0,3.35f,6),6.8f,"Sign_Dark",.25f);
            var board=Group("Ticket information / permanently waiting",props).transform;board.localPosition=new Vector3(7.9f,2.4f,16);board.localRotation=Quaternion.Euler(0,90,0);
            Box("Information cabinet",Vector3.zero,new Vector3(4,.96f,.22f),"Sign_Dark",board,false);
            for(int i=0;i<3;i++)Sign("NOW SERVING     004",new Vector3(0,.3f-i*.3f,-.16f),3.8f,"Sign_Dark",.16f,0,board,.27f);
            if(lastBus)
            {
                // One chair faces the wall; its neighbours all face the route.
                Prop("plastic_chair",new Vector3(-8.7f,0,20.7f),90);
                Box("Unoccupied ticket booth",new Vector3(6.9f,.63f,26),new Vector3(4.6f,1.26f,1.25f),"Wood");
                Sign("BACK IN 5 MINUTES",new Vector3(6.9f,1.45f,25.30f),3.7f,"Seat_White",.18f);
            }
            else {Prop("reception_desk",new Vector3(-6.9f,0,26),0);Sign("PLEASE WAIT",new Vector3(-6.9f,2.8f,27.55f),4.6f,"Sign_Dark",.25f);}
        }

        static void Office(bool court)
        {
            Shell("OfficeCarpet","Plaster_Warm");
            if(!court)
            {
                // Repeated shallow bays make a long administrative route; the full central lane remains open.
                for(int i=0;i<4;i++)
                {
                    float z=5+i*5.5f;
                    Door(9.65f,z,"04",90);Door(-9.65f,z,"04",270);
                    foreach(int side in new[]{-1,1})Box("Repeated corridor nib",new Vector3(side*7.1f,1.1f,z+1.8f),new Vector3(5.5f,2.2f,.22f),"Plaster_Warm");
                    Fluorescent(0,z,false,3.8f,i==2);
                    Box("Low suspended cross rail",new Vector3(0,4.15f,z+1.9f),new Vector3(19.5f,.10f,.14f),"Metal",null,false);
                }
                Prop("janitor_cart",new Vector3(6.4f,0,19.7f),90);Prop("lockers",new Vector3(-7.2f,0,24.8f));
                Sign("ADMINISTRATION   04 - 04",new Vector3(0,3.03f,3.8f),6,"Sign_Dark",.24f);
            }
            else
            {
                for(int side=-1;side<=1;side+=2)for(int row=0;row<3;row++)
                {
                    var p=new Vector3(side*7,0,6+row*7);
                    OfficeDesk(p,side==1?90:270,row==2&&side==-1);
                    if(row<2)Box("Low fabric partition",p+new Vector3(0,.73f,2.65f),new Vector3(5.6f,1.46f,.12f),"OfficeCarpet");
                }
                for(int z=6;z<27;z+=7){Fluorescent(-5.8f,z);Fluorescent(5.8f,z,false,4.2f,z==20);}
                for(int x=-3;x<=3;x+=6)Column(x,24.5f);
                Prop("planter",new Vector3(-8.8f,0,25.5f));Prop("directory_kiosk",new Vector3(7.1f,0,25.8f));
                Sign("NIGHT SHIFT   /   LEVEL 04",new Vector3(0,3.45f,26.9f),6.7f,"Sign_Dark",.22f);
            }
        }

        static void OfficeDesk(Vector3 position,float yaw,bool wrong)
        {
            var t=Group(wrong?"Workstation / chair facing empty wall":"Abandoned workstation",props).transform;t.localPosition=position;t.localRotation=Quaternion.Euler(0,yaw,0);
            Box("Laminate desktop",new Vector3(0,.78f,0),new Vector3(2.7f,.10f,1.14f),"Wood",t);
            foreach(int s in new[]{-1,1})Box("Desk pedestal",new Vector3(s*.98f,.37f,0),new Vector3(.56f,.74f,1.06f),"Porcelain",t);
            Box("CRT shell",new Vector3(0,1.16f,.18f),new Vector3(.69f,.64f,.6f),"Porcelain",t,false);
            Box("Unlit screen",new Vector3(0,1.17f,-.135f),new Vector3(.55f,.42f,.035f),"Glass_Dark",t,false);
            Box("Keyboard",new Vector3(0,.857f,-.38f),new Vector3(.73f,.055f,.22f),"Seat_White",t,false);
            var chair=Group("Chair",t).transform;chair.localPosition=new Vector3(0,0,-1.1f);chair.localRotation=Quaternion.Euler(0,wrong?0:180,0);ProxyChair(chair,"Seat_Teal");
            Box("Single sheet left on desk",new Vector3(.9f,.84f,-.12f),new Vector3(.35f,.005f,.48f),"Seat_White",t,false);
        }

        static void Pool(bool dryFountain)
        {
            Shell("PoolTiles","Plaster_Cold",20,28,5.3f);
            foreach(int side in new[]{-1,1})
            {
                Basin(new Vector3(side*6.35f,0,14),new Vector2(5.0f,dryFountain?12:19));
                for(int z=5;z<26;z+=6)Column(side*8.8f,z,"PoolTiles",5.0f);
                Box("Continuous pool coping",new Vector3(side*3.72f,.09f,14),new Vector3(.35f,.18f,dryFountain?12.8f:19.8f),"Porcelain");
                if(!dryFountain)PoolLadder(side*3.8f,17,side);
            }
            for(int z=5;z<28;z+=9)Fluorescent(0,z,true,4.8f,z==14&&dryFountain);
            Sign(dryFountain?"FOUNTAIN COURT":"REFLECTION HALL",new Vector3(0,3.7f,4.5f),5.8f,"Sign_Dark",.27f);
            Sign("DEPTH   0.00 m",new Vector3(8.9f,2.25f,14),3.5f,"Porcelain",.24f,90);
            if(dryFountain)
            {
                foreach(int side in new[]{-1,1})
                {
                    Shape("Silent fountain bowl",PrimitiveType.Cylinder,new Vector3(side*6.3f,.7f,14),new Vector3(3.25f,.20f,3.25f),"Porcelain",props,true);
                    Shape("Fountain pedestal",PrimitiveType.Cylinder,new Vector3(side*6.3f,.28f,14),new Vector3(.7f,.28f,.7f),"Porcelain",props,true);
                    Shape("Water without running taps",PrimitiveType.Cylinder,new Vector3(side*6.3f,.912f,14),new Vector3(2.95f,.012f,2.95f),"Water");
                    Prop("plastic_chair",new Vector3(side*6,0,4),side==1?0:180);
                }
                Prop("janitor_cart",new Vector3(7.3f,0,24),90);
            }
            else
            {
                Prop("plastic_chair",new Vector3(-6.4f,0,2.8f),180);
                Prop("lockers",new Vector3(6.7f,0,26),0);
                for(int i=0;i<3;i++)Box("Walled-up high window",new Vector3(9.68f,3.75f,7+i*7),new Vector3(.06f,1.65f,3.6f),"Light_Cold",null,false);
            }
        }

        static void Basin(Vector3 center,Vector2 size)
        {
            Box("Shallow reflecting pool / walkable",center+Vector3.up*.028f,new Vector3(size.x,.025f,size.y),"Water",null,false);
            foreach(int s in new[]{-1,1})
            {
                Box("Pool short coping",center+new Vector3(0,.09f,s*(size.y*.5f+.13f)),new Vector3(size.x+.5f,.18f,.28f),"Porcelain");
                Box("Pool outer coping",center+new Vector3(s*(size.x*.5f+.13f),.09f,0),new Vector3(.28f,.18f,size.y),"Porcelain");
            }
            for(float z=-size.y*.5f+1;z<size.y*.5f;z+=2)Box("Submerged lane stripe",center+new Vector3(0,.048f,z),new Vector3(.11f,.007f,.9f),"Paint_Teal",null,false);
        }

        static void PoolLadder(float x,float z,int side)
        {
            // Keep the manufactured object connected when room spacing changes.
            Prop("pool_ladder",new Vector3(x,0,z),side>0?270:90);
        }

        static void Mall(bool wrongChairs)
        {
            Shell("MallTiles","Plaster_Warm",20,28,5.1f);
            for(int s=-1;s<=1;s+=2)
            {
                for(int row=0;row<3;row++)
                {
                    float z=6+row*7;
                    var p=new Vector3(s*6.7f,0,z);
                    if(wrongChairs)
                    {
                        // Formal repetition is interrupted by one chair turned away from every table.
                        for(int k=0;k<3;k++)Prop("plastic_chair",p+new Vector3((k-1)*1.35f,0,0),k==2&&row==1&&s==-1?180:0);
                    }
                    else CafeTable(p,row==1&&s==1);
                }
                Shutter(s*7,26,s==1?"NOODLES":"FRESH DAILY");
                Column(s*8.8f,3,"Porcelain",4.8f);
            }
            if(wrongChairs){CafeTable(new Vector3(6.5f,0,23),true);Prop("directory_kiosk",new Vector3(-6.7f,0,23),15);}
            Prop("vending_machine",new Vector3(8.7f,0,14),90);Prop("planter",new Vector3(-8.7f,0,25));
            for(int z=5;z<27;z+=8){Fluorescent(0,z,false,4.6f);}
            Sign(wrongChairs?"SEATING AREA   /   240 SEATS":"FOOD COURT   /   OPEN 24 HOURS",new Vector3(0,3.55f,5),7.5f,"Sign_Dark",.24f);
            for(int x=-8;x<10;x+=2)Box("Decorative checker border",new Vector3(x,.018f,3),new Vector3(.9f,.012f,.8f),"Paint_Mustard",null,false);
        }

        static void CafeTable(Vector3 p,bool empty)
        {
            var t=Group(empty?"Dining table / the missing guest":"Food court table",props).transform;t.localPosition=p;
            Shape("Round laminate top",PrimitiveType.Cylinder,new Vector3(0,.82f,0),new Vector3(1.7f,.065f,1.7f),"Seat_White",t,true);
            Shape("Table pedestal",PrimitiveType.Cylinder,new Vector3(0,.39f,0),new Vector3(.15f,.39f,.15f),"Chrome",t,true);
            Shape("Table weighted foot",PrimitiveType.Cylinder,new Vector3(0,.07f,0),new Vector3(.73f,.05f,.73f),"Metal",t,false);
            foreach(int s in new[]{-1,1})Prop("plastic_chair",p+new Vector3(s*1.35f,0,0),s==-1?270:90);
            if(!empty){Prop("plastic_chair",p+new Vector3(0,0,-1.3f),180);Prop("plastic_chair",p+new Vector3(0,0,1.3f),0);}
        }

        static void Shutter(float x,float z,string label)
        {
            var t=Group("Closed shop / "+label,architecture).transform;t.localPosition=new Vector3(x,0,z);
            Box("Shop opening",new Vector3(0,1.5f,0),new Vector3(5.5f,3,.45f),"Rubber",t);
            for(int i=0;i<19;i++)Box("Roller shutter slat",new Vector3(0,.1f+i*.151f,-.27f),new Vector3(5.2f,.126f,.055f),"Metal",t,false);
            Box("Service counter",new Vector3(0,1.02f,-.62f),new Vector3(5.7f,.14f,1.04f),"Porcelain",t);
            Sign(label,new Vector3(0,3.42f,-.25f),5.6f,"Paint_Mustard",.34f,0,t);
        }

        static void Transit(bool platform)
        {
            Shell("Terrazzo","Plaster_Cold",20,28,5.6f);
            if(platform)
            {
                foreach(int s in new[]{-1,1})
                {
                    Box("Recessed track illusion / sealed",new Vector3(s*7.0f,.03f,14),new Vector3(4.8f,.055f,22),"Rubber");
                    for(int i=0;i<20;i++)Box("Rail sleeper",new Vector3(s*7f,.09f,4+i),new Vector3(4.35f,.12f,.23f),"Wood",null,false);
                    foreach(float offset in new[]{-1.25f,1.25f})Box("Rail",new Vector3(s*7f+offset,.2f,14),new Vector3(.13f,.17f,22),"Metal",null,false);
                    Box("Platform edge / low safety curb",new Vector3(s*4.5f,.17f,14),new Vector3(.25f,.34f,23),"Porcelain");
                    for(int i=0;i<23;i++)Box("Tactile safety strip",new Vector3(s*4.12f,.025f,3+i),new Vector3(.34f,.035f,.83f),"Paint_Mustard",null,false);
                }
                Prop("waiting_bench",new Vector3(-2.7f,0,7),0);Prop("directory_kiosk",new Vector3(2.9f,0,24),0);
                Sign("PLATFORM 00   /   -- : --",new Vector3(0,3.4f,12),6.8f,"Sign_Dark",.31f);
            }
            else
            {
                BaggageBelt(new Vector3(-6.65f,0,14));BaggageBelt(new Vector3(6.65f,0,14));
                Sign("BAGGAGE CLAIM   00",new Vector3(0,3.6f,6),6.3f,"Sign_Dark",.3f);
                Prop("directory_kiosk",new Vector3(-6.7f,0,3));Prop("waiting_bench",new Vector3(6.7f,0,24),0);
            }
            foreach(int s in new[]{-1,1})for(int z=6;z<27;z+=9)Column(s*8.9f,z,"Porcelain",5.2f);
            for(int z=5;z<27;z+=8)Fluorescent(0,z,true,5.1f,z==21);
        }

        static void BaggageBelt(Vector3 p)
        {
            var t=Group("Baggage carousel / no luggage",props).transform;t.localPosition=p;
            Box("Raised carousel base",new Vector3(0,.32f,0),new Vector3(4.6f,.64f,12.8f),"Metal",t);
            Box("Dark conveyor",new Vector3(0,.67f,0),new Vector3(4.4f,.08f,12.5f),"Rubber",t,false);
            Box("Carousel inaccessible center",new Vector3(0,.81f,0),new Vector3(2.2f,.23f,10.4f),"Porcelain",t);
            for(int i=0;i<27;i++)foreach(int s in new[]{-1,1})Box("Individual conveyor slat",new Vector3(s*1.7f,.723f,-5.95f+i*.44f),new Vector3(1.08f,.025f,.37f),i%4==0?"Metal":"Rubber",t,false);
            foreach(int s in new[]{-1,1})Box("Polished belt rim",new Vector3(s*2.23f,.73f,0),new Vector3(.09f,.09f,12.7f),"Chrome",t,false);
            Sign("00",new Vector3(0,2.1f,0),1.3f,"Sign_Dark",.55f,0,t);
            Shape("Carousel number pole",PrimitiveType.Cylinder,new Vector3(0,1.4f,.1f),new Vector3(.05f,.64f,.05f),"Chrome",t,false);
        }

        static void Service()
        {
            Shell("Concrete","Plaster_Cold",20,28,4.2f);
            foreach(int s in new[]{-1,1})
            {
                for(int row=0;row<3;row++)Prop("lockers",new Vector3(s*7.7f,0,6+row*6.5f),s<0?270:90);
                for(int z=2;z<27;z+=4)
                {
                    Box("Service wall buttress",new Vector3(s*5.2f,1.85f,z),new Vector3(.3f,3.7f,.4f),"Concrete");
                    Box("Overhead service pipe",new Vector3(s*5.15f,3.6f,z),new Vector3(.16f,.16f,4.05f),"Metal",null,false);
                }
            }
            Prop("janitor_cart",new Vector3(6.5f,0,23),110);Prop("vending_machine",new Vector3(-7.8f,0,25));
            for(int z=5;z<28;z+=7)Fluorescent(0,z,true,3.8f,z==12);
            Sign("SERVICE ACCESS",new Vector3(0,3.25f,4),5.5f,"Sign_Dark",.28f);
            Sign("OUTSIDE",new Vector3(0,2.9f,27.51f),3.9f,"Light_Warm",.35f);
            foreach(int s in new[]{-1,1})for(int i=0;i<5;i++)Box("Caution floor band",new Vector3(s*(4.1f+i*.38f),.018f,24.5f),new Vector3(.23f,.018f,1.45f),"Paint_Mustard",null,false);
        }

        static void Boss()
        {
            Shell("PoolTiles","Plaster_Cold",28,36,7);
            foreach(int s in new[]{-1,1})
            {
                Basin(new Vector3(s*10.2f,0,20),new Vector2(4.5f,25));
                for(int z=6;z<35;z+=7)Column(s*12.9f,z,"Porcelain",6.8f);
                Prop("waiting_bench",new Vector3(s*5.8f,0,5),s<0?270:90);
                Prop("planter",new Vector3(s*11.5f,0,3));
                Box("Monumental information wall",new Vector3(s*9.2f,4.2f,35.52f),new Vector3(8,3.3f,.12f),"Sign_Dark",null,false);
                for(int row=0;row<5;row++)Sign("DEPARTED     -- : --",new Vector3(s*9.2f,5.3f-row*.52f,35.40f),7.2f,"Sign_Dark",.27f,0,null,.46f);
            }
            Prop("reception_desk",new Vector3(0,0,30.8f));
            Sign("ALL DEPARTURES",new Vector3(0,5.8f,30.8f),9,"Sign_Dark",.50f);
            Sign("PLEASE WAIT FOR THE ATTENDANT",new Vector3(0,2.4f,32.1f),9,"Sign_Dark",.28f);
            for(int z=6;z<33;z+=8){Fluorescent(-4,z,true,6);Fluorescent(4,z,true,6,z==22);}
            // Concentric inlaid route marks make the large empty floor read as a final gathering place.
            foreach(float radius in new[]{5.0f,6.2f})for(int i=0;i<48;i++)
            {
                float a=i*Mathf.PI*2/48;var go=Box("Concourse radial inlay",new Vector3(Mathf.Sin(a)*radius,.015f,21+Mathf.Cos(a)*radius),new Vector3(.10f,.014f,.35f),"Paint_Teal",null,false);go.transform.localRotation=Quaternion.Euler(0,a*Mathf.Rad2Deg,0);
            }
            Box("Arrival number / empty destination",new Vector3(0,.016f,12),new Vector3(4,.014f,.08f),"Paint_Mustard",null,false);
        }

        static void Laundry()
        {
            Shell("MallTiles","Plaster_Cold",20,28,4.1f);
            // Two strict rows, then an L-shaped return that breaks the expected repetition.
            for(int row=0;row<5;row++)
            {
                Prop("laundry_washer",new Vector3(-8.1f,0,5+row*4.1f),270);
                Prop("laundry_dryer",new Vector3(8.1f,0,5+row*4.1f),90);
                Box("Laundry folding counter",new Vector3(-5.8f,.92f,5+row*4.1f),new Vector3(1.25f,.12f,2.65f),"Porcelain");
            }
            for(int i=0;i<3;i++)Prop("laundry_washer",new Vector3(-7.8f+i*1.45f,0,25.8f));
            Prop("waiting_bench",new Vector3(5.5f,0,13),90);Prop("plastic_chair",new Vector3(-5.2f,0,25.9f),180);
            Prop("vending_machine",new Vector3(7.7f,0,25.4f));Prop("caution_sign",new Vector3(-4.6f,0,3.5f),20);
            for(int z=5;z<28;z+=7)Fluorescent(0,z,true,3.8f,z==19);
            Sign("WASH   /   DRY   /   REPEAT",new Vector3(0,3.0f,4),7,"Sign_Dark",.29f);
            Sign("TIME REMAINING   00:01",new Vector3(7.6f,2.6f,27.53f),4.4f,"Light_Cold",.21f);
        }

        static void CopyWaiting()
        {
            Shell("OfficeCarpet","Plaster_Warm");
            // Waiting furniture faces equipment rather than a person or a service desk.
            for(int row=0;row<3;row++)
            {
                float z=6+row*7;
                Prop("photocopier",new Vector3(8.1f,0,z),90);
                Prop("waiting_bench",new Vector3(5.0f,0,z),270);
                Box("Copy alcove wall",new Vector3(6.9f,1.35f,z+2.8f),new Vector3(5.3f,2.7f,.20f),"Plaster_Warm");
                Door(-9.65f,z,"COPY 04",270);
                Prop("water_dispenser",new Vector3(-7.9f,0,z),270);
                Box("Paper carton",new Vector3(-6.6f,.3f,z),new Vector3(.62f,.6f,.8f),"Wood",props);
                Fluorescent(0,z,false,3.9f,row==1);
            }
            Prop("wall_clock",new Vector3(7.8f,2.9f,27.55f));
            Sign("PLEASE WAIT FOR YOUR COPY",new Vector3(0,3.35f,3.5f),7.8f,"Sign_Dark",.27f);
            Prop("photocopier",new Vector3(-6.7f,0,25.8f),180);
        }

        static void Parking()
        {
            Shell("Concrete","Concrete",20,28,3.7f);
            for(int side=-1;side<=1;side+=2)for(int row=0;row<3;row++)
            {
                float z=5+row*8;
                Column(side*6.4f,z,"Concrete",3.55f);
                Box("Oversized parking column base",new Vector3(side*6.4f,.4f,z),new Vector3(1.4f,.8f,1.4f),"Paint_Mustard");
                Sign("04",new Vector3(side*6.4f,2.0f,z-.77f),1.2f,"Paint_Teal",.35f);
                Box("Empty bay line",new Vector3(side*6.5f,.018f,z+3.3f),new Vector3(5.9f,.015f,.10f),"Seat_White",null,false);
                Box("Concrete wheel stop",new Vector3(side*8.7f,.13f,z+1.4f),new Vector3(.30f,.26f,2.1f),"Concrete");
                Box("Low ceiling beam / open cutaway",new Vector3(0,3.7f,z),new Vector3(19.8f,.25f,.43f),"Concrete",null,false);
                if(side==1)Fluorescent(0,z+2,true,3.5f,row==2);
            }
            Prop("folding_barrier",new Vector3(7.0f,0,24.2f),90);Prop("ticket_machine",new Vector3(-7,0,24.8f),10);
            Prop("ventilation_unit",new Vector3(9.15f,2.1f,13),90);
            Sign("P4  /  NO VEHICLES BEYOND THIS POINT",new Vector3(0,2.8f,26.7f),8,"Sign_Dark",.23f);
        }

        static void PhoneCourt()
        {
            Shell("Terrazzo","Plaster_Warm",20,28,5.4f);
            foreach(int side in new[]{-1,1})for(int i=0;i<4;i++)
            {
                float z=5+i*6;Prop("payphone",new Vector3(side*8.4f,0,z),side<0?270:90);
                Box("Telephone acoustic wing",new Vector3(side*8.2f,1.58f,z+1.0f),new Vector3(2.8f,1.65f,.10f),"Wood");
            }
            // A single bench is isolated at an unreasonable distance from the bank of telephones.
            Prop("waiting_bench",new Vector3(0,0,17.0f),180);
            for(int z=5;z<28;z+=8)Fluorescent(0,z,false,4.9f,z==13);
            Sign("PUBLIC TELEPHONES",new Vector3(0,3.65f,4.5f),6.8f,"Sign_Dark",.31f);
            Prop("wall_clock",new Vector3(7.0f,3.1f,27.5f));
            Prop("trash_bin",new Vector3(-7.9f,0,26));
        }

        static void Arcade()
        {
            Shell("MallTiles","Plaster_Warm",20,28,5.4f);
            for(int row=0;row<3;row++)
            {
                float z=5.5f+row*7.5f;
                foreach(int side in new[]{-1,1})
                {
                    var shop=Group("Arcade shallow shop bay",architecture).transform;shop.localPosition=new Vector3(side*8.8f,0,z);shop.localRotation=Quaternion.Euler(0,side<0?270:90,0);
                    Box("Opaque storefront",new Vector3(0,1.6f,0),new Vector3(5.5f,3.2f,.25f),"Glass_Dark",shop);
                    for(int n=0;n<18;n++)Box("Security screen rail",new Vector3(0,.15f+n*.165f,-.19f),new Vector3(5.5f,.022f,.028f),"Metal",shop,false);
                    Sign(row==1?"OPENING SOON":"OPENING SOON",new Vector3(0,3.6f,0),5.4f,"Paint_Mustard",.32f,0,shop);
                    Column(side*5.8f,z-3,"Porcelain",4.9f);
                }
                Box("Arcade ceiling ring",new Vector3(0,5.05f,z-3),new Vector3(12,.18f,.65f),"Porcelain",null,false);
                Fluorescent(0,z,false,4.8f,row==2);
            }
            Prop("foodcourt_table",new Vector3(-6.4f,0,25));Prop("directory_kiosk",new Vector3(6.7f,0,25));
            Sign("SHOPPING ARCADE   /   LEVEL 04",new Vector3(0,3.5f,3.5f),8,"Sign_Dark",.25f);
        }

        static void BentPool()
        {
            Shell("PoolTiles","PoolTiles",20,28,5.5f);
            Basin(new Vector3(-5.8f,0,12.4f),new Vector2(5.5f,16.0f));
            Basin(new Vector3(6.1f,0,19),new Vector2(4.7f,11));
            // Round columns and displaced half walls reveal the next space a little at a time.
            for(int i=0;i<4;i++)
            {
                float z=5+i*6;
                Shape("Round tiled column",PrimitiveType.Cylinder,new Vector3(-2.7f,2.5f,z),new Vector3(1.0f,2.5f,1.0f),"PoolTiles",architecture,true);
                Shape("Round tiled column",PrimitiveType.Cylinder,new Vector3(8.9f,2.5f,z),new Vector3(1.0f,2.5f,1.0f),"PoolTiles",architecture,true);
                Box("Offset colonnade lintel",new Vector3(3.1f,4.9f,z),new Vector3(12.5f,.26f,1.0f),"PoolTiles",null,false);
            }
            Box("Bent passage screen",new Vector3(4.7f,1.0f,9),new Vector3(8.5f,2.0f,.3f),"PoolTiles");
            Box("Pool alcove screen",new Vector3(-5.5f,1.0f,23.5f),new Vector3(7.8f,2,.3f),"PoolTiles");
            Prop("pool_ladder",new Vector3(-2.6f,0,13),90);Prop("plastic_chair",new Vector3(-7.5f,0,25.4f),180);
            Prop("caution_sign",new Vector3(6.5f,0,6),350);
            Fluorescent(3.5f,6,true,4.9f);Fluorescent(2.2f,16,true,4.9f,true);Fluorescent(0,25,true,4.9f);
            Sign("REFLECTIONS   /   THIS WAY",new Vector3(0,3.6f,4),7,"Sign_Dark",.26f);
        }

        static void IndoorGarden()
        {
            Shell("Terrazzo","Plaster_Warm",20,28,6.0f);
            // Rigidly repeated artificial greenery surrounds a much larger empty central court.
            foreach(int side in new[]{-1,1})
            {
                Box("Indoor garden raised bed",new Vector3(side*7.0f,.38f,14),new Vector3(4,.76f,18),"Porcelain");
                Box("Planter soil strip",new Vector3(side*7.0f,.77f,14),new Vector3(3.7f,.045f,17.7f),"Soil",null,false);
                for(int i=0;i<5;i++)Prop("planter",new Vector3(side*7.0f,.82f,6+i*4));
                Prop("waiting_bench",new Vector3(side*6.4f,0,3.3f),180);
                Column(side*8.9f,24,"Porcelain",5.5f);
            }
            for(int z=5;z<28;z+=9)
            {
                Box("Empty skylight frame",new Vector3(0,5.5f,z),new Vector3(7,.10f,.12f),"Metal",null,false);
                Fluorescent(0,z,false,5.4f,z==14);
            }
            Prop("water_dispenser",new Vector3(7.7f,0,25));
            Sign("WINTER GARDEN   /   CLIMATE CONTROLLED",new Vector3(0,3.7f,5),8.5f,"Sign_Dark",.24f);
            Sign("PLEASE DO NOT WATER THE PLANTS",new Vector3(-6.4f,1.5f,24),5.6f,"Sign_Dark",.18f);
        }

        static void ChairArchive()
        {
            Shell("MallTiles","Plaster_Warm",20,28,5.0f);
            // A calm foreground gives the impossible accumulation one precise location to disturb.
            Prop("plastic_chair",new Vector3(-6.4f,0,5),0);Prop("plastic_chair",new Vector3(6.1f,0,8),170);
            Prop("foodcourt_table",new Vector3(-6.9f,0,15),0);
            for(int column=0;column<4;column++)for(int layer=0;layer<4;layer++)
                Prop("plastic_chair",new Vector3(5.6f+column*.83f,layer*.66f,23.4f+(column%2)*.6f),layer%2==0?0:180);
            for(int row=0;row<3;row++)Prop("plastic_chair",new Vector3(-7.3f,0,21+row*1.3f),90);
            Prop("folding_barrier",new Vector3(6.6f,0,21.2f));
            Prop("janitor_cart",new Vector3(8.0f,0,13),90);
            for(int z=5;z<27;z+=8)Fluorescent(0,z,false,4.4f,z==13);
            Sign("SEATING FOR ALL VISITORS",new Vector3(0,3.4f,4.4f),7,"Sign_Dark",.28f);
            Sign("CAPACITY   004",new Vector3(6.8f,3.8f,27.48f),4.5f,"Sign_Dark",.28f);
        }

        static void ExtraProps(int index)
        {
            // Distribute authored service details with a purpose instead of scattering debris.
            if(index<2){Prop("ticket_machine",new Vector3(5.1f,0,3.0f));Prop("wall_clock",new Vector3(-7.0f,3.2f,27.45f));}
            if(index==0){Prop("turnstile",new Vector3(-6.5f,0,4.4f),0);Prop("payphone",new Vector3(8.7f,0,11),90);}
            if(index==1)Prop("luggage_cart",new Vector3(-7.2f,0,25.5f));
            if(index==2||index==3)Prop("water_dispenser",new Vector3(8.0f,0,2.8f));
            if(index==4||index==5){Prop("pool_ladder",new Vector3(3.9f,0,9.0f),270);Prop("caution_sign",new Vector3(-6.9f,0,25.2f),25);}
            if(index==6||index==7){Prop("foodcourt_table",new Vector3(-6.4f,0,3.0f));Prop("trash_bin",new Vector3(8.7f,0,3));}
            if(index==8){Prop("luggage_cart",new Vector3(7.6f,0,3));Prop("luggage_cart",new Vector3(6.5f,0,3.25f));}
            if(index==9)Prop("ticket_machine",new Vector3(-7.8f,0,25.8f));
            if(index==10){Prop("ventilation_unit",new Vector3(9.0f,2.2f,13),90);Prop("folding_barrier",new Vector3(-6.5f,0,2.8f));}
            if(index%3==0)Prop("trash_bin",new Vector3(-8.8f,0,2.0f));
            if(index==13)Prop("fluorescent_fixture",new Vector3(-6.4f,3.2f,24.5f));
        }

        static readonly Dictionary<string,Vector3> PropSize=new Dictionary<string,Vector3>{
            {"waiting_bench",new Vector3(3.15f,1.1f,.9f)}, {"vending_machine",new Vector3(1.3f,2.3f,1.0f)},
            {"janitor_cart",new Vector3(1.0f,1.4f,1.45f)}, {"planter",new Vector3(1.15f,1.8f,1.15f)},
            {"plastic_chair",new Vector3(.65f,1.0f,.68f)}, {"directory_kiosk",new Vector3(1.2f,2.2f,.65f)},
            {"reception_desk",new Vector3(4.5f,1.2f,1.5f)}, {"lockers",new Vector3(2.7f,2.0f,.6f)},
            {"pool_ladder",new Vector3(.95f,1.45f,.85f)}, {"payphone",new Vector3(.85f,1.95f,.65f)},
            {"water_dispenser",new Vector3(.62f,1.52f,.6f)}, {"photocopier",new Vector3(1.5f,1.2f,1.0f)},
            {"luggage_cart",new Vector3(1.2f,1.85f,1.5f)}, {"ticket_machine",new Vector3(1.0f,1.9f,.65f)},
            {"turnstile",new Vector3(1.4f,1.0f,.85f)}, {"ventilation_unit",new Vector3(1.8f,1.1f,.65f)},
            {"laundry_washer",new Vector3(1.25f,1.22f,.94f)}, {"laundry_dryer",new Vector3(1.25f,2.35f,.94f)},
            {"trash_bin",new Vector3(.7f,1.0f,.7f)}, {"caution_sign",new Vector3(.65f,.9f,.55f)},
            {"wall_clock",new Vector3(.65f,.65f,.1f)}, {"fluorescent_fixture",new Vector3(2.1f,.22f,.5f)},
            {"foodcourt_table",new Vector3(2.6f,1.05f,2.6f)}, {"folding_barrier",new Vector3(2.7f,1.0f,.45f)},
            {"backrooms_workstation",new Vector3(3.3f,1.75f,1.9f)},
            {"poolroom_arch",new Vector3(4.2f,4.5f,1.15f)},
            {"industrial_fan",new Vector3(3.4f,3.4f,.7f)}
        };

        static void Prop(string key,Vector3 position,float yaw=0)
        {
            var holder=Group("MeshySlot__"+key,props).transform;holder.localPosition=position;holder.localRotation=Quaternion.Euler(0,yaw,0);
            var size=PropSize[key];var collider=holder.gameObject.AddComponent<BoxCollider>();collider.center=new Vector3(0,size.y*.5f,0);collider.size=size;
            if(TryPlaceMeshy(holder,key))return;
            var t=Group("Authored proxy / replace with Meshy asset",holder).transform;
            switch(key)
            {
                case "waiting_bench":ProxyBench(t);break;case "plastic_chair":ProxyChair(t,"Seat_Orange");break;
                case "planter":ProxyPlanter(t);break;case "vending_machine":ProxyVending(t);break;
                case "directory_kiosk":ProxyKiosk(t);break;case "lockers":ProxyLockers(t);break;
                case "reception_desk":ProxyReception(t);break;case "janitor_cart":ProxyCart(t);break;
                default:ProxyService(t,key);break;
            }
            if(key=="poolroom_arch")ConfigurePoolArchCollision(holder,size);
        }

        static void ProxyBench(Transform t)
        {
            foreach(int s in new[]{-1,1}){Box("Cast metal leg",new Vector3(s*1.12f,.28f,0),new Vector3(.10f,.55f,.65f),"Metal",t,false);Box("Bench armrest",new Vector3(s*1.52f,.78f,0),new Vector3(.07f,.09f,.76f),"Chrome",t,false);}
            Box("Under-seat beam",new Vector3(0,.39f,0),new Vector3(3.0f,.09f,.12f),"Chrome",t,false);
            for(int i=-1;i<=1;i++)
            {
                var seat=Box("Moulded waiting seat",new Vector3(i*1.02f,.52f,0),new Vector3(.91f,.13f,.71f),"Seat_Teal",t,false);
                var back=Box("Seat back",new Vector3(i*1.02f,.86f,.30f),new Vector3(.91f,.64f,.10f),"Seat_Teal",t,false);back.transform.localRotation=Quaternion.Euler(9,0,0);
                for(int j=-3;j<=3;j++)Box("Seat ventilation slit",new Vector3(i*1.02f+j*.098f,.89f,.237f),new Vector3(.019f,.30f,.008f),"Rubber",t,false);
            }
        }

        static void ProxyChair(Transform t,string material)
        {
            Box("Moulded plastic seat",new Vector3(0,.48f,0),new Vector3(.62f,.095f,.60f),material,t,false);
            var back=Box("Plastic backrest",new Vector3(0,.78f,.25f),new Vector3(.62f,.55f,.065f),material,t,false);back.transform.localRotation=Quaternion.Euler(7,0,0);
            for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)Box("Tubular chair leg",new Vector3(x*.24f,.22f,z*.23f),new Vector3(.039f,.46f,.039f),"Chrome",t,false);
            Box("Handhold inset",new Vector3(0,.925f,.207f),new Vector3(.25f,.055f,.018f),"Rubber",t,false);
        }

        static void ProxyPlanter(Transform t)
        {
            Shape("Ceramic planter",PrimitiveType.Cylinder,new Vector3(0,.37f,0),new Vector3(1.06f,.37f,1.06f),"Porcelain",t,false);
            Shape("Planter rim",PrimitiveType.Cylinder,new Vector3(0,.75f,0),new Vector3(1.16f,.065f,1.16f),"Paint_Teal",t,false);
            Shape("Dry soil",PrimitiveType.Cylinder,new Vector3(0,.824f,0),new Vector3(.99f,.015f,.99f),"Soil",t,false);
            for(int i=0;i<9;i++)
            {
                float angle=i*137.5f*Mathf.Deg2Rad;var stem=Shape("Artificial leaf",PrimitiveType.Capsule,new Vector3(Mathf.Cos(angle)*.19f,1.21f+(i%3)*.13f,Mathf.Sin(angle)*.19f),new Vector3(.14f,.48f,.27f),"Foliage",t,false);stem.transform.localRotation=Quaternion.Euler(20+i*2,i*137.5f,12);
            }
        }

        static void ProxyVending(Transform t)
        {
            Box("Vending cabinet",new Vector3(0,1.1f,0),new Vector3(1.25f,2.2f,.91f),"Paint_Teal",t,false);
            Box("Illuminated display",new Vector3(-.14f,1.30f,-.464f),new Vector3(.77f,1.16f,.025f),"Glass_Dark",t,false);
            for(int y=0;y<3;y++)for(int x=0;x<4;x++)
            {
                Shape("Unsold drink",PrimitiveType.Cylinder,new Vector3(-.41f+x*.18f,.91f+y*.33f,-.48f),new Vector3(.1f,.11f,.1f),y==1?"Seat_Orange":"Seat_White",t,false);
            }
            for(int i=0;i<4;i++)Box("Selection button",new Vector3(.44f,1.55f-i*.18f,-.474f),new Vector3(.13f,.09f,.035f),i==2?"Light_Cold":"Metal",t,false);
            Box("Collection slot",new Vector3(0,.38f,-.478f),new Vector3(.83f,.22f,.035f),"Rubber",t,false);
            Sign("REFRESH",new Vector3(0,2.015f,-.49f),1.13f,"Light_Warm",.16f,0,t);
        }

        static void ProxyKiosk(Transform t)
        {
            Box("Weighted directory foot",new Vector3(0,.08f,0),new Vector3(1.22f,.16f,.67f),"Metal",t,false);
            Box("Directory stem",new Vector3(0,.92f,.1f),new Vector3(.21f,1.8f,.2f),"Chrome",t,false);
            Box("Map cabinet",new Vector3(0,1.66f,0),new Vector3(1.13f,1.15f,.16f),"Paint_Teal",t,false);
            Box("Blank map",new Vector3(0,1.66f,-.09f),new Vector3(.95f,.92f,.018f),"Seat_White",t,false);
            for(int i=0;i<4;i++)Box("Floor plan line",new Vector3(-.23f+i*.15f,1.60f,-.108f),new Vector3(.045f,.52f,.006f),"Paint_Teal",t,false);
            Box("You are here",new Vector3(.15f,1.65f,-.12f),new Vector3(.08f,.08f,.02f),"Light_Red",t,false);
            Sign("YOU ARE HERE",new Vector3(0,2.30f,0),1.75f,"Sign_Dark",.14f,0,t);
        }

        static void ProxyLockers(Transform t)
        {
            Box("Locker bank",new Vector3(0,.98f,0),new Vector3(2.7f,1.96f,.58f),"Metal",t,false);
            for(int x=0;x<4;x++)
            {
                Box("Locker door",new Vector3(-1.015f+x*.675f,1,-.3f),new Vector3(.63f,1.85f,.035f),"Paint_Teal",t,false);
                Box("Door latch",new Vector3(-.83f+x*.675f,1.07f,-.331f),new Vector3(.05f,.16f,.035f),"Chrome",t,false);
                for(int j=0;j<4;j++)Box("Vent",new Vector3(-1.015f+x*.675f,1.57f+j*.07f,-.324f),new Vector3(.36f,.018f,.013f),"Rubber",t,false);
            }
        }

        static void ProxyReception(Transform t)
        {
            Box("Reception plinth",new Vector3(0,.12f,0),new Vector3(4.55f,.24f,1.48f),"Paint_Teal",t,false);
            Box("Curved-desk proxy front",new Vector3(0,.65f,0),new Vector3(4.4f,1.08f,1.34f),"Wood",t,false);
            Box("Terrazzo countertop",new Vector3(0,1.23f,0),new Vector3(4.6f,.12f,1.52f),"Porcelain",t,false);
            for(int i=-8;i<=8;i++)Box("Vertical timber slat",new Vector3(i*.24f,.64f,-.69f),new Vector3(.06f,.91f,.025f),"Paint_Mustard",t,false);
            Shape("Service bell",PrimitiveType.Sphere,new Vector3(.82f,1.36f,-.27f),new Vector3(.19f,.10f,.19f),"Chrome",t,false);
            Box("Idle terminal",new Vector3(-1.18f,1.52f,.20f),new Vector3(.73f,.48f,.32f),"Porcelain",t,false);
            Sign("INFORMATION",new Vector3(0,.78f,-.735f),2.7f,"Sign_Dark",.18f,0,t);
        }

        static void ProxyCart(Transform t)
        {
            Box("Cart base",new Vector3(0,.22f,0),new Vector3(.88f,.13f,1.3f),"Paint_Mustard",t,false);
            foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})Shape("Cart wheel",PrimitiveType.Sphere,new Vector3(x*.34f,.12f,z*.51f),new Vector3(.17f,.23f,.17f),"Rubber",t,false);
            Box("Cleaning bin",new Vector3(0,.58f,.26f),new Vector3(.65f,.66f,.61f),"Paint_Teal",t,false);
            Box("Cart shelf",new Vector3(0,.70f,-.35f),new Vector3(.82f,.09f,.54f),"Metal",t,false);
            foreach(int s in new[]{-1,1})Box("Push frame",new Vector3(s*.38f,.76f,.59f),new Vector3(.035f,1.12f,.035f),"Chrome",t,false);
            Box("Handle",new Vector3(0,1.30f,.59f),new Vector3(.78f,.04f,.04f),"Chrome",t,false);
            var mop=Box("Mop handle",new Vector3(.24f,1.0f,-.12f),new Vector3(.03f,1.7f,.03f),"Wood",t,false);mop.transform.localRotation=Quaternion.Euler(0,0,10);
            for(int i=0;i<3;i++)Shape("Cleaning bottle",PrimitiveType.Cylinder,new Vector3(-.23f+i*.2f,.90f,-.36f),new Vector3(.11f,.15f,.11f),i==1?"Seat_Orange":"Seat_White",t,false);
        }

        static void ProxyService(Transform t,string key)
        {
            var size=PropSize[key];
            if(key=="laundry_washer"||key=="laundry_dryer")
            {
                int count=key=="laundry_dryer"?2:1;
                for(int i=0;i<count;i++)
                {
                    float y=i*1.13f;Box("Enamel laundry cabinet",new Vector3(0,y+.60f,0),new Vector3(1.21f,1.15f,.9f),"Porcelain",t,false);
                    var rim=Shape("Round steel door rim",PrimitiveType.Cylinder,new Vector3(0,y+.55f,-.47f),new Vector3(.81f,.045f,.81f),"Chrome",t,false);rim.transform.localRotation=Quaternion.Euler(90,0,0);
                    var glass=Shape("Dark round washer window",PrimitiveType.Cylinder,new Vector3(0,y+.55f,-.525f),new Vector3(.64f,.020f,.64f),"Glass_Dark",t,false);glass.transform.localRotation=Quaternion.Euler(90,0,0);
                    Box("Control fascia",new Vector3(0,y+1.01f,-.465f),new Vector3(1.08f,.18f,.03f),"Paint_Teal",t,false);
                    Box("Timer display",new Vector3(.22f,y+1.01f,-.486f),new Vector3(.34f,.10f,.008f),"Light_Cold",t,false);
                }return;
            }
            if(key=="wall_clock")
            {
                var face=Shape("Clock face",PrimitiveType.Cylinder,new Vector3(0,.325f,0),new Vector3(.65f,.04f,.65f),"Porcelain",t,false);face.transform.localRotation=Quaternion.Euler(90,0,0);
                Box("Stopped hour hand",new Vector3(.065f,.325f,-.045f),new Vector3(.14f,.022f,.012f),"Sign_Dark",t,false);
                Box("Stopped minute hand",new Vector3(0,.42f,-.046f),new Vector3(.018f,.20f,.012f),"Sign_Dark",t,false);
                for(int i=0;i<12;i++){float a=i*Mathf.PI/6;Box("Clock tick",new Vector3(Mathf.Sin(a)*.267f,.325f+Mathf.Cos(a)*.267f,-.046f),new Vector3(.024f,.024f,.012f),"Sign_Dark",t,false);}return;
            }
            if(key=="foodcourt_table")
            {
                Shape("Food court tabletop",PrimitiveType.Cylinder,new Vector3(0,.80f,0),new Vector3(1.6f,.06f,1.6f),"Seat_White",t,false);
                Shape("Pedestal",PrimitiveType.Cylinder,new Vector3(0,.4f,0),new Vector3(.18f,.4f,.18f),"Chrome",t,false);
                foreach(int s in new[]{-1,1}){var chair=Group("Attached seat",t).transform;chair.localPosition=new Vector3(s*1.0f,0,0);chair.localRotation=Quaternion.Euler(0,s<0?270:90,0);ProxyChair(chair,"Seat_Orange");}return;
            }
            if(key=="pool_ladder"||key=="luggage_cart")
            {
                foreach(int s in new[]{-1,1})
                {
                    Shape("Tubular upright",PrimitiveType.Cylinder,new Vector3(s*size.x*.42f,size.y*.5f,0),new Vector3(.055f,size.y*.5f,.055f),"Chrome",t,false);
                    Box("Top handle",new Vector3(s*size.x*.42f,size.y,-.2f),new Vector3(.055f,.055f,.48f),"Chrome",t,false);
                }
                for(int i=0;i<(key=="pool_ladder"?4:2);i++)Box("Cross rail",new Vector3(0,.18f+i*.33f,0),new Vector3(size.x*.86f,.06f,.08f),"Chrome",t,false);
                if(key=="luggage_cart"){Box("Luggage platform",new Vector3(0,.17f,-.38f),new Vector3(size.x,.08f,1.35f),"Paint_Teal",t,false);foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})Shape("Caster",PrimitiveType.Sphere,new Vector3(x*.48f,.1f,-.38f+z*.51f),new Vector3(.14f,.2f,.14f),"Rubber",t,false);}return;
            }
            if(key=="caution_sign"||key=="folding_barrier")
            {
                foreach(int s in new[]{-1,1}){var panel=Box("Folding caution panel",new Vector3(0,size.y*.5f,s*.12f),new Vector3(size.x,size.y,.04f),"Paint_Mustard",t,false);panel.transform.localRotation=Quaternion.Euler(s*12,0,0);}
                Sign(key=="caution_sign"?"CAUTION":"STAFF ONLY",new Vector3(0,size.y*.60f,-.21f),size.x*.91f,"Sign_Dark",key=="caution_sign"?.10f:.20f,0,t);return;
            }
            if(key=="fluorescent_fixture")
            {Box("Fluorescent housing",new Vector3(0,.11f,0),size,"Porcelain",t,false);Box("Twin tube diffuser",new Vector3(0,.012f,0),new Vector3(size.x*.94f,.04f,.35f),"Light_Cold",t,false);return;}
            if(key=="ventilation_unit")
            {Box("Vent casing",size.y*.5f*Vector3.up,size,"Metal",t,false);for(int i=0;i<11;i++)Box("Vent louvre",new Vector3(0,.09f+i*.09f,-size.z*.51f),new Vector3(size.x*.9f,.034f,.10f),"Rubber",t,false);return;}
            if(key=="turnstile")
            {
                Box("Turnstile steel body",new Vector3(0,.57f,0),new Vector3(.51f,.96f,.8f),"Chrome",t,false);
                Box("Turnstile arm",new Vector3(.54f,.92f,0),new Vector3(1.1f,.055f,.055f),"Metal",t,false);
                Box("Reader",new Vector3(0,1.08f,-.08f),new Vector3(.33f,.08f,.41f),"Glass_Dark",t,false);return;
            }
            if(key=="trash_bin")
            {Shape("Enamel litter bin",PrimitiveType.Cylinder,new Vector3(0,.48f,0),new Vector3(.67f,.48f,.67f),"Paint_Teal",t,false);Shape("Bin opening",PrimitiveType.Cylinder,new Vector3(0,.965f,0),new Vector3(.5f,.01f,.5f),"Rubber",t,false);return;}
            Box("Service cabinet",new Vector3(0,size.y*.5f,0),size,key=="payphone"?"Paint_Mustard":"Porcelain",t,false);
            if(key=="water_dispenser")
            {Shape("Water bottle",PrimitiveType.Cylinder,new Vector3(0,1.58f,0),new Vector3(.38f,.29f,.38f),"Glass_Dark",t,false);Box("Tap recess",new Vector3(0,.86f,-.307f),new Vector3(.37f,.38f,.035f),"Rubber",t,false);}
            else if(key=="photocopier")
            {Box("Scanner lid",new Vector3(0,1.24f,0),new Vector3(1.38f,.10f,.93f),"Rubber",t,false);Box("Output slot",new Vector3(0,.72f,-.52f),new Vector3(1.16f,.14f,.03f),"Rubber",t,false);Box("Control pad",new Vector3(.5f,1.30f,-.31f),new Vector3(.36f,.06f,.27f),"Light_Cold",t,false);}
            else
            {
                Box("Display",new Vector3(0,size.y*.69f,-size.z*.51f),new Vector3(size.x*.63f,.37f,.04f),"Glass_Dark",t,false);
                for(int x=-1;x<=1;x++)for(int y=0;y<3;y++)Box("Button",new Vector3(x*.105f,size.y*.45f+y*.09f,-size.z*.53f),new Vector3(.073f,.048f,.025f),"Metal",t,false);
                if(key=="payphone")Box("Telephone receiver",new Vector3(-.28f,1.38f,-.38f),new Vector3(.13f,.56f,.12f),"Rubber",t,false);
            }
        }

        static GameObject FindMeshy(string key)
        {
            string folder=Root+"/Art/Meshy";if(!AssetDatabase.IsValidFolder(folder))return null;
            string normalized=key.Replace("_","").ToLowerInvariant();
            foreach(string guid in AssetDatabase.FindAssets("t:GameObject",new[]{folder}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);string name=Path.GetFileNameWithoutExtension(path).Replace("_","").Replace("-","").Replace(" ","").ToLowerInvariant();
                if(name==normalized || name.StartsWith(normalized)) {var go=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(go && go.GetComponentsInChildren<Renderer>(true).Length>0)return go;}
            }
            return null;
        }

        /// <summary>One-time, idempotent correction of authored fronts after models use canonical -Z.</summary>
        public static void FixExistingAuthoredFacing()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before fixing authored fronts.");
            int changed=0;
            foreach(string id in RoomIds)
            {
                string path=RoomFolder+"/"+id+".prefab";if(!File.Exists(path))continue;
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int index=Array.IndexOf(RoomIds,id);
                    var nodes=root.GetComponentsInChildren<Transform>(true);
                    foreach(var node in nodes)
                    {
                        float x=node.localPosition.x;float? yaw=null;
                        if(node.name.StartsWith("Administrative door ")||node.name=="Arcade shallow shop bay")yaw=x>0?90:270;
                        if(node.name=="Ticket information / permanently waiting")yaw=90;
                        if(node.name.StartsWith("Sign / DEPTH"))yaw=90;
                        if(node.name=="Abandoned workstation"||node.name.StartsWith("Workstation /"))
                        {
                            yaw=x>0?90:270;
                            var chair=node.Find("Chair");if(chair)chair.localRotation=Quaternion.Euler(0,node.name.StartsWith("Workstation /")?0:180,0);
                        }
                        if(node.name.StartsWith("MeshySlot__"))
                        {
                            string key=node.name.Substring("MeshySlot__".Length);
                            if(key=="waiting_bench"&&(index==0||index==1||index==11))yaw=x>0?90:270;
                            if(key=="vending_machine"&&x>8.5f)yaw=90;
                            if(key=="payphone"&&Mathf.Abs(x)>8)yaw=x>0?90:270;
                            if(key=="ventilation_unit"&&x>8)yaw=90;
                            if(key=="lockers"&&index==10)yaw=x>0?90:270;
                            if((key=="laundry_washer"||key=="laundry_dryer")&&index==12&&Mathf.Abs(x)>8)yaw=x>0?90:270;
                            if(key=="photocopier"&&index==13&&x>0)yaw=90;
                            if(key=="water_dispenser"&&index==13&&x<0)yaw=270;
                            if(key=="janitor_cart"&&(index==2||index==5||index==19))yaw=90;
                            if(key=="janitor_cart"&&index==10)yaw=110;
                            if(key=="turnstile"&&index==0)yaw=0;
                            // Preserve the deliberate wrong-way chair; only the last-bus wall chair changes side.
                            if(key=="plastic_chair"&&index==1&&x<-8)yaw=90;
                            if(key=="plastic_chair"&&(index==6||index==7))
                            {
                                var table=nodes.Where(n=>n.name=="Food court table"||n.name.StartsWith("Dining table /")).OrderBy(n=>(n.localPosition-node.localPosition).sqrMagnitude).FirstOrDefault();
                                if(table)
                                {
                                    Vector3 toward=table.localPosition-node.localPosition;toward.y=0;
                                    if(toward.sqrMagnitude<3.1f&&toward.sqrMagnitude>.01f)yaw=Quaternion.LookRotation(-toward).eulerAngles.y;
                                }
                            }
                        }
                        if(yaw.HasValue){node.localRotation=Quaternion.Euler(0,yaw.Value,0);changed++;}
                    }
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();
            Debug.Log("LIMINAL_AUTHORED_FRONTS_FIXED: "+changed+" targeted transforms. Copy-waiting benches, unusual chairs, pool ladders and north-wall props retain their intended direction.");
        }

        static bool TryPlaceMeshy(Transform holder,string key)
        {
            var asset=FindMeshy(key);if(!asset)return false;
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(asset);instance.name="Meshy / "+key;instance.transform.SetParent(holder,false);
            var renderers=instance.GetComponentsInChildren<Renderer>(true);
            if(renderers.Length==0){Object.DestroyImmediate(instance);return false;}
            // Normalize in holder-local space so placement also works in rotated prefab instances.
            instance.transform.SetParent(null,false);instance.transform.position=Vector3.zero;instance.transform.rotation=Quaternion.identity;instance.transform.localScale=Vector3.one;
            Bounds b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);
            // Meshy exports do not share a front axis. Match each reviewed model to the authored -Z front.
            bool reverseFront=key=="waiting_bench"||key=="vending_machine"||key=="plastic_chair"||key=="directory_kiosk"||key=="reception_desk"||key=="pool_ladder"||key=="payphone"||key=="water_dispenser"||key=="photocopier"||key=="ticket_machine"||key=="turnstile"||key=="laundry_washer"||key=="laundry_dryer"||key=="trash_bin"||key=="caution_sign";
            Quaternion orientation=reverseFront?Quaternion.Euler(0,180,0):Quaternion.identity;
            if(key=="janitor_cart"&&b.size.x>b.size.z*1.4f)orientation=Quaternion.Euler(0,90,0);
            instance.transform.rotation=orientation;b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);
            var target=PropSize[key];
            bool footprintFirst=key=="waiting_bench"||key=="reception_desk"||key=="foodcourt_table"||key=="fluorescent_fixture"||key=="folding_barrier";
            float allowance=footprintFirst?1.05f:1.65f;
            float factor=Mathf.Min(target.x*allowance/Mathf.Max(.01f,b.size.x),target.y/Mathf.Max(.01f,b.size.y),target.z*allowance/Mathf.Max(.01f,b.size.z));
            instance.transform.SetParent(holder,false);instance.transform.localRotation=orientation;instance.transform.localScale=Vector3.one*factor;instance.transform.localPosition=new Vector3(-b.center.x*factor,-b.min.y*factor,-b.center.z*factor);
            foreach(var c in instance.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
            var collision=holder.GetComponent<BoxCollider>();if(collision){collision.size=b.size*factor;collision.center=Vector3.up*b.size.y*factor*.5f;}
            if(key=="poolroom_arch")ConfigurePoolArchCollision(holder,b.size*factor);
            return true;
        }

        [MenuItem("AC Roguelike/Liminal/Refresh Imported Meshy Models")]
        public static void RefreshMeshyModels()
        {
            PrepareMaterials();int count=0;
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{RoomFolder,PropFolder}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);var root=PrefabUtility.LoadPrefabContents(path);bool changed=false;
                try
                {
                    foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("MeshySlot__")).ToArray())
                    {
                        string key=t.name.Substring("MeshySlot__".Length);if(!PropSize.ContainsKey(key)||!FindMeshy(key))continue;
                        var oldVisuals=t.Cast<Transform>().Where(c=>c.name.StartsWith("Meshy / ")||c.name.StartsWith("Authored proxy /")).ToArray();
                        if(TryPlaceMeshy(t,key)){foreach(var child in oldVisuals)Object.DestroyImmediate(child.gameObject);count++;changed=true;}
                    }
                    if(changed)PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();Debug.Log("LIMINAL_MESHY_REFRESH: "+count+" prop anchors updated. Existing placement and collision bounds retained.");
        }

        static LiminalRoom Room(int index)=>AssetDatabase.LoadAssetAtPath<GameObject>(RoomFolder+"/"+RoomIds[index]+".prefab").GetComponent<LiminalRoom>();

        static void CreatePropPrefabs(bool overwrite)
        {
            foreach(string key in PropSize.Keys)
            {
                string path=PropFolder+"/"+key+".prefab";if(!overwrite&&File.Exists(path))continue;
                var root=Group("Prop / "+key);props=root.transform;Prop(key,Vector3.zero);
                PrefabUtility.SaveAsPrefabAsset(root,path);Object.DestroyImmediate(root);
            }
        }

        static void CreatePropGallery()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Environment(true);
            var gallery=Group("MESHY PROP GALLERY / 27 REPLACEABLE OBJECTS");architecture=Group("Display Architecture").transform;props=Group("Display Labels").transform;
            Box("Walkable prop inspection gallery",new Vector3(12,-.18f,24),new Vector3(40,.36f,61),"Concrete");
            int index=0;foreach(var pair in PropSize)
            {
                var p=new Vector3(index%4*8,0,index/4*8);index++;
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PropFolder+"/"+pair.Key+".prefab"));instance.transform.SetParent(gallery.transform,false);instance.transform.localPosition=p;
                Box("Display outline",p+new Vector3(0,.012f,0),new Vector3(5.8f,.022f,4.8f),"Paint_Teal",null,false);
                Sign(index.ToString("00")+" / "+pair.Key.Replace('_',' '),p+new Vector3(0,.65f,-3),6.5f,"Sign_Dark",.22f);
            }
            CreatePlayer(new Vector3(0,.1f,-4));EditorSceneManager.SaveScene(scene,PropGalleryPath);
        }

        static void CreateStages(bool overwrite)
        {
            string[] titles={"노란 방","풀룸","마지막 환승","끝없는 홀"};
            string[] subtitles={"노란 벽지와 꺼지지 않는 형광등","하얀 타일 너머 청록색 물결","셔터가 내려간 빈 환승 시설","모든 방이 모이는 마지막 공간"};
            int[][] pools={new[]{2,3,13,19},new[]{4,5,12,17,18},new[]{6,7,8,9,14,15,16},new int[0]};
            for(int i=0;i<4;i++)
            {
                string path=Root+"/Stages/Stage_"+(i+1).ToString("00")+".asset";
                var stage=AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(path);if(stage&&!overwrite)continue;
                if(!stage){stage=ScriptableObject.CreateInstance<LiminalStageDefinition>();AssetDatabase.CreateAsset(stage,path);}
                stage.stageId="liminal_"+(i+1);stage.title=titles[i];stage.subtitle=subtitles[i];stage.startRoom=Room(i==0?0:1);stage.endRoom=Room(i==3?11:10);stage.roomPool=pools[i].Select(Room).ToArray();stage.middleRoomCount=i==3?0:3;stage.isBossStage=i==3;stage.ambientColor=i==0||i==3?new Color(.48f,.44f,.30f):new Color(.39f,.47f,.48f);EditorUtility.SetDirty(stage);
            }
        }

        static void Environment(bool gallery)
        {
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.45f,.46f,.37f);RenderSettings.fog=false;
            var sun=Group("Soft architectural fill").AddComponent<Light>();sun.type=LightType.Directional;sun.color=new Color(1f,.97f,.86f);sun.intensity=.68f;sun.shadows=LightShadows.Soft;sun.shadowStrength=.65f;sun.transform.rotation=Quaternion.Euler(58,-35,0);
            QualitySettings.shadowDistance=55;
            CreateBackroomsGrade();
        }

        static GameObject CreatePlayer(Vector3 position)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Astraia/AstraiaPlayer.prefab");
            if(!prefab)prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Liminal/Prefabs/Explorer/LiminalExplorer.prefab");
            if(!prefab)throw new FileNotFoundException("Standalone LiminalExplorer prefab is required.");
            var player=(GameObject)PrefabUtility.InstantiatePrefab(prefab);player.name="Player / Astraia";player.transform.position=position;
            var cameraGo=Group("Main Camera");cameraGo.tag="MainCamera";var cam=cameraGo.AddComponent<Camera>();cameraGo.AddComponent<AudioListener>();cam.fieldOfView=36;cam.nearClipPlane=.1f;cam.farClipPlane=220;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.08f,.12f,.115f);cam.allowHDR=true;
            var cameraData=cameraGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();cameraData.renderPostProcessing=true;cameraData.volumeLayerMask=1;cameraData.antialiasing=UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;cameraData.antialiasingQuality=UnityEngine.Rendering.Universal.AntialiasingQuality.High;
            var follow=cameraGo.AddComponent<IsometricFollowCamera>();follow.target=player.transform;follow.distance=20;follow.pitch=58;follow.yaw=35;follow.Snap();
            var motor=player.GetComponent<PlayerMotor>();if(motor){motor.viewCamera=cam;var marker=Group("Aim marker").transform;motor.aimMarker=marker;}
            return player;
        }

        static void CreateGallery()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Environment(true);
            var gallery=Group("ROOM GALLERY / 20 EDITABLE VARIATIONS");
            var floorRoot=Group("Gallery Walkways").transform;architecture=floorRoot;
            Box("Gallery connecting floor",new Vector3(78,-.35f,147),new Vector3(209,.38f,323),"Concrete",floorRoot);
            for(int i=0;i<RoomIds.Length;i++)
            {
                int column=i%4,row=i/4;var position=new Vector3(column*52,0,row*65);
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(Room(i).gameObject);instance.name=RoomIds[i]+" / EDIT PREFAB TO CHANGE RUN";instance.transform.SetParent(gallery.transform,false);instance.transform.localPosition=position;
                var room=instance.GetComponent<LiminalRoom>();if(room.entranceGate)room.entranceGate.SetActive(false);if(room.exitGate)room.exitGate.SetActive(false);
                props=Group("Gallery Labels",floorRoot).transform;
                Sign((i+1).ToString("00")+" / "+RoomIds[i].Substring(3).Replace('_',' '),position+new Vector3(0,1.4f,-4),14,"Sign_Dark",.32f);
                Sign("Prefab > Architecture / Props / Lighting / Gameplay / Sockets",position+new Vector3(0,.55f,-4),16,"Sign_Dark",.17f);
            }
            CreatePlayer(new Vector3(0,.1f,-1));
            CreateGalleryHud();
            EditorSceneManager.SaveScene(scene,GalleryPath);
        }

        static void CreateGalleryHud()
        {
            var canvas=Group("Gallery guide / hide in Scene view",null).AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=canvas.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);
            var panel=new GameObject("Guide",typeof(RectTransform),typeof(UnityEngine.UI.Image));panel.transform.SetParent(canvas.transform,false);var rt=panel.GetComponent<RectTransform>();rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.pivot=new Vector2(0,1);rt.anchoredPosition=new Vector2(18,-18);rt.sizeDelta=new Vector2(595,128);panel.GetComponent<UnityEngine.UI.Image>().color=new Color(.04f,.09f,.085f,.91f);
            var label=new GameObject("Instructions",typeof(RectTransform),typeof(UnityEngine.UI.Text));label.transform.SetParent(panel.transform,false);var lr=label.GetComponent<RectTransform>();lr.anchorMin=Vector2.zero;lr.anchorMax=Vector2.one;lr.offsetMin=new Vector2(17,10);lr.offsetMax=new Vector2(-12,-10);var text=label.GetComponent<UnityEngine.UI.Text>();text.font=signageFont;text.fontSize=17;text.color=new Color(.82f,.89f,.8f);text.text="리미널 스페이스 / 방 작업실\n20개 공간을 직접 둘러보고 프리팹을 열어 배치를 바꿔 봐.\nWASD 이동  /  SHIFT 대시  /  SCROLL 확대\nAC Roguelike > Liminal > Room Workshop";text.raycastTarget=false;
        }

        static void CreateRun()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Environment(false);
            var player=CreatePlayer(new Vector3(0,.1f,3.5f));
            var go=Group("LIMINAL RUN / Seeded Stage Assembly");var director=go.AddComponent<LiminalRunDirector>();director.player=player.transform;director.seed=73029;director.stages=Enumerable.Range(1,4).Select(i=>AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(Root+"/Stages/Stage_"+i.ToString("00")+".asset")).ToArray();director.hudFont=signageFont;
            // Keep a visible editable sample in the saved scene; the director replaces its own preview at run start.
            director.GeneratePreview(0);
            EditorSceneManager.SaveScene(scene,RunPath);
        }

        public static void FrameGallery()
        {
            var room=Object.FindObjectsByType<LiminalRoom>(FindObjectsInactive.Include,FindObjectsSortMode.None).OrderBy(r=>r.roomId).FirstOrDefault();
            if(room){Selection.activeGameObject=room.gameObject;SceneView.lastActiveSceneView?.Frame(new Bounds(room.transform.TransformPoint(room.localBounds.center),room.localBounds.size+new Vector3(3,0,3)),false);}
        }

        public static string ValidateAll(string reportPath="Documentation/Liminal/authoring-validation.txt")
        {
            int rooms=0,errors=0,anchors=0,models=0;var messages=new List<string>();
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{RoomFolder}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);var go=AssetDatabase.LoadAssetAtPath<GameObject>(path);var room=go.GetComponent<LiminalRoom>();if(!room)continue;rooms++;
                if(!room.entry||!room.exit||!room.playerSpawn){errors++;messages.Add(go.name+": missing socket or player spawn");continue;}
                if(Vector3.Distance(room.entry.localPosition,Vector3.zero)>.01f||room.exit.localPosition.z<1||Mathf.Abs(room.exit.localPosition.x)>.01f){errors++;messages.Add(go.name+": invalid linear sockets");}
                foreach(string name in new[]{"Architecture","Props","Lighting","Gameplay","Sockets"})if(!go.transform.Find(name)){errors++;messages.Add(go.name+": missing "+name);}
                foreach(var t in go.GetComponentsInChildren<Transform>(true)){if(t.name.StartsWith("MeshySlot__"))anchors++;if(t.name.StartsWith("Meshy / "))models++;}
            }
            string result=rooms+" rooms; "+errors+" structural issues; "+models+"/"+anchors+" Meshy anchors populated.";
            foreach(var message in messages)Debug.LogError(message);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));File.WriteAllText(reportPath,result+"\n"+string.Join("\n",messages));
            return result;
        }
    }
}
