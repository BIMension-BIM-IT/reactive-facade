using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// sun_louver.csv를 읽어서 날짜와 시각에 맞게 태양(Directional Light)과 루버 각도를 움직여요.
/// CSV는 Assets/StreamingAssets/ 폴더에 넣어주세요.
/// </summary>
/// <summary>비교 대안: 시공 전(루버 없음), 외부 고정 0°(날을 열어 둠), 외부 반응형(우리 규칙)</summary>
public enum LouverAlternative { Bare, Fixed0, Reactive }

public class SunLouverController : MonoBehaviour
{
    public string csvFileName = "sun_louver.csv";
    [Tooltip("CSV의 대표일 이름 또는 날짜 (예: 하지, 6/21)")] public string day = "하지";
    [Range(5f, 20f)] public float hour = 12.5f;
    [Tooltip("켜면 재생 중 시각이 자동으로 흘러가요")] public bool autoPlay = true;
    [Tooltip("1초에 흐르는 시간(시간 단위). 1이면 1초에 1시간")] public float hoursPerSecond = 1f;
    [Tooltip("켜면 정오에 날이 쓸데없이 반 바퀴 돌지 않고 눕힌 채로 이어서 움직여요(연속각 열 사용)")]
    public bool useContinuousAngle = true;
    public Light sun;
    public LouverFacade[] facades;
    [Tooltip("외피 모델링 담당 루버(FBX)를 쓸 때: LouverRig를 붙인 오브젝트")] public LouverRig rig;
    [Header("수직 / 수평 루버")]
    [Tooltip("지금 보여 줄 날 방향. 수평을 쓰려면 아래 Rig Horizontal에 수평 날용 LouverRig(Orientation = Horizontal)를 연결해요")]
    public LouverOrientation orientation = LouverOrientation.Vertical;
    [Tooltip("보여 줄 대안: 시공 전(루버 없음) / 외부 고정 0° / 외부 반응형")] public LouverAlternative alternative = LouverAlternative.Reactive;
    [Tooltip("수평 루버 모델용 LouverRig (Orientation = Horizontal). 비워 두면 수평 기능은 꺼져 있어요")] public LouverRig rigHorizontal;
    [Tooltip("수평 루버 층별 각도 표(StreamingAssets)")] public string horizontalCsvFileName = "horizontal_angle_by_floor.csv";
    [Tooltip("겨울 날짜표(StreamingAssets). 겨울 모드를 켜면 이 표의 겨울 날짜에만 겨울 전략을 써요")] public string winterCsvFileName = "winter_calendar.csv";
    [Tooltip("선택: UI 슬라이더(범위 5~20)를 연결하면 hour를 대신 정해요")] public Slider hourSlider;

    [Header("웹 연결 (LouverBridgeGlue가 채워 줘요)")]
    [Tooltip("웹이 날짜·시각을 정하는 중이면 켜져요(자동 재생과 슬라이더를 무시해요)")] public bool externalControl;
    [Tooltip("겨울 모드. 켜면 난방이 필요한 날(기온 기준 날짜표)에 해와 나란히 눕혀 겨울 햇빛을 받고, 눈부심 시각만 0°. 끄면 연중 우리 규칙대로 움직여요")] public bool winterMode;
    [Tooltip("웹이 북면에 루버를 달지 않기로 하면 꺼져요")] public bool northLouver = true;

    [Header("층별 각도 (주변 건물 그림자 반영)")]
    [Tooltip("켜면 층마다 다른 각도로 돌려요(그림자 지는 층은 열림). 파일이 없으면 입면별 각도만 써요")]
    public bool useFloorSchedule = true;
    public string floorCsvFileName = "louver_angle_by_floor.csv";

    [Header("직접 계산 모드 (1784가 아닌 건물: 좌표를 찍은 네모 건물)")]
    [Tooltip("켜면 sun_louver.csv 없이 위도·경도로 해 위치를 계산하고, 규칙(수직·수평)을 바로 계산해요. 주변 건물 그림자(층별 표)는 반영하지 않아요")]
    public bool directCalc = false;
    [Tooltip("위도 (기본: 성남 1784 부근)")] public double latitude = 37.36;
    [Tooltip("경도")] public double longitude = 127.105;
    [Tooltip("표준시 차이 (한국 +9)")] public double timeZoneHours = 9.0;
    [Tooltip("건물이 정북에서 시계 방향으로 돌아간 각도(°). 모델은 북쪽 = +Z로 반듯하게 두고 이 값만 넣어요. 남향 건물이면 0")]
    public float buildingYawDeg = 0f;
    [Tooltip("직접 계산 컷오프 {춘분, 하지, 추분, 동지} (1784 검증값)")] public double[] cutoffDeg = { 30.0, 60.0, 45.0, 30.0 };
    [Tooltip("날 폭(mm)")] public double bladeDepthMm = 300.0;
    [Tooltip("날 간격(mm)")] public double bladeSpacingMm = 350.0;
    [Tooltip("수평 루버 컷오프(프로파일각, °)")] public double horizontalCutoffDeg = 15.0;

    SunLouverData data, sunData;
    FloorScheduleData floorData, floorDataH;
    WinterCalendar winterCal;
    int curDay = 21;
    bool loaded;
    readonly double[] prevV = new double[4];
    long lastDirectKey = long.MinValue;

    // ---- 직접 계산 모드 켜고 끄기 · 좌표 · 방향 (웹이 LouverOrientationReceiver로 부를 수 있어요) ----
    public void SetDirectMode(bool on)
    {
        directCalc = on;
        applied = false;
        lastDirectKey = long.MinValue;
        for (int i = 0; i < 4; i++) prevV[i] = 0.0;
        Debug.Log("[SunLouver] 직접 계산 모드: " + (on ? "켬" : "끔"));
    }

    public void SetLocation(double lat, double lon)
    {
        latitude = lat; longitude = lon;
        applied = false; lastDirectKey = long.MinValue;
    }

    public void SetBuildingYaw(float deg)
    {
        buildingYawDeg = deg;
        applied = false; lastDirectKey = long.MinValue;
    }

    LouverRig ActiveRig()
    {
        return orientation == LouverOrientation.Horizontal ? rigHorizontal : rig;
    }

    /// <summary>수직/수평 전환. 수직·수평 모델이 둘 다 씬에 있으면 고른 쪽만 보여요.</summary>
    public void SetOrientation(LouverOrientation o)
    {
        orientation = o;
        applied = false;
        RefreshVisibility();
        Debug.Log("[SunLouver] 날 방향: " + (o == LouverOrientation.Horizontal ? "수평" : "수직"));
    }

    // 시공 전이면 루버를 숨기고, 아니면 고른 방향의 모델만 보여요
    void RefreshVisibility()
    {
        bool bare = alternative == LouverAlternative.Bare;
        if (rig != null) rig.SetVisible(!bare && (rigHorizontal == null || orientation == LouverOrientation.Vertical));
        if (rigHorizontal != null) rigHorizontal.SetVisible(!bare && orientation == LouverOrientation.Horizontal);
    }

    // ---- 화면 버튼 · 슬라이더가 부르는 함수들 (시각화 담당: Button OnClick / Slider OnValueChanged에 연결) ----

    /// <summary>대안 고르기: 0 = 시공 전(루버 없음), 1 = 외부 고정 0°, 2 = 외부 반응형</summary>
    public void SetAlternative(int i)
    {
        alternative = i <= 0 ? LouverAlternative.Bare : (i == 1 ? LouverAlternative.Fixed0 : LouverAlternative.Reactive);
        applied = false;
        RefreshVisibility();
        Debug.Log("[SunLouver] 대안: " + alternative);
    }

    /// <summary>이름으로: "bare"/"시공전", "fixed"/"고정", 그 밖에는 반응형</summary>
    public void SetAlternativeByName(string name)
    {
        string n = (name ?? "").Trim().ToLowerInvariant();
        if (n == "bare" || n == "none" || n == "시공전" || n == "시공 전") SetAlternative(0);
        else if (n == "fixed" || n == "fixed0" || n == "고정") SetAlternative(1);
        else SetAlternative(2);
    }

    /// <summary>대표일 이름: 춘분, 하지, 추분, 동지 (CSV의 대표일과 같아야 해요)</summary>
    public void SetDayByName(string dayName)
    {
        day = dayName;
        curMonth = dayName == "춘분" ? 3 : dayName == "하지" ? 6 : dayName == "추분" ? 9 : 12;
        curDay = dayName == "추분" ? 23 : 21;
        externalControl = false;       // 화면 버튼이 우선(웹이 다시 보내면 웹이 다시 우선)
        applied = false;
    }

    /// <summary>시각 슬라이더(5~20). 직접 고르면 자동 재생은 멈춰요.</summary>
    public void SetHour(float h)
    {
        hour = Mathf.Clamp(h, 5f, 20f);
        autoPlay = false;
        externalControl = false;
        applied = false;
    }

    public void SetAutoPlay(bool on) { autoPlay = on; if (on) externalControl = false; }
    public void TogglePlay() { SetAutoPlay(!autoPlay); }
    public void SetPlaySpeed(float hoursPerSec) { hoursPerSecond = Mathf.Max(0.05f, hoursPerSec); }

    /// <summary>시연용 한 번에 설정(발표 때 버튼 한 번으로): "하지정오", "하지오후", "동지정오"</summary>
    public void ApplyScene(string name)
    {
        externalControl = false;
        autoPlay = false;
        SetAlternative(2);
        switch (name)
        {
            case "하지정오": SetDayByName("하지"); hour = 12.5f; winterMode = false; break;
            case "하지오후": SetDayByName("하지"); hour = 15.5f; winterMode = false; break;
            case "동지정오": SetDayByName("동지"); hour = 12.5f; winterMode = true; break;
            case "동지정오_규칙": SetDayByName("동지"); hour = 12.5f; winterMode = false; break;
        }
        applied = false;
        Debug.Log("[SunLouver] 시연 장면: " + name);
    }

    /// <summary>웹이 문자열로 부를 때: "horizontal"/"수평"/"h"이면 수평, 그 밖에는 수직</summary>
    public void SetOrientationByName(string name)
    {
        string n = (name ?? "").Trim().ToLowerInvariant();
        bool h = n == "horizontal" || n == "h" || n == "수평" || n == "수평형";
        SetOrientation(h ? LouverOrientation.Horizontal : LouverOrientation.Vertical);
    }
    string lastInfo = "";
    SunLouverRow lastRow;
    bool lastCont, lastWinter, lastForce, applied;
    int curMonth = 6;
    static readonly float[] faceAzimuth = { 90f, 180f, 270f, 0f };   // 동 남 서 북 (FacadeSide 순서)

    // ---- 웹이 부르는 함수들 (LouverBridgeGlue가 연결해요) ----

    /// <summary>웹의 날짜·시각(한국 시간). 가장 가까운 대표일(춘분·하지·추분·동지) 데이터로 보여줘요.</summary>
    public void SetDateTime(System.DateTime t)
    {
        externalControl = true;
        curMonth = t.Month;
        curDay = t.Day;
        string[] names = { "춘분", "하지", "추분", "동지" };
        int[] anchor = { 80, 172, 266, 355 };       // 3/21, 6/21, 9/23, 12/21 의 일 번호
        int best = 0, bestDiff = int.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            int d = Mathf.Abs(t.DayOfYear - anchor[i]);
            d = Mathf.Min(d, 365 - d);
            if (d < bestDiff) { bestDiff = d; best = i; }
        }
        day = names[best];
        hour = t.Hour + t.Minute / 60f;
        applied = false;                              // 바로 다시 적용
        Debug.Log("[SunLouver] 웹 시각 " + t.ToString("yyyy-MM-dd HH:mm") + " → " + day + " " + hour.ToString("0.0") + "시");
    }

    public void SetWinterMode(bool on)
    {
        winterMode = on;
        applied = false;
    }

    public void SetNorthLouver(bool on)
    {
        northLouver = on;
        if (rig != null) rig.SetSideActive(FacadeSide.North, on);
        if (rigHorizontal != null) rigHorizontal.SetSideActive(FacadeSide.North, on);
    }

    int MonthNow()
    {
        if (externalControl) return curMonth;
        switch (day)
        {
            case "춘분": return 3;
            case "하지": return 6;
            case "추분": return 9;
            case "동지": return 12;
        }
        return 6;
    }

    int DayNow()
    {
        if (externalControl) return curDay;
        return day == "추분" ? 23 : 21;
    }

    // 겨울 모드가 켜져 있고, 그 날이 난방이 필요한 겨울이면 true (날짜표는 기온 기준 자동 판정)
    bool IsWinterNow(int month, int dayOfMonth)
    {
        if (!winterMode) return false;
        return winterCal != null ? winterCal.IsWinter(month, dayOfMonth) : (month >= 11 || month <= 3);
    }

    // 해와 나란한 각도: 해가 입면 앞쪽에 있을 때만 날을 해 방향으로 맞춰요
    static float ParallelAngle(float sunAzimuth, int sideIndex)
    {
        float d = Mathf.DeltaAngle(faceAzimuth[sideIndex], sunAzimuth);
        return Mathf.Abs(d) < 90f ? -d : 0f;
    }

    // 웹(WebGL)에서는 File.ReadAllText가 안 돼서 UnityWebRequest로 읽어요(에디터/PC에서도 같은 방식으로 돼요)
    IEnumerator Start()
    {
        string path = Path.Combine(Application.streamingAssetsPath, csvFileName);
        string url = path.Contains("://") ? path : "file://" + path;
        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                if (!directCalc)
                {
                    Debug.LogError("CSV를 읽을 수 없어요: " + url + " (" + req.error + ")");
                    enabled = false;
                    yield break;
                }
                Debug.LogWarning("sun_louver.csv 없이 직접 계산 모드로 시작해요: " + url);
            }
            else
            {
                sunData = SunLouverData.LoadFromText(req.downloadHandler.text);
                Debug.Log("CSV 행 수: " + sunData.rows.Count);
            }
        }

        // 층별 각도(주변 건물 그림자 반영) CSV. 없으면 입면별 각도만 써요.
        if (useFloorSchedule && !string.IsNullOrEmpty(floorCsvFileName))
        {
            string path2 = Path.Combine(Application.streamingAssetsPath, floorCsvFileName);
            string url2 = path2.Contains("://") ? path2 : "file://" + path2;
            using (var req2 = UnityWebRequest.Get(url2))
            {
                yield return req2.SendWebRequest();
                if (req2.result == UnityWebRequest.Result.Success)
                {
                    floorData = FloorScheduleData.LoadFromText(req2.downloadHandler.text);
                    Debug.Log("층별 CSV 층 수: " + floorData.maxFloor);
                }
                else
                {
                    Debug.LogWarning("층별 CSV를 읽을 수 없어요(입면별 각도만 사용): " + url2 + "  이유: " + req2.error + "  → Assets/StreamingAssets 폴더(맨 위 Assets 바로 아래)에 louver_angle_by_floor.csv가 있는지 확인하세요");
                }
            }
        }
        // 수평 루버용 층별 각도 표 (수평 리그를 연결했을 때만)
        if (rigHorizontal != null && !string.IsNullOrEmpty(horizontalCsvFileName))
        {
            string path3 = Path.Combine(Application.streamingAssetsPath, horizontalCsvFileName);
            string url3 = path3.Contains("://") ? path3 : "file://" + path3;
            using (var req3 = UnityWebRequest.Get(url3))
            {
                yield return req3.SendWebRequest();
                if (req3.result == UnityWebRequest.Result.Success)
                {
                    floorDataH = FloorScheduleData.LoadFromText(req3.downloadHandler.text);
                    Debug.Log("수평 루버 층별 CSV 층 수: " + floorDataH.maxFloor);
                }
                else
                {
                    Debug.LogWarning("수평 루버 CSV를 읽을 수 없어요(수평은 열림으로 둠): " + url3 + "  이유: " + req3.error);
                }
            }
        }
        // 겨울 날짜표(기온 기준 자동 판정). 없으면 11~3월을 겨울로 봐요.
        if (!string.IsNullOrEmpty(winterCsvFileName))
        {
            string path4 = Path.Combine(Application.streamingAssetsPath, winterCsvFileName);
            string url4 = path4.Contains("://") ? path4 : "file://" + path4;
            using (var req4 = UnityWebRequest.Get(url4))
            {
                yield return req4.SendWebRequest();
                if (req4.result == UnityWebRequest.Result.Success)
                {
                    winterCal = WinterCalendar.LoadFromText(req4.downloadHandler.text);
                    Debug.Log("겨울 날짜표를 읽었어요(" + winterCsvFileName + ")");
                }
                else
                {
                    Debug.LogWarning("겨울 날짜표를 읽을 수 없어요(11~3월을 겨울로 씀): " + url4 + "  이유: " + req4.error);
                }
            }
        }
        data = sunData;   // 두 CSV를 다 읽은 뒤에 시작해요
        loaded = true;
        SetOrientation(orientation);   // 고른 방향의 모델만 보이게(날 위치 계산이 끝난 뒤라서 안전해요)
    }

    // 직접 계산 모드: CSV 없이 위도·경도로 해 위치와 루버 각도를 계산해요
    void UpdateDirect()
    {
        if (externalControl) { }
        else if (hourSlider != null) hour = hourSlider.value;
        else if (autoPlay)
        {
            hour += hoursPerSecond * Time.deltaTime;
            if (hour > 20f) hour = 5f;
        }
        int minutes = Mathf.RoundToInt(hour * 60f / 10f) * 10;          // 10분 간격
        int month = MonthNow(), dom = DayNow();
        bool winterActive = IsWinterNow(month, dom);
        bool forceOpen = alternative != LouverAlternative.Reactive;
        long key = ((((long)month * 40 + dom) * 1500 + minutes) * 4 + (winterActive ? 1 : 0) + (forceOpen ? 2 : 0)) * 2 + (orientation == LouverOrientation.Horizontal ? 1 : 0);
        if (applied && key == lastDirectKey) return;
        applied = true; lastDirectKey = key;

        int hh = Mathf.Clamp(minutes / 60, 0, 23);
        var local = new System.DateTime(2026, month, Mathf.Clamp(dom, 1, System.DateTime.DaysInMonth(2026, month)), hh, minutes % 60, 0);
        double alt, az;
        SunPosition.Compute(latitude, longitude, timeZoneHours, local, out alt, out az);

        if (alt <= 0.0)
        {
            if (sun != null) sun.enabled = false;
            foreach (var f in AllFacades()) f.Apply(0f);
            if (rig != null) rig.ApplyAll(0f);
            if (rigHorizontal != null) rigHorizontal.ApplyAll(0f);
            for (int i = 0; i < 4; i++) prevV[i] = 0.0;
            Report("해 없음 (" + month + "/" + dom + " " + hour.ToString("0.0") + "시)");
            return;
        }

        if (sun != null)
        {
            sun.enabled = true;
            double dx, dy, dz;
            SunPosition.Direction(alt, az - buildingYawDeg, out dx, out dy, out dz);   // 모델은 북쪽 = +Z로 두고 건물 회전만큼 해를 돌려요
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(-(float)dx, -(float)dy, -(float)dz), Vector3.up);
        }

        double cutoff = LouverRule.CutoffForDate(cutoffDeg, month, dom);
        float[] v = new float[4], h = new float[4];
        for (int k = 0; k < 4; k++)
        {
            double faceAz = LouverRule.Wrap180(faceAzimuth[k] + buildingYawDeg);
            if (forceOpen) { v[k] = 0f; h[k] = 0f; prevV[k] = 0.0; continue; }
            if (winterActive)
            {
                v[k] = (float)LouverRule.WinterVertical(alt, az, faceAz);
                h[k] = (float)LouverRule.WinterHorizontal(alt, az, faceAz);
                prevV[k] = v[k];
                continue;
            }
            double a;
            bool closed = LouverRule.VerticalAngle(alt, az, faceAz, cutoff, bladeDepthMm, bladeSpacingMm, out a);
            double target = closed ? a : 0.0;
            if (useContinuousAngle) target = LouverRule.Unwrap180(prevV[k], target);   // 정오에 날이 반 바퀴 돌지 않게
            prevV[k] = target;
            v[k] = (float)target;
            double t;
            LouverRule.HorizontalTilt(alt, az, faceAz, horizontalCutoffDeg, bladeDepthMm, bladeSpacingMm, out t);
            h[k] = (float)t;
        }
        foreach (var f in AllFacades()) f.Apply(v[(int)f.side]);
        if (orientation == LouverOrientation.Horizontal)
        {
            if (rigHorizontal != null) for (int k = 0; k < 4; k++) rigHorizontal.Apply((FacadeSide)k, h[k]);
        }
        else if (rig != null)
        {
            for (int k = 0; k < 4; k++) rig.Apply((FacadeSide)k, v[k]);
        }
        Report(string.Format("[직접] {0}/{1} {2:00}:{3:00} 고도 {4:0.0}° 방위 {5:0.0}° 컷오프 {6:0.0}°", month, dom, minutes / 60, minutes % 60, alt, az, cutoff));
    }

    void Update()
    {
        if (directCalc) { if (loaded) UpdateDirect(); return; }
        if (data == null) return;
        if (externalControl) { /* 웹이 시각을 정해요 */ }
        else if (hourSlider != null) hour = hourSlider.value;
        else if (autoPlay)
        {
            hour += hoursPerSecond * Time.deltaTime;
            if (hour > 20f) hour = 5f;
        }

        int minutes = Mathf.RoundToInt(hour * 60f);
        var row = data.Find(day, minutes);

        // 같은 행(10분 간격)이면 다시 계산하지 않아요(날이 많을 때 웹에서 느려지지 않게)
        int month = MonthNow();
        // 겨울 모드: 켜져 있고 그 날이 난방이 필요한 겨울이면 해와 나란히 + 눈부심 시각만 0°(혼합 규칙)
        bool winterActive = IsWinterNow(month, DayNow());
        // 대안이 '시공 전/고정 0°'이면 날은 항상 열림(0°)
        bool forceOpen = alternative != LouverAlternative.Reactive;
        if (applied && row == lastRow && lastCont == useContinuousAngle && lastWinter == winterActive && lastForce == forceOpen) return;
        applied = true; lastRow = row; lastCont = useContinuousAngle; lastWinter = winterActive; lastForce = forceOpen;

        if (row == null)
        {
            // 해가 없는 시각: 빛을 끄고 루버는 열림(0도)
            if (sun != null) sun.enabled = false;
            foreach (var f in AllFacades()) f.Apply(0f);
            if (rig != null) rig.ApplyAll(0f);
            if (rigHorizontal != null) rigHorizontal.ApplyAll(0f);
            Report("해 없음 (" + day + " " + hour.ToString("0.0") + "시)");
            return;
        }

        if (sun != null)
        {
            sun.enabled = true;
            // Directional Light는 앞(forward)으로 빛이 가요 -> 태양을 향하는 방향의 반대
            sun.transform.rotation = Quaternion.LookRotation(-row.sunDir, Vector3.up);
        }
        foreach (var f in AllFacades())
        {
            int idx = (int)f.side;
            f.Apply(forceOpen ? 0f : (winterActive ? (float)LouverRule.WinterVertical(row.altitude, row.azimuth, faceAzimuth[idx]) : (useContinuousAngle ? row.contAngle[idx] : row.angle[idx])));
        }
        if (orientation == LouverOrientation.Horizontal)
        {
            // 수평 루버: 수평용 층별 표(각도 = 바깥 끝이 아래로 내려가는 각도)를 그대로 써요. 겨울 모드는 0°.
            if (rigHorizontal != null)
            {
                FloorScheduleRow[] hrows = (floorDataH != null) ? floorDataH.Find(day, row.minutes) : null;
                if (forceOpen)
                {
                    rigHorizontal.ApplyAll(0f);
                }
                else if (winterActive)
                {
                    for (int k = 0; k < 4; k++)
                        rigHorizontal.Apply((FacadeSide)k, (float)LouverRule.WinterHorizontal(row.altitude, row.azimuth, faceAzimuth[k]));
                }
                else if (hrows == null)
                {
                    rigHorizontal.ApplyAll(0f);
                }
                else
                {
                    rigHorizontal.ApplyTable((fl, side) =>
                    {
                        int k = (int)side;
                        if (fl >= 1 && fl < hrows.Length && hrows[fl] != null) return hrows[fl].angle[k];
                        return 0f;
                    });
                }
            }
        }
        else if (rig != null)
        {
            FloorScheduleRow[] frows = (useFloorSchedule && floorData != null) ? floorData.Find(day, row.minutes) : null;
            if (forceOpen)
            {
                rig.ApplyAll(0f);
            }
            else if (winterActive)
            {
                // 겨울 모드(혼합 규칙): 해와 나란히, 낮은 해가 정면에서 들어오는 시각은 0°
                for (int k = 0; k < 4; k++)
                    rig.Apply((FacadeSide)k, (float)LouverRule.WinterVertical(row.altitude, row.azimuth, faceAzimuth[k]));
            }
            else if (frows != null)
            {
                // 층·입면마다 다른 각도 (날 전체를 한 번만 돌아요)
                rig.ApplyTable((fl, side) =>
                {
                    int k = (int)side;
                    if (fl >= 1 && fl < frows.Length && frows[fl] != null)
                        return useContinuousAngle ? frows[fl].cont[k] : frows[fl].angle[k];
                    return useContinuousAngle ? row.contAngle[k] : row.angle[k];   // 표에 없는 층은 입면별 각도
                });
            }
            else
            {
                for (int k = 0; k < 4; k++)
                    rig.Apply((FacadeSide)k, useContinuousAngle ? row.contAngle[k] : row.angle[k]);
            }
        }
        Report(string.Format("{0} {1} {2:00}:{3:00} 고도 {4:0.0}° 방위 {5:0.0}° 컷오프 {6:0.0}°",
            row.day, row.date, row.minutes / 60, row.minutes % 60, row.altitude, row.azimuth, row.cutoff));
    }

    IEnumerable<LouverFacade> AllFacades()
    {
        if (facades == null) yield break;
        foreach (var f in facades)
            if (f != null) yield return f;
    }

    void Report(string info)
    {
        if (info == lastInfo) return;
        lastInfo = info;
        Debug.Log(info);
    }
}
