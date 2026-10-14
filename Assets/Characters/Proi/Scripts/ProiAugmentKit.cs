using System.Collections.Generic;
using AcRoguelike;
using AcRoguelike.Liminal;
using UnityEngine;

/// <summary>
/// 게임 안 Proi 의 전용 증강 효과 중 몸에 붙는 것들 (ProiAugments):
///  - 화염질주: 대시 중 발밑에 화염, 경로의 적에게 대시마다 한 번씩 피해.
///  - 불씨수호: 몸을 도는 불씨 하나가 피격 1회를 막음. 막은 뒤 다시 생길 때까지 대기.
///  - 반사의화염: 화염 보호막이 피격 1회를 막고 그 피해의 2배를 주변 적에게 되돌림. 대기 후 재생성.
/// 막기는 LiminalPlayerHealth 의 가드로 걸고, 둘 다 있으면 반사의화염이 먼저 막습니다.
/// Proi 모델에 붙어 있어 Proi 를 입고 있을 때만 동작합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ProiAugmentKit : MonoBehaviour
{
    PlayerMotor motor;
    MeleeSlash melee;
    LiminalPlayerHealth health;
    HunterAugments augments;
    ProiFireFx fx;
    readonly HashSet<TrainingEnemy> dashHits = new HashSet<TrainingEnemy>();
    int lastDashCount;
    float emberReadyAt, reflectReadyAt, orbit, flameClock;
    bool guarded;

    public bool EmberReady => Has(ProiAugments.EmberGuard) && Time.time >= emberReadyAt;
    public bool ReflectReady => Has(ProiAugments.ReflectFlame) && Time.time >= reflectReadyAt;
    public int DashHits { get; private set; }
    public int Blocks { get; private set; }
    public int Reflections { get; private set; }

    void Awake()
    {
        motor = GetComponentInParent<PlayerMotor>();
        melee = GetComponentInParent<MeleeSlash>();
        health = GetComponentInParent<LiminalPlayerHealth>();
        augments = GetComponentInParent<HunterAugments>();
        fx = GetComponent<ProiFireFx>();
    }

    void OnEnable()
    {
        if (!health) health = GetComponentInParent<LiminalPlayerHealth>();
        if (health && !guarded) { health.AddGuard(Absorb); guarded = true; }
        lastDashCount = motor ? motor.DashCount : 0;
    }

    void OnDisable()
    {
        if (health && guarded) health.RemoveGuard(Absorb);
        guarded = false;
    }

    bool Has(string id)
    {
        if (!augments) augments = GetComponentInParent<HunterAugments>();
        return ProiAugments.Has(augments, id);
    }

    float DamageScale => melee ? Mathf.Max(.1f, melee.damageMultiplier) : 1f;

    void Update()
    {
        if (!motor) return;
        BlazeDash();
        Shields();
    }

    // ---- 화염질주 ----------------------------------------------------------------------------------------
    void BlazeDash()
    {
        if (motor.DashCount != lastDashCount) { lastDashCount = motor.DashCount; dashHits.Clear(); }
        if (!motor.IsDashing || !Has(ProiAugments.BlazeDash)) return;
        Vector3 feet = motor.transform.position;
        if (fx && fx.Ready)
            for (int i = 0; i < 2; i++)
                fx.Flame(feet + Random.insideUnitSphere * .3f + Vector3.up * .2f, Vector3.up * Random.Range(1f, 2.2f), fx.FlameSize * Random.Range(.9f, 1.3f), Random.Range(.3f, .45f));
        foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
        {
            if (!enemy.IsAlive || !enemy.CanBeTargeted || dashHits.Contains(enemy)) continue;
            Vector3 offset = enemy.transform.position - feet;
            if (Mathf.Abs(offset.y) > 2.5f) continue;
            offset.y = 0;
            var body = enemy.GetComponent<CharacterController>();
            if (offset.magnitude > ProiAugments.BlazeDashWidth + (body ? body.radius : .45f)) continue;
            dashHits.Add(enemy);
            Vector3 push = offset.sqrMagnitude > .01f ? offset.normalized : motor.transform.forward;
            enemy.LastHitDirection = push;
            enemy.LastHitImpact = .9f;
            int damage = Mathf.Max(1, Mathf.RoundToInt(ProiAugments.BlazeDashDamage * DamageScale));
            enemy.TakeDamage(damage);
            HitFeedback.Hit(enemy, enemy.AimPoint, push, damage, false);
            if (fx) fx.Embers(enemy.AimPoint, 10, 1.5f, 3.5f, Vector3.up);
            DashHits++;
        }
    }

    // ---- 불씨수호 / 반사의화염 ------------------------------------------------------------------------------
    bool Absorb(int amount)
    {
        if (!isActiveAndEnabled) return false;
        Vector3 chest = motor ? motor.transform.position + Vector3.up * 1f : transform.position;
        if (ReflectReady)
        {
            reflectReadyAt = Time.time + ProiAugments.ReflectRecharge;
            Blocks++;
            Reflections++;
            int reflected = Mathf.Max(1, Mathf.RoundToInt(amount * ProiAugments.ReflectMultiplier * DamageScale));
            foreach (var enemy in FindObjectsByType<TrainingEnemy>(FindObjectsSortMode.None))
            {
                if (!enemy.IsAlive || !enemy.CanBeTargeted) continue;
                Vector3 offset = enemy.transform.position - chest;
                offset.y = 0;
                if (offset.magnitude > ProiAugments.ReflectRadius) continue;
                Vector3 push = offset.sqrMagnitude > .01f ? offset.normalized : Vector3.forward;
                enemy.LastHitDirection = push;
                enemy.LastHitImpact = 1.3f;
                enemy.TakeDamage(reflected);
                HitFeedback.Hit(enemy, enemy.AimPoint, push, reflected, true);
            }
            Burst(chest, 1.5f, 26);
            HitFeedback.Ring(chest - Vector3.up * .9f, Vector3.up, ProiAugments.ReflectRadius);
            HitFeedback.Shake(.07f, .2f);
            HitFeedback.Play(HitFeedback.Sfx.HeavyHit, .7f, 1.3f);
            return true;
        }
        if (EmberReady)
        {
            emberReadyAt = Time.time + ProiAugments.EmberGuardRecharge;
            Blocks++;
            Burst(chest, .9f, 14);
            HitFeedback.Play(HitFeedback.Sfx.JustDodge, .6f, 1.5f);
            HitFeedback.Shake(.03f, .12f);
            return true;
        }
        return false;
    }

    void Burst(Vector3 at, float size, int embers)
    {
        if (!fx || !fx.Ready) return;
        fx.Flash(at, size);
        fx.Embers(at, embers, 2f, 5f, Vector3.up);
        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward;
            fx.Flame(at + dir * .3f, dir * 3f + Vector3.up, fx.FlameSize * 1.2f, .35f);
        }
    }

    /// <summary>The ready shields as fire around her: one orbiting ember (불씨수호), a ring of flames (반사의화염).</summary>
    void Shields()
    {
        if (!fx || !fx.Ready) return;
        bool ember = EmberReady, reflect = ReflectReady;
        if (!ember && !reflect) return;
        orbit += Time.deltaTime * 260f;
        flameClock += Time.deltaTime;
        if (flameClock < .05f) return;
        flameClock = 0;
        Vector3 center = motor.transform.position + Vector3.up * .95f;
        if (ember)
        {
            Vector3 p = center + Quaternion.Euler(0, orbit, 0) * Vector3.forward * .65f;
            fx.Flame(p, Vector3.up * .4f, fx.FlameSize * .7f, .18f);
        }
        if (reflect)
        {
            float a = Random.Range(0f, 360f);
            Vector3 p = center + Quaternion.Euler(0, a, 0) * Vector3.forward * .85f + Vector3.up * Random.Range(-.6f, .4f);
            fx.Flame(p, Vector3.up * 1.2f, fx.FlameSize * .8f, .3f);
        }
    }
}
