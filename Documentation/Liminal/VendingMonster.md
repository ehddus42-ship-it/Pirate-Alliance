# 자판기 잠복 몬스터

기존 `vending_machine`을 몸체로 사용하고 Meshy에서 제작한 팔·다리를 결합한 몬스터다. 기본 상태는 평범한 자판기이며, 플레이어가 감지 범위에 들어오면 팔, 다리 순서로 전개한 뒤 돌진과 캔 던지기를 사용한다.

## 바로 사용하기

- 프리팹: `Assets/Liminal/Prefabs/Enemies/VendingMachineMonster.prefab`
- 플레이 테스트 장면: `Assets/Liminal/Scenes/VendingMonsterShowcase.unity`
- 실제 스테이지: `Assets/Liminal/Scenes/LiminalRun.unity`
- 동작 영상: `Documentation/Liminal/Concepts/VendingMachine/vending_monster_animation.mp4`

테스트 장면에서 Play를 누르고 WASD로 자판기에 접근한다. 기존 플레이어의 전투·회피 입력을 사용할 수 있다. Inspector의 `VendingMonster`에서 감지 거리와 이동 속도, 피해량을 조절한다. 외부 이벤트에서 `Activate()`를 호출해도 같은 전개가 시작되며 중복 호출은 무시한다.

기존 자판기가 있던 6개 방 프리팹(01, 02, 07, 08, 11, 13)에서 장식 자판기와 고정 충돌체를 제거하고 같은 위치에 `VendingMachineAmbush` 몬스터 프리팹을 배치했다. 정면 방향도 기존 배치와 같다. `LiminalRun`의 첫 스테이지 미리보기에도 반영되어 있다. 배치 위치 기록은 `Concepts/VendingMachine/stage-placement.json`에 저장한다.

플레이어가 해당 방에 입장한 뒤 감지가 활성화된다. 전투 방의 몬스터는 기존 일반 적들과 함께 방 클리어·문 열림 집계에 포함된다. 도착·탐색·통로의 자판기는 접근하면 공격하는 선택적 매복이며 기존 이동 경로와 문 규칙을 유지한다. 자판기가 없던 위치에 몬스터를 추가 생성하지 않는다. 방 제작 도구로 재생성할 때도 기존 자판기 위치에 몬스터가 배치된다.

## 동작과 판정

| 항목 | 기본값·동작 |
| --- | --- |
| 감지 | 7m, 벽이 시야를 가리면 작동하지 않음 |
| 휴면 | 팔다리 숨김, 자판기 높이 1.9m, 자동 타기팅 제외 |
| 전개 | 3.2초. 몸체 떨림 → 팔 전개 → 다리 전개·몸체 상승 → 착지·정착 |
| 각성 후 높이 | 충돌체 3.08m |
| 보행 | 1.8m/s, 실제 이동량에 따라 애니메이션 재생 속도 조절 |
| 돌진 | 0.95초 예비동작·바닥 예고, 최대 8.5m/s·10m, 피해 22, 벽에서 정지 |
| 캔 던지기 | 2.65초. 배출구 접근 → 쥐기 → 뽑기 → 어깨 젖힘 → 발사 → 후속동작 |
| 캔 쥐기·발사 | 클립 시작 후 0.88초 / 1.81초 AnimationEvent |
| 투척 조준 | 1.45초에 목표 위치 고정. 발사 후 추적하지 않음 |
| 캔 피해 | 14. 중력 궤적, 이동 구간 SphereCast로 플레이어·벽 충돌 |
| 정리 | 일시정지는 동작·발사체를 함께 멈춤. 사망·스테이지 전환 시 관련 공격 정리 |

손에 든 캔은 `CanGrip` 자식으로 생성되고 발사 순간 월드 공간으로 분리된다. `CanDispenserSocket`은 꺼내기 동작의 기준이다. 애니메이션 이벤트와 낮은 프레임률 대응 처리는 중복 생성·중복 발사를 방지한다.

## 리깅과 애니메이션 편집

- 46관절 Generic 리그: 루트·자판기, 양팔·양다리, 손가락 30관절.
- 네 개의 독립 SkinnedMeshRenderer. 정점당 최대 4개의 정규화된 관절 가중치.
- 클립은 `Assets/Liminal/Art/VendingMonster/Animations/`에 저장된다.
- 9개 클립: Dormant, Awakening, Idle, Chase, ChargeWindup, Charging, ChargeRecover, CanThrow, Recovery.
- 60fps로 포즈를 샘플링하고 오차 범위 내 키를 줄였다. 루트 이동은 충돌을 고려하는 게임 코드에서 처리한다.
- `VendingMonsterRig`는 제작 포즈와 2관절 IK를 정의하며, `VendingMonsterBuilder`가 편집 가능한 AnimationClip을 굽는다. 런타임에서는 Animator가 클립을 재생한다.
- 제작용 Blender 파일: `Tools/VendingMonster/Source/vending_monster_rig.blend`.

포즈 생성 코드를 수정한 뒤 Unity의 `AC Roguelike > Liminal > Vending Monster > Build Rigged Monster`를 실행하면 프리팹과 클립을 다시 만든다. 직접 편집한 클립을 보존하려면 다른 경로에 복제해 사용한다. 테스트 장면 생성과 포즈 캡처 메뉴도 같은 위치에 있다.

## 게임용 리소스 최적화

| 부위 | 원본 삼각형 | 게임용 삼각형 |
| --- | ---: | ---: |
| 팔 한쪽 | 44,410 | 4,999 |
| 다리 한쪽 | 43,914 | 4,000 |
| 팔다리 합계 | 176,648 | **17,998** |
| 자판기 몸체 | 97,787 | **20,000** |
| 몬스터 전체, 캔 제외 | 274,435 | **37,998** |

팔다리는 약 89.8%, 전체는 약 86.2% 감량했다. 기존 UV를 보존하는 삼각형 메시 감량을 적용하고, 고해상도 원본의 형상·표면 노멀을 저폴리곤 메시의 tangent-space 노멀맵으로 베이킹했다. 수작업 쿼드 리토폴로지는 아니다. 팔·다리·몸체 각각 2048×2048 노멀맵을 사용한다.

게임용 텍스처는 `Assets/Liminal/Art/VendingMonster/Textures/`에 분리했다. PC Standalone은 BC7 압축, 2K 상한, 밉맵·스트리밍 밉맵, CPU 읽기 비활성화를 사용한다. glTF 셰이더가 RGB 노멀을 읽으므로 노멀은 선형 색 공간의 기본 텍스처로 유지한다. 좌우 부위는 재질·텍스처를 공유한다. 모바일 전용 ASTC 설정과 기기별 FPS 측정은 별도 작업이다.

기존 자판기 원본과 Meshy 고해상도 팔·다리는 보존했다. 프리팹은 `vending_monster_rig.glb`와 `vending_machine_game.glb`의 게임용 메시를 사용한다. 원본 작업 파일은 재생성용이며 런타임에서 불러오는 코드가 없다.

## 생성과 재현

1. `meshy_parts.py`: Meshy 작업 제출·상태·다운로드. 인증키는 `MESHY_API_KEY` 환경변수로만 읽는다.
2. `retopologize_bake.py`: 게임용 메시 감량 및 고해상도→저폴리곤 노멀 베이킹.
3. `build_limb_rig.py`: 관절 생성, 스킨 가중치, 좌우 부품, GLB·Blender 파일 생성.
4. `extract_game_textures.py`: 게임용 GLB에서 텍스처를 분리하여 Unity 압축 설정 적용 준비.
5. Unity `VendingMonsterBuilder.Build()`: 텍스처 압축 설정, 프리팹, Animator, 클립, 캔, 효과음 생성.

팔·다리는 완성 콘셉트에서 내장 이미지 생성으로 분리 참조를 만든 뒤 Meshy `meshy-7.1` image-to-3D로 제작했다. 원본 참조는 `Assets/Liminal/Art/Meshy/vending_monster_parts/{arm,leg}/reference.png`, 생성 기록은 같은 폴더의 `provenance.json`이다. 처음의 텍스트 기반 후보는 중복 손·불필요한 몸통 때문에 미채택했다. Meshy API 소비량은 최종 후보 70 credits, 미채택 후보 포함 **총 140 credits**다.

API 기준: [Meshy Text to 3D](https://docs.meshy.ai/en/api/text-to-3d), [Image to 3D](https://docs.meshy.ai/en/api/image-to-3d), [일반 인간형 자동 리깅의 적용 범위](https://docs.meshy.ai/en/api/rigging). 이 자판기 몬스터에는 별도 제작한 Generic 리그를 사용했다.

## 검증 기록

- `Concepts/VendingMachine/validation.json`: 감지·시야 차단·순차 전개·관절/폴리곤/노멀맵·돌진 피해·캔 생성/분리/피해·벽 충돌·일시정지·사망 정리.
- `Concepts/VendingMachine/ModelReview/optimization.json`: 실제 원본/감량 삼각형 수와 베이킹 설정.
- `Concepts/VendingMachine/ModelReview/rig-build.json`: 관절 수, 스킨 정점과 삼각형 수.
- `Documentation/liminal-validation.json`: 기존 4스테이지·9전투 방, 보스, 문·증강·승패·재시작 회귀 검증.
- `Concepts/VendingMachine/ModelReview/`: 실제 게임용 메시의 전개·돌진·캔 동작 포즈 렌더.

동작 영상은 편집용 클립을 확인하는 포즈 릴이다. 실제 플레이의 감지·이동·피해·충돌은 별도 Play Mode 검증으로 확인한다.
