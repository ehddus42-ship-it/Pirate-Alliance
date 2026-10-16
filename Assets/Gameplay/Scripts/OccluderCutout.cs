using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AcRoguelike
{
    /// <summary>
    /// Keeps the player readable behind walls and overhead structures: inside a soft circle around the
    /// player, whatever stands between the camera and the player is drawn semi-transparent.
    /// Occluding renderers are moved to a late render queue so a snapshot of the scene without them can be
    /// blended back in (see <see cref="OccluderCutoutPasses"/>), which works with any opaque material.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(120)]
    [RequireComponent(typeof(Camera))]
    public sealed class OccluderCutout : MonoBehaviour
    {
        /// <summary>Render queue occluders are moved to: first in the transparent range, so later transparents still sort over them.</summary>
        public const int CutoutQueue = (int)RenderQueue.GeometryLast + 1;

        [Tooltip("Circle radius in metres around the player; it scales with camera zoom just like the character.")]
        [Min(.1f)] public float radius = 1.7f;
        [Tooltip("Share of the radius used as a soft fade edge.")]
        [Range(0, 1)] public float edgeSoftness = .35f;
        [Tooltip("How much of the occluder is still drawn inside the circle (0 = fully see-through).")]
        [Range(0, 1)] public float occluderOpacity = .3f;
        [Tooltip("Height above the player's feet the circle is centred on.")]
        public float focusHeight = .9f;
        [Tooltip("Renderers whose top is lower than this above the player's feet (floors, rugs, low props) never count as occluders.")]
        public float minOccluderTop = 1f;
        [Tooltip("Occluder pixels lower than this above the player's feet are never cut, so the floor around the player stays solid.")]
        public float floorClearance = .12f;
        public LayerMask occluderLayers = ~0;
        [Min(.1f)] public float fadeSpeed = 8f;
        [Tooltip("Seconds between scene scans for new structure renderers (rooms spawned at runtime).")]
        [Min(.1f)] public float rescanInterval = 1f;

        sealed class Candidate
        {
            public Renderer renderer;
            public bool eligible;
            public bool cut;
            public Material[] original;
            public Material[] swapped;
        }

        sealed class Clone
        {
            public Material material;
            public int users;
        }

        readonly List<Candidate> candidates = new();
        readonly Dictionary<Renderer, Candidate> byRenderer = new();
        readonly Dictionary<Material, Clone> clones = new();
        readonly List<Material> sharedScratch = new();

        Camera view;
        IsometricFollowCamera follow;
        OccluderCutoutPasses passes;
        float nextScan, strength;
        int cutCount;

        /// <summary>True while at least one renderer is being cut around the player.</summary>
        public bool IsCutting => cutCount > 0;

        void Awake()
        {
            view = GetComponent<Camera>();
            follow = GetComponent<IsometricFollowCamera>();
        }

        void OnEnable()
        {
            var shader = Resources.Load<Shader>("OccluderCutout");
            if (shader && shader.isSupported) passes ??= new OccluderCutoutPasses(shader);
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            SceneManager.sceneLoaded += OnSceneLoaded;
            nextScan = 0;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ReleaseAll();
            strength = 0;
        }

        void OnDestroy()
        {
            ReleaseAll();
            foreach (var clone in clones.Values) if (clone.material) Destroy(clone.material);
            clones.Clear();
            passes?.Dispose();
            passes = null;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => nextScan = 0;

        void LateUpdate()
        {
            Transform target = follow ? follow.target : null;
            if (passes == null || !target || !view.isActiveAndEnabled)
            {
                ReleaseAll();
                strength = 0;
                return;
            }

            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + rescanInterval;
                Rescan(target);
            }

            float feet = target.position.y;
            Vector3 origin = view.transform.position;
            Vector3 toFocus = target.position + Vector3.up * focusHeight - origin;
            float distance = toFocus.magnitude;
            var ray = new Ray(origin, distance > .01f ? toFocus / distance : view.transform.forward);

            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                var candidate = candidates[i];
                var renderer = candidate.renderer;
                if (!renderer)
                {
                    Forget(i);
                    continue;
                }
                if (candidate.eligible && Occludes(renderer, ray, distance, feet)) Cut(candidate);
                else Release(candidate);
            }

            SyncClones();
            float goal = cutCount > 0 ? 1 : 0;
            strength = Mathf.MoveTowards(strength, goal, fadeSpeed * Time.unscaledDeltaTime);
        }

        bool Occludes(Renderer renderer, Ray ray, float distance, float feet)
        {
            if (!renderer.enabled || renderer.forceRenderingOff || !renderer.isVisible || !renderer.gameObject.activeInHierarchy)
                return false;
            Bounds bounds = renderer.bounds;
            if (bounds.max.y < feet + minOccluderTop) return false;
            // The cut cone is never wider than the circle at the player, so this test is conservative.
            bounds.Expand((radius + .25f) * 2);
            return bounds.IntersectRay(ray, out float hit) && hit < distance;
        }

        void Rescan(Transform target)
        {
            var found = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var renderer in found)
            {
                if (!byRenderer.TryGetValue(renderer, out var candidate))
                {
                    candidate = new Candidate { renderer = renderer };
                    byRenderer.Add(renderer, candidate);
                    candidates.Add(candidate);
                }
                if (!candidate.cut) candidate.eligible = IsStructure(renderer, target);
            }
        }

        /// <summary>Static, opaque scenery only: the player, enemies and other moving actors are never cut.</summary>
        bool IsStructure(Renderer renderer, Transform target)
        {
            if ((occluderLayers.value & (1 << renderer.gameObject.layer)) == 0) return false;
            if (renderer.transform.IsChildOf(target)) return false;
            if (renderer.GetComponentInParent<CharacterController>(true) || renderer.GetComponentInParent<Rigidbody>(true)) return false;
            renderer.GetSharedMaterials(sharedScratch);
            if (sharedScratch.Count == 0) return false;
            foreach (var material in sharedScratch)
                if (!material || material.renderQueue > (int)RenderQueue.GeometryLast) return false;
            return true;
        }

        void Cut(Candidate candidate)
        {
            if (candidate.cut) return;
            var original = candidate.renderer.sharedMaterials;
            foreach (var material in original) if (!material) return;
            var swapped = new Material[original.Length];
            for (int i = 0; i < original.Length; i++) swapped[i] = Acquire(original[i]);
            candidate.original = original;
            candidate.swapped = swapped;
            candidate.renderer.sharedMaterials = swapped;
            candidate.cut = true;
            cutCount++;
        }

        void Release(Candidate candidate)
        {
            if (!candidate.cut) return;
            candidate.cut = false;
            cutCount--;
            // Only restore when nothing else replaced the materials while they were swapped.
            if (candidate.renderer && SameMaterials(candidate.renderer, candidate.swapped))
                candidate.renderer.sharedMaterials = candidate.original;
            foreach (var source in candidate.original) if (source && clones.TryGetValue(source, out var clone)) clone.users--;
            candidate.original = candidate.swapped = null;
        }

        void Forget(int index)
        {
            var candidate = candidates[index];
            Release(candidate);
            byRenderer.Remove(candidate.renderer);
            candidates.RemoveAt(index);
        }

        void ReleaseAll()
        {
            foreach (var candidate in candidates) Release(candidate);
        }

        Material Acquire(Material source)
        {
            if (!clones.TryGetValue(source, out var clone))
            {
                clone = new Clone
                {
                    material = new Material(source) { name = source.name + " (Occluder Cutout)", hideFlags = HideFlags.DontSave }
                };
                clones.Add(source, clone);
            }
            clone.users++;
            clone.material.renderQueue = CutoutQueue;
            return clone.material;
        }

        /// <summary>Keeps swapped materials in step with runtime edits to their source materials.</summary>
        void SyncClones()
        {
            foreach (var pair in clones)
            {
                if (pair.Value.users <= 0 || !pair.Key || !pair.Value.material) continue;
                pair.Value.material.CopyPropertiesFromMaterial(pair.Key);
                pair.Value.material.renderQueue = CutoutQueue;
            }
        }

        bool SameMaterials(Renderer renderer, Material[] expected)
        {
            renderer.GetSharedMaterials(sharedScratch);
            if (sharedScratch.Count != expected.Length) return false;
            for (int i = 0; i < expected.Length; i++) if (sharedScratch[i] != expected[i]) return false;
            return true;
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != view || passes == null || strength <= .001f) return;
            Transform target = follow ? follow.target : null;
            if (!target) return;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            if (!cameraData || cameraData.scriptableRenderer == null) return;

            Vector3 focus = camera.worldToCameraMatrix.MultiplyPoint(target.position + Vector3.up * focusHeight);
            if (-focus.z <= camera.nearClipPlane) return;
            passes.Configure(focus, radius, edgeSoftness, target.position.y + floorClearance,
                strength * (1 - occluderOpacity));
            passes.Enqueue(cameraData.scriptableRenderer);
        }
    }
}
