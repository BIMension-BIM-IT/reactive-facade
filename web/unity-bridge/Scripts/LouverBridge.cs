// 반응형외피 웹 ↔ Unity 연결
// - 웹이 보내는 것: site_input(JSON) → OnSiteInput, 날짜·시각(ISO 문자열) → SetDateTime,
//   화면(exterior 외관 / interior 실내 / split 나란히) → SetView
// - Unity가 보내는 것: solar_result(JSON) → SendSolarResult(...)
// - 반사광(GlareSim, 있으면): 웹 → SetGlare("off" | "instant" | "daily"), SetGlareCase("inner" | "outer" | "open" | "none")
//   Unity → 웹: 켤 수 있는지(LouverGlareAvailable), 계산 결과 JSON(LouverSendGlareResult)
// 형식은 팀 문서 「반응형외피 데이터 형식」과 같다.
//
// 사용법
// 1) 씬에 빈 오브젝트를 하나 만들고 이 스크립트를 붙인다 (오브젝트 이름은 자동으로 "LouverBridge"가 된다).
// 2) Inspector의 onSiteInput / onDateTime 이벤트에 기존 루버 제어 스크립트의 함수를 연결한다.
//    (또는 LouverBridgeExample.cs 처럼 코드로 연결)
// 3) 연간 일사량 계산이 끝나면 SendSolarResult(result) 를 호출한다.

using System;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Events;

// ---------- 웹 → Unity: site_input ----------
[Serializable]
public class SiteInfo
{
    public string address; public double lat_deg; public double lon_deg;
    // 지도에서 고른 건물 외곽 (없으면 빈 배열). 마지막 닫는 점 없음, 한 줄 배열 [x0, y0, x1, y1, …]
    public double[] footprint_lonlat;   // 경도, 위도
    public float[] footprint_en_m;      // 건물 중심 기준 m (동 +, 북 +). Unity 로는 x = e, z = n
}

[Serializable]
public class FacadeInfo
{
    public string id;             // 입면 이름 (E, S, W, SE1 …)
    public float azimuth_deg;     // 입면이 바라보는 방위 (북 0, 동 90, 남 180, 서 270)
    public float width_m;         // 입면 폭 (없으면 0)
    public float height_m;        // 입면 높이 (없으면 0)
    public float wwr;             // 창면적비 0~1
    public float window_area_m2;  // 창 면적
    public bool louver;           // 이 입면에 루버를 다는지
}

[Serializable]
public class LouverSpec
{
    public float blade_depth_mm = 300;
    public float blade_spacing_mm = 350;
    public float louver_to_window_ratio = 0.9f;
    public float area_per_unit_m2 = 6;
    public float[] cutoff_deg = { 30, 60, 45, 30 }; // 춘분 · 하지 · 추분 · 동지
    public float min_sun_alt_deg = 5;
}

[Serializable]
public class SiteInput
{
    public SiteInfo site;
    public string project_type;   // new / remodel
    public bool winter_mode;      // 겨울 모드 (12~2월 햇빛과 나란히)
    public string sim_datetime;   // 예: 2026-06-21T12:30:00+09:00
    public FacadeInfo[] facades;
    public LouverSpec louver_spec;
}

// ---------- 웹 → Unity: 주변 건물 (그림자용, OnContext) ----------
// 기준점 = 고른 건물 중심 (site.footprint_en_m 와 같은 기준). 높이는 땅에서부터 (층수 없으면 3층 × 3.3 m)
[Serializable]
public class ContextBuilding
{
    public string id;
    public int floors;               // 0 = 모름
    public float height_m;
    public float[] footprint_en_m;   // [e0, n0, e1, n1, …] m
}

[Serializable]
public class ContextInput
{
    public double origin_lat_deg;
    public double origin_lon_deg;
    public ContextBuilding[] buildings;
}

// ---------- Unity → 웹: solar_result ----------
[Serializable]
public class SeasonValues
{
    // 입면 1㎡당 기간 합계 일사량 (kWh/㎡). bare = 통유리, 나머지는 대안별로 루버를 지난 값
    public float bare_kwh_m2_yr;
    public float internal_reactive_kwh_m2_yr;
    public float external_fixed_kwh_m2_yr;
    public float external_reactive_kwh_m2_yr;
}

[Serializable]
public class FacadeSolar
{
    public string id;                                          // site_input 의 입면 id 와 같게
    public SeasonValues cooling_season = new SeasonValues();   // 냉방기 6~9월
    public SeasonValues heating_season = new SeasonValues();   // 난방기 12~2월
    public float closed_hours_yr;                              // 외부 반응형 루버가 닫힌 연간 시간
    public float blade_rotation_deg_yr;                        // 날 하나의 연간 회전량 (°)
}

[Serializable]
public class SolarMeta
{
    public string weather_source = "unity";   // 예: KMA_서울_TMY
    public bool shading_included = true;      // 주변 건물 그림자 반영 여부
    public int timestep_min = 60;
    public string version = "unity-0.1";
}

[Serializable]
public class SolarResult
{
    public SolarMeta meta = new SolarMeta();
    public FacadeSolar[] facades;
}

public class LouverBridge : MonoBehaviour
{
    [Serializable] public class SiteInputEvent : UnityEvent<SiteInput> { }
    [Serializable] public class DateTimeEvent : UnityEvent<DateTime> { }

    [Tooltip("웹에서 건물 · 입면 정보가 올 때")] public SiteInputEvent onSiteInput = new SiteInputEvent();
    [Tooltip("웹에서 날짜 · 시각이 올 때 (한국 시간)")] public DateTimeEvent onDateTime = new DateTimeEvent();

    [Serializable] public class ContextEvent : UnityEvent<ContextInput> { }
    [Tooltip("웹에서 주변 건물이 올 때 (건물을 고르고 몇 초 ~ 40초 뒤)")] public ContextEvent onContext = new ContextEvent();
    public ContextInput Context { get; private set; }

    [Serializable] public class ViewEvent : UnityEvent<string> { }

    [Header("화면 (웹의 외관 / 실내 / 나란히 버튼)")]
    [Tooltip("건물 밖에서 보는 카메라")] public Camera exteriorCamera;
    [Tooltip("실내(예: 9층 서남쪽 모서리 방)에서 창 쪽을 보는 카메라")] public Camera interiorCamera;
    [Tooltip("화면이 바뀔 때 (카메라 말고 UI 등을 바꿀 때)")] public ViewEvent onView = new ViewEvent();
    public string CurrentView { get; private set; } = "exterior";

    public SiteInput Current { get; private set; }
    public DateTime CurrentTime { get; private set; } = new DateTime(2026, 6, 21, 12, 0, 0);

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void LouverSendSolarResult(string json);
    [DllImport("__Internal")] private static extern void LouverNotifyReady();
    [DllImport("__Internal")] private static extern void LouverGlareAvailable(int on);
    [DllImport("__Internal")] private static extern void LouverSendGlareResult(string json);
#else
    private static void LouverSendSolarResult(string json) { Debug.Log("[LouverBridge] solar_result → 웹: " + json); }
    private static void LouverNotifyReady() { Debug.Log("[LouverBridge] 준비됨"); }
    private static void LouverGlareAvailable(int on) { Debug.Log("[LouverBridge] 반사광 " + (on != 0 ? "있음" : "없음")); }
    private static void LouverSendGlareResult(string json) { Debug.Log("[LouverBridge] 반사광 결과 → 웹: " + json); }
#endif

    // 웹은 SendMessage('LouverBridge', ...) 로 부르므로 오브젝트 이름을 고정한다
    private void Awake() { gameObject.name = "LouverBridge"; }

    // Unity가 다 뜨면 웹에 알린다 → 웹이 현재 건물 · 시각을 보내 준다
    private void Start() { SetView(CurrentView); HookGlare(); LouverNotifyReady(); }

    // ---------- 반사광 (GlareSim) ----------
    // GlareReflectionSim 을 이름으로 찾아서 메시지만 넘긴다 → GlareSim 폴더가 없는 빌드에서도 컴파일된다
    MonoBehaviour glare;
    void HookGlare()
    {
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (mb != null && mb.GetType().Name == "GlareReflectionSim") { glare = mb; break; }
        if (glare != null)
        {
            // 웹에서는 Unity 패널 · 태양 궤적을 숨기고(웹 버튼으로만 켜고 끔), 처음엔 꺼 둔다 → 시작할 때 계산하지 않음
            var t = glare.GetType();
            var gui = t.GetField("showGui"); if (gui != null) gui.SetValue(glare, false);
            var ev = t.GetField("onResultJson")?.GetValue(glare) as UnityEvent<string>;
            if (ev != null) ev.AddListener(json => LouverSendGlareResult(json));
            SetGlare("off");
        }
        LouverGlareAvailable(glare != null ? 1 : 0);
    }

    // 웹 → Unity: 반사광 끄기 · 지금 시각 · 하루 누적
    public void SetGlare(string mode)
    {
        if (glare == null) { Debug.LogWarning("[LouverBridge] GlareSim 이 씬에 없어요 (GlareSim 오브젝트를 켜 주세요)"); return; }
        var path = glare.GetType().GetField("showSunPath"); if (path != null) path.SetValue(glare, mode != "off");
        glare.SendMessage("SetMode", mode, SendMessageOptions.DontRequireReceiver);
    }

    // 웹 → Unity: 반사광 계산 경우 (inner 반응형 내부 · outer 반응형 외부 · open 모두 열림 · none 루버 없음)
    public void SetGlareCase(string c)
    {
        if (glare != null) glare.SendMessage("SetLouverCase", c, SendMessageOptions.DontRequireReceiver);
    }

    // 웹 → Unity: 화면 바꾸기. exterior = 외관, interior = 실내, split = 왼쪽 외관 · 오른쪽 실내
    // 두 카메라를 Inspector에 넣으면 여기서 켜고 끈다. 오디오 리스너는 외관 카메라 쪽에만 둔다
    public void SetView(string mode)
    {
        if (mode != "interior" && mode != "split") mode = "exterior";
        CurrentView = mode;
        if (exteriorCamera != null && interiorCamera != null)
        {
            bool split = mode == "split";
            exteriorCamera.enabled = mode != "interior";
            interiorCamera.enabled = mode != "exterior";
            exteriorCamera.rect = split ? new Rect(0f, 0f, 0.5f, 1f) : new Rect(0f, 0f, 1f, 1f);
            interiorCamera.rect = split ? new Rect(0.5f, 0f, 0.5f, 1f) : new Rect(0f, 0f, 1f, 1f);
        }
        else Debug.LogWarning("[LouverBridge] exteriorCamera · interiorCamera 를 Inspector에 넣어 주세요");
        onView.Invoke(mode);
    }

    // 웹 → Unity: site_input
    public void OnSiteInput(string json)
    {
        try
        {
            Current = JsonUtility.FromJson<SiteInput>(json);
            onSiteInput.Invoke(Current);
            if (!string.IsNullOrEmpty(Current.sim_datetime)) SetDateTime(Current.sim_datetime);
        }
        catch (Exception e) { Debug.LogError("[LouverBridge] site_input 읽기 실패: " + e.Message + "\n" + json); }
    }

    // 웹 → Unity: 주변 건물 (그림자용)
    public void OnContext(string json)
    {
        try { Context = JsonUtility.FromJson<ContextInput>(json); onContext.Invoke(Context); }
        catch (Exception e) { Debug.LogError("[LouverBridge] 주변 건물 읽기 실패: " + e.Message); }
    }

    // 웹 → Unity: 날짜 · 시각 (예: 2026-06-21T12:30:00+09:00). 한국 시간 그대로 넘긴다
    public void SetDateTime(string iso)
    {
        if (DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            CurrentTime = dto.DateTime;
            onDateTime.Invoke(CurrentTime);
        }
        else Debug.LogWarning("[LouverBridge] 날짜 형식을 읽지 못함: " + iso);
    }

    // Unity → 웹: 연간 일사량 결과
    public void SendSolarResult(SolarResult result)
    {
        LouverSendSolarResult(JsonUtility.ToJson(result));
    }

    // 에디터에서 웹 없이 시험: Inspector 의 ⋮ 메뉴 → "시험: 1784 하지 정오"
    [ContextMenu("시험: 실내 보기")] private void EditorInterior() { SetView("interior"); }
    [ContextMenu("시험: 나란히 보기")] private void EditorSplit() { SetView("split"); }
    [ContextMenu("시험: 외관 보기")] private void EditorExterior() { SetView("exterior"); }

    [ContextMenu("시험: 1784 하지 정오")]
    private void EditorTest()
    {
        OnSiteInput("{\"site\":{\"address\":\"경기 성남시 분당구 정자일로 95\",\"lat_deg\":37.3595,\"lon_deg\":127.1052},\"project_type\":\"new\",\"winter_mode\":true,\"sim_datetime\":\"2026-06-21T12:00:00+09:00\",\"facades\":[{\"id\":\"E\",\"azimuth_deg\":90,\"window_area_m2\":300,\"louver\":true},{\"id\":\"S\",\"azimuth_deg\":180,\"window_area_m2\":400,\"louver\":true},{\"id\":\"W\",\"azimuth_deg\":270,\"window_area_m2\":300,\"louver\":true},{\"id\":\"N\",\"azimuth_deg\":0,\"window_area_m2\":300,\"louver\":false}],\"louver_spec\":{\"blade_depth_mm\":300,\"blade_spacing_mm\":350,\"louver_to_window_ratio\":0.9,\"area_per_unit_m2\":6,\"cutoff_deg\":[30,60,45,30],\"min_sun_alt_deg\":5}}");
    }
}
