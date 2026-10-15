using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AcRoguelike.EditorTools
{
    /// <summary>
    /// Builds the katana motions once when the project opens on a machine that still has the old talisman clips,
    /// so the melee basic attack plays without a manual step. Menu: AC Roguelike/Astraia/Rebuild Katana Motions.
    /// </summary>
    [InitializeOnLoad]
    static class AstraiaKatanaMotionInstaller
    {
        static AstraiaKatanaMotionInstaller()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (!File.Exists(AstraiaAnimationBuilder.ControllerPath)) return;
                if (File.Exists(AstraiaAnimationBuilder.Folder + "/" + AstraiaAnimationBuilder.ComboVersion + ".anim")) return;
                if (!File.Exists(AstraiaAnimationBuilder.KatanaFolder + "/Katana_IaiDraw.fbx")) return;
                Debug.Log("[Astraia] Building the katana combo motions (Attack1-6) for the melee basic attack.");
                AstraiaAnimationBuilder.Build();
            };
        }

        [MenuItem("AC Roguelike/Astraia/Rebuild Katana Motions")]
        static void Rebuild() { AstraiaAnimationBuilder.Build(); Debug.Log("[Astraia] Katana motions rebuilt."); }
    }

    /// <summary>
    /// On a fresh clone the katana FBX files may finish importing (as Humanoid) after the first delayCall; build the
    /// combo as soon as they are in, instead of keeping the procedural fallback until the next restart.
    /// </summary>
    sealed class AstraiaKatanaImportWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!imported.Any(p => p.StartsWith(AstraiaAnimationBuilder.KatanaFolder + "/", StringComparison.Ordinal) && p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(AstraiaAnimationBuilder.ControllerPath)) return;
                if (File.Exists(AstraiaAnimationBuilder.Folder + "/" + AstraiaAnimationBuilder.ComboVersion + ".anim")) return;
                AstraiaAnimationBuilder.Build();
            };
        }
    }

    /// <summary>Rebuildable Humanoid motion set. Combo root travel is applied by MeleeSlash, not the Animator.</summary>
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
            // Katana basic attack (MeleeSlash): six fast full-body motion-captured hits in the style of Yae Sakura.
            var attacks = MakeKatanaCombo() ?? new[] { MakeSlash(idle, 0), MakeSlash(idle, 1), MakeSlash(idle, 2) };
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
                var state = sm.AddState("Attack" + (i + 1), new Vector3(510 + i * 220, 180 + (i % 2) * 70));
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

        public const string KatanaFolder = Folder + "/Katana";

        /// <summary>
        /// One combo hit cut from a source motion (Meshy rig + text-to-motion, imported as Humanoid): start and end in
        /// seconds, the playback speed baked into the clip, and the aim. The whole body moves as captured (hips, waist,
        /// legs and the body turn), but the body turn is corrected so the cut lands toward the target: at `aimTime` the
        /// sword tip, measured `aimRel` degrees to the right of the body, points straight forward. The correction
        /// ramps in during the wind-up and starts from the previous hit's final facing, so hits chain without a turn
        /// pop. Segment bounds sit at slow sword-tip moments. Timing must match MeleeSlash.Durations / HitTimes.
        /// </summary>
        static readonly (string file, float start, float end, float speed, float aimTime, float aimRel)[] KatanaSegments =
        {
            ("Katana_Flurry", .48f, .84f, 1.6f, .68f, 54f),        // ta: stepping low sweep, right to left
            ("Katana_Flurry", .90f, 1.24f, 1.6f, 1.12f, 51f),      // ta: rising backhand, left to right
            ("Katana_Flurry", 1.26f, 1.66f, 1.55f, 1.48f, 1f),     // tak: cut back across
            ("Katana_Pirouette", 1.55f, 2.05f, 1.45f, 1.85f, -24f), // full-turn pirouette cut (aim kept loose so the turn stays whole)
            ("Katana_DashSlash", .40f, 1.26f, 1.7f, .80f, 13f),   // low dash through the target, two cuts
            ("Katana_IaiDraw", .45f, 1.40f, 1.45f, .70f, 63f),    // iai draw and a heavy second cut, short zanshin
        };

        /// <summary>Bumped when the combo changes, so the installer rebuilds clips made by an older version.</summary>
        public const string ComboVersion = "Astraia_Katana_06";

        static AnimationClip[] MakeKatanaCombo()
        {
            var clips = new AnimationClip[KatanaSegments.Length];
            float facing = float.NaN; // body yaw at the end of the previous hit, in the attack frame
            for (int i = 0; i < clips.Length; i++)
            {
                var (file, start, end, speed, aimTime, aimRel) = KatanaSegments[i];
                string path = KatanaFolder + "/" + file + ".fbx";
                var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).OrderByDescending(c => c.length).FirstOrDefault();
                if (!source || !AnimationUtility.GetCurveBindings(source).Any(b => b.type == typeof(Animator)))
                {
                    Debug.LogWarning("[Astraia] Humanoid katana motion missing (" + path + "); using the procedural slashes. Run git lfs pull.");
                    return null;
                }
                end = Mathf.Min(end, source.length);
                clips[i] = Segment(source, "Astraia_Katana_0" + (i + 1), start, end, speed, aimTime, aimRel, i == clips.Length - 1, ref facing);
            }
            return clips;
        }

        static readonly string[] RootQ = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
        static readonly string[] RootT = { "RootT.x", "RootT.y", "RootT.z" };

        static AnimationClip Segment(AnimationClip source, string name, float start, float end, float speed,
            float aimTime, float aimRel, bool last, ref float facing)
        {
            var clip = new AnimationClip { name = name, frameRate = 60 };
            const float step = 1f / 60f;
            var bindings = AnimationUtility.GetCurveBindings(source).Where(b => b.type == typeof(Animator)).ToArray();
            AnimationCurve Curve(string property)
            {
                var b = bindings.FirstOrDefault(x => x.propertyName == property);
                return b.propertyName == property ? AnimationUtility.GetEditorCurve(source, b) : null;
            }
            var q = RootQ.Select(Curve).ToArray();
            var p = RootT.Select(Curve).ToArray();
            bool hasRoot = q.All(c => c != null) && p.All(c => c != null);
            Quaternion Q(float t) => hasRoot ? new Quaternion(q[0].Evaluate(t), q[1].Evaluate(t), q[2].Evaluate(t), q[3].Evaluate(t)).normalized : Quaternion.identity;
            Vector3 P(float t) => hasRoot ? new Vector3(p[0].Evaluate(t), p[1].Evaluate(t), p[2].Evaluate(t)) : Vector3.zero;
            float Yaw(float t) { Vector3 f = Q(t) * Vector3.forward; return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; }

            var times = new List<float>();
            for (float t = start; t <= end + 1e-4f; t += step) times.Add(Mathf.Min(t, end));
            // Yaw correction: from the previous hit's final facing (or straight ahead for the opener) to the aim, eased
            // in over the wind-up; the last hit also settles back toward straight ahead during its hold.
            float aimCorrection = -(Yaw(aimTime) + aimRel);
            float startCorrection = float.IsNaN(facing) ? -Yaw(start) : facing - Yaw(start);
            startCorrection = aimCorrection + Mathf.DeltaAngle(aimCorrection, startCorrection);
            float rampEnd = Mathf.Max(start + step, aimTime - .05f);
            float settleFrom = Mathf.Min(end, aimTime + (end - aimTime) * .45f);
            float Correction(float t)
            {
                float c = Mathf.Lerp(startCorrection, aimCorrection, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(start, rampEnd, t)));
                if (last && t > settleFrom)
                {
                    float settled = aimCorrection + Mathf.DeltaAngle(aimCorrection + Yaw(end), 0);
                    c = Mathf.Lerp(aimCorrection, settled, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(settleFrom, end, t)));
                }
                return c;
            }

            var rootQ = new List<Keyframe>[4];
            var rootT = new List<Keyframe>[3];
            for (int k = 0; k < 4; k++) rootQ[k] = new List<Keyframe>();
            for (int k = 0; k < 3; k++) rootT[k] = new List<Keyframe>();
            Quaternion previous = Quaternion.identity;
            Vector3 travel = P(start);
            for (int i = 0; i < times.Count; i++)
            {
                float t = times[i], time = (t - start) / speed;
                var turn = Quaternion.AngleAxis(Correction(t), Vector3.up);
                Quaternion rotation = turn * Q(t);
                if (i > 0 && Quaternion.Dot(rotation, previous) < 0) rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                previous = rotation;
                // Horizontal travel turns with the body, so steps still go where the body faces.
                if (i > 0) travel += turn * Vector3.ProjectOnPlane(P(t) - P(times[i - 1]), Vector3.up);
                Vector3 position = new Vector3(travel.x, P(t).y, travel.z);
                for (int k = 0; k < 4; k++) rootQ[k].Add(new Keyframe(time, rotation[k]));
                for (int k = 0; k < 3; k++) rootT[k].Add(new Keyframe(time, position[k]));
            }
            facing = Mathf.DeltaAngle(0, Yaw(end) + Correction(end));

            foreach (var binding in bindings)
            {
                AnimationCurve segment;
                int qi = Array.IndexOf(RootQ, binding.propertyName), ti = Array.IndexOf(RootT, binding.propertyName);
                if (hasRoot && qi >= 0) segment = new AnimationCurve(rootQ[qi].ToArray());
                else if (hasRoot && ti >= 0) segment = new AnimationCurve(rootT[ti].ToArray());
                else
                {
                    var curve = AnimationUtility.GetEditorCurve(source, binding);
                    segment = new AnimationCurve(times.Select(t => new Keyframe((t - start) / speed, curve.Evaluate(t))).ToArray());
                }
                for (int k = 0; k < segment.length; k++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(segment, k, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(segment, k, AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(clip, binding, segment);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            // Body turn and height stay in the pose. Horizontal travel is root motion: MeleeSlash moves the
            // CharacterController by it, so the steps and the dash really carry the player and the feet do not slide.
            settings.loopBlendOrientation = true;
            settings.loopBlendPositionY = true;
            settings.loopBlendPositionXZ = false;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return Save(clip);
        }

        // Procedural fallback slashes (used only when the katana FBX motions are not available).
        public static readonly float[] SlashDurations = { .44f, .48f, .66f };
        public static readonly float[] SlashHits = { .15f, .17f, .30f };

        static AnimationClip MakeSlash(AnimationClip idle, int index)
        {
            float d = SlashDurations[index], h = SlashHits[index];
            var clip = PoseClip(idle, "Astraia_Slash_0" + (index + 1), d, .2f);
            // A closed right hand around the grip for every swing.
            foreach (string finger in new[] { "Index", "Middle", "Ring", "Little" })
                for (int j = 1; j <= 3; j++) Hold(clip, "RightHand." + finger + "." + j + " Stretched", d, -.75f);
            Hold(clip, "RightHand.Thumb.2 Stretched", d, -.35f);
            Hold(clip, "RightHand.Thumb.3 Stretched", d, -.35f);
            float w = h * .55f, f = h + .08f, r = d * .82f;
            if (index == 0)
            {
                // Right-to-left horizontal cut: wind back to the right, whip across the body, follow through left.
                Keys(clip, "Right Arm Front-Back", d, (w, .55f), (h, -.85f), (f, -1f), (r, -.6f));
                Keys(clip, "Right Arm Down-Up", d, (w, .15f), (h, -.05f), (f, -.25f), (r, -.45f));
                Keys(clip, "Right Forearm Stretch", d, (w, -.45f), (h, .75f), (f, .6f), (r, .2f));
                Keys(clip, "Right Arm Twist In-Out", d, (w, .35f), (h, -.3f), (f, -.35f));
                Keys(clip, "Right Hand Down-Up", d, (w, .3f), (h, -.2f), (f, -.3f));
                Keys(clip, "Chest Twist Left-Right", d, (w, -.35f), (h, .3f), (f, .4f), (r, .15f));
                Keys(clip, "Spine Twist Left-Right", d, (w, -.18f), (h, .16f), (f, .2f));
                Keys(clip, "Head Turn Left-Right", d, (w, .1f), (h, -.08f));
                Keys(clip, "Left Arm Down-Up", d, (w, -.45f), (h, -.3f), (f, -.35f));
                Keys(clip, "Left Arm Front-Back", d, (w, -.3f), (h, .35f), (f, .3f));
                Keys(clip, "Right Upper Leg Front-Back", d, (w, .1f), (h, .35f), (f, .35f), (r, .15f));
                Keys(clip, "Right Lower Leg Stretch", d, (h, .55f), (f, .55f));
                Keys(clip, "Left Upper Leg Front-Back", d, (h, -.2f), (f, -.2f));
            }
            else if (index == 1)
            {
                // Rising backhand: from low across the body up and out to the right.
                Keys(clip, "Right Arm Front-Back", d, (w, -.9f), (h, .15f), (f, .35f), (r, .1f));
                Keys(clip, "Right Arm Down-Up", d, (w, -.65f), (h, .55f), (f, .7f), (r, .1f));
                Keys(clip, "Right Forearm Stretch", d, (w, .1f), (h, .85f), (f, .8f), (r, .3f));
                Keys(clip, "Right Arm Twist In-Out", d, (w, -.5f), (h, .3f), (f, .35f));
                Keys(clip, "Right Hand Down-Up", d, (w, -.35f), (h, .35f), (f, .4f));
                Keys(clip, "Chest Twist Left-Right", d, (w, .4f), (h, -.25f), (f, -.35f), (r, -.1f));
                Keys(clip, "Spine Twist Left-Right", d, (w, .2f), (h, -.15f), (f, -.18f));
                Keys(clip, "Chest Front-Back", d, (w, .15f), (h, -.12f), (f, -.15f));
                Keys(clip, "Head Nod Down-Up", d, (w, -.08f), (h, .1f));
                Keys(clip, "Left Arm Down-Up", d, (w, -.4f), (h, -.55f));
                Keys(clip, "Left Arm Front-Back", d, (w, .3f), (h, -.25f));
                Keys(clip, "Left Upper Leg Front-Back", d, (w, .1f), (h, .3f), (f, .3f), (r, .1f));
                Keys(clip, "Left Lower Leg Stretch", d, (h, .5f), (f, .5f));
                Keys(clip, "Right Upper Leg Front-Back", d, (h, -.18f), (f, -.18f));
            }
            else
            {
                // Iai finisher: sink into a draw stance with the hand at the left hip, then one wide cut to the right
                // with a deep lunge, and hold the finish (zanshin) before recovering.
                float hold = h + .2f;
                Keys(clip, "Right Arm Front-Back", d, (w, -.75f), (h * .85f, -.6f), (h, .7f), (hold, .85f), (r, .3f));
                Keys(clip, "Right Arm Down-Up", d, (w, -.75f), (h * .85f, -.7f), (h, -.02f), (hold, .05f), (r, -.35f));
                Keys(clip, "Right Forearm Stretch", d, (w, -.35f), (h * .85f, -.2f), (h, .9f), (hold, .9f), (r, .3f));
                Keys(clip, "Right Arm Twist In-Out", d, (w, -.4f), (h, .25f), (hold, .3f));
                Keys(clip, "Right Hand Down-Up", d, (w, -.3f), (h, .1f), (hold, .15f));
                Keys(clip, "Chest Twist Left-Right", d, (w, .5f), (h * .85f, .45f), (h, -.45f), (hold, -.5f), (r, -.15f));
                Keys(clip, "Spine Twist Left-Right", d, (w, .25f), (h, -.22f), (hold, -.25f));
                Keys(clip, "Spine Front-Back", d, (w, .25f), (h, .35f), (hold, .3f), (r, .1f));
                Keys(clip, "Chest Front-Back", d, (w, .1f), (h, .15f), (hold, .12f));
                Keys(clip, "Head Turn Left-Right", d, (w, -.15f), (h, .12f));
                Keys(clip, "Left Arm Down-Up", d, (w, -.6f), (h * .85f, -.6f), (h, -.25f), (hold, -.2f));
                Keys(clip, "Left Arm Front-Back", d, (w, .55f), (h, -.45f), (hold, -.5f));
                Keys(clip, "Left Forearm Stretch", d, (w, -.6f), (h, .5f), (hold, .5f));
                Keys(clip, "Right Upper Leg Front-Back", d, (w, .35f), (h, .7f), (hold, .7f), (r, .2f));
                Keys(clip, "Right Lower Leg Stretch", d, (w, .1f), (h, .35f), (hold, .35f), (r, .8f));
                Keys(clip, "Left Upper Leg Front-Back", d, (w, .15f), (h, -.45f), (hold, -.45f), (r, -.1f));
                Keys(clip, "Left Lower Leg Stretch", d, (w, .2f), (h, .45f), (hold, .45f), (r, .85f));
            }
            return Save(clip);
        }

        static void Hold(AnimationClip clip, string property, float length, float value)
        {
            var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), property);
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, length, value));
        }

        /// <summary>Keys a muscle at the given times; the clip starts and ends on the idle pose value.</summary>
        static void Keys(AnimationClip clip, string property, float length, params (float time, float value)[] keys)
        {
            var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), property);
            var old = AnimationUtility.GetEditorCurve(clip, binding);
            float start = old != null ? old.Evaluate(0) : 0;
            var frames = new List<Keyframe> { new Keyframe(0, start) };
            foreach (var (time, value) in keys) if (time > 0 && time < length) frames.Add(new Keyframe(time, value));
            frames.Add(new Keyframe(length, start));
            var curve = new AnimationCurve(frames.ToArray());
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            AnimationUtility.SetEditorCurve(clip, binding, curve);
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
