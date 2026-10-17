using UnityEngine;

namespace MoonLuna
{
    [CreateAssetMenu(menuName = "Moon Luna/Combat effects")]
    public sealed class LunaCombatFxLibrary : ScriptableObject
    {
        public GameObject[] projectiles = new GameObject[3];
        public GameObject awakenedProjectile;
        public GameObject smallImpact, fullMoonImpact, awakenedImpact;
        public GameObject moonlightField, moonlightCast, ultimateCast;
        public Material eyeFlameMaterial, wispMaterial, moonMaterial, awakenedMoonMaterial;
        public Mesh[] phaseMeshes = new Mesh[3];
    }
}
