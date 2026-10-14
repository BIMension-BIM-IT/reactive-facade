# GlareSim: 반사광 시뮬레이션 (반응형 루버와 분리한 버전, 10/12)

우리 건물(유리 + 반응형 루버)에서 나간 햇빛 반사가 주변 건물·지면 어디에 얼마나 닿는지 보여줘요.

반응형 루버 스크립트(SunLouverController, LouverRig 등)는 **한 줄도 고치지 않아요.**
반사광 스크립트도 루버 스크립트를 코드로 직접 부르지 않고, 씬에서 이름으로 찾아서 읽기만 해요.
그래서 루버 스크립트가 업데이트되어도 이 폴더는 그대로 두면 돼요.

## 폴더

Assets 아래에 GlareSim 폴더를 통째로 넣어요. 안의 Editor, Resources 폴더 이름은 바꾸면 안 돼요.

    Assets/GlareSim/
      Scripts/   GlareReflectionSim.cs   붙이는 스크립트(패널, 계산 순서, CSV·JSON)
                 LouverSceneLink.cs      루버 쪽을 읽는 유일한 파일(이름으로 찾기)
                 GlareEngine.cs          계산 핵심(점마다 반사량 → 광선 → 닿은 곳에 쌓기)
                 GlareOptics.cs          직달일사, 유리 반사율, 루버 날 배열 계산
                 GlareReceivers.cs       주변 건물·지면 콜라이더(따로 만든 물리 씬)
                 GlareHeatmap.cs         격자 집계, 히트맵·광선 메시, 색
      Editor/    GlareReflectionSimEditor.cs   Scene 창·Inspector 패널(빌드에는 안 들어가요)
      Resources/ GlareVertexColor.shader       표시용 셰이더(WebGL 빌드에도 들어가요)

## v7에서 옮겨올 때 (이전에 반사광 파일을 넣었다면)

1. 이전 반사광 파일을 지워요. 안 지우면 같은 이름이 두 개라 컴파일 에러가 나요.
   - Assets/Scripts: GlareOptics.cs, GlareEngine.cs, GlareReceivers.cs, GlareHeatmap.cs, GlareReflectionSim.cs
   - Assets/Editor: GlareReflectionSimEditor.cs
   - Assets/Resources: GlareVertexColor.shader
2. LouverRig.cs, SunLouverController.cs는 루버 담당자의 최신본을 쓰면 돼요.
   v7 때 제가 고친 버전이어도 상관없어요. 반사광은 그 추가 기능을 쓰지 않아요.
3. GlareSim 폴더를 Assets에 넣어요.
4. 스크립트를 지웠다 넣으면 GlareSim 오브젝트의 컴포넌트가 "Missing Script"가 돼요.
   그 컴포넌트를 지우고 GlareReflectionSim을 다시 Add Component 해요. Receiver Roots 같은 칸도 다시 넣어요.

## 붙이는 순서

1. 빈 오브젝트 "GlareSim"에 GlareReflectionSim을 붙여요.
2. 연결 칸(Louver Controller, Louver Rig, Louver Root Override, Sun Override)은 비워 두면 이름으로 자동으로 찾아요.
   못 찾으면 해당 컴포넌트나 오브젝트를 끌어다 놓아요.
3. Receiver Roots에 주변 건물 모델의 부모를 넣어요. 바로 아래 자식 하나가 건물 하나로 집계돼요.
   비워 두면 씬의 메시 전부를 써요. 이때 우리 건물 범위 안에 있는 것은 빼요.
   하늘 돔, 큰 바닥판처럼 광선을 막는 것은 Exclude Roots에 넣어요.
   대지·도로·녹지처럼 광선은 막아야 하지만 결과에는 넣지 않을 것은 Blocker Roots에 넣어요.
   - 광선은 거기서 멈추고, 많이 받는 곳·히트맵·CSV·"주변에 닿음"에는 안 들어가요. 패널에는 "대지 등에 막힘"으로 따로 나와요.
   - Receiver Roots 아래에 있는 것은 받는 쪽으로 봐요(예: Blocker = 대지 전체, Receiver = 그 안의 주변건물).
4. 대지 모델에 바닥이 있으면 Add Ground를 꺼요.
5. 재생하고 Console을 확인해요.
   - `[Glare] 루버 연결: 컨트롤러 Manager, 날 부모 ..., 해 Directional Light | 날 17514장 (동 …, 남 …, 서 …, 북 …) | 하루 누적 가능`
   - `[Glare] 받는 쪽 콜라이더 …개, 그룹 …개`
6. 패널은 Game 창 오른쪽 위(G 키), Scene 창 오른쪽, GlareSim Inspector 맨 위에 나와요.

## 루버 쪽에서 읽는 것 (약속)

| 루버 쪽 | 이름 | 쓰는 곳 |
|---|---|---|
| SunLouverController | `day`, `hour`, `externalControl`, `sun`, `rig` | 시각 표시, 하루 누적(시각 돌리기), 해 |
| SunLouverController (있으면) | `data` 또는 `IsReady`, `winterMode`, `northLouver`, `useFloorSchedule` 등 | CSV 로드 확인, 하루 누적을 다시 계산할지 판단 |
| LouverRig | `louverRoot`, `skipNameContains` | 날 찾기 |
| LouverRig (있으면) | `userHidden` (입면별 bool[4]) | 숨긴 날(북면 루버 끔). LouverPerformance가 카메라 반대쪽 면을 끈 것은 날이 있다고 봐요 |
| 씬 | louverRoot의 직계 자식(날 하나씩)의 회전 (`userHidden`이 없으면 켜짐도) | 날 각도 |
| 씬 | 해 Light의 방향·켜짐 | 해 방향, 해 없음 |

- 루버 담당자가 위 이름을 바꾸면 **LouverSceneLink.cs 맨 위의 상수만** 고치면 돼요.
- 못 찾아도 컴파일 에러는 나지 않아요. Console에 경고가 뜨고 그 기능만 꺼져요.
  - 예: hour를 못 찾으면 하루 누적이 꺼져요.
- 날 각도는 계산 규칙(겨울 모드, 층별 CSV 등)을 다시 계산하지 않고, 날이 실제로 돌아간 회전을 읽어요.
  그래서 규칙이 바뀌어도 화면에 보이는 그대로 계산해요.
- 재생을 시작할 때 날 상태를 기준으로 잡아요. LouverRig 기본 동작처럼 처음에 열림(0°)이어야 해요.

## 동작

- **현재 시각**: 화면에 보이는 해와 날 각도 그대로 계산해요.
  - 0.2초마다 바뀌었는지 확인해요. 그래서 웹, 슬라이더, 자동 재생을 모두 따라가요.
- **경우 선택**: 패널에서 4가지 중 하나를 골라요.
  - 반응형(내부 루버): 지금 날 각도 + 날 바깥에 외측 유리가 있어요. 씬의 `외측 저반사유리`가 보여요.
  - 반응형(외부 루버): 지금 날 각도, 외측 유리는 없어요. 씬의 `외측 저반사유리`를 꺼요.
  - 모두 열림(0°), 루버 없음: 외측 유리 없이 계산하고 `외측 저반사유리`를 꺼요.
  - 루버 없음: 날 부모(`수직 루버`) 아래를 화면에서 숨겨요. 오브젝트는 그대로 두고 렌더러만 꺼서 루버 컨트롤러와 계산에는 영향이 없어요.
  - 끔 모드에서는 `외측 저반사유리`와 루버를 원래대로 둬요.
- **하루 누적 / 4가지 비교**: 컨트롤러의 hour를 04:00~21:00 사이에서 10분씩 바꿔 가며, 루버와 해가 실제로 돌아간 상태로 계산해요.
  - 끝나면 원래 시각으로 돌려놔요.
  - 계산 중에는 루버와 해가 하루 동안 움직이는 게 보여요.
  - Console에 컨트롤러의 시각 로그가 많이 떠요. 루버 스크립트가 찍는 로그라 정상이에요.
  - 계산 중에 다른 모드를 누르거나 GlareSim을 끄면 바로 원래 시각으로 돌아가요.
- **4가지 비교**: 루버 없음 / 모두 열림(0°) / 반응형(외부 루버) / 반응형(내부 루버)를 같은 하루로 한 번에 계산해요.
  결과는 패널과 Console에 표로 나와요.
- **CSV 저장**: 프로젝트 폴더/GlareResults에 칸별 값과 건물별 합계를 저장해요. WebGL에서는 안 돼요.
- **색**: 보라(약함) → 빨강 → 주황 → 노랑(셈). 최대값은 상위 2% 값으로 자동으로 맞춰요.
- **광선**: 하늘색 = 유리 반사, 주황색 = 날 반사, 연두색 = 외측 유리 반사(반응형 내부 루버).

## 계산 방식

- 날마다(2장마다, 층 높이를 2점으로) 점을 두고 다음 순서로 계산해요.
  1. 해가 입면 앞에 있는지, 주변 건물 그늘인지 Unity 모델로 확인해요.
  2. 그 날의 각도로 날 배열을 위에서 본 2D로 계산해요.
     - 유리에 해가 드는 비율
     - 유리 반사가 날 사이로 빠져나가는 비율
     - 날 면에 해가 드는 폭
     - 날 반사가 옆 날에 막히지 않고 나가는 폭
  3. 반사량을 구해요.
     - 유리 반사 = 직달일사 × cos(입사각) × 유리 반사율(입사각에 따라 커져요) × 빠져나가는 비율
     - 날 반사 = 직달일사 × cos(날 입사각) × 날 정반사율 × 빠져나가는 폭 ÷ 날 간격
     - 반응형(내부 루버)일 때만:
       - 외측 유리 반사 = 직달일사 × cos(입사각) × 외측 유리 반사율(입사각에 따라 커져요). 날에 막히지 않아요.
       - 위 유리·날 반사에 외측 유리 투과율을 들어올 때 한 번, 나갈 때 한 번 곱해요(투과율도 입사각에 따라 줄어요).
  4. 반사 방향으로 광선을 쏴서 처음 닿는 주변 표면의 2 m 칸에 쌓아요.
- 직달일사는 맑은 날 모델이에요(고도 90°에서 약 950, 30°에서 약 770 W/m²). 기상 데이터가 있으면 Dni Override에 넣어요.
- 노출 시간은 받는 면에서 Exposure Threshold(기본 50 W/m²) 이상인 10분을 더한 시간이에요.
  기본값은 임의로 정한 값이니 프로젝트 기준에 맞게 바꾸세요.
- v7에 있던 "층별 CSV로 그늘 판단(FloorCsv)" 옵션은 뺐어요. 그 CSV는 루버 쪽 데이터라서, 그늘은 Unity 모델로만 확인해요.

## 가장 중요한 두 값

- **Glass Reflectance**: 유리 사양서의 외부 반사율(수직 입사)
  - 일반 복층 0.10~0.20
  - 반사유리 0.30 이상
- **Blade Specular**: 날 표면의 거울 같은 반사분(기본 0.2). 결과가 이 값에 가장 민감해요.
  - 무광 도장 0.05~0.1
  - 반광 도장 0.1~0.3
  - 아노다이징·금속 0.3~0.6
- **Outer Glass Reflectance / Outer Glass Transmittance**: 반응형(내부 루버)에서만 써요.
  외측 저반사유리 사양서의 외부 반사율과 일사 투과율을 넣어요. 기본값(0.06 / 0.8)은 임의로 정한 값이에요.

가상 박스 건물(55 m × 55 m, 28층)로 하지 하루를 돌린 시험에서 주변에 닿은 반사는 이렇게 나왔어요.

| 경우 | 주변에 닿은 반사 |
|---|---|
| 루버 없음 | 약 11,800 kWh |
| 모두 열림 | 약 4,100 kWh |
| 반응형, 날 정반사율 0.1 / 0.2 / 0.4 | 약 3,400 / 6,300 / 12,300 kWh |

날 정반사율이 약 0.4를 넘으면 반응형이 유리만 있을 때보다 불리해요. 실제 모델 결과와는 다르니 경향만 보세요.

## 웹 연결 (선택)

- 결과 JSON: 계산이 끝날 때마다 Last Result Json 칸과 On Result Json(string) 이벤트로 나와요.
- 부를 수 있는 함수:
  - `SetMode("off" | "instant" | "daily")`
  - `SetLouverCase("inner" | "outer" | "open" | "none")` ("responsive"는 "inner"로 봐요)
  - `Recompute()`
  - `RunCompare()`

## 빌드 주의

- 주변 건물 FBX의 Import Settings → Model → Read/Write를 켜야 빌드(WebGL)에서 콜라이더를 만들 수 있어요.
- WebGL 패널에서 한글이 안 보이면 Gui Font에 한글 폰트를 넣어요.
- 셰이더는 Built-in, URP에서 돼요. HDRP는 안 돼요.
- 느리면 Blade Stride를 3~4로 올리거나 Samples Per Blade Height를 1로 줄여요.

## 계산에서 빠진 것

- 한 번 튄 빛만 봐요. 여러 번 반사, 날의 확산 반사, 하늘빛 반사는 빠져요.
- 유리는 날 바로 뒤에 평평하게 있다고 봐요. 루버가 없는 부분(저층부 유리 등)은 계산하지 않아요.
- 반응형(내부 루버)에서 외측 유리와 날·안쪽 유리 사이를 오가는 반사, 캐비티 그레이팅에 막히는 빛은 빠져요.
- 날 폭·간격은 Inspector 값(0.30 m, 0.35 m)을 써요. 루버 CSV의 날폭·날간격과 같게 맞춰 주세요.

## 확인한 것

Unity 없이 C# 9 컴파일러와 Unity API 대역(stub)으로 확인했어요.

- 이 폴더만 따로(루버 스크립트 없이) 에디터용·WebGL용으로 컴파일돼요.
- 날 각도를 읽는 결과
  - LouverRig처럼 피벗을 돌렸을 때 오차 0°
  - FBX 축 보정 회전이 있을 때도 오차 0°
  - LouverFacade의 각도 방향과 같아요.
- 컨트롤러 이름 읽기·시각 돌리기·원래대로 돌리기가 돼요.
- 계산 결과가 v7과 같아요.

Unity에서 직접 돌려 보지는 못했어요. 에러가 나면 Console 메시지를 알려 주세요.
