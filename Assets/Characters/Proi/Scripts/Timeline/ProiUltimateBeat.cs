using System;
using System.ComponentModel;
using UnityEngine.Timeline;

/// <summary>궁극기 비트: Timeline 마커의 시간이 ProiUltimate 의 장면 시간이 됩니다 (마커를 끌면 그 장면이 옮겨짐).</summary>
public enum ProiBeat
{
    Clash,          // 두 주먹 마주침
    ChargeStart,    // 차징 시작
    ChargeFull,     // 풀차지
    ChargeEnd,      // 맥박 / 맥박 흔들림 멈춤 (웅크림 시작)
    Leap,           // 도약 (전진 이동 시작)
    Apex,           // 정점
    Dive,           // 내려꽂기 시작
    Slam,           // 땅 찍기
    Row1, Row2, Row3,   // 불기둥 줄
    End,            // 이동 입력 다시 받음
}

/// <summary>
/// 궁극기 비트 마커 (Proi 궁극기 트랙에 놓음).
/// Timeline 에 저장되는 클래스라 파일 이름이 클래스 이름과 같아야 합니다 (다르면 다시 열 때 마커가 사라짐).
/// </summary>
[Serializable, DisplayName("Proi/궁극기 비트")]
public class ProiUltimateBeat : Marker
{
    public ProiBeat beat;
}
