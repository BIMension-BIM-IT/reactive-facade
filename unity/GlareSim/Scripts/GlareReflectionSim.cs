using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

public enum GlareMode { Off, Instant, Daily }
public enum GlareMetric { Energy, Exposure, Peak }
public enum GlareLouverCase { ResponsiveInner, ResponsiveOuter, AllOpen, NoLouver }
/// <summary>날 마감(날 반사값을 정해요). Custom = 직접 입력</summary>
public enum GlareBladeFinish { Custom, Matte, SemiGloss, Metal, Mirror }
public enum GlarePanelContext { Game, Scene, Inspector }

[System.Serializable] public class GlareStringEvent : UnityEvent<string> { }

/// <summary>
/// 우리 건물(유리 + 반응형 루버)에서 나간 햇빛 반사가 주변 건물·지면 어디에 얼마나 닿는지 보여줘요.
/// 반응형 루버 스크립트와 분리되어 있어요. 루버 쪽은 LouverSceneLink가 이름으로 찾아서 읽기만 해요.
///
/// 쓰는 법 (GlareSim/README 참고)
///  1) 빈 오브젝트 "GlareSim"에 이 스크립트를 붙여요. 연결 칸은 비워 두면 자동으로 찾아요.
///  2) Receiver Roots에 주변 건물 모델의 부모를 넣어요(비우면 씬 메시 중 우리 건물을 뺀 전부).
///  3) 재생하면 Game 창·Scene 창·Inspector에 같은 패널이 나와요.
///
/// 색: 보라(약함) → 빨강 → 주황 → 노랑(셈). 현재 시각은 W/m², 하루 누적은 kWh/m² 또는 노출 시간.
/// 광선: 하늘색 = 유리 반사, 주황색 = 루버 날 반사, 연두색 = 외측 유리 반사(반응형 내부 루버).
/// </summary>
public class GlareReflectionSim : MonoBehaviour
{
    [Header("반응형 루버와 연결 (비우면 이름으로 자동으로 찾아요)")]
    [Tooltip("SunLouverController 컴포넌트(끌어다 놓아도 돼요)")]
    public MonoBehaviour louverController;
    [Tooltip("LouverRig 컴포넌트")]
    public MonoBehaviour louverRig;
    [Tooltip("날들이 들어 있는 부모. 비우면 LouverRig의 Louver Root")]
    public Transform louverRootOverride;
    [Tooltip("해(Directional Light). 비우면 컨트롤러의 Sun")]
    public Light sunOverride;
    [Tooltip("이름에 이 글자가 있는 자식은 날로 보지 않아요(LouverRig의 Skip Name Contains도 같이 써요)")]
    public string[] skipNameContains = { "고정" };

    [Header("받는 쪽: 주변 건물·지면")]
    [Tooltip("주변 건물 모델의 부모들. 바로 아래 자식 하나 = 건물 하나로 집계해요. 비우면 씬의 메시 중 우리 건물(루버 범위 안)을 뺀 전부를 써요")]
    public Transform[] receiverRoots;
    [Tooltip("광선을 막기만 하고 결과(많이 받는 곳, 히트맵, CSV, 주변에 닿음)에는 넣지 않는 것(대지·도로·녹지 등). " +
             "Receiver Roots 아래에 있는 것은 받는 쪽으로 봐요")]
    public Transform[] blockerRoots;
    [Tooltip("받는 쪽에서 뺄 오브젝트(우리 건물 본체, 하늘 돔, 사람·나무 등). 루버 부모는 자동으로 빠져요")]
    public Transform[] excludeRoots;
    [Tooltip("평평한 지면을 추가해요(대지 모델에 바닥이 있으면 꺼도 돼요)")]
    public bool addGround = true;
    [Tooltip("켜면 가장 낮은 날의 바닥 높이를 지면으로 써요")]
    public bool groundFromLouvers = true;
    public float groundY = 0f;
    [Tooltip("지면 크기(건물 중심에서 반 변, m)")]
    public float groundHalfSizeM = 1000f;
    [Tooltip("이보다 멀리 가는 반사는 '하늘로'로 셈해요(m)")]
    public float maxRayDistanceM = 2000f;

    [Header("재료")]
    [Tooltip("유리 외부 반사율(수직 입사). 유리 사양서의 '외부 반사율'을 넣어요. 일반 복층 0.10~0.20, 반사유리 0.30 이상")]
    [Range(0f, 0.6f)] public float glassReflectance = 0.15f;
    [Tooltip("루버 날의 거울 같은 반사율(정반사분만). 무광 도장 0.05~0.1, 반광 도장 0.1~0.3, 아노다이징·금속 0.3~0.6, 경면 0.7 이상. 결과가 이 값에 가장 민감해요")]
    [Range(0f, 1f)] public float bladeSpecular = 0.2f;
    [Tooltip("날 마감을 고르면 날 반사값(Blade Specular)을 그 마감의 대표값으로 정해요. 직접 입력이면 위 값을 그대로 써요")]
    public GlareBladeFinish bladeFinish = GlareBladeFinish.Custom;
    public bool includeGlass = true;
    public bool includeBlades = true;
    [Tooltip("날 폭(m) = sun_louver.csv의 날폭w_mm")] public float bladeWidthM = 0.30f;
    [Tooltip("날 간격(m) = sun_louver.csv의 날간격s_mm")] public float bladeSpacingM = 0.35f;
    [Tooltip("유리면에서 날 회전 중심까지(m). 날 폭의 절반보다 커야 해요")] public float bladeCenterToGlassM = 0.25f;
    [Tooltip("이름에 이 글자가 있는 날은 뒤에 유리가 없다고 봐요(옥상 RF 스크린 등)")]
    public string[] noGlassNameContains = { "RF" };

    [Header("외측 유리 (반응형 내부 루버일 때만)")]
    [Tooltip("날 바깥의 외측 유리 오브젝트. 반응형(내부 루버)일 때만 보이고 다른 경우에는 꺼요. 비우면 아래 이름으로 찾아요")]
    public GameObject[] outerGlassObjects;
    [Tooltip("Outer Glass Objects가 비어 있을 때 씬에서 찾을 오브젝트 이름(라이노 레이어 이름)")]
    public string outerGlassName = "외측 저반사유리";
    [Tooltip("외측 유리의 외부 반사율(수직 입사). 저반사유리 사양서 값을 넣어요. 기본값은 임의의 값이에요")]
    [Range(0f, 0.3f)] public float outerGlassReflectance = 0.06f;
    [Tooltip("외측 유리의 일사 투과율(수직 입사). 날·안쪽 유리 반사는 들어올 때와 나갈 때 두 번 곱해요. 기본값은 임의의 값이에요")]
    [Range(0f, 1f)] public float outerGlassTransmittance = 0.8f;

    [Header("해")]
    [Tooltip("0이면 맑은 날 모델(고도로 계산), 0보다 크면 이 값(W/m²)을 직달일사로 써요")]
    public float dniOverride = 0f;
    [Tooltip("켜면 우리 입면에 주변 건물 그림자가 지는지 Unity 모델로 확인해요")]
    public bool facadeShadowRaycast = true;

    [Header("계산")]
    public GlareMode mode = GlareMode.Instant;
    [Tooltip("ResponsiveInner = 지금 루버 각도 + 날 바깥 외측 유리(내부 루버), ResponsiveOuter = 지금 루버 각도, 외측 유리 없음(외부 루버), " +
             "AllOpen = 모두 0도(열림), NoLouver = 루버 없는 유리 건물(비교용, 화면에서 루버 레이어도 숨겨요). AllOpen·NoLouver는 외측 유리 없이 계산해요")]
    public GlareLouverCase louverCase = GlareLouverCase.ResponsiveInner;
    [Tooltip("날 몇 장마다 점 하나를 둘지(1 = 모든 날). 크면 빠르고 거칠어요")]
    [Range(1, 8)] public int bladeStride = 2;
    [Tooltip("날 한 장(층 높이)을 위아래로 몇 점으로 나눌지")]
    [Range(1, 6)] public int samplesPerBladeHeight = 2;
    [Tooltip("받는 쪽 격자 크기(m)")]
    public float cellSizeM = 2f;
    [Tooltip("한 프레임에 계산에 쓰는 시간(ms). 크면 빨리 끝나지만 화면이 버벅여요")]
    public float msPerFrame = 12f;
    [Tooltip("하루 누적에서 볼 시각 범위(시)")]
    public float dayStartHour = 4f, dayEndHour = 21f;

    [Header("표시")]
    public GlareMetric dailyMetric = GlareMetric.Energy;
    public bool showRays = true;
    public int maxRayLines = 2500;
    [Tooltip("우리 입면 위에 '주변에 닿는 반사가 어디서 나오는지'를 칠해요")]
    public bool showSourceMap = false;
    [Tooltip("태양 궤적(sun_louver.csv의 해 길)과 지금 해 위치를 보여요. 패널의 '태양 궤적'으로 켜고 꺼요")]
    public bool showSunPath = true;
    [Tooltip("켜면 지금 대표일 말고 다른 대표일(춘분·하지·추분·동지) 궤적도 흐리게 같이 보여요")]
    public bool sunPathAllDays = true;
    [Tooltip("태양 궤적 반지름(m). 0이면 우리 건물 크기의 1.5배")]
    public float sunPathRadiusM = 0f;
    [Tooltip("켜면 색 최대값을 상위 2% 값으로 자동으로 정해요")]
    public bool autoScale = true;
    [Tooltip("autoScale이 꺼져 있을 때 색 최대값(지금 단위)")]
    public float scaleMax = 300f;
    [Tooltip("노출 시간을 셀 때 기준(W/m², 받는 면). 기본값은 임의의 값이니 프로젝트 기준에 맞게 바꾸세요")]
    public float exposureThresholdWm2 = 50f;
    [Tooltip("비우면 Resources의 Glare/VertexColorUnlit 셰이더로 만들어요")]
    public Material visMaterial;
    [Tooltip("Game 창에 패널을 보여요(G 키로 숨기기/보기)")]
    public bool showGui = true;
    [Tooltip("재생 중 Scene 창에도 같은 패널을 보여요(Inspector에도 같은 조작이 나와요)")]
    public bool showSceneGui = true;
    [Tooltip("WebGL에서 한글이 안 보이면 한글 폰트(예: NotoSansKR)를 넣어요")]
    public Font guiFont;

    [Header("결과 (웹 등으로 보낼 때)")]
    public GlareStringEvent onResultJson;
    [TextArea(2, 8)] public string lastResultJson;

    // ---------------- 내부 상태 ----------------
    LouverSceneLink link;
    GlareReceivers receivers;
    readonly GlareEngine engine = new GlareEngine();
    List<GlareEmitter> emitters;
    int builtStride = -1, builtSamples = -1;
    float builtSpacing, builtWidth;

    GlareGrid grid;
    float[] cellValues;
    float[] sourceValue;
    GlareTotals shownTotals;
    bool shownDaily;
    GlareLouverCase shownCase;
    string shownLabel = "";
    float shownDni, shownScale = 1f, shownMax, shownOverArea, shownMaxExposureH;
    string shownUnit = "W/m²";

    Mesh heatMesh, rayMesh, srcMesh;
    GameObject heatGo, rayGo, srcGo;
    GlareSunPath sunPath;
    GUIStyle sunLabelStyle;
    Texture2D legendTex;
    readonly List<Vector3> rayPts = new List<Vector3>();
    readonly List<Color> rayCols = new List<Color>();

    Coroutine job;
    bool ready, dirty = true, quitting;
    long instantSig = long.MinValue;
    float nextCheck;
    string dailySig = "";
    float progress;
    string status = "준비 중";
    readonly List<string> compareLines = new List<string>();
    GlareCompareJson[] compareJson;
    // 4가지 비교 결과(비교 CSV 저장용). 계산할 때의 조건도 같이 기억해요
    List<GlareLouverCase> compareCases;
    GlareGrid[] compareGrids;
    GlareTotals[] compareTotals;
    string compareLabel, compareConditions;
    readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

    readonly List<GameObject> outerGlass = new List<GameObject>();
    readonly List<bool> outerGlassWasActive = new List<bool>();
    int outerGlassShown = -1;   // -1 = 원래대로, 0 = 끔, 1 = 켬

    Renderer[] louverRenderers;     // 루버 레이어(날 부모) 아래 렌더러 전부
    bool[] louverRendererWasOn;
    bool louversHidden;

    static readonly Color GlassRay = new Color(0.35f, 0.85f, 1f, 0.55f);
    static readonly Color BladeRay = new Color(1f, 0.62f, 0.18f, 0.55f);
    static readonly Color OuterGlassRay = new Color(0.6f, 1f, 0.45f, 0.55f);

    // 패널 버튼 순서 = GlareLouverCase 순서
    static readonly string[] CaseLabels = { "반응형(내부 루버)", "반응형(외부 루버)", "모두 열림", "루버 없음" };
    static readonly string[] CaseKeys = { "inner", "outer", "open", "none" };
    // 날 마감별 날 반사값: README 범위의 가운데(무광 0.05~0.1, 반광 0.1~0.3, 금속 0.3~0.6, 경면 0.7 이상)
    static readonly string[] FinishLabels = { "직접 입력", "무광 도장", "반광 도장", "금속·아노다이징", "경면(거울)" };
    static readonly string[] FinishKeys = { "custom", "matte", "semigloss", "metal", "mirror" };
    static readonly float[] FinishValues = { -1f, 0.08f, 0.2f, 0.45f, 0.8f };
    GlareBladeFinish lastFinish = GlareBladeFinish.Custom;
    float sliderValue;
    bool sliderDragging;

    // ---------------- 에디터(Scene 창·Inspector)에서 읽는 값 ----------------

    /// <summary>씬에서 지금 돌고 있는 반사광 시뮬레이션(Scene 창 패널이 찾을 때 써요)</summary>
    public static GlareReflectionSim Active { get; private set; }
    public bool Ready { get { return ready; } }
    /// <summary>태양 궤적 글자(시각·방위). 켜져 있지 않으면 null(Scene 창 글자용)</summary>
    public List<GlareSunPath.Label> SunPathLabels { get { return showSunPath && sunPath != null ? sunPath.labels : null; } }
    public bool IsBusy { get { return job != null; } }
    /// <summary>결과나 상태가 바뀔 때마다 늘어요(에디터 창을 다시 그릴지 판단)</summary>
    public int ResultVersion { get; private set; }

    // ---------------- 시작 ----------------

    void Start()
    {
        Application.quitting += OnQuit;
        link = new LouverSceneLink();
        link.Setup(louverController, louverRig, louverRootOverride, sunOverride, skipNameContains, noGlassNameContains);
        Debug.Log(link.Summary());
        foreach (var w in link.warnings) Debug.LogWarning("[Glare] " + w);
        if (link.units.Count == 0)
        {
            Debug.LogError("[Glare] 루버 날을 하나도 못 찾았어요. Louver Rig 또는 Louver Root Override를 넣어 주세요.");
            enabled = false;
            return;
        }
        BuildReceivers();
        FindOuterGlass();
        louverRenderers = link.root.GetComponentsInChildren<Renderer>(true);
        SetupVisuals();
        ready = true;
        Active = this;
    }

    void OnQuit() { quitting = true; }

    void OnDisable()
    {
        // 꺼지면 코루틴이 멈춰요 → 하루 누적 중이었으면 루버 시각을 원래대로 돌리고, 다시 켜질 때 새로 계산해요
        if (link != null) link.EndDrive();
        if (!quitting)
        {
            RestoreOuterGlass();
            RestoreLouvers();
        }
        if (sunPath != null) sunPath.SetVisible(false);
        job = null;
        dirty = true;
    }

    void FindOuterGlass()
    {
        outerGlass.Clear();
        outerGlassWasActive.Clear();
        if (outerGlassObjects != null)
            foreach (var g in outerGlassObjects) if (g != null) outerGlass.Add(g);
        if (outerGlass.Count == 0 && !string.IsNullOrEmpty(outerGlassName))
        {
            var scene = link.root != null ? link.root.gameObject.scene : gameObject.scene;
            foreach (var r in scene.GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                    if (t.name == outerGlassName) outerGlass.Add(t.gameObject);
        }
        foreach (var g in outerGlass) outerGlassWasActive.Add(g.activeSelf);
        if (outerGlass.Count > 0)
            Debug.Log("[Glare] 외측 유리 " + outerGlass.Count + "개(" + outerGlass[0].name + "): 반응형(내부 루버)일 때만 보여요");
        else
            Debug.LogWarning("[Glare] 외측 유리 오브젝트('" + outerGlassName + "')를 못 찾았어요. 계산은 되지만 화면에서 켜고 끄지는 않아요. Outer Glass Objects에 넣어 주세요.");
    }

    /// <summary>외측 유리를 반응형(내부 루버)일 때만 보여요. 끔 모드에서는 원래대로 둬요.</summary>
    void UpdateOuterGlassVisual()
    {
        if (outerGlass.Count == 0) return;
        int want = mode == GlareMode.Off ? -1 : louverCase == GlareLouverCase.ResponsiveInner ? 1 : 0;
        if (want == outerGlassShown) return;
        if (want < 0) { RestoreOuterGlass(); return; }
        foreach (var g in outerGlass) if (g != null) g.SetActive(want == 1);
        outerGlassShown = want;
    }

    void RestoreOuterGlass()
    {
        for (int i = 0; i < outerGlass.Count; i++)
            if (outerGlass[i] != null) outerGlass[i].SetActive(outerGlassWasActive[i]);
        outerGlassShown = -1;
    }

    /// <summary>
    /// 루버 없음일 때 루버 레이어(날 부모 아래 전부)를 화면에서 숨겨요. 끔 모드와 다른 경우에는 원래대로 둬요.
    /// 오브젝트는 그대로 두고 렌더러만 꺼요. 오브젝트를 끄면 루버 컨트롤러가 다시 켜거나,
    /// 4가지 비교에서 반응형까지 날이 없다고 계산하기 때문이에요.
    /// </summary>
    // 태양 궤적: 대표일이 바뀔 때만 다시 만들고, 지금 해 위치는 매 프레임 따라가요(하루 누적 중에도 움직여요)
    void UpdateSunPath()
    {
        if (sunPath == null) return;
        sunPath.SetVisible(showSunPath);
        if (!showSunPath || !link.ControllerReady()) return;
        sunPath.Rebuild(link, link.DayName(), sunPathAllDays);
        Vector3 sd;
        bool up = link.SunUp(out sd);
        sunPath.UpdateSun(up, sd);
    }

    void UpdateLouverVisual()
    {
        bool hide = mode != GlareMode.Off && louverCase == GlareLouverCase.NoLouver;
        if (hide == louversHidden || louverRenderers == null) return;
        if (!hide) { RestoreLouvers(); return; }
        louverRendererWasOn = new bool[louverRenderers.Length];
        for (int i = 0; i < louverRenderers.Length; i++)
        {
            var r = louverRenderers[i];
            if (r == null) continue;
            louverRendererWasOn[i] = r.enabled;
            r.enabled = false;
        }
        louversHidden = true;
    }

    void RestoreLouvers()
    {
        if (!louversHidden || louverRenderers == null) return;
        for (int i = 0; i < louverRenderers.Length; i++)
            if (louverRenderers[i] != null) louverRenderers[i].enabled = louverRendererWasOn[i];
        louversHidden = false;
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
        Application.quitting -= OnQuit;
        if (link != null) link.EndDrive();
        if (!quitting && receivers != null) receivers.Dispose();
        if (!quitting && sunPath != null) sunPath.Dispose();
    }

    void OnValidate()
    {
        // Inspector에서 마감을 바꾸면 값을 맞추고, 값을 직접 바꾸면 '직접 입력'으로 돌려요
        if (bladeFinish != lastFinish)
        {
            if (bladeFinish != GlareBladeFinish.Custom) bladeSpecular = FinishValues[(int)bladeFinish];
            lastFinish = bladeFinish;
        }
        else if (bladeFinish != GlareBladeFinish.Custom && Mathf.Abs(bladeSpecular - FinishValues[(int)bladeFinish]) > 1e-4f)
        {
            bladeFinish = lastFinish = GlareBladeFinish.Custom;
        }
        dirty = true;
    }

    void EnsureEmitters()
    {
        if (emitters != null && builtStride == bladeStride && builtSamples == samplesPerBladeHeight &&
            builtSpacing == bladeSpacingM && builtWidth == bladeWidthM) return;
        emitters = new List<GlareEmitter>();
        int stride = Mathf.Max(1, bladeStride), vs = Mathf.Max(1, samplesPerBladeHeight);
        for (int i = 0; i < link.units.Count; i += stride)
        {
            var u = link.units[i];
            Vector3 c = u.center + u.n * (bladeWidthM * 0.5f + 0.05f);   // 날 바깥 끝보다 조금 밖
            float dh = u.height / vs;
            for (int v = 0; v < vs; v++)
            {
                emitters.Add(new GlareEmitter
                {
                    pos = c + Vector3.up * (-0.5f * u.height + (v + 0.5f) * dh),
                    side = u.side,
                    unit = i,
                    area = stride * bladeSpacingM * dh,
                    width = stride * bladeSpacingM,
                    height = dh,
                    glass = u.glass
                });
            }
        }
        builtStride = bladeStride; builtSamples = samplesPerBladeHeight;
        builtSpacing = bladeSpacingM; builtWidth = bladeWidthM;
        Debug.Log("[Glare] 반사 계산점 " + emitters.Count + "개 (날 " + link.units.Count + "장, " + stride + "장마다, 높이 " + vs + "점)");
    }

    static bool IsUnder(Transform t, List<Transform> roots)
    {
        for (var p = t; p != null; p = p.parent)
            if (roots.Contains(p)) return true;
        return false;
    }

    static string GroupName(Transform t, Transform root)
    {
        if (t == root) return t.name;
        var p = t;
        while (p.parent != null && p.parent != root) p = p.parent;
        return p.name;
    }

    void BuildReceivers()
    {
        receivers = GlareReceivers.Create();
        var env = link.envelope;
        var self = env;
        self.Expand(new Vector3(8f, 4f, 8f));

        var excluded = new List<Transform> { transform };
        if (link.root != null) excluded.Add(link.root);
        if (link.rig != null) excluded.Add(link.rig.transform);
        if (excludeRoots != null) foreach (var t in excludeRoots) if (t != null) excluded.Add(t);

        var roots = new List<Transform>();
        if (receiverRoots != null) foreach (var t in receiverRoots) if (t != null) roots.Add(t);
        bool explicitRoots = roots.Count > 0;

        // 막기만 하는 것(대지·도로 등): Receiver Roots 아래에 있는 것은 빼고 넣어요
        var blockers = new List<Transform>();
        if (blockerRoots != null) foreach (var t in blockerRoots) if (t != null) blockers.Add(t);
        foreach (var root in blockers)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                if (!IsUnder(mf.transform, excluded) && !IsUnder(mf.transform, roots)) receivers.AddMesh(mf, null, true);
            foreach (var tr in root.GetComponentsInChildren<Terrain>())
                if (!IsUnder(tr.transform, roots)) receivers.AddTerrain(tr, null, true);
        }

        int added = 0, skippedSelf = 0;
        if (explicitRoots)
        {
            foreach (var root in receiverRoots)
            {
                if (root == null) continue;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                {
                    if (IsUnder(mf.transform, excluded)) continue;
                    if (receivers.AddMesh(mf, GroupName(mf.transform, root))) added++;
                }
                foreach (var tr in root.GetComponentsInChildren<Terrain>())
                    if (receivers.AddTerrain(tr, GroupName(tr.transform, root))) added++;
            }
        }
        else
        {
            foreach (var mf in FindObjectsOfType<MeshFilter>())
            {
                if (IsUnder(mf.transform, excluded) || IsUnder(mf.transform, blockers)) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                var b = mr.bounds;
                if (self.Contains(b.min) && self.Contains(b.max)) { skippedSelf++; continue; }   // 우리 건물 안쪽
                if (receivers.AddMesh(mf, mf.name)) added++;
            }
            foreach (var tr in Terrain.activeTerrains)
                if (!IsUnder(tr.transform, blockers) && receivers.AddTerrain(tr, tr.name)) added++;
        }

        if (addGround)
        {
            float y = groundFromLouvers ? env.min.y : groundY;
            receivers.AddGround(env.center, y, groundHalfSizeM, "지면");
        }
        receivers.Sync();

        Debug.Log("[Glare] 받는 쪽 콜라이더 " + (receivers.colliderCount - receivers.blockerCount) + "개, 그룹 " + receivers.names.Count + "개" +
                  (receivers.blockerCount > 0 ? ", 막기만 하는 것 " + receivers.blockerCount + "개" : "") +
                  ", 삼각형 " + receivers.triangleCount + "개" + (explicitRoots ? "" : " (자동 수집, 우리 건물로 보고 뺀 것 " + skippedSelf + "개)") +
                  (addGround ? ", 지면 포함" : ""));
        if (receivers.unreadableCount > 0)
            Debug.LogWarning("[Glare] Read/Write가 꺼진 메시 " + receivers.unreadableCount +
                             "개. 에디터에서는 되지만 빌드(WebGL)에서는 빠져요. FBX Import Settings → Model → Read/Write를 켜 주세요.");
        if (added == 0 && !addGround)
            Debug.LogWarning("[Glare] 받는 쪽이 하나도 없어요. Receiver Roots를 넣거나 Add Ground를 켜 주세요.");
    }

    GameObject MakeVis(string name, Material mat, out Mesh mesh)
    {
        var go = new GameObject(name);
        mesh = new Mesh { name = name };
        mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go;
    }

    void SetupVisuals()
    {
        var mat = visMaterial;
        if (mat == null)
        {
            var sh = Shader.Find("Glare/VertexColorUnlit");
            if (sh == null)
            {
                Debug.LogWarning("[Glare] Glare/VertexColorUnlit 셰이더를 못 찾았어요. GlareSim/Resources 폴더에 GlareVertexColor.shader가 있는지 확인해 주세요.");
                sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("UI/Default");
            }
            if (sh == null)
            {
                Debug.LogError("[Glare] 표시용 셰이더가 없어요. Vis Material 칸에 정점 색을 쓰는 머티리얼을 넣어 주세요.");
                sh = Shader.Find("Standard");
            }
            mat = new Material(sh);
        }
        heatGo = MakeVis("Glare_Heatmap", mat, out heatMesh);
        rayGo = MakeVis("Glare_Rays", mat, out rayMesh);
        srcGo = MakeVis("Glare_FacadeSources", mat, out srcMesh);
        sunPath = new GlareSunPath(mat, link.envelope, sunPathRadiusM);
        sunPath.SetVisible(false);
        legendTex = GlareVis.LegendTexture();
    }

    // ---------------- 매 프레임 ----------------

    void Update()
    {
        if (!ready) return;
        if (link == null)
        {
            // 재생 중에 스크립트가 다시 컴파일되면 루버 연결(직렬화 안 되는 값)이 사라져요
            ready = false;
            Debug.LogWarning("[Glare] 재생 중에 스크립트가 다시 컴파일되어 루버 연결이 끊겼어요. 재생을 멈췄다가 다시 시작해 주세요.");
            return;
        }
        UpdateOuterGlassVisual();
        UpdateLouverVisual();
        UpdateSunPath();
        if (mode == GlareMode.Off)
        {
            SetVisible(false);
            return;
        }
        if (job != null) return;   // 계산 중이면 끝날 때까지 기다려요

        if (mode == GlareMode.Instant)
        {
            // 해·날이 바뀌었는지 0.2초마다 확인해요(웹·슬라이더·자동 재생 모두 따라가요)
            if (!dirty && Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.2f;
            long sig = link.StateSignature(5);
            if (dirty || sig != instantSig)
            {
                instantSig = sig;
                dirty = false;
                job = StartCoroutine(InstantJob());
            }
        }
        else
        {
            if (!link.CanDrive)
            {
                if (status != "하루 누적 불가") Debug.LogWarning("[Glare] 하루 누적은 " + LouverSceneLink.ControllerTypeName + "의 hour·externalControl이 필요해요.");
                status = "하루 누적 불가";
                return;
            }
            if (!link.ControllerReady()) return;
            string sig = link.DaySignature() + "|" + louverCase;
            if (dirty || sig != dailySig)
            {
                dailySig = sig;
                dirty = false;
                job = StartCoroutine(SweepJob(new List<GlareLouverCase> { louverCase }, false));
            }
        }
    }

    void Configure()
    {
        engine.glassReflectance = glassReflectance;
        engine.bladeSpecular = bladeSpecular;
        engine.outerGlassReflectance = outerGlassReflectance;
        engine.outerGlassTransmittance = outerGlassTransmittance;
        engine.includeGlass = includeGlass;
        engine.includeBlades = includeBlades;
        engine.shadowRaycast = facadeShadowRaycast;
        engine.maxDist = maxRayDistanceM;
        engine.raycaster = receivers;
        engine.array.spacing = bladeSpacingM;
        engine.array.width = bladeWidthM;
        engine.array.centerOffset = Mathf.Max(bladeCenterToGlassM, bladeWidthM * 0.5f + 0.01f);
    }

    static void SunAngles(Vector3 sunDir, out float altitude, out float azimuth)
    {
        altitude = Mathf.Asin(Mathf.Clamp(sunDir.y, -1f, 1f)) * Mathf.Rad2Deg;
        azimuth = Mathf.Repeat(Mathf.Atan2(sunDir.x, sunDir.z) * Mathf.Rad2Deg, 360f);   // 정북 0, 시계방향(동 90)
    }

    // ---------------- 계산 ----------------

    /// <summary>
    /// 한 시각을 계산해요(점마다 해·날 각도 → 반사 → 광선). 시간이 오래 걸리면 프레임을 나눠요.
    /// 날 각도는 이 시각을 시작할 때 link.ReadAngles()로 읽어 둔 값을 써요.
    /// </summary>
    IEnumerator ComputeStep(Vector3 sunDir, GlareLouverCase lc, GlareGrid g, GlareTotals step, float[] src, float srcScale,
                            float progress0, float progressSpan, string what)
    {
        float alt, az;
        SunAngles(sunDir, out alt, out az);
        float dni = dniOverride > 0f ? dniOverride : GlareOptics.ClearSkyDNI(alt);
        engine.BeginStep(sunDir, dni);
        step.Clear();

        bool prevBack = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;   // 법선이 뒤집힌 Rhino 메시도 맞게
        var units = link.units;
        for (int i = 0; i < emitters.Count; i++)
        {
            var e = emitters[i];
            var u = units[e.unit];
            bool blades = lc != GlareLouverCase.NoLouver && u.active;      // 숨긴 날(북면 루버 끔 등)은 없다고 봐요
            float ang = IsResponsive(lc) ? u.angle : 0f;
            float got = engine.ProcessEmitter(e, ang, blades, lc == GlareLouverCase.ResponsiveInner, g, step);
            src[i] += got * srcScale;
            if (sw.ElapsedMilliseconds > msPerFrame)
            {
                progress = progress0 + progressSpan * i / emitters.Count;
                status = what + Mathf.RoundToInt(progress * 100f) + "%";
                Physics.queriesHitBackfaces = prevBack;
                yield return null;
                prevBack = Physics.queriesHitBackfaces;
                Physics.queriesHitBackfaces = true;
                sw.Reset(); sw.Start();
            }
        }
        Physics.queriesHitBackfaces = prevBack;
    }

    /// <summary>현재 시각: 지금 화면의 해와 날 각도 그대로 계산해요.</summary>
    IEnumerator InstantJob()
    {
        EnsureEmitters();
        Configure();
        var g = new GlareGrid(cellSizeM);
        var total = new GlareTotals();
        var src = new float[emitters.Count];
        var pts = new List<Vector3>();
        var cols = new List<Color>();
        int every = Mathf.Max(1, emitters.Count / Mathf.Max(1, maxRayLines)), counter = 0, cap = maxRayLines;
        engine.onRay = !showRays ? null : (GlareEngine.RayCallback)((from, to, p, source) =>
        {
            if ((counter++ % every) != 0 || pts.Count / 2 >= cap) return;
            pts.Add(from); pts.Add(to);
            var c = source == GlareRaySource.Blade ? BladeRay : source == GlareRaySource.OuterGlass ? OuterGlassRay : GlassRay;
            cols.Add(c); cols.Add(new Color(c.r, c.g, c.b, c.a * 0.6f));
        });

        Vector3 sd;
        bool up = link.SunUp(out sd);
        string label = link.TimeLabel();
        float dni = 0f;
        sw.Reset(); sw.Start();
        if (up)
        {
            link.ReadAngles();
            var it = ComputeStep(sd, louverCase, g, total, src, 1f, 0f, 1f, "계산 중 ");
            while (it.MoveNext()) yield return it.Current;
            dni = engine.Dni;
            float alt, az;
            SunAngles(sd, out alt, out az);
            label = string.Format("{0} · 고도 {1:0}° 방위 {2:0}°", label, alt, az);
        }
        else label += " · 해 없음";
        g.EndStep(10f, exposureThresholdWm2);
        engine.onRay = null;

        rayPts.Clear(); rayPts.AddRange(pts);
        rayCols.Clear(); rayCols.AddRange(cols);
        Show(g, total, false, louverCase, src, dni, label);
        job = null;
    }

    /// <summary>
    /// 하루 누적: 컨트롤러 시각을 10분씩 돌려서 루버·해가 실제로 움직인 상태를 읽고, 경우(cases)마다 계산해요.
    /// compare가 true면 끝나고 비교표를 만들어요. 끝나면 컨트롤러 시각을 원래대로 돌려놔요.
    /// </summary>
    IEnumerator SweepJob(List<GlareLouverCase> cases, bool compare)
    {
        EnsureEmitters();
        Configure();
        engine.onRay = null;
        int n = cases.Count;
        var grids = new GlareGrid[n];
        var totals = new GlareTotals[n];
        var srcs = new float[n][];
        var step = new GlareTotals();
        for (int c = 0; c < n; c++)
        {
            grids[c] = new GlareGrid(cellSizeM);
            totals[c] = new GlareTotals();
            srcs[c] = new float[emitters.Count];
        }

        const float dt = 10f;   // 분
        int m0 = Mathf.RoundToInt(dayStartHour * 60f / dt) * (int)dt, m1 = Mathf.RoundToInt(dayEndHour * 60f);
        int count = Mathf.Max(1, (m1 - m0) / (int)dt + 1), done = 0, sunSteps = 0;
        string day = link.DayName();
        string what = compare ? "4가지 비교 " : "하루 누적 ";

        link.BeginDrive();
        sw.Reset(); sw.Start();
        for (int m = m0; m <= m1; m += (int)dt, done++)
        {
            link.DriveTo(m / 60f);
            yield return null;   // 다음 프레임에 컨트롤러가 이 시각으로 해와 루버를 돌려요
            sw.Reset(); sw.Start();
            progress = (float)done / count;
            status = what + Mathf.RoundToInt(progress * 100f) + "%";

            Vector3 sd;
            if (!link.SunUp(out sd)) continue;
            link.ReadAngles();
            sunSteps++;
            for (int c = 0; c < n; c++)
            {
                var it = ComputeStep(sd, cases[c], grids[c], step, srcs[c], dt / 60f,
                                     (done + (float)c / n) / count, 1f / (count * n), what);
                while (it.MoveNext()) yield return it.Current;
                grids[c].EndStep(dt, exposureThresholdWm2);
                totals[c].AddScaled(step, dt / 60.0);
            }
        }
        link.EndDrive();
        yield return null;   // 컨트롤러가 원래 시각으로 돌아갈 때까지
        yield return null;
        instantSig = long.MinValue;   // 현재 시각 모드로 돌아가면 다시 계산하게

        string label = string.Format("{0} 하루 ({1}개 시각, {2:0}분 간격)", day, sunSteps, dt);
        int cur = cases.IndexOf(louverCase);
        if (mode == GlareMode.Daily && cur >= 0)
        {
            rayPts.Clear(); rayCols.Clear();
            Show(grids[cur], totals[cur], true, louverCase, srcs[cur], 0f, label);
            dailySig = link.DaySignature() + "|" + louverCase;
        }
        if (compare)
        {
            BuildCompare(cases, grids, totals, day);
            compareCases = new List<GlareLouverCase>(cases);
            compareGrids = grids;
            compareTotals = totals;
            compareLabel = label;
            var cond = new StringBuilder();
            AppendConditions(cond, label, "4가지 비교(내부 루버 · 외부 루버 · 모두 열림 · 루버 없음)");
            compareConditions = cond.ToString();
        }
        status = compare ? "비교 완료" : "완료";
        ResultVersion++;
        job = null;
    }

    void BuildCompare(List<GlareLouverCase> cases, GlareGrid[] grids, GlareTotals[] totals, string day)
    {
        compareLines.Clear();
        var json = new List<GlareCompareJson>();
        int bi = cases.IndexOf(GlareLouverCase.NoLouver);
        double baseReceived = bi >= 0 ? totals[bi].received : 0;
        var log = new StringBuilder("[Glare] " + day + " 하루 비교 (주변에 닿은 반사 에너지)\n");
        for (int c = 0; c < cases.Count; c++)
        {
            var g = grids[c];
            float maxE = 0f, exposed = 0f;
            foreach (var cell in g.cells)
            {
                float e = (float)(cell.energyWh / g.CellArea / 1000.0);
                if (e > maxE) maxE = e;
                if (cell.exposureMin > 0f) exposed += g.CellArea;
            }
            double kwh = totals[c].received / 1000.0;
            string pct = baseReceived > 0 ? (totals[c].received / baseReceived * 100.0).ToString("0") + "%" : "-";
            string line = string.Format("{0}: {1:0} kWh ({2}) · 최대 {3:0.00} kWh/m² · 노출 {4:0} m²", CaseName(cases[c]), kwh, pct, maxE, exposed);
            compareLines.Add(line);
            log.AppendLine("  " + line);
            json.Add(new GlareCompareJson
            {
                louverCase = cases[c].ToString(),
                receivedKWh = (float)kwh,
                reflectedKWh = (float)(totals[c].Reflected / 1000.0),
                maxKWhPerM2 = maxE,
                exposedAreaM2 = exposed
            });
        }
        compareJson = json.ToArray();
        Debug.Log(log.ToString());
        PublishJson();
    }

    static string CaseName(GlareLouverCase lc)
    {
        switch (lc)
        {
            case GlareLouverCase.ResponsiveOuter: return "반응형(외부 루버)";
            case GlareLouverCase.AllOpen: return "모두 열림(0°)";
            case GlareLouverCase.NoLouver: return "루버 없음";
            default: return "반응형(내부 루버)";
        }
    }

    static bool IsResponsive(GlareLouverCase lc)
    {
        return lc == GlareLouverCase.ResponsiveInner || lc == GlareLouverCase.ResponsiveOuter;
    }

    void CancelJob()
    {
        StopAllCoroutines();   // 안쪽 계산까지 같이 멈춰요
        if (link != null) link.EndDrive();   // 하루 누적 중이었으면 루버 시각을 원래대로
        job = null;
        status = "취소됨";
    }

    // ---------------- 화면 ----------------

    void SetVisible(bool on)
    {
        if (heatGo != null && heatGo.activeSelf != on) heatGo.SetActive(on);
        bool rays = on && showRays && !shownDaily;
        if (rayGo != null && rayGo.activeSelf != rays) rayGo.SetActive(rays);
        bool src = on && showSourceMap;
        if (srcGo != null && srcGo.activeSelf != src) srcGo.SetActive(src);
    }

    void Show(GlareGrid g, GlareTotals totals, bool daily, GlareLouverCase lc, float[] src, float dni, string label)
    {
        grid = g;
        shownTotals = totals;
        shownDaily = daily;
        shownCase = lc;
        sourceValue = src;
        shownDni = dni;
        shownLabel = label;
        Redraw();
        status = "완료";
        PublishJson();
    }

    float CellValue(GlareCell c)
    {
        if (!shownDaily) return c.lastIrr;
        switch (dailyMetric)
        {
            case GlareMetric.Exposure: return c.exposureMin / 60f;
            case GlareMetric.Peak: return c.maxIrr;
            default: return (float)(c.energyWh / grid.CellArea / 1000.0);
        }
    }

    void Redraw()
    {
        if (grid == null || heatMesh == null) return;
        shownUnit = !shownDaily ? "W/m²" : dailyMetric == GlareMetric.Exposure ? "시간" : dailyMetric == GlareMetric.Peak ? "W/m²" : "kWh/m²";

        cellValues = new float[grid.cells.Count];
        shownMax = 0f;
        shownOverArea = 0f;
        shownMaxExposureH = 0f;
        for (int i = 0; i < cellValues.Length; i++)
        {
            var c = grid.cells[i];
            cellValues[i] = CellValue(c);
            if (cellValues[i] > shownMax) shownMax = cellValues[i];
            bool over = shownDaily ? c.exposureMin > 0f : c.lastIrr >= exposureThresholdWm2;
            if (over) shownOverArea += grid.CellArea;
            if (c.exposureMin / 60f > shownMaxExposureH) shownMaxExposureH = c.exposureMin / 60f;
        }
        shownScale = autoScale ? GlareVis.Percentile(cellValues, 0.98f) : Mathf.Max(1e-6f, scaleMax);
        GlareVis.BuildCells(heatMesh, grid.cells, cellValues, shownScale, grid.cellSize, 0.08f);

        if (showRays && !shownDaily) GlareVis.BuildLines(rayMesh, rayPts, rayCols);
        else rayMesh.Clear();

        if (showSourceMap && sourceValue != null && emitters != null && sourceValue.Length == emitters.Count)
        {
            var perArea = new float[emitters.Count];
            for (int i = 0; i < perArea.Length; i++) perArea[i] = sourceValue[i] / Mathf.Max(1e-6f, emitters[i].area);
            GlareVis.BuildSources(srcMesh, emitters, perArea, GlareVis.Percentile(perArea, 0.98f), 0.15f);
        }
        else srcMesh.Clear();

        SetVisible(true);
        ResultVersion++;
    }

    // ---------------- 웹·외부에서 부르는 함수 ----------------

    /// <summary>"off", "instant", "daily"</summary>
    public void SetMode(string m)
    {
        switch ((m ?? "").ToLowerInvariant())
        {
            case "off": mode = GlareMode.Off; break;
            case "daily": mode = GlareMode.Daily; break;
            default: mode = GlareMode.Instant; break;
        }
        CancelJob();
        dirty = true;
    }

    /// <summary>"inner"(반응형 내부 루버), "outer"(반응형 외부 루버), "open", "none". "responsive"는 inner로 봐요</summary>
    public void SetLouverCase(string c)
    {
        switch ((c ?? "").ToLowerInvariant())
        {
            case "outer": case "responsiveouter": case "responsive_outer": louverCase = GlareLouverCase.ResponsiveOuter; break;
            case "open": case "allopen": louverCase = GlareLouverCase.AllOpen; break;
            case "none": case "nolouver": louverCase = GlareLouverCase.NoLouver; break;
            default: louverCase = GlareLouverCase.ResponsiveInner; break;
        }
        CancelJob();
        dirty = true;
    }

    public void Recompute()
    {
        CancelJob();
        dirty = true;
    }

    /// <summary>날 마감: "matte"(무광 도장), "semigloss"(반광 도장), "metal"(금속·아노다이징), "mirror"(경면). 날 반사값을 그 대표값으로 바꾸고 다시 계산해요</summary>
    public void SetBladeFinish(string f)
    {
        int i = System.Array.IndexOf(FinishKeys, (f ?? "").Trim().ToLowerInvariant());
        if (i <= 0) return;   // 모르는 이름이거나 custom이면 그대로
        SetBladeFinish((GlareBladeFinish)i, bladeSpecular);
    }

    /// <summary>날 반사값을 직접 정해요(0~1). 마감은 '직접 입력'이 돼요</summary>
    public void SetBladeSpecular(float v)
    {
        SetBladeFinish(GlareBladeFinish.Custom, v);
    }

    void SetBladeFinish(GlareBladeFinish f, float customValue)
    {
        float v = f == GlareBladeFinish.Custom ? Mathf.Clamp01(customValue) : FinishValues[(int)f];
        if (f == bladeFinish && Mathf.Approximately(v, bladeSpecular)) return;
        bladeFinish = lastFinish = f;
        bladeSpecular = v;
        Recompute();
    }

    /// <summary>지금 대표일 하루를 네 경우(루버 없음 / 모두 열림 / 반응형 외부 루버 / 반응형 내부 루버)로 한 번에 계산해요.</summary>
    public void RunCompare()
    {
        if (!ready) return;
        if (!link.CanDrive || !link.ControllerReady())
        {
            status = "비교 불가(루버 컨트롤러 필요)";
            return;
        }
        CancelJob();
        job = StartCoroutine(SweepJob(new List<GlareLouverCase>
        {
            GlareLouverCase.NoLouver, GlareLouverCase.AllOpen, GlareLouverCase.ResponsiveOuter, GlareLouverCase.ResponsiveInner
        }, true));
    }

    // ---------------- 결과 JSON / CSV ----------------

    void PublishJson()
    {
        if (shownTotals == null) return;
        double k = 1.0 / 1000.0;   // W → kW, Wh → kWh
        var j = new GlareResultJson
        {
            mode = shownDaily ? "daily" : "instant",
            louverCase = shownCase.ToString(),
            label = shownLabel,
            unit = shownDaily ? "kWh" : "kW",
            dni = shownDni,
            incident = (float)(shownTotals.incident * k),
            reflectedGlass = (float)(shownTotals.glass * k),
            reflectedBlade = (float)(shownTotals.blade * k),
            reflectedOuterGlass = (float)(shownTotals.outerGlass * k),
            received = (float)(shownTotals.received * k),
            blocked = (float)(shownTotals.blocked * k),
            toSky = (float)(shownTotals.toSky * k),
            mapUnit = shownUnit,
            mapMax = shownMax,
            areaOverThresholdM2 = shownOverArea,
            receivers = TopReceivers(20),
            compare = compareJson
        };
        lastResultJson = JsonUtility.ToJson(j);
        if (onResultJson != null) onResultJson.Invoke(lastResultJson);
    }

    GlareReceiverJson[] TopReceivers(int max)
    {
        var outList = new List<GlareReceiverJson>();
        if (shownTotals == null || receivers == null) return outList.ToArray();
        var list = new List<KeyValuePair<int, double>>(shownTotals.perReceiver);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        for (int i = 0; i < list.Count && i < max; i++)
            outList.Add(new GlareReceiverJson { name = receivers.ReceiverName(list[i].Key), value = (float)(list[i].Value / 1000.0) });
        return outList.ToArray();
    }

    public void ExportCsv()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.LogWarning("[Glare] WebGL에서는 파일 저장이 안 돼요. 에디터나 PC 빌드에서 저장하세요.");
#else
        if (grid == null || cellValues == null) return;
        string dir = ResultDir();
        string stamp = (shownDaily ? "daily_" : "instant_") + shownCase + "_" + System.DateTime.Now.ToString("MMdd_HHmmss");
        string th = exposureThresholdWm2.ToString("0", Inv);

        // 칸별 결과: 맨 위에 조건과 열 설명(#으로 시작하는 줄), 그 아래 표
        var sb = new StringBuilder();
        sb.AppendLine("# GlareSim 반사광 결과 - 칸별 (" + (shownDaily ? "하루 누적" : "현재 시각") + ")");
        AppendConditions(sb, shownLabel, CaseName(shownCase));
        sb.AppendLine("# 열 설명");
        Note(sb, "x / y / z", "반사광이 떨어진 " + CellText() + " 칸의 위치(Unity 좌표, m)");
        Note(sb, "nx / ny / nz", "그 면이 향하는 방향. ny≈1이면 지붕(지면), ny≈0이면 벽");
        Note(sb, "receiver", "받은 건물 이름(Rhino 객체 이름)");
        if (shownDaily)
        {
            Note(sb, "value", "패널에서 고른 하루 지표(단위는 unit 열): 에너지 kWh/m² · 노출 시간 시간 · 최대 세기 W/m²");
            Note(sb, "unit", "value의 단위");
            Note(sb, "energy_kWh_m2", "하루 동안 1 m²에 쌓인 에너지 합(kWh/m²)");
            Note(sb, "exposure_h", "하루 중 세기가 " + th + " W/m² 이상이었던 시간 합(시간, 10분 단위)");
            Note(sb, "peak_Wm2", "하루 중 가장 셌던 순간의 세기(W/m²)");
            Note(sb, "last_Wm2", "계산한 마지막 시각의 세기(W/m²). 해 질 무렵이라 보통 작아요");
        }
        else
        {
            Note(sb, "value / last_Wm2", "그 순간 칸에 닿는 세기(W/m²)");
            Note(sb, "unit", "value의 단위");
            Note(sb, "energy_kWh_m2", "10분 동안 쌓인 에너지(kWh/m²)");
            Note(sb, "exposure_h", th + " W/m² 이상이면 0.17(=10분), 아니면 0");
            Note(sb, "peak_Wm2", "최대 세기(W/m²). 현재 시각 모드에서는 value와 같아요");
        }
        sb.AppendLine("#");
        sb.AppendLine("x,y,z,nx,ny,nz,receiver,value,unit,energy_kWh_m2,exposure_h,peak_Wm2,last_Wm2");
        for (int i = 0; i < grid.cells.Count; i++)
        {
            var c = grid.cells[i];
            Vector3 p = c.Position, nn = c.Normal;
            sb.AppendLine(string.Format(Inv, "{0:0.00},{1:0.00},{2:0.00},{3:0.000},{4:0.000},{5:0.000},{6},{7:0.####},{8},{9:0.####},{10:0.##},{11:0.#},{12:0.#}",
                p.x, p.y, p.z, nn.x, nn.y, nn.z, Csv(receivers.ReceiverName(c.receiver)), cellValues[i], shownUnit,
                c.energyWh / grid.CellArea / 1000.0, c.exposureMin / 60f, c.maxIrr, c.lastIrr));
        }
        string cellsPath = Path.Combine(dir, "glare_cells_" + stamp + ".csv");
        File.WriteAllText(cellsPath, sb.ToString(), new UTF8Encoding(true));

        // 건물별 결과
        string u = shownDaily ? "kWh" : "kW";
        var stats = ReceiverStats(grid);
        sb.Length = 0;
        sb.AppendLine("# GlareSim 반사광 결과 - 건물별 (" + (shownDaily ? "하루 누적" : "현재 시각") + ")");
        AppendConditions(sb, shownLabel, CaseName(shownCase));
        sb.AppendLine("# 열 설명");
        Note(sb, "receiver", "받은 건물 이름(Rhino 객체 이름)");
        Note(sb, u, shownDaily ? "하루 동안 그 건물에 닿은 반사 에너지 합(kWh)" : "그 순간 그 건물에 닿은 반사 전력 합(kW)");
        Note(sb, "cells / area_m2", "반사광이 닿은 칸 수와 면적(칸 1개 = " + CellText() + ")");
        Note(sb, "peak_Wm2", "그 건물에서 가장 센 칸의 세기(W/m²)");
        sb.AppendLine("#");
        sb.AppendLine("receiver," + u + ",cells,area_m2,peak_Wm2");
        foreach (var kv in SortedReceivers(shownTotals))
        {
            KeyValuePair<int, float> st;   // 칸 수, 최대 세기
            stats.TryGetValue(kv.Key, out st);
            sb.AppendLine(string.Format(Inv, "{0},{1:0.###},{2},{3:0},{4:0.#}", Csv(receivers.ReceiverName(kv.Key)), kv.Value / 1000.0,
                st.Key, st.Key * grid.CellArea, st.Value));
        }
        File.WriteAllText(Path.Combine(dir, "glare_receivers_" + stamp + ".csv"), sb.ToString(), new UTF8Encoding(true));
        Debug.Log("[Glare] 저장했어요: " + Path.GetFullPath(cellsPath));
        status = "CSV 저장함";
#endif
    }

    /// <summary>
    /// "4가지 비교" 결과를 CSV 3개로 저장해요(같은 하루, 같은 조건).
    ///  - glare_compare_summary: 경우마다 한 줄(합계·최대·노출 면적)
    ///  - glare_compare_cells: 같은 칸을 한 줄로 놓고 경우별 값을 옆으로(같은 위치끼리 비교)
    ///  - glare_compare_receivers: 건물마다 한 줄, 경우별 값을 옆으로
    /// </summary>
    public void ExportCompareCsv()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.LogWarning("[Glare] WebGL에서는 파일 저장이 안 돼요. 에디터나 PC 빌드에서 저장하세요.");
#else
        if (compareGrids == null || compareTotals == null || compareCases == null)
        {
            status = "먼저 '4가지 비교'를 눌러 주세요";
            return;
        }
        string dir = ResultDir();
        string stamp = System.DateTime.Now.ToString("MMdd_HHmmss");
        string th = exposureThresholdWm2.ToString("0", Inv);
        // 열 순서: 내부 루버, 외부 루버, 모두 열림, 루버 없음
        var order = new List<int>();
        foreach (var lc in new[] { GlareLouverCase.ResponsiveInner, GlareLouverCase.ResponsiveOuter, GlareLouverCase.AllOpen, GlareLouverCase.NoLouver })
        {
            int i = compareCases.IndexOf(lc);
            if (i >= 0) order.Add(i);
        }
        int none = compareCases.IndexOf(GlareLouverCase.NoLouver);
        double baseReceived = none >= 0 ? compareTotals[none].received : 0;
        float area = compareGrids[0].CellArea;
        const double k = 1.0 / 1000.0;   // Wh → kWh

        // 1) 요약
        var sb = new StringBuilder();
        sb.AppendLine("# GlareSim 4가지 비교 - 요약 (하루 누적)");
        sb.Append(compareConditions);
        sb.AppendLine("# 열 설명");
        Note(sb, "case / case_name", "경우(inner = 반응형 내부 루버, outer = 반응형 외부 루버, open = 모두 열림 0°, none = 루버 없음)");
        Note(sb, "received_kWh", "하루 동안 주변 건물에 닿은 반사 에너지 합(kWh)");
        Note(sb, "vs_none_pct", "루버 없음 대비 비율(%). 100보다 작으면 루버 없음보다 덜 반사");
        Note(sb, "reflected_kWh", "건물 밖으로 나간 반사 합(kWh) = 외측 유리 + 안쪽 유리 + 날");
        Note(sb, "outer_glass_kWh / inner_glass_kWh / blade_kWh", "반사가 나온 면별 합(kWh). 외측 유리는 내부 루버일 때만");
        Note(sb, "blocked_kWh / to_sky_kWh", "대지·도로 등에 막힌 반사 / 아무것에도 안 닿고 하늘로 간 반사(kWh)");
        Note(sb, "incident_kWh", "우리 입면(날이 있는 부분)에 들어온 직달일사 합(kWh)");
        Note(sb, "max_energy_kWh_m2", "가장 많이 받은 칸의 하루 에너지(kWh/m²)");
        Note(sb, "peak_Wm2", "하루 중 가장 셌던 칸의 세기(W/m²)");
        Note(sb, "exposed_area_m2", "하루 중 한 번이라도 " + th + " W/m² 이상 받은 면적(m²)");
        Note(sb, "max_exposure_h", "가장 오래 " + th + " W/m² 이상 받은 칸의 시간(시간)");
        Note(sb, "cells", "반사광이 닿은 칸 수(칸 1개 = " + CellText() + ")");
        sb.AppendLine("#");
        sb.AppendLine("case,case_name,received_kWh,vs_none_pct,reflected_kWh,outer_glass_kWh,inner_glass_kWh,blade_kWh,blocked_kWh,to_sky_kWh,incident_kWh,max_energy_kWh_m2,peak_Wm2,exposed_area_m2,max_exposure_h,cells");
        foreach (int c in order)
        {
            var g = compareGrids[c];
            var t = compareTotals[c];
            float maxE = 0f, peak = 0f, exposed = 0f, maxExp = 0f;
            foreach (var cell in g.cells)
            {
                maxE = Mathf.Max(maxE, (float)(cell.energyWh / g.CellArea / 1000.0));
                peak = Mathf.Max(peak, cell.maxIrr);
                maxExp = Mathf.Max(maxExp, cell.exposureMin / 60f);
                if (cell.exposureMin > 0f) exposed += g.CellArea;
            }
            string pct = baseReceived > 0 ? (t.received / baseReceived * 100.0).ToString("0.0", Inv) : "";
            sb.AppendLine(string.Format(Inv, "{0},{1},{2:0.0},{3},{4:0.0},{5:0.0},{6:0.0},{7:0.0},{8:0.0},{9:0.0},{10:0.0},{11:0.###},{12:0.#},{13:0},{14:0.##},{15}",
                CaseKeys[(int)compareCases[c]], CaseName(compareCases[c]), t.received * k, pct, t.Reflected * k, t.outerGlass * k, t.glass * k, t.blade * k,
                t.blocked * k, t.toSky * k, t.incident * k, maxE, peak, exposed, maxExp, g.cells.Count));
        }
        string sumPath = Path.Combine(dir, "glare_compare_summary_" + stamp + ".csv");
        File.WriteAllText(sumPath, sb.ToString(), new UTF8Encoding(true));

        // 2) 칸별: 같은 칸(같은 격자 위치)을 한 줄로
        var keys = new List<long>();
        var at = new Dictionary<long, GlareCell[]>();
        foreach (int c in order)
            foreach (var cell in compareGrids[c].cells)
            {
                long key = CellKey(cell.Position);
                GlareCell[] row;
                if (!at.TryGetValue(key, out row)) { row = new GlareCell[compareCases.Count]; at[key] = row; keys.Add(key); }
                row[c] = cell;
            }
        sb.Length = 0;
        sb.AppendLine("# GlareSim 4가지 비교 - 칸별 (하루 누적)");
        sb.Append(compareConditions);
        sb.AppendLine("# 열 설명");
        Note(sb, "x / y / z", "반사광이 떨어진 " + CellText() + " 칸의 위치(Unity 좌표, m)");
        Note(sb, "nx / ny / nz", "그 면이 향하는 방향. ny≈1이면 지붕(지면), ny≈0이면 벽");
        Note(sb, "receiver", "받은 건물 이름(Rhino 객체 이름)");
        Note(sb, "energy_kWh_m2_경우", "하루 동안 1 m²에 쌓인 에너지 합(kWh/m²). 그 경우에 반사가 안 닿았으면 0");
        Note(sb, "exposure_h_경우", "하루 중 세기가 " + th + " W/m² 이상이었던 시간 합(시간)");
        Note(sb, "peak_Wm2_경우", "하루 중 가장 셌던 순간의 세기(W/m²)");
        Note(sb, "경우 이름", "inner = 반응형 내부 루버, outer = 반응형 외부 루버, open = 모두 열림 0°, none = 루버 없음");
        sb.AppendLine("#");
        sb.Append("x,y,z,nx,ny,nz,receiver");
        foreach (string col in new[] { "energy_kWh_m2", "exposure_h", "peak_Wm2" })
            foreach (int c in order) sb.Append(',').Append(col).Append('_').Append(CaseKeys[(int)compareCases[c]]);
        sb.AppendLine();
        foreach (var key in keys)
        {
            var row = at[key];
            GlareCell any = null;
            foreach (int c in order) if (row[c] != null) { any = row[c]; break; }
            Vector3 p = any.Position, nn = any.Normal;
            sb.Append(string.Format(Inv, "{0:0.00},{1:0.00},{2:0.00},{3:0.000},{4:0.000},{5:0.000},{6}", p.x, p.y, p.z, nn.x, nn.y, nn.z, Csv(receivers.ReceiverName(any.receiver))));
            foreach (int c in order) sb.Append(',').Append(row[c] != null ? (row[c].energyWh / area / 1000.0).ToString("0.####", Inv) : "0");
            foreach (int c in order) sb.Append(',').Append(row[c] != null ? (row[c].exposureMin / 60f).ToString("0.##", Inv) : "0");
            foreach (int c in order) sb.Append(',').Append(row[c] != null ? row[c].maxIrr.ToString("0.#", Inv) : "0");
            sb.AppendLine();
        }
        File.WriteAllText(Path.Combine(dir, "glare_compare_cells_" + stamp + ".csv"), sb.ToString(), new UTF8Encoding(true));

        // 3) 건물별
        var ids = new List<int>();
        foreach (int c in order)
            foreach (var kv in compareTotals[c].perReceiver)
                if (!ids.Contains(kv.Key)) ids.Add(kv.Key);
        int sortBy = order.Count > 0 ? order[0] : 0;
        ids.Sort((a, b) => Get(compareTotals[sortBy].perReceiver, b).CompareTo(Get(compareTotals[sortBy].perReceiver, a)));
        var stats = new Dictionary<int, int[]>[compareCases.Count];
        foreach (int c in order) stats[c] = ExposedCells(compareGrids[c]);
        sb.Length = 0;
        sb.AppendLine("# GlareSim 4가지 비교 - 건물별 (하루 누적)");
        sb.Append(compareConditions);
        sb.AppendLine("# 열 설명");
        Note(sb, "receiver", "받은 건물 이름(Rhino 객체 이름). 첫 번째 경우(내부 루버)에서 많이 받은 순서");
        Note(sb, "kWh_경우", "하루 동안 그 건물에 닿은 반사 에너지 합(kWh)");
        Note(sb, "exposed_m2_경우", "그 건물에서 하루 중 한 번이라도 " + th + " W/m² 이상 받은 면적(m²)");
        Note(sb, "경우 이름", "inner = 반응형 내부 루버, outer = 반응형 외부 루버, open = 모두 열림 0°, none = 루버 없음");
        sb.AppendLine("#");
        sb.Append("receiver");
        foreach (int c in order) sb.Append(",kWh_").Append(CaseKeys[(int)compareCases[c]]);
        foreach (int c in order) sb.Append(",exposed_m2_").Append(CaseKeys[(int)compareCases[c]]);
        sb.AppendLine();
        foreach (int id in ids)
        {
            sb.Append(Csv(receivers.ReceiverName(id)));
            foreach (int c in order) sb.Append(',').Append((Get(compareTotals[c].perReceiver, id) * k).ToString("0.###", Inv));
            foreach (int c in order)
            {
                int[] e;
                sb.Append(',').Append(((stats[c].TryGetValue(id, out e) ? e[0] : 0) * area).ToString("0", Inv));
            }
            sb.AppendLine();
        }
        File.WriteAllText(Path.Combine(dir, "glare_compare_receivers_" + stamp + ".csv"), sb.ToString(), new UTF8Encoding(true));
        Debug.Log("[Glare] 4가지 비교 CSV 3개를 저장했어요: " + Path.GetFullPath(sumPath).Replace("summary", "*"));
        status = "비교 CSV 저장함";
#endif
    }

    // ---------------- CSV 도우미 ----------------

    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    static string ResultDir()
    {
        string dir = Application.isEditor ? Path.Combine(Application.dataPath, "../GlareResults") : Path.Combine(Application.persistentDataPath, "GlareResults");
        Directory.CreateDirectory(dir);
        return dir;
    }

    string CellText()
    {
        return string.Format(Inv, "{0:0.#}m×{0:0.#}m", cellSizeM);
    }

    // 쉼표·따옴표가 있어도 한 칸에 들어가게
    static string Csv(string s)
    {
        s = s ?? "";
        return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    // 설명 줄: "# 항목,설명" (Excel에서는 두 칸으로, 분석 도구에서는 #줄을 건너뛰면 돼요)
    static void Note(StringBuilder sb, string key, string desc)
    {
        sb.Append("# ").Append(key.Replace(",", " ")).Append(',').AppendLine("\"" + desc.Replace("\"", "\"\"") + "\"");
    }

    static string RootNames(Transform[] roots)
    {
        var names = new List<string>();
        if (roots != null) foreach (var t in roots) if (t != null) names.Add(t.name);
        return names.Count > 0 ? string.Join("·", names.ToArray()) : "(자동)";
    }

    /// <summary>CSV 맨 위에 넣는 계산 조건</summary>
    void AppendConditions(StringBuilder sb, string when, string what)
    {
        sb.AppendLine("# 조건");
        Note(sb, "저장 시각", System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Note(sb, "시각·해", when);
        Note(sb, "경우", what);
        Note(sb, "직달일사", dniOverride > 0f ? dniOverride.ToString("0", Inv) + " W/m² (직접 입력)" : "맑은 날 모델(해 고도로 계산)");
        Note(sb, "반사율", string.Format(Inv, "안쪽 유리 {0} · 날(정반사) {1} [{4}] · 외측 유리 {2}, 투과율 {3} (외측 유리는 내부 루버일 때만)",
            glassReflectance, bladeSpecular, outerGlassReflectance, outerGlassTransmittance, FinishLabels[(int)bladeFinish]));
        Note(sb, "날", string.Format(Inv, "폭 {0} m · 간격 {1} m · 계산점 {2}장마다, 높이 {3}점", bladeWidthM, bladeSpacingM, bladeStride, samplesPerBladeHeight));
        Note(sb, "격자 · 노출 기준", string.Format(Inv, "{0} 칸({1:0.#} m²) · {2:0} W/m² 이상을 노출로 셈", CellText(), cellSizeM * cellSizeM, exposureThresholdWm2));
        string recv = RootNames(receiverRoots);
        var b = new List<string>();
        if (blockerRoots != null) foreach (var t in blockerRoots) if (t != null) b.Add(t.name);
        Note(sb, "받는 쪽", recv + (b.Count > 0 ? " (막기만 하는 것: " + string.Join("·", b.ToArray()) + ")" : ""));
        Note(sb, "좌표", "Unity 좌표(m). +X = 동, +Z = 북, +Y = 위");
        sb.AppendLine("#");
    }

    long CellKey(Vector3 p)
    {
        float s = Mathf.Max(0.1f, cellSizeM);
        const long o = 1L << 20;
        return ((Mathf.FloorToInt(p.x / s) + o) << 42) | ((Mathf.FloorToInt(p.y / s) + o) << 21) | (Mathf.FloorToInt(p.z / s) + o);
    }

    static double Get(Dictionary<int, double> d, int id)
    {
        double v;
        return d.TryGetValue(id, out v) ? v : 0.0;
    }

    static List<KeyValuePair<int, double>> SortedReceivers(GlareTotals t)
    {
        var list = new List<KeyValuePair<int, double>>(t != null ? t.perReceiver : new Dictionary<int, double>());
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        return list;
    }

    // 건물별 (칸 수, 최대 세기)
    static Dictionary<int, KeyValuePair<int, float>> ReceiverStats(GlareGrid g)
    {
        var d = new Dictionary<int, KeyValuePair<int, float>>();
        foreach (var c in g.cells)
        {
            KeyValuePair<int, float> v;
            d.TryGetValue(c.receiver, out v);
            d[c.receiver] = new KeyValuePair<int, float>(v.Key + 1, Mathf.Max(v.Value, c.maxIrr));
        }
        return d;
    }

    // 건물별 [노출 기준 이상인 칸 수]
    static Dictionary<int, int[]> ExposedCells(GlareGrid g)
    {
        var d = new Dictionary<int, int[]>();
        foreach (var c in g.cells)
        {
            if (c.exposureMin <= 0f) continue;
            int[] e;
            if (!d.TryGetValue(c.receiver, out e)) { e = new int[1]; d[c.receiver] = e; }
            e[0]++;
        }
        return d;
    }

    // ---------------- 패널 ----------------
    // 같은 패널을 Game 창(OnGUI), Scene 창, Inspector에서 그려요(Scene 창·Inspector는 Editor/GlareReflectionSimEditor.cs).

    [ContextMenu("반사광: 다시 계산")] void MenuRecompute() { Recompute(); }
    [ContextMenu("반사광: 4가지 비교")] void MenuCompare() { RunCompare(); }
    [ContextMenu("반사광: CSV 저장")] void MenuExport() { ExportCsv(); }
    [ContextMenu("반사광: 4가지 비교 CSV 저장")] void MenuExportCompare() { ExportCompareCsv(); }

    void OnGUI()
    {
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.G) { showGui = !showGui; e.Use(); }
        if (guiFont != null) GUI.skin.font = guiFont;
        if (ready && e.type == EventType.Repaint) DrawSunPathLabels();
        if (!showGui)
        {
            gamePanelRect = new Rect(Screen.width - 90, 10, 80, 24);
            if (GUI.Button(gamePanelRect, "반사광")) showGui = true;
            return;
        }
        if (!ready) { gamePanelRect = Rect.zero; return; }

        float w = 340f;
        var area = new Rect(Screen.width - w - 10, 10, w, Screen.height - 20);
        GUILayout.BeginArea(area);
        GUILayout.BeginVertical("box");
        DrawPanelContents(w, GlarePanelContext.Game);
        GUILayout.EndVertical();
        if (e.type == EventType.Repaint)
        {
            var r = GUILayoutUtility.GetLastRect();
            gamePanelRect = new Rect(area.x + r.x, area.y + r.y, r.width, r.height);
        }
        GUILayout.EndArea();
    }

    Rect gamePanelRect;   // 마지막으로 그린 Game 창 패널 범위(GUI 좌표, 왼쪽 위가 0)

    /// <summary>Game 창의 이 위치(GUI 좌표)가 반사광 패널 위인지. 카메라 조작 스크립트(GameViewCamera)가 이름으로 찾아 써요.</summary>
    public bool IsOverGamePanel(Vector2 guiPos)
    {
        return gamePanelRect.Contains(guiPos);
    }

    // Game 창에 태양 궤적 글자(시각·방위)를 써요. Main Camera 태그가 없으면 켜진 카메라 중 첫 번째를 써요.
    void DrawSunPathLabels()
    {
        var labels = SunPathLabels;
        if (labels == null || labels.Count == 0) return;
        var cam = Camera.main;
        if (cam == null)
            foreach (var c in Camera.allCameras)
                if (c.targetTexture == null) { cam = c; break; }
        if (cam == null) return;
        if (sunLabelStyle == null)
            sunLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        foreach (var l in labels)
        {
            var sp = cam.WorldToScreenPoint(l.pos);
            if (sp.z <= 0f) continue;
            var rect = new Rect(sp.x - 40f, Screen.height - sp.y - 10f, 80f, 20f);
            sunLabelStyle.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), l.text, sunLabelStyle);
            sunLabelStyle.normal.textColor = new Color(1f, 0.95f, 0.7f, 1f);
            GUI.Label(rect, l.text, sunLabelStyle);
        }
    }

    /// <summary>패널 안쪽(버튼·결과·범례). GUILayout 영역 안에서 불러요.</summary>
    public void DrawPanelContents(float width, GlarePanelContext ctx)
    {
        if (!ready) { GUILayout.Label("준비 중"); return; }

        GUILayout.BeginHorizontal();
        GUILayout.Label("<b>반사광 시뮬레이션</b>", Rich());
        if (ctx == GlarePanelContext.Game && GUILayout.Button("숨기기 (G)", GUILayout.Width(80))) showGui = false;
        if (ctx == GlarePanelContext.Scene && GUILayout.Button("숨기기", GUILayout.Width(60))) showSceneGui = false;
        GUILayout.EndHorizontal();

        int m = GUILayout.Toolbar((int)mode, new[] { "끔", "현재 시각", "하루 누적" });
        if (m != (int)mode) SetMode(m == 0 ? "off" : m == 1 ? "instant" : "daily");

        int lc = GUILayout.SelectionGrid((int)louverCase, CaseLabels, 2);
        if (lc != (int)louverCase) SetLouverCase(CaseKeys[lc]);

        // 날 마감 → 날 반사값
        GUILayout.Label("날 마감 (날 반사값 " + bladeSpecular.ToString("0.00", Inv) + ")");
        int bf = GUILayout.SelectionGrid((int)bladeFinish, FinishLabels, 3);
        if (bf != (int)bladeFinish) SetBladeFinish((GlareBladeFinish)bf, bladeSpecular);
        if (bladeFinish == GlareBladeFinish.Custom)
        {
            // 끄는 동안은 숫자만 바꾸고, 손을 떼면 한 번만 다시 계산해요
            float shown = sliderDragging ? sliderValue : bladeSpecular;
            GUILayout.BeginHorizontal();
            float v = GUILayout.HorizontalSlider(shown, 0f, 1f);
            GUILayout.Label(v.ToString("0.00", Inv), GUILayout.Width(36));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(v, shown)) { sliderValue = Mathf.Round(v * 100f) / 100f; sliderDragging = true; }
            if (sliderDragging && GUIUtility.hotControl == 0) { sliderDragging = false; SetBladeSpecular(sliderValue); }
        }

        if (mode == GlareMode.Daily)
        {
            int dm = GUILayout.Toolbar((int)dailyMetric, new[] { "에너지", "노출 시간", "최대 세기" });
            if (dm != (int)dailyMetric) { dailyMetric = (GlareMetric)dm; Redraw(); }
        }

        GUILayout.BeginHorizontal();
        bool r = GUILayout.Toggle(showRays, "광선");
        bool s = GUILayout.Toggle(showSourceMap, "입면 발생 위치");
        if (r != showRays || s != showSourceMap) { showRays = r; showSourceMap = s; Redraw(); }
        bool sp = GUILayout.Toggle(showSunPath, "태양 궤적");
        if (sp != showSunPath) { showSunPath = sp; ResultVersion++; }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("다시 계산")) Recompute();
        if (GUILayout.Button("4가지 비교")) RunCompare();
        if (GUILayout.Button("CSV 저장")) ExportCsv();
        GUILayout.EndHorizontal();
        if (compareGrids != null && job == null && GUILayout.Button("4가지 비교 CSV 저장 (" + compareLabel + ")")) ExportCompareCsv();

        GUILayout.Label("상태: " + status);
        if (mode != GlareMode.Off && shownTotals != null && grid != null)
        {
            string u = shownDaily ? "kWh" : "kW";
            double k = 1.0 / 1000.0;
            GUILayout.Label(shownLabel + (shownDni > 0f ? "\nDNI " + shownDni.ToString("0") + " W/m² (" + (dniOverride > 0f ? "직접 입력" : "맑은 날 모델") + ")" : ""));
            string parts = shownCase == GlareLouverCase.ResponsiveInner
                ? string.Format("외측 유리 {0:0}, 안쪽 유리 {1:0}, 날 {2:0}", shownTotals.outerGlass * k, shownTotals.glass * k, shownTotals.blade * k)
                : string.Format("유리 {0:0}, 날 {1:0}", shownTotals.glass * k, shownTotals.blade * k);
            string blockedPart = receivers != null && receivers.blockerCount > 0
                ? string.Format(" · 대지 등에 막힘 {0:0} {1}", shownTotals.blocked * k, u) : "";
            GUILayout.Label(CaseName(shownCase));
            GUILayout.Label(string.Format("입면에 든 햇빛 {0:0} {1}\n밖으로 나간 반사 {2:0} {1} ({3})\n주변에 닿음 {4:0} {1}{6} · 하늘로 {5:0} {1}",
                shownTotals.incident * k, u, shownTotals.Reflected * k, parts, shownTotals.received * k, shownTotals.toSky * k, blockedPart));
            GUILayout.Label(shownDaily
                ? string.Format("최대 {0:0.00} {1} · 노출 면적 {2:0} m² · 최장 {3:0.0}시간\n(노출 = 받는 면 {4:0} W/m² 이상)", shownMax, shownUnit, shownOverArea, shownMaxExposureH, exposureThresholdWm2)
                : string.Format("최대 {0:0} W/m² · {1:0} W/m² 이상 면적 {2:0} m²", shownMax, exposureThresholdWm2, shownOverArea));

            var top = TopReceivers(5);
            if (top.Length > 0)
            {
                var sb = new StringBuilder("많이 받는 곳\n");
                for (int i = 0; i < top.Length; i++) sb.AppendFormat("  {0}. {1}  {2:0.0} {3}\n", i + 1, top[i].name, top[i].value, u);
                GUILayout.Label(sb.ToString().TrimEnd());
            }

            // 범례
            GUILayout.Label("색: 0 ~ " + shownScale.ToString(shownScale < 10f ? "0.00" : "0") + " " + shownUnit + (autoScale ? " (상위 2%)" : ""));
            Rect bar = GUILayoutUtility.GetRect(Mathf.Max(60f, width - 30f), 12f);
            if (legendTex != null) GUI.DrawTexture(bar, legendTex, ScaleMode.StretchToFill);
        }
        foreach (var line in compareLines) GUILayout.Label(line);
    }

    // Game 창과 에디터 창은 GUI 스킨이 달라서 스킨마다 스타일을 새로 만들어요
    GUIStyle rich;
    GUISkin richSkin;
    GUIStyle Rich()
    {
        if (rich == null || richSkin != GUI.skin)
        {
            rich = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 };
            richSkin = GUI.skin;
        }
        return rich;
    }
}

// ---------------- JSON 형식 ----------------

[System.Serializable]
public class GlareReceiverJson
{
    public string name;
    public float value;
}

[System.Serializable]
public class GlareCompareJson
{
    public string louverCase;
    public float receivedKWh;
    public float reflectedKWh;
    public float maxKWhPerM2;
    public float exposedAreaM2;
}

[System.Serializable]
public class GlareResultJson
{
    public string mode;
    public string louverCase;
    public string label;
    public string unit;          // kW(현재 시각) 또는 kWh(하루)
    public float dni;
    public float incident;
    public float reflectedGlass;
    public float reflectedBlade;
    public float reflectedOuterGlass;   // 반응형 내부 루버일 때 외측 유리 반사
    public float received;
    public float blocked;               // 대지 등 막기만 하는 것에 닿음(received·receivers에는 안 넣어요)
    public float toSky;
    public string mapUnit;
    public float mapMax;
    public float areaOverThresholdM2;
    public GlareReceiverJson[] receivers;
    public GlareCompareJson[] compare;
}
