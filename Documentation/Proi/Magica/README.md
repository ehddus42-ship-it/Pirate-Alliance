# 프로이 머리카락 · 옷 물리 (Magica Cloth 2) + 이동 자세

프로이(Proi)의 트윈테일, 벨트에 매달린 찢어진 천 띠, 통 넓은 바지를 Magica Cloth 2 로 움직이고, 걷기 / 달리기 상체 자세와
궁극기 연출 중 머리 휘날림을 더했습니다. 게임에서 프로이로 교체(F)하면 `Resources/Hunters/Proi.asset` 이
`Assets/Characters/Proi/Magica/Proi_Magica.prefab` 을 생성합니다.

## 받아서 보는 법

1. **Magica Cloth 2 가 프로젝트에 있어야 합니다** (유료 에셋이라 저장소에는 없음, `.gitignore`).
   Package Manager > My Assets 에서 가져와 기본 위치 `Assets/MagicaCloth2` 에 두면 Magica 가 `MAGICACLOTH2` 심볼을 자동으로 켭니다.
   만든 버전: **2.18.4** (미리 구운 PreBuild 데이터를 쓰므로 2.18.x 권장).
   Magica 가 없으면 컴파일 오류 없이 예전 트윈테일 스프링으로 움직이고, 프리팹의 Magica 컴포넌트만 Missing Script 로 보입니다.
2. Git LFS 로 받습니다 (분할 메시 `Proi_MagicaMeshes.asset`, `PreBuild/*.asset`, 영상 / 이미지).
3. 게임 실행 → 게이트에서 **F** 로 프로이 교체. 이동 WASD, 걷기 **Ctrl** + 이동, 공격 좌클릭, 궁극기 **R**.
4. 영상: `proi_magica_motion.mp4` (변경 후) / `proi_before_tailspring.mp4` (이전). 앞 3/4 · 옆 · 뒤 3분할,
   대기 → 걷기 → 달리기 → 선회 → 3타 콤보 → 대쉬 → 정지 → 궁극기.

## 변경 이력

### 2026-10-14 — 머리카락 무게 · 진행 반대 날림 · 통바지 · 걷기 자세 · 궁극기 휘날림

| 요청 | 원인 | 변경 |
| --- | --- | --- |
| 트윈테일이 두상 근처만 많이 움직이고 그 뒤로는 둔탁함 / 같은 무게로 | LongHair 프리셋의 입자 속도 제한 3 m/s 가 끝부분을 붙잡고, 복원력·각도 제한이 뿌리→끝으로 달라짐 | 복원력 / 각도 제한을 뿌리부터 끝까지 같은 값으로, 속도 제한 5 m/s, 중력 절반을 늘 걸어 무게감 유지 |
| 걸을 때 머리카락이 진행 반대로 더 | 움직임으로 생기는 바람 없음 | Moving Wind 0.9 (이동 속도만큼 반대쪽 바람) |
| 바지를 통 큰 바지처럼 | 무릎 아래 일부만, 최대 3 cm 만 움직임 | 무릎 스트랩 아래 풍선 부분 전체(+ 그 위 스트랩, 끈 끝)가 최대 12 cm 흔들리고 부풀며 날림. 커프는 부츠에 고정 |
| 걷기 허리를 더 굽혀 자연스럽게 (원인 파악) | Run 클립: ① 허리가 펴진 채 엉덩이~머리가 한 덩어리로 18° 기움(머리도 숙임) ② **골반 / 어깨가 걸음과 거꾸로 돔** (왼발이 나갈 때 왼쪽 어깨가 앞으로) ③ 좌우 기울기 / 숙임 리듬 없음. Ctrl 걷기는 Idle(가드 자세)과 Run 을 반반 섞어 상체가 옆으로 비틀림 | `ProiRunPosture`: 허리 / 가슴 굽힘 + 고개 들기, 클립의 거꾸로 된 비틀림을 지우고 자연스러운 방향(나간 발 쪽 골반, 반대쪽 어깨가 앞으로)으로, 딛는 발 쪽 기울기 · 숙임. 블렌드 트리에 걷기(Run 0.79배속, Speed 0.5) 추가 |
| 궁극기 컷씬 때 머리 휘날림 | — | `ProiHairFlutter`: 주먹 마주칠 때 충격, 차징 때 솟는 열기 소용돌이, 도약 / 정점 / 내려꽂기 / 땅 찍기마다 바람. 물리 시간을 프로이 연출 배속에 맞춤 |

측정 (`ProiMagicaPlayModeTests`, 같은 동작 · 같은 경로, 이전 = 트윈테일 스프링):

| | 이전 | 변경 후 |
| --- | --- | --- |
| 걷기: 머리끝이 머리보다 뒤로 / 위로 (대기 대비) | 0.17 m / 0.11 m | 0.25 m / 0.20 m |
| 달리기: 머리끝 뒤로 / 위로 | 0.20 m / 0.13 m | 0.30 m / 0.24 m |
| 걷기 · 달리기: 허리 굽힘 (골반–가슴 각) | 3.8° | 9.5° · 10.2° |
| 걷기 · 달리기: 어깨 회전 방향 (+1 자연스러움, −1 거꾸로) | −0.99 | +0.78 · +0.94 |

![걷기 옆](compare_walk_side.png)
![달리기 뒤](compare_run_back.png)
![궁극기](compare_ultimate.png)

알려진 점: 3타(360° 돌려차기) 동안 긴 트윈테일이 몸을 감으며 잠깐 얼굴 앞을 지나갑니다 (끝까지 같은 무게로 움직이게 한 결과,
`report.json` 의 attack 관통 수). 궁극기 런지 자세의 왼쪽 허벅지 관통 수는 이전부터 있던 스키닝(물리 아님) 겹침입니다.

## 구성

| 클로스 | 대상 | 고정 | 움직임 |
| --- | --- | --- | --- |
| Cloth_Hair | 빨간 머리카락 전체 (분할 렌더러 `Proi_Hair`) | 두피, 트윈테일 묶은 곳 | 같은 무게로 출렁, 이동 반대로 날림, 머리 · 목 · 가슴 · 허리 · 팔 · 허벅지 충돌 |
| Cloth_Tatters | 벨트 / 허벅지 스트랩에 매달린 얇은 천 띠 (`Proi_Lower`) | 매달린 곳 (무릎 위) | 가볍게 펄럭, 다리 충돌 |
| Cloth_Pants | 무릎 스트랩 아래 풍선 바지 + 그 위 스트랩 (`Proi_Lower`) | 무릎 스트랩, 부츠에 모인 커프 | 크게 흔들리고 부풀며 날림, 정강이 충돌 |

| 컴포넌트 (`ProiHunterProfile.Configure`) | 하는 일 |
| --- | --- |
| `ProiRunPosture` | 이동 중 상체 굽힘 / 고개 / 비틀림 바로잡기 / 걸음 리듬 (Magica 와 무관, 항상) |
| `ProiHairFlutter` | 궁극기 장면별 연출 바람 + 물리 시간 배속 (Magica 있을 때) |
| `ProiTailSpring` | Magica 가 없을 때만 (예전 방식) |

### 다시 만들기

1. Blender: `blender -b --python Tools/Proi/proi_magica_data.py -- --source Assets/Characters/Proi/Models/Proi_Character.fbx --output Assets/Characters/Proi/Magica/ProiMagicaData.json`
   (고정 / 이동 페인트, 몸 콜라이더)
2. Unity: **Tools > Proi > Setup Magica Cloth** (또는 `-executeMethod ProiMagicaSetup.RunBatch`) — 메시 분할, 클로스 / 콜라이더, PreBuild,
   프리팹 변형, `Proi.asset` 의 model 한 줄. 클로스 값은 `ProiMagicaSetup.cs` 의 `Plans`.
3. 확인: Test Runner > PlayMode > `ProiMagicaPlayModeTests` (→ `report.json`, 프레임 `Logs/ProiMagicaFrames`),
   게임 안 검사 `-executeMethod ProiHunterValidation.RunBatch` (→ `Documentation/Proi/ingame-validation.json`).

## 측정 항목 (`report*.json` 의 `phases`, 구간마다 처음 0.5초는 전환이라 뺌)

| 항목 | 뜻 |
| --- | --- |
| `hair_tips_behind_head_m` / `hair_tips_rise_m` | 머리카락 끝(묶은 곳에서 가장 먼 20%)이 대기 때보다 진행 반대쪽 / 위로 간 거리 |
| `hair_speed_per_m_by_band` | 묶은 곳에서의 거리 5구간별 (머리 기준 상대 속도 ÷ 거리) |
| `torso_lean_deg` / `waist_bend_deg` / `neck_lean_deg` | 엉덩이→머리 앞 기울기 / 골반–가슴 사이 굽힘 / 목 기울기 |
| `shoulder_twist_rms_deg` / `counter_rotation` | 골반 대비 어깨 비틀림 크기 / 방향 (+1: 나간 발 반대쪽 어깨가 앞으로) |
| `collider_penetration_worst_vertices` | 구간별로 처음엔 밖에 있던 정점이 몸 콜라이더 안으로 1 cm 넘게 들어간 최대 개수 |
