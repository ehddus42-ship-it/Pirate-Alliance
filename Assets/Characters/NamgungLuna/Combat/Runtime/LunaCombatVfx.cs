using System.Collections.Generic;
using NamgungLuna.Motion;
using UnityEngine;

namespace NamgungLuna
{
    /// <summary>Character-local effects; timed bursts are pooled and all persistent effects stop on a form switch.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(270)]
    public sealed class LunaCombatVfx : MonoBehaviour
    {
        public LunaCombatFxLibrary library;
        [Min(.5f)] public float moonlightRadius = 4.5f;
        public bool MoonlightActive { get; private set; }
        public bool Awakened { get; private set; }
        public LunaEyeFire EyeFire => eyes;
        public int ActiveBursts { get { int n=0; foreach(var b in bursts) if(b.instance && b.instance.activeSelf) n++; return n; } }
        class Burst { public GameObject source, instance; public float expires; }
        readonly List<Burst> bursts = new List<Burst>();
        Transform world;
        GameObject field, ornament;
        LunaEyeFire eyes;
        LunaMoonGait gait;
        Renderer[] oldMoonRenderers;
        bool[] oldMoonEnabled;
        MeshFilter ornamentMesh;
        MeshRenderer ornamentRenderer;
        MaterialPropertyBlock ornamentBlock;
        float moonlightUntil, reveal;
        Vector3 previousPosition;
        bool installed;

        void Awake() => Ensure();
        void Ensure()
        {
            if (installed || !library) return;
            installed = true;
            gait = GetComponent<LunaMoonGait>();
            world = new GameObject(name + "_LunarCombatFX").transform;
            eyes = GetComponent<LunaEyeFire>() ?? gameObject.AddComponent<LunaEyeFire>();
            eyes.flameMaterial = library.eyeFlameMaterial;
            eyes.wispMaterial = library.wispMaterial;
            if (library.moonlightField)
            {
                field = Instantiate(library.moonlightField, world);
                field.SetActive(false);
            }
            if (gait && gait.moon)
            {
                // Replace just the disc's renderers. The existing gait still owns the ornament's movement.
                oldMoonRenderers = gait.moon.GetComponentsInChildren<Renderer>(true);
                oldMoonEnabled = new bool[oldMoonRenderers.Length];
                for (int i=0;i<oldMoonRenderers.Length;i++) oldMoonEnabled[i]=oldMoonRenderers[i].enabled;
                ornament = new GameObject("LunarPhaseOrnament");
                ornament.transform.SetParent(transform, false);
                ornamentMesh = ornament.AddComponent<MeshFilter>();
                ornamentRenderer = ornament.AddComponent<MeshRenderer>();
                ornamentRenderer.sharedMaterial = library.moonMaterial;
                ornamentRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ornamentRenderer.receiveShadows = false;
                ornamentBlock = new MaterialPropertyBlock();
                var occlusion=GetComponent<LunaOcclusionFade>();
                if(occlusion)
                {
                    var fadeRenderers=new List<Renderer>(occlusion.renderers);
                    fadeRenderers.Add(ornamentRenderer);
                    occlusion.renderers=fadeRenderers.ToArray();
                }
                SetOrnament(false);
            }
            previousPosition = transform.position;
        }
        void OnEnable()
        {
            Ensure();
            if(world) world.gameObject.SetActive(true);
            if(installed) SetOrnament(false);
        }
        public GameObject CreateProjectileVisual(int phase, bool awakened, Transform parent)
        {
            Ensure();
            if (!library) return null;
            var prefab = awakened ? library.awakenedProjectile : library.projectiles[Mathf.Clamp(phase,0,2)];
            return prefab ? Instantiate(prefab,parent,false) : null;
        }
        public void PlayImpact(Vector3 position, int phase, bool awakened)
        {
            Ensure();
            if(!library) return;
            PlayBurst(awakened ? library.awakenedImpact : phase==2 ? library.fullMoonImpact : library.smallImpact, position, 2.4f);
        }
        public void PlayCast(bool ultimate)
        {
            Ensure();
            if(library) PlayBurst(ultimate ? library.ultimateCast : library.moonlightCast, transform.position+Vector3.up*.08f, 2.8f);
        }
        public void ClearTransientEffects()
        {
            foreach(var b in bursts) if(b.instance) b.instance.SetActive(false);
            SetMoonlight(false,0);
            SetAwakened(false);
        }
        void PlayBurst(GameObject source, Vector3 position, float life)
        {
            if(!source || !world) return;
            Burst selected=null;
            foreach(var b in bursts) if(b.source==source && !b.instance.activeSelf) { selected=b; break; }
            if(selected==null)
            {
                if(bursts.Count>=28)
                {
                    // Bound effect count during rapid full-moon fire; the gameplay projectile is independent.
                    foreach(var b in bursts) if(b.source==source) { selected=b; break; }
                    if(selected==null) return;
                }
                else
                {
                    selected=new Burst { source=source, instance=Instantiate(source,world) };
                    bursts.Add(selected);
                }
            }
            selected.instance.SetActive(false);
            selected.instance.transform.SetPositionAndRotation(position,Quaternion.identity);
            selected.expires=Time.time+life;
            selected.instance.SetActive(true);
            foreach(var ps in selected.instance.GetComponentsInChildren<ParticleSystem>(true)) { ps.Clear(true); ps.Play(true); }
        }
        public void SetMoonlight(bool active, float duration)
        {
            Ensure();
            MoonlightActive=active;
            moonlightUntil=active ? Time.time+Mathf.Max(.01f,duration) : 0;
            if(field)
            {
                field.transform.position=transform.position+Vector3.up*.08f;
                field.transform.localScale=Vector3.one*(moonlightRadius/4.5f);
                field.SetActive(active);
                foreach(var ps in field.GetComponentsInChildren<ParticleSystem>(true))
                    if(active) { ps.Clear(true); ps.Play(true); }
                    else ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            SetOrnament(active||Awakened);
        }
        public void SetAwakened(bool active)
        {
            Ensure();
            var combat=GetComponent<LunaCombatController>();
            Awakened=active && isActiveAndEnabled && combat && combat.IsAwakened;
            if(eyes) eyes.SetActiveFire(Awakened);
            SetOrnament(Awakened||MoonlightActive);
        }
        void SetOrnament(bool full)
        {
            if(!ornamentMesh || !library) return;
            for(int i=0;i<oldMoonRenderers.Length;i++) if(oldMoonRenderers[i]) oldMoonRenderers[i].enabled=false;
            ornament.SetActive(true);
            ornamentMesh.sharedMesh=library.phaseMeshes[full?2:0];
            ornamentRenderer.sharedMaterial=Awakened ? library.awakenedMoonMaterial : library.moonMaterial;
            reveal=full ? 1 : 0;
        }
        void LateUpdate()
        {
            if(!installed) return;
            if((transform.position-previousPosition).sqrMagnitude>36)
                foreach(var b in bursts) if(b.instance) b.instance.SetActive(false);
            previousPosition=transform.position;
            foreach(var b in bursts) if(b.instance.activeSelf && Time.time>=b.expires) b.instance.SetActive(false);
            if(MoonlightActive && Time.time>=moonlightUntil) SetMoonlight(false,0);
            if(field && MoonlightActive) field.transform.position=transform.position+Vector3.up*.08f;
            if(ornament && gait && gait.moon)
            {
                ornament.transform.position=gait.moon.position;
                // Match the original metre-scale ornament without inheriting the imported mesh's scale of 100.
                ornament.transform.rotation=transform.rotation*Quaternion.Euler(0,0,Mathf.Sin(Time.time*.4f)*5);
                ornament.transform.localScale=Vector3.one*(Awakened ? .92f : .82f);
                float pulse=1+Mathf.Sin(Time.time*(Awakened?4.8f:1.5f))*(Awakened?.1f:.025f);
                ornamentBlock.SetFloat("_Intensity",(reveal>.5f?1.65f:1.15f)*pulse);
                ornamentRenderer.SetPropertyBlock(ornamentBlock);
            }
        }
        void OnDisable()
        {
            MoonlightActive=Awakened=false;
            if(eyes) eyes.SetActiveFire(false);
            if(world) world.gameObject.SetActive(false);
            foreach(var b in bursts) if(b.instance) b.instance.SetActive(false);
            if(field) field.SetActive(false);
            if(ornament) ornament.SetActive(false);
            if(oldMoonRenderers!=null) for(int i=0;i<oldMoonRenderers.Length;i++)
                if(oldMoonRenderers[i]) oldMoonRenderers[i].enabled=oldMoonEnabled[i];
        }
        void OnDestroy() { if(world) Destroy(world.gameObject); }
    }
}
