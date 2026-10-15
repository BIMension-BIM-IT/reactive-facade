using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace IndoorGlare
{
    /// <summary>하늘 밝기를 정하는 방법</summary>
    public enum IndoorSkyModel { Clear, PartlyCloudy, Overcast, Epw }

    /// <summary>
    /// 천공 패치: Tregenza 145(하늘) + 145(지면). Grasshopper Glare2의 SkyPatches와 순서·크기가 같아요.
    /// 좌표만 Unity 좌표(+X 동, +Y 위, +Z 북)로 바꿨어요.
    /// </summary>
    public class IndoorSkyPatches
    {
        public int SkyCount, Count, Res;
        public Vector3[] Dir;       // 패치 중심 방향
        public float[] Omega;       // 패치 입체각(sr)
        public Vector3[] SampDir;   // 하위 표본 방향(패치를 res×res로 나눔)
        public int[] SampPatch;     // 표본이 속한 패치
        public float[] SampOmega;

        public static IndoorSkyPatches Build(int res)
        {
            res = Mathf.Clamp(res, 1, 4);
            int[] counts = { 30, 30, 24, 24, 18, 12, 6 };
            const double D2R = Math.PI / 180.0;
            var dir = new List<Vector3>(); var om = new List<float>();
            var sd = new List<Vector3>(); var spch = new List<int>(); var so = new List<float>();
            for (int ground = 0; ground < 2; ground++)
            {
                double zs = ground == 0 ? 1 : -1;
                for (int b = 0; b <= counts.Length; b++)
                {
                    bool cap = b == counts.Length;
                    int n = cap ? 1 : counts[b];
                    double s0 = Math.Sin(12.0 * b * D2R), s1 = cap ? 1.0 : Math.Sin(12.0 * (b + 1) * D2R);
                    double dphi = 2 * Math.PI / n;
                    for (int j = 0; j < n; j++)
                    {
                        int pid = dir.Count;
                        double omega = dphi * (s1 - s0);
                        dir.Add(cap ? new Vector3(0, (float)zs, 0) : FromAz(j * dphi + dphi / 2, 0.5 * (s0 + s1), zs));
                        om.Add((float)omega);
                        for (int u = 0; u < res; u++)
                            for (int v = 0; v < res; v++)
                            {
                                sd.Add(FromAz(j * dphi + (u + 0.5) * dphi / res, s0 + (v + 0.5) * (s1 - s0) / res, zs));
                                spch.Add(pid);
                                so.Add((float)(omega / (res * res)));
                            }
                    }
                }
            }
            var p = new IndoorSkyPatches();
            p.Res = res; p.Count = dir.Count; p.SkyCount = dir.Count / 2;
            p.Dir = dir.ToArray(); p.Omega = om.ToArray();
            p.SampDir = sd.ToArray(); p.SampPatch = spch.ToArray(); p.SampOmega = so.ToArray();
            return p;
        }

        // phi: 북에서 시계방향(동 = 90°), sinAlt: 고도의 sin, zs: +1 하늘 / -1 지면
        static Vector3 FromAz(double phi, double sinAlt, double zs)
        {
            double ca = Math.Sqrt(Math.Max(0, 1 - sinAlt * sinAlt));
            return new Vector3((float)(Math.Sin(phi) * ca), (float)(zs * sinAlt), (float)(Math.Cos(phi) * ca));
        }
    }

    /// <summary>한 시각의 하늘·해 상태(조도 lx, 휘도 cd/m²)</summary>
    public class IndoorSkyState
    {
        public bool Lit, SunUp;
        public Vector3 Sun;          // 해를 향하는 단위벡터
        public float AltDeg;
        public float DnLux, DhLux;   // 직달 법선면 조도, 확산 수평면 조도
        public float ClearWeight;    // 0 = CIE 담천, 1 = CIE 청천
        public float[] L;            // 패치 휘도. 길이 = 패치 수 + 1 (마지막 칸 = 창 밖으로 보이는 주변 건물 면)
        public float BladeLum;       // 루버 날이 가린 부분의 밝기(근사)
        public string Note = "";

        public IndoorSkyState(int patchCount) { L = new float[patchCount + 1]; }
    }

    /// <summary>
    /// 실내 눈부심 계산식 모음(Grasshopper Glare2 Core를 옮긴 것 + 하늘 모델 + 루버 투과).
    /// 유니티 씬과 상관없는 순수 계산이에요.
    /// </summary>
    public static class IndoorGlareCore
    {
        public const float SrcLum = 2000f;     // 글레어 광원 휘도 기준(cd/m², evalglare 기본)
        public const float SunOmega = 6.8e-5f; // 해 원반 입체각(sr)
        const double D2R = Math.PI / 180.0, R2D = 180.0 / Math.PI;

        // ---------------- 하늘 모델 ----------------

        /// <summary>대기 밖 법선면 조도(lx). 1년 동안 ±3.4% 변해요</summary>
        public static float ExtraterrestrialLux(int doy)
        {
            return (float)(127500.0 * (1.0 + 0.034 * Math.Cos(2 * Math.PI * (doy - 2) / 365.0)));
        }

        /// <summary>대기질량(Kasten-Young)</summary>
        public static float AirMass(float altDeg)
        {
            double h = Math.Max(altDeg, 0.01);
            return (float)(1.0 / (Math.Sin(h * D2R) + 0.50572 * Math.Pow(h + 6.07995, -1.6364)));
        }

        /// <summary>
        /// IES(Gillette·Kusuda) 하늘 모델. 직달 E_dn = E_xt·exp(−c·m), 확산 수평 E_kh = A + B·sin^C(고도).
        ///  맑음: c = clearExtinction(기본 0.21), A 0.8 klx, B 15.5 klx, C 0.5
        ///  구름 조금: c 0.80, A 0.3, B 45.0, C 1.0
        ///  흐림: 직달 없음, A 0.3, B 21.0, C 1.0
        /// clearWeight = 휘도 분포를 CIE 청천과 담천 중 어느 쪽에 가깝게 할지(1 = 청천)
        /// </summary>
        public static void ModelSky(IndoorSkyModel m, float altDeg, int doy, float clearExtinction, out float dn, out float dh, out float clearWeight)
        {
            dn = 0f; dh = 0f; clearWeight = 0f;
            if (altDeg <= 0f) return;
            double s = Math.Sin(altDeg * D2R);
            double ext = ExtraterrestrialLux(doy), am = AirMass(altDeg);
            switch (m)
            {
                case IndoorSkyModel.PartlyCloudy:
                    dn = (float)(ext * Math.Exp(-0.80 * am));
                    dh = (float)(1000.0 * (0.3 + 45.0 * s));
                    clearWeight = 0.5f;
                    break;
                case IndoorSkyModel.Overcast:
                    dn = 0f;
                    dh = (float)(1000.0 * (0.3 + 21.0 * s));
                    clearWeight = 0f;
                    break;
                default:
                    dn = (float)(ext * Math.Exp(-clearExtinction * am));
                    dh = (float)(1000.0 * (0.8 + 15.5 * Math.Sqrt(s)));
                    clearWeight = 1f;
                    break;
            }
        }

        /// <summary>Perez 청명도 ε (EPW일 때 청천·담천 분포를 섞는 비율에 써요)</summary>
        public static float PerezEpsilon(float dhiW, float dniW, float zenithRad)
        {
            if (dhiW <= 0) return 999f;
            double z3 = Math.Pow(Math.Max(0, Math.Min(zenithRad, Math.PI / 2)), 3) * 1.041;
            return (float)(((dhiW + dniW) / dhiW + z3) / (1 + z3));
        }

        public static float ClearWeightFromEpsilon(float eps)
        {
            return Mathf.Clamp01((eps - 1f) / (6.2f - 1f));   // ε=1 담천 … ε≥6.2 청천
        }

        /// <summary>
        /// 하늘 패치 상대 휘도. Σ rel·sin(고도)·ω = 1이 되도록 맞춰서 L = 확산 수평 조도 × rel 이에요.
        /// CIE 담천과 CIE 청천을 clearWeight로 섞어요(Glare2와 같아요).
        /// </summary>
        public static void SkyRelative(IndoorSkyPatches sp, Vector3 sun, bool sunUp, float clearWeight, float[] rel)
        {
            int n = sp.SkyCount;
            double sOv = 0, sCl = 0;
            var ov = new double[n];
            var cl = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sg = Math.Max(1e-3, sp.Dir[i].y);
                ov[i] = (1 + 2 * sg) / 3.0;
                sOv += ov[i] * sg * sp.Omega[i];
                if (sunUp)
                {
                    double chi = Math.Acos(Math.Max(-1.0, Math.Min(1.0, Vector3.Dot(sp.Dir[i], sun))));
                    double cc = Math.Cos(chi);
                    cl[i] = (0.91 + 10 * Math.Exp(-3 * chi) + 0.45 * cc * cc) * (1 - Math.Exp(-0.32 / sg));
                    sCl += cl[i] * sg * sp.Omega[i];
                }
            }
            double w = sunUp ? Mathf.Clamp01(clearWeight) : 0;
            for (int i = 0; i < n; i++)
                rel[i] = (float)((1 - w) * ov[i] / sOv + (w > 0 ? w * cl[i] / sCl : 0));
        }

        /// <summary>
        /// s의 SunUp, Sun, DnLux, DhLux, ClearWeight를 채운 뒤 부르면 패치 휘도를 채워요.
        ///  하늘: L = 확산 수평 조도 × 상대 휘도
        ///  지면: 균일 확산 L = ρ_지면 · (확산 + 직달·sin고도) / π
        ///  날·주변 건물 면: ρ · 연직 확산 조도 / π (연직 확산 조도 ≈ ½하늘 + ½지면 반사)
        /// </summary>
        public static void FillSky(IndoorSkyState s, IndoorSkyPatches sp, float rhoGround, float rhoBlade, float rhoContext)
        {
            s.Lit = s.DhLux > 0f || s.DnLux > 0f;
            for (int k = 0; k < s.L.Length; k++) s.L[k] = 0f;
            s.BladeLum = 0f;
            if (!s.Lit) return;
            var rel = new float[sp.SkyCount];
            SkyRelative(sp, s.Sun, s.SunUp, s.ClearWeight, rel);
            for (int k = 0; k < sp.SkyCount; k++) s.L[k] = s.DhLux * rel[k];
            float sinAlt = s.SunUp ? Mathf.Max(0f, s.Sun.y) : 0f;
            float eh = s.DhLux + s.DnLux * sinAlt;                     // 수평 전천 조도
            float lg = rhoGround * eh / Mathf.PI;
            for (int k = sp.SkyCount; k < sp.Count; k++) s.L[k] = lg;
            float ev = 0.5f * s.DhLux + 0.5f * rhoGround * eh;          // 연직면 확산 조도(근사)
            s.BladeLum = rhoBlade * ev / Mathf.PI;
            s.L[sp.Count] = rhoContext * ev / Mathf.PI;
        }

        // ---------------- 위치지수 · DGP (Glare2와 같아요) ----------------

        /// <summary>시선 좌표계. up = 연직(+Y)을 시선에 수직으로 만든 것</summary>
        public static void Frame(Vector3 view, out Vector3 up, out Vector3 rt)
        {
            view = view.normalized;
            var y = new Vector3(0, 1, 0);
            up = y - view * Vector3.Dot(y, view);
            if (up.magnitude < 1e-6f) up = new Vector3(0, 0, 1);
            up = up.normalized;
            rt = Vector3.Cross(view, up).normalized;   // 좌우 부호는 쓰지 않아요(|x|만 써요)
        }

        /// <summary>evalglare get_posindex: 시선 위는 Guth, 아래는 Iwata(CIE 2010), 상한 16. 시야 밖이면 -1</summary>
        public static float PositionIndex(Vector3 view, Vector3 up, Vector3 rt, Vector3 d)
        {
            double z = Vector3.Dot(d, view);
            if (z <= 1e-9) return -1f;
            double x = Vector3.Dot(d, rt), y = Vector3.Dot(d, up);
            double sigma = Math.Acos(Math.Min(1.0, z)) * R2D;
            double tau = Math.Atan2(Math.Abs(x), y) * R2D;   // 0 = 바로 위, 90 = 옆, 180 = 바로 아래
            double P;
            if (y >= 0)
                P = Math.Exp((35.2 - 0.31889 * tau - 1.22 * Math.Exp(-2 * tau / 9)) / 1000 * sigma
                           + (21 + 0.26667 * tau - 0.002963 * tau * tau) / 100000 * sigma * sigma);
            else
            {
                double ct = Math.Cos(tau * D2R);
                double beta = Math.Atan(Math.Tan(sigma * D2R) * Math.Sqrt(1 + 0.3225 * ct * ct)) * R2D;
                P = Math.Exp(6.49 / 1000 * beta + 21.0 / 100000 * beta * beta);
            }
            return (float)(P > 16 ? 16 : P);
        }

        /// <summary>DGP = 5.87e-5·Ev + 0.092·log10(1 + Σ L²ω/(Ev^1.87·P²)) + 0.159 (evalglare 계수)</summary>
        public static float Dgp(double ev, double sum)
        {
            if (ev <= 0) return 0f;
            double d = 5.87e-5 * ev + 0.092 * Math.Log10(1 + sum / Math.Pow(ev, 1.87)) + 0.159;
            return (float)Math.Max(0, Math.Min(1, d));
        }

        public static string DgpClass(float dgp)
        {
            if (dgp < 0.35f) return "인지 불가";
            if (dgp < 0.40f) return "인지 가능";
            if (dgp < 0.45f) return "방해";
            return "견디기 어려움";
        }

        // ---------------- 루버 ----------------

        /// <summary>방향 d가 입면 side의 바깥 방향과 평면에서 이루는 각(라디안). 오른쪽(바깥에서 볼 때)이 +</summary>
        public static float PlanAngle(Vector3 d, IndoorSide side)
        {
            Vector3 n = IndoorFacade.NormalOf(side), r = IndoorFacade.RightOf(side);
            return Mathf.Atan2(Vector3.Dot(d, r), Vector3.Dot(d, n));
        }

        /// <summary>
        /// 수직 루버 날 배열을 평면 방향 phi로 통과하는 비율(0~1). 날이 위아래로 길다고 보고 평면만 봐요.
        /// 한 장이 유리 위에 드리우는 폭 = 폭·|sin(θ−φ)|/|cos φ|, 이것이 간격보다 크면 다 막혀요.
        /// 안에서 밖을 볼 때와 밖에서 해가 들어올 때가 같아요(LouverArray2D의 glassLit와 같은 값).
        /// thetaDeg: 날 각도(0 = 입면에 수직, + = 바깥 끝이 오른쪽)
        /// </summary>
        public static float LouverTransmission(float phi, float thetaDeg, float width, float spacing)
        {
            float c = Mathf.Abs(Mathf.Cos(phi));
            float sn = Mathf.Abs(Mathf.Sin(thetaDeg * Mathf.Deg2Rad - phi));
            if (c < 1e-5f) return sn < 1e-5f ? 1f : 0f;
            float blocked = width * sn / (Mathf.Max(spacing, 1e-4f) * c);
            return blocked >= 1f ? 0f : 1f - blocked;
        }

        // ---------------- 날짜 ----------------

        static readonly int[] Cum = { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 };

        public static int DayOfYear(int month, int dom)
        {
            return Cum[Mathf.Clamp(month, 1, 12) - 1] + dom;
        }

        /// <summary>대표일 이름(춘분·하지·추분·동지) 또는 "6/21"을 날짜로 바꿔요</summary>
        public static bool DayToDate(string day, out int month, out int dom)
        {
            month = 6; dom = 21;
            if (string.IsNullOrEmpty(day)) return false;
            switch (day.Trim())
            {
                case "춘분": month = 3; dom = 21; return true;
                case "하지": month = 6; dom = 21; return true;
                case "추분": month = 9; dom = 23; return true;
                case "동지": month = 12; dom = 21; return true;
            }
            var p = day.Trim().Split('/');
            int m, d;
            if (p.Length == 2 && int.TryParse(p[0], out m) && int.TryParse(p[1], out d) && m >= 1 && m <= 12 && d >= 1 && d <= 31)
            {
                month = m; dom = d;
                return true;
            }
            return false;
        }
    }

    /// <summary>EPW 기상 파일(Glare2와 같은 열을 읽어요)</summary>
    public class IndoorEpw
    {
        public string City = "";
        public float Lat, Lon, Tz;
        public int Count;
        public int[] Month, Day, Hour;
        public float[] DniW, DhiW, DniLux, DhiLux;

        public static IndoorEpw Parse(string text)
        {
            var ci = CultureInfo.InvariantCulture;
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (lines.Length < 9) throw new Exception("EPW 형식이 아니에요");
            var hd = lines[0].Split(',');
            var e = new IndoorEpw();
            e.City = hd.Length > 1 ? hd[1] : "";
            if (hd.Length > 8)
            {
                float.TryParse(hd[6], NumberStyles.Float, ci, out e.Lat);
                float.TryParse(hd[7], NumberStyles.Float, ci, out e.Lon);
                float.TryParse(hd[8], NumberStyles.Float, ci, out e.Tz);
            }
            var rows = new List<string[]>();
            for (int i = 8; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                var f = lines[i].Split(',');
                if (f.Length >= 19) rows.Add(f);
            }
            int n = rows.Count;
            e.Count = n;
            e.Month = new int[n]; e.Day = new int[n]; e.Hour = new int[n];
            e.DniW = new float[n]; e.DhiW = new float[n]; e.DniLux = new float[n]; e.DhiLux = new float[n];
            for (int i = 0; i < n; i++)
            {
                var f = rows[i];
                e.Month[i] = int.Parse(f[1], ci);
                e.Day[i] = int.Parse(f[2], ci);
                e.Hour[i] = int.Parse(f[3], ci);
                float dniW = float.Parse(f[14], ci), dhiW = float.Parse(f[15], ci);
                float dniL = float.Parse(f[17], ci), dhiL = float.Parse(f[18], ci);
                if (dniW >= 9999 || dniW < 0) dniW = 0;
                if (dhiW >= 9999 || dhiW < 0) dhiW = 0;
                // 조도 칸이 비어 있으면 발광효율로 환산(직달 93, 확산 120 lm/W) — Glare2와 같아요
                if (dniL >= 999900 || dniL < 0 || (dniL == 0 && dniW > 0)) dniL = dniW * 93f;
                if (dhiL >= 999900 || dhiL < 0 || (dhiL == 0 && dhiW > 0)) dhiL = dhiW * 120f;
                e.DniW[i] = dniW; e.DhiW[i] = dhiW; e.DniLux[i] = dniL; e.DhiLux[i] = dhiL;
            }
            return e;
        }

        /// <summary>날짜·지방표준시(소수)에 해당하는 행. EPW의 Hour h는 (h−1)~h시 구간이에요</summary>
        public int Row(int month, int dom, float hourLocal)
        {
            int doy = IndoorGlareCore.DayOfYear(month, dom);
            int row = (doy - 1) * 24 + Mathf.Clamp(Mathf.FloorToInt(hourLocal), 0, 23);
            return Mathf.Clamp(row, 0, Count - 1);
        }
    }
}
