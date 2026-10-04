# 아포칼립스 던전 몬스터

`G-04 / 저항체 소탕`의 `concept_ruins` 던전에 쓰는 몬스터 5종이다. 폐허의 기계, 뒤틀린 인간형, 무인 드론, 부유 촉수 기계의 실루엣과 전투 역할을 나눴다.

## 적용 범위

- 로비 임무 ID: `ruins_clearance`, 목적지 ID: `ruins`.
- 스테이지: `Assets/StageConcepts/Stages/Ruins.asset`, `stageId = concept_ruins`.
- 도착 방과 출구 방 사이의 전투방 3개에서 각각 3마리, 합계 9마리를 배치한다. 방 순서와 스폰 순서로 종류를 순환하므로 한 번의 던전에 5종이 모두 등장한다.
- `LiminalRunDirector.SpawnRuinsMonsters`가 기존 방의 스폰 위치를 사용한다. 다른 테마의 몬스터 구성과 방 배치는 바꾸지 않는다. 독립 `StageConcept_Ruins` 씬도 같은 스테이지 ID 분기를 사용한다.
- 일반 임무 목록에서 사이버 감옥을 제외한 기존 설정은 유지한다.

## 몬스터 구성과 현재 수치

수치는 `RuinsMonster.cs`, `RuinsProjectile.cs`, `RuinsGroundPulse.cs` 기준이다. 거리와 속도는 Unity 월드 단위(m, m/s)다. 기본 HP는 스테이지 인덱스 0의 값이며, 추가 인덱스마다 12가 더해진다.

| 이름 / 종류 | 기본 HP | 일반 이동 | 주요 역할 | 모델 배치 높이 |
| --- | ---: | ---: | --- | --- |
| 고철 방벽 / `ScrapBulwark` | 150 | 1.15 | 느린 추적, 고정 방향 철편포 3연사 | 본체 1.65m |
| 속죄의 잔해 / `PenitentHusk` | 105 | 2.45 | 양팔 내지르기, 짧은 돌진 | 본체 2.7m |
| 사체 수색기 / `CarrionDrone` | 65 | 2.7 | 중거리 주회, 3발 부채 탄막, 근접 시 3.8m/s 후퇴 | 본체 0.75m, 바닥에서 1.15m 띄움 |
| 납골 해파리 / `OssuaryMedusa` | 125 | 1.65 | 세 지점 지연 방전, 근접 촉수 쓸기 | 본체 1.2m, 바닥에서 1.65m 띄움; 아래에 촉수 8개 |
| 애곡의 모체 / `MourningMatron` | 190 | 1.65 | 느린 추적, 넓은 손톱 쓸기, 긴 돌진 | 본체 3.3m |

G-04 임무의 기존 보정은 별도로 적용된다. HP 배율은 1.25이고, 적이 받는 피해에는 장갑 배율 0.75가 적용된다. 따라서 이 임무의 첫 스테이지 HP는 순서대로 188 / 131 / 81 / 156 / 238이다. 정수 반올림과 최소 피해 1 규칙은 `TrainingEnemy`와 `GateMissionEnemyModifier`가 처리한다. 독립 씬에서 임무 보정 없이 생성하면 기본 HP를 사용한다.

| 공격 | 준비 예고 | 실제 동작과 피해 | 공격 후 회복 |
| --- | --- | --- | --- |
| 고철 방벽 철편포 | 1.05초, 전방 길이 18m 직선 | 0 / 0.18 / 0.36초에 같은 방향으로 3발. 발당 피해 12, 속도 7.4m/s, 최대 이동 18m, 탄 반경 0.30m | 1.15초 |
| 속죄의 잔해 양팔 | 0.9초, 반경 3.2m·좌우 27° 부채 | 공격 시작 0.28초에 1회 판정, 피해 19 | 1.05초 |
| 속죄의 잔해 돌진 | 0.9초, 길이 3.95m·반폭 0.85m 직선 | 이동 2.6m, 속도 6.5m/s, 앞쪽 타격 여유 1.35m, 피해 20을 최대 1회 | 1.05초 |
| 사체 수색기 탄막 | 0.85초, 반경 16m·좌우 17° 부채 | -14° / 0° / +14°로 동시에 3발. 발당 피해 9, 속도 10.8m/s, 최대 이동 16m, 탄 반경 0.18m | 0.9초 |
| 납골 해파리 방전 | 1.25초, 반경 1.15m 원 3개 | 준비 시작 때 세 지점을 고정. 각 원이 지연 후 피해 16을 최대 1회 판정 | 1.15초 |
| 납골 해파리 촉수 | 1.0초, 반경 3.4m·좌우 72° 부채 | 공격 시작 0.3초에 피해 17을 최대 1회 | 1.15초 |
| 애곡의 모체 손톱 | 1.2초, 반경 4.3m·좌우 75° 부채 | 공격 시작 0.35초에 피해 23을 최대 1회 | 1.25초 |
| 애곡의 모체 돌진 | 1.3초, 길이 6.6m·반폭 1m 직선 | 이동 5.2m, 속도 7.5m/s, 앞쪽 타격 여유 1.4m, 피해 25를 최대 1회 | 1.25초 |

방전 위치는 처음부터 고정한다. 방향 공격은 준비 시간의 첫 33%까지만 플레이어를 따라가고, 나머지 시간에는 방향을 고정한다. 회복 중에는 이동하거나 공격하지 않는다. 투사체와 원형 방전은 플레이어의 실제 충돌 몸체가 위험 영역에 겹치는지 검사하며, 피해 적용은 공통 `LiminalPlayerHealth.TakeDamage`를 통해 대시 무적과 피격 무적을 따른다.

## 모델과 애니메이션

Meshy AI가 만든 본체와 PBR 재질을 사용한다. `Art/Meshy/<종류>/provenance.json`에 생성 방식, 작업 ID, 메시 통계와 출처를 남긴다.

- 고철 방벽: Meshy 본체에 반동 포신과 머즐 광원을 추가한다. 이동 중 차체 진동, 공격 시 포신 반동, 피격과 사망을 표현한다.
- 사체 수색기: Meshy 동체에 두 회전 팬과 엔진 발광 부품을 더한다. 방향 전환 때 몸체가 기울고, 정지 중에도 작게 떠 움직인다.
- 납골 해파리: Meshy가 만든 부유 본체 아래에 절곡 가능한 기계 촉수 8개를 조립한다. 촉수는 관절 재질과 장갑 재질을 나눈 동적 튜브 메시다. 대기, 이동 지연, 공격 준비, 앞쪽 쓸기, 사망 시 이완이 서로 다르다.
- 속죄의 잔해와 애곡의 모체: Meshy의 골격과 스킨 메시를 사용한다. 대기, 걷기, 달리기, 공격, 사망 클립을 `PlayableGraph`로 재생한다. 속죄의 잔해 공격은 양팔 내지르기, 애곡의 모체 공격은 손톱 쓸기 계열이다. 피격은 공통 몸체 반동과 리그 반응으로 처리한다.
- 인간형은 `Animator.applyRootMotion = false`로 둔다. 이동 권한은 캐릭터 컨트롤러가 가지며, 애니메이션은 시각 표현을 담당한다.
- 애곡의 모체는 첫 텍스트 생성 결과가 얼굴, 케이블 머리카락, 비어 있는 기계 복부, 긴 손톱을 충분히 담지 못해 새 독창적 콘셉트 이미지를 만든 뒤 Meshy 이미지 기반 생성으로 대체하는 절차를 사용한다. 최종 채택 모델과 대체 이력은 해당 `provenance.json`이 기준이다.

프리팹 루트는 바닥 기준점, 정면은 +Z다. 모델과 기계 부품은 `Visual/Pose` 아래에 놓는다. 떠 있는 몬스터도 바닥에 있는 캐릭터 컨트롤러로 이동하므로 벽과 잔해를 통과하지 않는다. 모델이 위아래로 떠 움직여도 게임의 판정 몸체가 함께 위로 날아가지는 않는다.

`RuinsMonsterNavigation`은 방마다 경로 정보를 공유한다. 방의 가장 큰 몬스터 충돌 반경을 기준으로 잔해 사이 통과 가능 여부를 검사한다. 실제 이동에는 기존 `CharacterObstacleSlide`를 사용해 스치는 장애물을 따라 비껴가되, 정면 벽을 통과하지 않는다. 돌진은 요청한 경로 길이를 소모하므로 비껴가는 궤적이 총 돌진 거리를 늘리지 않는다.

## Figma 제작 규칙의 적용 범위

기준 문서는 [몬스터 제작 규칙이 있는 Figma 파일](https://www.figma.com/design/4WRuwkklDt0IvMblKaS92K/)이다. 공통 규칙은 다음 노드에서 확인했다.

| 구분 | Figma 노드 |
| --- | --- |
| 공통 2 | [165:47](https://www.figma.com/design/4WRuwkklDt0IvMblKaS92K/?node-id=165-47) |
| 공통 3 | [165:69](https://www.figma.com/design/4WRuwkklDt0IvMblKaS92K/?node-id=165-69) |
| 공통 4 | [165:104](https://www.figma.com/design/4WRuwkklDt0IvMblKaS92K/?node-id=165-104) |
| 공통 5 | [165:121](https://www.figma.com/design/4WRuwkklDt0IvMblKaS92K/?node-id=165-121) |
| 공통 8 | [173:392](https://www.figma.com/design/4WRuwkklDt0IvMblKaS92K/?node-id=173-392) |
| 공통 9 | [173:471](https://www.figma.com/design/4WRuwkklDt0IvMblKaS92K/?node-id=173-471) |
| 공통 10 | [173:536](https://www.figma.com/design/4WRuwkklDt0IvMblKaS92K/?node-id=173-536) |
| 공통 11 | [192:2](https://www.figma.com/design/4WRuwkklDt0IvMblKaS92K/?node-id=192-2) |

6·7번의 리미널 스페이스 몬스터는 해당 테마의 **예시**다. 사무용품 형태, 매복, 사망 폭발을 모든 테마의 의무로 해석하지 않는다. 이번 몬스터는 폐허 테마에 맞춘 기계·인간형·드론·촉수 기계다. 사망 폭발은 이 명단 전체에 추가하지 않았다.

적용한 공통 기준은 다음과 같다.

- 일반 몬스터는 대기 자세의 전체 높이를 0.5–3.5m 범위로 맞춘다. 본체만 따로 잰 크기와 월드에 배치한 전체 크기를 구분한다. 바닥 피벗과 정면 방향을 통일한다.
- 소재뿐 아니라 실루엣, 움직임, 공격 역할도 테마와 종류에 따라 구분한다.
- 대기·이동·준비·공격·회수·피격·사망이 읽히게 한다. 모든 종류에 같은 상태 수나 같은 연출을 강제하지 않는다.
- 예고한 방향과 범위에 실제 공격 판정을 맞춘다. 기존 `Telegraph`의 오버레이 재질을 사용해 물, 나무길, 다리 위에서도 예고의 크기와 진행을 읽을 수 있게 한다.
- 체력이 0이 되면 즉시 전투 등록을 해제하고, 공격 예고·탄·지연 방전을 취소한다. 시체 애니메이션이 끝날 때까지 방 클리어를 기다리지 않는다.
- 모서리를 살짝 스치는 이동은 부드럽게 비껴간다. 정면 장애물 차단과 이동 거리 한도는 유지한다.

## 소스 위치

| 경로 | 내용 |
| --- | --- |
| `Assets/RuinsMonsters/Art/Meshy/` | 본체 GLB, 인간형 골격·모션 FBX/GLB, 생성 이력 |
| `Assets/RuinsMonsters/Art/Generated/` | 빌더가 저장한 촉수 메시, 기계 부품 재질, 애니메이션 클립 |
| `Assets/RuinsMonsters/Resources/RuinsMonsters/` | 종류 이름으로 로드하는 최종 프리팹 5개 |
| `Assets/RuinsMonsters/Scripts/RuinsMonster.cs` | 행동, 공격 준비·판정·회복, 생성과 정리 |
| `Assets/RuinsMonsters/Scripts/RuinsMonsterRig.cs` | 기계 부품, 촉수, 인간형 클립 재생 |
| `Assets/RuinsMonsters/Scripts/RuinsProjectile.cs` | 철편탄·드론탄과 연속 충돌 검사 |
| `Assets/RuinsMonsters/Scripts/RuinsGroundPulse.cs` | 고정 위치 원형 예고와 지연 방전 |
| `Assets/RuinsMonsters/Scripts/RuinsMonsterNavigation.cs` | 방 단위 경로 탐색 |
| `Assets/RuinsMonsters/Editor/RuinsMonsterBuilder.cs` | 반복 가능한 프리팹 조립 |
| `Assets/RuinsMonsters/Editor/RuinsMonsterValidation.cs` | 실제 로비 진입부터 임무 완료까지 통합 검증 |
| `Assets/RuinsMonsters/Editor/RuinsProjectileValidation.cs` | 독립 물리 공간에서 탄·방전 검증 |
| `Tools/RuinsMonsters/meshy_monsters.py` | 본체 생성과 재개 가능한 작업 기록 |
| `Tools/RuinsMonsters/meshy_rig.py` | 인간형 골격과 애니메이션 생성 |
| `Tools/RuinsMonsters/meshy_matron_image.py` | 애곡의 모체 이미지 기반 대체 생성 |
| `Tools/RuinsMonsters/References/` | 이번 작업의 독창적 제작용 콘셉트 |

Meshy API 키는 환경 변수 `MESHY_API_KEY`로만 읽는다. 원본 API 응답과 작업 캐시는 `Tools/RuinsMonsters/Source/`에 두고 버전 관리에서 제외한다. 저장한 작업 ID로 재개하며, 성공 여부가 불명확한 생성 요청을 자동으로 중복 제출하지 않는다.

## 다시 만들기와 검증

Unity 버전은 `6000.3.19f1`이다. 에디터에서 `AC Roguelike > Ruins > Build Apocalypse Monsters`를 실행하면 최종 프리팹을 다시 만든다. 모든 본체와 인간형 모션 파일이 있어야 한다. 이 과정은 Meshy API를 다시 호출하지 않는다.

프로젝트 루트에서 별도 배치 에디터로 조립하려면 다음을 실행한다. 같은 프로젝트의 에디터가 이미 열려 있으면 위 메뉴를 사용한다.

```powershell
$unityExe = 'C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe'
$projectPath = (Get-Location).Path
Start-Process -FilePath $unityExe -ArgumentList @(
    '-batchmode', '-projectPath', ('"{0}"' -f $projectPath),
    '-executeMethod', 'AcRoguelike.Ruins.Editor.RuinsMonsterBuilder.Build',
    '-logFile', 'Library/RuinsMonsterBuild.log', '-quit'
) -WindowStyle Hidden -Wait
```

통합 검증은 아래 명령으로 실행한다. 이 검증은 Play Mode에 들어갔다가 자체적으로 종료하므로 `-quit`를 붙이지 않는다. 화면 캡처가 있으므로 `-nographics`도 사용하지 않는다.

```powershell
New-Item -ItemType Directory -Force 'Library/RuinsMonsterValidation' | Out-Null
$validation = Start-Process -FilePath $unityExe -ArgumentList @(
    '-batchmode', '-projectPath', ('"{0}"' -f $projectPath),
    '-executeMethod', 'AcRoguelike.Ruins.Editor.RuinsMonsterValidation.RunBatch',
    '-logFile', 'Library/RuinsMonsterValidation/editor.log'
) -WindowStyle Hidden -Wait -PassThru
$validation.ExitCode
```

검증은 로비 G-04 진입, 5종 등장, 리미널 예시 몬스터 혼입 여부, 공격과 회복, 실제 뼈·촉수 움직임, 일시정지, 사망 정리, 방 해제, 승리·재시작·로비 복귀, G-06 격리를 확인한다. 탄과 방전은 별도 물리 공간에서 얇은 벽, 발사 지점 겹침, 사거리, 대시 무적, 시전자·플레이어 사망 정리를 확인한다. 검증이 건드린 진행도 설정은 복원하고 열린 원본 씬은 저장하지 않는다.

2026-10-04 Unity 6000.3.19f1의 실제 Play Mode에서 **92개 확인 항목 통과, 오류 0개**를 기록했다. G-04 전체 진행과 5종 배치, 공격 예고·뼈·촉수 변형, 대시 무적, 얇은 벽, 사망 취소, 승리·재시작·로비, 다른 테마 격리를 확인했다. 수동 플레이의 장기 난이도 평가는 별도다.

결과 원본은 `Library/RuinsMonsterValidation/validation.json`에 있고, 보관본은 [검증 보고서](Validation/validation.json)에 있다. [5종 미리보기](Validation/apocalypse-roster.png), [애곡의 모체 실제 폐허 배치](Validation/MourningMatron-front.png), [부유 촉수 기계 최신 모습](Validation/review-OssuaryMedusa.png)도 함께 보관한다.

납골 해파리의 촉수 연결 위치를 추가 수정했다. 촉수 시작점의 반경을 0.55m에서 0.32m로 좁히고 높이를 1.73m로 맞춰 본체 하부 링에 붙였다. 본체와 촉수의 기울어지는 중심도 일치시켜 움직일 때 연결부가 벌어지지 않게 했다. 이 수정 후 이동·공격 준비·공격 자세에서 촉수 8개의 연결부를 총 288회 확인했고, 본체 대비 위치 변화와 촉수 뿌리의 틈은 모두 0.0001m 미만이었다. [연결부 검증 결과](Validation/medusa-attachment.json)에 수치를 보관한다. 위의 92개 통합 검증은 이 위치 수정 전 기록이다. 이전 폐허 배치 캡처 `OssuaryMedusa-front.png`도 수정 전 모습이다.

실제 대기 자세의 측정 높이는 고철 방벽 1.65m, 속죄의 잔해 2.42m, 드론 본체 0.75m, 납골 해파리 전체 2.78m, 애곡의 모체 3.09m였다. 인간형은 대기 중 자세 변화 때문에 조립 기준 높이와 차이가 난다. 피부 메시를 굽는 `BakeMesh`에는 `useScale=true`를 주어 좌표 변환 때 크기가 중복 적용되지 않게 했다.

## 참고와 제작 이력

`The Forever Winter`는 거칠게 수리된 전쟁 기계, 불안한 인간형, 산업 잔해의 분위기 참고로 사용한다. 사용자가 제공한 부유 촉수 기계와 추가 인간형 이미지는 특징과 실루엣을 이해하기 위한 참고다. 해당 게임이나 첨부 이미지의 원본 메시·텍스처를 추출해 재사용하지 않는다. 본체는 이번 작업의 새 프롬프트 또는 독창적 콘셉트 이미지에서 Meshy로 생성하고, 움직이는 기계 부품과 투사체는 프로젝트에서 새로 만든다.

Meshy 실제 누적 사용량은 **208 크레딧**이다. 채택한 몸체 5종 150, 인간형 2종의 리깅·동작 28, 요구 특징이 부족해 제외한 첫 애곡의 모체 30을 합한 값이다. 제외한 결과는 `Tools/RuinsMonsters/Source/mourning_matron/rejected_text_body/`에 보존했고 게임 프리팹에서는 사용하지 않는다. 재제작한 모체는 이번 작업에서 만든 원본 콘셉트 이미지를 입력했다. 생성 요청의 중복 제출 방지 기록과 각 `provenance.json`, `motion_provenance.json`을 함께 보관한다.
