# 남궁루나 (NamgungLuna)

URP Toon 캐릭터 + Magica Cloth 2 옷·머리 물리 설정.

## stage_capture 통합 (2026-10-15)

- `Assets/Liminal/Scenes/LiminalRun.unity`를 재생하고 로비 편성에서 **남궁루나**를 선택합니다. 선택 목록과 **F** 캐릭터 교체에는 남궁루나 한 명만 포함됩니다. 로비에서는 평시 모습으로, 게이트에 들어가면 전시 모습으로 자동 전환하고 로비로 돌아오면 다시 평시가 됩니다.
- 로비 평시는 이동 전용이고, 던전 전시는 아래 **달 투사체 전투**를 사용합니다. WASD / 게임패드 왼쪽 스틱으로 이동하고 Ctrl로 천천히 걷습니다. 기존 캐릭터로 교체하면 원래 기능이 돌아옵니다.
- **평시·전시 모두 게임 대시(Space / 왼쪽 Shift)를 씁니다**(프로필 `keepDash`). 공용 `PlayerMotor`의 대시(13 m/s × 0.19 s)로 실제 이동합니다. 평시는 양손을 모은 채 낮게 기울여 빠르게 내딛고 짧게 착지한 뒤 걷기로 돌아갑니다. 전시는 콜롬비나의 도약 모션으로 0.3초 동안 떠서 날았다가 착지 물결과 함께 내려옵니다(`LunaMoonGait.dashLeapSeconds`). 던전은 기존 스태미나 3칸 규칙, 로비는 기존 무료 대시 규칙을 따릅니다.
- 평시는 과거 `남궁루나_일상.png` 일러스트에서 Meshy로 모델과 리그를 만들고, 새로 생성한 단정한 걷기를 제자리 순환 모션으로 편집했습니다. 대기 자세도 포함합니다. `Peaceful/`에 모델·모션 원본, `Stage/`에 게임용 프리팹·클립·컨트롤러가 있습니다.
- 평시 기본 이동은 0.65 m/s, Ctrl 걷기는 0.4 m/s입니다. 공용 모터의 정규화 속도에 맞춰 걷기 재생률도 함께 낮춥니다. 전시는 기존 부유 이동 속도 4.2 / 2.1 m/s를 사용합니다.
- 실제 평시 프리팹은 `LunaPeaceful_Game.fbx`와 `LunaPeacefulIdle/Walk/Dash_Game.fbx`를 사용합니다. 자동 리깅의 얼굴·베일·치마와 신발을 보정한 뒤, 팔이 몸통·치마에 묶여 있던 부분을 별도 팔·팔뚝·손 제어로 분리했습니다. 기존 손을 유지하고 소매를 보강해, 대기·걷기·대쉬에서 배 앞에 양손을 모읍니다. 치마 안쪽에는 스타킹 형태의 허벅지·정강이를 추가해 발걸음에 맞춰 무릎이 굽혀집니다.
- 손 모은 동작과 다리 보강은 `Tools/NamgungLuna/build_peaceful_prayer.py`를 Blender 배경 모드에서 실행해 재현합니다. 입력은 `Tools/NamgungLuna/Source/PeacefulBeforePrayer/`의 보존 원본이며, 결과는 `Temp/NamgungLuna/Prayer/`에 생성됩니다. 모델은 `Peaceful/Models/`, 세 모션은 `Peaceful/Animations/`로 옮긴 뒤 `Install Peaceful Movement`를 실행합니다. 새 소매·스타킹 재질에는 원본 의상 아틀라스를 적용하지 않습니다.
- 평시 대쉬 클립은 약 0.33초의 한 번 재생 동작이며, 실제 이동이 끝난 뒤 짧게 자세를 회복하고 걷기로 복귀합니다. `Validate Peaceful Poses`는 별도 미리보기 씬에서 팔·다리 가중치, 손 간격, 클립 반복 설정과 여러 시점의 메시를 검사합니다. 모델·포즈 검증 화면과 보고서는 `Documentation/NamgungLuna/PeacefulPrayerQA/`에 저장됩니다.
- 전시는 `feature/namgungluna-character`의 `a088201` 자산과 GUID를 유지하여 가져왔습니다. `Stage/NamgungLuna_Battle.prefab`은 기존 부유 모션을 게임의 공용 이동 모터에 연결합니다.
- 선택용 프로필은 `Assets/Liminal/Resources/Hunters/NamgungLuna.asset` 하나이며 ID는 `namgung_luna`입니다. 평시 프로필은 `Stage/Profiles/NamgungLunaPeaceful.asset`에 두고 메인의 `lobbyForm`으로 연결합니다. 평시 내부 ID `namgung_luna_peace`는 유지하며, 이전 평시·전시 선택 저장값은 `previousCharacterIds`를 통해 남궁루나로 이어집니다.
- 기존 두 프로필을 정리할 때는 `Tools > Namgung Luna > Stage Capture > Install Unified Hunter`를 사용합니다. 기존 파일을 이동해 GUID를 유지하고 모델·모션은 다시 생성하지 않습니다. `Install Battle Movement`와 `Install Peaceful Movement`를 다시 실행해도 같은 단일 선택 프로필과 평시 연결을 갱신합니다.
- 실제 플레이 검증은 같은 메뉴의 `Validate Both Forms`를 사용합니다. 검증 결과와 화면은 `Documentation/NamgungLuna/Validation/`에 저장됩니다. 기존 선택 캐릭터와 편집 씬은 검증 종료 후 복원됩니다.
- 생성 이력은 `Documentation/NamgungLuna/peaceful_*_meshy.json`과 `peaceful_walk_retarget.json`에 기록했습니다. 모델·리깅·걷기·리타깃 생성에 Meshy 48크레딧을 사용했습니다. `Tools/NamgungLuna/`의 요청 스크립트는 저장된 작업 ID를 재사용하며 API 키와 다운로드 서명 URL은 문서에 저장하지 않습니다.
- 성능(렉) 분석: `Documentation/NamgungLuna/Performance/battle_form_lag.md`. 전시 모델 `Models/NamgungLuna.fbx`는 775,107 → 249,020 삼각형으로 감량했습니다(UV·노멀 보존, `Tools/NamgungLuna/optimize_battle_model.py`, 원본은 `Models/Source~/NamgungLuna_Full.fbx`). 감량 뒤에는 `Setup Magica Cloth` → `Install Battle Movement`를 다시 실행하세요. 안쪽·작은 파츠 20개(`LunaStageSetup.BattleShadowlessParts`)는 그림자를 드리우지 않고, `LunaClothFollower`는 같은 결과를 내는 행렬 블렌드로 계산합니다. Profiler 마커는 `LunaClothFollower.Follow`입니다.
- 아래의 대시 수치(0.3초 · 3 m)와 자동 시연은 전시 **단독 데모 씬**(`LunaMoonMotor`) 기준입니다. 게임 안에서는 위의 공용 대시를 따릅니다.

## 달 투사체 전투 (2026-10-16)

- **좌클릭 / J / 게임패드 RB·RT**: 초승달 → 반달 → 보름달 순서로 발사합니다. 보름달은 적중 또는 최대 사거리 도달 시 주변에 폭발 피해를 줍니다. 공격 애니메이션 없이 발사 타이밍과 이펙트를 연결한 상태입니다.
- **E / 오른쪽 스틱 클릭**: 8초 달빛 영역. 등 뒤 달이 보름달로 바뀌고, 기본공격에 맞은 영역 내 적을 우선 추적합니다. Q의 기존 지원 스킬 입력과 Y의 캐릭터 교체 입력은 변경하지 않습니다.
- **R / 게임패드 X**: 10초 달의 힘 해방. 이동·대시는 유지하고 기본공격을 전용 보름달 투사체로 고정하며 발사 속도를 2배로 올립니다. 궁극기 사용 중에만 실제 홍채 위치의 옅은 금백색 눈빛이 위로 번지고, 이동 시 뒤에 진주빛·연보라 잔상을 남깁니다. 사용 전의 이동·일반공격·E 스킬에서는 눈 잔상이 없으며, 궁극기가 종료되면 남은 눈 잔상도 즉시 제거합니다. 눈 효과에는 주황·붉은 불꽃색과 빠른 깜박임을 사용하지 않습니다.
- 새로 생성한 분화구 달 표면·보라색 연무·황금 불꽃 텍스처에 두께가 있는 달 메시, 유기적인 파티클과 파편을 겹쳤습니다. 개인 색상 크림 `#FAEFE3`, 연보라 `#BDC8ED`, 짙은 남보라 `#40446E`를 바탕으로 각성은 금빛을 강조합니다. 전역 URP 설정 변경 없이 캐릭터 전용 재질을 씁니다.
- `Combat/Prefabs/`에 3종 평타·각성 투사체, 3종 폭발, 달빛 영역, 두 시전 이펙트가 있습니다. `Combat/LunaCombatFxLibrary.asset`과 전시 프리팹에 연결됩니다. `Tools > Namgung Luna > Combat > Install Lunar Effects and Projectiles`로 재생성할 수 있습니다. 기존 전시·통합 설치도 연결을 유지합니다.
- 기획서에 확정된 지속시간(8초/10초), 공격 단계와 2배 공격속도를 적용했습니다. 피해량·사거리·반경·쿨다운은 문서 미정이므로 `LunaCombatController`의 공개 필드에 시험 수치를 두었습니다. 전용 증강·지원 드롭 효과는 이번 구현에 포함하지 않습니다.
- `customCombat` 프로필로 공용 근접 공격 대신 전시 전용 전투를 사용합니다. 로비의 전시 옷 미리보기에서도 공격을 차단하고, 사망·방 변경·캐릭터 교체 때 투사체와 이펙트를 정리합니다. 다른 캐릭터의 기존 Animator·콤보 설정을 유지합니다.
- 실제 게임 검증은 `Tools > Namgung Luna > Combat > Validate Combat` / `Validate Combat With Short Frame Capture`입니다. 결과·이미지·영상용 프레임은 `Documentation/NamgungLuna/Combat/Validation/`, 원문 분석과 이미지 생성 프롬프트는 `Documentation/NamgungLuna/Combat/`에 저장합니다.
- 눈빛의 정면 비교와 이동 잔상은 `Capture Golden Eye Preview`로 촬영합니다. 시전 연무가 사라진 뒤 실제 캐릭터를 24fps·48프레임으로 기록하고, 촬영에 사용한 시간·카메라·선택 설정을 복원합니다. `NamgungLuna_GoldenEyes.mp4`는 이 2초 시퀀스입니다. 최신 눈 전용 텍스처는 `Combat/Textures/LunaEyeAfterimage.png`, 생성 기록은 `Documentation/NamgungLuna/Combat/eye-afterimage-prompt.json`입니다.
- 2026-10-16 검증: 실제 전투 61개 검사 통과. 궁극기 사용 전의 이동·평타·E와 외부 이펙트 활성화 요청에서는 눈 입자·렌더러가 모두 0이며, 궁극기 중 활성 및 종료 후 완전 제거를 확인했습니다. 궁극기 10.02초, 평타 간격 0.55→0.275초를 확인했습니다. 앞선 로비·던전 형태 전환 및 이동·대시 136개 검사와 눈 잔상 스타일의 별도 48프레임 시연도 통과했습니다. 종료 후 편집 씬은 저장하지 않고 원래 상태로 복원됩니다.

## 필요한 것
- **Magica Cloth 2** (유료 에셋, 저장소에 포함하지 않음). 각자 Asset Store 계정으로
  Package Manager > My Assets > Magica Cloth 2 > Download > Import 하세요. Example 폴더는 없어도 됩니다.
- Magica를 가져오면 `MAGICACLOTH2` 심볼이 자동으로 추가되고, 그때부터 아래 Magica 관련 코드가 컴파일됩니다.
  Magica가 없으면 해당 코드는 건너뛰며(빌드 오류 없음), 프리팹의 Magica 컴포넌트만 Missing으로 보입니다.

## 평시 의상 물리

- 게임에서 사용하는 `Stage/NamgungLuna_Peaceful.prefab`에 Magica Cloth 2 MeshCloth를 적용했습니다. 치마, 베일, 목 리본, 왼쪽·오른쪽 소매 끝의 5개 그룹으로 나뉩니다. 얼굴·손·신발·상체는 원래 애니메이션을 유지합니다.
- 허리와 베일 윗부분, 손목 가까운 소매를 고정하고 끝단으로 갈수록 움직임을 허용합니다. 원본에서 붙어 있는 남색 겉치마와 흰색 안쪽 천은 한 시뮬레이션으로 처리해 공통 경계가 갈라지지 않게 했습니다. 몸통·머리·목·골반·양쪽 다리·손목에는 총 10개의 충돌체가 연결되어 있습니다.
- 치마 안쪽에 연결되어 있던 부츠·정강이 복원 잔재 436개 삼각형은 고정 메시로 분리했습니다. 외부 치마와 허리 양옆의 작은 천은 유지합니다. 치마에는 안쪽으로 접히는 범위를 제한하는 Backstop도 적용했습니다. 시뮬레이션 최대 이동 설정은 치마 14cm, 베일 6cm, 리본 2.8cm, 소매 끝 5mm입니다. 치마는 원래 주름과 물리를 50%씩 섞어 두꺼운 의상의 형태를 유지하며, 실제 렌더 메시 변위는 별도로 검증합니다.
- 원본 FBX를 바꾸지 않고 스킨 가중치·바인드 포즈·UV·재질을 보존한 별도 메시를 `Peaceful/Cloth/`에 생성합니다. `PreBuild/`의 계산 데이터를 사용하므로 캐릭터를 생성할 때 메시 분석을 다시 할 필요가 없습니다. 비활성화·재등장과 큰 순간이동에는 천 상태를 초기화합니다.
- 재설치: `Tools > Namgung Luna > Stage Capture > Install Peaceful Cloth`. 기존 `Install Peaceful Movement`도 같은 의상 설정을 보존해 다시 생성합니다. 원본 모델의 해시가 달라지면 낡은 페인트를 적용하지 않고 중단합니다.
- 영역 데이터는 `Tools/NamgungLuna/peaceful_cloth_data.py`로 생성하며 `Documentation/NamgungLuna/PeacefulCloth/`에 고정 영역 이미지와 JSON이 있습니다. 물리 수치는 `Editor/LunaPeacefulClothSetup.cs`에서 관리합니다.
- 검증: `Validate Peaceful Cloth`는 임시 씬에서 실제 프리팹과 동일 포즈의 물리 없는 기준 메시를 비교합니다. 대기·걷기·대시·급회전·정지 후 회복·20m 순간이동·재활성화의 메시 변위, 충돌 악화, 잔떨림과 유한성을 기록합니다. 결과와 영상용 프레임은 `Documentation/NamgungLuna/PeacefulCloth/Validation/`에 저장하며, 종료 후 열려 있던 씬과 선택을 복원합니다.
- 2026-10-15 최종 검증: 60fps 1,083프레임과 30fps 543프레임에서 각각 312개 검사 통과. 보호용 충돌체 기준 관통 악화 최대값은 각각 2.70mm/8.25mm로 10mm 이내이며, 천 정점은 바닥 위를 유지했습니다. 실제 게임의 선택·이동·대시·평시/전시 전환 검사도 123개 통과했습니다. `cloth-validation-60fps.json`, `cloth-validation-30fps.json`과 16초 시연 영상 `NamgungLuna_Peaceful_Cloth.mp4`를 같은 검증 폴더에 보관합니다.

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
| `Motion/Scripts/` | `LunaMoonGait` (이동 모션), `LunaMoonMotor` (조작), `LunaMoonFx` (달빛 연출), `LunaOcclusionFade` (달 가림 처리), `LunaOrbitCamera`, `LunaMoonShowcase` (자동 시연) |
| `Motion/Shaders/`, `Motion/Materials/` | 달빛 물결 / 빛·반짝임·꼬리 셰이더와 재질, 데모 바닥 |
| `Motion/Tests/` | 이동 모션 Play 모드 검증, 영상 프레임 녹화(Explicit) |

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
| 소품 | 랜턴은 손잡이 기준 최대 14° 안에서 부드럽게 기울고 빠르게 안정됨. 급회전·대시에서 관성이 누적되지 않으며, 방 이동·재활성화 때 이력을 초기화. 등 뒤 보름달은 조금 늦게 따라오며 천천히 자전 |
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

## 클로스 구성
- 머리 4개(앞머리 / 뒷머리 좌 / 뒷머리 우 / 뒷머리 보강다발): 두피는 고정, 길이만 움직임. 머리·목·몸통·어깨 콜라이더.
- 치마 2개(속치마 / 겉치마): 겉치마→속치마 상호 충돌(Mutual Collision)로 서로 뚫지 않음. 골반·허벅지·정강이·발 콜라이더.
- 소매 2개(팔꿈치 고정, 팔뚝 콜라이더), 칼라 리본 1개.
- 고정/이동 영역은 Magica 페인트 데이터로 저장되어 인스펙터 페인트 도구로 수정할 수 있습니다.

## 주의
- 전시 원본은 스킨 웨이트·애니메이션 클립이 없는 정적 모델입니다. 몸 전체와 하체·부츠·다리 콜라이더·랜턴·달은 `LunaMoonGait`가 움직이고,
  클로스는 그 움직임과 바람에 반응합니다. 팔은 아직 고정 자세입니다(소매는 클로스로 흔들림).
  나중에 리깅하면 `Magica_Colliders` 아래 콜라이더를 해당 뼈 아래로 옮기세요.
- `NamgungLuna_MagicaDemo` 씬은 클로스 검증용이라 `LunaMoonGait`/`LunaMoonFx`가 꺼져 있습니다.
- Magica 제한: 클로스 1개의 렌더 정점 합 65,535 이하(넘으면 빌드가 멈춤), 렌더러 31개 이하.
  그래서 머리는 3개 메시로 나뉘어 있고 치마 금장식은 클로스가 아니라 `LunaClothFollower`로 따라갑니다.
- `Setup Magica Cloth`를 다시 실행하면 프리팹과 데모 씬을 새로 만듭니다(직접 바꾼 값은 덮어씀).
