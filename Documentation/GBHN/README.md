# 금비·하늘 — Magica Cloth 2

`New_Character(GB,HN)`의 `f05ce14` 모델을 바탕으로 만든, Unity에서 편집 가능한 옷·머리 물리 프리팹입니다. 원본 FBX, 원본 프리팹, 재질은 보존하고 별도 프리팹과 스킨 가중치 메쉬를 생성합니다.

## 바로 확인하기

1. Unity **6000.3.19f1**에서 프로젝트를 엽니다. 각 개발자의 라이선스로 **Magica Cloth 2**를 설치해야 합니다. 유료 패키지 원본은 Git에 포함하지 않습니다.
2. `Assets/Characters/GBHN/GBHN_ClothShowcase.unity`를 열고 Play를 누릅니다.
3. 금비는 제공된 걷기 애니메이션, 하늘은 검사 씬 전용 편안한 팔 자세로 자동 이동·회전합니다. 실제 사용할 프리팹에는 이 검사 씬용 이동 스크립트가 붙지 않습니다.

실제 씬에 배치할 프리팹:

| 캐릭터 | Toon | URP Lit |
| --- | --- | --- |
| 금비 | `Assets/Characters/Geumbi/Magica/Geumbi_Magica.prefab` | `Assets/Characters/Geumbi/Magica/Geumbi_Magica_Lit.prefab` |
| 하늘 | `Assets/Characters/Haneul/Magica/Haneul_Magica.prefab` | `Assets/Characters/Haneul/Magica/Haneul_Magica_Lit.prefab` |

하늘의 제작 원본에는 Animator Controller가 없습니다. 실제 게임용 애니메이션은 해당 프리팹의 Animator에 연결하면 됩니다. 이번 작업은 옷·머리 물리 및 성능 설정이며, 캐릭터 전투 기술이나 플레이어 선택 목록을 추가하지 않습니다.

## 적용 부위와 성능 설정

| 캐릭터 | 부위 | 시뮬레이션 점 수 | 최대 이동 설정 |
| --- | --- | ---: | ---: |
| 금비 | 머리 끝 | 24 | 2.5cm |
| 금비 | 재킷 자락 | 18 | 5.5cm |
| 금비 | 바지 끝 | 12 | 1.5cm |
| 하늘 | 긴 머리 | 42 | 12cm |
| 하늘 | 옆머리 5가닥 | 15 | 1.2cm |
| 하늘 | 재킷 끝 | 18 | 2.5cm |

- **BoneCloth**가 작은 보조 뼈대만 계산하고, 일반 SkinnedMeshRenderer가 모델을 그립니다. 고밀도 메쉬 전체를 매 프레임 CPU에서 변형·복사하는 MeshCloth 방식은 사용하지 않습니다.
- 캐릭터당 3개 물리 그룹, 금비 54점·하늘 75점입니다. 정점당 최대 4개 뼈 가중치이며 루트·머리 윗부분·옷의 부착 부위는 고정합니다. 몸에 붙는 하늘의 수트와 단단한 장비는 원래 애니메이션을 따릅니다.
- 머리·목·몸통·골반·다리 충돌체, 이동·회전 속도 제한, 각도·이동 범위 제한을 적용했습니다. 고비용 자기 충돌은 꺼져 있습니다.
- 카메라에서 **13.5m부터 효과를 줄이고 18m에서 시뮬레이션을 정지**합니다. `MainCamera` 태그를 사용하며, 별도 카메라 구조에서는 각 MagicaCloth의 Distance Culling Reference Object를 지정합니다.
- 화면 밖에서는 시뮬레이션을 정지하고 돌아올 때 초기화합니다. 순간이동 2m 또는 한 프레임에 100도 이상 회전해도 초기화하여 급격한 늘어짐을 줄입니다.
- 모든 그룹에 **PreBuild** 데이터가 포함되어 런타임의 물리 메쉬 생성이 필요 없습니다. 전역 Magica 주파수나 다른 캐릭터 설정은 바꾸지 않습니다.

## 수정·재생성

Prefab Mode에서 `Magica_Hair`, `Magica_LongHair`, `Magica_CoatHem` 등의 컴포넌트로 강도·감쇠·충돌체·거리 설정을 조절할 수 있습니다. `MC2_` 이름의 보조 뼈대는 해당 몸 뼈 아래에 있습니다. 뼈대 구조를 바꾸면 Magica Cloth의 PreBuild도 다시 생성해야 합니다.

`Tools > Geumbi and Haneul > Rebuild Magica Cloth Prefabs`는 원본으로부터 파생 메쉬·두 재질 버전·검사 씬을 다시 생성합니다. 프리팹에서 직접 한 조정은 재생성 시 덮어써지므로 유지할 설정은 `Assets/Characters/GBHN/Editor/GBHNClothSetup.cs`에도 반영합니다. 재생성은 원본 FBX나 원본 프리팹을 수정하지 않습니다.

메쉬는 LFS로 보관합니다. 다른 PC에서 받은 뒤 `git lfs pull`로 실파일을 내려받습니다.

## 검증

프로젝트의 에디터를 닫은 상태에서 PowerShell로 실행합니다.

```powershell
./Tools/GBHN/Validate.ps1
# 원본부터 다시 만들고 검증할 경우
./Tools/GBHN/Validate.ps1 -Rebuild
```

검사는 실제 Play Mode에서 부위별 움직임, 고정 루트, 변형 안정성, 거리·화면 밖 정지와 복귀, 순간이동, 비활성화 후 복귀, Toon/Lit 반복 생성, 4명 동시 실행, 검사 씬을 확인합니다. 하늘의 애니메이션 스트레스 검사에는 금비의 걷기를 임시 리타기팅하며, 그 애니메이션을 하늘의 실제 프리팹에 저장하지 않습니다.

측정 환경: **i5-14400F / RTX 5060 / Unity 6000.3.19f1**. 단일 캐릭터 480프레임, 4명 동시 360프레임을 측정합니다. 수치는 각 Magica 시뮬레이션 시작부터 작업 완료까지의 CPU 경과 시간입니다. Burst 준비, 첫 리소스 로딩, 스크린샷, 메쉬 검사, 렌더링은 제외합니다. 따라서 게임 전체 FPS를 의미하지 않으며 원본 모델의 렌더링 비용은 별도입니다. 여러 캐릭터를 소환하는 게임에서는 프리팹과 텍스처를 로딩 화면에서 미리 읽고 인스턴스를 재사용하는 편이 좋습니다.

최종 수치는 [금비](Validation/Geumbi.json), [하늘](Validation/Haneul.json), [4명 동시 검사](Validation/four-characters.json), [Play Mode 결과](Validation/playmode-results.xml)에 기록됩니다. [생성 보고서](cloth-setup.txt)에는 기본 자세 보존 오차와 물리 점 수가 기록됩니다.

![검사 씬](Validation/showcase.png)
