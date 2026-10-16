using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NamgungLuna.Motion.Tests
{
    /// <summary>
    /// 기본공격 3타 검증: 팔 리그가 만들어지고, 세 번 누르면 1타 → 2타 → 3타가 이어지며 발사 프레임(f8 / f9 / f15)에 빛 구체가
    /// 나가고, 1타는 왼손이 앞으로 뻗고 3타는 랜턴이 머리 높이 위로 올라가며, 공격 중에는 이동이 잠기고, 끝나면 팔이 제자리로
    /// 돌아오고, 쉬었다 누르면 다시 1타부터 시작하는지 봅니다. Report: Recordings/LunaMoonGait/attack.json.
    /// </summary>
    public class LunaBasicAttackPlayModeTests
    {
        const float Step = 1f / 60f;

        [UnityTest, Timeout(1800000)]
        public IEnumerator ThreeHitComboCastsFromHandAndLantern()
        {
            Directory.CreateDirectory(LunaMoonGaitPlayModeTests.OutDir);
            var rig = new LunaMoonGaitPlayModeTests.Rig();
            yield return LunaMoonGaitPlayModeTests.LoadDemo(rig, Step);
            var attack = rig.motor.GetComponent<LunaBasicAttack>();
            var arms = rig.motor.GetComponent<LunaArmRig>();
            Assert.NotNull(attack, "LunaBasicAttack missing on the prefab");
            Assert.NotNull(arms, "LunaArmRig missing on the prefab");
            Assert.IsTrue(arms.Built, "arm rig was not built");
            Assert.GreaterOrEqual(arms.SkinnedRenderers, 3, "skin, bodice and puff sleeves should be skinned to the arms");

            Transform luna = rig.motor.transform;
            Vector3 restHand = luna.InverseTransformPoint(arms.Left.hand.position);
            Vector3 restLantern = luna.InverseTransformPoint(rig.gait.lantern.position);

            var releases = new List<(int hit, float time, Vector3 local)>();
            var starts = new List<int>();
            float clock = 0f;
            attack.AttackEvent += e =>
            {
                if (e.kind == LunaAttackEventKind.Release) releases.Add((e.hit, clock, luna.InverseTransformPoint(e.position)));
                if (e.kind == LunaAttackEventKind.Start) starts.Add(e.hit);
            };

            // three presses, as a player would tap: the second and third are queued during the hit before
            float[] presses = { 0f, 0.4f, 0.8f };
            int pressed = 0;
            float lowest = float.MaxValue, maxLantern = 0f, maxHandForward = 0f;
            bool nan = false;
            Vector3 start = luna.position;
            rig.motor.scriptedMove = luna.forward;   // held the whole time: the cast must keep her planted
            float lockedTravel = 0f;
            Vector3 previous = luna.position;
            int frames = Mathf.CeilToInt(2.8f / Step);
            for (int f = 0; f < frames; f++)
            {
                if (pressed < presses.Length && clock >= presses[pressed]) { rig.motor.Attack(); pressed++; }
                yield return null;
                clock += Step;
                if (attack.LocksMovement) lockedTravel += Vector3.ProjectOnPlane(luna.position - previous, Vector3.up).magnitude;
                previous = luna.position;
                if (clock > 1.2f) rig.motor.scriptedMove = Vector3.zero;   // let go during 3타
                Vector3 hand = luna.InverseTransformPoint(arms.Left.hand.position);
                Vector3 shoulder = luna.InverseTransformPoint(arms.Left.upper.position);
                if (attack.CurrentHit == 0) maxHandForward = Mathf.Max(maxHandForward, hand.z - shoulder.z);
                if (attack.CurrentHit == 2) maxLantern = Mathf.Max(maxLantern, luna.InverseTransformPoint(rig.gait.lantern.position).y);
                if (rig.motor.IsGrounded) lowest = Mathf.Min(lowest, rig.gait.LowestSoleHeight);
                Vector3 h = arms.Left.hand.position;
                if (float.IsNaN(h.x) || float.IsNaN(h.y) || float.IsNaN(h.z)) nan = true;
            }
            // settle: the arms go back to the rest pose
            for (int i = 0; i < 60; i++) yield return null;
            float handBack = Vector3.Distance(luna.InverseTransformPoint(arms.Left.hand.position), restHand);
            float lanternBack = Vector3.Distance(luna.InverseTransformPoint(rig.gait.lantern.position), restLantern);
            // after a pause the combo starts over
            rig.motor.Attack();
            yield return null;
            yield return null;
            int restartHit = attack.CurrentHit;

            var report = new StringBuilder("{\n");
            report.AppendLine($"  \"starts\": [{string.Join(", ", starts)}],");
            report.AppendLine($"  \"releases\": [{string.Join(", ", releases.Select(r => $"{{\"hit\": {r.hit}, \"t\": {r.time:F3}, \"y\": {r.local.y:F2}}}"))}],");
            report.AppendLine($"  \"max_hand_forward_m\": {maxHandForward:F3},");
            report.AppendLine($"  \"max_lantern_height_m\": {maxLantern:F3},");
            report.AppendLine($"  \"locked_travel_m\": {lockedTravel:F3},");
            report.AppendLine($"  \"lowest_sole_m\": {lowest:F4},");
            report.AppendLine($"  \"hand_back_m\": {handBack:F4},");
            report.AppendLine($"  \"lantern_back_m\": {lanternBack:F4},");
            report.AppendLine($"  \"restart_hit\": {restartHit},");
            report.AppendLine($"  \"nan\": {nan.ToString().ToLower()}");
            report.AppendLine("}");
            File.WriteAllText(Path.Combine(LunaMoonGaitPlayModeTests.OutDir, "attack.json"), report.ToString(), new UTF8Encoding(false));
            rig.motor.scriptedMove = Vector3.zero;
            Time.captureDeltaTime = 0f;

            Assert.IsFalse(nan, "NaN in the arm pose");
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, starts.Take(3).ToArray(), "1타 → 2타 → 3타");
            Assert.AreEqual(3, releases.Count, "one orb per hit");
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, releases.Select(r => r.hit).ToArray());
            // release frames: hit 1 at f8 after the first press; the chained hits start at the combo cut of the hit before
            float f1 = LunaBasicAttack.Clips[0].release / LunaBasicAttack.Fps;
            float start2 = LunaBasicAttack.Clips[0].comboCut / LunaBasicAttack.Fps;
            float f2 = start2 + LunaBasicAttack.Clips[1].release / LunaBasicAttack.Fps;
            float f3 = start2 + LunaBasicAttack.Clips[1].comboCut / LunaBasicAttack.Fps + LunaBasicAttack.Clips[2].release / LunaBasicAttack.Fps;
            Assert.That(releases[0].time, Is.InRange(f1 - 0.05f, f1 + 0.05f), "1타 발사 프레임");
            Assert.That(releases[1].time, Is.InRange(f2 - 0.05f, f2 + 0.05f), "2타 발사 프레임");
            Assert.That(releases[2].time, Is.InRange(f3 - 0.05f, f3 + 0.05f), "3타 발사 프레임");
            Assert.GreaterOrEqual(maxHandForward, 0.25f, "1타: the free (left) hand should reach forward");
            Assert.GreaterOrEqual(maxLantern, 1.45f, "3타: the lantern should be lifted above head height");
            Assert.Greater(releases[2].local.y, 1.3f, "3타 orb leaves from the raised lantern");
            Assert.Less(lockedTravel, 0.2f, "she should stay planted while casting");
            Assert.Greater(lowest, -0.01f, "a boot sank into the floor during the attack");
            Assert.Less(handBack, 0.02f, "the arm should return to its rest pose");
            Assert.Less(lanternBack, 0.03f, "the lantern should return to the hand's rest grip");
            Assert.AreEqual(0, restartHit, "after a pause the combo starts again from 1타");
        }
    }
}
