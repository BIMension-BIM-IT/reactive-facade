using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>받는 쪽 표면의 한 칸(정육면체 격자 한 칸 안에 떨어진 반사광을 모아요)</summary>
public class GlareCell
{
    public Vector3 posSum, nrmSum;
    public double wSum;
    public int receiver = -1;
    public bool touched;

    // 지금 계산 중인 시각
    public double stepPowerW;   // 이 칸에 떨어진 반사광 전력 합(W)
    public float stepMaxIrr;    // 이 칸에 떨어진 빛줄기 중 가장 센 것(받는 면 기준 W/m²)

    // 끝난 시각들
    public float lastIrr;       // 마지막 시각의 세기(W/m²)
    public double energyWh;     // 누적 에너지(Wh)
    public float exposureMin;   // 기준 이상으로 비친 시간(분)
    public float maxIrr;        // 가장 셌던 순간(W/m²)

    public Vector3 Position { get { return wSum > 0 ? posSum / (float)wSum : posSum; } }
    public Vector3 Normal { get { return nrmSum.sqrMagnitude > 1e-12f ? nrmSum.normalized : Vector3.up; } }
}

/// <summary>
/// 반사광이 떨어진 곳을 격자(cellSize m 정육면체)로 모아요. 빈 칸은 만들지 않아요(넓은 도시에서도 가벼워요).
/// 세기 = max(칸의 전력 합 ÷ 칸 넓이, 가장 센 빛줄기 하나). 앞은 여러 빛줄기가 겹칠 때, 뒤는 표본이 성길 때를 잡아요.
/// </summary>
public class GlareGrid
{
    public readonly float cellSize;
    readonly Dictionary<long, int> index = new Dictionary<long, int>();
    public readonly List<GlareCell> cells = new List<GlareCell>();
    readonly List<int> touchedList = new List<int>();
    public int steps;
    public float totalMinutes;

    public GlareGrid(float cellSize)
    {
        this.cellSize = Mathf.Max(0.1f, cellSize);
    }

    public float CellArea { get { return cellSize * cellSize; } }

    static long Key(int x, int y, int z)
    {
        const long o = 1L << 20;
        return ((x + o) << 42) | ((y + o) << 21) | (z + o);
    }

    public void Deposit(Vector3 p, Vector3 normal, float powerW, float irr, int receiver)
    {
        int x = Mathf.FloorToInt(p.x / cellSize);
        int y = Mathf.FloorToInt(p.y / cellSize);
        int z = Mathf.FloorToInt(p.z / cellSize);
        long key = Key(x, y, z);
        int i;
        if (!index.TryGetValue(key, out i))
        {
            i = cells.Count;
            cells.Add(new GlareCell());
            index[key] = i;
        }
        var c = cells[i];
        if (!c.touched) { c.touched = true; touchedList.Add(i); }
        float w = Mathf.Max(powerW, 1e-6f);
        c.posSum += p * w;
        c.nrmSum += normal * w;
        c.wSum += w;
        c.stepPowerW += powerW;
        if (irr > c.stepMaxIrr) c.stepMaxIrr = irr;
        if (c.receiver < 0) c.receiver = receiver;
    }

    /// <summary>한 시각이 끝났을 때 불러요. dtMin = 이 시각이 대표하는 시간(분)</summary>
    public void EndStep(float dtMin, float exposureThresholdWm2)
    {
        float area = CellArea;
        // 이번 시각에 빛을 못 받은 칸은 0으로
        for (int i = 0; i < cells.Count; i++) if (!cells[i].touched) cells[i].lastIrr = 0f;
        foreach (int i in touchedList)
        {
            var c = cells[i];
            float e = Mathf.Max((float)(c.stepPowerW / area), c.stepMaxIrr);
            c.lastIrr = e;
            c.energyWh += c.stepPowerW * dtMin / 60.0;
            if (e >= exposureThresholdWm2) c.exposureMin += dtMin;
            if (e > c.maxIrr) c.maxIrr = e;
            c.stepPowerW = 0;
            c.stepMaxIrr = 0f;
            c.touched = false;
        }
        touchedList.Clear();
        steps++;
        totalMinutes += dtMin;
    }
}

/// <summary>격자·광선·입면을 색칠한 메시로 바꿔요(Glare/VertexColorUnlit 셰이더용 정점 색)</summary>
public static class GlareVis
{
    // 어두운 보라 → 빨강 → 주황 → 노랑 → 흰빛 (inferno 비슷하게)
    static readonly Color[] Stops =
    {
        new Color(0.25f, 0.05f, 0.45f),
        new Color(0.60f, 0.10f, 0.50f),
        new Color(0.90f, 0.28f, 0.25f),
        new Color(0.98f, 0.60f, 0.10f),
        new Color(1.00f, 0.95f, 0.55f),
    };

    public static Color Heat(float t)
    {
        t = Mathf.Clamp01(t) * (Stops.Length - 1);
        int i = Mathf.Min(Mathf.FloorToInt(t), Stops.Length - 2);
        return Color.Lerp(Stops[i], Stops[i + 1], t - i);
    }

    public static Texture2D LegendTexture()
    {
        var tex = new Texture2D(256, 1, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int x = 0; x < 256; x++) tex.SetPixel(x, 0, Heat(x / 255f));
        tex.Apply();
        return tex;
    }

    /// <summary>값들 중 상위 q(0~1) 위치의 값. 아주 센 몇 칸 때문에 색이 다 어두워지지 않게 써요</summary>
    public static float Percentile(float[] values, float q)
    {
        var list = new List<float>();
        foreach (var v in values) if (v > 0f) list.Add(v);
        if (list.Count == 0) return 1f;
        list.Sort();
        int i = Mathf.Clamp(Mathf.RoundToInt(q * (list.Count - 1)), 0, list.Count - 1);
        return Mathf.Max(list[i], 1e-6f);
    }

    static void Apply(Mesh mesh, List<Vector3> v, List<Color> c, int[] idx, MeshTopology topo)
    {
        mesh.Clear();
        mesh.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(v);
        mesh.SetColors(c);
        mesh.SetIndices(idx, topo, 0);
        mesh.RecalculateBounds();
    }

    static void Basis(Vector3 n, out Vector3 a, out Vector3 b)
    {
        a = Mathf.Abs(n.y) > 0.9f ? Vector3.Cross(n, Vector3.forward) : Vector3.Cross(n, Vector3.up);
        a.Normalize();
        b = Vector3.Cross(n, a);
    }

    /// <summary>칸마다 표면 방향으로 사각형을 하나씩 그려요. values[i]가 vmax의 0.5% 미만이면 안 그려요.</summary>
    public static int BuildCells(Mesh mesh, List<GlareCell> cells, float[] values, float vmax, float size, float lift)
    {
        var v = new List<Vector3>();
        var c = new List<Color>();
        var idx = new List<int>();
        float h = size * 0.5f;
        for (int i = 0; i < cells.Count; i++)
        {
            float val = values[i];
            if (val <= vmax * 0.005f) continue;
            var cell = cells[i];
            Vector3 n = cell.Normal, p = cell.Position + n * lift, a, b;
            Basis(n, out a, out b);
            float t = Mathf.Clamp01(val / vmax);
            Color col = Heat(t);
            col.a = 0.55f + 0.4f * t;
            int s = v.Count;
            v.Add(p - a * h - b * h); v.Add(p + a * h - b * h); v.Add(p + a * h + b * h); v.Add(p - a * h + b * h);
            c.Add(col); c.Add(col); c.Add(col); c.Add(col);
            idx.Add(s); idx.Add(s + 1); idx.Add(s + 2);
            idx.Add(s); idx.Add(s + 2); idx.Add(s + 3);
        }
        Apply(mesh, v, c, idx.ToArray(), MeshTopology.Triangles);
        return idx.Count / 6;
    }

    public static void BuildLines(Mesh mesh, List<Vector3> pts, List<Color> cols)
    {
        var idx = new int[pts.Count];
        for (int i = 0; i < idx.Length; i++) idx[i] = i;
        Apply(mesh, pts, cols, idx, MeshTopology.Lines);
    }

    /// <summary>입면 위 발생 위치(어느 부분의 반사가 주변에 닿는지)를 그려요</summary>
    public static void BuildSources(Mesh mesh, List<GlareEmitter> emitters, float[] perArea, float vmax, float lift)
    {
        var v = new List<Vector3>();
        var c = new List<Color>();
        var idx = new List<int>();
        for (int i = 0; i < emitters.Count; i++)
        {
            float val = perArea[i];
            if (val <= vmax * 0.005f) continue;
            var e = emitters[i];
            Vector3 n = GlareOptics.NormalOf(e.side), r = GlareOptics.RightOf(e.side);
            Vector3 p = e.pos + n * lift;
            Vector3 a = r * (e.width * 0.5f), b = Vector3.up * (e.height * 0.5f);
            float t = Mathf.Clamp01(val / vmax);
            Color col = Heat(t);
            col.a = 0.5f + 0.45f * t;
            int s = v.Count;
            v.Add(p - a - b); v.Add(p + a - b); v.Add(p + a + b); v.Add(p - a + b);
            c.Add(col); c.Add(col); c.Add(col); c.Add(col);
            idx.Add(s); idx.Add(s + 1); idx.Add(s + 2);
            idx.Add(s); idx.Add(s + 2); idx.Add(s + 3);
        }
        Apply(mesh, v, c, idx.ToArray(), MeshTopology.Triangles);
    }
}
