using UnityEngine;

namespace AcRoguelike.GameTheme
{
    /// <summary>Cuts away only this prop's renderers that sit between the gameplay camera and its player.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(110)]
    public sealed class GameThemeOccluder : MonoBehaviour
    {
        [Min(0)] public float boundsPadding = .18f;
        [Min(0)] public float targetHeight = .6f;

        sealed class Entry
        {
            public Renderer renderer;
            public bool overridden;
            public bool previousForceRenderingOff;
        }

        Entry[] entries;
        Camera gameplayCamera;
        IsometricFollowCamera follow;

        void Awake() => CacheRenderers();
        void OnEnable() => CacheRenderers();

        void CacheRenderers()
        {
            if (entries != null) return;
            var children = GetComponentsInChildren<Renderer>(true);
            entries = new Entry[children.Length];
            for (int i = 0; i < children.Length; i++) entries[i] = new Entry { renderer = children[i] };
        }

        void LateUpdate()
        {
            var main = Camera.main;
            if (gameplayCamera != main || !follow)
            {
                gameplayCamera = main;
                follow = gameplayCamera ? gameplayCamera.GetComponent<IsometricFollowCamera>() : null;
            }
            Evaluate(gameplayCamera, follow ? follow.target : null);
        }

        /// <summary>Also used by isolated editor captures with an explicit camera and player.</summary>
        public void Evaluate(Camera camera, Transform target)
        {
            CacheRenderers();
            if (!camera || !target || !isActiveAndEnabled) { Restore(); return; }
            Vector3 toTarget = target.position + Vector3.up * targetHeight - camera.transform.position;
            float distance = toTarget.magnitude;
            if (distance < .01f) { Restore(); return; }
            var ray = new Ray(camera.transform.position, toTarget / distance);
            foreach (var entry in entries)
            {
                var renderer = entry.renderer;
                if (!renderer) continue;
                Bounds bounds = renderer.bounds;
                bounds.Expand(Mathf.Max(0, boundsPadding) * 2);
                bool blocksPlayer = renderer.enabled && renderer.gameObject.activeInHierarchy
                    && bounds.IntersectRay(ray, out float hitDistance) && hitDistance < distance - .01f;
                if (blocksPlayer)
                {
                    if (!entry.overridden)
                    {
                        entry.previousForceRenderingOff = renderer.forceRenderingOff;
                        entry.overridden = true;
                    }
                    renderer.forceRenderingOff = true;
                }
                else Restore(entry);
            }
        }

        static void Restore(Entry entry)
        {
            if (!entry.overridden) return;
            if (entry.renderer) entry.renderer.forceRenderingOff = entry.previousForceRenderingOff;
            entry.overridden = false;
        }

        void Restore()
        {
            if (entries == null) return;
            foreach (var entry in entries) Restore(entry);
        }

        void OnDisable() => Restore();
        void OnDestroy() => Restore();
        void OnTransformChildrenChanged()
        {
            Restore();
            entries = null;
        }
    }
}
