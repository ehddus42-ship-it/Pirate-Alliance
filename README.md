# Pirate-Alliance
해적의 시대가 왔다 알호이!

## Stage_1 — 리미널 스페이스 맵

Unity **6000.3.19f1 / URP 17.3.0**로 여는 독립 맵 프로젝트야. 원본 게임의 캐릭터·애니메이션 팩과 훈련장은 포함하지 않았어. 맵 확인용 탐방자와 최소 전투 기능만 함께 들어 있어.

### 바로 열어 보기

- `Assets/Liminal/Scenes/LiminalRoomGallery.unity`: 방 20종을 한눈에 보는 편집용 갤러리.
- `Assets/Liminal/Scenes/LiminalPropGallery.unity`: Meshy 기물 24종을 비교하는 갤러리.
- `Assets/Liminal/Scenes/LiminalRun.unity`: 시드에 따라 방을 이어 붙이는 4스테이지 플레이.

Hierarchy의 각 방은 `Architecture / Props / Lighting / Gameplay / Sockets`로 나뉘어 있어. 방 프리팹을 열어 기물을 옮기면 다음 생성에도 반영돼. `AC Roguelike > Liminal > Room Workshop`에서 변형을 복제하고 스테이지 후보에 추가할 수 있어.

### 조작

WASD 이동 · 마우스 좌클릭 부적 · SPACE 회피 · E 출구 · ESC 일시정지. 전투방을 정리하고 출구에서 증강 하나를 선택한 뒤, 다음 스테이지로 이동해. 세 번째 탐방 스테이지 다음은 보스 방이야.

### 저장소 받기

큰 3D 모델과 텍스처는 Git LFS로 관리해. `Stage_1`을 받은 뒤 `git lfs pull`을 실행해 줘. Unity가 패키지를 불러오면 별도 외부 에셋 팩 없이 열 수 있어.

`Documentation`에는 리미널 스페이스 설계 근거, 기물 제작 목록과 검증 결과가 들어 있어.
