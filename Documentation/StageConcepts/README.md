# 스테이지 컨셉 실험실

현재 `stage_capture` 브랜치에서 기존 스테이지 1을 기준으로 만든 네 가지 던전 바리에이션이다. 기존 **`Assets/Liminal/Scenes/LiminalRoomGallery.unity`**에 원래 방 20개와 신규 테마 방 20개를 함께 모았다. 기존 방·스테이지·실행 씬은 보존한다.

## 기존 맵과 함께 보기

**AC Roguelike → Liminal → Room Workshop → 전체 맵 갤러리**를 연다. 같은 씬의 왼쪽에는 기존 20개 방, 오른쪽에는 숲·프로그램·폐허·동굴이 열별로 5개씩 배치되어 있다. 두 구역은 연결 바닥으로 이어진다.

Room Workshop의 **같은 갤러리의 숲 · 프로그램 · 폐허 · 동굴 구역 보기** 또는 **AC Roguelike → Stage Concepts → 00 Open Shared Room Gallery**로 새 테마 구역을 바로 볼 수 있다. 방 목록에는 기존 방과 신규 방이 함께 나오며 **씬에서 보기**로 해당 방을 선택한다. 프리팹 연결을 유지하므로 원본 방을 편집하면 갤러리에도 반영된다.

갤러리를 재생성해도 테마 프리팹이 있으면 자동으로 함께 배치한다. **Add Missing Themes to Shared Gallery**는 기존 배치를 보존하면서 누락된 테마 방만 추가한다.

[통합 갤러리 전체 캡처](Previews/Shared_Gallery.png). 통합 후 40개 방의 ID·프리팹 연결·방 간 겹침을 확인했고, 재동기화 시 중복 오브젝트가 생기지 않는 것도 확인했다. 기존 갤러리 검사 결과는 `Documentation/Liminal/backrooms-validation.json`에 기록했다.

## 실행

Unity 메뉴 **AC Roguelike → Stage Concepts**에서 테마를 열고 Play를 누른다.

| 테마 | 실행 씬 | 핵심 구조물 |
| --- | --- | --- |
| 판타지 숲 | `Assets/StageConcepts/Scenes/StageConcept_Forest.unity` | 마법 거목, 룬 석문, 거대 버섯, 반딧불과 뿌리 |
| 프로그램 감옥 | `Assets/StageConcepts/Scenes/StageConcept_Digital.unity` | 서버 기둥, 연산 코어, 데이터 게이트, 회로 바닥과 데이터 장벽 |
| 멸망한 지구 | `Assets/StageConcepts/Scenes/StageConcept_Ruins.unity` | 붕괴 고층 건물, 끊어진 고가도로, 녹슨 버스, 도로와 철근 잔해 |
| 동굴 | `Assets/StageConcepts/Scenes/StageConcept_Cave.unity` | 암석 아치, 석순, 수정 군집, 암벽과 지하수 |

Play 중 **F1~F4**로 각 던전을 새로 시작할 수 있다. **TAB** 또는 우측 상단 **테마 실험실** 버튼으로 선택 패널을 연다. 이동·전투는 기존 게임과 같다: WASD 이동, SHIFT/SPACE 대시, 마우스 조준/공격, 휠 확대, 출구 E.

각 던전은 스테이지 1처럼 **26 × 36.4m 방 5개**, 총 **182m** 길이다. 도착방 → 전투방 3개 → 출구방 구성이다. 중간방은 기존 시드 기반 셔플을 사용하며 각 전투방에 적 3마리를 배치한다. 중앙 폭 5m를 이동로로 확보했다.

## 편집

- `Assets/StageConcepts/Prefabs/Rooms`의 테마별 5개 프리팹을 편집한다.
- `Props/MeshySlot__<key>` 아래 모델의 배치와 크기를 조정할 수 있다.
- `Architecture`는 기본 바닥과 경계 충돌, `Gameplay`는 문·스폰, `Sockets`는 방 연결점, `Lighting`은 그림자 없는 보조 조명이다.
- 테마 정의는 `Assets/StageConcepts/Stages`, 색보정은 `Assets/StageConcepts/Profiles`에 있다.
- **Build Four Theme Dungeons**는 이 실험실의 방·씬·재질·메시를 다시 만든다. 프리팹을 직접 수정한 경우 재생성 전에 복제해 보관한다.

## Meshy 및 경량화

Meshy AI `meshy-7.1` text-to-3D로 12종을 실제 생성했다. API preview/remesh 후 PBR 텍스처를 생성했으며 각 자산의 요청, task ID, 삼각형 수와 사용량은 `Assets/StageConcepts/Art/Meshy/<key>/provenance.json`에 기록되어 있다. 비밀 키는 저장하지 않는다.

- 고유 Meshy 메시 합계 **51,465 삼각형**, 모델당 **2,960~5,867 삼각형**.
- 최종 GLB 합계 **33.92 MiB**. 고용량 원본을 게임용 텍스처로 최적화했다.
- 자산마다 메시 1개·머티리얼 1개·PBR 노말맵 포함.
- 반복 소품 9종은 1K, 거목·붕괴 건물·동굴 아치 3종은 2K.
- 노말맵은 Meshy에서 생성한 PBR 노말이며 별도의 고해상도 메시 베이크 작업은 수행하지 않았다.
- 실제 생성 사용량 **390 Meshy API credits**. 동굴 아치는 상자 형태로 나온 첫 후보를 폐기하고 자연 바위 아치로 교체했다.
- 반복 모델은 같은 메시·재질을 공유하고, 보조 장식은 재질별 메시로 합친다. 충돌은 단순 박스, 보조 조명은 방당 2개 이하로 제한했다.

재생성 도구는 `Tools/StageConcepts/meshy_assets.py`, 텍스처 최적화는 `optimize_meshy_textures.py`다. 대용량 원본·중간 생성물은 git에서 제외된 `Tools/StageConcepts/Source`에 보관한다.

## 검증 및 캡처

Unity 메뉴 **AC Roguelike → Stage Concepts**:

- **Validate All Concepts**: 20방의 치수·참조·Meshy 연결·삼각수·Astraia 캡슐 이동 경로 및 4개 씬 검사. 결과 `validation.json`.
- **Validate Play Through All Themes**: 실제 씬 전환·CharacterController 이동·적 생성·전투 게이트·5방 완료·Victory 검사. 결과 `play-validation.json`. 적 처치는 테스트에서 `TakeDamage`를 호출하므로 전투 밸런스 검증은 포함하지 않는다.
- **Capture Four Themes**: 저장된 테마 씬을 독립 미리보기로 렌더해 `Previews`에 전체방 및 플레이 카메라 PNG를 저장한다.

최종 검증은 **20/20개 방, 4/4개 스테이지와 씬, 155/155개 Meshy 배치 연결**을 통과했다. 실제 Play 검증에서도 **4개 테마의 이동, 전투방 12개, 출구 Victory**를 확인했다. 상세 수치는 위 JSON 보고서에 있다. 성능 예산 검사는 삼각형 수와 리소스 구조를 다루며 특정 기기의 FPS를 보장하는 벤치마크는 아니다.

전체방 캡처: [판타지 숲](Previews/Forest_Overview.png) · [프로그램 감옥](Previews/Digital_Overview.png) · [멸망한 지구](Previews/Ruins_Overview.png) · [동굴](Previews/Cave_Overview.png)

플레이 카메라 캡처: [판타지 숲](Previews/Forest_Gameplay.png) · [프로그램 감옥](Previews/Digital_Gameplay.png) · [멸망한 지구](Previews/Ruins_Gameplay.png) · [동굴](Previews/Cave_Gameplay.png) · [테마 선택 UI](Previews/Lab_Play.png)
