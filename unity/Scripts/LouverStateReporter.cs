using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// 유니티 화면 버튼으로 바뀐 상태를 웹에 알려 줘요(유니티 -> 웹).
/// 웹이 보낸 값(SetDateTime 등)으로 바뀐 경우에는 다시 보내지 않아서 서로 되풀이하지 않아요.
/// 웹에서는 window.LouverBridge.receiveState(state)로 받아요. 에디터에서는 Console에 같은 JSON이 찍혀요.
/// 사용법: 빈 오브젝트에 붙이면 끝(Controller 칸은 비워 두면 자동으로 찾아요).
/// </summary>
public class LouverStateReporter : MonoBehaviour
{
    public SunLouverController controller;
    [Tooltip("같은 상태를 너무 자주 보내지 않게 하는 최소 간격(초)")] public float minInterval = 0.15f;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void LouverStateChanged(string json);
#else
    static void LouverStateChanged(string json) { Debug.Log("[LouverState] " + json); }
#endif

    string lastJson = "";
    float lastTime;

    void Awake()
    {
#if UNITY_6000_0_OR_NEWER
        if (controller == null) controller = FindAnyObjectByType<SunLouverController>();
#else
        if (controller == null) controller = FindObjectOfType<SunLouverController>();
#endif
    }

    void Update()
    {
        if (controller == null || controller.externalControl) return;      // 웹이 정한 상태는 되돌려 보내지 않아요
        if (Time.unscaledTime - lastTime < minInterval) return;
        string json = Build();
        if (json == lastJson) return;
        lastJson = json;
        lastTime = Time.unscaledTime;
        LouverStateChanged(json);
    }

    string Build()
    {
        int month, d;
        switch (controller.day)
        {
            case "춘분": month = 3; d = 21; break;
            case "추분": month = 9; d = 23; break;
            case "동지": month = 12; d = 21; break;
            default: month = 6; d = 21; break;     // 하지
        }
        // 10분 단위로 맞춰 보내요(데이터가 10분 간격이라서)
        int minutes = Mathf.RoundToInt(controller.hour * 60f / 10f) * 10;
        int hh = minutes / 60, mm = minutes % 60;
        var inv = CultureInfo.InvariantCulture;
        string sim = string.Format(inv, "2026-{0:00}-{1:00}T{2:00}:{3:00}:00+09:00", month, d, hh, mm);
        string alt = controller.alternative == LouverAlternative.Bare ? "bare" : (controller.alternative == LouverAlternative.Fixed0 ? "fixed" : "reactive");
        string ori = controller.orientation == LouverOrientation.Horizontal ? "horizontal" : "vertical";
        return string.Format(inv,
            "{{\"source\":\"unity\",\"sim_datetime\":\"{0}\",\"day_name\":\"{1}\",\"hour\":{2:0.00},\"alternative\":\"{3}\",\"orientation\":\"{4}\",\"winter_mode\":{5},\"playing\":{6}}}",
            sim, controller.day, hh + mm / 60f, alt, ori, controller.winterMode ? "true" : "false", controller.autoPlay ? "true" : "false");
    }
}
