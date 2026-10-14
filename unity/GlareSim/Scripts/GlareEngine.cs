using System.Collections.Generic;
using UnityEngine;

/// <summary>반사광을 내보내는 입면 위의 한 점(날 한 장 앞, 층 높이의 일부를 대표해요)</summary>
public class GlareEmitter
{
    public Vector3 pos;          // 광선 시작점(날 바깥 끝보다 조금 밖)
    public GlareSide side;
    public int unit;             // 어느 날(LouverSceneLink.units 번호)에 붙은 점인지
    public float area;           // 이 점이 대표하는 입면 넓이(m²)
    public float width, height;  // 표시용 크기(m)
    public bool glass = true;    // 뒤에 유리가 있는지(옥상 RF 스크린 등은 false)
}

/// <summary>반사가 어디서 나왔는지(광선 색)</summary>
public enum GlareRaySource { Glass, Blade, OuterGlass }

/// <summary>한 시각(또는 하루) 동안의 합계. 현재 시각은 W, 하루 누적은 Wh로 쌓아요.</summary>
public class GlareTotals
{
    public double incident;      // 입면에 들어온 직달일사(그늘 제외)
    public double glass, blade;  // 건물 밖으로 나간 반사(날 뒤 유리, 날)
    public double outerGlass;    // 건물 밖으로 나간 반사(날 바깥 외측 유리, 반응형 내부 루버일 때만)
    public double received;      // 주변 건물·지면에 닿음
    public double blocked;       // 막기만 하는 것(대지·도로 등)에 닿음. 칸·건물별 결과에는 안 넣어요
    public double toSky;         // 아무것에도 안 닿음(하늘, 먼 곳)
    public double litArea, shadedArea;   // 해를 받는/주변 건물 그늘인 입면 넓이(m², 시각마다 더해요)
    public readonly Dictionary<int, double> perReceiver = new Dictionary<int, double>();

    /// <summary>건물 밖으로 나간 반사 전체</summary>
    public double Reflected { get { return glass + blade + outerGlass; } }

    public void Clear()
    {
        incident = glass = blade = outerGlass = received = blocked = toSky = litArea = shadedArea = 0;
        perReceiver.Clear();
    }

    public void AddScaled(GlareTotals o, double k)
    {
        incident += o.incident * k; glass += o.glass * k; blade += o.blade * k; outerGlass += o.outerGlass * k;
        received += o.received * k; blocked += o.blocked * k; toSky += o.toSky * k;
        litArea += o.litArea; shadedArea += o.shadedArea;
        foreach (var kv in o.perReceiver)
        {
            double v;
            perReceiver.TryGetValue(kv.Key, out v);
            perReceiver[kv.Key] = v + kv.Value * k;
        }
    }
}

/// <summary>
/// 반사광 계산의 핵심(유니티 화면·코루틴과 분리해서 따로 시험할 수 있게 했어요).
///
/// 점 하나마다:
///  1) 해가 그 입면 앞에 있는지, 주변 건물 그늘인지 확인
///  2) 그 날의 각도로 LouverArray2D를 계산(각도마다 한 번만, 캐시)
///  3) 유리 반사: DNI × cos(입사각) × 유리 반사율(입사각) × (날 사이로 들어왔다 나가는 비율) × 넓이
///     날 반사:  DNI × cos(날 입사각) × 날 정반사율 × (빠져나가는 날 폭 ÷ 간격) × 넓이
///     외측 유리가 있으면(반응형 내부 루버):
///       외측 유리 반사 = DNI × cos(입사각) × 외측 유리 반사율(입사각) × 넓이 (날 바깥이라 날에 막히지 않아요)
///       위 유리·날 반사에는 외측 유리 투과율을 들어올 때 한 번, 나갈 때 한 번 곱해요
///  4) 반사 방향으로 광선을 쏴서 처음 닿는 주변 표면 칸에 전력(W)을 쌓아요
/// </summary>
public class GlareEngine
{
    public float glassReflectance = 0.15f;
    public float bladeSpecular = 0.2f;
    public float outerGlassReflectance = 0.06f;
    public float outerGlassTransmittance = 0.8f;
    public bool includeGlass = true;
    public bool includeBlades = true;
    public bool shadowRaycast = true;
    public float maxDist = 2000f;
    public readonly LouverArray2D array = new LouverArray2D();
    public IGlareRaycaster raycaster;

    public delegate void RayCallback(Vector3 from, Vector3 to, float powerW, GlareRaySource source);
    public RayCallback onRay;           // 주변에 닿은 광선(표시용)

    Vector3 sun;
    float dni;
    readonly Vector3[] normals = new Vector3[4];
    readonly float[] cosI = new float[4];
    readonly float[] glassR = new float[4];
    readonly float[] outerR = new float[4];
    readonly float[] outerT = new float[4];
    readonly bool[] lit = new bool[4];
    readonly Dictionary<int, LouverFaceResult> cache = new Dictionary<int, LouverFaceResult>();

    public float Dni { get { return dni; } }

    public void BeginStep(Vector3 sunDir, float dniWm2)
    {
        sun = sunDir.normalized;
        dni = dniWm2;
        cache.Clear();
        for (int k = 0; k < 4; k++)
        {
            normals[k] = GlareOptics.NormalOf((GlareSide)k);
            cosI[k] = Vector3.Dot(sun, normals[k]);
            lit[k] = dni > 0f && cosI[k] > 1e-3f;
            glassR[k] = GlareOptics.GlassReflectance(glassReflectance, cosI[k]);
            outerR[k] = GlareOptics.GlassReflectance(outerGlassReflectance, cosI[k]);
            outerT[k] = GlareOptics.GlassTransmittance(outerGlassTransmittance, outerGlassReflectance, cosI[k]);
        }
    }

    public LouverFaceResult Optics(GlareSide side, float angleDeg, bool blades)
    {
        // 날은 앞뒤가 같아서 180도 돌려도 같은 모양이에요 → -90~90으로 모아서 캐시
        float a = Mathf.Repeat(angleDeg + 90f, 180f) - 90f;
        int q = Mathf.RoundToInt(a * 10f);
        int key = ((int)side * 2 + (blades ? 1 : 0)) * 10000 + (q + 5000);
        LouverFaceResult r;
        if (!cache.TryGetValue(key, out r))
        {
            r = array.Evaluate(sun, side, q / 10f, blades);
            cache[key] = r;
        }
        return r;
    }

    /// <summary>
    /// 점 하나를 계산해요. angleDeg = 그 점 앞 날의 각도(0 = 입면에 수직, 열림), blades = 날이 있는지(숨겨졌으면 false),
    /// outerSkin = 날 바깥에 외측 유리가 있는지(반응형 내부 루버).
    /// 주변에 닿은 전력(W)을 돌려줘요.
    /// </summary>
    public float ProcessEmitter(GlareEmitter e, float angleDeg, bool blades, bool outerSkin, GlareGrid grid, GlareTotals totals)
    {
        int k = (int)e.side;
        if (!lit[k]) return 0f;

        Vector3 hp, hn;
        int rid;
        if (shadowRaycast && raycaster != null && raycaster.Raycast(e.pos, sun, maxDist, out hp, out hn, out rid))
        {
            totals.shadedArea += e.area;
            return 0f;
        }
        totals.litArea += e.area;
        totals.incident += dni * cosI[k] * e.area;

        LouverFaceResult f = Optics(e.side, blades ? angleDeg : 0f, blades);
        float got = 0f;
        bool outer = outerSkin && e.glass;     // 뒤에 유리가 없는 날(옥상 스크린 등)은 외측 유리도 없다고 봐요
        float tIn = outer ? outerT[k] : 1f;    // 외측 유리를 지나 안으로 들어오는 비율

        if (outer && includeGlass)
        {
            float flux = dni * cosI[k] * outerR[k];
            float p = flux * e.area;
            totals.outerGlass += p;
            got += Cast(e.pos, f.glassReflectDir, p, dni * outerR[k], GlareRaySource.OuterGlass, grid, totals);
        }
        if (includeGlass && e.glass && f.glassEscape > 0f)
        {
            float tt = tIn * tIn;                                          // 같은 각도로 들어왔다 나가요
            float flux = dni * cosI[k] * glassR[k] * f.glassEscape * tt;  // 입면 1 m²에서 나가는 W
            float p = flux * e.area;
            float beam = dni * glassR[k] * f.glassEscape * tt;            // 빛줄기에 수직인 면에서의 세기
            totals.glass += p;
            got += Cast(e.pos, f.glassReflectDir, p, beam, GlareRaySource.Glass, grid, totals);
        }
        if (includeBlades && blades && f.bladeEscapeLen > 0f)
        {
            float cross = Mathf.Abs(Vector3.Dot(f.bladeReflectDir, normals[k]));
            float tt = outer ? tIn * GlareOptics.GlassTransmittance(outerGlassTransmittance, outerGlassReflectance, cross) : 1f;
            float flux = dni * f.bladeCos * bladeSpecular * f.bladeEscapeLen / array.spacing * tt;
            float p = flux * e.area;
            float beam = Mathf.Min(dni * bladeSpecular * tt, flux / Mathf.Max(cross, 0.05f));
            totals.blade += p;
            got += Cast(e.pos, f.bladeReflectDir, p, beam, GlareRaySource.Blade, grid, totals);
        }
        return got;
    }

    float Cast(Vector3 o, Vector3 dir, float powerW, float beamWm2, GlareRaySource source, GlareGrid grid, GlareTotals totals)
    {
        if (powerW <= 0f) return 0f;
        Vector3 hp, hn;
        int rid;
        if (raycaster != null && raycaster.Raycast(o, dir, maxDist, out hp, out hn, out rid))
        {
            if (rid == GlareReceivers.BlockerId)              // 대지·도로 등: 광선은 여기서 멈추고 결과에는 안 넣어요
            {
                totals.blocked += powerW;
                return 0f;
            }
            if (Vector3.Dot(hn, dir) > 0f) hn = -hn;          // 뒷면에 맞았으면 법선을 광선 쪽으로
            float cosR = Mathf.Clamp01(-Vector3.Dot(hn, dir));
            grid.Deposit(hp, hn, powerW, beamWm2 * cosR, rid);
            totals.received += powerW;
            double v;
            totals.perReceiver.TryGetValue(rid, out v);
            totals.perReceiver[rid] = v + powerW;
            if (onRay != null) onRay(o, hp, powerW, source);
            return powerW;
        }
        totals.toSky += powerW;
        return 0f;
    }
}
