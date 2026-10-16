using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace NamgungLuna.Motion
{
    /// <summary>
    /// 평타 빛 구체: 곧게 날아가다 콜라이더에 닿거나 사거리 끝에서 사라집니다. 맞은 대상은 콜백으로 알려 줍니다(피해는 게임 쪽).
    /// The basic attack's light orb: flies straight, sphere-casts ahead each frame (ignoring the caster's own colliders),
    /// reports the first collider it meets and fades out there or at the end of its range.
    /// </summary>
    public class LunaLightOrb : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");

        Vector3 velocity;
        float range, travelled, radius, size, fade = -1f;
        LayerMask mask;
        Transform owner;
        Action<Collider, Vector3> onHit;
        Color color;
        MeshRenderer core;
        TrailRenderer trail;
        Light glow;
        MaterialPropertyBlock block;

        public static LunaLightOrb Launch(Vector3 origin, Vector3 direction, float speed, float range, float radius, float size, Color color,
            Material glowMaterial, Material trailMaterial, LayerMask mask, Transform owner, Action<Collider, Vector3> onHit)
        {
            var go = new GameObject("LunaLightOrb");
            go.transform.position = origin;
            var orb = go.AddComponent<LunaLightOrb>();
            orb.velocity = direction.normalized * speed;
            orb.range = range;
            orb.radius = radius * size;
            orb.size = size;
            orb.mask = mask;
            orb.owner = owner;
            orb.onHit = onHit;
            orb.color = color;
            orb.block = new MaterialPropertyBlock();
            if (glowMaterial != null)
            {
                var quad = new GameObject("Core");
                quad.transform.SetParent(go.transform, false);
                quad.transform.localScale = Vector3.one * (0.32f * size);
                quad.AddComponent<MeshFilter>().sharedMesh = LunaBasicAttack.Quad();
                orb.core = quad.AddComponent<MeshRenderer>();
                orb.core.sharedMaterial = glowMaterial;
                orb.core.shadowCastingMode = ShadowCastingMode.Off;
                orb.core.receiveShadows = false;
            }
            if (trailMaterial != null)
            {
                orb.trail = go.AddComponent<TrailRenderer>();
                orb.trail.sharedMaterial = trailMaterial;
                orb.trail.time = 0.16f;
                orb.trail.minVertexDistance = 0.04f;
                orb.trail.widthMultiplier = 0.16f * size;
                orb.trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 1f) },
                                 new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
                orb.trail.colorGradient = gradient;
                orb.trail.alignment = LineAlignment.View;
                orb.trail.shadowCastingMode = ShadowCastingMode.Off;
                orb.trail.receiveShadows = false;
            }
            orb.glow = go.AddComponent<Light>();
            orb.glow.type = LightType.Point;
            orb.glow.color = color.maxColorComponent > 0f ? color / color.maxColorComponent : Color.white;
            orb.glow.range = 1.6f * size;
            orb.glow.intensity = 1.2f;
            orb.glow.shadows = LightShadows.None;
            return orb;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (fade >= 0f)
            {
                fade += dt / 0.15f;
                if (fade >= 1f) { Destroy(gameObject); return; }
            }
            else
            {
                Vector3 step = velocity * dt;
                float distance = step.magnitude;
                if (distance > 1e-5f)
                {
                    RaycastHit[] hits = Physics.SphereCastAll(transform.position, radius, step / distance, distance, mask, QueryTriggerInteraction.Ignore);
                    float best = float.MaxValue;
                    RaycastHit first = default;
                    foreach (RaycastHit h in hits)
                    {
                        if (owner != null && h.transform.IsChildOf(owner)) continue;
                        if (h.distance < best) { best = h.distance; first = h; }
                    }
                    if (first.collider != null)
                    {
                        transform.position += step / distance * best;
                        onHit?.Invoke(first.collider, first.point == Vector3.zero ? transform.position : first.point);
                        Stop();
                    }
                    else
                    {
                        transform.position += step;
                        travelled += distance;
                        if (travelled >= range) Stop();
                    }
                }
            }
            float level = fade >= 0f ? 1f - fade : 1f;
            if (glow != null) glow.intensity = 1.2f * level;
            if (core != null)
            {
                Camera cam = Camera.main;
                if (cam != null) core.transform.rotation = Quaternion.LookRotation(core.transform.position - cam.transform.position, cam.transform.up);
                core.transform.localScale = Vector3.one * (0.32f * size * (fade >= 0f ? 1f + fade : 1f));
                block.SetColor(ColorId, color * (1.4f * level));
                core.SetPropertyBlock(block);
            }
        }

        void Stop()
        {
            fade = 0f;
            if (trail != null) trail.emitting = false;
        }
    }
}
