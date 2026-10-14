using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 웹이 보낸 건물(입면 방위·폭·높이)로 단순한 매싱 건물과 루버를 만들어요. "박스 건물 장면"에서만 써요(1784 장면에는 붙이지 않아요).
///
/// 만드는 것: 건물 몸체(외곽선을 따라 세운 벽 + 지붕), 입면마다 수직 날과 수평 날 두 세트(LouverRig 두 개), 바닥 평면.
/// 연결: SunLouverController의 rig(수직), rigHorizontal(수평)에 자동으로 넣어요. 날은 직접 계산 모드에서 쓰는 입면 전체 단위라서
///       층마다 쪼개지 않고 입면 전체 높이의 날 하나로 만들어요(수직은 날 1개 = 건물 높이, 수평은 날 1개 = 입면 폭).
/// 모양: 입면 방위에서 건물 회전각을 구하고 가장 가까운 직각 방향(동·서·남·북)으로 맞춰 반듯하게 세워요.
///       직사각형과 ㄱ·ㄷ자는 입면 순서대로 따라가서 닫혀요. 안 닫히면 직사각형으로 대신 만들어요.
/// 건물 회전은 만들지 않고(모델은 북쪽 = +Z로 반듯하게), SunLouverController의 buildingYawDeg가 해 방향을 돌려요.
///
/// 쓰는 법: 박스 장면의 빈 오브젝트에 이 스크립트를 붙여요. 나머지 칸은 비워 두면 자동으로 찾아요.
/// 머티리얼: 아래 칸에 연결하면 그걸 써요. 비워 두면 URP Lit 등을 찾아서 만드는데, 웹 빌드에서는 셰이더가 빠질 수 있어서
///           웹 빌드에서는 머티리얼 3개를 꼭 연결해 주세요.
/// </summary>
public class BoxBuildingGenerator : MonoBehaviour
{
    public LouverBridge bridge;
    public SunLouverController controller;

    [Header("크기")]
    [Tooltip("웹이 높이를 안 보냈을 때 건물 높이(m)")] public float defaultHeightM = 20f;
    [Tooltip("날 두께(m)")] public float bladeThicknessM = 0.025f;
    [Tooltip("벽에서 날까지 떨어진 거리(m)")] public float standoffM = 0.15f;
    [Tooltip("날이 너무 많아지지 않게: 입면 하나의 날 개수 최대")] public int maxBladesPerFacade = 800;

    [Header("머티리얼 (웹 빌드에서는 직접 연결)")]
    public Material wallMaterial;
    public Material bladeMaterial;
    public Material groundMaterial;

    [Header("카메라")]
    [Tooltip("켜면 건물을 만들 때마다 메인 카메라를 건물이 보이게 옮겨요")] public bool frameCamera = true;

    [Header("시험")]
    [Tooltip("웹 없이 시험할 때: 빌드 후 Rebuild Test(우클릭 메뉴)를 누르면 이 크기의 ㄱ자를 만들어요")] public bool testUseLShape = true;

    GameObject root;
    class Edge { public Vector2 a, b; public FacadeSide side; public float len; public bool louver = true; }

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

    void OnEnable() { if (bridge != null) bridge.onSiteInput.AddListener(OnSiteInput); }
    void OnDisable() { if (bridge != null) bridge.onSiteInput.RemoveListener(OnSiteInput); }

    void OnSiteInput(SiteInput s)
    {
        if (s == null) return;
        Build(s.facades);
    }

    [ContextMenu("시험: 건물 만들기 (ㄱ자)")]
    void RebuildTest()
    {
        // 반시계 방향 ㄱ자 예: 30x20 안에서 오른쪽 위 15x10을 덜어낸 모양 (방위 = 입면이 바라보는 방향)
        var l = new List<FacadeInfo>();
        if (testUseLShape)
        {
            l.Add(new FacadeInfo { id = "S", azimuth_deg = 180f, width_m = 30f, height_m = 24f, louver = true });
            l.Add(new FacadeInfo { id = "E1", azimuth_deg = 90f, width_m = 10f, height_m = 24f, louver = true });
            l.Add(new FacadeInfo { id = "N1", azimuth_deg = 0f, width_m = 15f, height_m = 24f, louver = true });
            l.Add(new FacadeInfo { id = "E2", azimuth_deg = 90f, width_m = 10f, height_m = 24f, louver = true });
            l.Add(new FacadeInfo { id = "N2", azimuth_deg = 0f, width_m = 15f, height_m = 24f, louver = false });
            l.Add(new FacadeInfo { id = "W", azimuth_deg = 270f, width_m = 20f, height_m = 24f, louver = true });
        }
        else
        {
            l.Add(new FacadeInfo { id = "S", azimuth_deg = 180f, width_m = 30f, height_m = 24f, louver = true });
            l.Add(new FacadeInfo { id = "E", azimuth_deg = 90f, width_m = 20f, height_m = 24f, louver = true });
            l.Add(new FacadeInfo { id = "N", azimuth_deg = 0f, width_m = 30f, height_m = 24f, louver = true });
            l.Add(new FacadeInfo { id = "W", azimuth_deg = 270f, width_m = 20f, height_m = 24f, louver = true });
        }
        Build(l.ToArray());
    }

    // ---------------- 만들기 ----------------

    public void Build(FacadeInfo[] facades)
    {
        // 1) 건물 회전각과 높이
        double yaw = 0, dev = 0;
        float height = 0f;
        if (facades != null && facades.Length > 0)
        {
            var az = new double[facades.Length];
            var wt = new double[facades.Length];
            for (int i = 0; i < az.Length; i++)
            {
                az[i] = facades[i] != null ? facades[i].azimuth_deg : 0;
                wt[i] = facades[i] != null ? facades[i].width_m : 0;
                if (facades[i] != null) height = Mathf.Max(height, facades[i].height_m);
            }
            yaw = LouverRule.GridYawDeg(az, wt, out dev);
        }
        if (height < 3f) height = defaultHeightM;

        // 2) 외곽선: 입면 순서대로 따라가요 -> 안 닫히면 방위 순서 -> 그래도 안 되면 직사각형
        List<Vector2> poly = null;
        if (facades != null && facades.Length >= 4)
        {
            poly = TryWalk(facades, yaw, false);
            if (poly == null) poly = TryWalk(facades, yaw, true);
        }
        if (poly == null)
        {
            poly = FallbackRect(facades, yaw);
            Debug.LogWarning("[BoxBuilding] 입면이 닫히지 않아 직사각형으로 만들었어요(웹 담당이 건물 외곽 좌표를 보내 주면 정확해져요)");
        }
        // 반시계 방향으로
        if (SignedArea(poly) < 0f) poly.Reverse();
        // 중심을 원점으로
        Vector2 c = Vector2.zero;
        foreach (var p in poly) c += p;
        c /= poly.Count;
        for (int i = 0; i < poly.Count; i++) poly[i] -= c;

        // 3) 변마다 입면 방향과 루버 유무
        var edges = new List<Edge>();
        for (int i = 0; i < poly.Count; i++)
        {
            var e = new Edge { a = poly[i], b = poly[(i + 1) % poly.Count] };
            Vector2 d = e.b - e.a;
            e.len = d.magnitude;
            if (e.len < 0.5f) continue;
            // 반시계 방향이라 바깥쪽은 진행 방향의 오른쪽: n = (d.y, -d.x) (x = 동, y = 북)
            Vector2 n = new Vector2(d.y, -d.x).normalized;
            if (Mathf.Abs(n.x) > Mathf.Abs(n.y)) e.side = n.x > 0 ? FacadeSide.East : FacadeSide.West;
            else e.side = n.y > 0 ? FacadeSide.North : FacadeSide.South;
            edges.Add(e);
        }
        bool northLouver = FacadeNorthLouver(facades, yaw);

        // 4) 오브젝트 만들기
        if (root != null) Destroy(root);
        root = new GameObject("GeneratedBuilding");
        root.transform.SetParent(transform, false);

        BuildBody(root.transform, poly, height);
        BuildGround(root.transform, poly);
        var vRoot = BuildBlades(root.transform, edges, height, true);
        var hRoot = BuildBlades(root.transform, edges, height, false);
        var vRig = MakeRig(vRoot, LouverOrientation.Vertical);
        var hRig = MakeRig(hRoot, LouverOrientation.Horizontal);

        // 5) 컨트롤러에 연결
        if (controller != null)
        {
            controller.rig = vRig;
            controller.rigHorizontal = hRig;
            controller.SetOrientation(controller.orientation);
            controller.SetNorthLouver(northLouver);
        }
        if (frameCamera) FrameCamera(poly, height);
        Debug.Log("[BoxBuilding] 건물 " + edges.Count + "면, 높이 " + height.ToString("0") + " m, 회전 " + yaw.ToString("0.0") + "°, 어긋남 " + dev.ToString("0") + "°");
    }

    // 입면 순서대로 걸어요. 방위에서 건물 회전을 빼서 가장 가까운 직각 방향으로 맞춘 뒤, 닫히면 외곽선을, 아니면 null
    List<Vector2> TryWalk(FacadeInfo[] facades, double yaw, bool sortByAzimuth)
    {
        var items = new List<KeyValuePair<float, FacadeInfo>>();
        foreach (var f in facades)
        {
            if (f == null || f.width_m < 0.5f) continue;
            double rel = LouverRule.Wrap180(f.azimuth_deg - yaw);
            float snapped = Mathf.Round((float)rel / 90f) * 90f;
            items.Add(new KeyValuePair<float, FacadeInfo>(((snapped % 360f) + 360f) % 360f, f));
        }
        if (items.Count < 4) return null;
        if (sortByAzimuth)
            items.Sort((p, q) => q.Key.CompareTo(p.Key));       // 반시계 방향이면 방위(시계)는 줄어드는 순서

        var pts = new List<Vector2> { Vector2.zero };
        float perim = 0f;
        foreach (var it in items)
        {
            float a = it.Key * Mathf.Deg2Rad;                    // 바깥 방향의 방위
            Vector2 d = new Vector2(-Mathf.Cos(a), Mathf.Sin(a));   // 진행 방향(동, 북)
            pts.Add(pts[pts.Count - 1] + d * it.Value.width_m);
            perim += it.Value.width_m;
        }
        Vector2 gap = pts[pts.Count - 1];                         // 시작점으로 못 돌아온 만큼
        if (gap.magnitude > 0.12f * perim) return null;
        pts.RemoveAt(pts.Count - 1);
        for (int i = 0; i < pts.Count; i++) pts[i] -= gap * ((float)i / items.Count);   // 남은 오차를 고르게 나눠요
        return pts;
    }

    List<Vector2> FallbackRect(FacadeInfo[] facades, double yaw)
    {
        float ns = 0f, ew = 0f;
        if (facades != null)
            foreach (var f in facades)
            {
                if (f == null) continue;
                double rel = LouverRule.Wrap180(f.azimuth_deg - yaw);
                bool isEW = Mathf.Abs(Mathf.Abs((float)rel) - 90f) < 45f;
                if (isEW) ew = Mathf.Max(ew, f.width_m); else ns = Mathf.Max(ns, f.width_m);
            }
        if (ns < 3f) ns = 20f;
        if (ew < 3f) ew = 20f;
        return new List<Vector2> { new Vector2(0, 0), new Vector2(ns, 0), new Vector2(ns, ew), new Vector2(0, ew) };
    }

    // 건물 기준 북쪽 입면 중 하나라도 루버를 달면 켜요. 북쪽 입면이 없으면 켠 채로 둬요.
    static bool FacadeNorthLouver(FacadeInfo[] facades, double yaw)
    {
        if (facades == null) return true;
        bool any = false, on = false;
        foreach (var f in facades)
        {
            if (f == null) continue;
            if (System.Math.Abs(LouverRule.Wrap180(f.azimuth_deg - yaw)) <= 45.0) { any = true; if (f.louver) on = true; }
        }
        return any ? on : true;
    }

    static float SignedArea(List<Vector2> p)
    {
        float s = 0f;
        for (int i = 0; i < p.Count; i++) { var a = p[i]; var b = p[(i + 1) % p.Count]; s += a.x * b.y - b.x * a.y; }
        return s * 0.5f;
    }

    // ---------------- 몸체 · 바닥 ----------------

    void BuildBody(Transform parent, List<Vector2> poly, float height)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % n];
            int k = verts.Count;
            verts.Add(new Vector3(a.x, 0, a.y));
            verts.Add(new Vector3(a.x, height, a.y));
            verts.Add(new Vector3(b.x, height, b.y));
            verts.Add(new Vector3(b.x, 0, b.y));
            tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
            tris.Add(k); tris.Add(k + 2); tris.Add(k + 3);
        }
        // 지붕: 귀 자르기(오목한 ㄱ자도 가능). 반시계 삼각형을 위에서 볼 때 시계 방향이 앞면이라 순서를 뒤집어요
        var idx = new List<int>();
        for (int i = 0; i < n; i++) idx.Add(i);
        int roofStart = verts.Count;
        foreach (var p in poly) verts.Add(new Vector3(p.x, height, p.y));
        int guard = 0;
        while (idx.Count > 3 && guard++ < 1000)
        {
            bool cut = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int i0 = idx[(i + idx.Count - 1) % idx.Count], i1 = idx[i], i2 = idx[(i + 1) % idx.Count];
                Vector2 A = poly[i0], B = poly[i1], C = poly[i2];
                if (Cross(B - A, C - B) <= 1e-5f) continue;                  // 볼록한 꼭짓점만
                bool inside = false;
                foreach (int j in idx)
                {
                    if (j == i0 || j == i1 || j == i2) continue;
                    if (PointInTri(poly[j], A, B, C)) { inside = true; break; }
                }
                if (inside) continue;
                tris.Add(roofStart + i0); tris.Add(roofStart + i2); tris.Add(roofStart + i1);
                idx.RemoveAt(i);
                cut = true;
                break;
            }
            if (!cut) break;
        }
        if (idx.Count == 3) { tris.Add(roofStart + idx[0]); tris.Add(roofStart + idx[2]); tris.Add(roofStart + idx[1]); }

        var mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var go = new GameObject("Body");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = Mat(wallMaterial, new Color(0.62f, 0.72f, 0.80f), "wall");
    }

    static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }
    static bool PointInTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
        return d1 > 0 && d2 > 0 && d3 > 0;
    }

    void BuildGround(Transform parent, List<Vector2> poly)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = "Ground";
        go.transform.SetParent(parent, false);
        Destroy(go.GetComponent<Collider>());
        go.transform.localPosition = new Vector3(0, -0.02f, 0);
        go.transform.localScale = new Vector3(40f, 1f, 40f);   // 400 m
        go.GetComponent<MeshRenderer>().sharedMaterial = Mat(groundMaterial, new Color(0.78f, 0.78f, 0.74f), "ground");
    }

    // ---------------- 날 ----------------

    Transform BuildBlades(Transform parent, List<Edge> edges, float height, bool vertical)
    {
        // 비활성 상태로 만들어서 LouverRig(Awake)가 날이 다 만들어진 뒤에 시작하게 해요
        var rootGo = new GameObject(vertical ? "Louvers_Vertical" : "Louvers_Horizontal");
        rootGo.SetActive(false);
        rootGo.transform.SetParent(parent, false);
        double sMm = controller != null ? controller.bladeSpacingMm : 350.0;
        double wMm = controller != null ? controller.bladeDepthMm : 300.0;
        float s = (float)(sMm / 1000.0), w = (float)(wMm / 1000.0);
        var mat = Mat(bladeMaterial, new Color(0.92f, 0.92f, 0.90f), "blade");
        string[] letter = { "E", "S", "W", "N" };
        var counter = new int[4];
        foreach (var e in edges)
        {
            Vector2 d = (e.b - e.a) / e.len;
            Vector2 n = new Vector2(d.y, -d.x);
            Vector3 n3 = new Vector3(n.x, 0, n.y);
            bool ns = e.side == FacadeSide.North || e.side == FacadeSide.South;      // 입면이 x방향으로 뻗음
            int count = Mathf.Min(maxBladesPerFacade, vertical ? Mathf.FloorToInt(e.len / s) : Mathf.FloorToInt(height / s));
            for (int i = 0; i < count; i++)
            {
                Vector3 centerOnWall, size;
                if (vertical)
                {
                    Vector2 p = e.a + d * ((i + 0.5f) * s);
                    centerOnWall = new Vector3(p.x, height * 0.5f, p.y);
                    size = ns ? new Vector3(bladeThicknessM, height, w) : new Vector3(w, height, bladeThicknessM);
                }
                else
                {
                    Vector2 p = (e.a + e.b) * 0.5f;
                    centerOnWall = new Vector3(p.x, (i + 0.5f) * s, p.y);
                    size = ns ? new Vector3(e.len, bladeThicknessM, w) : new Vector3(w, bladeThicknessM, e.len);
                }
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                counter[(int)e.side]++;
                go.name = string.Format("1_{0}_{1:000}", letter[(int)e.side], counter[(int)e.side]);
                go.transform.SetParent(rootGo.transform, false);
                go.transform.position = centerOnWall + n3 * (standoffM + w * 0.5f);
                go.transform.localScale = size;
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        return rootGo.transform;
    }

    LouverRig MakeRig(Transform rootT, LouverOrientation o)
    {
        var rig = rootT.gameObject.AddComponent<LouverRig>();   // 비활성이라 아직 Awake 전
        rig.orientation = o;
        rig.louverRoot = rootT;
        rig.classifyByPosition = false;                          // 이름("1_S_001")으로만 입면을 정해요
        rootT.gameObject.SetActive(true);                        // 이제 Awake -> 날을 모아요
        return rig;
    }

    // ---------------- 카메라 · 머티리얼 ----------------

    void FrameCamera(List<Vector2> poly, float height)
    {
        var cam = Camera.main;
        if (cam == null) return;
        float r = 10f;
        foreach (var p in poly) r = Mathf.Max(r, p.magnitude);
        Vector3 target = new Vector3(0, height * 0.4f, 0);
        Vector3 dir = new Vector3(-0.6f, 0.45f, -1f).normalized;
        cam.transform.position = target + dir * (r * 2.4f + height * 0.8f);
        cam.transform.LookAt(target);
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, 2000f);
    }

    Material Mat(Material given, Color color, string tag)
    {
        if (given != null) return given;
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh == null) { Debug.LogError("[BoxBuilding] " + tag + " 머티리얼을 만들 셰이더를 못 찾았어요. 인스펙터에 머티리얼을 연결해 주세요"); return null; }
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        return m;
    }
}
