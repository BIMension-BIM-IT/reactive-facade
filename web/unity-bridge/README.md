# 웹 ↔ Unity 연결 스크립트

담당: 총괄 · 웹 · 보고서

웹 검토 도구가 Unity WebGL 화면(네이버 1784)과 주고받는 데 쓰는 스크립트입니다. Unity 프로젝트에 넣고 WebGL로 빌드합니다.
지금 사이트의 빌드(10/15 빛반사 v4)에 들어 있는 것과 같은 기준이며, Unity 담당자가 빌드하면서 고친 부분이 있으면 그쪽이 최신입니다.

| 파일 | 넣는 곳 | 하는 일 |
|---|---|---|
| `Scripts/LouverBridge.cs` | `Assets/unity-bridge/Assets/Scripts/` | 웹 → Unity: 건물 정보(`OnSiteInput`), 날짜 · 시각(`SetDateTime`), 화면(`SetView` 외관 · 실내 · 나란히), 주변 건물(`OnContext`), 빛반사(`SetGlare`, `SetGlareCase`). Unity → 웹: 준비 알림, 일사 결과, 빛반사 결과 |
| `Plugins/WebGL/LouverBridge.jslib` | `Assets/Plugins/WebGL/` | Unity → 웹 함수 (`LouverNotifyReady`, `LouverSendSolarResult`, `LouverGlareAvailable`, `LouverSendGlareResult`) |
| `Scripts/LouverBridgeGlue.cs` | 위와 같은 폴더 | 팀 루버 컨트롤러와 브리지 연결. 함수 이름 `HandleSiteInput` · `HandleDateTime` (`OnSiteInput` 이면 웹 SendMessage 가 겹쳐 오류) |
| `Scripts/LouverViewSetup.cs` | 위와 같은 폴더 | 외관 · 실내 카메라 자동 생성, 입면 보기(`LookAtFacade`), 나침반(지름 85 px), 외관 조작(왼쪽 끌기 · 휠) |
| `Scripts/LouverInteriorLook.cs` | 위와 같은 폴더 | 실내 조작을 외관과 같게: 왼쪽 끌기 · 휠, 휴대폰 한 손가락 · 두 손가락 (10/16, 다음 빌드에 넣을 것) |

- 씬에 따로 붙이지 않아도 `LouverViewSetup` · `LouverInteriorLook` 은 스스로 생깁니다. `LouverBridge` 는 씬의 빈 오브젝트에 붙입니다 (이름은 자동으로 "LouverBridge").
- 빛반사(GlareSim)는 씬에 켜 두면 `LouverBridge` 가 시작할 때 끄고 패널을 숨깁니다. 웹의 "빛반사 보기" 로 켜고 끕니다.
- Unity 6000.6.4f1 에서 컴파일 오류 없는 것을 확인했습니다.

## 웹이 하는 일 (참고)

- 1784 를 볼 때만 건물 정보 · 주변 건물을 Unity 로 보냅니다. 다른 건물을 볼 때는 날짜 · 시각만 보내고, 실내 화면은 1784 실내를 예시로 보여 줍니다.
- 다른 건물의 외관만 볼 때는 숨겨진 Unity 를 멈춰 웹 3D 가 버벅이지 않게 합니다.
