# Unity

담당: 대지 · 시각화, 분석 · 개발

1784 사옥의 루버가 시간에 따라 움직이는 장면을 만드는 Unity 스크립트입니다. WebGL로 빌드해 웹 도구 안에서 실행됩니다.

| 파일 | 내용 |
|---|---|
| `Scripts/` | 태양 위치 계산, 루버 규칙, 루버 회전, 웹 연동, 좌표로 박스 건물 자동 생성 |
| `Plugins/WebGL/` | Unity → 웹으로 루버 상태를 보내는 연결 |
| `StreamingAssets/` | 1784용 시간별 루버 각도표, 층별 각도표, 겨울 모드 달력 |
| `LouverRule_TestVectors.csv` | 규칙 구현 확인용 정답표 |
| `PACKAGE_README.md` | 스크립트 넣는 방법, 웹 연결 함수 |
| `GUIDE_Two_Builds.md` | 1784 빌드와 박스 건물 빌드 만드는 방법 |
| `GlareSim/` | 반사광 시뮬레이션: 우리 건물 유리 · 루버에서 나간 반사가 주변 건물 · 지면에 닿는 곳을 히트맵으로 표시 (루버 스크립트는 고치지 않음, 넣는 방법은 [GlareSim/README.md](GlareSim/README.md)) |

## 두 가지 모드

- **CSV 모드 (1784):** 미리 계산한 각도표를 읽어 루버를 돌림
- **직접 계산 모드 (다른 건물):** 위도 · 경도와 입면 방향만으로 Unity 안에서 태양 위치와 루버 각도를 바로 계산

## 대지 · 지형 모델 (Releases)

50MB가 넘어서 이 폴더에 올리지 않고 [Releases](https://github.com/BIMension-BIM-IT/reactive-facade/releases/tag/site-v1)에 올렸습니다. 파일 이름은 GitHub에서 한글이 깨지지 않도록 영문으로 바꿨습니다.

| 원래 파일 이름 | Releases 파일 | 크기 | 링크 |
|---|---|---|---|
| `bimit지형작은거.3dm` | `bimit_site_terrain_small.3dm` | 140 MB | [받기](https://github.com/BIMension-BIM-IT/reactive-facade/releases/download/site-v1/bimit_site_terrain_small.3dm) |
| `유니티용bimit지형작은거.3dm` | `bimit_site_terrain_small_unity.3dm` | 187 MB | [받기](https://github.com/BIMension-BIM-IT/reactive-facade/releases/download/site-v1/bimit_site_terrain_small_unity.3dm) |

## 올리지 않는 것

- Unity 프로젝트 전체 (Library, Temp, Builds)
- 3D 모델 · FBX (외피 모델링 담당 자료)
- zip, .unitypackage
