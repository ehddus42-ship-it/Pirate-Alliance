using System.Collections.Generic;
using AcRoguelike.Liminal;
using UnityEngine;

namespace AcRoguelike.Forest
{
    public enum ForestButterflyState { Hover, PollenWindup, PollenBurst, WindWindup, WindCast, Recovery, Dead }

    public sealed class ForestButterfly : LiminalPropMonster
    {
        public float pollenRadius = 4.2f, pollenWindup = 1.05f, windWindup = .9f, attackCooldown = 2.6f;
        public int pollenDamage = 15, windDamage = 17;
        public ForestButterflyState State { get; private set; }
        public int PollenBursts { get; private set; }
        public int WindShots { get; private set; }
        public int DamageAttempts { get; private set; }
        public float StateTime => elapsed;
        public Vector3 LockedAim => aim;
        readonly List<ForestWindProjectile> shots = new List<ForestWindProjectile>();
        readonly RaycastHit[] sight = new RaycastHit[96];
        ForestButterflyWingRig wings;
        float elapsed, nextAttack, orbit;
        int pattern;
        Vector3 origin, aim;
        LiminalPlayerHealth subscribedPlayer;
        protected override int MaxHealth(int s) => 58 + s * 12;
        protected override float HitTilt => 15;
        protected override float LyingHalfDepth => .18f;

        protected override void OnSetup()
        {
            displayName = "달가루 나비";
            if (pose)
            {
                wings = pose.GetComponent<ForestButterflyWingRig>(); if (!wings) wings = pose.gameObject.AddComponent<ForestButterflyWingRig>();
                wings.Initialize(pose.Find("Model") ? pose.Find("Model") : pose);
            }
            State = ForestButterflyState.Hover; nextAttack = Time.time + 1.2f; orbit = Random.value < .5f ? -1 : 1;
            if (subscribedPlayer) subscribedPlayer.Died -= CancelAttack;
            subscribedPlayer = player; if (subscribedPlayer) subscribedPlayer.Died += CancelAttack;
        }

        protected override void Think(float dt, Vector3 to, float distance)
        {
            elapsed += dt;
            poseOffset = Vector3.up * (1.15f + Mathf.Sin(Time.time * 3.2f) * .1f);
            poseScale = Vector3.one;
            poseRotation = Quaternion.Euler(Mathf.Sin(Time.time * 3.2f) * 3, 0, Mathf.Sin(Time.time * 2.1f) * 4);
            if (wings) wings.Effort = IsWindingUp ? .85f : State == ForestButterflyState.WindCast ? 1 : .2f;
            switch (State)
            {
                case ForestButterflyState.Hover:
                    Face(to, 130);
                    bool pollenNext = pattern % 2 == 0;
                    float desired = pollenNext ? 3.4f : 7;
                    if (Time.time >= staggerUntil)
                    {
                        Vector3 direction = distance > desired + .7f ? to : distance < desired - 1.2f ? -to : Vector3.Cross(Vector3.up, to) * orbit;
                        MoveBody(Steer(direction, 1.4f) * (distance > desired + .7f ? 2.1f : .75f) * dt);
                        if (Time.time >= nextAttack && distance <= (pollenNext ? pollenRadius + .15f : 11f) && CanSeePlayer())
                        {
                            origin = transform.position; aim = ForestAttackUtility.Flat(to); pattern++;
                            Enter(pollenNext ? ForestButterflyState.PollenWindup : ForestButterflyState.WindWindup);
                        }
                    }
                    break;
                case ForestButterflyState.PollenWindup:
                    MoveBody(Vector3.zero); telegraph.Circle(origin, pollenRadius, elapsed / pollenWindup);
                    poseOffset += Vector3.up * (.22f * Mathf.Clamp01(elapsed / pollenWindup));
                    if (elapsed >= pollenWindup)
                    {
                        HideTelegraph(); PollenBursts++;
                        ForestVfx.Burst(room ? room.transform : null, origin, new Color(.72f, .55f, 1, .85f), pollenRadius, 70);
                        ForestVfx.Motes(room ? room.transform : transform.parent, room ? room.transform.InverseTransformPoint(origin + Vector3.up) : origin + Vector3.up,
                            new Color(.9f, .76f, 1, .8f), 65, pollenRadius, false);
                        Vector3 delta = player.transform.position - origin; delta.y = 0;
                        if (delta.magnitude <= pollenRadius && ForestAttackUtility.HasLineOfSight(gameObject, origin + Vector3.up * .8f, player, sight))
                        { DamageAttempts++; player.TakeDamage(pollenDamage); }
                        Enter(ForestButterflyState.PollenBurst);
                    }
                    break;
                case ForestButterflyState.WindWindup:
                    MoveBody(Vector3.zero); Face(aim, 160);
                    TelegraphLine(origin, aim, 15f, 1.05f, elapsed / windWindup);
                    poseRotation *= Quaternion.Euler(-20f * Mathf.Clamp01(elapsed / windWindup), 0, 0);
                    if (elapsed >= windWindup)
                    {
                        HideTelegraph();
                        var shot = ForestWindProjectile.Fire(origin, aim, Health, player, room, windDamage); shots.Add(shot); WindShots++;
                        Enter(ForestButterflyState.WindCast);
                    }
                    break;
                case ForestButterflyState.WindCast:
                    poseRotation *= Quaternion.Euler(17f * Mathf.Exp(-elapsed * 5), 0, 0);
                    if (elapsed >= .4f) Enter(ForestButterflyState.Recovery);
                    break;
                case ForestButterflyState.PollenBurst:
                    if (elapsed >= .35f) Enter(ForestButterflyState.Recovery);
                    break;
                case ForestButterflyState.Recovery:
                    if (elapsed >= .65f) { nextAttack = Time.time + attackCooldown; Enter(ForestButterflyState.Hover); }
                    break;
            }
            shots.RemoveAll(shot => !shot);
        }
        void Enter(ForestButterflyState next)
        {
            State = next; elapsed = 0;
            IsWindingUp = next == ForestButterflyState.PollenWindup || next == ForestButterflyState.WindWindup;
        }
        public void CancelAttack()
        {
            if (telegraph) telegraph.Hide();
            foreach (var shot in shots) if (shot) shot.Cancel(); shots.Clear();
            if (State != ForestButterflyState.Dead) { Enter(ForestButterflyState.Hover); nextAttack = Time.time + 1; }
        }
        protected override void OnHit(Vector3 direction, float impact)
        {
            if (impact >= 1.4f && IsWindingUp) { if (telegraph) telegraph.Hide(); Enter(ForestButterflyState.Recovery); }
        }
        protected override void OnDeathStart(Vector3 direction)
        {
            CancelAttack(); Enter(ForestButterflyState.Dead); if (wings) wings.Stop();
            ForestVfx.Burst(room ? room.transform : null, transform.position, new Color(.75f, .6f, 1, .65f), 1.4f, 25);
        }
        void OnDisable() => CancelAttack();
        protected override void OnDestroy()
        {
            if (subscribedPlayer) subscribedPlayer.Died -= CancelAttack;
            CancelAttack(); base.OnDestroy();
        }
    }
}
