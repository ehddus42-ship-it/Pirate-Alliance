using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 임시 적 허수아비 만들기: Tools > Proi > Create Scarecrow
/// 기본 도형(기둥 / 가로대 / 셔츠 / 지푸라기 / 자루 머리 / 모자)으로 허수아비를 만들어 씬 뷰 가운데(없으면 원점 앞)에 놓고,
/// 머티리얼은 Assets/Characters/Proi/Materials/Scarecrow_*.mat 으로 저장합니다. 발밑이 원점, 충돌은 루트의 캡슐 하나.
/// </summary>
public static class ProiScarecrowSetup
{
    const string MatDir = "Assets/Characters/Proi/Materials";

    [MenuItem("Tools/Proi/Create Scarecrow")]
    static void FromMenu()
    {
        Vector3 at = new Vector3(0f, 0f, 3f);
        if (SceneView.lastActiveSceneView != null)
        {
            Vector3 p = SceneView.lastActiveSceneView.pivot;
            at = new Vector3(p.x, 0f, p.z);
        }
        Selection.activeGameObject = Create(at, Quaternion.identity);
    }

    public static GameObject Create(Vector3 position, Quaternion rotation)
    {
        Material wood = Mat("Scarecrow_Wood", new Color(0.36f, 0.23f, 0.13f));
        Material straw = Mat("Scarecrow_Straw", new Color(0.86f, 0.7f, 0.34f));
        Material burlap = Mat("Scarecrow_Burlap", new Color(0.74f, 0.62f, 0.44f));
        Material shirt = Mat("Scarecrow_Shirt", new Color(0.28f, 0.36f, 0.55f));
        Material hat = Mat("Scarecrow_Hat", new Color(0.2f, 0.13f, 0.08f));

        var root = new GameObject("Scarecrow");
        Undo.RegisterCreatedObjectUndo(root, "Create Scarecrow");
        root.transform.SetPositionAndRotation(position, rotation);

        Part(root, PrimitiveType.Cylinder, "Post", new Vector3(0f, 0.85f, -0.04f), Vector3.zero, new Vector3(0.08f, 0.85f, 0.08f), wood);
        Part(root, PrimitiveType.Cylinder, "Crossbar", new Vector3(0f, 1.3f, -0.04f), new Vector3(0f, 0f, 90f), new Vector3(0.06f, 0.58f, 0.06f), wood);
        Part(root, PrimitiveType.Cube, "Shirt", new Vector3(0f, 1.16f, 0f), Vector3.zero, new Vector3(0.48f, 0.52f, 0.24f), shirt);
        Part(root, PrimitiveType.Cube, "Sleeves", new Vector3(0f, 1.3f, 0f), Vector3.zero, new Vector3(0.9f, 0.14f, 0.16f), shirt);
        Part(root, PrimitiveType.Sphere, "StrawL", new Vector3(-0.55f, 1.3f, 0f), Vector3.zero, new Vector3(0.16f, 0.2f, 0.16f), straw);
        Part(root, PrimitiveType.Sphere, "StrawR", new Vector3(0.55f, 1.3f, 0f), Vector3.zero, new Vector3(0.16f, 0.2f, 0.16f), straw);
        Part(root, PrimitiveType.Sphere, "StrawWaist", new Vector3(0f, 0.86f, 0f), Vector3.zero, new Vector3(0.36f, 0.18f, 0.22f), straw);
        Part(root, PrimitiveType.Sphere, "Head", new Vector3(0f, 1.6f, 0f), Vector3.zero, new Vector3(0.32f, 0.34f, 0.32f), burlap);
        Part(root, PrimitiveType.Cylinder, "HatBrim", new Vector3(0f, 1.75f, 0f), new Vector3(-6f, 0f, 4f), new Vector3(0.46f, 0.015f, 0.46f), hat);
        Part(root, PrimitiveType.Cylinder, "HatCrown", new Vector3(0f, 1.84f, 0f), new Vector3(-6f, 0f, 4f), new Vector3(0.26f, 0.08f, 0.26f), hat);

        var col = root.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.95f, 0f);
        col.height = 1.9f;
        col.radius = 0.28f;
        root.AddComponent<ProiScarecrow>();
        EditorSceneManager.MarkSceneDirty(root.scene);
        return root;
    }

    static void Part(GameObject root, PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = pos;
        go.transform.localEulerAngles = euler;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static Material Mat(string name, Color color)
    {
        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", 0.12f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
