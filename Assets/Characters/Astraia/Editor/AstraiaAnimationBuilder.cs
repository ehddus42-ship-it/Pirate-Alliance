using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AcRoguelike.EditorTools
{
    /// <summary>Rebuildable Humanoid motion set. No root translation competes with the collision motor.</summary>
    public static class AstraiaAnimationBuilder
    {
        public const string Folder = "Assets/Characters/Astraia/Animations";
        public const string ControllerPath = Folder + "/Astraia.controller";

        public static AnimatorController Build()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            var idle = FindClip("HumanF@Idle01");
            var walk = FindClip("HumanF@Walk01_Forward");
            var run = FindClip("HumanF@Run01_Forward");
            var sprint = FindClip("HumanF@Sprint01_Forward");
            var attacks = new[] { MakeAttack(idle, 0, .52f), MakeAttack(idle, 1, .56f), MakeAttack(idle, 2, .70f) };
            var dash = MakeDash(sprint);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller)
            {
                controller.layers = Array.Empty<AnimatorControllerLayer>();
                controller.parameters = Array.Empty<AnimatorControllerParameter>();
                foreach (var child in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                    if (child && child != controller) UnityEngine.Object.DestroyImmediate(child, true);
                controller.AddLayer("Base Layer");
            }
            else controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            foreach (var p in new[] { "Speed", "MoveX", "MoveY", "AttackSpeed" }) controller.AddParameter(p, AnimatorControllerParameterType.Float);
            foreach (var p in new[] { "Dashing", "Attacking", "Grounded" }) controller.AddParameter(p, AnimatorControllerParameterType.Bool);
            foreach (var p in new[] { "Attack", "Dash" }) controller.AddParameter(p, AnimatorControllerParameterType.Trigger);
            controller.AddParameter("AttackIndex", AnimatorControllerParameterType.Int);
            var parameters = controller.parameters;
            foreach (var p in parameters) if (p.name == "AttackSpeed") p.defaultFloat = 1;
            controller.parameters = parameters;
            var sm = controller.layers[0].stateMachine;
            var locomotion = sm.AddState("Locomotion", new Vector3(260, 70));
            var tree = new BlendTree { name = "Idle - Walk - Run", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(idle, 0);
            tree.AddChild(walk, .5f);
            tree.AddChild(run, 1);
            locomotion.motion = tree;
            locomotion.iKOnFeet = true;
            sm.defaultState = locomotion;
            for (int i = 0; i < attacks.Length; i++)
            {
                var state = sm.AddState("Attack" + (i + 1), new Vector3(510 + i * 220, 180));
                state.motion = attacks[i];
                state.speedParameter = "AttackSpeed";
                state.speedParameterActive = true;
                state.iKOnFeet = false;
                // PlayerCombat owns the recovery / combo / cancel clock, including cooldown upgrades.
            }
            var dodge = sm.AddState("Dash", new Vector3(280, 320));
            dodge.motion = dash;
            dodge.iKOnFeet = false;
            var enter = sm.AddAnyStateTransition(dodge);
            enter.AddCondition(AnimatorConditionMode.If, 0, "Dashing");
            enter.duration = .045f;
            enter.hasFixedDuration = true;
            enter.hasExitTime = false;
            enter.canTransitionToSelf = false;
            var exit = dodge.AddTransition(locomotion);
            exit.AddCondition(AnimatorConditionMode.IfNot, 0, "Dashing");
            exit.duration = .11f;
            exit.hasFixedDuration = true;
            exit.hasExitTime = false;
            AssetDatabase.SaveAssets();
            return controller;
        }

        public static AnimationClip FindClip(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/ThirdParty/HumanBasicMotions" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != name) continue;
                return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => c.name == name);
            }
            throw new FileNotFoundException("Required Human Basic Motions clip: " + name);
        }

        static AnimationClip PoseClip(AnimationClip reference, string name, float duration, float sample)
        {
            var clip = new AnimationClip { name = name, frameRate = 60 };
            foreach (var binding in AnimationUtility.GetCurveBindings(reference))
            {
                if (binding.type != typeof(Animator)) continue;
                var value = AnimationUtility.GetEditorCurve(reference, binding).Evaluate(sample);
                // Retain the reference's body and IK coordinate conventions for Humanoid retargeting.
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, duration, value));
            }
            var settings = AnimationUtility.GetAnimationClipSettings(reference);
            settings.startTime = 0;
            settings.stopTime = duration;
            settings.loopTime = false;
            settings.loopBlend = false;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionXZ = true;
            settings.keepOriginalPositionY = true;
            settings.loopBlendOrientation = true;
            settings.loopBlendPositionXZ = true;
            settings.loopBlendPositionY = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        static AnimationClip MakeAttack(AnimationClip idle, int index, float duration)
        {
            var clip = PoseClip(idle, "Astraia_Cast_0" + (index + 1), duration, .2f);
            bool left = index == 1;
            string arm = left ? "Left" : "Right";
            string support = left ? "Right" : "Left";
            float sign = left ? -1 : 1;
            // Anticipation, crisp palm release and a softer follow-through; all are normalized muscles.
            Gesture(clip, "Chest Twist Left-Right", duration, -.12f * sign, .23f * sign, .08f * sign);
            Gesture(clip, "Spine Twist Left-Right", duration, -.06f * sign, .12f * sign, .04f * sign);
            Gesture(clip, "Chest Front-Back", duration, -.03f, .12f, .02f);
            Gesture(clip, "Head Turn Left-Right", duration, .04f * sign, -.08f * sign, -.03f * sign);
            Gesture(clip, arm + " Arm Down-Up", duration, -.48f, -.10f, -.36f);
            Gesture(clip, arm + " Arm Front-Back", duration, .20f, -.75f, -.35f);
            Gesture(clip, arm + " Arm Twist In-Out", duration, .04f, -.18f, -.06f);
            Gesture(clip, arm + " Forearm Stretch", duration, -.55f, .78f, .16f);
            Gesture(clip, arm + " Hand Down-Up", duration, -.10f, .32f, .08f);
            Gesture(clip, support + " Arm Down-Up", duration, -.58f, -.48f, -.62f);
            Gesture(clip, support + " Arm Front-Back", duration, -.12f, .16f, -.06f);
            Gesture(clip, support + " Forearm Stretch", duration, -.36f, -.22f, -.30f);
            if (index == 2)
            {
                Gesture(clip, "Left Arm Down-Up", duration, -.38f, -.06f, -.42f);
                Gesture(clip, "Left Arm Front-Back", duration, .32f, -.70f, -.28f);
                Gesture(clip, "Left Forearm Stretch", duration, -.58f, .66f, -.04f);
                Gesture(clip, "Spine Front-Back", duration, -.08f, .18f, .03f);
                Gesture(clip, "Chest Twist Left-Right", duration, -.16f, .06f, .01f);
            }
            return Save(clip);
        }

        static void Gesture(AnimationClip clip, string property, float length, float anticipate, float strike, float follow)
        {
            var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), property);
            var old = AnimationUtility.GetEditorCurve(clip, binding);
            float start = old != null ? old.Evaluate(0) : 0;
            float hit = length < .55f ? .18f : length < .6f ? .20f : .27f;
            var curve = new AnimationCurve(new Keyframe(0, start), new Keyframe(hit * .58f, anticipate),
                new Keyframe(hit, strike), new Keyframe(hit + .09f, follow), new Keyframe(length, start));
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        static AnimationClip MakeDash(AnimationClip sprint)
        {
            var clip = PoseClip(sprint, "Astraia_Dash", .24f, .12f);
            foreach (var pair in new Dictionary<string, float> {
                {"Spine Front-Back", .28f}, {"Chest Front-Back", .20f}, {"Head Nod Down-Up", -.08f},
                {"Left Arm Down-Up", -.65f}, {"Right Arm Down-Up", -.65f},
                {"Left Arm Front-Back", .28f}, {"Right Arm Front-Back", .28f},
                {"Left Forearm Stretch", -.24f}, {"Right Forearm Stretch", -.24f}})
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), pair.Key), AnimationCurve.Constant(0, .24f, pair.Value));
            return Save(clip);
        }

        static AnimationClip Save(AnimationClip clip)
        {
            string path = Folder + "/" + clip.name + ".anim";
            var saved = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (saved) { EditorUtility.CopySerialized(clip, saved); UnityEngine.Object.DestroyImmediate(clip); return saved; }
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }
    }
}
