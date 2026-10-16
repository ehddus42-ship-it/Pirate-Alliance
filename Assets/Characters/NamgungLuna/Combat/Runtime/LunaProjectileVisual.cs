using UnityEngine;

namespace NamgungLuna
{
    /// <summary>The lunar silhouette stays readable from the game camera; world-space vapor preserves motion.</summary>
    [DefaultExecutionOrder(280)]
    public sealed class LunaProjectileVisual : MonoBehaviour
    {
        public Transform moon;
        public float rollSpeed = 15;
        public Camera CaptureCamera;
        ParticleSystem[] particles;
        Quaternion initialMoonRotation;
        float birth;
        Camera view;

        void Awake()
        {
            particles = GetComponentsInChildren<ParticleSystem>(true);
            if (moon) initialMoonRotation = moon.localRotation;
        }
        void OnEnable()
        {
            birth = Time.time;
            foreach (var ps in particles) { ps.Clear(true); ps.Play(true); }
        }
        void OnDisable()
        {
            foreach (var ps in particles) if (ps) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        void LateUpdate()
        {
            if (!moon) return;
            if (CaptureCamera) view = CaptureCamera;
            else if (!view || !view.isActiveAndEnabled) view = Camera.main;
            if (!view) return;
            moon.rotation = Quaternion.LookRotation(-view.transform.forward, view.transform.up) *
                initialMoonRotation * Quaternion.Euler(0, 0, Mathf.Sin((Time.time-birth)*1.4f)*rollSpeed);
        }
    }
}
