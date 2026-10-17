using UnityEngine;

/// <summary>
/// Proi_Magica 프리팹이 쓰는 분할 메시(몸 / 머리카락 / 하의)를 하위 에셋으로 담는 컨테이너.
/// 프로젝트가 텍스트 직렬화라 블렌드셰이프가 있는 메시는 수백 MB 가 되므로 이 에셋만 이진으로 저장합니다.
/// Tools > Proi > Setup Magica Cloth 가 만듭니다.
/// </summary>
[PreferBinarySerialization]
public sealed class ProiMeshBundle : ScriptableObject
{
}
