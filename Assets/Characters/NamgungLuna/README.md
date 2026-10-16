# 남궁루나 (NamgungLuna)

URP Toon 캐릭터 + Magica Cloth 2 옷·머리 물리 설정.

## 필요한 것
- **Magica Cloth 2** (유료 에셋, 저장소에 포함하지 않음). 각자 Asset Store 계정으로
  Package Manager > My Assets > Magica Cloth 2 > Download > Import 하세요. Example 폴더는 없어도 됩니다.
- Magica를 가져오면 `MAGICACLOTH2` 심볼이 자동으로 추가되고, 그때부터 아래 Magica 관련 코드가 컴파일됩니다.
  Magica가 없으면 해당 코드는 건너뛰며(빌드 오류 없음), 프리팹의 Magica 컴포넌트만 Missing으로 보입니다.

## 파일
| 경로 | 내용 |
|---|---|
| `Magica/NamgungLuna_Magica.prefab` | 게임에 쓰는 프리팹 (모델 + Magica 콜라이더 + 클로스 9개) |
| `Magica/NamgungLuna_MagicaDemo.unity` | 데모 씬. Play 하면 캐릭터 전체가 걷기·방향 전환·회전, 바람 포함 |
| `Generated/Setup_*/Prefabs/NamgungLuna_Toon.prefab` | 물리 없는 Toon 프리팹 |
| `Models/NamgungLuna.fbx` | 모델 (파츠별 분리 메시, Read/Write 켜짐) |
| `Magica/Scripts/` | `LunaClothFollower` (치마 금장식이 치마를 따라감), `LunaMagicaDemoMotion` (데모용 이동) |
| `Editor/` | `Tools > Namgung Luna > Create Toon Prefabs from FBX`, `Tools > Namgung Luna > Setup Magica Cloth` |
| `Magica/*.json` | 고정/이동 페인트 점 데이터와 콜라이더 정의 (Setup Magica Cloth가 사용) |
| `Magica/Tests/` | Play 모드 검증 테스트 (관통·겹침·정지 유지), 영상 프레임 녹화(Explicit) |
| `Motion/NamgungLuna_MoonGaitDemo.unity` | **이동 모션 데모 씬** (콜롬비나식 이동). 아래 "이동 모션" 참고 |
| `Motion/Scripts/` | `LunaMoonGait` (이동 모션), `LunaMoonMotor` (조작), `LunaMoonFx` (달빛 연출), `LunaOcclusionFade` (달 가림 처리), `LunaOrbitCamera`, `LunaMoonShowcase` (자동 시연), `LunaArmRig` (팔 리그), `LunaBasicAttack` (기본공격 3타), `LunaLightOrb` (평타 빛 구체) |
| `Motion/Shaders/`, `Motion/Materials/` | 달빛 물결 / 빛·반짝임·꼬리 셰이더와 재질, 데모 바닥 |
| `Motion/Tests/` | 이동 모션·기본공격 Play 모드 검증, 영상 프레임 녹화(Explicit) |

## 이동 모션 (원신 콜롬비나식)
콜롬비나는 걷지 않고 **발끝으로 떠서** 움직입니다. 대기 중에는 발끝을 세운 채 몇 cm 떠 있고, 달릴 때는 발끝을 한 번 대면
달 표면처럼 길게 떠서 나아가며(앞다리는 뻗고 뒷다리는 무릎을 접은 채), 발끝이 닿은 자리마다 달빛 물결이 퍼집니다.
질주할 때는 몸을 크게 숙이고 뒤로 빛의 꼬리를 남깁니다. 남궁루나는 이 **질주 모션이 기본 이동**이고, 게임에 점프가 없으므로
콜롬비나의 점프(다리를 접고 떠올랐다가 뻗으며 내려오는 도약)를 **대쉬(회피)** 로 씁니다.
`LunaMoonGait`가 이것을 리깅 없는 모델에 절차적으로 입힙니다.

| 동작 | 구현 |
|---|---|
| 대기 | 발끝 세움(부츠 앞코가 아래로), 4.5 cm 부유 + 느린 위아래 흔들림, 발밑 은은한 빛 |
| 걷기 (Ctrl) | 짧고 가벼운 발끝 걸음, 낮은 부유 |
| 이동 (기본) | 질주 모션: 22° 앞으로 숙이고 0.56초마다 발끝을 대며 길고 낮게 떠서 나아감, 앞다리 뻗고 뒷다리 접음, 착지마다 달빛 물결, 빛 꼬리. 출발 때 작은 빛·반짝임. 속도는 게임 기본 4.2 m/s |
| 대쉬 (Space / 왼쪽 Shift) | 0.3초 동안 3 m. 몸이 최고 32 cm 떠올라 다리를 접고 앞으로 엎드리듯 날았다가 뻗으며 착지. 시작 때 큰 빛 폭발·반짝임, 착지 물결. 루트(충돌)는 바닥을 따라 이동 |
| 대쉬 규칙 | 게임 회피(`PlayerMotor`)와 같음: 스태미나 3칸, 칸당 0.85초 회복(마지막 대쉬 0.3초 뒤부터), 대쉬 사이 최소 0.3초 |
| 몸 | 회전 방향으로 기울기(뱅크), 가속·감속에 따라 앞뒤 기울기, 걸음마다 좌우 흔들림 |
| 소품 | 랜턴은 손잡이에 매달린 진자처럼 흔들리고, 등 뒤 보름달은 조금 늦게 따라오며 천천히 자전 |
| 옷·머리 | 치마가 골반 위에 걸치고, 다리(허벅지·정강이·발) 콜라이더가 치마를 안쪽에서 참. 이동 풍(movingWind)으로 머리와 치맛단이 뒤로 날림 |

- 구조: 프리팹 루트(이동) → `Visual`(부유·기울기) → 모델 / Magica 콜라이더·클로스. 랜턴(`LanternPivot`)과 달(`MoonPivot`)은 별도 피벗입니다.
- 하체: 원본 모델은 드레스 안에 몸이 없어서 치마가 기댈 곳이 없었습니다. `LowerBody`(스킨 메시, `Motion/Meshes/LunaLowerBody_Skinned`)가
  골반과 두 다리(허벅지·슬개골·종아리·발목 단면, 부츠 안감과 같은 짙은 스타킹 재질)를 채웁니다. 단면은 높이마다 속치마·허리 안쪽에
  8 mm 여유를 두고 맞춰 줄였기 때문에 서 있을 때 치마를 뚫지 않습니다(애니메이션 체형, 골반 폭 약 19 cm, 허벅지 지름 약 10.6 cm).
  골반은 `Visual`, 허벅지는 골반→`Hip_L/R`, 정강이는 `Knee_L/R`에 스킨되고, 부츠는 정강이·발(`Ankle`) 두 뼈에 스킨해 발끝을 세우면 발목에서 꺾입니다.
  Magica 콜라이더(`Hips`, `Leg`, `Shin`, `Foot`)는 이 하체 굵기에 맞춰 만들어집니다. 수치는 `LunaLocomotionSetup`의 `LegProfile` / `PelvisProfile`.
- 등 뒤의 보름달이 카메라와 캐릭터 사이를 가리면 `LunaOcclusionFade`가 디더로 투명하게 합니다(`LunaToonURP`의 `_Fade`).
- 조작(데모 씬): WASD 이동(카메라 기준) · Space / 왼쪽 Shift 대쉬 · Ctrl 걷기 전환 · 마우스 오른쪽 드래그/Q·E 카메라 회전 · 휠 거리.
  4초 동안 키를 누르지 않으면 `LunaMoonShowcase`가 모든 동작을 차례로 보여 줍니다.
- 다른 이동 스크립트와 함께 쓰기: `LunaMoonGait`는 같은 오브젝트의 `ILunaMotionSource`(속도·접지·질주·대쉬 진행도)를 읽고,
  없으면 루트의 움직임에서 속도를 계산합니다. 게임의 플레이어 모터에 `ILunaMotionSource`만 구현하면 그대로 붙습니다.
- 수치(박자·높이·기울기·다리 각도)는 `LunaMoonGait` 인스펙터의 Walk/Run/Sprint 프로필에서 조절합니다.

## 기본공격 (평타 3타)
랜턴은 **오른손**에 들려 있으므로 랜턴 안 든 손은 왼손입니다. 30fps 프레임 데이터(블로킹 v0.2)를 `LunaBasicAttack`이 재생합니다.

| 타 | 동작 | 길이 | 발사 | 다음 타 입력 / 연계 | 이동 캔슬 | 발사 위치 |
|---|---|---|---|---|---|---|
| 1타 | 왼손을 어깨 옆으로 당겼다가 앞으로 곧게 뻗어 발사. 앞발(오른발)을 들었다 f7에 디딤 | 20f | f8 | f6~ / f13 | f12 | 왼손 손바닥 |
| 2타 | 왼손을 오른쪽 가슴 앞까지 감았다가 바깥으로 휘둘러, 정면을 지날 때 발사 | 22f | f9 | f7~ / f15 | f14 | 왼손 손바닥 |
| 3타 | 앉으며 랜턴을 낮췄다가 오른무릎을 차올리며 랜턴을 머리 위로 들고, f14 디딤 → f15 발사 | 30f | f15 | — (1타로) | f21 | 랜턴 등불 |

- 조작(데모 씬): 왼쪽 마우스 / J. 발사 프레임 2프레임 전부터 다시 누르면 다음 타가 예약되어 연계 프레임에 이어집니다.
  예약이 없으면 그 타를 끝까지 재생하고, `comboResetTime`(0.6초) 안에 누르면 다음 타, 지나면 1타부터 시작합니다.
- 공격 중에는 이동 캔슬 프레임까지 멈춰 서고(이동 키를 누르고 있어도), 누르는 순간 이동 키 방향을 봅니다. 대쉬(회피)로 언제든 끊을 수 있습니다.
- 몸: 원본에 척추 뼈가 없어 상체 비틀기는 몸 전체(`Visual`) 회전으로, 체중 이동은 `Visual` 위치로, 디딤·무릎 차올리기는 다리 체인으로 합니다.
  골반·다리가 0.5~1프레임 먼저 움직이는 오버랩이 있습니다. `LunaMoonGait`의 `ILunaPoseOverlay`로 이동 모션 위에 덮어씁니다.
- 팔: `LunaArmRig`가 시작할 때 Visual 아래에 `Clavicle → UpperArm → ForeArm → Hand` 뼈를 만들고 `Body_VisibleSkin`, `Bodice_Navy`(퍼프 소매),
  `Puff_Sleeve_Join_L/R`을 스킨 메시로 바꿉니다(원본 렌더러는 꺼짐, FBX Read/Write 필요). 벨 소매·소매 띠(클로스)는 위팔의 `SleevePivot`에 매달려
  아래팔 회전을 70% 따라가고, `Collider_Forearm_L/R`은 아래팔을 따라갑니다. 랜턴 손잡이는 오른손을 따라갑니다.
  관절 위치는 FBX 정점 단면에서 잰 값이며 `LunaArmRig` 인스펙터에서 고칠 수 있습니다.
- 연출: 발사 프레임에 빛 구체(1·2타 달빛 소형, 3타 랜턴빛 대형)와 섬광·반짝임, 디딤 프레임에 달빛 물결, 예비동작 동안 손바닥·랜턴에 빛이 모입니다.
- 게임 연동: `NamgungLuna.Motion`은 게임 전투 코드(`PlayerCombat`, `TrainingEnemy`)를 참조할 수 없으므로 피해는 넣지 않았습니다.
  `LunaBasicAttack.AttackEvent`(Start / Release / End)와 `Hit`(구체가 맞힌 콜라이더)를 구독해 피해를 주면 됩니다.
- `LunaMoonShowcase` 자동 시연 끝에 3타 콤보가 추가되었습니다.

## 클로스 구성
- 머리 4개(앞머리 / 뒷머리 좌 / 뒷머리 우 / 뒷머리 보강다발): 두피는 고정, 길이만 움직임. 머리·목·몸통·어깨 콜라이더.
- 치마 2개(속치마 / 겉치마): 겉치마→속치마 상호 충돌(Mutual Collision)로 서로 뚫지 않음. 골반·허벅지·정강이·발 콜라이더.
- 소매 2개(팔꿈치 고정, 팔뚝 콜라이더), 칼라 리본 1개.
- 고정/이동 영역은 Magica 페인트 데이터로 저장되어 인스펙터 페인트 도구로 수정할 수 있습니다.

## 주의
- 원본은 스킨 웨이트·애니메이션 클립이 없는 정적 모델입니다. 몸 전체와 하체·부츠·다리 콜라이더·랜턴·달은 `LunaMoonGait`가 움직이고,
  클로스는 그 움직임과 바람에 반응합니다. 팔은 `LunaArmRig`가 실행 중에 절차적으로 리깅합니다(기본공격 때만 움직이고 평소에는 원래 자세).
  나중에 제대로 리깅하면 `LunaArmRig`를 빼고 `Magica_Colliders` 아래 콜라이더를 해당 뼈 아래로 옮기세요.
- `NamgungLuna_MagicaDemo` 씬은 클로스 검증용이라 `LunaMoonGait`/`LunaMoonFx`가 꺼져 있습니다.
- Magica 제한: 클로스 1개의 렌더 정점 합 65,535 이하(넘으면 빌드가 멈춤), 렌더러 31개 이하.
  그래서 머리는 3개 메시로 나뉘어 있고 치마 금장식은 클로스가 아니라 `LunaClothFollower`로 따라갑니다.
- `Setup Magica Cloth`를 다시 실행하면 프리팹과 데모 씬을 새로 만듭니다(직접 바꾼 값은 덮어씀).
