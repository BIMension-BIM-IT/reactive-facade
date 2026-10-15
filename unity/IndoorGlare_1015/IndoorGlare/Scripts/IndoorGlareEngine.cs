using System.Collections.Generic;
using UnityEngine;

namespace IndoorGlare
{
    /// <summary>
    /// 루버 경우(반사광 GlareSim과 같은 4가지).
    ///  ResponsiveInner = 지금 루버 각도 + 날 바깥 외측 유리(내부 루버, 외측 유리 가시광 투과율을 한 번 더 곱해요)
    ///  ResponsiveOuter = 지금 루버 각도, 외측 유리 없음(외부 루버)
    ///  AllOpen = 모두 0°(열림), NoLouver = 루버 없음. 이 둘은 외측 유리 없이 계산해요
    /// </summary>
    public enum IndoorLouverCase { ResponsiveInner, ResponsiveOuter, AllOpen, NoLouver }

    /// <summary>실내 점에서 한 방향으로 쏜 광선이 어디로 가는지</summary>
    public enum IndoorHit
    {
        Interior,   // 창을 지나지 못함(벽·슬래브·가구 등)
        Sky,        // 창을 지나 하늘(또는 모델 밖 지면)까지 막힘없음
        Ground,     // 창을 지나 바닥·지붕 같은 수평면에 닿음(지면 휘도로 봐요)
        Wall        // 창을 지나 주변 건물 벽 등에 닿음(주변 건물 면 휘도로 봐요)
    }

    /// <summary>광선 검사(유니티 물리 씬 또는 시험용 가짜)</summary>
    public interface IIndoorTracer
    {
        /// <summary>루버 날을 뺀 모델(유리·벽·주변 건물)에 대해 광선을 쏴요. exit = 마지막으로 지난 유리 위 점</summary>
        IndoorHit TraceStatic(Vector3 p, Vector3 d, out Vector3 exit);
        /// <summary>지금 자세의 루버 날에 막히는지</summary>
        bool BladeBlocked(Vector3 p, Vector3 d);
        /// <summary>유리 위 점 바로 앞의 날(LouverSceneLink.units 번호), 없으면 -1</summary>
        int UnitNear(Vector3 exit);
    }

    /// <summary>센서 위치 하나(여러 시선 방향이 같이 써요)</summary>
    public class IndoorPoint
    {
        public Vector3 pos;
        public int zone;
        // 창으로 보이는 표본을 패치(slot)별로 묶어 둬요: slot j의 표본 = samp[slotStart[j] .. slotStart[j+1])
        public int[] slotPatch, slotStart, samp, unit;
        public float[] phi;     // 표본 방향이 앞 날의 입면과 이루는 평면각(라디안)
        public float[] tbar;    // 이번 시각에 slot별 루버 통과 비율
        public bool sunStatic, sunVis;

        public int VisibleCount { get { return samp == null ? 0 : samp.Length; } }
        public int SlotCount { get { return slotPatch == null ? 0 : slotPatch.Length; } }
    }

    /// <summary>센서 하나 = 위치 + 시선 방향</summary>
    public class IndoorSensor
    {
        public int pt;
        public Vector3 view, up, rt;
        public float[] C, G;    // slot별 연직조도 계수 Σω·cosθ, 글레어 항 계수 Σω/P²
    }

    /// <summary>영역(방) 하나의 상호반사 계산용 값</summary>
    public class IndoorZoneData
    {
        public float roomArea;                       // 방 겉넓이(m²)
        public readonly float[] glassArea = new float[4];   // 입면별 유리 넓이(m²)
        public readonly List<int>[] sideUnits = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        public float er;                             // 이번 시각 상호반사 조도(lx)
    }

    /// <summary>
    /// 실내 눈부심 계산 핵심(Grasshopper Glare2를 옮기고 반응형 루버를 더했어요). 유니티 화면·코루틴과 분리했어요.
    ///
    ///  1) 점마다 천공 표본 방향으로 광선: 창을 지나 밖이 보이는 방향만 남기고, 그 앞 날과 평면각을 기억(한 번만)
    ///  2) 시각마다: 날 각도로 표본별 통과 비율 → 패치별 평균(tbar)
    ///  3) Ev = 상호반사 + Σ vt·L·tbar·C + Σ vt·L_날·(1−tbar)·C + (해가 보이면) vt·E_dn·cos
    ///  4) DGP = 5.87e-5·Ev + 0.092·log10(1 + Σ L²·ω·tbar/(Ev^1.87·P²)) + 0.159, L > 2000 cd/m²와 해 원반이 광원
    ///  해는 점마다 실제 날 상자에 광선을 쏴서 보이는지 정해요(평균 비율이 아니라 보이거나 안 보이거나).
    /// </summary>
    public class IndoorGlareEngine
    {
        public float vt = 0.6f;              // 이번 경우의 투과율(유리 VT × 외측 유리 VT)
        public float rhoIn = 0.5f;
        public float bladeWidth = 0.30f, bladeSpacing = 0.35f;
        public IndoorSkyPatches sp;
        public List<IndoorLouverLink.Unit> units = new List<IndoorLouverLink.Unit>();
        public readonly List<IndoorPoint> points = new List<IndoorPoint>();
        public readonly List<IndoorSensor> sensors = new List<IndoorSensor>();
        public readonly List<IndoorZoneData> zones = new List<IndoorZoneData>();

        public int CtxPatch { get { return sp.Count; } }

        // ---------------- 한 번만: 보이는 방향 ----------------

        public void TracePoint(IndoorPoint p, IIndoorTracer tr)
        {
            int ns = sp.SampDir.Length;
            var patchOf = new List<int>();
            var samp = new List<int>();
            var unit = new List<int>();
            var phi = new List<float>();
            for (int k = 0; k < ns; k++)
            {
                Vector3 d = sp.SampDir[k], exit;
                IndoorHit h = tr.TraceStatic(p.pos, d, out exit);
                if (h == IndoorHit.Interior) continue;
                int u = tr.UnitNear(exit);
                samp.Add(k);
                patchOf.Add(h == IndoorHit.Wall ? CtxPatch : sp.SampPatch[k]);
                unit.Add(u);
                phi.Add(u >= 0 && u < units.Count ? IndoorGlareCore.PlanAngle(d, units[u].side) : 0f);
            }

            // 패치별로 묶기
            int n = samp.Count;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => patchOf[a] != patchOf[b] ? patchOf[a].CompareTo(patchOf[b]) : a.CompareTo(b));
            var sPatch = new List<int>();
            var sStart = new List<int>();
            p.samp = new int[n]; p.unit = new int[n]; p.phi = new float[n];
            for (int i = 0; i < n; i++)
            {
                int o = order[i];
                if (sPatch.Count == 0 || sPatch[sPatch.Count - 1] != patchOf[o]) { sPatch.Add(patchOf[o]); sStart.Add(i); }
                p.samp[i] = samp[o]; p.unit[i] = unit[o]; p.phi[i] = phi[o];
            }
            sStart.Add(n);
            p.slotPatch = sPatch.ToArray();
            p.slotStart = sStart.ToArray();
            p.tbar = new float[p.slotPatch.Length];
        }

        /// <summary>센서(시선 방향)마다 slot별 계수. 시선 뒤쪽 표본은 0이에요</summary>
        public void SensorCoeffs(IndoorSensor s, IndoorPoint p)
        {
            s.view = s.view.normalized;
            IndoorGlareCore.Frame(s.view, out s.up, out s.rt);
            int m = p.SlotCount;
            s.C = new float[m];
            s.G = new float[m];
            for (int j = 0; j < m; j++)
            {
                double c = 0, g = 0;
                for (int i = p.slotStart[j]; i < p.slotStart[j + 1]; i++)
                {
                    int k = p.samp[i];
                    Vector3 d = sp.SampDir[k];
                    float cs = Vector3.Dot(d, s.view);
                    if (cs <= 0f) continue;
                    float P = IndoorGlareCore.PositionIndex(s.view, s.up, s.rt, d);
                    if (P <= 0f) continue;
                    c += sp.SampOmega[k] * cs;
                    g += sp.SampOmega[k] / (P * P);
                }
                s.C[j] = (float)c;
                s.G[j] = (float)g;
            }
        }

        // ---------------- 시각마다 ----------------

        bool UnitBlades(int u, IndoorLouverCase lc, out float angle)
        {
            angle = 0f;
            if (lc == IndoorLouverCase.NoLouver || u < 0 || u >= units.Count) return false;
            var U = units[u];
            if (!U.active) return false;             // 숨긴 날(북면 루버 끔 등)
            angle = IsResponsive(lc) ? U.angle : 0f;
            return true;
        }

        public static bool IsResponsive(IndoorLouverCase lc)
        {
            return lc == IndoorLouverCase.ResponsiveInner || lc == IndoorLouverCase.ResponsiveOuter;
        }

        /// <summary>영역마다 상호반사 조도(적분구 근사): E_r = Φ·ρ / (A_방·(1−ρ)), Φ = Σ vt·A_유리·E_창밖·루버 통과</summary>
        public void ZoneIllum(IndoorSkyState sky, IndoorLouverCase lc)
        {
            foreach (var z in zones)
            {
                z.er = 0f;
                if (!sky.Lit || z.roomArea <= 0f) continue;
                double flux = 0;
                for (int k = 0; k < 4; k++)
                {
                    float A = z.glassArea[k];
                    if (A <= 0f) continue;
                    var side = (IndoorSide)k;
                    Vector3 n = IndoorFacade.NormalOf(side);
                    // 그 영역·입면의 날 각도(같은 층·입면은 같은 각도로 돌아요 → 처음 보이는 날)
                    float ang = 0f;
                    bool blades = false;
                    foreach (int u in z.sideUnits[k])
                        if (UnitBlades(u, lc, out ang)) { blades = true; break; }
                    double e = 0;
                    for (int p = 0; p < sp.Count; p++)
                    {
                        float cs = Vector3.Dot(n, sp.Dir[p]);
                        if (cs <= 0f) continue;
                        float t = blades ? IndoorGlareCore.LouverTransmission(IndoorGlareCore.PlanAngle(sp.Dir[p], side), ang, bladeWidth, bladeSpacing) : 1f;
                        e += sky.L[p] * cs * sp.Omega[p] * t;
                    }
                    if (sky.SunUp && sky.DnLux > 0f)
                    {
                        float cs = Vector3.Dot(n, sky.Sun);
                        if (cs > 0f)
                        {
                            float t = blades ? IndoorGlareCore.LouverTransmission(IndoorGlareCore.PlanAngle(sky.Sun, side), ang, bladeWidth, bladeSpacing) : 1f;
                            e += sky.DnLux * cs * t;
                        }
                    }
                    flux += vt * A * e;
                }
                z.er = (float)(flux * rhoIn / (z.roomArea * (1 - rhoIn)));
            }
        }

        /// <summary>점 하나: slot별 루버 통과 비율과 해가 보이는지</summary>
        public void PointStep(IndoorPoint p, IndoorLouverCase lc, bool sunVisible)
        {
            p.sunVis = sunVisible;
            for (int j = 0; j < p.SlotCount; j++)
            {
                int a = p.slotStart[j], b = p.slotStart[j + 1];
                float sum = 0f;
                for (int i = a; i < b; i++)
                {
                    float ang;
                    sum += UnitBlades(p.unit[i], lc, out ang) ? IndoorGlareCore.LouverTransmission(p.phi[i], ang, bladeWidth, bladeSpacing) : 1f;
                }
                p.tbar[j] = b > a ? sum / (b - a) : 1f;
            }
        }

        /// <summary>센서 하나의 DGP. er = 그 점 영역의 상호반사 조도</summary>
        public float SensorDgp(IndoorSensor s, IndoorPoint p, IndoorSkyState sky, float er, out float ev)
        {
            ev = 0f;
            if (!sky.Lit) return 0f;
            double e = er, sum = 0;
            for (int j = 0; j < p.SlotCount; j++)
            {
                float c = s.C[j];
                if (c <= 0f && s.G[j] <= 0f) continue;
                float t = p.tbar[j];
                double l = vt * sky.L[p.slotPatch[j]];
                e += (l * t + vt * sky.BladeLum * (1f - t)) * c;
                if (l > IndoorGlareCore.SrcLum) sum += l * l * t * s.G[j];
            }
            if (p.sunVis && sky.SunUp && sky.DnLux > 0f)
            {
                float cs = Vector3.Dot(sky.Sun, s.view);
                if (cs > 0f)
                {
                    e += vt * sky.DnLux * cs;
                    float P = IndoorGlareCore.PositionIndex(s.view, s.up, s.rt, sky.Sun);
                    if (P > 0f)
                    {
                        double ls = vt * sky.DnLux / IndoorGlareCore.SunOmega;
                        sum += ls * ls * IndoorGlareCore.SunOmega / (P * P);
                    }
                }
            }
            ev = (float)e;
            return IndoorGlareCore.Dgp(e, sum);
        }
    }
}
