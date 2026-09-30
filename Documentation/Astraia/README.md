# Astraia 플레이어

`Stage_1` 브랜치의 `Assets/Liminal/Scenes/LiminalRun.unity`를 열고 Play로 실행합니다.
Room Gallery / Prop Gallery에도 같은 캐릭터가 연결되어 있습니다.
독립 배치용 프리팹은 `Assets/Characters/Astraia/AstraiaPlayer.prefab`입니다.

## 조작

| 동작 | 키보드·마우스 | 게임패드 |
| --- | --- | --- |
| 이동·달리기 | WASD | 왼쪽 스틱 |
| 걷기 | Ctrl + 이동 | LT + 이동 / 스틱 약하게 |
| 조준 | 마우스 위치 | 오른쪽 스틱 |
| 부적 3연타 | 좌클릭 또는 J, 유지 가능 | RB 또는 RT |
| 대시·공격 취소 | Space 또는 Shift | B / 동쪽 버튼 또는 LB |
| 카메라 확대 | 마우스 휠 | — |

이동 방향으로 회전하고, 공격할 때 조준 방향의 전방 적을 잠급니다. 발사 시점에 표적 생존·거리·벽 가림을 다시 검사합니다.
표적이 없으면 동작만 재생됩니다. 이동 속도는 걷기 2.1m/s, 달리기 4.2m/s, 대시는 0.24초 동안 3m이며 벽에서 멈춥니다.

## 구성

- 원본 `Astraia.fbx`는 팔 본만 갖고 있었습니다. 최적화 모델에 골반·척추·머리·팔·다리·발을 포함한 22개 Humanoid 본을 생성했습니다.
- 10개 메시를 스키닝하고 유효한 Humanoid Avatar를 생성했습니다. A포즈 가중치에서 T포즈 Avatar를 구성하므로 원본 메시 좌표와 바인드 포즈가 일치합니다.
- `Human Basic Motions FREE Unity`의 여성 Idle / Walk / Run을 Speed 블렌드 트리에 사용합니다. 여성 Sprint를 대시 포즈의 바탕으로 사용합니다.
- 공격은 전용 Humanoid 근육 곡선으로 제작한 오른손 → 왼손 → 양손 시전입니다. 타격 시점은 각각 0.18 / 0.20 / 0.27초, 전체 길이는 0.52 / 0.56 / 0.70초입니다.
- 공격 입력 버퍼, 3연타, 이동 제한, 대시 취소와 기존 부적·도깨비불 피해 및 시전 간격 증강을 연결했습니다.
- CharacterController가 실제 이동을 담당하고 애니메이션의 루트 이동은 끕니다.
- 기존 `LiminalExplorer.prefab` GUID를 유지해 이미 배치된 맵 플레이어도 새 외형을 받습니다. 새 맵 생성은 `AstraiaPlayer.prefab`을 우선 사용합니다.

## 원본과 현재 표현 범위

사용자가 제공한 원본 `C:/Users/user/Downloads/Astraia.fbx`는 변경하지 않았습니다. 프로젝트에는 플레이용 `Model/AstraiaOptimized.fbx`만 포함합니다.
원본 약 412만 면을 부위별로 단순화했으며 Unity 임포트 후 삼각형은 146,133개입니다. 원본의 팔 변형과 잘못된 중첩 스케일을 먼저 구운 뒤 최적화했습니다.

다운로드에 있던 Astraia 얼굴 원본의 컬러·노멀 텍스처를 복원했습니다. 의상·머리·피부 원본 텍스처는 FBX에 포함되지 않아, 사용자 요청에 따라 현재 자료로 남색 의상·은색 머리·금색 장식의 임시 재질을 적용했습니다.
자동 가중치 기반 전신 리그이며 손가락 본·표정 본·머리카락/치마 물리 시뮬레이션은 없습니다. 머리카락은 Head를 따라가고 의상은 스키닝으로 움직입니다. 세밀한 근접 촬영용 리깅은 별도 보정 대상입니다.

## 검증

Unity 6000.3.19f1에서 스크립트 컴파일 및 실제 Play Mode 10개 검사를 통과했습니다.

- 구성·접지, 걷기/달리기 실측 속도, 감속 후 정지
- 대시 거리 3m, 벽 앞 충돌 종료
- 정확히 3타·3발 시전 및 표적 피해
- 타격 전 대시 취소 시 발사 0회
- 위치 초기화 후 지연 공격/대시 없음

`play-validation.json`에 실행 결과, `rig-validation.json`에 Avatar·가중치·모션 샘플 결과, `mesh-optimization.json`에 부위별 단순화 수치가 있습니다. `Previews`는 실제 Unity 렌더입니다.
최종 플레이 검증 구간에 Console 오류·경고가 없었습니다. 이 검사는 에디터 실행 검증이며 별도 플랫폼 빌드 성능 검증은 아닙니다.

## 재생성·조정

Unity 메뉴 `Tools > Pirate Alliance > Astraia > Install Playable Character`로 생성 에셋을 다시 만들 수 있습니다. 이 메뉴는 생성한 리그·모션·재질·플레이어 프리팹의 수동 변경을 덮어씁니다. 원본 FBX는 건드리지 않습니다.
`Rebuild Humanoid Visual`은 외형/Avatar만 다시 생성하므로 전체 플레이어 구성을 복원하려면 Install 메뉴를 사용합니다.

이동·대시는 `PlayerMotor`, 공격/타격 시점은 `PlayerCombat`, 캐릭터 모션 제작은 `AstraiaAnimationBuilder`에서 조정합니다.
선택 추출한 Human Basic Motions 5개 FBX와 저자 PDF는 `Assets/ThirdParty/HumanBasicMotions` 아래에 있습니다. 저자: Kevin Iglesias. 다른 공격 에셋은 추가하지 않았습니다.

Blender에서 원본 최적화를 다시 수행할 경우 `Tools/Astraia/optimize_model.py`에 `--source`, `--output`, `--report`를 명시합니다. 최적화 출력 경로는 원본과 다른 파일로 지정합니다.
