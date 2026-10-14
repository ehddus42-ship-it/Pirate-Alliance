# 로비 편성 · 지원 캐릭터 꼬끼오 · 프로이 기본 스킬과 전용 증강

기획서(Figma `대해적연합_기획서공장`)의 "지원 캐릭터 일람", "프로이 기획서", "증강 12–15"를 게임에 옮긴 내용이야.
플레이 가능한 헌터는 지금 있는 아스트라이아와 프로이(YHS_TEST 브랜치에서 가져옴)뿐이야. 다른 캐릭터(서금비 · 연하늘 · 피바다)는 제작 중이라 넣지 않았어.

## 로비 편성 담당 요원

| 항목 | 내용 |
| --- | --- |
| 위치 | 로비 왼쪽 위, 파란 LCD 단말기 앞 (`LiminalLobby.LoadoutOfficerPosition` / `LoadoutTerminalPosition`) |
| 모델 | `gate_engineer` (통신 단말을 맡은 게이트 기술 요원, idle · talk · greet) |
| 상호작용 | 2.6 m 안에서 `E 헌터 편성` |
| 창 | 왼쪽 HUNTER 카드(로스터의 헌터), 오른쪽 SUPPORT 카드(지원 캐릭터). 카드를 누르면 바로 편성되고, `편성 완료 [ESC]`·ESC·E로 닫아. |
| 저장 | `HunterLoadout` (PlayerPrefs `Hunter.Loadout.Hunter` / `Hunter.Loadout.Support`) |

- 단말기는 오퍼레이터 통신 화면 느낌(기획 메모의 "파란색 LCD")으로 화면 두 개와 상태 띠를 절차 텍스처로 그려.
- 헌터를 바꾸면 그 자리에서 몸이 바뀌어. 아스트라이아는 로비에서 평상복, 다른 헌터는 게이트 복장 그대로 다녀.
- 게이트 안의 F 교체는 그대로 남아 있고, F로 바꾼 헌터도 편성에 저장돼.
- 헌터 카드 일러스트는 `HunterProfile.portraitResource`(프로이: `Resources/Loadout/hunter_proi`)에서 읽어. 일러스트가 없는 헌터(아스트라이아)는 이름 첫 글자 판으로 보여.

## 지원 캐릭터

`SupportCharacterCatalog`에 등록된 지원 캐릭터 중 하나만 편성돼. 편성되지 않은 지원 캐릭터는 스킬도 HUD 카드도 꺼져.

| 지원 캐릭터 | 스킬 | 구현 |
| --- | --- | --- |
| 유니 | 보너스 스테이지! (액티브 · Q) | `SupportCharacterSkill` (기존) |
| 꼬끼오 | 헌터 야작 식당 (패시브) | `KokkioSupport` |

### 꼬끼오 · 헌터 야작 식당

정체불명의 닭꼬치 상인. 닭 인형 옷을 입고 있어.

- 방의 몬스터가 쓰러질 때마다 33% 확률로 닭꼬치가 떨어져 (`LiminalRunDirector.EnemyKilled`).
- 위치는 **체력이 0이 된 자리**야. 넉백이나 사망 연출로 시체가 미끄러져 멈춘 자리가 아니야.
- 헌터가 닿으면(수평 1.05 m) 최대 체력의 3%를 회복해. 예: 최대 체력 1,000이면 30 회복.
- 줍지 않은 닭꼬치는 필드에 남아. 다음 스테이지로 넘어가거나 로비로 돌아가면 사라져.
- 처치당 기대 회복량은 최대 체력의 약 1%야. 평균 3마리 처치마다 1개가 떨어져.
- HUD 카드: 왼쪽 아래, 일러스트가 파란 LCD 모니터 안에 있어(스캔라인 · 통신 램프). `PASSIVE` 표시와 필드의 닭꼬치 수, 회복량이 떠.
- 일러스트: `Assets/Liminal/Resources/SupportSkill/support_kokkio_portrait.png` (Figma에서 추출, 1024 px로 축소).
- 공통 증강 **알아하쇼**(지원스킬 자동 사용)는 꼬끼오처럼 패시브인 지원 캐릭터가 편성되어 있으면 선택지에 나오지 않아.

## 프로이 (게임 안)

| 입력 | 기술 | 구현 |
| --- | --- | --- |
| 좌클릭 | 3타 콤보 (왼쪽 잽 → 오른쪽 잽 → 왼발 회축). 타격마다 카메라 5% 줌인, 마무리 타에서 흔들림 | `ProiHunterKit` / `ProiBlazeCombo` |
| 우클릭 · K · 패드 R3 | 기본 스킬: 잡아 던지기 / 화염공 | `ProiHunterThrow` (신규) |
| R · 패드 X | 궁극기: 땅 폭발 (불기둥 3갈래). 플레이그라운드의 카메라 연출(정면샷 → 주먹 푸시인 → 도약 → 다이브 → 불기둥 와이드 → 복귀)을 게임 카메라에서도 그대로 재생 | `ProiHunterUltimate` / `ProiCinematicCamera` |
| SPACE | 화염 대시 (무적, 불꽃 잔상) | `ProiHunterKit` |

### 기본 스킬 (ProiHunterThrow)

- 2.8 m 안에 일반 적이 있으면 그쪽으로 돌아 다가가서 오른손으로 잡아. 뒤로 들어 올렸다가 앞으로 던져.
- 던진 적은 포물선으로 날아가. 바닥 · 벽 · 다른 적에 닿는 순간 폭발해서 던진 적은 55, 주변 적은 40 피해를 받아(반지름 2.4 m).
- 보스는 잡을 수 없어.
- 잡을 적이 없으면 같은 동작으로 손에 화염공이 자라나 던져져. 피해 45, 반지름 2 m, 사거리 10 m.
- 시전할 때 카메라가 5% 줌인하고 던질 때 줌아웃해.
- 재사용 대기는 3.5초야. 쓰는 동안 이동과 평타가 막혀.
- 카메라 줌은 `HitFeedback.Zoom` / `IsometricFollowCamera.AddZoom`을 새로 추가해서 써.

## 프로이 전용 증강 6종 (ProiAugments)

공통 증강과 같은 풀에 섞여서, 프로이를 입고 있을 때만 나와(`characterId = "proi"`).

| 증강 | 효과 | 구현 |
| --- | --- | --- |
| 화염질주 | 대시에 화염. 대시 경로의 적에게 26 피해 (대시마다 적 하나에 1번) | `ProiAugmentKit` |
| 불씨수호 | 몸을 도는 불씨가 피격 1회를 막음. 12초 뒤 다시 생김 | `ProiAugmentKit` (`LiminalPlayerHealth.AddGuard`) |
| 업화해방 | 궁극기 판정 범위 ×1.5, 피해 ×1.4 | `ProiHunterUltimate` |
| 반사의화염 | 화염 보호막이 피격 1회를 막고 그 피해의 2배를 3.5 m 안의 적에게 되돌림. 16초마다 재생성 | `ProiAugmentKit` |
| 맹화돌진 | 기본 스킬이 바뀜: 잡아 던지기 대신 전방 6.5 m 화염 돌진. 경로의 적에게 40 피해, 끝에서 폭발 35 피해 | `ProiHunterThrow` |
| 폭발확장 | 기본 스킬 폭발 범위 ×1.6 (착지 폭발 · 화염공 · 돌진 끝 폭발) | `ProiHunterThrow` |

- 기획서에서 아직 정하지 않은 부분은 임시값으로 정했어.
  - 불씨수호와 반사의화염의 재생성 조건은 대기 시간으로 했어.
  - 맹화돌진과 폭발확장을 같이 가지면 폭발확장이 돌진 끝 폭발에도 적용돼.
  - 수치는 모두 `ProiAugments`의 상수로 조절해.
- 둘 다 가지고 있으면 반사의화염이 먼저 막아.

## 검증

`AcRoguelike.Liminal.Editor.LoadoutValidation.RunBatch`로 `LiminalRun` 씬을 실제로 재생하며 확인해. Play Mode 검사가 끝나면 스스로 종료하니 `-quit`는 붙이지 마. 저장된 편성은 검사 뒤 원래대로 되돌려.

```
Unity -batchmode -projectPath . -executeMethod AcRoguelike.Liminal.Editor.LoadoutValidation.RunBatch -logFile Logs/loadout.log
```

결과(`loadout-validation.json`)와 화면이 이 폴더에 남아.

![편성 창](loadout_window.png)
![편성 요원과 단말기](loadout_officer.png)
![로비의 프로이와 꼬끼오 HUD](loadout_lobby_proi.png)
![잡아 던지기](proi_skill_grab.png)
