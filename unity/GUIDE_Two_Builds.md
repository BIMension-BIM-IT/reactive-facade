# 가이드: 하나의 프로젝트, 두 개의 웹 빌드 (1784 / 박스 건물)

구조: **Unity 프로젝트는 하나, 장면(Scene)은 둘, 웹 빌드는 둘.**
- Scene_1784: 외피 모델링 담당 1784 모델. CSV 모드(지금까지 하던 것). 웹 빌드 이름 `louver`.
- Scene_Box: 웹이 보낸 건물을 스크립트가 자동으로 만들어 주는 장면. 직접 계산 모드. 웹 빌드 이름 `louverbox`.
스크립트는 같은 것을 같이 써요(Assets/Scripts 하나). 빌드할 때 장면만 다르게 골라요.

※ 아래는 제가 Unity 에디터에서 직접 돌려 본 게 아니라 컴파일만 확인한 거예요. 돌려 보다가 에러가 나면 Console 메시지를 그대로 알려 주세요.

---
## 0. v9 설치 (한 번만, 약 10분)
README.md 1번대로: Scripts 폴더 전체를 덮어쓰고, StreamingAssets의 CSV 4개와 Plugins/WebGL/LouverState.jslib를 넣어요.
이번에 새로 들어온 스크립트: `BoxBuildingGenerator.cs`. (LouverBridge.cs는 웹 담당 것이라 그대로 두세요.)

## 1. Scene_1784 (기존 장면) 확인 (약 5분)
- `SunLouverController`: **Direct Calc 꺼짐**(기본값). Rig = 1784 수직 루버, Rig Horizontal = 비워 둠(수평은 박스 쪽에서).
- `LouverBridgeGlue`: **Auto Switch Site 꺼짐**(기본값).
- 여기에는 `BoxBuildingGenerator`를 붙이지 마세요.

## 2. Scene_Box 만들기 (약 30~40분)
1. File > New Scene > (Basic URP) 로 새 장면을 만들고 `Scene_Box`로 저장해요.
2. 빈 오브젝트 4개를 만들고 스크립트를 붙여요.
   | 오브젝트 이름 | 붙일 스크립트 | 설정 |
   |---|---|---|
   | LouverBridge | LouverBridge (웹 담당 것), LouverBridgeGlue | Glue의 Bridge/Controller 칸은 비워 둬도 자동 연결 |
   | SunController | SunLouverController | **Direct Calc 체크(켜짐)**, Sun = 장면의 Directional Light, 위도·경도는 기본값 둬도 돼요(웹이 보내면 바뀜), Facades/Rig/Rig Horizontal은 비워 둬요(생성기가 채워요) |
   | BoxBuilding | BoxBuildingGenerator | 아래 3번 머티리얼 연결 |
   | LouverOrientation | LouverOrientationReceiver | 이름은 자동으로 LouverOrientation이 돼요 |
3. 머티리얼 3개를 만들어서 BoxBuilding의 Wall Material / Blade Material / Ground Material에 연결해요.
   (색은 마음대로: 벽 연한 파랑, 날 흰색, 바닥 회색 정도.) **웹 빌드에서는 꼭 연결하세요**. 비워 두면 코드가 셰이더를 찾는데 웹 빌드에서는 빠져서 분홍색이나 안 보임이 될 수 있어요.
4. `LouverBridgeGlue`의 **Auto Switch Site는 꺼 둬요**(Scene_Box는 항상 직접 계산이라서 Direct Calc 체크만 믿으면 돼요).
5. (선택) `LouverStateReporter`, 시연 버튼 UI는 Scene_Box에는 넣지 않아도 돼요. Radiance 일사 결과는 1784 전용이에요.

## 3. 에디터에서 먼저 시험 (약 10분)
1. Scene_Box에서 Play를 눌러요.
2. Hierarchy에서 `BoxBuilding`을 선택하고, Inspector 맨 위 스크립트 이름 옆 점 세 개(⋮) 또는 우클릭 > **시험: 건물 만들기 (ㄱ자)**를 눌러요.
3. 확인할 것:
   - Scene 뷰에 ㄱ자 건물과 바닥, 날이 보여요. Console에 `[BoxBuilding] 건물 6면 …`이 나와요.
   - `SunController`의 Hour 슬라이더나 자동 재생으로 시간이 흐르면, 해가 있는 면의 날이 돌아가요(하지 정오면 남면이 닫혀요).
   - `SunController` Inspector에서 Orientation을 Horizontal로 바꾸면 수평 날로 바뀌어요.
4. 안 되면 Console의 빨간 에러를 알려 주세요.

## 4. 웹 빌드 두 개 (각 약 20~40분, 빌드 시간 포함)
공통 설정은 지금까지와 같아요(Compression Disabled, .data 100 MB 미만 유지 등).

**빌드 A: 1784**
- File > Build Profiles(또는 Build Settings)에서 Scenes In Build에 **Scene_1784만** 체크해요.
- WebGL로 빌드하고, 결과 파일 이름은 **louver**(louver.loader.js 등)가 되도록 해요 → 웹의 `docs/unity/Build/`에 넣어요(지금 위치 그대로).

**빌드 B: 박스 건물**
- Scenes In Build에서 **Scene_Box만** 체크해요.
- 결과 파일 이름은 **louverbox**(louverbox.loader.js 등) → 웹의 `docs/unity_box/Build/`에 넣어요.
- StreamingAssets는 `winter_calendar.csv`만 있으면 돼요(나머지는 1784용이라 빼면 용량이 줄어요).

## 5. 웹 담당께 전달할 것 (웹에서 어느 빌드를 부를지)
웹이 고른 좌표가 1784에서 300 m 이내면 `louver`, 아니면 `louverbox`를 불러요. 기준(좌표 37.3595, 127.1052)은 Glue에 들어 있는 것과 같아요.
`unity.js`의 `UNITY` 상수 대신 아래처럼 하면 돼요(예시):

```js
// 좌표로 어느 빌드를 쓸지 고르기 (m 단위 근사 거리)
function pickBuild(lat, lon){
  const dLat=(lat-37.3595)*110540, dLon=(lon-127.1052)*111320*Math.cos(37.3595*Math.PI/180);
  const near = Math.hypot(dLat,dLon) < 300;
  return near ? {NAME:'louver', BUILD_URL:'unity/Build'} : {NAME:'louverbox', BUILD_URL:'unity_box/Build'};
}
// 건물을 새로 골랐을 때 빌드가 바뀌면 unityInstance를 Quit() 한 뒤 새로 불러요.
```
빌드가 바뀔 때 Unity 로딩이 다시 오래 걸려요(첫 로딩과 같아요). 1784 근처만 쓰는 시연이면 빌드 전환 없이 `louver` 하나로 돌아가요.

웹이 Unity에 보내는 건 그대로예요(`OnSiteInput`, `SetDateTime`, `SetView`). 박스 빌드는 `OnSiteInput`을 받으면 건물을 새로 만들어요.
**더 정확하게(선택):** 지도에서 고른 건물의 외곽 좌표를 `OnSiteInput`에 같이 보내면 건물 모양이 정확해져요. 지금은 입면 방위·폭 순서로 모양을 복원해서 ㄱ·ㄷ자 같은 직각 건물만 맞아요.

## 6. 알려진 한계
- 박스 건물은 직각으로 꺾인 건물(직사각형, ㄱ·ㄷ자)만 맞아요. 비스듬한 입면은 가장 가까운 직각 방향으로 맞춰서 만들어져요.
- 입면 순서가 이상하게 오면(안 닫히면) 직사각형으로 대신 만들고 Console에 경고를 내요.
- 주변 건물 그림자는 반영하지 않아요(모든 층이 같은 각도).
- 박스 건물에서는 Radiance 일사 결과를 쓰지 않아요(웹의 자체 계산 값이 보여요).
- 날이 많으면 느려요. 수직 날은 입면 폭 ÷ 0.35 m 개, 수평 날은 건물 높이 ÷ 0.35 m 개 만들어요(입면마다 최대 800).
- 날 크기·개수가 이상하면 SunController의 Blade Depth/Spacing(웹이 보낸 값으로 덮어써요)과 BoxBuilding의 Blade Thickness를 확인하세요.
