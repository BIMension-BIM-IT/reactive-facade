using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rhino나 Revit에서 FBX로 들어온 루버(날)에 우리 로직(각도)을 얹어요.
///
/// 날 한 개 = louverRoot의 "직계 자식" 하나예요.
///  - 날이 여러 조각(날 + 프레임)이어도 직계 자식 하나로 묶여 있으면 한 덩어리로 돌아가요.
///  - 루버가 한 덩어리로 합쳐져 있으면 따로 돌릴 수 없어요(날마다 따로 나와야 해요).
///
/// 입면 판별:
///  1) 이름이 "층_입면_번호"(예: 5_S_001, E/S/W/N 또는 동/남/서/북)이면 이름을 써요.
///  2) 이름이 달라도(Revit FBX 등) classifyByPosition이 켜져 있으면 날의 위치로 판별해요
///     (모든 날을 감싸는 범위에서 가장 가까운 면 = 그 입면, 높이 = 층).
///
/// FBX 날은 피벗이 원점(0,0,0)이라서, 시작할 때 각 날의 "자기 중심"에 피벗을 만들어서 그 둘레로 돌려요.
/// 좌표: Unity에서 북쪽 = +Z, 동쪽 = +X, 위 = +Y
/// </summary>
/// <summary>날 방향: 수직(세로로 긴 날, 위(Y)축으로 회전) / 수평(가로로 긴 날, 입면을 따라가는 가로축으로 회전)</summary>
public enum LouverOrientation { Vertical, Horizontal }

public class LouverRig : MonoBehaviour
{
    [Tooltip("이 리그가 돌리는 날의 방향. 수평이면 날의 각도는 '바깥쪽 끝이 아래로 내려가는 각도'(0 = 열림)예요")]
    public LouverOrientation orientation = LouverOrientation.Vertical;
    [Tooltip("루버 날들이 들어 있는 부모 오브젝트(비우면 이 오브젝트). 이 오브젝트의 직계 자식이 날 하나씩이에요")]
    public Transform louverRoot;
    [Tooltip("이름으로 입면을 못 읽을 때 위치로 판별해요(직사각형 건물 기준)")]
    public bool classifyByPosition = true;
    [Tooltip("위치로 층을 셀 때 쓰는 층고(m)")]
    public float floorHeightM = 4.2f;
    [Tooltip("이 글자가 이름에 들어 있는 오브젝트는 돌리지 않아요(예: 외피 모델링 담당 V2의 모서리 '고정' 기둥)")]
    public string[] skipNameContains = { "고정", "스크린", "각파이프", "기둥" };   // 코너 기둥, 옥상 "RF 스크린", 수평 루버의 각파이프·기둥은 돌리지 않아요
    // 코너 기둥("고정"), 옥상 가림막("스크린"), 수평 루버의 각파이프·기둥은 날이 아니라서 항상 제외
    static readonly string[] AlwaysSkip = { "고정", "스크린", "각파이프", "기둥" };
    [Tooltip("시작할 때 찾은 날 개수(확인용)")]
    public int foundBlades;

    class Blade
    {
        public Transform pivot;
        public FacadeSide side;
        public int floor;
    }

    readonly List<Blade> blades = new List<Blade>();

    /// <summary>모든 날을 감싸는 범위(월드). 성능 스크립트가 보이지 않는 면을 판단할 때 써요.</summary>
    public Bounds AllBounds { get; private set; }

    // 면별로 "사용자가 숨김"(예: 북면 루버 없음)과 "카메라에서 안 보여서 숨김"을 따로 기억해요
    readonly bool[] userHidden = new bool[4];
    readonly bool[] culled = new bool[4];

    void Awake()
    {
        Build();
    }

    void Build()
    {
        var root = louverRoot != null ? louverRoot : transform;

        // 1) 직계 자식마다 렌더러를 모아서 날 하나의 범위(Bounds)를 구해요
        var items = new List<KeyValuePair<Transform, Bounds>>();
        bool has = false;
        Bounds all = new Bounds();
        foreach (Transform child in root)
        {
            // 모서리 고정 기둥(이름에 "고정")처럼 돌리지 않을 것은 건너뛰어요
            bool skip = false;
            foreach (var key in skipNameContains)
                if (!string.IsNullOrEmpty(key) && child.name.Contains(key)) { skip = true; break; }
            // 씬에 이미 저장된 목록이 예전 값이어도 돌리면 안 되는 것들은 항상 건너뛰어요(스크립트만 덮어써도 적용돼요)
            if (!skip)
                foreach (var key in AlwaysSkip)
                    if (child.name.Contains(key)) { skip = true; break; }
            if (skip) continue;

            var rs = child.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            items.Add(new KeyValuePair<Transform, Bounds>(child, b));
            if (!has) { all = b; has = true; } else { all.Encapsulate(b); }
        }

        // 2) 입면과 층을 정하고, 날 중심에 피벗을 만들어요
        int byName = 0, byPos = 0, skipped = 0;
        foreach (var kv in items)
        {
            Transform blade = kv.Key;
            int floor;
            FacadeSide side;
            if (TryParse(blade.name, out floor, out side))
            {
                byName++;
            }
            else if (classifyByPosition)
            {
                Classify(kv.Value.center, all, out floor, out side);
                // 이름에 "09F-123"처럼 층이 있으면 그 층을 써요(외피 모델링 담당 V2). 없으면 높이로 센 층을 써요.
                var mf = System.Text.RegularExpressions.Regex.Match(blade.name, @"(\d+)F-");
                if (mf.Success) floor = int.Parse(mf.Groups[1].Value);
                byPos++;
            }
            else
            {
                skipped++;
                continue;
            }

            var pivot = new GameObject("Pivot_" + blade.name).transform;
            pivot.SetParent(root, true);
            pivot.position = kv.Value.center;     // 날의 실제 중심(월드)
            pivot.rotation = Quaternion.identity;
            blade.SetParent(pivot, true);         // 날의 월드 위치/방향은 그대로
            blades.Add(new Blade { pivot = pivot, side = side, floor = floor });
        }

        AllBounds = all;
        foundBlades = blades.Count;
        Debug.Log("LouverRig: 날 " + blades.Count + "개 (이름으로 " + byName + "개, 위치로 " + byPos + "개, 건너뜀 " + skipped + "개)");
        if (blades.Count == 0)
            Debug.LogWarning("LouverRig: 날을 못 찾았어요. louverRoot의 직계 자식이 날 하나씩인지 확인하세요");
    }

    /// <summary>모든 날을 감싸는 범위에서 가장 가까운 면을 그 날의 입면으로 정해요.</summary>
    void Classify(Vector3 c, Bounds all, out int floor, out FacadeSide side)
    {
        float dE = all.max.x - c.x;
        float dW = c.x - all.min.x;
        float dN = all.max.z - c.z;
        float dS = c.z - all.min.z;
        float m = Mathf.Min(Mathf.Min(dE, dW), Mathf.Min(dN, dS));
        if (m == dE) side = FacadeSide.East;
        else if (m == dW) side = FacadeSide.West;
        else if (m == dN) side = FacadeSide.North;
        else side = FacadeSide.South;
        floor = 1 + Mathf.FloorToInt((c.y - all.min.y) / Mathf.Max(0.01f, floorHeightM));
    }

    static bool TryParse(string n, out int floor, out FacadeSide side)
    {
        floor = 0;
        side = FacadeSide.South;
        var t = n.Split('_');
        if (t.Length < 2) return false;
        if (!int.TryParse(t[0].Trim(), out floor)) return false;
        switch (t[1].Trim().ToUpperInvariant())
        {
            case "E": case "동": case "동면": side = FacadeSide.East; return true;
            case "S": case "남": case "남면": side = FacadeSide.South; return true;
            case "W": case "서": case "서면": side = FacadeSide.West; return true;
            case "N": case "북": case "북면": side = FacadeSide.North; return true;
        }
        return false;
    }

    /// <summary>입면 전체의 날을 같은 각도로 돌려요. angleDeg는 CSV의 각도(0 = 열림, +는 바깥 끝이 오른쪽).</summary>
    public void Apply(FacadeSide side, float angleDeg)
    {
        var q = Rot(side, angleDeg);
        foreach (var b in blades)
            if (b.side == side) b.pivot.rotation = q;
    }

    /// <summary>한 층의 한 입면만 돌려요(층마다 다른 각도를 줄 때).</summary>
    public void Apply(int floor, FacadeSide side, float angleDeg)
    {
        var q = Rot(side, angleDeg);
        foreach (var b in blades)
            if (b.side == side && b.floor == floor) b.pivot.rotation = q;
    }

    /// <summary>층·입면마다 다른 각도를 한 번에 줘요. angleOf(층, 입면)이 각도(도)를 돌려줘요.</summary>
    public void ApplyTable(System.Func<int, FacadeSide, float> angleOf)
    {
        foreach (var b in blades)
            b.pivot.rotation = Rot(b.side, angleOf(b.floor, b.side));
    }

    // 수직 날: 위(Y)축 둘레. Unity에서 위축 양의 회전은 시계방향이라서 -각도만큼 돌려요.
    // 수평 날: 입면을 따라가는 가로축(f) 둘레. 같은 이유로 -각도, 양의 각도 = 바깥쪽 끝이 아래로.
    Quaternion Rot(FacadeSide side, float angleDeg)
    {
        if (orientation == LouverOrientation.Vertical)
            return Quaternion.AngleAxis(-angleDeg, Vector3.up);
        return Quaternion.AngleAxis(-angleDeg, TangentAxis(side));
    }

    // 입면의 바깥 방향 n(Unity: 동 +X, 북 +Z)에서 가로축 f = (-n.z, 0, n.x)
    static Vector3 TangentAxis(FacadeSide side)
    {
        switch (side)
        {
            case FacadeSide.East: return new Vector3(0f, 0f, 1f);
            case FacadeSide.South: return new Vector3(1f, 0f, 0f);
            case FacadeSide.West: return new Vector3(0f, 0f, -1f);
            default: return new Vector3(-1f, 0f, 0f);
        }
    }

    /// <summary>이 리그의 날 묶음(louverRoot)을 보이게/숨겨요. 수직·수평 모델을 한 씬에 두고 번갈아 쓸 때 써요.</summary>
    public void SetVisible(bool on)
    {
        var root = louverRoot != null ? louverRoot : transform;
        root.gameObject.SetActive(on);
    }

    /// <summary>한 입면의 날을 통째로 보이게/숨겨요(웹에서 북면 루버를 끌 때).</summary>
    public void SetSideActive(FacadeSide side, bool active)
    {
        userHidden[(int)side] = !active;
        RefreshSide(side);
    }

    /// <summary>카메라에서 안 보이는 면은 날을 꺼서 그리는 양을 줄여요(LouverPerformance가 불러요).</summary>
    public void SetSideCulled(FacadeSide side, bool isCulled)
    {
        if (culled[(int)side] == isCulled) return;
        culled[(int)side] = isCulled;
        RefreshSide(side);
    }

    void RefreshSide(FacadeSide side)
    {
        bool on = !userHidden[(int)side] && !culled[(int)side];
        foreach (var b in blades)
            if (b.side == side && b.pivot != null && b.pivot.gameObject.activeSelf != on) b.pivot.gameObject.SetActive(on);
    }

    public void ApplyAll(float angleDeg)
    {
        foreach (var b in blades) b.pivot.rotation = Rot(b.side, angleDeg);
    }
}

