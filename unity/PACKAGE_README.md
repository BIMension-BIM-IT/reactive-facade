# Unity 전달용 v9 (10/12)

**처음 읽을 것: GUIDE_Two_Builds.md** (1784 빌드와 박스 건물 빌드, 두 개를 만드는 방법)

v9은 v8에 겨울 모드(혼합 규칙 + 기온 기준 자동 판정)를 넣은 최종본이에요. 이 zip 하나만 쓰면 돼요(이전 zip은 쓰지 마세요).
v8에서 바뀐 점: 겨울 모드가 '12~2월 0° 고정'에서 '난방이 필요한 날에 해와 나란히 + 눈부심 시각만 0°'로 바뀌었어요(아래 3번).
새 파일: Scripts\WinterCalendar.cs, StreamingAssets\winter_calendar.csv

## 1. 넣는 법 (시각화 담당, 약 10분)
1. Scripts 안의 .cs 파일 전부를 Assets/Scripts(지금 쓰는 폴더)에 덮어써요.
2. StreamingAssets의 CSV 4개를 Assets/StreamingAssets(맨 위 Assets 바로 아래)에 넣어요.
   sun_louver.csv, louver_angle_by_floor.csv, horizontal_angle_by_floor.csv, winter_calendar.csv
3. 웹 빌드를 쓸 때만: Plugins/WebGL/LouverState.jslib를 Assets/Plugins/WebGL에 넣어요.
4. 재생해서 Console에 에러가 없는지 봐요. 에러가 나면 메시지를 그대로 보내 주세요.
   확인 메시지: "층별 CSV 층 수: 28", 수평 모델을 연결했다면 "수평 루버 층별 CSV 층 수: 28"

## 2. 씬 구성 (오브젝트 -> 붙일 스크립트)
| 오브젝트 | 스크립트 | 설정 |
|---|---|---|
| Manager | SunLouverController | Sun = Directional Light, Rig = 수직 루버 리그, Rig Horizontal = 수평 리그(있을 때) |
| 수직 루버 리그 | LouverRig | Louver Root = 수직 날 부모, Orientation = Vertical, Floor Height M = 4.2 |
| 수평 루버 리그 | LouverRig | Louver Root = 수평 날 부모, Orientation = Horizontal, Floor Height M = 4.2 |
| LouverBridge | LouverBridge(웹 담당) + LouverBridgeGlue | 칸은 비워 두면 자동으로 찾아요 |
| 빈 오브젝트 | LouverOrientationReceiver | 이름이 자동으로 "LouverOrientation"이 돼요 |
| 빈 오브젝트(웹 빌드) | LouverStateReporter | 유니티 화면 버튼의 상태를 웹에 보내요 |
| 빈 오브젝트(시점 버튼) | CameraPresets | 시점마다 빈 오브젝트를 두고 이름과 함께 등록 |
| 빈 오브젝트(성능) | LouverPerformance | 노이즈·끊김 줄이기(아래 5번). Controller 칸은 비워 두면 자동으로 찾아요 |
- 수직 모델과 수평 모델 둘 다 씬에서 켜 둔 채로 저장하세요(시작할 때 날 위치를 계산한 뒤 고른 쪽만 남기고 숨겨요).
- LouverRig는 이름에 "고정, 스크린, 각파이프, 기둥"이 들어 있으면 항상 돌리지 않아요(Inspector를 안 고쳐도 돼요).
- 수평 모델은 날을 줄 단위로 합친 것(층_입면_번호 이름)을 쓰세요. 안 합친 5만 개는 웹에서 너무 무거워요.

## 3. 화면 버튼 · 슬라이더 연결표
| 화면 요소 | 연결할 함수 | 인자 |
|---|---|---|
| 시각 슬라이더(5~20) | SunLouverController.SetHour(float) | Slider의 Dynamic float |
| 재생/일시정지 | SunLouverController.TogglePlay() | |
| 재생 속도 | SunLouverController.SetPlaySpeed(float) | 1초에 흐르는 시간(예: 1) |
| 대표일 버튼 4개 | SunLouverController.SetDayByName(string) | 춘분 / 하지 / 추분 / 동지 |
| 대안 버튼 3개 | SunLouverController.SetAlternative(int) | 0 시공 전, 1 외부 고정 0°, 2 외부 반응형 |
| 수직/수평 | LouverOrientationReceiver.SetOrientation(string) | vertical / horizontal |
| 겨울 모드 체크 | SunLouverController.SetWinterMode(bool) | Toggle의 Dynamic bool |
| 시연 장면 버튼 | SunLouverController.ApplyScene(string) | 하지정오 / 하지오후 / 동지정오 / 동지정오_규칙 |
| 시점 버튼 | CameraPresets.GoToName(string) | 등록한 시점 이름 |
- 시공 전 = 루버가 사라짐, 고정 0° = 날이 항상 열림, 반응형 = 우리 규칙.
- 겨울 모드는 끔/켬 두 가지예요. 켜면 winter_calendar.csv(성남 기준 7일 평균 외기온도 12°C 미만 = 겨울, 약 10/30~4/17)의 겨울 날짜에만 겨울 전략을 써요:
  해와 나란히 눕히고(각도 = -θ), 해 고도 20° 미만이고 방위차 60° 이내인 눈부심 시각, 방위차 75° 초과인 면은 0°. 겨울이 아닌 날은 일반 규칙이에요.
  파일이 없으면 11~3월을 겨울로 봐요. 켜지 않으면 연중 일반 규칙(겨울에도 규칙대로 닫음)이에요.
  수평 루버의 겨울 각도(해와 나란히 = 바깥 끝을 프로파일각만큼 위로)는 Radiance로 검증했어요(난방기 11~3월 일사 손실, 시공 전 대비: 남 10%, 동 24%, 서 25%).
- 화면 버튼이 웹이 보낸 날짜·시각보다 우선해요(웹이 다시 보내면 웹이 다시 우선).

## 4. 웹 연결
- 웹 -> 유니티: LouverBridgeGlue가 LouverBridge의 이벤트(site_input, 날짜·시각)를 SunLouverController에 연결해요.
  시험: LouverBridge 컴포넌트 ⋮ 메뉴 "시험: 1784 하지 정오" -> Console에 "[LouverBridgeGlue] site_input ..." 과 "[SunLouver] 웹 시각 ... 하지 12.0시".
- 수직/수평 선택(웹이 부르는 법): unityInstance.SendMessage('LouverOrientation', 'SetOrientation', 'horizontal' 또는 'vertical')
- 유니티 -> 웹: LouverStateReporter가 아래 JSON을 window.LouverBridge.receiveState(state)로 보내요(웹이 받는 함수를 만들어야 해요).
    {"source":"unity","sim_datetime":"2026-06-21T12:30:00+09:00","day_name":"하지","hour":12.50,
     "alternative":"reactive","orientation":"vertical","winter_mode":false,"playing":false}
  웹이 보낸 값으로 바뀐 경우에는 되돌려 보내지 않아요(되풀이 방지). 웹 쪽 예제:
    window.LouverBridge = window.LouverBridge || {};
    window.LouverBridge.receiveState = function (s) { fromUnity = true; setSimDateTime(s.sim_datetime);
      setAlternative(s.alternative); setOrientation(s.orientation); setWinterMode(s.winter_mode); fromUnity = false; };
- WebGL 빌드 설정: Compression Format = Disabled, .data 파일 100 MB 미만(GitHub 제한).


## 4-2. 날이 많아서 생기는 노이즈 · 끊김 줄이기 (LouverPerformance + 직접 바꿀 설정)
LouverPerformance를 빈 오브젝트에 붙이면 자동으로 해요:
- MSAA 4x와 프레임 제한(60), 해 그림자 Bias 조정 -> 멀리서 번쩍이는 줄무늬와 그림자 줄무늬 줄이기
- 외관 뷰에서는 날의 그림자 그리기를 끄고, 실내 뷰에서는 켜요(실내 뷰로 바뀔 때 SetInteriorView(true) 또는 OnViewChanged("interior")를 불러 주세요.
  LouverBridge에 onView 이벤트가 있으면 Inspector에서 OnViewChanged에 연결하면 돼요)
- 카메라에서 보이지 않는 면의 날을 꺼서 그리는 양을 줄여요(카메라가 건물 바로 위나 안이면 끄지 않아요)
직접 바꿔야 하는 설정(Project Settings):
- URP Asset: Anti Aliasing(MSAA) 4x, Shadows의 Max Distance 200~300, Cascade Count 2~4, Shadow Resolution 2048
- 날 FBX Import: Read/Write 끄기, Tangents None, Generate Lightmap UVs 끄기, Mesh Compression 켜기
- Player Settings(WebGL): Initial Memory Size 256~512 MB, Compression Format = Disabled
- SRP Batcher는 켜 두세요(기본값)
- 그래도 끊기면 객체 수를 줄이세요(수평은 줄 단위 합치기, 수직은 같은 자리 위아래 3개 층 묶기)

## 5. 일반 건물(직육면체 매싱)용 계산 코드
Scripts\SunPosition.cs, Scripts\LouverRule.cs: 유니티에 의존하지 않는 순수 수학(해 위치, 루버 규칙).
정답표 LouverRule_TestVectors.csv와 같은 값이 나오는지 확인하세요.
  SunPosition.Compute(lat, lon, 9.0, dt, out alt, out az);
  double cut = LouverRule.CutoffForDate(cutoffDeg, dt.Month, dt.Day);
  bool closed = LouverRule.VerticalAngle(alt, az, facadeAzimuthDeg, cut, bladeDepthMm, bladeSpacingMm, out double angle);
  if (winterMode && winterCal.IsWinter(dt.Month, dt.Day)) angle = LouverRule.WinterVertical(alt, az, facadeAzimuthDeg);   // 겨울 모드
  angle = LouverRule.Unwrap180(prevAngle, angle);
  // 회전: Quaternion.AngleAxis(-angle, Vector3.up), 0 = 열림, + = 바깥 끝이 바깥에서 볼 때 오른쪽
방위는 북 0, 동 90, 남 180, 서 270. 수평 루버는 LouverRule.HorizontalTilt(...)(기울기 + = 바깥 끝이 아래로).

## 5-2. 직접 계산 모드 (1784가 아닌 건물, 좌표를 찍은 네모 건물)
SunLouverController에 추가된 모드예요. 켜면 sun_louver.csv 없이 위도·경도로 해 위치를 계산하고, 수직·수평 루버 각도도 바로 계산해요.
- 켜는 법: 인스펙터의 `Direct Calc` 체크, 또는 코드/웹에서 SetDirectMode(true).
- 입력: `Latitude`, `Longitude`, `Time Zone Hours`(한국 9), `Building Yaw Deg`(건물이 정북에서 시계 방향으로 돌아간 각도).
  모델은 북쪽 = +Z로 반듯하게 두고 건물 회전은 이 값만 넣으세요(해 방향을 그만큼 돌려서 보여 줘요).
- 날 사양: `Blade Depth Mm`(300), `Blade Spacing Mm`(350), 컷오프 4개(30/60/45/30), 수평 컷오프 15°. 모두 인스펙터에서 바꿀 수 있어요.
- 수직 · 수평은 평소와 같이 SetOrientation으로 고르고, 수직 모델은 `Rig`, 수평 모델은 `Rig Horizontal`에 연결해요.
- 웹에서 부르는 법(LouverOrientation 오브젝트):
    SendMessage('LouverOrientation', 'SetSiteMode', 'direct');            // 직접 계산 (1784로 돌아가기: '1784')
    SendMessage('LouverOrientation', 'SetLocation', '37.5665,126.9780');   // 위도,경도
    SendMessage('LouverOrientation', 'SetBuildingYaw', '15');             // 건물 방위 회전(°)
  날짜·시각은 기존 SetDateTime(웹 연결)을 그대로 써요.
- **자동 전환(LouverBridgeGlue, 기본은 꺼짐: 박스 건물 생성기를 붙인 뒤 `Auto Switch Site`를 켜세요)**: 웹이 보내는 `OnSiteInput`의 위도·경도가 1784(정자일로 95)에서 300 m보다 멀면 직접 계산 모드로 자동 전환돼요(가까우면 CSV 모드).
  직접 계산이 켜지면 좌표, 날 폭·간격, 컷오프를 웹 값으로 바꾸고, 입면 방위들에서 건물 회전각을 자동으로 구해요(직사각형·ㄱ·ㄷ자). 건물 기준 북쪽 입면의 "루버 없음"도 반영해요.
  비스듬한 입면(직각에서 15° 넘게 어긋남)이 있으면 Console에 경고가 나오고, 그 입면은 가장 가까운 방향 날로 근사돼요.
- **시각화 담당 설정(박스 모델 자동 생성)**: LouverBridgeGlue의 `On Direct Mode Changed(bool)` 이벤트에 1784 모델과 박스 모델의 SetActive를 연결하세요(true = 박스, false = 1784).
  박스 모델의 날 이름은 `층_입면_번호`(예: 5_S_001, 입면 = E/S/W/N)로 만들어 주세요. 이름이 있으면 위치로 입면을 추측하지 않아서 ㄱ자의 안쪽 면도 맞게 돌아가요.
  입면 E/S/W/N은 건물 회전 전 기준(남쪽 = 건물이 +Z 반대를 보는 면)이에요. 모델은 반듯하게 만들고 회전은 코드가 맡아요.
- 직접 계산에서는 주변 건물 그림자(층별 표)를 반영하지 않아요(모든 층 같은 각도). 겨울 모드는 켜면 성남 기준 날짜표를 써요(수도권은 비슷하고, 다른 지역은 정확하지 않을 수 있어요).
- 확인: 성남 좌표로 1784 CSV 1,160건과 비교해서 해 위치 차이는 최대 약 1°, 각도 차이는 최대 6.4°(컷오프 근처), 열림/닫힘이 다른 건 4건이에요. 정확히 같지 않은 이유는 태양 계산식이 Ladybug와 약간 달라서예요.

## 6. 확인된 것과 아직 확인 안 된 것
- 확인됨: 전체 스크립트 컴파일 에러 0개(Unity 6000.6.4f1 참조 DLL, UNITY_WEBGL 분기 포함). 규칙 계산은 1784 CSV 870건과 일치.
- 아직 안 됨: 유니티 에디터에서 실행, WebGL 빌드와 웹 연결, 수평 모델 연결. 에러나 이상한 동작은 Console 메시지를 알려 주세요.
- 수평 컷오프 15°는 제안값(팀 확정 전). 확정되면 각도 표만 다시 만들어 드려요.
- 변경 이력은 CHANGELOG_v4_to_v7.md에 있어요.
