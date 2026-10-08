using UnityEngine;

namespace AcRoguelike.Forest
{
    public sealed class ForestEffectLifetime : MonoBehaviour
    {
        public float seconds = 1.5f, radius = 1;
        public Material ownedMaterial;
        public LineRenderer ring;
        float elapsed;
        void Update()
        {
            if (Time.deltaTime <= 0 || seconds < 0) return;
            elapsed += Time.deltaTime;
            if (ring)
            {
                float p = Mathf.Clamp01(elapsed / .5f);
                ring.transform.localScale = Vector3.one * Mathf.Lerp(.2f, radius, Mathf.Sin(p * Mathf.PI * .5f));
                ring.startColor = ring.endColor = new Color(1, 1, 1, (1 - p) * .8f);
            }
            if (elapsed >= seconds) Destroy(gameObject);
        }
        void OnDestroy() { if (ownedMaterial) Destroy(ownedMaterial); }
    }
}
