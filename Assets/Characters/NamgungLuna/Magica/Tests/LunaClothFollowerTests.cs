#if UNITY_EDITOR && MAGICACLOTH2
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NamgungLuna.Magica.Tests
{
    public class LunaClothFollowerTests
    {
        GameObject root;
        Mesh source, ornament, simulated;

        [UnityTest]
        public IEnumerator OrnamentFollowsKnownRigidMotion()
        {
            root = new GameObject("Rigid cloth fixture"); root.SetActive(false);
            var points = new Vector3[121]; var normals = new Vector3[121];
            for (int i = 0; i < points.Length; i++) { points[i] = new Vector3((i % 11 - 5) * .01f, 0, (i / 11 - 5) * .01f); normals[i] = Vector3.up; }
            source = new Mesh { vertices = points, normals = normals };
            var cloth = new GameObject("Cloth"); cloth.transform.SetParent(root.transform);
            cloth.AddComponent<MeshFilter>().sharedMesh = source;
            var renderer = cloth.AddComponent<MeshRenderer>();
            var decoration = new GameObject("Ornament"); decoration.transform.SetParent(root.transform);
            var ornamentPoints = (Vector3[])points.Clone();
            for (int i = 0; i < ornamentPoints.Length; i++) ornamentPoints[i] += Vector3.up * .008f;
            ornament = new Mesh { vertices = ornamentPoints, normals = normals };
            var filter = decoration.AddComponent<MeshFilter>(); filter.sharedMesh = ornament;
            var follower = root.AddComponent<LunaClothFollower>();
            follower.target = renderer; follower.followers = new[] { decoration.AddComponent<MeshRenderer>() };
            root.SetActive(true); follower.enabled = false;
            var rotation = Quaternion.Euler(8, 25, -6); var offset = new Vector3(.025f, .012f, -.01f);
            var moved = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++) moved[i] = rotation * points[i] + offset;
            simulated = new Mesh { vertices = moved, normals = normals };
            cloth.GetComponent<MeshFilter>().sharedMesh = simulated;
            var update = typeof(LunaClothFollower).GetMethod("Follow", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < 12; i++) update.Invoke(follower, null);
            var actual = filter.sharedMesh.vertices; var actualNormals = filter.sharedMesh.normals;
            for (int i = 0; i < actual.Length; i++)
            {
                Assert.Less(Vector3.Distance(rotation * ornamentPoints[i] + offset, actual[i]), .0001f, "Rigid attachment must retain its shape and gap");
                Assert.Less(Vector3.Angle(rotation * Vector3.up, actualNormals[i]), .1f, "Normals follow the same motion");
            }
            root.SetActive(false); root.SetActive(true);
            yield return null;
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (root) Object.Destroy(root);
            yield return null;
            if (source) Object.Destroy(source); if (ornament) Object.Destroy(ornament); if (simulated) Object.Destroy(simulated);
        }
    }
}
#endif
