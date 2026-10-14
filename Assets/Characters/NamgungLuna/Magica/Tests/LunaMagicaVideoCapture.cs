using System.Collections;
using System.IO;
using System.Linq;
using MagicaCloth2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace NamgungLuna.Magica.Tests
{
    /// <summary>
    /// Records the demo scene (walk, turns, spin, wind) as numbered PNG frames: front three-quarter view on the left,
    /// back view on the right. Run explicitly: -testFilter NamgungLuna.Magica.Tests.LunaMagicaVideoCapture
    /// Frames go to MagicaValidation/video/ (encode with ffmpeg at 30 fps).
    /// </summary>
    [Explicit("records video frames")]
    public class LunaMagicaVideoCapture
    {
#if UNITY_EDITOR
        static string ScenePath => UnityEditor.AssetDatabase.GUIDToAssetPath(UnityEditor.AssetDatabase.FindAssets("NamgungLuna_MagicaDemo t:Scene").First());
#endif
        const int Fps = 30, Seconds = 13, Width = 720, Height = 960;

        [UnityTest, Timeout(1800000)]
        public IEnumerator RecordDemo()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "MagicaValidation", "video"));
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "f_*.png")) File.Delete(f);
            Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously = true;
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            Time.captureDeltaTime = 1f / Fps;
            var motion = Object.FindFirstObjectByType<LunaMagicaDemoMotion>();
            motion.play = false;
            var cloths = Object.FindObjectsByType<MagicaCloth>(FindObjectsSortMode.None);
            float start = Time.realtimeSinceStartup;
            while (cloths.Any(c => !c.Process.IsRunning()) && Time.realtimeSinceStartup - start < 120f) yield return null;
            for (int i = 0; i < Fps; i++) yield return null;   // settle one second before recording
            motion.ResetMotion();
            motion.play = true;

            Transform root = motion.target;
            var moon = root.GetComponentsInChildren<Renderer>().Where(r => r.name.Contains("FullMoon")).ToArray();
            var sceneCamera = Camera.main;
            if (sceneCamera != null) sceneCamera.enabled = false;
            var front = MakeCamera("VideoFront", new Vector3(1.25f, 1.2f, 3.3f), new Vector3(0f, 0.85f, 0f));
            var back = MakeCamera("VideoBack", new Vector3(-0.9f, 1.25f, -3.4f), new Vector3(0f, 0.88f, 0f));
            var rt = new RenderTexture(Width, Height, 24) { antiAliasing = 4 };
            var frame = new Texture2D(Width * 2, Height, TextureFormat.RGB24, false);

            for (int n = 0; n < Fps * Seconds; n++)
            {
                yield return null;   // Magica writes the cloth meshes in PostLateUpdate of the previous frame
                Render(front, rt, frame, 0);
                foreach (var r in moon) r.enabled = false;   // the moon prop stands behind her back
                Render(back, rt, frame, Width);
                foreach (var r in moon) r.enabled = true;
                frame.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"f_{n:0000}.png"), frame.EncodeToPNG());
            }
            Time.captureDeltaTime = 0f;
            Object.Destroy(rt);
            Assert.AreEqual(Fps * Seconds, Directory.GetFiles(dir, "f_*.png").Length);
        }

        static Camera MakeCamera(string name, Vector3 position, Vector3 lookAt)
        {
            var cam = new GameObject(name).AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.17f, 0.18f, 0.22f);
            cam.fieldOfView = 30f;
            cam.transform.position = position;
            cam.transform.LookAt(lookAt);
            cam.enabled = false;
            return cam;
        }

        static void Render(Camera cam, RenderTexture rt, Texture2D frame, int x)
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            frame.ReadPixels(new Rect(0, 0, Width, Height), x, 0);
            RenderTexture.active = null;
            cam.targetTexture = null;
        }
    }
}
