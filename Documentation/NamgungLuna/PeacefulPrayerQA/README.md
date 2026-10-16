# 평시 남궁루나 손 모음 자세 보정

기존 Meshy 원본의 손 형상을 보존하고, 배 앞에서 두 손을 포개는 자세로 정리했다. 몸통과 붙어 있던 소매는 별도 검은 퍼프 소매와 흰 커프로 교체했다. 치마 안쪽에는 허벅지부터 부츠까지 이어지는 검은 스타킹 다리를 추가했다.

기존 상체의 리타깃 위치 변형은 가슴과 베일을 눌렀으므로, 원래 체형을 유지하는 작은 호흡·기울기 동작으로 바꿨다. 새 팔과 다리 보조 본은 모델과 모든 모션에 함께 포함된다. 원본 `Armature/Hips` 계층은 유지된다.

부츠는 원본 표면의 좌우 연결면 때문에 걸을 때 작은 삼각형이 길게 늘어났다. 좌우 신발을 독립 메시로 분리하고 절단면을 닫았으며, 치마의 모든 Boot 가중치를 제거했다. 각 신발은 해당 Boot 본 하나로만 움직인다. 색상 마스크에서 빠진 원본 신발 표면도 제거하여 세 번째 발처럼 남지 않도록 했다.

## 산출물

- 후보: `Temp/NamgungLuna/Prayer/LunaPeaceful_Game.fbx`
- 대기: `LunaPeacefulIdle_Game.fbx` — 2초, 61프레임
- 걷기: `LunaPeacefulWalk_Game.fbx` — 1.667초, 51프레임
- 대시: `LunaPeacefulDash_Game.fbx` — 0.333초, 11프레임. 대기 자세에서 시작·종료하며 중앙 구간에 낮은 상체 기울기와 빠른 발걸음이 있다.
- 전체 본 36개. 추가 팔 제어 6개와 내부 다리 제어 4개.

`idle_01_*`, `walk_13_*`, `walk_38_threequarter.png`, `dash_06_*`는 최종 후보의 실제 렌더다. `baseline_*`는 보정 전 비교용이다. 원본에서 생성된 베일의 일부 불규칙한 흰 가장자리는 보존되어 있다.

## 재현

저장된 불변 입력은 `Tools/NamgungLuna/Source/PeacefulBeforePrayer/`의 모델·대기·걷기 FBX 세 개다. 현재 Assets의 보정본을 다시 입력으로 사용하지 않는다. 입력 SHA-256과 재질 색상은 `prayer_build.json`에 기록된다.

프로젝트 루트에서 별도 Blender 5.2 백그라운드 프로세스로 실행한다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python 'Tools/NamgungLuna/build_peaceful_prayer.py'
```

이 명령은 후보와 검토 이미지에만 쓴다. Unity 설치와 Assets 반영은 별도 단계다. 원본 아틀라스 이외의 새 재질은 모두 `LunaPeaceful_` 접두사를 사용하며, 단색 Toon 매핑이 필요하다. 손가락은 원본 메시의 편안하게 굽힌 형상을 사용하고 별도 손가락 애니메이션은 포함하지 않는다.
