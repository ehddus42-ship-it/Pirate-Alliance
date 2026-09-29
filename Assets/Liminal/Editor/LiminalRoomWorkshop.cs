using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AcRoguelike.Liminal.Editor
{
    public sealed class LiminalRoomWorkshop : EditorWindow
    {
        Vector2 scroll;
        string filter="",newName="NewRoomVariation",validation="";
        int seed=73029,stageIndex;
        bool addCloneToStage=true;
        static readonly string[] StageNames={"01 기다림의 층","02 물이 시작되는 곳","03 마지막 환승","04 모든 출발"};

        [MenuItem("AC Roguelike/Liminal/Room Workshop")]
        public static void Open()
        {
            var window=GetWindow<LiminalRoomWorkshop>("Liminal Room Workshop");window.minSize=new Vector2(420,500);window.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("리미널 스페이스 · 방 작업실",EditorStyles.boldLabel);
            EditorGUILayout.LabelField("프리팹을 열어 수정하면 새로 생성되는 스테이지에도 반영돼.",EditorStyles.wordWrappedLabel);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("20개 방 갤러리",GUILayout.Height(29)))OpenScene(LiminalMapBuilder.GalleryPath);
                if(GUILayout.Button("24개 기물 갤러리",GUILayout.Height(29)))OpenScene(LiminalMapBuilder.PropGalleryPath);
                if(GUILayout.Button("게임 맵 열기",GUILayout.Height(29)))OpenScene(LiminalMapBuilder.RunPath);
            }
            EditorGUILayout.Space(8);
            filter=EditorGUILayout.TextField("방 찾기",filter);
            scroll=EditorGUILayout.BeginScrollView(scroll,GUILayout.MinHeight(140));
            foreach(var room in Rooms())
            {
                if(!string.IsNullOrEmpty(filter)&&room.name.IndexOf(filter,StringComparison.OrdinalIgnoreCase)<0&&room.displayName.IndexOf(filter,StringComparison.OrdinalIgnoreCase)<0)continue;
                using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(room.displayName+"  ·  "+room.kind,EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(room.name,EditorStyles.miniLabel);
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        if(GUILayout.Button("프리팹 열기"))AssetDatabase.OpenAsset(room.gameObject);
                        if(GUILayout.Button("프로젝트에서 찾기")){Selection.activeObject=room.gameObject;EditorGUIUtility.PingObject(room.gameObject);}
                        if(GUILayout.Button("씬에서 보기"))FrameRoom(room.roomId);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(7);
            EditorGUILayout.LabelField("선택한 방으로 새 바리에이션 만들기",EditorStyles.boldLabel);
            newName=EditorGUILayout.TextField("새 프리팹 이름",newName);
            stageIndex=EditorGUILayout.Popup("스테이지",stageIndex,StageNames);
            addCloneToStage=EditorGUILayout.Toggle("해당 스테이지 방 풀에 추가",addCloneToStage);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("선택한 방 복제"))CloneSelectedRoom();
                if(GUILayout.Button("선택한 방 변경 저장"))SaveSelectedRoom();
            }
            EditorGUILayout.HelpBox("Hierarchy: Architecture 건축 · Props 기물 · Lighting 조명 · Gameplay 스폰/문 · Sockets 입출구\n기물을 추가할 땐 Props 아래에 배치하고, Entry/Exit 소켓과 통로는 비워 둬.",MessageType.Info);
            seed=EditorGUILayout.IntField("생성 시드",seed);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("이 시드로 스테이지 미리보기"))PreviewStage();
                if(GUILayout.Button("모든 방 구조 검사")){validation=LiminalMapBuilder.ValidateAll();Debug.Log(validation);}
            }
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("가져온 Meshy 모델 반영")){LiminalMapBuilder.RefreshMeshyModels();validation=LiminalMapBuilder.ValidateAll();}
                if(GUILayout.Button("누락된 초기 에셋 생성"))LiminalMapBuilder.BuildMissing();
            }
            if(!string.IsNullOrEmpty(validation))EditorGUILayout.HelpBox(validation,MessageType.None);
            EditorGUILayout.Space(7);
        }

        static LiminalRoom[] Rooms()
        {
            if(!AssetDatabase.IsValidFolder(LiminalMapBuilder.RoomFolder))return Array.Empty<LiminalRoom>();
            return AssetDatabase.FindAssets("t:Prefab",new[]{LiminalMapBuilder.RoomFolder}).Select(g=>AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).Where(g=>g).Select(g=>g.GetComponent<LiminalRoom>()).Where(r=>r).OrderBy(r=>r.name).ToArray();
        }

        static void OpenScene(string path)
        {
            if(!File.Exists(path)){EditorUtility.DisplayDialog("맵이 아직 없어","먼저 '누락된 초기 에셋 생성'을 실행해 줘.","확인");return;}
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.OpenScene(path);
            if(path==LiminalMapBuilder.GalleryPath)LiminalMapBuilder.FrameGallery();
        }

        static LiminalRoom SelectedRoom()
        {
            var go=Selection.activeGameObject;
            if(go){var room=go.GetComponentInParent<LiminalRoom>();if(room)return room;}
            var stage=PrefabStageUtility.GetCurrentPrefabStage();
            return stage?stage.prefabContentsRoot.GetComponent<LiminalRoom>():null;
        }

        static void FrameRoom(string roomId)
        {
            var room=Object.FindObjectsByType<LiminalRoom>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(r=>r.roomId==roomId);
            if(!room)
            {
                OpenScene(LiminalMapBuilder.GalleryPath);
                room=Object.FindObjectsByType<LiminalRoom>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(r=>r.roomId==roomId);
            }
            if(!room)return;
            Selection.activeGameObject=room.gameObject;SceneView.lastActiveSceneView?.Frame(new Bounds(room.transform.TransformPoint(room.localBounds.center),room.localBounds.size+Vector3.one*3),false);
        }

        void CloneSelectedRoom()
        {
            var selected=SelectedRoom();
            if(!selected){EditorUtility.DisplayDialog("복제할 방 선택","먼저 Hierarchy나 Project에서 방 프리팹을 선택해 줘.","확인");return;}
            string safeName=string.Concat(newName.Where(c=>!Path.GetInvalidFileNameChars().Contains(c))).Trim();
            if(string.IsNullOrEmpty(safeName)){EditorUtility.DisplayDialog("이름 필요","새 방 이름을 입력해 줘.","확인");return;}
            string path=AssetDatabase.GenerateUniqueAssetPath(LiminalMapBuilder.RoomFolder+"/"+safeName+".prefab");
            var copy=Object.Instantiate(selected.gameObject);copy.name=Path.GetFileNameWithoutExtension(path);copy.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            if(PrefabUtility.IsPartOfPrefabInstance(copy))PrefabUtility.UnpackPrefabInstance(copy,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            var component=copy.GetComponent<LiminalRoom>();component.roomId=copy.name;component.displayName=copy.name;
            var prefab=PrefabUtility.SaveAsPrefabAsset(copy,path);Object.DestroyImmediate(copy);
            if(addCloneToStage&&stageIndex<3)
            {
                var stage=AssetDatabase.LoadAssetAtPath<LiminalStageDefinition>(LiminalMapBuilder.Root+"/Stages/Stage_"+(stageIndex+1).ToString("00")+".asset");
                if(stage){Undo.RecordObject(stage,"Add room variation to stage");stage.roomPool=stage.roomPool.Concat(new[]{prefab.GetComponent<LiminalRoom>()}).ToArray();EditorUtility.SetDirty(stage);AssetDatabase.SaveAssets();}
            }
            Selection.activeObject=prefab;AssetDatabase.OpenAsset(prefab);
            validation="새 바리에이션을 저장했어: "+path;
        }

        void SaveSelectedRoom()
        {
            var selected=SelectedRoom();if(!selected){validation="저장할 방을 먼저 선택해 줘.";return;}
            var stage=PrefabStageUtility.GetCurrentPrefabStage();
            if(stage&&selected.gameObject==stage.prefabContentsRoot)
            {
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot,stage.assetPath);validation="프리팹 변경을 저장했어.";return;
            }
            if(PrefabUtility.IsPartOfPrefabInstance(selected.gameObject))
            {
                PrefabUtility.ApplyPrefabInstance(selected.gameObject,InteractionMode.UserAction);
                validation="선택한 방의 변경을 원본 프리팹에 반영했어.";
            }
            else if(EditorUtility.IsPersistent(selected))AssetDatabase.SaveAssets();
            else validation="새로 만든 방이라면 '선택한 방 복제'로 프리팹을 만들어 줘.";
        }

        void PreviewStage()
        {
            if(EditorApplication.isPlaying){validation="미리보기는 Play 모드를 끈 뒤 사용할 수 있어.";return;}
            var director=Object.FindFirstObjectByType<LiminalRunDirector>();
            if(!director){OpenScene(LiminalMapBuilder.RunPath);director=Object.FindFirstObjectByType<LiminalRunDirector>();}
            if(!director)return;
            // Regeneration replaces only GeneratedRoute. Save authored changes first, then explicitly confirm the replacement.
            if(director.transform.Find("GeneratedRoute") && !EditorUtility.DisplayDialog("스테이지 미리보기 교체","GeneratedRoute에 직접 배치한 변경은 새 조립 결과로 바뀌어. 방 변경은 먼저 프리팹에 저장해 줘.","새 시드로 생성","취소"))return;
            Undo.RecordObject(director,"Change liminal preview seed");director.seed=seed;director.GeneratePreview(stageIndex);EditorUtility.SetDirty(director);EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
            Selection.activeGameObject=director.gameObject;SceneView.lastActiveSceneView?.FrameSelected();validation=StageNames[stageIndex]+" · 시드 "+seed+" 미리보기를 만들었어. Ctrl+S로 씬을 저장할 수 있어.";
        }
    }
}
