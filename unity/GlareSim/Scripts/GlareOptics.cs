using System;
using UnityEngine;

/// <summary>입면 방향. 순서는 루버 쪽(동 남 서 북)과 같지만 반사광 쪽에서 따로 정의해요(루버 스크립트와 분리).</summary>
public enum GlareSide { East = 0, South = 1, West = 2, North = 3 }

/// <summary>
/// 반사광 계산에 쓰는 광학 함수 모음이에요. (유니티 오브젝트와 상관없는 순수 계산)
/// </summary>
public static class GlareOptics
{
    /// <summary>
    /// 맑은 날 직달일사(DNI, W/m²)를 태양 고도(도)로 어림해요.
    /// 대기질량은 Kasten-Young 식, 감쇠는 Meinel 모델(1353 × 0.7^(AM^0.678))이에요.
    /// 고도 90°에서 약 950 W/m², 30°에서 약 820 W/m², 10°에서 약 600 W/m².
    /// </summary>
    public static float ClearSkyDNI(float altitudeDeg)
    {
        if (altitudeDeg <= 0f) return 0f;
        double h = altitudeDeg;
        double am = 1.0 / (Math.Sin(h * Math.PI / 180.0) + 0.50572 * Math.Pow(h + 6.07995, -1.6364));
        return (float)(1353.0 * Math.Pow(0.7, Math.Pow(am, 0.678)));
    }

    /// <summary>유리 반사율(Schlick 근사). r0 = 수직으로 들어올 때의 외부 반사율, cosI = 입사각 코사인</summary>
    public static float GlassReflectance(float r0, float cosI)
    {
        cosI = Mathf.Clamp01(cosI);
        float m = 1f - cosI;
        return r0 + (1f - r0) * m * m * m * m * m;
    }

    /// <summary>
    /// 유리 투과율 어림. t0 = 수직으로 들어올 때의 일사 투과율, r0 = 수직 입사 반사율, cosI = 입사각 코사인.
    /// 비스듬할수록 반사가 늘어난 만큼 투과가 줄어요(흡수는 각도와 상관없다고 봐요).
    /// </summary>
    public static float GlassTransmittance(float t0, float r0, float cosI)
    {
        float r = GlassReflectance(r0, cosI);
        return t0 * Mathf.Clamp01((1f - r) / Mathf.Max(1e-4f, 1f - r0));
    }

    /// <summary>거울 반사 방향. incoming = 빛이 나아가는 방향, normal = 면의 법선(단위벡터)</summary>
    public static Vector3 Reflect(Vector3 incoming, Vector3 normal)
    {
        return incoming - 2f * Vector3.Dot(incoming, normal) * normal;
    }

    /// <summary>입면 바깥 방향(법선)</summary>
    public static Vector3 NormalOf(GlareSide s)
    {
        switch (s)
        {
            case GlareSide.East: return new Vector3(1, 0, 0);
            case GlareSide.South: return new Vector3(0, 0, -1);
            case GlareSide.West: return new Vector3(-1, 0, 0);
            default: return new Vector3(0, 0, 1);
        }
    }

    /// <summary>입면을 바깥에서 볼 때 오른쪽</summary>
    public static Vector3 RightOf(GlareSide s)
    {
        return Vector3.Cross(Vector3.up, -NormalOf(s));
    }
}

/// <summary>한 입면(같은 각도로 늘어선 날 + 그 뒤 유리)을 해 하나에 대해 계산한 결과</summary>
public struct LouverFaceResult
{
    public float glassLit;          // 유리 중 해를 받는 비율(0~1)
    public float glassEscape;       // 해를 받고, 반사가 날 사이로 빠져나가는 유리 비율(0~1)
    public float bladeLitLen;       // 날 한 장에서 해를 받는 폭(m)
    public float bladeEscapeLen;    // 그중 반사가 날 밖으로 빠져나가는 폭(m)
    public float bladeCos;          // 날 면에 들어오는 햇빛의 입사각 코사인(3D)
    public Vector3 bladeReflectDir; // 날에서 반사된 빛의 방향(월드)
    public Vector3 glassReflectDir; // 유리에서 반사된 빛의 방향(월드)
}

/// <summary>
/// 수직 루버 날 배열을 위에서 본 2D로 계산해요(날이 위아래로 길어서 평면만 보면 돼요).
/// u = 입면 바깥 방향(유리면 u = 0), v = 입면을 바깥에서 볼 때 오른쪽.
/// 날 k의 중심은 (centerOffset, k × spacing)이고, 각도 θ만큼 돌아가 있어요(0 = 입면에 수직 = 열림).
/// 각도 부호는 sun_louver.csv와 같아요: 양수 = 날의 바깥쪽 끝이 바깥에서 볼 때 오른쪽.
///
/// 표본점에서 광선을 쏘아서 확인해요.
///  - 유리: 해를 받는지(해 쪽으로 쏜 광선이 날에 막히는지), 반사광이 날 사이로 빠져나가는지
///  - 날: 해를 받는 면에서 해를 받는지(옆 날 그림자), 반사광이 옆 날에 막히지 않고 빠져나가는지
/// 한 번 튄 빛만 봐요(날→유리→밖 같은 여러 번 반사는 무시).
/// </summary>
public class LouverArray2D
{
    public float spacing = 0.35f;       // 날 간격(m)
    public float width = 0.30f;         // 날 폭(m)
    public float centerOffset = 0.25f;  // 유리면에서 날 회전 중심까지(m). 날 폭의 절반보다 커야 해요
    public int glassSamples = 64;
    public int bladeSamples = 48;

    const float Eps = 1e-5f;

    public LouverFaceResult Evaluate(Vector3 sunDir, GlareSide side, float angleDeg, bool hasBlades)
    {
        Vector3 n = GlareOptics.NormalOf(side);
        Vector3 r = GlareOptics.RightOf(side);
        var res = new LouverFaceResult();
        float sn = Vector3.Dot(sunDir, n);
        float sr = Vector3.Dot(sunDir, r);
        res.glassReflectDir = GlareOptics.Reflect(-sunDir, n);
        if (sn <= 1e-4f) return res;   // 해가 입면 뒤에 있어요

        if (!hasBlades)
        {
            res.glassLit = 1f;
            res.glassEscape = 1f;
            return res;
        }

        float th = angleDeg * Mathf.Deg2Rad;
        float bu = Mathf.Cos(th), bv = Mathf.Sin(th);   // 날 방향
        float half = 0.5f * width;

        // 1) 유리
        int lit = 0, esc = 0;
        for (int i = 0; i < glassSamples; i++)
        {
            float v = (i + 0.5f) / glassSamples * spacing;
            float tb;
            bool blocked = TraceBlades(Eps, v, sn, sr, int.MinValue, bu, bv, half, out tb);
            if (blocked) continue;
            lit++;
            // 유리에서 반사: 평면에서는 v 성분만 뒤집혀요
            blocked = TraceBlades(Eps, v, sn, -sr, int.MinValue, bu, bv, half, out tb);
            if (!blocked) esc++;
        }
        res.glassLit = (float)lit / glassSamples;
        res.glassEscape = (float)esc / glassSamples;

        // 2) 날 (해를 받는 면 하나)
        float nu = -bv, nv = bu;                 // 날 면 법선
        float d = nu * sn + nv * sr;             // 3D 입사각 코사인과 같아요(법선이 수평이라서)
        if (d < 0f) { nu = -nu; nv = -nv; d = -d; }
        res.bladeCos = d;
        Vector3 nf3 = n * nu + r * nv;
        res.bladeReflectDir = GlareOptics.Reflect(-sunDir, nf3);
        if (d > 1e-4f)
        {
            float ru = -sn + 2f * d * nu;        // 반사 방향(평면)
            float rv = -sr + 2f * d * nv;
            int bl = 0, be = 0;
            for (int j = 0; j < bladeSamples; j++)
            {
                float lam = (j + 0.5f) / bladeSamples - 0.5f;
                float pu = centerOffset + lam * width * bu + nu * Eps;
                float pv = lam * width * bv + nv * Eps;
                float tb;
                if (TraceBlades(pu, pv, sn, sr, 0, bu, bv, half, out tb)) continue;   // 옆 날 그림자
                bl++;
                bool hitBlade = TraceBlades(pu, pv, ru, rv, 0, bu, bv, half, out tb);
                float tGlass = ru < -1e-9f ? -pu / ru : float.MaxValue;
                if (hitBlade && tb < tGlass) continue;          // 옆 날에 막혀요
                if (ru > 1e-6f) be++;                            // 밖으로 나가요
                // ru <= 0 이면 유리로 들어가요(여기서는 버려요)
            }
            res.bladeLitLen = (float)bl / bladeSamples * width;
            res.bladeEscapeLen = (float)be / bladeSamples * width;
        }
        return res;
    }

    /// <summary>
    /// (ou, ov)에서 (du, dv) 방향으로 쏜 광선이 날(skipK 제외)에 맞는지 확인해요. 맞으면 가장 가까운 거리를 tHit에 줘요.
    /// </summary>
    bool TraceBlades(float ou, float ov, float du, float dv, int skipK, float bu, float bv, float half, out float tHit)
    {
        tHit = float.MaxValue;
        float uExt = half * Mathf.Abs(bu), vExt = half * Mathf.Abs(bv);
        float uMin = centerOffset - uExt, uMax = centerOffset + uExt;

        // 광선이 날이 있는 띠(uMin~uMax) 안에 있는 구간
        float t0, t1;
        if (Mathf.Abs(du) < 1e-9f)
        {
            if (ou < uMin - 1e-6f || ou > uMax + 1e-6f) return false;
            t0 = 0f; t1 = 50f;
        }
        else
        {
            float a = (uMin - ou) / du, b = (uMax - ou) / du;
            t0 = Mathf.Min(a, b); t1 = Mathf.Max(a, b);
            if (t1 < 0f) return false;
            if (t0 < 0f) t0 = 0f;
            t1 = Mathf.Min(t1, t0 + 50f);
        }
        float va = ov + dv * t0, vb = ov + dv * t1;
        float vLo = Mathf.Min(va, vb) - vExt, vHi = Mathf.Max(va, vb) + vExt;
        int k0 = Mathf.FloorToInt(vLo / spacing) - 1;
        int k1 = Mathf.CeilToInt(vHi / spacing) + 1;
        if (k1 - k0 > 4000) k1 = k0 + 4000;

        float eu = width * bu, ev = width * bv;   // 날 선분 벡터
        float denom = du * ev - dv * eu;           // cross(d, e)
        if (Mathf.Abs(denom) < 1e-12f) return false; // 광선이 날과 나란해요
        bool hit = false;
        for (int k = k0; k <= k1; k++)
        {
            if (k == skipK) continue;
            float pu = centerOffset - half * bu;
            float pv = k * spacing - half * bv;
            float wu = pu - ou, wv = pv - ov;
            float t = (wu * ev - wv * eu) / denom;     // cross(w, e) / cross(d, e)
            float lam = (wu * dv - wv * du) / denom;   // cross(w, d) / cross(d, e)
            if (t > 1e-6f && lam >= 0f && lam <= 1f && t < tHit)
            {
                tHit = t;
                hit = true;
            }
        }
        return hit;
    }
}
