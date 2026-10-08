# 파란 불 게이트

로비 게이트를 위로 타오르는 파란 불꽃으로 교체했다. 어두운 입구 주변에 청백색 불꽃 중심, 파란 불길과 흩어지는 불티를 배치했다. 지지대는 위를 열어 두고, 보강판과 볼트가 달린 금속 기둥으로 구성했다.

- 불꽃마다 길이·폭·휘어짐·흐르는 속도가 다르다.
- 바닥의 푸른 반사와 두 광원이 불길에 맞춰 약하게 흔들린다.
- 진입 시 화면 전환도 짙은 파랑으로 이어진다.
- 미션 선택과 던전 진입·복귀는 기존 동작을 사용한다.
- 일시정지와 로비 비활성화 시 효과도 멈춘다. 복귀하면 같은 효과가 이어진다.

## 구성

`Assets/Liminal/Scenes/LiminalRun.unity` 실행 시 시작 로비에서 확인할 수 있다.

불꽃 리본 144개를 포함해 생성 메시 4개, 2,820삼각형으로 구성한다. 재질 5개, 광원 2개, 최대 128개의 불티를 사용한다. 불꽃 무늬는 전용 셰이더에서 계산하며 외부 텍스처를 사용하지 않는다. 효과 자체에는 충돌체가 없다.

`LiminalLobby`가 지지대와 `BlueFireGate`를 생성하고, 생성한 메시·재질은 로비가 해제할 때 함께 정리한다. 셰이더는 `Resources/LiminalLobby/BlueFireGate.shader`에 두어 게임 빌드에도 포함한다.

## 검증 및 촬영

Unity 6000.3.19f1에서 최종 **40개 검사 통과, 오류 0개**. 기록된 불티의 최대 수는 39개다. 결과는 [validation.json](validation.json), 3초 동작 영상은 [blue-fire-preview.mp4](blue-fire-preview.mp4)에 저장했다.

검사 진입점은 `AcRoguelike.Liminal.Editor.BlueFireGateValidation.RunBatch`다. 그래픽 장치가 있는 별도 Unity batch editor에서 실행한다. 비동기 Play Mode 검사이므로 `-quit`와 `-nographics`는 사용하지 않는다.

검사는 실제 플레이어 이동, 미션 선택, 던전 진입과 세 차례 복귀, 효과의 정지·재개와 리소스 재사용을 확인한다. 플레이어의 강화·재화·발견 기록은 실행 전 저장하고 종료 시 복구한다. 촬영 이미지는 실제 Unity 렌더이며, 근접·전체 사진과 영상은 검토용 카메라를 사용한다. 플레이어 화면은 게임의 기본 카메라다.

![게이트 근접](blue-fire-gate.png)

![플레이어 카메라](blue-fire-player-view.png)

![로비 전체](blue-fire-overview.png)
