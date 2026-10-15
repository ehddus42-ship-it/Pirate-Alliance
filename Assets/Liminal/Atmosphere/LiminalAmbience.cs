using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>Room-sized sound field. Audio follows occupancy, rather than camera height.</summary>
    [DisallowMultipleComponent]
    public sealed class LiminalAmbience : MonoBehaviour
    {
        public enum Mood { Office, Pool, Service }
        public Mood mood;
        public Vector3 localCenter = new Vector3(0, 1, 14);
        public Vector3 halfExtents = new Vector3(10, 5, 14);
        [Range(0, 1)] public float volume = .18f;
        [Min(.1f)] public float doorwayBlendDistance = 4f;
        AudioSource source;
        Transform listener;

        void Start()
        {
            var motor = FindFirstObjectByType<PlayerMotor>();
            listener = motor ? motor.transform : (Camera.main ? Camera.main.transform : null);
            source = gameObject.AddComponent<AudioSource>();
            string clip = mood == Mood.Pool ? "pool_ventilation" : mood == Mood.Service ? "service_plant" : "fluorescent_air";
            source.clip = Resources.Load<AudioClip>("LiminalAudio/" + clip);
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.volume = 0;
            source.priority = 180;
            if (source.clip)
            {
                source.time = Mathf.Repeat(Mathf.Abs(transform.position.x * .31f + transform.position.z * .73f), source.clip.length);
                source.Play();
            }
        }

        void Update()
        {
            if (!source || !listener) return;
            Vector3 delta = transform.InverseTransformPoint(listener.position) - localCenter;
            float outside = Mathf.Max(Mathf.Abs(delta.x) - halfExtents.x, Mathf.Abs(delta.z) - halfExtents.z);
            float target = volume * (1 - Mathf.Clamp01(outside / doorwayBlendDistance));
            source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime * .14f);
        }

        void OnDisable() { if (source) source.Stop(); }
        void OnEnable() { if (source && source.clip) source.Play(); }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(.2f, .8f, .9f, .35f);
            Gizmos.DrawWireCube(localCenter, halfExtents * 2);
        }
    }
}
