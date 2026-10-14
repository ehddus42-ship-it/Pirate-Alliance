# 남궁루나 (NamgungLuna)

URP Toon 캐릭터 + Magica Cloth 2 옷·머리 물리 설정.

## 필요한 것
- **Magica Cloth 2** (유료 에셋, 저장소에 포함하지 않음). 각자 Asset Store 계정으로
  Package Manager > My Assets > Magica Cloth 2 > Download > Import 하세요. Example 폴더는 없어도 됩니다.
- Magica를 가져오면 `MAGICACLOTH2` 심볼이 자동으로 추가되고, 그때부터 아래 Magica 관련 코드가 컴파일됩니다.
  Magica가 없으면 해당 코드는 건너뛰며(빌드 오류 없음), 프리팹의 Magica 컴포넌트만 Missing으로 보입니다.

## 파일
| 경로 | 내용 |
|---|---|
| `Magica/NamgungLuna_Magica.prefab` | 게임에 쓰는 프리팹 (모델 + Magica 콜라이더 + 클로스 9개) |
| `Magica/NamgungLuna_MagicaDemo.unity` | 데모 씬. Play 하면 캐릭터 전체가 걷기·방향 전환·회전, 바람 포함 |
| `Generated/Setup_*/Prefabs/NamgungLuna_Toon.prefab` | 물리 없는 Toon 프리팹 |
| `Models/NamgungLuna.fbx` | 모델 (파츠별 분리 메시, Read/Write 켜짐) |
| `Magica/Scripts/` | `LunaClothFollower` (치마 금장식이 치마를 따라감), `LunaMagicaDemoMotion` (데모용 이동) |
| `Editor/` | `Tools > Namgung Luna > Create Toon Prefabs from FBX`, `Tools > Namgung Luna > Setup Magica Cloth` |
| `Magica/*.json` | 고정/이동 페인트 점 데이터와 콜라이더 정의 (Setup Magica Cloth가 사용) |
| `Magica/Tests/` | Play 모드 검증 테스트 (관통·겹침·정지 유지), 영상 프레임 녹화(Explicit) |

## 클로스 구성
- 머리 4개(앞머리 / 뒷머리 좌 / 뒷머리 우 / 뒷머리 보강다발): 두피는 고정, 길이만 움직임. 머리·목·몸통·어깨 콜라이더.
- 치마 2개(속치마 / 겉치마): 겉치마→속치마 상호 충돌(Mutual Collision)로 서로 뚫지 않음. 다리 콜라이더.
- 소매 2개(팔꿈치 고정, 팔뚝 콜라이더), 칼라 리본 1개.
- 고정/이동 영역은 Magica 페인트 데이터로 저장되어 인스펙터 페인트 도구로 수정할 수 있습니다.

## 주의
- 현재 뼈대·애니메이션이 없는 정적 모델입니다. 클로스는 캐릭터 전체의 이동·회전과 바람에 반응합니다.
  리깅 후에는 `Magica_Colliders` 아래 콜라이더를 해당 뼈 아래로 옮기세요.
- Magica 제한: 클로스 1개의 렌더 정점 합 65,535 이하(넘으면 빌드가 멈춤), 렌더러 31개 이하.
  그래서 머리는 3개 메시로 나뉘어 있고 치마 금장식은 클로스가 아니라 `LunaClothFollower`로 따라갑니다.
- `Setup Magica Cloth`를 다시 실행하면 프리팹과 데모 씬을 새로 만듭니다(직접 바꾼 값은 덮어씀).
