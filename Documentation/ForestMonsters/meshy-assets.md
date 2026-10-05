# 판타지 숲 몬스터 리소스

달빛고목의 숲 전용 몬스터 5종과 흙 파편 1종. 기존 숲의 청록 발광, 짙은 나무껍질, 에메랄드 이끼와 연녹색 포인트 사용.
실루엣은 게임의 높은 사선 카메라에서도 구분되도록 몸통과 주요 공격 부위 중심으로 구성.

| 리소스 | 역할 | 폴리곤 목표 | Unity 조립 크기 |
|---|---|---:|---|
| moonworm | 분절 지렁이 / 잠복 돌출 / 꾸물거리는 돌진 | 14,000 tris | 길이 3m / 높이 2.03m |
| moss_frog | 혀로 당기는 개구리 / 웅크림과 도약 | 14,000 tris | 높이 1.4m |
| lunar_butterfly | 가루 방사 / 날개바람 / 날갯짓 | 12,000 tris | 날개 폭 3.0m |
| elderwood | 움직이는 고목 / 묘목 투척 / 아치형 뿌리 | 16,000 tris | 높이 3.1m |
| volatile_sapling | 파괴 가능한 투척 묘목 / 추적 자폭 | 8,000 tris | 높이 0.8m |
| burrow_earth_clod | 솟구치는 흙 / 뿌리·이끼 파편 | 2,500 tris | 프리팹 폭 1m / 연출 시 18~34cm |

## 모델과 연출의 경계

- Meshy: 외형, UV, 2K PBR 컬러·거칠기·노멀 맵. 작은 반복 흙 파편만 1K로 축소.
- Unity: 지렁이 분절 변형, 개구리 탄성 도약과 혀, 나비 날개 동작, 고목 걸음, 묘목 추적.
- Unity VFX: 흙 솟구침, 가루 방사, 회오리 투사체, 뿌리의 지면 출입, 묘목 폭발.
- 별도 재료가 되는 기존 상용 게임의 캐릭터나 모델은 사용하지 않음. 묘목 추적·폭발의 동작만 참고.

## 생성과 재현

`python Tools/ForestMonsters/meshy_monsters.py plan`

`python Tools/ForestMonsters/meshy_monsters.py run --budget 210 --workers 3`

`python Tools/ForestMonsters/optimize_textures.py`

`python Tools/ForestMonsters/prepare_forms.py`

`python Tools/ForestMonsters/finalize_assets.py`

환경 변수 `MESHY_API_KEY`만 사용. 키, 원본 응답, 서명된 다운로드 주소, 생성 캐시는 커밋 대상에서 제외.
제출 직전에 예약 상태를 저장. 응답이 불명확한 POST는 자동 재시도하지 않으며, 확인된 task ID만 재개.
모든 시도의 예약 크레딧을 포함한 상한 210. 지렁이 최초 결과 1회 반려 후 재생성 포함.
최종 실제 사용량·폴리곤 수·메모리 크기는 `meshy-assets.json` 참조.

## 방향과 피벗

모델은 +Y 위, +Z 정면을 요청. 실제 방향은 썸네일과 조립 화면을 확인한 뒤 기록.
GLB의 원래 좌표를 유지하고, Unity 조립 시 단일 배율을 적용. X/Z 중앙과 바닥 Y를 원점에 맞춤.
`meshy-assets.json`의 경계는 glTFast의 X축 반전을 적용한 Unity 기준.
권장 배율과 오프셋도 기록. 최종 전투 콜라이더·피격점은 시각 메시와 별도로 설정.

참조: [Meshy Text to 3D API](https://docs.meshy.ai/en/api/text-to-3d) — 2026-10-05 확인.
