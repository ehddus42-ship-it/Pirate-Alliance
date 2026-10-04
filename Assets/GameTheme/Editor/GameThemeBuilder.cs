using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcRoguelike.Liminal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace AcRoguelike.GameTheme.Editor
{
    /// <summary>Authored game exhibition kit. Rebuilding only replaces assets owned by this theme.</summary>
    public static class GameThemeBuilder
    {
        public const string Root = "Assets/GameTheme";
        public const string ScenePath = Root + "/Scenes/StageConcept_Game.unity";
        static readonly string[] Names = { "코인 너머의 전시장", "열린 칸의 정원", "쌓이지 않는 블록 광장", "끝나지 않는 오락실", "다음 게임의 문", "깃발 아래의 보드", "블록 낙하 관측소" };
        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        static readonly Color Ambient = new Color(.54f, .59f, .68f);
        static int currentRoom;

        [MenuItem("AC Roguelike/Game Theme/Build Game Dungeon")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop play before authoring.");
            for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scene first.");
            foreach (var folder in new[] {"Materials", "Meshes", "Prefabs/Rooms", "Data", "Scenes", "Profiles"}) Directory.CreateDirectory(Root+"/"+folder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Palette();
            GameThemeMonsterBuilder.BuildAll();
            var rooms=new LiminalRoom[7];
            for(int i=1;i<=7;i++) rooms[i-1]=BuildRoom(i);
            var stage=AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(Root+"/Data/Stage_Game.asset");
            if(!stage) { stage=ScriptableObject.CreateInstance<LiminalStageDefinition>(); AssetDatabase.CreateAsset(stage,Root+"/Data/Stage_Game.asset"); }
            stage.name="Game"; stage.stageId="concept_game"; stage.title="게임의 잔상";
            stage.subtitle="열린 칸과 블록 사이, 끝나지 않는 게임의 전시장을 지나라";
            stage.startRoom=rooms[0]; stage.endRoom=rooms[4]; stage.roomPool=new[]{rooms[1],rooms[2],rooms[3],rooms[5],rooms[6]};
            stage.middleRoomCount=3; stage.isBossStage=false; stage.ambientColor=Ambient; EditorUtility.SetDirty(stage);
            RegisterMission(stage);
            GameTetrominoBossBuilder.Build();
            AssetDatabase.SaveAssets(); CreateScene(stage);
            var scenes=EditorBuildSettings.scenes.ToList();
            if(!scenes.Any(s=>s.path==ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath,true));
            EditorBuildSettings.scenes=scenes.ToArray(); AssetDatabase.SaveAssets();
            Debug.Log("Game theme: eight room assets, five-room route ending in Drop Keeper, three voxel enemy roles, gate mission G06 built.");
        }

        public static void BuildAndValidate() { BuildAll(); GameThemeValidation.RunAll(); GameThemeOccluderValidation.RunBatch(); }
        public static void BuildValidateAndPlayBatch() { BuildAndValidate(); GameThemeValidation.ValidateBatch(); }

        [MenuItem("AC Roguelike/Game Theme/Open Game Dungeon")]
        public static void OpenGame()
        {
            if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(ScenePath);
        }

        static void Palette()
        {
            Mats.Clear();
            Mat("Shell", "26344D", .35f, .3f); Mat("Dark", "152239", .1f, .3f);
            Mat("Floor", "718599", .2f, .32f); Mat("FloorAlt", "788D9F", .12f,.27f);
            Mat("Ivory", "D5D9CC", .1f,.38f); Mat("Edge", "9BAFAF", .55f,.32f);
            Mat("Cyan", "56BEC9", .25f,.38f); Mat("Blue", "597AD5", .25f,.34f);
            Mat("Purple", "9276CD", .25f,.36f); Mat("Pink", "D46A88", .2f,.34f);
            Mat("Amber", "D5AC58", .48f,.4f); Mat("Green", "6FAF91", .22f,.38f);
            Mat("Screen", "213A4B", .35f,.65f); Mat("Glow", "A4E6EC", .2f,.45f,1.25f);
            Mat("PinkGlow", "DCA3D0", .15f,.4f,.7f);
            Mat("Carpet", "42506B", .03f,.12f); Mat("CarpetMotif", "58687F", .02f,.12f);
            Mat("SoftBlue", "698594", .13f,.3f); Mat("SoftPurple", "7D839A", .13f,.3f);
        }
        static void Mat(string key,string hex,float metallic,float smooth,float emission=0)
        {
            var path=Root+"/Materials/Game_"+key+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
            ColorUtility.TryParseHtmlString("#"+hex,out var c); m.SetColor("_BaseColor",c); m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smooth);
            m.enableInstancing=true;
            if(emission>0){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",c*emission);}
            Mats[key]=m;EditorUtility.SetDirty(m);
        }

        static LiminalRoom BuildRoom(int index)
        {
            currentRoom=index;
            string id="Game_"+index.ToString("00"); var root=new GameObject(id);
            try
            {
                var room=root.AddComponent<LiminalRoom>(); room.roomId=id; room.displayName=Names[index-1];
                room.kind=index==1?LiminalRoomKind.Arrival:index==5?LiminalRoomKind.Threshold:LiminalRoomKind.Combat;
                room.localBounds=new Bounds(new Vector3(0,4,18.2f),new Vector3(26,8,36.4f));
                var marker=root.AddComponent<GameThemeRoom>();
                if(index==1)root.AddComponent<GameThemeAtmosphere>();
                marker.enemyRoles=index==2||index==6 ? new[]{GameVoxelRole.PixelMaw,GameVoxelRole.BitSentry,GameVoxelRole.PixelMaw}
                    :index==3||index==7?new[]{GameVoxelRole.StackGuardian,GameVoxelRole.BitSentry,GameVoxelRole.PixelMaw}
                    :new[]{GameVoxelRole.BitSentry,GameVoxelRole.PixelMaw,GameVoxelRole.StackGuardian};
                var geo=new Geometry(root.transform); var gameplay=Group("Gameplay",root.transform); var sockets=Group("Sockets",root.transform);
                room.entry=Marker("Entry",sockets,Vector3.zero); room.exit=Marker("Exit",sockets,new Vector3(0,0,36.4f));
                room.playerSpawn=Marker("PlayerSpawn",gameplay,new Vector3(0,.05f,4));
                room.enemySpawns=room.kind==LiminalRoomKind.Combat?new[]{Marker("Enemy_A",gameplay,new Vector3(0,.08f,13)),Marker("Enemy_B",gameplay,new Vector3(1,.08f,22)),Marker("Enemy_C",gameplay,new Vector3(-1,.08f,29))}:Array.Empty<Transform>();
                if(index==3||index==7){room.enemySpawns[0].localPosition=new Vector3(-3,.08f,14);room.enemySpawns[1].localPosition=new Vector3(4,.08f,25);}
                if(index==4){room.enemySpawns[0].localPosition=new Vector3(-3,.08f,14);room.enemySpawns[1].localPosition=new Vector3(3,.08f,23);}
                if(index==6){room.enemySpawns[0].localPosition=new Vector3(3,.08f,13);room.enemySpawns[2].localPosition=new Vector3(-3,.08f,29);}
                Solid("WalkableFloor",gameplay,new Vector3(0,-.35f,18.2f),new Vector3(26,.7f,36.4f));
                foreach(float x in new[]{-13.3f,13.3f}) Solid("SideBoundary",gameplay,new Vector3(x,2,18.2f),new Vector3(.6f,4,36.4f));
                foreach(float z in new[]{0f,36.4f}) foreach(float x in new[]{-8.35f,8.35f}) Solid("DoorBoundary",gameplay,new Vector3(x,1.5f,z),new Vector3(9.7f,3,.25f));
                room.entranceGate=Gate("EntranceGate",gameplay,0); room.exitGate=Gate("ExitGate",gameplay,36.4f);room.SetGates(false,false);
                Shell(geo,index);
                switch(index){case 1: Arrival(geo);break;case 2: MineBoard(geo,false);break;case 3: Blocks(geo,false);break;case 4: Arcade(geo);break;case 5: Exit(geo);break;case 6: MineBoard(geo,true);break;case 7: Blocks(geo,true);break;}
                geo.Flush(id); Light(root.transform,new Vector3(8,5,12),new Color(.5f,.82f,1),2.4f,12);
                Light(root.transform,new Vector3(-7,4,27),new Color(1,.75f,.44f),1.6f,10);
                Light(root.transform,new Vector3(9,7,31),new Color(.7f,.58f,1),2,13);
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/Rooms/"+id+".prefab");
                return prefab.GetComponent<LiminalRoom>();
            }
            finally{Object.DestroyImmediate(root);}
        }

        static void Shell(Geometry g,int index)
        {
            g.Box("Shell",new Vector3(0,-.55f,18.2f),new Vector3(27,1,36.4f),.2f);
            g.Box("Dark",new Vector3(5,-1.15f,18.2f),new Vector3(37,.45f,36.1f),.15f);
            for(int x=-6;x<=6;x++)for(int z=0;z<18;z++)
            {
                string tile=(x+z)%3==0?"FloorAlt":"Floor";
                if(index==4)tile=Math.Abs(x)<3?"Carpet":"Floor";
                if(index==3||index==7)tile=(x+6+z/3)%5==0?"SoftPurple":(x+5+z/3)%5==0?"SoftBlue":"Floor";
                g.Box(tile,new Vector3(x*2,-.1f,1.2f+z*2),new Vector3(1.96f,.2f,1.96f),.035f);
                if(index==4&&Math.Abs(x)<3)
                {
                    g.Box("CarpetMotif",new Vector3(x*2,.008f,1.2f+z*2),new Vector3(.12f,.012f,.6f),0,35);
                    g.Box("CarpetMotif",new Vector3(x*2,.01f,1.2f+z*2),new Vector3(.6f,.012f,.12f),0,35);
                }
            }
            foreach(float x in new[]{-12.8f,12.8f})
            {
                g.Box("Shell",new Vector3(x,.22f,18.2f),new Vector3(.36f,.5f,36.4f),.07f);
                g.Box("Glow",new Vector3(x,.49f,18.2f),new Vector3(.055f,.045f,35.8f));
            }
            // Recessed circuit trenches and a raised exhibition wall fill the far-camera side.
            for(int z=2;z<36;z+=4)
            {
                g.Box("Shell",new Vector3(16,1.2f,z),new Vector3(6,2.4f,3.8f),.16f);
                g.Box("Edge",new Vector3(13.3f,1.25f,z),new Vector3(.12f,2.1f,3.4f),.03f);
                g.Box("Screen",new Vector3(13.2f,1.25f,z),new Vector3(.08f,1.65f,2.9f));
                for(int n=0;n<3;n++)g.Box(n==1?"Cyan":"Blue",new Vector3(13.14f,1.25f,z-1+n),new Vector3(.04f,.12f,.7f));
                g.Box("Ivory",new Vector3(-12.3f,.35f,z),new Vector3(.7f,.7f,2.5f),.09f);
            }
            foreach(float z in new[]{1.4f,35f})
            {
                foreach(float x in new[]{-7f,7f})g.Box("Shell",new Vector3(x,.42f,z),new Vector3(7,.84f,.8f),.12f);
                g.Box("Cyan",new Vector3(0,.015f,z),new Vector3(6.4f,.03f,.25f));
                for(int i=0;i<5;i++)g.Box("Ivory",new Vector3(-2+i,.03f,z+.48f),new Vector3(.55f,.025f,.15f));
            }
            // Every room has a unique arcade marquee outside the combat lane.
            var titles=new[]{"INSERT COIN","OPEN CELLS","STACK GARDEN","ARCADE","CONTINUE","FLAG FIELD","NEXT BLOCK"};
            Sign(g,titles[index-1],new Vector3(8,5.8f,31.5f),.28f);
            foreach(float z in new[]{8f,18f,28f})
            {
                g.Box("Dark",new Vector3(17,3.4f,z),new Vector3(6,.25f,6.4f),.07f);
                // Connector contacts turn the backdrop into an oversized circuit board.
                for(int n=0;n<5;n++)g.Box("Amber",new Vector3(19,3.6f,z-2+n),new Vector3(1.4f,.18f,.32f),.04f);
                for(int n=0;n<3;n++)
                {
                    g.Box("Cyan",new Vector3(15+n*.6f,3.57f,z),new Vector3(.07f,.05f,4.5f));
                    g.Box("Cyan",new Vector3(16.5f,3.57f,z-2+n*.5f),new Vector3(3,.05f,.07f));
                }
            }
            if(index==1||index==4||index==5)
            {
                // Rows of machines form a layered skyline beyond the navigable exhibition concourse.
                for(int n=0;n<4;n++)Prop(g,"arcade_cabinet",new Vector3(15.3f,2.42f,5+n*8),3.8f,225,false);
                for(int n=0;n<2;n++)Prop(g,"prize_cabinet",new Vector3(18,3.6f,12+n*17),4.2f,225,false);
            }
        }

        static void Arrival(Geometry g)
        {
            // An oversized console is the first reveal; coin trails lead to the safe central lane.
            Pedestal(g,new Vector3(7.9f,0,11),new Vector3(5.2f,.7f,4.4f));
            Prop(g,"handheld_monument",new Vector3(7.9f,.7f,11),6.8f,205,false);
            Solid("ConsolePlinth",g.root,new Vector3(7.9f,.8f,11),new Vector3(5.2f,1.6f,4.4f));
            Prop(g,"controller_bench",new Vector3(-8,0,9),1.25f,165,true);
            for(int i=0;i<3;i++)Prop(g,"arcade_cabinet",new Vector3(9,0,20+i*3.2f),2.8f,235,true);
            Prop(g,"pinball_table",new Vector3(-8.6f,0,23),2.7f,155,true);
            Prop(g,"prize_cabinet",new Vector3(-9,0,29),3.1f,155,true);
            // Decor batch: a claw machine beside the room-number display and a CRT pile closing the east row.
            Decor(g,"claw_machine",new Vector3(-8.8f,0,18.2f),2.3f,155,true);
            Decor(g,"crt_stack",new Vector3(9.2f,0,30.6f),2.2f,220,true);
            Portal(g,32.8f); Coins(g,-4,7,6); FloorWord(g,"PLAY",new Vector3(-2.3f,.025f,17),.45f);
            PixelInvader(g,new Vector3(17,4.5f,12),.52f,"Purple");
            Tetromino(g,new Vector3(17,2.7f,25),1.4f,2,"Cyan",false);
            Display(g,new Vector3(-8,0,15),"01");
        }

        static void MineBoard(Geometry g,bool flags)
        {
            // A pressed cell is a continuous walkable plane; unopened cells are actual walls.
            for(int x=-5;x<=5;x++)for(int z=2;z<=15;z++)
            {
                float px=x*2,pz=z*2+1.2f;
                bool closed = Math.Abs(x)>=4 || (flags ? (x<=-2&&z>=6&&z<=8)||(x>=2&&z>=11&&z<=13) : (x<=-2&&z>=9&&z<=11)||(x>=2&&z>=4&&z<=6));
                if(closed)
                {
                    float h=Math.Abs(x)>=4 ? (x>0?1.9f:1.15f):1.2f;
                    g.Box("Shell",new Vector3(px,h*.5f,pz),new Vector3(1.95f,h,1.95f),.13f);
                    g.Box("Ivory",new Vector3(px,h+.06f,pz),new Vector3(1.62f,.16f,1.62f),.09f);
                    Solid("UnopenedCell",g.root,new Vector3(px,h*.5f,pz),new Vector3(1.95f,h,1.95f));
                    if((z+x+35)%7==0)Flag(g,new Vector3(px,h+.15f,pz));
                }
                else
                {
                    g.Box((z+x)%3==0?"Ivory":"Edge",new Vector3(px,-.001f,pz),new Vector3(1.92f,.03f,1.92f),.035f);
                    int near=0; for(int d=-1;d<=1;d++)if(Math.Abs(x+d)>=4)near++;
                    if((near>0||(z*7+x+50)%4==0)&&!(Math.Abs(x)<2&&(z==6||z==10||z==14)))
                        FloorDigit(g,1+(Math.Abs(x+z)%3),new Vector3(px,.025f,pz),.22f);
                }
            }
            // Giant score-window, not a blocking ceiling, sits beyond the east play boundary.
            g.Box("Ivory",new Vector3(12.1f,4,21),new Vector3(4.2f,6,5.7f),.28f);
            g.Box("Shell",new Vector3(12.1f,5,18.08f),new Vector3(3.7f,3.1f,.2f),.1f);
            Sign(g,flags?"099":"024",new Vector3(12.1f,5.5f,17.9f),.37f);
            Flag(g,new Vector3(12,7.1f,21),2.3f);
            Solid("ScoreTower",g.root,new Vector3(12.1f,4,21),new Vector3(4.2f,6,5.7f));
            for(int n=0;n<3;n++)
            {
                g.Box("Shell",new Vector3(17,4+n*1.8f,10+n*7),new Vector3(3.8f,3.8f,3.8f),.23f);
                g.Box("Ivory",new Vector3(17,6+n*1.8f,10+n*7),new Vector3(3.4f,.23f,3.4f),.1f);
            }
            Prop(g,"joystick_tower",new Vector3(8,0,32),4.3f,205,true);
            Prop(g,"controller_bench",new Vector3(-8,0,32),1.2f,160,true);
            Prop(g,"arcade_cabinet",new Vector3(8.5f,0,3),2.8f,210,true);
            Display(g,new Vector3(-9.5f,0,3),flags?"06":"02");
            if(flags)Decor(g,"dance_machine",new Vector3(12.8f,0,29.2f),2.6f,250,false);
            else Decor(g,"speaker_tower",new Vector3(12.6f,0,9.6f),2.1f,250,false);
            Decor(g,"cartridge_crate",new Vector3(-6.4f,0,3.1f),1.1f,flags?20:-15,true);
            Coins(g,flags?3:-3,11,4);
        }

        static void Blocks(Geometry g,bool observatory)
        {
            // Distinct tetromino silhouettes, bevelled faces and join recesses read from the play camera.
            string[] colors={"Cyan","Purple","Amber","Blue","Pink","Green"};
            for(int n=0;n<5;n++)
            {
                var p=new Vector3(11.7f+(n%2)*1.1f,2.8f+(n%3)*.5f,5+n*6.2f);
                Tetromino(g,p,1.55f,n,colors[n],false);
                if(n%2==0)Tetromino(g,p+new Vector3(3.5f,4.7f,.5f),1.25f,(n+2)%5,colors[(n+2)%6],false);
                g.Box("Shell",new Vector3(11.4f,1.2f,5+n*6.2f),new Vector3(3,2.4f,2.8f),.2f);
                Solid("BlockDisplayPlinth",g.root,new Vector3(11.4f,2.5f,5+n*6.2f),new Vector3(3,5,2.8f));
            }
            for(int n=0;n<2;n++)Tetromino(g,new Vector3(-9,1.4f,15+n*14),1.2f,n+2,colors[n+1],false);
            if(observatory)
            {
                BlockPlatform(g,new Vector3(0,0,18),2.1f,3,"Purple");
                BlockPlatform(g,new Vector3(-6.8f,0,10),1.45f,1,"Purple");
                BlockPlatform(g,new Vector3(7.6f,0,22),1.45f,3,"Green");
                BlockPlatform(g,new Vector3(-7.3f,0,29),1.4f,2,"Amber");
                // Suspended next-piece gantry is entirely east of the camera's fight corridor.
                g.Box("Shell",new Vector3(12,4,20),new Vector3(.8f,8,.8f),.1f);
                g.Box("Shell",new Vector3(19,4,20),new Vector3(.8f,8,.8f),.1f);
                g.Box("Edge",new Vector3(15.5f,8,20),new Vector3(8,.6f,.8f),.1f);
                Tetromino(g,new Vector3(15.3f,6,20),1.25f,0,"Pink",false);
                Prop(g,"racing_cockpit",new Vector3(8,0,7),3.1f,300,true);
                Prop(g,"handheld_monument",new Vector3(8.6f,0,29.5f),5.1f,220,true);
                Decor(g,"crt_stack",new Vector3(-8.8f,0,20),2.2f,120,true);
                Decor(g,"speaker_tower",new Vector3(8.6f,0,15.6f),2,240,true);
                FloorWord(g,"NEXT",new Vector3(-2.3f,.025f,10),.35f);
            }
            else
            {
                BlockPlatform(g,new Vector3(0,0,18),2.1f,2,"Blue");
                Tetromino(g,new Vector3(0,2.5f,19.4f),1.25f,2,"Purple",false);
                BlockPlatform(g,new Vector3(-7,0,12),1.5f,0,"Cyan");
                BlockPlatform(g,new Vector3(6.6f,0,18),1.5f,2,"Purple");
                BlockPlatform(g,new Vector3(-7.8f,0,26),1.5f,4,"Amber");
                Prop(g,"joystick_tower",new Vector3(8,0,29),4.6f,225,true);
                Prop(g,"pinball_table",new Vector3(-8.6f,0,4.7f),2.5f,155,true);
                Decor(g,"air_hockey",new Vector3(-8.9f,0,19.4f),1,90,true);
                Decor(g,"claw_machine",new Vector3(8.2f,0,14.3f),2.3f,230,true);
                FloorWord(g,"STACK",new Vector3(-3,.025f,7),.3f);
            }
            for(int z=4;z<34;z+=3)g.Box("Cyan",new Vector3(3.5f,.02f,z),new Vector3(.09f,.025f,1.25f));
            Display(g,new Vector3(8.7f,0,3),observatory?"07":"03");
        }

        static void Arcade(Geometry g)
        {
            // Three different hardware exhibits; generous gaps leave an eight-metre combat concourse.
            for(int n=0;n<4;n++)Prop(g,"arcade_cabinet",new Vector3(9.4f,0,5+n*3.5f),2.8f,235,true);
            for(int n=0;n<2;n++)Prop(g,"pinball_table",new Vector3(-9,0,8+n*5),2.6f,145,true);
            Prop(g,"racing_cockpit",new Vector3(8.3f,0,23),3.3f,305,true);
            Prop(g,"prize_cabinet",new Vector3(8.7f,0,30.5f),3.8f,215,true);
            Prop(g,"controller_bench",new Vector3(-8,0,23),1.3f,160,true);
            Prop(g,"joystick_tower",new Vector3(-8.8f,0,30),3.4f,180,true);
            Decor(g,"dance_machine",new Vector3(-8.6f,0,18.4f),2.6f,150,true);
            Decor(g,"air_hockey",new Vector3(9.6f,0,18.5f),1,90,true);
            for(int z=7;z<33;z+=5)
            {
                g.Box("Shell",new Vector3(7.4f,-.004f,z),new Vector3(3.9f,.04f,4.5f),.01f);
                g.Box("Cyan",new Vector3(5.42f,.033f,z),new Vector3(.08f,.02f,4.3f));
            }
            g.Box("Shell",new Vector3(16.5f,5,18),new Vector3(6.5f,6.7f,1.2f),.3f);
            g.Box("Screen",new Vector3(16.5f,5.4f,17.35f),new Vector3(5.8f,4.9f,.12f),.08f);
            PixelInvader(g,new Vector3(16.5f,6.2f,17.24f),.4f,"Cyan");
            Sign(g,"HI SCORE",new Vector3(16.5f,8.7f,17.2f),.2f);
            Coins(g,-3.8f,12,5); FloorWord(g,"ARCADE",new Vector3(-3.6f,.025f,27),.28f);
            Display(g,new Vector3(-8.5f,0,4),"04");
        }

        static void Exit(Geometry g)
        {
            Portal(g,30.4f);
            Prop(g,"handheld_monument",new Vector3(8.2f,0,16),6,205,true);
            Prop(g,"prize_cabinet",new Vector3(-8.8f,0,19),3.2f,165,true);
            Prop(g,"controller_bench",new Vector3(-8,0,9),1.25f,165,true);
            Prop(g,"pinball_table",new Vector3(8.2f,0,7),2.7f,220,true);
            Decor(g,"claw_machine",new Vector3(-8.8f,0,13.6f),2.3f,150,true);
            Decor(g,"cartridge_crate",new Vector3(8.7f,0,21.8f),1.1f,220,true);
            for(int side=-1;side<=1;side+=2)for(int n=0;n<3;n++)
            {
                float x=side*(7.5f+n*.8f); var p=new Vector3(x,.25f,25+n*1.4f);
                g.Box("Blue",p,new Vector3(1.4f,.5f,1.4f),.12f);
                g.Box("Ivory",p+Vector3.up*.27f,new Vector3(1.1f,.07f,1.1f),.03f);
            }
            FloorWord(g,"CONTINUE",new Vector3(-4.2f,.025f,20),.28f);
            PixelInvader(g,new Vector3(17,5.5f,24),.48f,"Amber"); Coins(g,-3.5f,7,7);
            Display(g,new Vector3(9,0,26),"05");
        }

        static readonly int[][][] Pieces={
            new[]{new[]{-1,0},new[]{0,0},new[]{1,0},new[]{2,0}},
            new[]{new[]{0,0},new[]{1,0},new[]{0,1},new[]{1,1}},
            new[]{new[]{-1,0},new[]{0,0},new[]{1,0},new[]{0,1}},
            new[]{new[]{-1,0},new[]{0,0},new[]{0,1},new[]{1,1}},
            new[]{new[]{0,0},new[]{1,0},new[]{0,1},new[]{0,2}}
        };
        static void Tetromino(Geometry g,Vector3 p,float unit,int type,string color,bool flat)
        {
            foreach(var xy in Pieces[type%Pieces.Length])
            {
                var q=p+(flat?new Vector3(xy[0]*unit,0,xy[1]*unit):new Vector3(xy[0]*unit,xy[1]*unit,0));
                g.Box("Dark",q,Vector3.one*(unit*.81f),unit*.08f);
                g.Box(color,q,Vector3.one*(unit*.92f),unit*.1f);
                // Small recessed face, with a clean raised border around each voxel.
                g.Box(color,q+new Vector3(0,0,-unit*.467f),new Vector3(unit*.64f,unit*.64f,.035f),.035f);
                g.Box("Ivory",q+new Vector3(-unit*.24f,unit*.25f,-unit*.48f),new Vector3(unit*.2f,unit*.05f,.025f));
            }
        }
        static void BlockPlatform(Geometry g,Vector3 p,float unit,int type,string color)
        {
            foreach(var xy in Pieces[type%Pieces.Length])
            {
                var q=p+new Vector3(xy[0]*unit,.52f,xy[1]*unit);
                g.Box("Shell",q,new Vector3(unit*.97f,1.04f,unit*.97f),.09f);
                g.Box(color,q+Vector3.up*.53f,new Vector3(unit*.89f,.22f,unit*.89f),.08f);
                Solid("SolidTetromino",g.root,q,new Vector3(unit*.97f,1.22f,unit*.97f));
            }
        }
        static void Pedestal(Geometry g,Vector3 p,Vector3 size)
        {
            g.Box("Shell",p+Vector3.up*size.y*.5f,size,.15f);
            g.Box("Amber",p+Vector3.up*(size.y-.06f),new Vector3(size.x+.04f,.1f,size.z+.04f),.04f);
        }
        static void Portal(Geometry g,float z)
        {
            Prop(g,"cartridge_arch",new Vector3(0,0,z),6.4f,180,false);
            // Meshy's actual opening is 3.63m at foot height: collisions follow the visible piers.
            foreach(float x in new[]{-4.8f,4.8f})
            {
                g.Box("Shell",new Vector3(x,2.9f,z),new Vector3(1.3f,5.8f,1.4f),.18f);
                g.Box("Glow",new Vector3(x-.15f,2.9f,z-.74f),new Vector3(.14f,4.5f,.05f));
            }
            foreach(float x in new[]{-3.6f,3.6f})Solid("CartridgeArchPier",g.root,new Vector3(x,2.7f,z),new Vector3(3.45f,5.4f,4.8f));
            Sign(g,"CONTINUE",new Vector3(0,6.7f,z-.8f),.24f);
        }
        static void Display(Geometry g,Vector3 p,string number)
        {
            g.Box("Shell",p+Vector3.up*.7f,new Vector3(1.8f,1.4f,1.3f),.15f);
            g.Box("Screen",p+new Vector3(0,1.4f,0),new Vector3(1.65f,.09f,1.15f),.07f);
            FloorWord(g,number,p+new Vector3(-.48f,1.46f,0),.15f);
            Solid("InformationPlinth",g.root,p+Vector3.up*.7f,new Vector3(1.8f,1.4f,1.3f));
        }
        static void Coins(Geometry g,float x,float z,int count)
        {
            for(int i=0;i<count;i++)
            {
                // Embedded collectibles motif: no collider and no false pick-up promise.
                g.Box("Amber",new Vector3(x,.022f,z+i*1.65f),new Vector3(.42f,.03f,.42f),.12f,45);
                g.Box("Ivory",new Vector3(x,.04f,z+i*1.65f),new Vector3(.08f,.015f,.26f));
            }
        }
        static void Flag(Geometry g,Vector3 p,float scale=1)
        {
            g.Box("Shell",p+Vector3.up*.6f*scale,new Vector3(.075f,1.2f,.075f)*scale,.025f*scale);
            g.Box("Pink",p+new Vector3(.3f,.93f,0)*scale,new Vector3(.65f,.4f,.07f)*scale,.025f*scale);
            g.Box("Amber",p+new Vector3(0,.06f,0),new Vector3(.6f,.08f,.6f)*scale,.08f*scale);
        }
        static void PixelInvader(Geometry g,Vector3 p,float cell,string color)
        {
            string[] rows={"001000100","000101000","001111100","011010110","111111111","101111101","101000101","000101000"};
            for(int y=0;y<rows.Length;y++)for(int x=0;x<rows[y].Length;x++)if(rows[y][x]=='1')g.Box(color,p+new Vector3((x-4)*cell,-y*cell,0),Vector3.one*cell*.91f,cell*.08f);
        }

        static readonly Dictionary<char,string> Glyphs=new Dictionary<char,string>{
            {'0',"111101101101111"},{'1',"010110010010111"},{'2',"111001111100111"},{'3',"111001111001111"},{'4',"101101111001001"},{'5',"111100111001111"},{'6',"111100111101111"},{'7',"111001010010010"},{'8',"111101111101111"},{'9',"111101111001111"},
            {'A',"010101111101101"},{'B',"110101110101110"},{'C',"111100100100111"},{'D',"110101101101110"},{'E',"111100110100111"},{'F',"111100110100100"},{'G',"111100101101111"},{'H',"101101111101101"},{'I',"111010010010111"},{'J',"001001001101111"},{'K',"101101110101101"},{'L',"100100100100111"},{'M',"101111111101101"},{'N',"101111111111101"},{'O',"111101101101111"},{'P',"111101111100100"},{'Q',"111101101111001"},{'R',"110101110101101"},{'S',"111100111001111"},{'T',"111010010010010"},{'U',"101101101101111"},{'V',"101101101101010"},{'W',"101101111111101"},{'X',"101101010101101"},{'Y',"101101010010010"},{'Z',"111001010100111"}};
        static void Sign(Geometry g,string text,Vector3 center,float cell)
        {
            float w=(text.Length*4+1)*cell;
            g.Box("Shell",center+new Vector3(0,-cell*2,.1f),new Vector3(w,cell*7,.25f),.1f);
            Text(g,text,center+new Vector3(-w*.5f+cell,0,-.05f),cell,false,"Glow");
        }
        static void FloorWord(Geometry g,string text,Vector3 origin,float cell)=>Text(g,text,origin,cell,true,"Ivory");
        static void FloorDigit(Geometry g,int digit,Vector3 center,float cell)=>Text(g,digit.ToString(),center+new Vector3(-cell,0,2*cell),cell,true,digit==1?"Blue":digit==2?"Green":"Purple");
        static void Text(Geometry g,string text,Vector3 origin,float cell,bool floor,string color)
        {
            for(int n=0;n<text.Length;n++)if(Glyphs.TryGetValue(text[n],out var glyph))
                for(int y=0;y<5;y++)for(int x=0;x<3;x++)if(glyph[y*3+x]=='1')
                    g.Box(color,origin+(floor?new Vector3((n*4+x)*cell,0,-y*cell):new Vector3((n*4+x)*cell,-y*cell,0)),floor?new Vector3(cell*.87f,.012f,cell*.87f):new Vector3(cell*.87f,cell*.87f,.035f));
        }
        // Decor batch props are optional until their Meshy models are reviewed in: a missing one is skipped, not an error.
        static void Decor(Geometry g,string key,Vector3 p,float height,float yaw,bool collision)
        {
            if(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/Meshy/game_"+key+"/game_"+key+".glb"))Prop(g,key,p,height,yaw,collision);
        }
        static void Prop(Geometry g,string key,Vector3 p,float height,float yaw,bool collision)
        {
            string path=Root+"/Art/Meshy/game_"+key+"/game_"+key+".glb";
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!asset) throw new FileNotFoundException("Required reviewed Meshy prop missing: "+path);
            var holder=Group("Meshy_"+key,g.root); holder.localPosition=p;holder.localRotation=Quaternion.Euler(0,yaw,0);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(asset,holder);
            model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.identity;model.transform.localScale=Vector3.one;
            var renderers=model.GetComponentsInChildren<Renderer>(true);
            var bounds=new Bounds();bool first=true;
            foreach(var r in renderers)
            {
                var mf=r.GetComponent<MeshFilter>();if(!mf||!mf.sharedMesh)continue;
                var b=mf.sharedMesh.bounds;var matrix=holder.worldToLocalMatrix*r.transform.localToWorldMatrix;
                for(int c=0;c<8;c++) {var v=matrix.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((c&1)==0?-1:1,(c&2)==0?-1:1,(c&4)==0?-1:1)));if(first){bounds=new Bounds(v,Vector3.zero);first=false;}else bounds.Encapsulate(v);}
            }
            if(first||bounds.size.y<.001f)throw new InvalidOperationException("Prop contains no valid mesh: "+key);
            float scale=height/bounds.size.y; model.transform.localScale=Vector3.one*scale;
            model.transform.localPosition=new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z)*scale;
            foreach(var c in model.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
            if(key=="cartridge_arch")model.AddComponent<GameThemeOccluder>();
            if(collision){var box=holder.gameObject.AddComponent<BoxCollider>();box.center=new Vector3(0,height*.48f,0);box.size=new Vector3(bounds.size.x*scale*.86f,height*.96f,bounds.size.z*scale*.84f);}
        }
        static GameObject Gate(string name,Transform parent,float z)
        {
            var t=Marker(name,parent,new Vector3(0,0,z)); var b=t.gameObject.AddComponent<BoxCollider>();b.center=new Vector3(0,1.5f,0);b.size=new Vector3(6.8f,3,.35f);
            for(int i=-3;i<=3;i++){var bar=GameObject.CreatePrimitive(PrimitiveType.Cube);bar.name="PixelSeal";bar.transform.SetParent(t,false);bar.transform.localPosition=new Vector3(i,1.5f,0);bar.transform.localScale=new Vector3(.07f,3,.07f);bar.GetComponent<Renderer>().sharedMaterial=Mats["Glow"];Object.DestroyImmediate(bar.GetComponent<Collider>());}
            return t.gameObject;
        }
        static Transform Group(string name,Transform parent){var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
        static Transform Marker(string name,Transform parent,Vector3 p){var t=Group(name,parent);t.localPosition=p;return t;}
        static void Solid(string name,Transform parent,Vector3 p,Vector3 size){var t=Marker(name,parent,p);t.gameObject.AddComponent<BoxCollider>().size=size;}
        static void Light(Transform parent,Vector3 p,Color color,float intensity,float range){var t=Marker("Exhibit light",parent,p);var l=t.gameObject.AddComponent<Light>();l.type=LightType.Point;l.color=color;l.intensity=intensity;l.range=range;l.shadows=LightShadows.None;}

        static void RegisterMission(LiminalStageDefinition stage)
        {
            var catalog=AssetDatabase.LoadAssetAtPath<GateMissionCatalog>("Assets/Liminal/Resources/GateMissions.asset");
            if(!catalog)throw new InvalidOperationException("Gate mission catalog must exist.");
            var list=catalog.Missions.Where(m=>m.id!="game_exhibition").ToList();
            list.Add(new GateMissionDefinition { id="game_exhibition",code="G-06",title="멈추지 않는 게임",destinationId="game",destinationName="게임의 잔상",
                briefing="눌린 칸은 길이 되고, 닫힌 칸은 벽이 된다. 거대한 블록과 게임 기기 사이를 통과하라.",difficulty=2,difficultyName="보통",healthMultiplier=1,
                enemyGimmicks=GateEnemyGimmick.None,gimmickName="복셀 군단",gimmickDescription="픽셀 아귀의 돌진 · 비트 파수꾼의 탄막 · 스택 수호자의 내려찍기. 바닥의 공격 예고를 보고 피하라.",
                objective="세 전투 구역을 돌파하고 출구로 이동",stages=new[]{stage}});
            catalog.missions=list.ToArray();EditorUtility.SetDirty(catalog);
        }
        static void CreateScene(LiminalStageDefinition stage)
        {
            var scene=EditorSceneManager.OpenScene("Assets/Liminal/Scenes/LiminalRun.unity",OpenSceneMode.Single);
            stage=AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(Root+"/Data/Stage_Game.asset");
            EditorSceneManager.SaveScene(scene,ScenePath);
            var director=Object.FindFirstObjectByType<LiminalRunDirector>();director.name="GAME / Exhibition Run";director.stages=new[]{stage};director.startInLobby=false;
            director.GenerateFirstStagePreview();director.player.position=director.Rooms[0].playerSpawn.position+Vector3.up*.05f;
            ConfigureEnvironment();var cam=Object.FindFirstObjectByType<IsometricFollowCamera>();cam.distance=24;cam.pitch=55;cam.yaw=35;cam.Snap();
            EditorSceneManager.SaveScene(scene,ScenePath);
        }
        public static void ConfigureEnvironment()
        {
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Ambient;RenderSettings.skybox=null;
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogColor=new Color(.13f,.19f,.27f);RenderSettings.fogDensity=.006f;
            foreach(var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))if(l.type==LightType.Directional){l.color=new Color(1,.92f,.8f);l.intensity=1.85f;l.shadows=LightShadows.Soft;l.shadowStrength=.6f;l.transform.rotation=Quaternion.Euler(52,-28,0);}
            var fill=new GameObject("Game sky fill").AddComponent<Light>();fill.type=LightType.Directional;fill.shadows=LightShadows.None;fill.color=new Color(.54f,.74f,1);fill.intensity=.55f;fill.transform.rotation=Quaternion.Euler(35,150,0);
            foreach(var volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))Object.DestroyImmediate(volume.gameObject);
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root+"/Profiles/Game.asset");
            if(!profile){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,Root+"/Profiles/Game.asset");}
            var bloom=Component<Bloom>(profile);bloom.intensity.Override(.25f);bloom.threshold.Override(1.1f);bloom.scatter.Override(.55f);
            Component<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var grade=Component<ColorAdjustments>(profile);grade.postExposure.Override(.35f);grade.contrast.Override(5);grade.saturation.Override(3);
            Component<Vignette>(profile).intensity.Override(.14f);EditorUtility.SetDirty(profile);
            var global=new GameObject("Game exhibition atmosphere").AddComponent<Volume>();global.isGlobal=true;global.sharedProfile=profile;
            if(Camera.main){Camera.main.backgroundColor=RenderSettings.fogColor;var data=Camera.main.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.volumeLayerMask=1;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;}
        }
        static T Component<T>(VolumeProfile p) where T:VolumeComponent {if(p.TryGet<T>(out var c))return c;c=p.Add<T>(true);AssetDatabase.AddObjectToAsset(c,p);return c;}

        /// <summary>Hard-surface bevels are combined per material: hundreds of tiles do not mean hundreds of draw calls.</summary>
        sealed class Geometry
        {
            public readonly Transform root;readonly Dictionary<string,MeshData> batches=new Dictionary<string,MeshData>();
            public Geometry(Transform root){this.root=root;}
            public void Box(string mat,Vector3 p,Vector3 size,float bevel=0,float yaw=0)
            {
                if(!batches.TryGetValue(mat,out var data)){data=new MeshData();batches.Add(mat,data);}
                Vector3 h=size*.5f;float b=Mathf.Min(bevel,Mathf.Min(h.x,Mathf.Min(h.y,h.z))*.8f);Vector3 inner=h-Vector3.one*b;Quaternion q=Quaternion.Euler(0,yaw,0);
                for(int axis=0;axis<3;axis++)for(int s=-1;s<=1;s+=2)
                {
                    int a=(axis+1)%3,c=(axis+2)%3;var pts=new Vector3[4];int[] sa={-1,1,1,-1},sc={-1,-1,1,1};
                    for(int n=0;n<4;n++){pts[n][axis]=h[axis]*s;pts[n][a]=inner[a]*sa[n];pts[n][c]=inner[c]*sc[n];}
                    var normal=Vector3.zero;normal[axis]=s;data.Face(pts,normal,p,q);
                }
                if(b<=0)return;
                for(int a=0;a<3;a++)for(int c=a+1;c<3;c++)for(int sa=-1;sa<=1;sa+=2)for(int sc=-1;sc<=1;sc+=2)
                {
                    int d=3-a-c;var pts=new Vector3[4];
                    for(int n=0;n<4;n++){bool high=n==0||n==3;pts[n][a]=(high?h[a]:inner[a])*sa;pts[n][c]=(high?inner[c]:h[c])*sc;pts[n][d]=inner[d]*(n<2?-1:1);}
                    var normal=Vector3.zero;normal[a]=sa;normal[c]=sc;data.Face(pts,normal.normalized,p,q);
                }
                for(int sx=-1;sx<=1;sx+=2)for(int sy=-1;sy<=1;sy+=2)for(int sz=-1;sz<=1;sz+=2)
                {var sign=new Vector3(sx,sy,sz);var v=Vector3.Scale(inner,sign);data.Face(new[]{v+new Vector3(sx*b,0,0),v+new Vector3(0,sy*b,0),v+new Vector3(0,0,sz*b)},sign.normalized,p,q);}
            }
            public void Flush(string id)
            {
                var parent=Group("Batched architecture",root);
                foreach(var pair in batches)
                {
                    string path=Root+"/Meshes/"+id+"_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}else mesh.Clear();
                    mesh.name=id+"_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;mesh.SetVertices(pair.Value.vertices);mesh.SetNormals(pair.Value.normals);mesh.SetUVs(0,pair.Value.uv);mesh.SetTriangles(pair.Value.tris,0);mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
                    var t=Group(pair.Key,parent);t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;var r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=Mats[pair.Key];
                }
            }
        }
        sealed class MeshData
        {
            public List<Vector3> vertices=new List<Vector3>(),normals=new List<Vector3>();public List<Vector2> uv=new List<Vector2>();public List<int> tris=new List<int>();
            public void Face(Vector3[] points,Vector3 normal,Vector3 p,Quaternion q)
            {
                if(Vector3.Dot(Vector3.Cross(points[1]-points[0],points[2]-points[0]),normal)<0)Array.Reverse(points);
                int start=vertices.Count;foreach(var v in points){vertices.Add(p+q*v);normals.Add(q*normal);uv.Add(new Vector2(v.x+v.z,v.y+v.z));}
                for(int i=1;i<points.Length-1;i++){tris.Add(start);tris.Add(start+i);tris.Add(start+i+1);}
            }
        }
    }
}
