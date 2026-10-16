using System;
using UnityEngine;

/// <summary>
/// 웹(LouverBridge)이 보내는 값을 우리 SunLouverController에 전달하는 접착 스크립트예요.
/// README 3번(기존 코드와 연결)을 이 스크립트가 대신해요. Inspector에서 이벤트를 끌어다 놓을 필요가 없어요.
///
/// 쓰는 법: LouverBridge가 붙은 오브젝트(또는 아무 오브젝트)에 이 스크립트를 붙이면 끝이에요.
///  - 웹 → "날짜·시각"이 오면 SunLouverController.SetDateTime(...)
///  - 웹 → "건물·입면·겨울 모드"가 오면 SetWinterMode(...), SetNorthLouver(...)
/// bridge와 controller 칸은 비워 두면 씬에서 자동으로 찾아요.
/// </summary>
public class LouverBridgeGlue : MonoBehaviour
{
    public LouverBridge bridge;
    public SunLouverController controller;

    void Awake()
    {
#if UNITY_6000_0_OR_NEWER
        if (bridge == null) bridge = FindAnyObjectByType<LouverBridge>();
        if (controller == null) controller = FindAnyObjectByType<SunLouverController>();
#else
        if (bridge == null) bridge = FindObjectOfType<LouverBridge>();
        if (controller == null) controller = FindObjectOfType<SunLouverController>();
#endif
    }

    void OnEnable()
    {
        if (bridge == null || controller == null)
        {
            Debug.LogError("[LouverBridgeGlue] LouverBridge 또는 SunLouverController를 씬에서 못 찾았어요. 둘 다 씬에 있어야 해요.");
            return;
        }
        bridge.onSiteInput.AddListener(HandleSiteInput);
        bridge.onDateTime.AddListener(HandleDateTime);
        Debug.Log("[LouverBridgeGlue] 연결됨");
    }

    void OnDisable()
    {
        if (bridge == null) return;
        bridge.onSiteInput.RemoveListener(HandleSiteInput);
        bridge.onDateTime.RemoveListener(HandleDateTime);
    }

    [Header("1784가 아닌 건물 (직접 계산 모드 자동 전환)")]
    [Tooltip("켜면(기본은 꺼짐, 박스 건물 생성기를 붙인 뒤에 켜세요) 웹이 보낸 좌표가 1784에서 멀 때 직접 계산 모드로 바꿔요(1784 근처면 CSV 모드)")] public bool autoSwitchSite = false;
    [Tooltip("1784 위치(정자일로 95)")] public double site1784Lat = 37.3595, site1784Lon = 127.1052;
    [Tooltip("이 거리(m) 안이면 1784로 봐요")] public double site1784RadiusM = 300.0;
    [Tooltip("직각이 아닌 입면이 있을 때 경고하는 어긋남(°)")] public double gridWarnDeg = 15.0;
    [System.Serializable] public class BoolEvent : UnityEngine.Events.UnityEvent<bool> { }
    [Tooltip("모드가 바뀔 때 부르는 이벤트(true = 좌표를 찍은 건물 / 직접 계산, false = 1784). 시각화 담당: 1784 모델과 박스 모델을 켜고 끄는 데 쓰세요")]
    public BoolEvent onDirectModeChanged = new BoolEvent();
    int lastDirect = -1;

    // 웹 → Unity: 건물, 입면, 루버 사양, 겨울 모드
    // 이름이 LouverBridge.OnSiteInput(string) 과 같으면 웹의 SendMessage 가 이 함수도 불러 MissingMethodException 이 나서 Handle… 로 둔다
    void HandleSiteInput(SiteInput s)
    {
        if (s == null || controller == null) return;
        controller.SetWinterMode(s.winter_mode);

        // 자동 전환이 꺼져 있으면 컨트롤러의 Direct Calc 체크를 그대로 따라요
        // (1784 빌드: 꺼짐 / 박스 건물 빌드: 켜짐). 자동 전환이 켜져 있으면 좌표로 정해요.
        bool direct = controller.directCalc;
        if (autoSwitchSite)
        {
            direct = false;
            if (s.site != null && (s.site.lat_deg != 0 || s.site.lon_deg != 0))
            {
                double dLat = (s.site.lat_deg - site1784Lat) * 110540.0;
                double dLon = (s.site.lon_deg - site1784Lon) * 111320.0 * Math.Cos(site1784Lat * Math.PI / 180.0);
                direct = Math.Sqrt(dLat * dLat + dLon * dLon) > site1784RadiusM;
            }
            controller.SetDirectMode(direct);
        }
        if (direct && s.site != null)
        {
            controller.SetLocation(s.site.lat_deg, s.site.lon_deg);
            var spec = s.louver_spec;
            if (spec != null)
            {
                if (spec.blade_depth_mm > 0) controller.bladeDepthMm = spec.blade_depth_mm;
                if (spec.blade_spacing_mm > 0) controller.bladeSpacingMm = spec.blade_spacing_mm;
                if (spec.cutoff_deg != null && spec.cutoff_deg.Length >= 4)
                    controller.cutoffDeg = new double[] { spec.cutoff_deg[0], spec.cutoff_deg[1], spec.cutoff_deg[2], spec.cutoff_deg[3] };
            }
            double yaw = 0, maxDev = 0;
            if (s.facades != null && s.facades.Length > 0)
            {
                var az = new double[s.facades.Length];
                var wt = new double[s.facades.Length];
                for (int i = 0; i < az.Length; i++)
                {
                    az[i] = s.facades[i] != null ? s.facades[i].azimuth_deg : 0;
                    wt[i] = s.facades[i] != null ? s.facades[i].width_m : 0;
                }
                yaw = LouverRule.GridYawDeg(az, wt, out maxDev);
                // 북쪽(건물 기준) 입면의 루버 유무
                bool anyNorth = false, northLouver = false;
                foreach (var f in s.facades)
                {
                    if (f == null) continue;
                    double rel = LouverRule.Wrap180(f.azimuth_deg - yaw);
                    if (Math.Abs(rel) <= 45.0) { anyNorth = true; if (f.louver) northLouver = true; }
                }
                controller.SetNorthLouver(anyNorth ? northLouver : true);
            }
            controller.SetBuildingYaw((float)yaw);
            if (maxDev > gridWarnDeg)
                Debug.LogWarning("[LouverBridgeGlue] 직각이 아닌 입면이 있어요(최대 " + maxDev.ToString("0") + "° 어긋남). 비스듬한 변은 가장 가까운 방향의 날로 근사돼요.");
            Debug.Log("[LouverBridgeGlue] 직접 계산: 위도 " + s.site.lat_deg + " 경도 " + s.site.lon_deg + " 건물 회전 " + yaw.ToString("0.0") + "° 입면 " + (s.facades != null ? s.facades.Length : 0) + "개");
        }
        else if (s.facades != null)
        {
            foreach (var f in s.facades)
                if (f != null && f.id == "N") controller.SetNorthLouver(f.louver);   // 북면 루버 유무
            Debug.Log("[LouverBridgeGlue] site_input: 1784(CSV) 모드, 겨울 모드 " + s.winter_mode + ", 입면 " + s.facades.Length + "개");
        }
        if (lastDirect != (direct ? 1 : 0)) { lastDirect = direct ? 1 : 0; onDirectModeChanged.Invoke(direct); }
    }

    // 웹 → Unity: 날짜·시각 (한국 시간)
    void HandleDateTime(DateTime t)
    {
        if (controller != null) controller.SetDateTime(t);
    }
}
