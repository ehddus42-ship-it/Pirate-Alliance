using System.Collections.Generic;
using AcRoguelike.Liminal;
using UnityEngine;

namespace AcRoguelike.Forest
{
    public enum ForestTreantState { Stalk, ThrowWindup, ThrowFollowThrough, RootWindup, RootFollowThrough, Recovery, Dead }

    public sealed class ForestTreant : LiminalPropMonster
    {
        public float throwWindup = .95f, rootWindup = 1.2f, attackCooldown = 3.4f;
        public ForestTreantState State { get; private set; }
        public int SaplingsThrown { get; private set; }
        public int RootWaves { get; private set; }
        public float StateTime => elapsed;
        public int ActiveSaplings => saplings.FindAll(s => s && !s.Finished).Count;
        readonly List<ForestSapling> saplings = new List<ForestSapling>();
        readonly List<ForestRootWave> roots = new List<ForestRootWave>();
        ForestCanopyRig canopy;
        float elapsed, nextAttack, walk;
        int pattern;
        Vector3 origin, aim, landing;
        LiminalPlayerHealth subscribedPlayer;
        protected override int MaxHealth(int s) => 155 + s * 26;
        protected override float Weight => State == ForestTreantState.Stalk ? .72f : .4f;
        protected override float HitTilt => 7;
        protected override float KnockbackSpeed => 1.1f;
        protected override float LyingHalfDepth => .62f;

        protected override void OnSetup()
        {
            displayName = "달빛 고목"; nextAttack = Time.time + 1.65f; State = ForestTreantState.Stalk;
            if (pose)
            {
                ForestAttackCueAnchors.Prepare(transform);
                canopy = pose.GetComponent<ForestCanopyRig>(); if (!canopy) canopy = pose.gameObject.AddComponent<ForestCanopyRig>();
                canopy.Initialize(pose.Find("Model") ? pose.Find("Model") : pose);
            }
            if (subscribedPlayer) subscribedPlayer.Died -= CancelAttack;
            subscribedPlayer = player; if (subscribedPlayer) subscribedPlayer.Died += CancelAttack;
        }
        protected override void Think(float dt, Vector3 to, float distance)
        {
            elapsed += dt; poseOffset = Vector3.zero; poseScale = Vector3.one; poseRotation = Quaternion.identity;
            if (canopy) { canopy.Movement = 0; canopy.Casting = IsWindingUp ? 1 : 0; }
            switch (State)
            {
                case ForestTreantState.Stalk:
                    Face(to, 70);
                    if (Time.time >= staggerUntil && distance > 5.6f)
                    {
                        float moved = MoveBody(Steer(to, 1.8f) * 1.05f * dt);
                        walk += moved * 5.6f; float stride = Mathf.Clamp01(moved / Mathf.Max(.001f, dt));
                        poseOffset = Vector3.up * (Mathf.Abs(Mathf.Sin(walk)) * .055f * stride);
                        poseRotation = Quaternion.Euler(1.5f * Mathf.Sin(walk), 0, 3.5f * Mathf.Sin(walk) * stride);
                        if (canopy) canopy.Movement = stride;
                    }
                    if (Time.time >= nextAttack && distance < 12 && CanSeePlayer())
                    {
                        origin = transform.position; aim = ForestAttackUtility.Flat(to);
                        landing = origin + aim * Mathf.Clamp(distance - .6f, 2.4f, 7.5f);
                        if (room)
                        {
                            var local = room.transform.InverseTransformPoint(landing);
                            local.x = Mathf.Clamp(local.x, room.localBounds.min.x + 1, room.localBounds.max.x - 1);
                            local.z = Mathf.Clamp(local.z, room.localBounds.min.z + 1, room.localBounds.max.z - 1);
                            landing = room.transform.TransformPoint(local);
                        }
                        Enter(pattern++ % 2 == 0 ? ForestTreantState.ThrowWindup : ForestTreantState.RootWindup);
                    }
                    break;
                case ForestTreantState.ThrowWindup:
                    MoveBody(Vector3.zero); Face(aim, 70);
                    AttackCue(elapsed / throwWindup);
                    poseRotation = Quaternion.Euler(-17f * Mathf.Clamp01(elapsed / throwWindup), 0, -7f);
                    if (elapsed >= throwWindup)
                    {
                        HideTelegraph();
                        var sapling = ForestSapling.Throw(origin + Vector3.up * 2.15f, landing, Health, player, room, stage);
                        if (sapling) { saplings.Add(sapling); SaplingsThrown++; }
                        Enter(ForestTreantState.ThrowFollowThrough);
                    }
                    break;
                case ForestTreantState.ThrowFollowThrough:
                    poseRotation = Quaternion.Euler(16f * Mathf.Exp(-elapsed * 4), 0, 5f * Mathf.Exp(-elapsed * 4));
                    if (elapsed >= .55f) Enter(ForestTreantState.Recovery);
                    break;
                case ForestTreantState.RootWindup:
                    MoveBody(Vector3.zero); Face(aim, 75);
                    TelegraphLine(origin, aim, ForestRootWave.Range, ForestRootWave.WarningHalfWidth, elapsed / rootWindup);
                    poseScale = new Vector3(1.04f, 1 - .075f * Mathf.Clamp01(elapsed / rootWindup), 1.04f);
                    poseRotation = Quaternion.Euler(7f * Mathf.Clamp01(elapsed / rootWindup), 0, 0);
                    if (elapsed >= rootWindup)
                    {
                        HideTelegraph(); roots.Add(ForestRootWave.Fire(origin, aim, Health, player, room)); RootWaves++;
                        Thud(origin + aim * .8f, 1.1f); Enter(ForestTreantState.RootFollowThrough);
                    }
                    break;
                case ForestTreantState.RootFollowThrough:
                    poseRotation = Quaternion.Euler(7f * Mathf.Exp(-elapsed * 3), 0, 0);
                    if (elapsed >= .7f) Enter(ForestTreantState.Recovery);
                    break;
                case ForestTreantState.Recovery:
                    if (elapsed >= .85f) { nextAttack = Time.time + attackCooldown; Enter(ForestTreantState.Stalk); }
                    break;
            }
            saplings.RemoveAll(s => !s); roots.RemoveAll(r => !r);
        }
        void Enter(ForestTreantState next)
        {
            State = next; elapsed = 0; IsWindingUp = next == ForestTreantState.ThrowWindup || next == ForestTreantState.RootWindup;
        }
        public void CancelAttack()
        {
            if (telegraph) telegraph.Hide();
            AttackAnticipation.Hide(transform);
            foreach (var sapling in saplings) if (sapling) sapling.Cancel(); saplings.Clear();
            foreach (var root in roots) if (root) root.Cancel(); roots.Clear();
            if (State != ForestTreantState.Dead) { Enter(ForestTreantState.Stalk); nextAttack = Time.time + 1.5f; }
        }
        protected override void OnDeathStart(Vector3 direction)
        {
            CancelAttack(); Enter(ForestTreantState.Dead); if (canopy) canopy.Stop();
        }
        void OnDisable() => CancelAttack();
        protected override void OnDestroy()
        {
            if (subscribedPlayer) subscribedPlayer.Died -= CancelAttack;
            CancelAttack(); base.OnDestroy();
        }
    }
}
