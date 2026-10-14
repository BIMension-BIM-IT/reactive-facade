using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 태양 궤적(해 길)을 화면에 그려요. GlareReflectionSim이 만들고, 패널의 "태양 궤적"으로 켜고 꺼요.
/// 컨트롤러가 읽은 sun_louver.csv의 해 방향을 그대로 써서 화면의 해(Directional Light)와 어긋나지 않아요.
///  - 지금 대표일: 진한 노란 선, 1시간마다 점, 3시간마다 시각 글자
///  - 다른 대표일(춘분·하지·추분·동지): 흐린 흰 선(끌 수 있어요)
///  - 지평선 원과 동서남북 글자(Unity 기준 +Z = 북, +X = 동, CSV와 같은 기준)
///  - 지금 해: 큰 노란 점과 건물로 오는 해 방향선
/// 선과 점은 LineRenderer로 그려요(카메라를 향하는 굵은 선이라 멀리서도 보여요).
/// </summary>
public class GlareSunPath
{
    /// <summary>화면에 쓸 글자(시각, 방위). 위치는 월드 좌표</summary>
    public struct Label
    {
        public Vector3 pos;
        public string text;
    }

    public readonly List<Label> labels = new List<Label>();
    public bool HasData { get { return days.Count > 0; } }

    static readonly Color CurrentDay = new Color(1f, 0.82f, 0.15f, 0.95f);
    static readonly Color OtherDay = new Color(1f, 1f, 1f, 0.35f);
    static readonly Color Horizon = new Color(1f, 1f, 1f, 0.3f);
    static readonly Color HourDot = new Color(1f, 0.95f, 0.65f, 1f);
    static readonly Color SunDot = new Color(1f, 0.9f, 0.1f, 1f);
    static readonly Color SunRay = new Color(1f, 0.9f, 0.3f, 0.45f);

    readonly GameObject root;
    readonly Material mat;
    readonly Vector3 center, target;
    readonly float radius;
    readonly LineRenderer horizon, sunRay, sunDot;
    readonly List<LineRenderer> dayLines = new List<LineRenderer>();
    readonly List<LineRenderer> hourDots = new List<LineRenderer>();
    readonly List<LouverSceneLink.SunPathDay> days = new List<LouverSceneLink.SunPathDay>();
    string builtDay;
    bool builtAll, built, warned;

    /// <param name="envelope">우리 건물(날 전체) 범위. 궤적은 건물 바닥 중심을 둘러싸요</param>
    /// <param name="radiusOverride">궤적 반지름(m). 0이면 건물 크기의 1.5배</param>
    public GlareSunPath(Material mat, Bounds envelope, float radiusOverride)
    {
        this.mat = mat;
        center = new Vector3(envelope.center.x, envelope.min.y, envelope.center.z);
        target = envelope.center;
        radius = radiusOverride > 0f ? radiusOverride : 1.5f * Mathf.Max(envelope.size.x, Mathf.Max(envelope.size.y, envelope.size.z));
        root = new GameObject("Glare_SunPath");
        horizon = MakeLine("지평선", Horizon, radius * 0.003f, true);
        sunRay = MakeLine("해 방향", SunRay, radius * 0.004f, false);
        sunDot = MakeDot("지금 해", SunDot, radius * 0.06f);
    }

    LineRenderer MakeLine(string name, Color color, float width, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = mat;
        lr.useWorldSpace = true;
        lr.loop = loop;
        lr.widthMultiplier = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 2;
        lr.alignment = LineAlignment.View;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.positionCount = 0;
        return lr;
    }

    // 아주 짧은 선에 둥근 끝을 주면 카메라를 향하는 둥근 점이 돼요
    LineRenderer MakeDot(string name, Color color, float size)
    {
        var lr = MakeLine(name, color, size, false);
        lr.numCapVertices = 8;
        lr.positionCount = 2;
        return lr;
    }

    static void PlaceDot(LineRenderer dot, Vector3 p)
    {
        dot.SetPosition(0, p);
        dot.SetPosition(1, p + Vector3.up * 0.01f);
    }

    Vector3 OnDome(Vector3 dir)
    {
        return center + dir * radius;
    }

    public void SetVisible(bool on)
    {
        if (root != null && root.activeSelf != on) root.SetActive(on);
    }

    /// <summary>대표일이 바뀌었거나 처음이면 궤적을 다시 만들어요(바뀌지 않았으면 아무것도 안 해요).</summary>
    public void Rebuild(LouverSceneLink link, string currentDay, bool allDays)
    {
        if (days.Count == 0 && !link.TryGetSunPaths(days))
        {
            if (!warned) Debug.LogWarning("[Glare] 태양 궤적: 컨트롤러의 sun_louver.csv 데이터를 못 읽었어요(직접 계산 모드이거나 CSV를 아직 못 읽었어요).");
            warned = true;
            return;
        }
        if (built && currentDay == builtDay && allDays == builtAll) return;
        built = true;
        builtDay = currentDay;
        builtAll = allDays;
        labels.Clear();

        // 지평선 원 + 동서남북
        const int ring = 96;
        horizon.positionCount = ring;
        for (int i = 0; i < ring; i++)
        {
            float a = i * Mathf.PI * 2f / ring;
            horizon.SetPosition(i, center + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius);
        }
        AddLabel(center + Vector3.forward * radius * 1.06f, "북");
        AddLabel(center + Vector3.right * radius * 1.06f, "동");
        AddLabel(center + Vector3.back * radius * 1.06f, "남");
        AddLabel(center + Vector3.left * radius * 1.06f, "서");

        // 대표일마다 선 하나
        while (dayLines.Count < days.Count) dayLines.Add(MakeLine("궤적 " + days[dayLines.Count].day, OtherDay, 1f, false));
        LouverSceneLink.SunPathDay cur = null;
        for (int i = 0; i < days.Count; i++)
        {
            var d = days[i];
            var lr = dayLines[i];
            bool isCur = d.day == currentDay;
            if (isCur) cur = d;
            lr.gameObject.SetActive(isCur || allDays);
            lr.startColor = lr.endColor = isCur ? CurrentDay : OtherDay;
            lr.widthMultiplier = radius * (isCur ? 0.008f : 0.004f);
            lr.positionCount = d.dirs.Count;
            for (int k = 0; k < d.dirs.Count; k++) lr.SetPosition(k, OnDome(d.dirs[k]));
            if (allDays && !isCur && d.dirs.Count > 0)
            {
                int top = 0;   // 가장 높은 점(남중)에 대표일 이름
                for (int k = 1; k < d.dirs.Count; k++) if (d.dirs[k].y > d.dirs[top].y) top = k;
                AddLabel(OnDome(d.dirs[top]) + Vector3.up * radius * 0.03f, d.day);
            }
        }

        // 지금 대표일: 1시간마다 점, 3시간마다 글자
        int used = 0;
        if (cur != null)
        {
            for (int k = 0; k < cur.dirs.Count; k++)
            {
                int m = cur.minutes[k];
                if (m % 60 != 0) continue;
                if (used == hourDots.Count) hourDots.Add(MakeDot("정시", HourDot, radius * 0.02f));
                var dot = hourDots[used++];
                dot.gameObject.SetActive(true);
                PlaceDot(dot, OnDome(cur.dirs[k]));
                if ((m / 60) % 3 == 0) AddLabel(OnDome(cur.dirs[k]) + Vector3.up * radius * 0.035f, (m / 60) + "시");
            }
            int noon = 0;
            for (int k = 1; k < cur.dirs.Count; k++) if (cur.dirs[k].y > cur.dirs[noon].y) noon = k;
            AddLabel(OnDome(cur.dirs[noon]) + Vector3.up * radius * 0.08f, cur.day);
        }
        for (int i = used; i < hourDots.Count; i++) hourDots[i].gameObject.SetActive(false);
        if (cur == null && !warned)
        {
            Debug.LogWarning("[Glare] 태양 궤적: CSV에 '" + currentDay + "' 대표일이 없어요.");
            warned = true;
        }
    }

    void AddLabel(Vector3 p, string text)
    {
        labels.Add(new Label { pos = p, text = text });
    }

    /// <summary>지금 해 위치(해를 향하는 방향). 해가 없으면 hasSun = false</summary>
    public void UpdateSun(bool hasSun, Vector3 sunDir)
    {
        sunDot.gameObject.SetActive(hasSun);
        sunRay.gameObject.SetActive(hasSun);
        if (!hasSun) return;
        var p = OnDome(sunDir.normalized);
        PlaceDot(sunDot, p);
        sunRay.positionCount = 2;
        sunRay.SetPosition(0, p);
        sunRay.SetPosition(1, target);
    }

    public void Dispose()
    {
        if (root != null) Object.Destroy(root);
    }
}
