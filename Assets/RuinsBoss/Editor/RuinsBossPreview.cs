using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AcRoguelike.RuinsBoss.Editor
{
    public static class RuinsBossPreview
    {
        const string Folder = "Library/RuinsBossValidation";
        [MenuItem("AC Roguelike/Ruins/Capture Boss Models")]
        public static void Capture()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var ambient = RenderSettings.ambientLight; var mode = RenderSettings.ambientMode; bool fog = RenderSettings.fog;
            Material floor = null;
            try
            {
                SceneManager.SetActiveScene(scene);
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.45f, .49f, .55f); RenderSettings.fog = false;
                var root = new GameObject("Temporary boss model studio"); root.transform.position = Vector3.up * 1000;
                var camera = new GameObject("Studio camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform, false);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .045f, .065f);
                camera.cullingMask = 1 << 30; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                Light(root.transform, new Vector3(35, -25, 0), 2.1f, new Color(1, .9f, .8f));
                Light(root.transform, new Vector3(30, 145, 0), 1.1f, new Color(.55f, .73f, 1));
                floor = new Material(Shader.Find("Universal Render Pipeline/Lit")); floor.SetColor("_BaseColor", new Color(.085f, .1f, .12f));
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.transform.SetParent(root.transform, false);
                ground.transform.localPosition = new Vector3(0, -.15f, 0); ground.transform.localScale = new Vector3(45, .2f, 20);
                ground.GetComponent<Renderer>().sharedMaterial = floor; ground.layer = 30;
                var names = new[] { "IronRam", "StormSovereign", "SiegeWalker", "MissileTurret", "MissileVisual" };
                var models = new GameObject[names.Length];
                Directory.CreateDirectory(Folder);
                for (int i = 0; i < names.Length; i++)
                {
                    var prefab = Resources.Load<GameObject>("RuinsBoss/" + names[i]);
                    models[i] = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    models[i].transform.localPosition = Vector3.right * ((i - 2) * 7.2f);
                    var rig = models[i].GetComponentInChildren<RuinsBossRig>();
                    if (rig)
                    {
                        rig.RefreshRestPose();
                        if (rig.humanoidAnimator && rig.idleClip) rig.idleClip.SampleAnimation(rig.humanoidAnimator.gameObject, .2f);
                    }
                    foreach (var t in models[i].GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
                }
                for (int i = 0; i < models.Length; i++)
                {
                    for (int j = 0; j < models.Length; j++) models[j].SetActive(j == i);
                    var b = RuinsBossBuilder.BoundsIn(models[i].transform);
                    Vector3 focus = models[i].transform.TransformPoint(b.center);
                    camera.transform.position = focus + new Vector3(4, 2.2f, 10);
                    camera.transform.rotation = Quaternion.LookRotation(focus - camera.transform.position);
                    camera.orthographic = true; camera.orthographicSize = Mathf.Max(.65f, Mathf.Max(b.size.y * .64f, b.size.x * .76f));
                    Render(camera, Folder + "/review-" + names[i] + ".png", 900, 1100);
                    var legs = models[i].GetComponentInChildren<RuinsWalkerLegRig>();
                    if (legs)
                    {
                        legs.PreviewPose(.7f);
                        Render(camera, Folder + "/review-" + names[i] + "-walking.png", 900, 1100);
                        legs.RestoreRestPose();
                    }
                }
                for (int i = 0; i < 4; i++) models[i].SetActive(true); models[4].SetActive(false);
                models[0].transform.localPosition = new Vector3(-7, 0, 0);
                models[1].transform.localPosition = new Vector3(0, 0, 0);
                models[2].transform.localPosition = new Vector3(7, 0, 0);
                models[3].transform.localPosition = new Vector3(0, 0, -7);
                var center = root.transform.position + new Vector3(0, 2, -1.5f);
                camera.transform.position = center + new Vector3(.5f, 8, 21); camera.transform.LookAt(center);
                camera.orthographicSize = 6.8f;
                Render(camera, Folder + "/boss-model-lineup.png", 2000, 1100);
                Debug.Log("RUINS_BOSS_REVIEW_CAPTURED");
            }
            finally
            {
                RenderSettings.ambientLight = ambient; RenderSettings.ambientMode = mode; RenderSettings.fog = fog;
                SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true);
                if (floor) Object.DestroyImmediate(floor);
            }
        }
        static void Light(Transform parent, Vector3 angles, float intensity, Color color)
        {
            var light = new GameObject("Studio light").AddComponent<Light>(); light.transform.SetParent(parent, false);
            light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(angles);
            light.intensity = intensity; light.color = color; light.cullingMask = 1 << 30;
        }
        static void Render(Camera camera, string path, int width, int height)
        {
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active; Texture2D pixels = null;
            try
            {
                texture.Create(); camera.aspect = width / (float)height;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = texture };
                RenderPipeline.SubmitRenderRequest(camera, request); RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = texture; pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; if (pixels) Object.DestroyImmediate(pixels); texture.Release(); Object.DestroyImmediate(texture); }
        }
    }
}
