# 스테이지 컨셉 실험실

`stage_capture` 브랜치에서 기존 스테이지 1을 기준으로 만든 네 가지 던전 바리에이션이다.

- 테마마다 **서로 다른 방 7종**이 있고, 한 판에 그중 5개를 지난다.
- **`Assets/Liminal/Scenes/LiminalRoomGallery.unity`**에 원래 방 20개와 신규 테마 방 28개를 함께 모았다.
- 기존 리미널 방·스테이지·실행 씬은 보존한다.

방별 설계, 카메라 분석, 예산과 검증 결과는 [VariationPlan.md](VariationPlan.md)에 있다.

## 기존 맵과 함께 보기

**AC Roguelike → Liminal → Room Workshop → 전체 맵 갤러리**를 연다. 같은 씬에서 두 구역이 연결 바닥으로 이어진다.

- 왼쪽: 기존 20개 방
- 오른쪽: 숲·프로그램·폐허·동굴이 열마다 7개씩

새 테마 구역으로 바로 가는 방법은 두 가지다.

- Room Workshop의 **같은 갤러리의 숲 · 프로그램 · 폐허 · 동굴 구역 보기**
- **AC Roguelike → Stage Concepts → 00 Open Shared Room Gallery**

방 목록에는 기존 방과 신규 방이 함께 나오고, **씬에서 보기**로 해당 방을 선택한다. 프리팹 연결을 유지하므로 원본 방을 편집하면 갤러리에도 반영된다.

갤러리를 재생성해도 테마 프리팹이 있으면 자동으로 함께 배치한다. **Add Missing Themes to Shared Gallery**는 기존 배치를 보존한다. 누락된 테마 방만 추가하고, 제목이 바뀐 방의 표지판과 바리에이션 수 표지를 갱신한다. Unity 밖에서는 `Tools/StageConcepts/variations/gallery.py`가 같은 작업을 한다.

## 실행

Unity 메뉴 **AC Roguelike → Stage Concepts**에서 테마를 열고 Play를 누른다.

| 테마 | 실행 씬 | 방 7종 |
| --- | --- | --- |
| 판타지 숲 | `Assets/StageConcepts/Scenes/StageConcept_Forest.unity` | 반딧불 오솔길 · 달빛 연못 · 버섯 습지와 나무다리 · 뿌리 회랑 · 수호자 석상의 뿌리 문 · 엘프 성소 · 요정의 고리 |
| 프로그램 감옥 | `Assets/StageConcepts/Scenes/StageConcept_Digital.unity` | 부팅 패드 · 수용 포드와 감시 로봇 · 메모리 블록 미로 · 대각선 데이터 강 · 데이터 게이트 · 중앙 연산 코어 · 붉은 공백의 손상 섹터 |
| 멸망한 지구 | `Assets/StageConcepts/Scenes/StageConcept_Ruins.unity` | 고속도로 검문소 · 끊어진 고가도로 · 무너진 주거지 · 버려진 주유소 · 군 대피소 · 싱크홀 대로 · 마른 분수 광장 |
| 동굴 | `Assets/StageConcepts/Scenes/StageConcept_Cave.unity` | 푸른 균열 · 종유석 회랑 · 자수정 정동 · 지하 호수 · 심연의 문 · 버려진 광산 · 발광 버섯 동굴 |

Play 중 **F1~F4**로 각 던전을 새로 시작한다. 선택 패널은 **TAB** 또는 우측 상단 **테마 실험실** 버튼으로 연다. 이동·전투는 기존 게임과 같다.

- WASD 이동, SHIFT/SPACE 대시
- 마우스 조준/공격, 휠 확대
- E 출구

각 던전은 **일반방 3개와 고정 마지막 방 1개**, 총 **4개 방**이다. 일반방은 스테이지 1처럼 **26 × 36.4m**이고, 별도 보스방을 쓰는 경우 해당 보스방의 크기를 따른다.

1. 일반 전투방 3개: 02·03·04·06·07 다섯 후보에서 중복 없이 섞어 고른다. 첫 출동부터 새로운 무작위 시드를 사용한다.
2. 고정 마지막 방: 숲·프로그램·동굴은 출구방 05, 폐허는 전용 보스방을 사용한다.

도착방 01은 원본 에셋으로 보존하며 실제 출동 경로에는 포함하지 않는다.

전투방은 적 3마리를 방마다 다른 위치에 배치한다. 동선은 연못, 개울, 강, 구덩이, 엄폐물 배치에 따라 방마다 다르다. 모든 방은 스폰에서 양쪽 문과 적 스폰까지 이어진다.

## 편집

- 방 레시피는 `Tools/StageConcepts/variations/`의 `forest.py` · `digital.py` · `ruins.py` · `cave.py`다.
  - `python3 Tools/StageConcepts/variations/build.py --unity`를 실행하면 아래가 다시 쓰이고 이동 검사까지 돈다.
    - 프리팹·메시·재질·스테이지
    - `Assets/StageConcepts/Layouts/*.json`
    - 공유 갤러리
  - 자세한 절차는 [VariationPlan.md](VariationPlan.md) 5절에 있다.
- `Assets/StageConcepts/Prefabs/Rooms`의 테마별 7개 프리팹은 Unity에서 직접 편집할 수도 있다.
  - `Props/MeshySlot__<key>` 아래 모델의 배치와 크기를 조정한다.
  - `Architecture`는 기본 바닥과 경계 충돌, `Gameplay`는 문·스폰, `Sockets`는 방 연결점, `Lighting`은 그림자 없는 보조 조명이다.
  - 레시피로 다시 빌드하면 `Props`와 `Lighting`이 새로 쓰인다. 직접 수정한 프리팹은 먼저 복제해 보관한다.
- **Rebuild Variation Rooms From Layout**: 레이아웃 JSON으로 Unity 프리팹 API를 거쳐 방을 다시 만든다.
- **Build Four Theme Dungeons**: 위 재빌드 뒤 스테이지 정의, 실행 씬, 빌드 설정, 공유 갤러리까지 갱신한다.
- 테마 정의는 `Assets/StageConcepts/Stages`, 색보정은 `Assets/StageConcepts/Profiles`에 있다.

## Meshy 모델

키는 환경 변수 `MESHY_API_KEY`로만 읽고 어디에도 저장하지 않는다. 자산마다 요청, task ID, 삼각형 수와 사용량을 `Assets/StageConcepts/Art/Meshy/<key>/provenance.json`에 기록했다.

**2차 바리에이션 킷 28종**을 `meshy-7.1`로 생성했다.

- 모델당 13k~31k 삼각형이다.
- 텍스처는 PBR 2K다.
- 사용량은 900 credits이고, 재생성 3회를 포함한다.
- 목록과 판정은 [VariationPlan.md](VariationPlan.md) 4절과 [meshy-variations-review.md](meshy-variations-review.md)에 있다.
- 생성 도구는 `Tools/StageConcepts/meshy_variations.py`다.

**1차 킷 12종**(고목, 거대 버섯, 룬 아치, 서버 모놀리스, 연산 코어, 데이터 게이트, 붕괴 건물, 끊어진 고가도로, 녹슨 버스, 동굴 아치, 석순, 수정 군집)도 계속 쓴다.

- 모델당 2,960~5,867 삼각형이다.
- 반복 소품은 1K, 큰 모델 3종은 2K 텍스처다.
- 사용량은 390 credits였다.
- 생성 도구는 `Tools/StageConcepts/meshy_assets.py`와 `optimize_meshy_textures.py`다.

두 킷을 합쳐 고유 모델 40종을 28개 방의 353곳에 배치했다. 운영 기준은 다음과 같다.

- 반복 모델은 같은 메시·재질을 공유한다.
- 보조 장식은 방마다 재질별 메시 하나로 합친다.
- 충돌은 단순 박스다.
- 보조 조명은 그림자 없는 점광원이다.

대용량 원본·중간 생성물은 git에서 제외된 `Tools/StageConcepts/Source`에 보관한다.

## 검증 및 캡처

Unity 메뉴 **AC Roguelike → Stage Concepts**:

- **Validate All Concepts**: 28방과 4개 씬을 검사하고 결과를 `validation.json`에 저장한다.
  - 치수·참조·Meshy 연결·삼각수
  - Astraia 캡슐 이동 경로. 다리 데크처럼 1.2m 이하로 높은 바닥을 포함한다.
  - 스테이지 풀 구성과 시드별 경로
- **Validate Play Through All Themes**: 실제 씬 전환, CharacterController 이동, 적 생성, 전투 게이트, 4방 완료, Victory를 검사한다. 결과는 `play-validation.json`이다. 적 처치는 테스트에서 `TakeDamage`를 호출하므로 전투 밸런스는 검사하지 않는다.
- **Capture All Variation Rooms**: 28개 방마다 전체 1장과 게임 카메라 3장을 `Previews/Variations/Unity/`에 저장한다.
- **Capture Four Themes**: 테마별 01번 방의 전체방·플레이 카메라 PNG를 `Previews`에 저장한다.

이번 개편은 Unity 에디터 없이 작업했다. 그래서 위 메뉴의 결과 파일(`validation.json`, `play-validation.json`, `Previews/*_Overview.png` 등)은 아직 개편 전 20방 기준이다.

Unity 밖에서는 다음을 확인했다. 상세는 [VariationPlan.md](VariationPlan.md) 7절에 있다.

- `validate.py`: 이동 검사를 그대로 옮긴 판정으로 28/28 통과. 결과는 `variations-traversal.json`이다.
- `verify_unity.py`: 자산 구조 문제 0건.
- 공유 갤러리 48개 방 검사 통과.
- C# 컴파일 통과.

Unity에서 위 메뉴를 한 번 실행하면 공식 보고서와 캡처가 새 방 기준으로 바뀐다.

게임 카메라 미리보기(웹 렌더러, Unity 조명과 평균 색 차이 약 8% 이내):

- 테마 카탈로그: [판타지 숲](Previews/Variations/Forest_Catalog.jpg) · [프로그램 감옥](Previews/Variations/Digital_Catalog.jpg) · [멸망한 지구](Previews/Variations/Ruins_Catalog.jpg) · [동굴](Previews/Variations/Cave_Catalog.jpg)
- 방별 화면: `Previews/Variations/<방 ID>.jpg`

개편 전 캡처:

- 전체방: [판타지 숲](Previews/Forest_Overview.png) · [프로그램 감옥](Previews/Digital_Overview.png) · [멸망한 지구](Previews/Ruins_Overview.png) · [동굴](Previews/Cave_Overview.png)
- 테마 선택 UI: [Lab_Play.png](Previews/Lab_Play.png)
