using UnityEngine;

/// <summary>
/// 웹이 "수직 루버 / 수평 루버"를 고르면 유니티의 보이는 날 모델을 바꿔 주는 받는 쪽이에요.
/// 씬에 빈 오브젝트를 만들고 이 스크립트를 붙이면 끝이에요. 오브젝트 이름은 실행 시 자동으로 "LouverOrientation"이 돼요.
///
/// 웹(JavaScript)에서 부르는 법 (웹 담당):
///   unityInstance.SendMessage('LouverOrientation', 'SetOrientation', 'horizontal');   // 수평
///   unityInstance.SendMessage('LouverOrientation', 'SetOrientation', 'vertical');     // 수직
/// </summary>
public class LouverOrientationReceiver : MonoBehaviour
{
    public SunLouverController controller;

    void Awake()
    {
        gameObject.name = "LouverOrientation";
#if UNITY_6000_0_OR_NEWER
        if (controller == null) controller = FindAnyObjectByType<SunLouverController>();
#else
        if (controller == null) controller = FindObjectOfType<SunLouverController>();
#endif
    }

    /// <summary>웹이 부르는 함수. 이름 문자열: "horizontal"(수평) 또는 "vertical"(수직)</summary>
    public void SetOrientation(string name)
    {
        if (controller == null)
        {
            Debug.LogError("[LouverOrientation] SunLouverController를 씬에서 못 찾았어요");
            return;
        }
        controller.SetOrientationByName(name);
    }

    /// <summary>웹이 부르는 함수. "direct"(직접 계산: 좌표를 찍은 네모 건물) 또는 "1784"(CSV 기준 1784)</summary>
    public void SetSiteMode(string name)
    {
        if (controller == null) return;
        string n = (name ?? "").Trim().ToLowerInvariant();
        controller.SetDirectMode(n == "direct" || n == "box" || n == "직접");
    }

    /// <summary>웹이 부르는 함수. "위도,경도" 문자열 (예: "37.5665,126.9780")</summary>
    public void SetLocation(string latLon)
    {
        if (controller == null || string.IsNullOrEmpty(latLon)) return;
        var p = latLon.Split(',');
        double lat, lon;
        if (p.Length >= 2 &&
            double.TryParse(p[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lat) &&
            double.TryParse(p[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lon))
            controller.SetLocation(lat, lon);
        else
            Debug.LogError("[LouverOrientation] 좌표 형식이 달라요(예: 37.5665,126.9780): " + latLon);
    }

    /// <summary>웹이 부르는 함수. 건물이 정북에서 시계 방향으로 돌아간 각도(°) 문자열</summary>
    public void SetBuildingYaw(string deg)
    {
        float d;
        if (controller != null && float.TryParse(deg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d))
            controller.SetBuildingYaw(d);
    }

    [ContextMenu("시험: 수평 루버")]
    void TestHorizontal() { SetOrientation("horizontal"); }

    [ContextMenu("시험: 수직 루버")]
    void TestVertical() { SetOrientation("vertical"); }
}
