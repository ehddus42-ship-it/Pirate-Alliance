using System;
using UnityEngine;

namespace NamgungLuna.Magica
{
    /// <summary>Authored-pose attachment weights, prepared in the editor instead of on character activation.</summary>
    public sealed class LunaClothBindingData : ScriptableObject
    {
        [Serializable] public class Anchor
        {
            public int[] cloth;
            public Vector3[] clothRest;
            public Vector3 restCenter;
        }
        [Serializable] public class Part
        {
            public Mesh source;
            public Vector3[] rest, restNormal;
            public int[] anchor;
            public float[] weight;
            public Matrix4x4 targetToFollower;
        }
        public Mesh target;
        public int targetVertexCount;
        public float anchorSpacing;
        public Anchor[] anchors;
        public Part[] parts;
    }
}
