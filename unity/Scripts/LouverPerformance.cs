using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 날이 많아서 생기는 노이즈·끊김을 줄이는 설정을 한꺼번에 적용해요(빈 오브젝트에 붙이면 끝).
///  1) 안티에일리어싱(MSAA)과 프레임 제한 적용 -> 멀리서 줄무늬가 번쩍이는 것 줄이기
///  2) 해(Directional Light)의 그림자 편향 조정 -> 그림자 줄무늬 줄이기
///  3) 외관 뷰에서는 날의 그림자 그리기를 꺼서 부하 줄이기, 실내 뷰에서는 켜서 날 그림자 무늬를 보여 주기
///  4) 카메라에서 보이지 않는 면의 날을 꺼서 그리는 양 줄이기(카메라가 건물 안이면 끄지 않아요)
/// URP Asset의 그림자 거리 같은 항목은 Project Settings에서 직접 바꿔야 해요(README 참고).
/// </summary>
public class LouverPerformance : MonoBehaviour
{
    public SunLouverController controller;
    public Camera cam;

    [Header("1) 화면")]
    [Tooltip("MSAA 배수(0, 2, 4, 8). 4가 보통 좋아요")] public int antiAliasing = 4;
    [Tooltip("프레임 제한(웹은 30~60). 0이면 건드리지 않아요")] public int targetFps = 60;

    [Header("2) 해 그림자")]
    public bool adjustSunShadow = true;
    public float sunShadowBias = 0.1f;
    public float sunShadowNormalBias = 1.0f;

    [Header("3) 날의 그림자")]
    [Tooltip("외관 뷰에서 날이 그림자를 만들게 할지(끄면 훨씬 가벼워요)")] public bool exteriorBladesCastShadows = false;
    [Tooltip("실내 뷰에서 날이 그림자를 만들게 할지(날 그림자 무늬가 보여요)")] public bool interiorBladesCastShadows = true;

    [Header("4) 안 보이는 면 끄기")]
    public bool cullBackFaces = true;
    [Tooltip("면을 켜 두는 여유(m)")] public float cullMargin = 3f;
    [Tooltip("몇 초마다 검사할지")] public float cullInterval = 0.25f;

    bool interior;
    float nextCull;
    Renderer[] vertRenderers, horzRenderers;

    void Awake()
    {
#if UNITY_6000_0_OR_NEWER
        if (controller == null) controller = FindAnyObjectByType<SunLouverController>();
#else
        if (controller == null) controller = FindObjectOfType<SunLouverController>();
#endif
    }

    void Start()
    {
        if (cam == null) cam = Camera.main;
        if (antiAliasing > 0) QualitySettings.antiAliasing = antiAliasing;
        if (targetFps > 0) Application.targetFrameRate = targetFps;
        if (adjustSunShadow && controller != null && controller.sun != null)
        {
            controller.sun.shadowBias = sunShadowBias;
            controller.sun.shadowNormalBias = sunShadowNormalBias;
        }
        // 날 렌더러를 모아 두고 그림자 모드를 적용(날 수가 많아서 한 번만 해요)
        if (controller != null)
        {
            vertRenderers = Collect(controller.rig);
            horzRenderers = Collect(controller.rigHorizontal);
        }
        ApplyShadowMode();
    }

    static Renderer[] Collect(LouverRig rig)
    {
        if (rig == null) return new Renderer[0];
        var root = rig.louverRoot != null ? rig.louverRoot : rig.transform;
        return root.GetComponentsInChildren<Renderer>(true);
    }

    /// <summary>실내 뷰이면 true: 날 그림자를 켜고 면 끄기를 멈춰요. 웹의 화면 전환(OnView) 이벤트나 버튼에 연결하세요.</summary>
    public void SetInteriorView(bool on)
    {
        interior = on;
        ApplyShadowMode();
        if (on) ShowAllSides();
    }

    /// <summary>웹이 "exterior" / "interior" / "split"을 보낼 때: interior와 split이면 실내 모드</summary>
    public void OnViewChanged(string view)
    {
        string v = (view ?? "").Trim().ToLowerInvariant();
        SetInteriorView(v == "interior" || v == "split");
    }

    void ApplyShadowMode()
    {
        bool cast = interior ? interiorBladesCastShadows : exteriorBladesCastShadows;
        var mode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
        SetMode(vertRenderers, mode);
        SetMode(horzRenderers, mode);
    }

    static void SetMode(Renderer[] rs, ShadowCastingMode mode)
    {
        if (rs == null) return;
        for (int i = 0; i < rs.Length; i++)
            if (rs[i] != null) rs[i].shadowCastingMode = mode;
    }

    void ShowAllSides()
    {
        if (controller == null) return;
        for (int s = 0; s < 4; s++)
        {
            if (controller.rig != null) controller.rig.SetSideCulled((FacadeSide)s, false);
            if (controller.rigHorizontal != null) controller.rigHorizontal.SetSideCulled((FacadeSide)s, false);
        }
    }

    void Update()
    {
        if (!cullBackFaces || interior || controller == null || cam == null) return;
        if (Time.unscaledTime < nextCull) return;
        nextCull = Time.unscaledTime + cullInterval;
        Cull(controller.rig);
        Cull(controller.rigHorizontal);
    }

    void Cull(LouverRig rig)
    {
        if (rig == null || !rig.gameObject.activeInHierarchy) return;
        Bounds b = rig.AllBounds;
        if (b.size.sqrMagnitude < 0.001f) return;
        Vector3 p = cam.transform.position;
        // 카메라가 건물 바로 위나 안(위에서 내려다보기, 실내 시점)이면 모든 면을 켜 둬요
        float m = cullMargin * 2f;
        bool inside = p.x > b.min.x - m && p.x < b.max.x + m && p.z > b.min.z - m && p.z < b.max.z + m;
        Vector3 c = b.center, e = b.extents;
        rig.SetSideCulled(FacadeSide.East, !inside && p.x < c.x + e.x - cullMargin);
        rig.SetSideCulled(FacadeSide.West, !inside && p.x > c.x - e.x + cullMargin);
        rig.SetSideCulled(FacadeSide.North, !inside && p.z < c.z + e.z - cullMargin);
        rig.SetSideCulled(FacadeSide.South, !inside && p.z > c.z - e.z + cullMargin);
    }
}
