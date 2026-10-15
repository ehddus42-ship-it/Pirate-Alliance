using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 기술 동작 클립 굽기: Tools > Proi > Bake Skill Clips (궁극기 Ultimate + 우클릭 잡아 던지기 Throw)
/// [궁극기(R)]
///  1) 가슴 앞에서 두 주먹을 마주 침  2) 깊은 런지(왼발 앞, 오른다리 뒤로 뻗음), 왼팔 가드 + 오른주먹을 허리에 장전하며 화염 차징
///  3) 웅크렸다가 수직으로 높이 솟아 정점에서 몸을 눕혀 앞으로 날아가는 아치 점프 (전진 이동은 ProiUltimate 가 캐릭터를 옮김)
///  4) 대각선으로 내리꽂히며 오른주먹으로 땅을 찍음
///  (한쪽 무릎을 꿇는 착지)  5) 일어나 대기 자세로
/// 아래 Poses 표(캐릭터 공간: x 오른쪽, y 위, z 앞, 발밑 원점)의 손 / 발 목표와 골반 / 척추 / 머리 각도를
/// 2본 IK 로 리그에 적용해 Assets/Characters/Proi/Animations/Ultimate.anim 으로 굽고, Proi.controller 에 "Ultimate" 상태를 추가합니다.
/// [대쉬(Shift)] 몸을 앞으로 숙이고 두 팔을 뒤로 뻗은 채 뒷다리를 끌며 미끄러지고(0~0.2초) 앞발로 브레이크(0.3초) -> Dash.anim / "Dash".
/// [잡아 던지기(우클릭), 야구 투수 폼] 기본 자세에서 오른손을 앞으로 뻗어 잡고(0.3초), 왼다리를 높이 차올리며 몸을 닫고(0.5초),
/// 크게 내디디며 팔을 벌려 던질 팔을 뒤로 뺀 뒤(0.64초, 팔꿈치가 허리 뒤로), 골반부터 열며 팔을 젖히고(0.72초) 앞다리 위로
/// 상체를 덮으며 던지고(0.78초) 몸을 숙이고 뒷다리를 차올리며 마무리 -> Throw.anim / "Throw" 상태 (ThrowPoses 표).
/// 자세를 고치려면 표의 값을 바꾸고 메뉴를 다시 실행하세요. 타이밍은 ProiUltimate / ProiThrow 의 이벤트 시간과 맞춰져 있습니다.
/// </summary>
public static class ProiUltimateBaker
{
    const string Fbx = "Assets/Characters/Proi/Models/Proi_Character.fbx";
    const string UltimatePath = "Assets/Characters/Proi/Animations/Ultimate.anim";
    const string ThrowPath = "Assets/Characters/Proi/Animations/Throw.anim";
    const string DashPath = "Assets/Characters/Proi/Animations/Dash.anim";
    const string ControllerPath = "Assets/Characters/Proi/Animations/Proi.controller";

    class Pose
    {
        public float t;
        public bool idle;                                   // the Idle clip's first frame as is
        public Vector3 hips = new Vector3(0f, 0.93f, -0.02f);
        public Vector3 pelvis, spine, head;                 // euler (deg): pelvis = hips, spine spread over Spine / Chest / UpperChest
        public Vector3 lHand, rHand;                        // wrist targets
        public Vector3 lElbow = new Vector3(-1f, -1f, 0f), rElbow = new Vector3(1f, -1f, 0f);   // where the elbow pokes
        public Vector3 lAim = Vector3.down, rAim = Vector3.down;                                  // knuckles
        public Vector3 lBack = Vector3.left, rBack = Vector3.right;                              // back of the hand
        public Vector3 lFoot = new Vector3(-0.13f, 0.15f, 0f), rFoot = new Vector3(0.13f, 0.15f, -0.02f);   // ankles
        public Vector3 lKnee = Vector3.forward, rKnee = Vector3.forward;
        public Vector2 lFootRot, rFootRot;                  // pitch (+ = toes down) / yaw on top of the flat bind foot
        public bool airFeet;                                // in the air: foot pitch is relative to the shin (pointed toes)

        public Pose At(float time) { var c = (Pose)MemberwiseClone(); c.t = time; return c; }
    }

    static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

    // ------------------------------------------------------------------ the move (seconds)
    // 0.48 fists clash · 0.95-2.65 pull back + charge (front camera) · 2.88 crouch · 3.0 leap straight up · 3.45 apex, flying
    // forward (ProiUltimate carries her forward along the arch) · 3.85 diagonal dive · 3.98 slam · 5.15 idle
    static List<Pose> Poses()
    {
        var stand = new Pose
        {
            t = 0f, hips = V(0f, 0.93f, -0.02f), spine = V(-3f, 0f, 0f),
            lHand = V(-0.22f, 1.13f, 0.05f), rHand = V(0.22f, 1.13f, 0.05f),
            lElbow = V(-0.3f, 0f, -1f), rElbow = V(0.3f, 0f, -1f),
            lFootRot = V(0f, -8f, 0f), rFootRot = V(0f, 8f, 0f),
        };
        var spread = new Pose
        {
            t = 0.3f, hips = V(0f, 0.95f, -0.02f), spine = V(-6f, 0f, 0f),
            lHand = V(-0.30f, 1.31f, 0.17f), rHand = V(0.30f, 1.31f, 0.17f),
            lElbow = V(-1f, -0.6f, 0f), rElbow = V(1f, -0.6f, 0f),
            lAim = V(1f, 0f, 0.3f), rAim = V(-1f, 0f, 0.3f), lBack = V(0f, 0.3f, 1f), rBack = V(0f, 0.3f, 1f),
            lFootRot = V(0f, -8f, 0f), rFootRot = V(0f, 8f, 0f),
        };
        var clash = new Pose
        {
            t = 0.48f, hips = V(0f, 0.915f, -0.02f), spine = V(3f, 0f, 0f),
            lHand = V(-0.085f, 1.33f, 0.22f), rHand = V(0.085f, 1.33f, 0.22f),
            lElbow = V(-1f, -0.6f, 0f), rElbow = V(1f, -0.6f, 0f),
            lAim = V(1f, 0f, 0f), rAim = V(-1f, 0f, 0f), lBack = V(0f, 0.4f, 1f), rBack = V(0f, 0.4f, 1f),
            lFootRot = V(0f, -8f, 0f), rFootRot = V(0f, 8f, 0f),
        };
        Pose recoil = clash.At(0.58f); recoil.hips = V(0f, 0.93f, -0.02f); recoil.spine = V(0f, 0f, 0f);
        recoil.lHand = V(-0.10f, 1.335f, 0.215f); recoil.rHand = V(0.10f, 1.335f, 0.215f);
        Pose hold = recoil.At(0.95f);

        // charge stance (user's sketch, side view): deep lunge, upright torso, left foot forward with the shin upright,
        // right leg stretched far back on the ball of the foot; left arm guarding (elbow bent square, the forearm across the
        // front of the chest toward the right hand), right arm chambered (elbow pulled back, fist at the waist) - the fist that charges; the waist bends a
        // little forward and twists toward the charging right hand, the head turns a little that way too
        var pull = new Pose
        {
            t = 1.45f, hips = V(0f, 0.69f, -0.12f), pelvis = V(12f, 10f, 0f), spine = V(26f, 22f, 0f), head = V(-14f, -8f, 0f),
            lHand = V(0.07f, 1.09f, 0.45f), lElbow = V(-1f, -0.3f, 0.2f), lAim = V(1f, 0.1f, 0.2f), lBack = V(0f, 0.5f, 1f),
            rHand = V(0.22f, 0.98f, -0.06f), rElbow = V(0.3f, 0.2f, -1f), rAim = V(0f, -0.4f, 1f), rBack = V(0f, -1f, 0f),
            lFoot = V(-0.13f, 0.15f, 0.32f), rFoot = V(0.15f, 0.10f, -0.58f),
            lKnee = V(0f, 0f, 1f), rKnee = V(0.2f, -0.3f, 1f), lFootRot = V(0f, -8f, 0f), rFootRot = V(40f, 20f, 0f),
        };
        Pose chargeMid = pull.At(2.05f); chargeMid.hips = V(0f, 0.68f, -0.13f); chargeMid.rHand = V(0.22f, 0.97f, -0.08f);
        Pose charge = pull.At(2.65f); charge.hips = V(0f, 0.67f, -0.14f); charge.rHand = V(0.23f, 0.96f, -0.10f);
        charge.spine = V(30f, 26f, 0f); charge.head = V(-16f, -10f, 0f);

        var crouch = new Pose
        {   // take-off: deep and coiled
            t = 2.88f, hips = V(0f, 0.52f, -0.08f), pelvis = V(25f, 8f, 0f), spine = V(18f, 5f, 0f), head = V(-30f, 0f, 0f),
            lHand = V(-0.15f, 0.88f, 0.26f), lElbow = V(-1f, -1f, 0f), lAim = V(0f, 0f, 1f), lBack = V(-0.6f, 0.6f, 0f),
            rHand = V(0.33f, 0.88f, -0.25f), rElbow = V(1f, 0.5f, 0f), rAim = V(0.3f, -0.3f, -1f), rBack = V(1f, 0.4f, 0f),
            lFoot = V(-0.15f, 0.15f, 0.16f), rFoot = V(0.15f, 0.15f, -0.22f),
            lKnee = V(-0.3f, 0f, 1f), rKnee = V(0.3f, 0f, 1f), lFootRot = V(0f, -10f, 0f), rFootRot = V(20f, 15f, 0f),
        };
        var launch = new Pose
        {   // exploding straight up off the ground (reference video): body stretched, legs pushing down, left arm reaching up,
            // the burning right fist cocked low behind
            t = 3.0f, hips = V(0f, 1.35f, 0f), pelvis = V(-5f, 0f, 0f), spine = V(-8f, 0f, 0f), head = V(5f, 0f, 0f),
            lHand = V(-0.22f, 2.15f, 0.12f), lElbow = V(-1f, 0f, 0f), lAim = V(0f, 1f, 0.2f), lBack = V(-1f, 0f, 0f),
            rHand = V(0.32f, 1.62f, -0.22f), rElbow = V(1f, 0.3f, 0f), rAim = V(0.2f, -0.6f, -1f), rBack = V(1f, 0f, 0f),
            lFoot = V(-0.10f, 0.52f, -0.02f), rFoot = V(0.10f, 0.58f, -0.14f), lFootRot = V(45f, 0f, 0f), rFootRot = V(45f, 0f, 0f),
            airFeet = true,
        };
        var rise = new Pose
        {   // still climbing: left knee drawn up in front, right leg trailing down behind, the fist winding up over the shoulder
            t = 3.2f, hips = V(0f, 2.65f, 0.02f), pelvis = V(8f, 0f, 0f), spine = V(-5f, 0f, 0f), head = V(0f, 0f, 0f),
            lHand = V(-0.30f, 3.05f, 0.30f), lElbow = V(-1f, -1f, 0f), lAim = V(0f, -0.3f, 1f), lBack = V(-1f, 0f, 0f),
            rHand = V(0.25f, 3.30f, -0.25f), rElbow = V(1f, 0f, -0.3f), rAim = V(0f, 1f, -0.4f), rBack = V(0f, 0f, -1f),
            lFoot = V(-0.12f, 2.12f, 0.05f), rFoot = V(0.12f, 1.92f, -0.32f),
            lKnee = V(0f, 0.3f, 1f), rKnee = V(0f, -0.3f, 1f), lFootRot = V(40f, 0f, 0f), rFootRot = V(50f, 0f, 0f), airFeet = true,
        };
        var apex = new Pose
        {   // top of the arch: laid forward, flying - left knee tucked under, right leg stretched out behind, fist cocked high
            t = 3.45f, hips = V(0f, 3.5f, 0.05f), pelvis = V(25f, 0f, 0f), spine = V(10f, 0f, 0f), head = V(-25f, 0f, 0f),
            lHand = V(-0.28f, 3.70f, 0.50f), lElbow = V(-1f, -0.5f, 0f), lAim = V(0f, -0.3f, 1f), lBack = V(-1f, 0.3f, 0f),
            rHand = V(0.28f, 4.15f, -0.02f), rElbow = V(1f, 0f, -0.5f), rAim = V(0f, 1f, -0.2f), rBack = V(0f, 0f, -1f),
            lFoot = V(-0.13f, 3.08f, -0.18f), rFoot = V(0.13f, 2.98f, -0.62f),
            lKnee = V(0f, -0.2f, 1f), rKnee = V(0f, -1f, 0.2f), lFootRot = V(45f, 0f, 0f), rFootRot = V(55f, 0f, 0f), airFeet = true,
        };
        var turn = new Pose
        {   // tipping over into the dive, the fist swinging forward over her head, legs still split
            t = 3.65f, hips = V(0f, 3.05f, 0.1f), pelvis = V(45f, 0f, 0f), spine = V(20f, 0f, 0f), head = V(-35f, 0f, 0f),
            lHand = V(-0.35f, 3.15f, 0.35f), lElbow = V(-1f, 0.3f, 0f), lAim = V(-0.4f, -0.5f, -0.5f), lBack = V(-0.3f, 0.3f, 1f),
            rHand = V(0.15f, 3.55f, 0.70f), rElbow = V(1f, 0.3f, -0.5f), rAim = V(0f, 0.2f, 1f), rBack = V(0f, 1f, 0f),
            lFoot = V(-0.13f, 2.70f, -0.22f), rFoot = V(0.13f, 2.90f, -0.70f),
            lKnee = V(0f, -0.6f, 1f), rKnee = V(0f, -1f, 0f), lFootRot = V(45f, 0f, 0f), rFootRot = V(55f, 0f, 0f), airFeet = true,
        };
        var dive = new Pose
        {   // diving diagonally, fist first, legs trailing up behind (knees bending down, never backward)
            t = 3.85f, hips = V(0f, 1.5f, 0.2f), pelvis = V(55f, 0f, 0f), spine = V(25f, 0f, 0f), head = V(-45f, 0f, 0f),
            lHand = V(-0.38f, 1.55f, 0.10f), lElbow = V(-1f, 0.3f, 0f), lAim = V(-0.4f, -0.5f, -0.5f), lBack = V(-0.3f, 0.3f, 1f),
            rHand = V(0.15f, 1.15f, 0.92f), rElbow = V(1f, 0.3f, -0.5f), rAim = V(0f, -0.8f, 0.6f), rBack = V(0f, 0.5f, 1f),
            lFoot = V(-0.13f, 1.62f, -0.52f), rFoot = V(0.13f, 1.98f, -0.48f),
            lKnee = V(0f, -1f, 0.3f), rKnee = V(0f, -1f, 0.2f), lFootRot = V(45f, 0f, 0f), rFootRot = V(55f, 0f, 0f), airFeet = true,
        };
        var slam = new Pose
        {
            t = 3.98f, hips = V(0.02f, 0.36f, 0.10f), pelvis = V(28f, 0f, 0f), spine = V(42f, 0f, 0f), head = V(-45f, 0f, 0f),
            lHand = V(-0.38f, 0.75f, -0.05f), lElbow = V(-1f, 0.5f, 0f), lAim = V(-0.5f, -0.6f, -0.3f), lBack = V(-0.3f, 0.2f, 1f),
            rHand = V(0.16f, 0.20f, 0.72f), rElbow = V(1f, 0.3f, -0.5f), rAim = V(0f, -1f, 0.1f), rBack = V(0f, 0f, 1f),
            lFoot = V(-0.18f, 0.15f, 0.36f), rFoot = V(0.14f, 0.08f, -0.42f),
            lKnee = V(-0.4f, 0.3f, 1f), rKnee = V(0.2f, -1f, 0.3f), lFootRot = V(0f, -10f, 0f), rFootRot = V(50f, 10f, 0f),
        };
        Pose slamHold = slam.At(4.45f); slamHold.hips = V(0.02f, 0.34f, 0.10f);
        var getUp = new Pose
        {
            t = 4.8f, hips = V(0f, 0.70f, 0f), pelvis = V(12f, 0f, 0f), spine = V(10f, 0f, 0f), head = V(-10f, 0f, 0f),
            lHand = V(-0.15f, 1.0f, 0.25f), lElbow = V(-1f, -1f, 0f), lAim = V(0f, 0.3f, 1f), lBack = V(-0.6f, 0.6f, 0f),
            rHand = V(0.18f, 0.95f, 0.15f), rElbow = V(1f, -1f, 0f), rAim = V(0f, 0.3f, 1f), rBack = V(0.6f, 0.6f, 0f),
            lFoot = V(-0.15f, 0.15f, 0.20f), rFoot = V(0.14f, 0.15f, -0.20f), lFootRot = V(0f, -10f, 0f), rFootRot = V(0f, 15f, 0f),
        };
        var end = new Pose { t = 5.15f, idle = true };
        return new List<Pose> { stand, spread, clash, recoil, hold, pull, chargeMid, charge, crouch, launch, rise, apex, turn, dive, slam, slamHold, getUp, end };
    }

    // ------------------------------------------------------------------ the grab-and-throw (seconds) - a baseball pitch
    // 0.12 set · 0.3 right hand reaching out, grabs · 0.5 leg kick (left knee up, body closed) · 0.64 long stride, arms split
    // (throwing arm way back, elbow behind the waist) · 0.72 front foot planted, hips open first, arm laid back ·
    // 0.78 thrown, torso whipping over the front leg · 0.92 follow-through bent over, back leg kicking up · 1.35 idle
    // ProiThrow slides her forward ThrowStride m over ThrowStrideTime (the stride) - poses after it are shifted back by
    // that much so the planted front foot stays put.
    public const float ThrowStride = 0.3f;
    public static readonly Vector2 ThrowStrideTime = new Vector2(0.7f, 0.95f);

    static List<Pose> ThrowPoses()
    {
        var start = new Pose { t = 0f, idle = true };
        var set = new Pose
        {   // from the idle stance: left foot steps forward, right fist drawn to the waist
            t = 0.12f, hips = V(0f, 0.88f, -0.02f), pelvis = V(0f, 8f, 0f), spine = V(4f, 8f, 0f), head = V(0f, -12f, 0f),
            lHand = V(-0.20f, 1.24f, 0.22f), lElbow = V(-1f, -1f, 0f), lAim = V(0.3f, 0.3f, 1f), lBack = V(-1f, 0.4f, 0f),
            rHand = V(0.22f, 1.06f, -0.06f), rElbow = V(0.4f, 0f, -1f), rAim = V(0f, -0.4f, 1f), rBack = V(0f, -1f, 0f),
            lFoot = V(-0.13f, 0.15f, 0.22f), rFoot = V(0.15f, 0.15f, -0.18f), lFootRot = V(0f, -8f, 0f), rFootRot = V(0f, 18f, 0f),
        };
        var reach = new Pose
        {   // the right arm shoots straight out to grab, right shoulder forward
            t = 0.28f, hips = V(0f, 0.86f, 0.04f), pelvis = V(4f, -12f, 0f), spine = V(14f, -18f, 0f), head = V(-10f, 28f, 0f),
            lHand = V(-0.24f, 1.20f, 0.12f), lElbow = V(-1f, -1f, 0f), lAim = V(0.2f, 0.3f, 1f), lBack = V(-1f, 0.4f, 0f),
            rHand = V(0.06f, 1.28f, 0.55f), rElbow = V(1f, -0.5f, 0f), rAim = V(0f, 0f, 1f), rBack = V(0f, 1f, 0f),
            lFoot = V(-0.13f, 0.15f, 0.30f), rFoot = V(0.15f, 0.15f, -0.22f), lFootRot = V(0f, -8f, 0f), rFootRot = V(0f, 18f, 0f),
        };
        Pose grab = reach.At(0.38f); grab.hips = V(0f, 0.88f, 0.03f); grab.rHand = V(0.08f, 1.34f, 0.50f);
        var kick = new Pose
        {   // leg kick: standing tall on the right leg, left knee drawn up high, body closed (turned right), hands gathered
            // at the chest
            t = 0.5f, hips = V(0.02f, 0.93f, -0.06f), pelvis = V(0f, 40f, 0f), spine = V(-6f, 25f, 0f), head = V(0f, -62f, 0f),
            lHand = V(0.14f, 1.22f, 0.14f), lElbow = V(-0.5f, -1f, 0.5f), lAim = V(1f, 0.3f, 0f), lBack = V(0f, 0.3f, 1f),
            rHand = V(0.22f, 1.30f, -0.02f), rElbow = V(0.6f, -1f, -0.4f), rAim = V(0f, 1f, 0f), rBack = V(1f, 0f, 0f),
            lFoot = V(-0.06f, 0.62f, 0.12f), lKnee = V(0f, 1f, 0.8f), lFootRot = V(35f, -10f, 0f),
            rFoot = V(0.13f, 0.15f, -0.08f), rFootRot = V(0f, 40f, 0f),
        };
        var stride = new Pose
        {   // the long stride: dropping low onto a far-reaching front foot, arms split - the glove arm points at the target,
            // the throwing arm swung way back (elbow behind the waist), body still closed
            t = 0.64f, hips = V(0.02f, 0.74f, 0.04f), pelvis = V(-2f, 28f, 0f), spine = V(-10f, 42f, 0f), head = V(4f, -62f, 0f),
            lHand = V(-0.02f, 1.30f, 0.55f), lElbow = V(-1f, -0.5f, 0f), lAim = V(0f, 0.2f, 1f), lBack = V(-1f, 0.4f, 0f),
            rHand = V(0.24f, 1.28f, -0.50f), rElbow = V(0f, -1f, -0.4f), rAim = V(0.2f, 0.6f, -1f), rBack = V(1f, 0f, 0f),
            lFoot = V(-0.14f, 0.17f, 0.56f), lKnee = V(0f, 0f, 1f), lFootRot = V(-12f, -10f, 0f),
            rFoot = V(0.14f, 0.15f, -0.36f), rKnee = V(0.6f, -0.2f, 1f), rFootRot = V(0f, 50f, 0f),
        };
        var layback = new Pose
        {   // front foot planted, hips fire open first while the shoulders stay back: elbow up at shoulder height leading,
            // the fist laid back behind it; the glove arm tucks in
            t = 0.72f, hips = V(0.01f, 0.70f, 0.16f), pelvis = V(4f, -14f, 0f), spine = V(-8f, 34f, 0f), head = V(-6f, -22f, 0f),
            lHand = V(-0.20f, 1.08f, 0.30f), lElbow = V(-1f, -0.6f, 0f), lAim = V(0f, 0f, 1f), lBack = V(-1f, 0.3f, 0f),
            rHand = V(0.40f, 1.42f, -0.10f), rElbow = V(1f, -0.2f, 0.8f), rAim = V(0f, 1f, -0.5f), rBack = V(1f, 0f, 0f),
            lFoot = V(-0.14f, 0.15f, 0.58f), lFootRot = V(0f, -8f, 0f),
            rFoot = V(0.14f, 0.15f, -0.34f), rKnee = V(0.4f, -0.3f, 1f), rFootRot = V(20f, 40f, 0f),
        };
        var release = new Pose
        {   // let go: the torso whips over the front leg, the arm out high in front, the glove yanked to the hip, back foot
            // dragging on its toes
            t = 0.78f, hips = V(0f, 0.68f, 0.26f), pelvis = V(14f, -30f, 0f), spine = V(30f, -40f, 0f), head = V(-30f, 50f, 0f),
            lHand = V(-0.30f, 0.92f, 0.05f), lElbow = V(-1f, 0f, -0.5f), lAim = V(0f, -0.3f, 1f), lBack = V(-1f, 0f, 0f),
            rHand = V(0.02f, 1.26f, 0.78f), rElbow = V(1f, -0.3f, 0f), rAim = V(0f, 0.3f, 1f), rBack = V(0f, 1f, -0.2f),
            lFoot = V(-0.14f, 0.15f, 0.58f), lFootRot = V(0f, -8f, 0f),
            rFoot = V(0.13f, 0.20f, -0.30f), rKnee = V(0.2f, -0.5f, 1f), rFootRot = V(40f, 30f, 0f),
        };
        var follow = new Pose
        {   // follow-through: bent right over the front knee, the throwing arm swept down past it, the back leg kicking up
            t = 0.92f, hips = V(-0.02f, 0.66f, 0.32f), pelvis = V(30f, -42f, 0f), spine = V(46f, -40f, 0f), head = V(-48f, 58f, 0f),
            lHand = V(-0.32f, 0.92f, 0.10f), lElbow = V(-1f, 0f, -0.5f), lAim = V(0f, -0.3f, 1f), lBack = V(-1f, 0f, 0f),
            rHand = V(-0.30f, 0.62f, 0.62f), rElbow = V(1f, 0.3f, 0.2f), rAim = V(-0.3f, -1f, 0.2f), rBack = V(0f, 0f, 1f),
            lFoot = V(-0.14f, 0.15f, 0.58f), lKnee = V(-0.1f, 0f, 1f), lFootRot = V(0f, -8f, 0f),
            rFoot = V(0.10f, 0.48f, -0.20f), rKnee = V(0f, -1f, 0.3f), rFootRot = V(75f, 20f, 0f),
        };
        Pose recover = follow.At(1.02f);
        recover.hips = V(-0.01f, 0.74f, 0.30f); recover.pelvis = V(16f, -34f, 0f); recover.spine = V(30f, -34f, 0f);
        recover.head = V(-32f, 50f, 0f); recover.rFoot = V(0.12f, 0.30f, -0.10f); recover.rFootRot = V(50f, 20f, 0f);
        foreach (Pose p in new[] { layback, release, follow, recover }) StrideBack(p);
        Pose settle = set.At(1.15f); settle.hips = V(0f, 0.9f, -0.02f);
        var end = new Pose { t = 1.35f, idle = true };
        return new List<Pose> { start, set, reach, grab, kick, stride, layback, release, follow, recover, settle, end };
    }

    /// <summary>How far ProiThrow has slid her forward by time t (smoothstep over ThrowStrideTime).</summary>
    public static float StrideAt(float t)
    {
        float k = Mathf.Clamp01((t - ThrowStrideTime.x) / (ThrowStrideTime.y - ThrowStrideTime.x));
        return ThrowStride * k * k * (3f - 2f * k);
    }

    static void StrideBack(Pose p)
    {
        Vector3 d = new Vector3(0f, 0f, StrideAt(p.t));
        p.hips -= d; p.lHand -= d; p.rHand -= d; p.lFoot -= d; p.rFoot -= d;
    }

    // ------------------------------------------------------------------ the dash (seconds)
    // 0-0.2 shooting forward: leaned hard over, both arms swept back, the back leg trailing · 0.3 front foot brakes · 0.5 idle
    static List<Pose> DashPoses()
    {
        var shoot = new Pose
        {   // the instant she goes: low and leaned far forward, arms flung straight back, front knee tucked, back leg stretched
            t = 0f, hips = V(0f, 0.80f, 0.06f), pelvis = V(22f, 0f, 0f), spine = V(20f, 0f, 0f), head = V(-34f, 0f, 0f),
            lHand = V(-0.26f, 1.00f, -0.12f), lElbow = V(-1f, 0.3f, 0f), lAim = V(0f, -0.4f, -1f), lBack = V(-1f, 0f, 0f),
            rHand = V(0.26f, 1.00f, -0.12f), rElbow = V(1f, 0.3f, 0f), rAim = V(0f, -0.4f, -1f), rBack = V(1f, 0f, 0f),
            lFoot = V(-0.12f, 0.30f, 0.22f), lKnee = V(0f, 0.2f, 1f), lFootRot = V(20f, -6f, 0f),
            rFoot = V(0.13f, 0.30f, -0.40f), rKnee = V(0f, -0.4f, 1f), rFootRot = V(60f, 6f, 0f),
        };
        Pose glide = shoot.At(0.2f);
        glide.hips = V(0f, 0.78f, 0.08f); glide.pelvis = V(24f, 0f, 0f); glide.spine = V(22f, 0f, 0f);
        glide.lHand = V(-0.27f, 1.02f, -0.15f); glide.rHand = V(0.27f, 1.02f, -0.15f); glide.rFoot = V(0.13f, 0.32f, -0.44f);
        var brake = new Pose
        {   // the front foot plants and she pulls up, arms swinging forward and out for balance
            t = 0.3f, hips = V(0f, 0.74f, 0f), pelvis = V(8f, 0f, 0f), spine = V(6f, 0f, 0f), head = V(-10f, 0f, 0f),
            lHand = V(-0.30f, 1.05f, 0.10f), lElbow = V(-1f, -0.6f, 0f), lAim = V(0f, 0f, 1f), lBack = V(-1f, 0.3f, 0f),
            rHand = V(0.30f, 1.00f, -0.02f), rElbow = V(1f, -0.6f, 0f), rAim = V(0f, -0.3f, 1f), rBack = V(1f, 0.3f, 0f),
            lFoot = V(-0.14f, 0.15f, 0.22f), lFootRot = V(-5f, -8f, 0f), rFoot = V(0.14f, 0.15f, -0.30f), rFootRot = V(30f, 15f, 0f),
        };
        var end = new Pose { t = 0.5f, idle = true };
        return new List<Pose> { shoot, glide, brake, end };
    }

    // ------------------------------------------------------------------ baking

    [MenuItem("Tools/Proi/Bake Skill Clips")]
    public static void Bake()
    {
        BakeClip(UltimatePath, "Ultimate", Poses(), new Vector3(560f, 300f));
        BakeClip(ThrowPath, "Throw", ThrowPoses(), new Vector3(560f, 360f));
        BakeClip(DashPath, "Dash", DashPoses(), new Vector3(560f, 420f));
        AssetDatabase.SaveAssets();
    }

    static void BakeClip(string clipPath, string stateName, List<Pose> poses, Vector3 statePos)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        AnimationClip idle = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>().FirstOrDefault(c => c.name == "Idle");
        if (model == null || idle == null) { Debug.LogError("[ProiUltimateBaker] Proi_Character.fbx / Idle clip not found"); return; }

        GameObject go = Object.Instantiate(model);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var rig = new Rig(go);
            var curves = new Dictionary<string, AnimationCurve>();
            var last = new Dictionary<Transform, Quaternion>();
            foreach (Pose p in poses)
            {
                rig.Apply(p, idle);
                foreach (Transform b in rig.bones)
                {
                    string path = AnimationUtility.CalculateTransformPath(b, go.transform);
                    Quaternion q = b.localRotation;
                    if (last.TryGetValue(b, out Quaternion prev) && Quaternion.Dot(prev, q) < 0f)
                        q = new Quaternion(-q.x, -q.y, -q.z, -q.w);              // keep the shortest way round
                    last[b] = q;
                    Key(curves, path, "localRotation.x", p.t, q.x);
                    Key(curves, path, "localRotation.y", p.t, q.y);
                    Key(curves, path, "localRotation.z", p.t, q.z);
                    Key(curves, path, "localRotation.w", p.t, q.w);
                    if (b == rig.hips)
                    {
                        Key(curves, path, "localPosition.x", p.t, b.localPosition.x);
                        Key(curves, path, "localPosition.y", p.t, b.localPosition.y);
                        Key(curves, path, "localPosition.z", p.t, b.localPosition.z);
                    }
                }
            }

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                clip = new AnimationClip { name = stateName };
                AssetDatabase.CreateAsset(clip, clipPath);
            }
            clip.ClearCurves();
            clip.frameRate = 30f;
            foreach (var kv in curves)
            {
                AnimationCurve c = kv.Value;
                for (int i = 0; i < c.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
                }
                int bar = kv.Key.LastIndexOf('|');
                clip.SetCurve(kv.Key.Substring(0, bar), typeof(Transform), kv.Key.Substring(bar + 1), c);
            }
            clip.EnsureQuaternionContinuity();
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);

            AddState(clip, stateName, statePos);
            Debug.Log($"[ProiUltimateBaker] Baked {clipPath}: {poses.Count} poses, {clip.length:F2} s");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    static void Key(Dictionary<string, AnimationCurve> curves, string path, string prop, float t, float v)
    {
        string k = path + "|" + prop;
        if (!curves.TryGetValue(k, out AnimationCurve c)) curves[k] = c = new AnimationCurve();
        c.AddKey(new Keyframe(t, v));
    }

    static void AddState(AnimationClip clip, string stateName, Vector3 statePos)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) return;
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName);
        if (state == null) state = sm.AddState(stateName, statePos);
        state.motion = clip;
        EditorUtility.SetDirty(controller);
    }

    // ------------------------------------------------------------------ the rig + IK

    class Rig
    {
        public readonly Transform[] bones;
        public readonly Transform hips;
        readonly GameObject root;
        readonly Dictionary<Transform, Quaternion> bindRot = new Dictionary<Transform, Quaternion>();
        readonly Transform spine, chest, upperChest, neck, head, lShoulder, rShoulder;
        readonly Transform lUpperArm, lLowerArm, lHand, rUpperArm, rLowerArm, rHand;
        readonly Transform lUpperLeg, lLowerLeg, lFoot, lToes, rUpperLeg, rLowerLeg, rFoot, rToes;

        public Rig(GameObject go)
        {
            root = go;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Transform armature = go.transform.Find("Proi_Armature");
            bones = armature.GetComponentsInChildren<Transform>(true).Where(t => t != armature).ToArray();
            Transform F(string n) => bones.First(t => t.name == n);
            hips = F("Hips"); spine = F("Spine"); chest = F("Chest"); upperChest = F("UpperChest"); neck = F("Neck"); head = F("Head");
            lShoulder = F("LeftShoulder"); rShoulder = F("RightShoulder");
            lUpperArm = F("LeftUpperArm"); lLowerArm = F("LeftLowerArm"); lHand = F("LeftHand");
            rUpperArm = F("RightUpperArm"); rLowerArm = F("RightLowerArm"); rHand = F("RightHand");
            lUpperLeg = F("LeftUpperLeg"); lLowerLeg = F("LeftLowerLeg"); lFoot = F("LeftFoot"); lToes = F("LeftToes");
            rUpperLeg = F("RightUpperLeg"); rLowerLeg = F("RightLowerLeg"); rFoot = F("RightFoot"); rToes = F("RightToes");

            // bind (T-pose) world rotations from the skin
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            Matrix4x4 m2w = smr.transform.localToWorldMatrix;
            Matrix4x4[] bp = smr.sharedMesh.bindposes;
            for (int i = 0; i < smr.bones.Length; i++)
                bindRot[smr.bones[i]] = (m2w * bp[i].inverse).rotation;
        }

        Quaternion Bind(Transform t) => bindRot[t];

        public void Apply(Pose p, AnimationClip idle)
        {
            idle.SampleAnimation(root, 0f);                     // hair tails etc. keep the idle pose
            if (p.idle) return;

            Quaternion pelvis = Quaternion.Euler(p.pelvis);
            hips.SetPositionAndRotation(p.hips, pelvis * Bind(hips));
            Transform[] chain = { spine, chest, upperChest };
            Quaternion top = pelvis;
            for (int i = 0; i < chain.Length; i++)
            {
                top = pelvis * Quaternion.Euler(p.spine * ((i + 1) / 3f));
                chain[i].rotation = top * Bind(chain[i]);
            }
            neck.rotation = top * Quaternion.Euler(p.head * 0.4f) * Bind(neck);
            head.rotation = top * Quaternion.Euler(p.head) * Bind(head);
            lShoulder.rotation = top * Bind(lShoulder);
            rShoulder.rotation = top * Bind(rShoulder);

            // arms: hinge = world up (left) / down (right) in the T-pose, the elbow bends the hand forward
            Limb(lUpperArm, lLowerArm, lHand, p.lHand, p.lElbow, Vector3.up);
            Limb(rUpperArm, rLowerArm, rHand, p.rHand, p.rElbow, Vector3.down);
            lHand.rotation = Basis(p.lAim, p.lBack);             // hand: +Y = knuckles, +Z = back of the hand
            rHand.rotation = Basis(p.rAim, p.rBack);

            // legs: hinge = world +x in the T-pose, the knee bends forward
            lUpperLeg.rotation = pelvis * Bind(lUpperLeg);
            rUpperLeg.rotation = pelvis * Bind(rUpperLeg);
            Limb(lUpperLeg, lLowerLeg, lFoot, p.lFoot, p.lKnee, Vector3.right);
            Limb(rUpperLeg, rLowerLeg, rFoot, p.rFoot, p.rKnee, Vector3.right);
            Quaternion lBase = p.airFeet ? lLowerLeg.rotation * Quaternion.Inverse(Bind(lLowerLeg)) : Quaternion.identity;
            Quaternion rBase = p.airFeet ? rLowerLeg.rotation * Quaternion.Inverse(Bind(rLowerLeg)) : Quaternion.identity;
            lFoot.rotation = lBase * Quaternion.Euler(p.lFootRot.x, p.lFootRot.y, 0f) * Bind(lFoot);
            rFoot.rotation = rBase * Quaternion.Euler(p.rFootRot.x, p.rFootRot.y, 0f) * Bind(rFoot);
            lToes.rotation = lFoot.rotation * Quaternion.Inverse(Bind(lFoot)) * Bind(lToes);
            rToes.rotation = rFoot.rotation * Quaternion.Inverse(Bind(rFoot)) * Bind(rToes);
        }

        /// <summary>Two-bone IK with the joint as a hinge (the bone's bind hinge axis = the bend plane's normal).</summary>
        void Limb(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint, Vector3 hingeWorldBind)
        {
            Vector3 pa = a.position;
            float l1 = Vector3.Distance(a.position, b.position), l2 = Vector3.Distance(b.position, c.position);
            Vector3 to = target - pa;
            float d = Mathf.Clamp(to.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, (l1 + l2) * 0.999f);
            Vector3 dir = to.sqrMagnitude > 1e-8f ? to.normalized : Vector3.down;
            Vector3 pole = Vector3.ProjectOnPlane(hint, dir);
            if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(Vector3.forward, dir);
            pole.Normalize();
            float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 elbow = pa + dir * (l1 * cosA) + pole * (l1 * sinA);
            Vector3 end = pa + dir * d;
            Vector3 n = -Vector3.Cross(dir, pole).normalized;    // bend plane normal (matches the bind hinge)
            a.rotation = Orient(a, elbow - pa, n, hingeWorldBind);
            b.rotation = Orient(b, end - elbow, n, hingeWorldBind);
        }

        Quaternion Orient(Transform bone, Vector3 boneDir, Vector3 hingeWorld, Vector3 hingeWorldBind)
        {
            Vector3 hingeLocal = Quaternion.Inverse(Bind(bone)) * hingeWorldBind;
            return Basis(boneDir, hingeWorld) * Quaternion.Inverse(Basis(Vector3.up, hingeLocal));
        }
    }

    /// <summary>Rotation whose +Y is <paramref name="y"/> (exact) and +Z is <paramref name="z"/> (made perpendicular).</summary>
    static Quaternion Basis(Vector3 y, Vector3 z)
    {
        Vector3 zz = Vector3.ProjectOnPlane(z, y);
        if (zz.sqrMagnitude < 1e-6f) zz = Vector3.ProjectOnPlane(Vector3.forward, y);
        if (zz.sqrMagnitude < 1e-6f) zz = Vector3.ProjectOnPlane(Vector3.right, y);
        return Quaternion.LookRotation(zz.normalized, y.normalized);
    }
}
