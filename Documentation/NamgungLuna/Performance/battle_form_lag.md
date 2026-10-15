# 남궁루나 플레이 렉 분석 (stage_capture, 2026-10-15)

Unity 없이 정적 분석(프리팹 YAML, `.meta`, FBX 바이너리 직접 파싱)으로 조사했습니다. 수치는 아래 명령으로 재현할 수 있습니다.
FBX는 Git LFS이므로 `git lfs pull --include=<경로>` 후 파싱합니다.

## 결론 요약

| # | 원인 | 근거 | 조치 |
|---|---|---|---|
| 1 | **전시 모델 폴리곤 과다** | `Models/NamgungLuna.fbx` **775,107 삼각형** / 아스트라이아 `AstraiaOptimized.fbx` **146,133** (5.3배) / 프로이 97,113 / 평시 25,532 | **249,020으로 감량 (C절)** |
| 2 | 모든 렌더러가 그림자 패스에 참여 | 전시 프리팹 렌더러 38개 전부 `m_CastShadows: 1`. `PC_RPAsset` 캐스케이드 4개, 4096, 150 m. `PC_Renderer` Forward+ + SSAO(DepthNormals) → 775k 삼각형이 프레임당 여러 번 그려짐 | **안쪽·작은 파츠 20개 그림자 끔 (감량 전 227,780 / 감량 후 69,042 삼각형)** |
| 3 | Magica Cloth 9개, 렌더 정점 약 36.8만 개를 매 프레임 갱신 | `Magica/MagicaSetupReport.txt` (머리 4개 162,906 / 치마 2개 85,222 / 소매 102,746 / 리본 17,653) | **감량으로 약 14.6만 개 (C절)** |
| 4 | `LunaClothFollower`가 금장식 약 2.5만 정점을 매 프레임 관리 코드로 변형 | `Gold_Hem_Charms_R` 11,874 + `_L` 9,164 + `Gold_Hem_Trim` 3,820 컨트롤 포인트. 정점마다 앵커 4개 × 쿼터니언 회전 2회 + 행렬 변환을 `Vector3`/`Quaternion` 연산자로 계산 | **같은 결과를 내는 행렬 블렌드로 교체** |
| 5 | Magica PreBuild 미사용 → 첫 게이트 진입 때 9개 클로스를 런타임 빌드 | 전시 프리팹 `preBuildData.enabled: 0` (9개 모두). 프로이 `Proi_General_Magica.prefab`은 `enabled: 1` + `Magica/PreBuild/*.asset` | 권장 사항 (Unity 에디터에서 생성 필요) |
| 6 | 런 시작 때 숨겨진 바디의 컴포넌트가 깨어남 | `HunterRoster.Spawn`이 활성 프리팹을 `Instantiate`한 뒤 `SetActive(false)` → 그 사이 `Awake`/`OnEnable` 실행. 루나는 `LunaClothFollower.Awake`(격자 탐색·정렬·메시 복제)와 Magica 9개가 이때 돌아감 | 공용 코드라 미변경 → 권장 사항 (한 번만 생기는 끊김) |

### 확인했지만 문제가 아닌 것
- `LunaMoonFx`: 물결은 풀링(`ripples` 재사용), 반짝임 `maxParticles 400`, 트레일 1개, 플래시 라이트 `LightShadows.None`이며 꺼져 있다가 대시 때만 켜짐. 매 프레임 할당 없음. `fxRoot`는 `OnDestroy`에서 파괴.
- `LunaMoonGait`/`LunaStageMotionSource`/`LunaOcclusionFade`: 매 프레임 `Find*`, `GetComponent*`, `new Material`, `Instantiate`, GC 할당 없음.
- 폼 전환(평시 ↔ 전시): 바디는 런 시작에 한 번만 생성되고 `SetActive`로만 바뀜. 누적되는 오브젝트 없음.
- 랜턴(`UpdateLantern`): 상수 시간 계산, 텔레포트·큰 dt에서 초기화.
- 텍스처: LFS 원본, 대부분 `maxTextureSize 2048`. 렉 원인 아님.

## 적용한 수정

### A. 그림자 패스 정리 (화질 변화 최소)
`LunaStageSetup.BattleShadowlessParts`의 렌더러는 그림자를 **드리우지 않습니다**(렌더링·그림자 받기는 그대로).
`Install Battle Movement`가 이 목록을 적용하고, `Stage/NamgungLuna_Battle.prefab`에도 같은 값을 반영했습니다.

| 파츠 | 삼각형 | 이유 |
|---|---:|---|
| `Hair_BackFill_Lock_01..07` | 101,348 | 뒷머리(`Hair_Back_L/R`) 안쪽을 채우는 다발, 실루엣은 뒷머리가 만듦 |
| `Gold_Hem_Charms_L/R`, `Gold_Hem_Trim` | 48,848 | 치맛단의 얇은 장식 |
| `Chest_Crescent`, `Chest_Star`, `Waist_Navy_Inset` | 39,260 | 몸통 실루엣 안쪽 |
| `Boot_Shaft_Underlap_L/R` | 23,040 | 부츠 안쪽 겹침 |
| `Puff_Sleeve_Join_L/R`, `Neckline_Navy_Facing_L/R` | 14,564 | 연결부·안단 |
| `Lantern_Emissive_Core` | 720 | 발광 코어 (그림자가 오히려 부자연스러움) |

**화질 영향**: 바닥 그림자 윤곽은 치마·뒷머리·소매가 만들므로 거의 같습니다. 가슴 장식이 몸에 드리우던 아주 작은 자기 그림자는 사라질 수 있습니다.
되돌리려면 목록에서 이름을 빼고 `Tools > Namgung Luna > Stage Capture > Install Battle Movement`를 다시 실행합니다.

### B. `LunaClothFollower.Follow` 연산 축소 (화질 변화 없음)
- 앵커마다 `targetToFollower * [R | t]`를 3x4 행렬로 한 번만 만들고, 정점은 가중치로 행렬 하나를 섞어 한 번 곱합니다.
  가중치 합이 1이므로 `Σw·T(R·p + t) = (Σw·T[R|t])·p`로 수학적으로 같습니다.
- 정점당 쿼터니언 회전 8회 + 행렬 변환 2회 → 곱셈 약 70회의 float 연산으로 줄었습니다. 안쪽 루프에서 `Vector3`/`Quaternion` 연산자 호출이 없습니다.
- 검증: 무작위 2만 정점·50 앵커·비등방 스케일 변환으로 기존 식과 비교, 위치 오차 최대 `6.08E-007`, 노멀 오차 최대 `2.40E-006` (float 반올림 수준).
- Profiler 마커 `LunaClothFollower.Follow` 추가 (Magica 콜백 안에서 돌아서 일반 프로파일에서 묻히던 비용이 바로 보임).

### C. 전시 모델 감량 775,107 → 249,020 삼각형 (-67.9%)
목표는 프로이(97k)·아스트라이아(146k)까지 내리지 않는 **25만 이하**입니다. (중간 단계로 35만 버전도 만들었으며, 같은 스크립트에서 `TARGETS`만 다릅니다.)
- 스크립트: `Tools/NamgungLuna/optimize_battle_model.py` (bpy 5.2.2 + meshoptimizer). 결과 `battle_model_decimation.json`.
- 원본 보존: `Assets/Characters/NamgungLuna/Models/Source~/NamgungLuna_Full.fbx` (`~`로 끝나는 폴더는 Unity가 가져오지 않음, LFS).
- `Models/NamgungLuna.fbx`를 **같은 GUID·같은 .meta로 덮어씀**. 내보내기 설정을 원본과 맞춰(FBX 7400, Y up, UnitScaleFactor 1 + 루트 100배)
  감량 없이 왕복했을 때 전역 설정·메시 35개의 이름·메시 데이터 이름·계층·변환·정점 범위·레이어·재질·텍스처 상대경로 **차이 0**을 확인했습니다.
  그래서 Unity 메시 fileID가 그대로이고 기존 프리팹(Toon / Magica / Battle)이 가벼운 메시를 바로 씁니다.
- 얼굴(`Face_Head` 14,502)·소매 밴드·가슴 별·안단·랜턴 코어는 그대로 두고 30개 파츠를 줄였습니다.
- **UV·노멀 보존**: 처음 Blender Decimate(collapse)를 시험했더니 아틀라스 텍스처를 쓰는 머리·겉치마·랜턴의 UV가 섞였습니다.
  그래서 Unity처럼 코너별(위치+UV+노멀) 렌더 정점으로 나눈 뒤 meshoptimizer로 줄였습니다. 기존 정점만 남기고 UV 이음새는 짝지어 접기 때문에
  - 감량본의 모든 코너 (위치, UV)가 원본에 그대로 존재 (누락 0),
  - 서로 다른 UV 섬을 잇는 삼각형 **0개** (전체 파츠),
  - 남은 코너의 노멀은 원본의 커스텀 노멀 값 그대로입니다.
- 머리(`Hair_Back_L/R`, `Hair_Front`)는 더 줄이면 얇은 가닥 끝이 최대 15 mm 짧아져서 35만 버전 개수(20,000 / 19,000 / 12,000)를 유지했습니다.
  대신 무늬 없는 소매·속치마를 더 줄였습니다(편차 1 mm 미만). 랜턴은 UV 섬이 잘게 나뉘어 이음새 보존 한계(약 16k)에서 멈춥니다.
- 비교 렌더(왼쪽 원본, 오른쪽 감량본, Cycles): `decimation_front.png`, `decimation_head.png`, `decimation_hairback.png`, `decimation_hem.png`.

| 파츠 | 원본 | 감량 | 평균 편차 mm | p95 mm | 최대 mm |
|---|---:|---:|---:|---:|---:|
| `BellSleeve_L` | 94,464 | 12,000 | 0.122 | 0.333 | 0.933 |
| `BellSleeve_R` | 94,464 | 12,000 | 0.123 | 0.334 | 0.682 |
| `Bodice_Navy` | 24,796 | 8,467 | 0.606 | 2.043 | 4.204 |
| `Body_VisibleSkin` | 14,785 | 7,903 | 0.106 | 0.434 | 0.809 |
| `Boot_L` | 12,057 | 5,811 | 0.103 | 0.394 | 0.839 |
| `Boot_R` | 12,057 | 5,812 | 0.104 | 0.4 | 0.839 |
| `Boot_Shaft_Underlap_L` | 11,520 | 2,500 | 0.003 | 0.016 | 0.048 |
| `Boot_Shaft_Underlap_R` | 11,520 | 2,500 | 0.003 | 0.017 | 0.052 |
| `Chest_Crescent` | 20,348 | 3,490 | 0.003 | 0.014 | 0.069 |
| `Collar_Ribbons_Ivory` | 16,442 | 5,640 | 0.398 | 1.403 | 2.864 |
| `Floating_FullMoon` | 8,376 | 3,000 | 1.02 | 2.593 | 4.121 |
| `Gold_Hem_Charms_L` | 18,272 | 6,507 | 0.006 | 0.021 | 0.237 |
| `Gold_Hem_Charms_R` | 23,684 | 8,909 | 0.004 | 0.017 | 0.227 |
| `Gold_Hem_Trim` | 6,892 | 4,500 | 0.064 | 0.325 | 0.622 |
| `Hair_BackFill_Lock_01` | 18,428 | 4,000 | 0.035 | 0.094 | 0.187 |
| `Hair_BackFill_Lock_02` | 18,428 | 4,000 | 0.037 | 0.102 | 0.203 |
| `Hair_BackFill_Lock_03` | 18,428 | 4,000 | 0.034 | 0.09 | 0.205 |
| `Hair_BackFill_Lock_04` | 11,516 | 3,000 | 0.025 | 0.07 | 0.131 |
| `Hair_BackFill_Lock_05` | 11,516 | 3,000 | 0.024 | 0.07 | 0.137 |
| `Hair_BackFill_Lock_06` | 11,516 | 3,000 | 0.024 | 0.067 | 0.134 |
| `Hair_BackFill_Lock_07` | 11,516 | 3,000 | 0.023 | 0.065 | 0.117 |
| `Hair_Back_L` | 38,958 | 20,000 | 0.063 | 0.351 | 3.07 |
| `Hair_Back_R` | 37,024 | 19,000 | 0.07 | 0.387 | 3.039 |
| `Hair_Front` | 18,195 | 12,000 | 0.03 | 0.19 | 5.256 |
| `Lantern_Frame` | 28,850 | 16,036 | 0.32 | 1.529 | 6.831 |
| `Puff_Sleeve_Join_L` | 5,612 | 3,000 | 0.015 | 0.064 | 0.133 |
| `Puff_Sleeve_Join_R` | 5,320 | 3,000 | 0.014 | 0.062 | 0.151 |
| `Skirt_Ivory` | 92,928 | 12,000 | 0.079 | 0.233 | 0.636 |
| `Skirt_Navy` | 30,213 | 16,591 | 0.467 | 2.028 | 6.756 |
| `Waist_Navy_Inset` | 16,128 | 3,500 | 0.101 | 0.344 | 1.2 |

편차는 원본 정점에서 감량 표면까지의 거리입니다. 최대값이 큰 곳은 겉치마 접힘 가장자리, 랜턴 외곽 장식 끝, 머리카락 끝의 1~5 mm 부스러기입니다.
평균 편차가 가장 큰 것은 지름 1 m가 넘는 보름달(1.02 mm)입니다.

Magica 클로스 렌더 정점(코너 분할 기준 추정): 머리 4개 163,009 → 87,820, 치마 2개 84,847 → 32,673, 소매 99,482 → 17,018, 리본 17,656 → 8,866. **합계 364,994 → 146,377 (-60%)**.

#### 감량 후 Unity에서 할 일
1. Unity가 `NamgungLuna.fbx`를 다시 가져오면 Toon / Magica / Battle 프리팹이 가벼운 메시를 씁니다.
2. 하체 맞춤·부츠 스킨 메시·Magica 페인트를 새 메시에 맞추려면 `Tools > Namgung Luna > Setup Magica Cloth` → `Tools > Namgung Luna > Stage Capture > Install Battle Movement` 순서로 다시 실행합니다.
   부츠(`Boot_L/R`)는 설정 도구가 만든 스킨 메시라서 이 재실행 뒤에 감량이 반영됩니다. 페인트는 위치 기반(`Magica/selection_*.json`)이라 그대로 맞습니다.
3. 원본으로 되돌리려면 `Source~/NamgungLuna_Full.fbx`를 `Models/NamgungLuna.fbx`로 복사합니다(.meta는 건드리지 않음).

## 권장 사항 (미적용: Unity 에디터 필요)
1. **Magica PreBuild**: `Proi/Editor/ProiMagicaSetup.cs`의 `PreBuildDataCreation.CreatePreBuildData` 흐름을 `LunaMagicaSetup`에 적용하면 첫 게이트 진입 때 런타임 빌드 끊김이 사라집니다.
2. **숨긴 바디 늦게 깨우기**: `HunterRoster.Spawn`에서 비활성 임시 부모 아래로 `Instantiate`한 뒤 옮기면 착용 전까지 `Awake`가 돌지 않습니다(주석의 의도). 모든 헌터에 영향이 있어 이번에는 바꾸지 않았습니다.

## Unity에서 확인할 것
- **Profiler (CPU)**: `PostLateUpdate` 아래 `MagicaManager` 계열 항목과 `LunaClothFollower.Follow` 마커. 수정 전후 ms 비교.
- **Profiler (Rendering) / Frame Debugger**: 루나 vs 아스트라이아의 `Triangles`, `Shadow Casters`, `MainLightShadow`(캐스케이드별), `DepthNormalPrepass`, `DrawOpaqueObjects`.
- **첫 게이트 진입 스파이크**: Timeline 뷰에서 Magica 빌드 작업. PreBuild 적용 여부 판단용.
- **검증 메뉴**: `Tools > Namgung Luna > Stage Capture > Validate Both Forms` (결과 `Documentation/NamgungLuna/Validation/`),
  Test Runner PlayMode `NamgungLuna.Magica.Tests`(치마 관통·겹침), `Motion/Tests`(이동 모션).
- 금장식이 치마를 그대로 따라가는지 대시·급회전에서 눈으로 확인.
- 감량 후: Console에 FBX 재임포트 경고가 없는지, Battle 프리팹에서 Missing Mesh가 없는지, 머리·겉치마·랜턴 텍스처가 원래대로 보이는지,
  `Setup Magica Cloth` 보고서(`Magica/MagicaSetupReport.txt`)의 클로스별 정점 수가 위 추정치와 비슷한지 확인.
