using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.Ruins.Editor
{
    public static class RuinsMonsterPreview
    {
        const string Folder = "Library/RuinsMonsterValidation";

        [MenuItem("AC Roguelike/Ruins/Capture Model Review")]
        public static void Capture()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var oldAmbient = RenderSettings.ambientLight; var oldMode = RenderSettings.ambientMode;
            var oldIntensity = RenderSettings.ambientIntensity; bool oldFog = RenderSettings.fog;
            Material floor = null;
            try
            {
                SceneManager.SetActiveScene(scene);
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.46f, .50f, .55f);
                RenderSettings.ambientIntensity = 1; RenderSettings.fog = false;
                var root = new GameObject("Temporary model studio"); root.transform.position = new Vector3(0, 1000, 0);
                var camera = new GameObject("Preview Camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform, false);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .069f, .075f);
                camera.cullingMask = 1 << 30; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                Light(root.transform, "Key", new Vector3(35, -25, 0), 2.2f, new Color(1, .90f, .76f));
                Light(root.transform, "Fill", new Vector3(30, 145, 0), 1.1f, new Color(.61f, .73f, 1));
                floor = new Material(Shader.Find("Universal Render Pipeline/Lit")); floor.SetColor("_BaseColor", new Color(.085f, .10f, .105f));
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.transform.SetParent(root.transform, false);
                ground.transform.localPosition = new Vector3(0, -.14f, 0); ground.transform.localScale = new Vector3(40, .2f, 16);
                ground.GetComponent<Renderer>().sharedMaterial = floor; ground.layer = 30;
                var prefabs = Enum.GetValues(typeof(RuinsMonsterKind)).Cast<RuinsMonsterKind>()
                    .Select(k => Resources.Load<GameObject>("RuinsMonsters/" + k)).Where(p => p).ToArray();
                var models = new GameObject[prefabs.Length];
                Directory.CreateDirectory(Folder);
                for (int i = 0; i < prefabs.Length; i++)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], root.transform); models[i] = go;
                    go.transform.localPosition = Vector3.right * ((i - (prefabs.Length - 1) * .5f) * 4.1f);
                    var rig = go.GetComponentInChildren<RuinsMonsterRig>(); rig.RefreshRestPose();
                    if (rig.idleClip && rig.humanoidAnimator) rig.idleClip.SampleAnimation(rig.humanoidAnimator.gameObject, .2f);
                    foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
                    Bounds bounds = default; bool found = false;
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (!found) { bounds = r.bounds; found = true; } else bounds.Encapsulate(r.bounds); }
                    Debug.Log("RUINS_MODEL_BOUNDS " + go.name + " " + bounds.size.ToString("F3") + " min=" + (bounds.min - go.transform.position).ToString("F3"));
                }
                for (int i = 0; i < models.Length; i++)
                {
                    for (int j = 0; j < models.Length; j++) models[j].SetActive(i == j);
                    Vector3 focus = models[i].transform.position + Vector3.up * 1.65f;
                    camera.transform.position = focus + new Vector3(2.1f, 1.2f, 6.4f);
                    camera.transform.rotation = Quaternion.LookRotation(focus - camera.transform.position);
                    camera.orthographic = true; camera.orthographicSize = 2.1f;
                    Render(camera, Folder + "/review-" + prefabs[i].name + ".png", 800, 1000);
                }
                foreach (var go in models) go.SetActive(true);
                camera.transform.position = root.transform.position + new Vector3(1.2f, 5.2f, 15);
                camera.transform.rotation = Quaternion.LookRotation(root.transform.position + Vector3.up * 1.45f - camera.transform.position);
                camera.orthographic = true; camera.orthographicSize = 4.6f;
                Render(camera, Folder + "/apocalypse-roster.png", 2200, 1000);
                Debug.Log("RUINS_MODEL_REVIEW_CAPTURED");
            }
            finally
            {
                RenderSettings.ambientLight = oldAmbient; RenderSettings.ambientMode = oldMode;
                RenderSettings.ambientIntensity = oldIntensity; RenderSettings.fog = oldFog;
                SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true);
                if (floor) Object.DestroyImmediate(floor);
            }
        }

        static void Light(Transform parent, string name, Vector3 rotation, float intensity, Color color)
        {
            var light = new GameObject(name).AddComponent<Light>(); light.transform.SetParent(parent, false);
            light.transform.rotation = Quaternion.Euler(rotation); light.type = LightType.Directional;
            light.intensity = intensity; light.color = color; light.cullingMask = 1 << 30;
        }

        static void Render(Camera camera, string path, int width, int height)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active; Texture2D pixels = null;
            try
            {
                target.Create(); camera.aspect = width / (float)height;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                RenderPipeline.SubmitRenderRequest(camera, request); RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target; pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; if (pixels) Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target); }
        }
    }
}
