# Pirate-Alliance
해적의 시대가 왔다 알호이!

## Stage_1 — 쿼터뷰 백룸 맵

Unity **6000.3.19f1 / URP 17.3.0**로 여는 독립 맵 프로젝트야. 원본 게임의 캐릭터·애니메이션 팩과 훈련장은 포함하지 않았어. 맵 확인용 탐방자와 최소 전투 기능만 함께 들어 있어.

2026-09-30 개정은 쿼터뷰에서 방에 들어오자마자 공간을 알아보는 데 초점을 맞췄어. 첫 구간은 **노란 벽지·카펫·반복 형광등**, 다음은 **흰 타일·청록색 수면·아치**, 이후는 **빈 상가와 환승 시설**, 보스는 **끝없는 노란 홀**로 구성해. 작은 문구나 의자 하나의 방향을 살피기 전에 큰 색과 재료, 형태가 먼저 읽히도록 했어.

일반 방은 `20×28m → 26×36.4m`로 각 변 1.3배, 보스 방은 `28×36m → 42×54m`로 각 변 1.5배 넓혔어. 바닥 면적은 각각 1.69배와 2.25배야. 방 루트와 가구 크기는 유지하고 건축 크기, 배치, 소켓과 소리 영역을 함께 맞추는 방식이야.

**완료 상태:** 방 20종을 개정했고 기존 24종에 작업대·풀룸 아치·산업용 환풍기를 더한 Meshy 27종을 반입했어. 최종 치수·이동 검사는 20/20방, Meshy 연결은 198/198곳(누락 0), 플레이 검사는 4스테이지·전투 방 9개를 통과했어. [최종 검사](Documentation/Liminal/backrooms-validation.json) · [플레이 검사](Documentation/liminal-validation.json) · [20방 카탈로그](Documentation/Liminal/Previews/Backrooms/Catalog_20Rooms.jpg)

HUD를 포함한 실제 게임 시점도 확인할 수 있어. [입구](Documentation/Liminal/Previews/Backrooms/Play_Arrival.png) · [일반 전투](Documentation/Liminal/Previews/Backrooms/Play_Combat.png) · [보스전](Documentation/Liminal/Previews/Backrooms/Play_Boss.png)

신규 3종의 실제 내장 텍스처는 base color·normal 4096×4096, metallic/roughness 2048×2048이야. 품질 비교용 미채택 결과를 포함한 6개 생성 후보에 총 210 Meshy API credits를 사용했어. [모델·비용 검증 기록](Documentation/Liminal/backrooms-meshy-validation.json)

### 바로 열어 보기

- `Assets/Liminal/Scenes/LiminalRoomGallery.unity`: 방 20종을 한눈에 보는 편집용 갤러리.
- `Assets/Liminal/Scenes/LiminalPropGallery.unity`: 반입을 마친 기물 27종을 비교하는 갤러리.
- `Assets/Liminal/Scenes/LiminalRun.unity`: 시드에 따라 방을 이어 붙이는 4스테이지 플레이.

Hierarchy의 각 방은 `Architecture / Props / Lighting / Gameplay / Sockets`로 나뉘어 있어. 방 프리팹을 열어 기물을 옮기면 다음 생성에도 반영돼. `AC Roguelike > Liminal > Room Workshop`에서 변형을 복제하고 스테이지 후보에 추가할 수 있어.

### 조작

WASD 이동 · 마우스 좌클릭 카타나 4연격([근접 공격 문서](Documentation/PlayerMelee/README.md)) · SPACE 회피 · Q 지원 스킬 · E 출구 · ESC 일시정지. 전투방을 정리하고 출구에서 증강 하나를 선택한 뒤, 다음 스테이지로 이동해. 세 번째 탐방 스테이지 다음은 보스 방이야.

한 세션의 방 수는 기존과 같은 `5+5+5+2=17개`야. 크기 변경으로 직선 보행만 계산하면 약 35초 늘어나지만, 전투·회피·탐색을 포함한 실제 시간은 별도야. 목표인 약 10분(8~12분)의 사람 플레이 시간은 아직 측정하지 않았어.

### 개정 검증과 미리보기

- `AC Roguelike > Liminal > Backrooms > Validate Dimensions and Gallery`: 크기, 루트 스케일, 소켓·바닥·환경음 범위, Meshy 슬롯 누락, 아치 개구부, 갤러리 중첩을 검사해. 결과는 `Documentation/Liminal/backrooms-validation.json`에 저장해.
- `AC Roguelike > Liminal > Backrooms > Capture Room Catalog`: 격리된 PreviewScene에서 20개 방 전체 PNG, 4×5 카탈로그와 4개 입구 시점 이미지를 `Documentation/Liminal/Previews/Backrooms/`에 생성해. 촬영 중에는 셰이더를 동기 컴파일하고 예열 프레임을 한 번 렌더링해.
- 실제 이동 연결과 전투 진행 검사는 별도의 `LiminalPlayValidation.ValidateTraversal()` / `ValidateStructure()` / `Start()`를 사용해. 실행 조건과 결과 경로는 [설계 문서](Documentation/LiminalDesign.md)에 정리했어.

### 테마 던전 실험실

스테이지 1을 기준으로 한 숲·프로그램 감옥·멸망한 지구·동굴 네 테마 던전이야. 테마마다 동선과 랜드마크가 다른 방 7종이 있어. 한 판은 도착방 → 전투방 3개(다섯 후보에서 시드로 선택) → 출구방이야. 2차 Meshy 모델 28종(모델당 1.3만~3.1만 삼각형)과 1차 12종을 배치했어. 실행·편집 방법은 [스테이지 컨셉 문서](Documentation/StageConcepts/README.md), 방별 설계·카메라 분석·검증은 [바리에이션 계획](Documentation/StageConcepts/VariationPlan.md)에 있어.

### 지원 캐릭터 · 유니

게이머 지원 캐릭터 유니의 액티브 스킬 **보너스 스테이지!**(Q)를 추가했어. 복셀 운석이 한 번 떨어지고, 머리 위에 5초 동안 1UP이 떠. 그동안 공격할 때마다 팩맨 투사체가 함께 나가서 추가 피해를 줘. 시전하는 동안에는 무적이야. 화면 왼쪽 아래 카드에 일러스트와 쿨타임이 표시되고, `LiminalRun` 씬에서 바로 써 볼 수 있어. 오브젝트는 Meshy로 만든 뒤 복셀로 변환했어. [지원 캐릭터 문서](Documentation/Liminal/SupportSkill/README.md)

### 저장소 받기

큰 3D 모델과 텍스처는 Git LFS로 관리해. `Stage_1`을 받은 뒤 `git lfs pull`을 실행해 줘. Unity가 패키지를 불러오면 별도 외부 에셋 팩 없이 열 수 있어.

`Documentation`에는 리미널 스페이스 설계 근거, 기물 제작 목록과 검증 결과가 들어 있어.
