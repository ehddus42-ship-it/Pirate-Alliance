using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 콤보 클립 자동 등록: 에디터가 스크립트를 다시 불러올 때 (그리고 메뉴 Tools > Proi > Update Attack Clips)
///  1) Proi_Character.fbx 의 임포트 클립 목록에 Attack1 / Attack2 / Attack3 / Recover2 를 추가 (루프 없음)
///  2) Proi.controller 에 같은 이름의 상태를 추가 (전환은 ProiCombat 이 CrossFade 로 처리)
/// 이미 되어 있으면 아무것도 하지 않습니다.
/// </summary>
[InitializeOnLoad]
public static class ProiAttackSetup
{
    const string Fbx = "Assets/Characters/Proi/Models/Proi_Character.fbx";
    const string ControllerPath = "Assets/Characters/Proi/Animations/Proi.controller";
    public static readonly string[] AttackClips = { "Attack1", "Attack2", "Attack3", "Recover2" };

    static ProiAttackSetup()
    {
        EditorApplication.delayCall += () => Ensure(false);
    }

    [MenuItem("Tools/Proi/Update Attack Clips")]
    static void FromMenu() => Ensure(true);

    static readonly string[] FireTextures = { "Assets/Characters/Proi/Resources/ProiEmber.png", "Assets/Characters/Proi/Resources/ProiFlameCel.png",
                                                 "Assets/Characters/Proi/Resources/ProiImpactStar.png" };

    /// <summary>불티 / 대쉬 셀 화염 텍스처 임포트 설정 (Clamp, 투명). Blaze 에셋은 ProiBlazeSetup 이 맡습니다.</summary>
    static void EnsureFireFx()
    {
        foreach (string path in FireTextures)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter ti &&
                (ti.wrapMode != TextureWrapMode.Clamp || !ti.alphaIsTransparency))
            {
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaIsTransparency = true;
                ti.SaveAndReimport();
                Debug.Log("[ProiAttackSetup] Fire texture import settings updated: " + path);
            }
        }
    }

    public static void Ensure(bool verbose)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        EnsureFireFx();
        var mi = AssetImporter.GetAtPath(Fbx) as ModelImporter;
        if (mi == null) return;

        // ---- 1) clips in the FBX importer
        var defaults = mi.defaultClipAnimations;
        var clips = mi.clipAnimations.ToList();
        bool changed = false;
        foreach (string name in AttackClips)
        {
            ModelImporterClipAnimation take = defaults.FirstOrDefault(c => c.takeName.EndsWith(name));
            if (take == null) continue;                       // the FBX has no such take (yet)
            ModelImporterClipAnimation clip = clips.FirstOrDefault(c => c.name == name);
            if (clip == null)
            {
                take.name = name;
                take.loopTime = false;
                take.loopPose = false;
                clips.Add(take);
                changed = true;
            }
            else if (clip.firstFrame != take.firstFrame || clip.lastFrame != take.lastFrame)
            {
                clip.firstFrame = take.firstFrame;                // re-exported with another length
                clip.lastFrame = take.lastFrame;
                changed = true;
            }
        }
        if (changed)
        {
            mi.clipAnimations = clips.ToArray();
            mi.SaveAndReimport();
            Debug.Log("[ProiAttackSetup] FBX clips: " + string.Join(", ", clips.Select(c => $"{c.name} {c.firstFrame}-{c.lastFrame}")));
        }

        // ---- 2) states in the animator controller
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) return;
        AnimationClip[] assets = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>().ToArray();
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        bool controllerChanged = false;
        for (int i = 0; i < AttackClips.Length; i++)
        {
            string name = AttackClips[i];
            AnimationClip motion = assets.FirstOrDefault(c => c.name == name);
            if (motion == null) continue;
            AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
            if (state == null)
            {
                state = sm.AddState(name, new Vector3(560f, 60f * i));
                controllerChanged = true;
            }
            if (state.motion != motion)
            {
                state.motion = motion;
                controllerChanged = true;
            }
        }
        if (controllerChanged)
        {
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[ProiAttackSetup] Animator states added: " + string.Join(", ", AttackClips));
        }
        else if (verbose)
        {
            Debug.Log("[ProiAttackSetup] Attack clips are up to date.");
        }
    }
}
