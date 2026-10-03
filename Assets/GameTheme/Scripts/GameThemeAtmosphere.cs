using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AcRoguelike.GameTheme
{
    /// <summary>
    /// First-room lease for the exhibition's environment. Works for lobby missions and standalone scenes.
    /// No asset is modified: the previous scene lighting, volumes and framing return when the last lease ends.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameThemeAtmosphere : MonoBehaviour
    {
        public VolumeProfile profile;
        static readonly HashSet<GameThemeAtmosphere> leases = new HashSet<GameThemeAtmosphere>();
        static Snapshot current;
        bool registered;

        public static bool IsApplied => current != null;
        public static int LeaseCount => leases.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            if (current != null) current.Restore();
            current = null;
            leases.Clear();
        }

        void OnEnable()
        {
            if (!Application.isPlaying || registered) return;
            registered = true;
            leases.Add(this);
            if (current == null)
            {
                current = new Snapshot();
                current.Apply(profile);
            }
        }

        void OnDisable() => Release();
        void OnDestroy() => Release();

        void Release()
        {
            if (!registered) return;
            registered = false;
            leases.Remove(this);
            if (leases.Count == 0 && current != null)
            {
                current.Restore();
                current = null;
            }
        }

        sealed class Snapshot
        {
            readonly AmbientMode ambientMode;
            readonly Color ambientLight, ambientSky, ambientEquator, ambientGround, fogColor;
            readonly float ambientIntensity, reflectionIntensity, fogDensity, fogStart, fogEnd;
            readonly bool fog;
            readonly FogMode fogMode;
            readonly Material skybox;
            readonly Light sun;
            readonly Camera camera;
            readonly Color cameraBackground;
            readonly CameraClearFlags cameraClear;
            readonly bool cameraHdr;
            readonly float cameraFov;
            readonly UniversalAdditionalCameraData cameraData;
            readonly bool cameraPost;
            readonly LayerMask cameraVolumeMask;
            readonly AntialiasingMode cameraAA;
            readonly IsometricFollowCamera rig;
            readonly float pitch, yaw, distance;
            readonly Vector3 focusOffset;
            readonly List<LightState> lights = new List<LightState>();
            readonly List<VolumeState> volumes = new List<VolumeState>();
            GameObject owned;
            VolumeProfile ownedProfile;
            bool restored;

            struct LightState { public Light light; public bool enabled; }
            struct VolumeState { public Volume volume; public bool enabled; }

            public Snapshot()
            {
                ambientMode = RenderSettings.ambientMode;
                ambientLight = RenderSettings.ambientLight;
                ambientSky = RenderSettings.ambientSkyColor;
                ambientEquator = RenderSettings.ambientEquatorColor;
                ambientGround = RenderSettings.ambientGroundColor;
                ambientIntensity = RenderSettings.ambientIntensity;
                reflectionIntensity = RenderSettings.reflectionIntensity;
                skybox = RenderSettings.skybox;
                sun = RenderSettings.sun;
                fog = RenderSettings.fog; fogMode = RenderSettings.fogMode;
                fogColor = RenderSettings.fogColor; fogDensity = RenderSettings.fogDensity;
                fogStart = RenderSettings.fogStartDistance; fogEnd = RenderSettings.fogEndDistance;
                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.type == LightType.Directional) lights.Add(new LightState { light = light, enabled = light.enabled });
                foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
                    volumes.Add(new VolumeState { volume = volume, enabled = volume.enabled });
                camera = Camera.main;
                if (camera)
                {
                    cameraBackground = camera.backgroundColor; cameraClear = camera.clearFlags;
                    cameraHdr = camera.allowHDR; cameraFov = camera.fieldOfView;
                    cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
                    if (cameraData)
                    {
                        cameraPost = cameraData.renderPostProcessing;
                        cameraVolumeMask = cameraData.volumeLayerMask;
                        cameraAA = cameraData.antialiasing;
                    }
                    rig = camera.GetComponent<IsometricFollowCamera>();
                    if (rig) { pitch = rig.pitch; yaw = rig.yaw; distance = rig.distance; focusOffset = rig.focusOffset; }
                }
            }

            public void Apply(VolumeProfile authoredProfile)
            {
                foreach (var state in lights) if (state.light) state.light.enabled = false;
                foreach (var state in volumes) if (state.volume) state.volume.enabled = false;
                owned = new GameObject("Game theme runtime atmosphere");
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.54f, .59f, .68f);
                RenderSettings.ambientIntensity = 1;
                RenderSettings.skybox = null;
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = new Color(.13f, .19f, .27f); RenderSettings.fogDensity = .006f;
                var key = MakeLight("Game exhibition key", new Color(1, .92f, .8f), 1.85f, new Vector3(52, -28, 0), LightShadows.Soft);
                key.shadowStrength = .6f;
                RenderSettings.sun = key;
                MakeLight("Game exhibition fill", new Color(.54f, .74f, 1), .55f, new Vector3(35, 150, 0), LightShadows.None);
                var volume = owned.AddComponent<Volume>();
                volume.isGlobal = true; volume.priority = 100; volume.weight = 1;
                if (authoredProfile) volume.sharedProfile = authoredProfile;
                else
                {
                    // A first build can add the room before its profile asset exists. The fallback is runtime-only.
                    ownedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                    ownedProfile.name = "Game exhibition runtime profile";
                    var bloom = ownedProfile.Add<Bloom>(true);
                    bloom.intensity.Override(.25f); bloom.threshold.Override(1.1f); bloom.scatter.Override(.55f);
                    ownedProfile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
                    var grade = ownedProfile.Add<ColorAdjustments>(true);
                    grade.postExposure.Override(.35f); grade.contrast.Override(5); grade.saturation.Override(3);
                    ownedProfile.Add<Vignette>(true).intensity.Override(.14f);
                    volume.sharedProfile = ownedProfile;
                }
                if (camera)
                {
                    camera.backgroundColor = RenderSettings.fogColor; camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.fieldOfView = 36; camera.allowHDR = true;
                    if (cameraData)
                    {
                        cameraData.renderPostProcessing = true; cameraData.volumeLayerMask = 1;
                        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    }
                    if (rig) { rig.pitch = 55; rig.yaw = 35; rig.distance = 24; rig.focusOffset = Vector3.zero; rig.Snap(); }
                }
            }

            Light MakeLight(string name, Color color, float intensity, Vector3 rotation, LightShadows shadows)
            {
                var light = new GameObject(name).AddComponent<Light>();
                light.transform.SetParent(owned.transform, false);
                light.type = LightType.Directional; light.color = color; light.intensity = intensity;
                light.shadows = shadows; light.transform.rotation = Quaternion.Euler(rotation);
                return light;
            }

            public void Restore()
            {
                if (restored) return;
                restored = true;
                // Disable immediately: Destroy is deferred, and a new route may acquire a lease this same frame.
                if (owned) owned.SetActive(false);
                foreach (var state in lights) if (state.light) state.light.enabled = state.enabled;
                foreach (var state in volumes) if (state.volume) state.volume.enabled = state.enabled;
                RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientSkyColor = ambientSky; RenderSettings.ambientEquatorColor = ambientEquator; RenderSettings.ambientGroundColor = ambientGround;
                RenderSettings.ambientIntensity = ambientIntensity; RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.skybox = skybox; RenderSettings.sun = sun;
                RenderSettings.fog = fog; RenderSettings.fogMode = fogMode; RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity; RenderSettings.fogStartDistance = fogStart; RenderSettings.fogEndDistance = fogEnd;
                if (camera)
                {
                    camera.backgroundColor = cameraBackground; camera.clearFlags = cameraClear;
                    camera.fieldOfView = cameraFov; camera.allowHDR = cameraHdr;
                    if (cameraData)
                    {
                        cameraData.renderPostProcessing = cameraPost; cameraData.volumeLayerMask = cameraVolumeMask;
                        cameraData.antialiasing = cameraAA;
                    }
                    if (rig) { rig.pitch = pitch; rig.yaw = yaw; rig.distance = distance; rig.focusOffset = focusOffset; rig.Snap(); }
                }
                if (owned) Object.Destroy(owned);
                if (ownedProfile)
                {
                    foreach (var component in ownedProfile.components) if (component) Object.Destroy(component);
                    Object.Destroy(ownedProfile);
                }
            }
        }
    }
}
