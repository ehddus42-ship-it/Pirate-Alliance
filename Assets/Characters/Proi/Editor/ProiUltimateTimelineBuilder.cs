using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// 궁극기 Timeline 만들기: Tools > Proi > Build Ultimate Timeline
/// 지금 씬의 Player(ProiUltimate) 값으로 Assets/Characters/Proi/Timeline/ProiUltimate.playable 을 만들고 Player 에 PlayableDirector 를 붙여 연결합니다.
///  - 몸 동작: Ultimate.anim (Animator)
///  - 궁극기: 비트 마커(주먹 마주침 / 차징 / 도약 / 정점 / 내려꽂기 / 꽂힘 / 불기둥 1~3열 / 이동 가능) + 전진 이동 미리보기 구간
///  - 카메라: 샷 클립 (시작 순간 그 구도로 컷 / 이동, 움직이는 샷은 클립 동안 오빗 / 달리 / 줌)
///  - 배경 블러: 차징 동안 캐릭터에 초점, 주변 흐림 (심도)
///  - 배속: 게임 배속 커브
/// 이미 있으면 다시 만들지 물어봅니다 (Timeline 에서 고친 내용은 사라짐).
/// </summary>
public static class ProiUltimateTimelineBuilder
{
    const string Folder = "Assets/Characters/Proi/Timeline";
    const string AssetPath = Folder + "/ProiUltimate.playable";
    const string ClipPath = "Assets/Characters/Proi/Animations/Ultimate.anim";

    [MenuItem("Tools/Proi/Build Ultimate Timeline")]
    static void FromMenu()
    {
        if (AssetDatabase.LoadAssetAtPath<TimelineAsset>(AssetPath) != null &&
            !EditorUtility.DisplayDialog("Proi 궁극기 Timeline", AssetPath + " 이(가) 이미 있습니다.\n지금 ProiUltimate 값으로 다시 만들까요? (Timeline 에서 고친 내용은 사라집니다)", "다시 만들기", "취소"))
            return;
        Debug.Log("[ProiUltimateTimelineBuilder] " + Build());
    }

    static ProiShot Moving(ProiShot s, ProiEase ease, System.Action<ProiShot> end)
    {   // a shot that keeps moving over its clip: start from its own values, then change only what moves
        s.moving = true;
        s.moveEase = ease;
        s.WithEnd();
        end(s);
        return s;
    }

    public static string Build()
    {
        var ult = Object.FindFirstObjectByType<ProiUltimate>();
        if (ult == null) return "ProiUltimate 가 씬에 없습니다 (ProiPlayground 씬을 여세요)";
        var animator = ult.GetComponentInChildren<Animator>();
        var anim = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        var cam = Camera.main;
        if (animator == null || anim == null || cam == null) return "Animator / Ultimate.anim / Main Camera 를 찾지 못했습니다";
        var so = new SerializedObject(ult);
        float F(string n) => so.FindProperty(n).floatValue;
        Vector2 V2(string n) => so.FindProperty(n).vector2Value;
        Vector3 V3(string n) => so.FindProperty(n).vector3Value;

        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Characters/Proi", "Timeline");
        if (AssetDatabase.LoadAssetAtPath<Object>(AssetPath) != null) AssetDatabase.DeleteAsset(AssetPath);
        var tl = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(tl, AssetPath);

        // ---- body
        var body = tl.CreateTrack<AnimationTrack>(null, "몸 동작");
        body.trackOffset = TrackOffset.ApplySceneOffsets;          // never moves the model root
        var bodyClip = body.CreateClip(anim);
        bodyClip.displayName = "Ultimate";
        bodyClip.start = 0;
        bodyClip.easeInDuration = F("blendIn");                    // blends in from / back out to the Animator Controller
        bodyClip.easeOutDuration = F("blendOut");

        // ---- beats (marker times = scene times) + the arc preview
        float clash = F("clashTime"), cStart = F("chargeStart"), cFull = F("chargeFull"), cEnd = F("cameraBack");
        float leap = F("dashStart"), apex = F("apexTime"), dive = F("diveTime"), slam = F("slamTime"), end = F("endTime");
        Vector3 rows = V3("rowDelays");
        var beats = tl.CreateTrack<ProiUltimateTrack>(null, "궁극기");
        var move = beats.CreateClip<ProiUltimateMoveClip>();
        move.displayName = "전진 이동 (미리보기)";
        move.start = leap;
        move.duration = slam - leap + 0.3;
        void Beat(ProiBeat b, double t, string label)
        {
            var m = beats.CreateMarker<ProiUltimateBeat>(t);
            m.beat = b;
            m.name = label;
        }
        Beat(ProiBeat.Clash, clash, "주먹 마주침");
        Beat(ProiBeat.ChargeStart, cStart, "차징 시작");
        Beat(ProiBeat.ChargeFull, cFull, "풀차지");
        Beat(ProiBeat.ChargeEnd, cEnd, "차징 끝 (웅크림)");
        Beat(ProiBeat.Leap, leap, "도약");
        Beat(ProiBeat.Apex, apex, "정점");
        Beat(ProiBeat.Dive, dive, "내려꽂기");
        Beat(ProiBeat.Slam, slam, "꽂힘");
        Beat(ProiBeat.Row1, slam + rows.x, "불기둥 1열");
        Beat(ProiBeat.Row2, slam + rows.y, "불기둥 2열");
        Beat(ProiBeat.Row3, slam + rows.z, "불기둥 3열");
        Beat(ProiBeat.End, end, "이동 가능");

        // ---- camera shots (the moves glide with a smooth ease unless set otherwise)
        Vector3 open = V3("openShot"), closeUp = V3("clashCloseUp"), diveCam = V3("diveCamera");
        Vector2 push = V2("clashPush"), pull = V2("clashPullOut"), jumpCut = V2("jumpCut");
        float yaw = F("frontYaw"), fov = F("frontFov");
        var shots = new List<(double t, string name, ProiShot s)>
        {
            (0, "정면 상반신 (컷)", new ProiShot { type = ProiShotType.Front, blend = F("cameraBlendIn"), ease = ProiEase.Smooth, distance = open.x, height = open.y, lookHeight = open.z, yaw = yaw, fov = fov }),
            (push.x, "주먹 크래시 줌", new ProiShot { type = ProiShotType.Fists, blend = push.y, ease = ProiEase.Smooth, distance = closeUp.x, height = closeUp.y, lookHeight = 1.33f, yaw = yaw, fov = closeUp.z }),
            // fists met -> straight onto the fist (a strong accelerate / decelerate), arriving before the fire starts to
            // gather, then a slow push-in for the whole charge: the fire gathering in the fist is the subject, her face over it
            (pull.x, "차징 주먹 (불꽃 모임)", Moving(new ProiShot { type = ProiShotType.ChargeFist, blend = 0.3f, ease = ProiEase.EaseInOut, distance = 2.2f, height = 0.05f, lookHeight = 0f, yaw = 48f, fov = 32f },
                                     ProiEase.Smooth, s => { s.endDistance = 1.6f; s.endYaw = 40f; s.endFov = 30f; })),
            (jumpCut.x, "도약 와이드 (컷)", new ProiShot { type = ProiShotType.ArcWide, blend = jumpCut.y, ease = ProiEase.Smooth, distance = F("jumpCameraDistance") }),
            (apex, "착지점 당김", new ProiShot { type = ProiShotType.Landing, blend = diveCam.z, ease = ProiEase.Smooth, distance = diveCam.x, fovOffset = -diveCam.y }),
            (slam + F("wideShotDelay"), "불기둥 와이드", new ProiShot { type = ProiShotType.PillarWide, blend = 0.6f, ease = ProiEase.Smooth, distance = F("pillarCameraDistance") }),
            (slam + rows.z + F("pillarLife") * 0.6f, "기본 카메라 복귀", new ProiShot { type = ProiShotType.Gameplay, blend = 0.8f, ease = ProiEase.Smooth }),
        };
        var camTrack = tl.CreateTrack<ProiCameraShotTrack>(null, "카메라");
        for (int i = 0; i < shots.Count; i++)
        {
            var c = camTrack.CreateClip<ProiCameraShotClip>();
            c.start = shots[i].t;
            c.duration = i + 1 < shots.Count ? Mathf.Max((float)(shots[i + 1].t - shots[i].t), 0.05f) : 0.1;   // the ultimate ends with the timeline
            c.displayName = shots[i].name;
            ((ProiCameraShotClip)c.asset).shot = shots[i].s;
        }

        // ---- background blur while she charges (fills in / drains with the clip's ease)
        var blurTrack = tl.CreateTrack<ProiBlurTrack>(null, "배경 블러");
        var blur = blurTrack.CreateClip<ProiBlurClip>();
        blur.displayName = "차징 배경 블러";
        blur.start = pull.x;
        blur.duration = jumpCut.x - pull.x;
        blur.easeInDuration = 0.5;
        blur.easeOutDuration = 0.12;
        ((ProiBlurClip)blur.asset).fistFocus = 0.7f;           // sharp on the gathering fire, the face just soft

        // ---- game speed
        var warpProp = so.FindProperty("timeWarp");
        var curve = new AnimationCurve();
        for (int i = 0; i < warpProp.arraySize; i++)
        {
            Vector2 k = warpProp.GetArrayElementAtIndex(i).vector2Value;
            curve.AddKey(new Keyframe(k.x, k.y));
        }
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        var warpTrack = tl.CreateTrack<ProiTimeWarpTrack>(null, "배속");
        var w = warpTrack.CreateClip<ProiTimeWarpClip>();
        w.displayName = "게임 배속";
        w.start = 0;
        w.duration = curve.length > 0 ? curve[curve.length - 1].time + 0.05f : 1f;
        ((ProiTimeWarpClip)w.asset).speed = curve;

        EditorUtility.SetDirty(tl);
        AssetDatabase.SaveAssets();

        // ---- the director on the player
        var director = ult.GetComponent<PlayableDirector>();
        if (director == null) director = ult.gameObject.AddComponent<PlayableDirector>();
        director.playableAsset = tl;
        director.playOnAwake = false;
        director.extrapolationMode = DirectorWrapMode.None;
        director.timeUpdateMode = DirectorUpdateMode.GameTime;
        director.SetGenericBinding(body, animator);
        director.SetGenericBinding(beats, ult);
        director.SetGenericBinding(camTrack, cam);
        so.Update();
        so.FindProperty("cutscene").objectReferenceValue = director;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(director);
        EditorSceneManager.MarkSceneDirty(ult.gameObject.scene);
        EditorSceneManager.SaveScene(ult.gameObject.scene);
        return "built " + AssetPath + " (" + tl.duration.ToString("F2") + "s, " + shots.Count + " camera shots) and bound it on " + ult.name;
    }
}
