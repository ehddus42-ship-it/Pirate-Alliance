using UnityEngine;

/// <summary>
/// 무작위 간격으로 "Blink" 블렌드셰이프를 0 → 100 → 0 으로 재생합니다.
/// </summary>
public class ProiBlink : MonoBehaviour
{
    [SerializeField] SkinnedMeshRenderer face;
    [SerializeField] string blendShapeName = "Blink";
    [SerializeField] Vector2 interval = new Vector2(2f, 5f);
    [SerializeField] float closeTime = 0.06f;
    [SerializeField] float openTime = 0.1f;

    int index = -1;
    float nextBlink;
    float t = -1f;

    void Awake()
    {
        if (face == null) face = GetComponentInChildren<SkinnedMeshRenderer>();
        if (face != null && face.sharedMesh != null) index = face.sharedMesh.GetBlendShapeIndex(blendShapeName);
        nextBlink = Time.time + Random.Range(interval.x, interval.y);
    }

    void Update()
    {
        if (index < 0) return;
        if (t < 0f && Time.time >= nextBlink) t = 0f;
        if (t < 0f) return;

        t += Time.deltaTime;
        float w = t < closeTime ? t / closeTime : 1f - (t - closeTime) / openTime;
        face.SetBlendShapeWeight(index, Mathf.Clamp01(w) * 100f);
        if (t >= closeTime + openTime)
        {
            face.SetBlendShapeWeight(index, 0f);
            t = -1f;
            nextBlink = Time.time + Random.Range(interval.x, interval.y);
        }
    }
}
