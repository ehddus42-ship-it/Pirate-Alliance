using UnityEngine;

/// <summary>
/// 주먹 쥐기: "Fist_L" / "Fist_R" 블렌드셰이프 (손가락 본이 없어서 블렌드셰이프로 손가락을 말아 쥡니다).
/// </summary>
public class ProiFist : MonoBehaviour
{
    [SerializeField] SkinnedMeshRenderer body;
    [Range(0f, 100f)] [SerializeField] float leftFist = 100f;
    [Range(0f, 100f)] [SerializeField] float rightFist = 100f;

    int left = -1, right = -1;

    void Awake()
    {
        if (body == null) body = GetComponentInChildren<SkinnedMeshRenderer>();
        if (body != null && body.sharedMesh != null)
        {
            left = body.sharedMesh.GetBlendShapeIndex("Fist_L");
            right = body.sharedMesh.GetBlendShapeIndex("Fist_R");
        }
    }

    void LateUpdate()
    {
        if (left >= 0) body.SetBlendShapeWeight(left, leftFist);
        if (right >= 0) body.SetBlendShapeWeight(right, rightFist);
    }
}
