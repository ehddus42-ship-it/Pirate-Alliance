using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>A short, camera-facing white glint near the end of an attack windup.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(100)]
    public sealed class AttackAnticipation : MonoBehaviour
    {
        public const float TriggerProgress = .78f;
        const float Duration = .18f;
        static Material material;
        Transform anchor, sparkle;
        Renderer anchorRenderer;
        Renderer[] bodyRenderers;
        CharacterController body;
        TrainingEnemy health;
        Mesh mesh;
        Vector3[] glintVertices, frameVertices;
        MeshRenderer sparkleRenderer;
        MaterialPropertyBlock properties;
        float lastProgress, startedAt = -1;
        int lastFrame = -10;
        bool fired, allowDefeated;
        public bool Visible => isActiveAndEnabled && sparkleRenderer && sparkleRenderer.enabled && sparkle.gameObject.activeInHierarchy;
        public Transform Anchor => anchor;
        public Vector3 CuePosition => Position();

        public static void Show(Transform owner, float progress, Transform faceAnchor = null, bool allowDefeated = false)
        {
            if (!owner || !owner.gameObject.activeInHierarchy) return;
            var cue = owner.GetComponent<AttackAnticipation>();
            if (!cue) cue = owner.gameObject.AddComponent<AttackAnticipation>();
            if (!cue.sparkleRenderer) cue.Initialize();
            if (faceAnchor && cue.anchor != faceAnchor)
            {
                cue.anchor = faceAnchor; cue.anchorRenderer = faceAnchor.GetComponent<Renderer>();
            }
            cue.allowDefeated = allowDefeated;
            cue.Windup(progress);
        }

        public static void Hide(Transform owner)
        {
            if (owner && owner.TryGetComponent<AttackAnticipation>(out var cue)) cue.Clear();
        }

        void Initialize()
        {
            properties = new MaterialPropertyBlock();
            health = GetComponent<TrainingEnemy>();
            body = GetComponent<CharacterController>();
            Transform visual = transform.Find("Visual");
            var models = new List<Renderer>();
            foreach (var renderer in (visual ? visual : transform).GetComponentsInChildren<Renderer>(true))
                if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer) && !renderer.GetComponentInParent<Telegraph>()
                    && renderer.gameObject.layer != 2) models.Add(renderer);
            bodyRenderers = models.ToArray();
            int best = 0;
            foreach (var candidate in (visual ? visual : transform).GetComponentsInChildren<Transform>(true))
            {
                string name = candidate.name.ToLowerInvariant().Replace("_", "").Replace(" ", "");
                int score = name == "headfront" ? 5 : name == "face" || name == "faceanchor" || name == "lcdface" ? 4
                    : name == "head" || name.EndsWith(":head") || name.EndsWith("/head") ? 3
                    : name == "eyes" || name == "eyecenter" ? 2 : 0;
                if (score <= best) continue;
                best = score; anchor = candidate;
            }
            if (anchor) anchorRenderer = anchor.GetComponent<Renderer>();
            var go = new GameObject("Attack anticipation glint") { layer = 2 };
            go.transform.SetParent(transform, false);
            sparkle = go.transform;
            mesh = BuildGlint();
            mesh.MarkDynamic();
            glintVertices = mesh.vertices; frameVertices = new Vector3[glintVertices.Length];
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            sparkleRenderer = go.AddComponent<MeshRenderer>();
            if (!material) { material = Telegraph.CreateOverlayMaterial("White anticipation glint"); material.renderQueue = 3103; }
            sparkleRenderer.sharedMaterial = material;
            sparkleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sparkleRenderer.receiveShadows = false;
            sparkleRenderer.enabled = false;
        }

        void Windup(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (Time.frameCount - lastFrame > 1 || progress + .01f < lastProgress)
            {
                fired = false; startedAt = -1;
            }
            lastFrame = Time.frameCount; lastProgress = progress;
            if (!fired && progress >= TriggerProgress)
            {
                startedAt = Time.time; fired = true;
            }
            RenderGlint();
        }

        void LateUpdate()
        {
            if (health && !health.IsAlive && !allowDefeated) { Clear(); return; }
            // Windup clocks also stop during pause/hit stop. Preserve this pulse until time resumes.
            if (Time.deltaTime <= 0) { if (lastFrame >= 0) lastFrame = Time.frameCount; return; }
            if (Time.frameCount - lastFrame > 1) { Clear(); return; }
            RenderGlint();
        }

        void RenderGlint()
        {
            if (!sparkleRenderer) return;
            float age = startedAt < 0 ? Duration : Time.time - startedAt;
            sparkleRenderer.enabled = age < Duration;
            if (!sparkleRenderer.enabled) return;
            float pulse = Mathf.Sin(Mathf.Clamp01(age / Duration) * Mathf.PI);
            var camera = Camera.main;
            Vector3 position = Position();
            Quaternion facing = camera ? camera.transform.rotation : Quaternion.identity;
            sparkle.SetPositionAndRotation(position, Quaternion.identity);
            float size = Mathf.Lerp(.76f, 1.44f, pulse);
            // Build in camera/world space before converting back, including non-uniform owner scales.
            sparkle.localScale = Vector3.one;
            for (int i = 0; i < glintVertices.Length; i++)
                frameVertices[i] = sparkle.InverseTransformPoint(position + facing * (glintVertices[i] * size));
            mesh.vertices = frameVertices; mesh.RecalculateBounds();
            properties.SetColor("_BaseColor", new Color(1, 1, 1, Mathf.Lerp(.3f, 1, pulse)));
            sparkleRenderer.SetPropertyBlock(properties);
        }

        Vector3 Position()
        {
            Vector3 position;
            if (anchor) position = anchorRenderer ? anchorRenderer.bounds.center : anchor.position;
            else
            {
                Bounds bounds = default;
                bool found = false;
                if (bodyRenderers != null) foreach (var renderer in bodyRenderers)
                {
                    if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
                }
                position = found ? bounds.center : body ? transform.TransformPoint(body.center) : transform.position + Vector3.up;
            }
            // Burrowing enemies still announce their emergence above the floor.
            position.y = Mathf.Max(position.y, transform.position.y + .25f);
            return position;
        }

        static Mesh BuildGlint()
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            for (int ray = 0; ray < 8; ray++)
            {
                float angle = ray * Mathf.PI / 4;
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                var side = new Vector3(-direction.y, direction.x, 0) * .055f;
                int n = vertices.Count;
                vertices.Add(-side); vertices.Add(side); vertices.Add(direction * (ray % 2 == 0 ? 1 : .48f));
                colors.Add(Color.white); colors.Add(Color.white); colors.Add(new Color(1, 1, 1, 0));
                triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2);
            }
            var result = new Mesh { name = "Anticipation star glint" };
            result.SetVertices(vertices); result.SetColors(colors); result.SetTriangles(triangles, 0); result.RecalculateBounds();
            return result;
        }

        void Clear()
        {
            startedAt = -1; fired = false; lastFrame = -10;
            if (sparkleRenderer) sparkleRenderer.enabled = false;
        }
        void OnDisable() => Clear();
        void OnDestroy()
        {
            if (!mesh) return;
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }
    }
}
