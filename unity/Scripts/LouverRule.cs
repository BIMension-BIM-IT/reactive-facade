using System;

/// <summary>
/// 반응형 루버 규칙(유니티에 의존하지 않는 순수 수학). 1784 검증에 쓴 규칙을 그대로 옮겼어요.
///
/// 방위 약속: 북 0°, 동 90°, 남 180°, 서 270°. 입면 방위 = 입면 바깥쪽 방향의 방위.
/// 날 각도: 0° = 열림(날이 유리에 수직), + = 날의 바깥쪽 끝이 입면을 바깥에서 볼 때 오른쪽으로.
/// 수직 날은 180° 대칭이라 +85°와 -85°는 같은 자세예요(애니메이션에서 한 바퀴 안 돌게 Unwrap180을 써요).
/// </summary>
public static class LouverRule
{
    public const double MinSunAltitude = 5.0;      // 해 고도가 이보다 낮으면 열림

    public static double Wrap180(double d)
    {
        return ((d + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
    }

    /// <summary>
    /// 날짜별 컷오프: 대표일(춘분 3/21, 하지 6/21, 추분 9/23, 동지 12/21) 값 사이를 직선으로 이어요.
    /// cutoffs = LouverSpec.cutoff_deg = {춘분, 하지, 추분, 동지} (기본 30, 60, 45, 30)
    /// </summary>
    public static double CutoffForDate(double[] cutoffs, int month, int day)
    {
        int[] cum = { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 };
        int n = cum[month - 1] + day;
        int[] anchor = { 80, 172, 266, 355, 80 + 365 };            // 3/21, 6/21, 9/23, 12/21, 다음 해 3/21
        double[] val = { cutoffs[0], cutoffs[1], cutoffs[2], cutoffs[3], cutoffs[0] };
        if (n < anchor[0]) n += 365;                               // 1~3월 초는 동지 -> 춘분 구간
        for (int i = 0; i < 4; i++)
        {
            if (n >= anchor[i] && n <= anchor[i + 1])
            {
                double t = (n - anchor[i]) / (double)(anchor[i + 1] - anchor[i]);
                return val[i] + (val[i + 1] - val[i]) * t;
            }
        }
        return cutoffs[3];
    }

    /// <summary>북쪽(±45°) 입면은 해가 거의 안 들어서 루버를 닫지 않아요(웹의 "북쪽 ±45° 입면은 루버 없음"과 같아요).</summary>
    public static bool IsNorthFacing(double faceAzimuthDeg)
    {
        return Math.Abs(Wrap180(faceAzimuthDeg - 0.0)) <= 45.0;
    }

    /// <summary>
    /// 수직 루버 규칙. 닫으면 true와 각도(°)를 돌려주고, 열림이면 false와 0.
    /// 닫는 조건: 해 고도 5° 이상, 해와 입면의 방위 차이 |θ| ≤ 컷오프, 북쪽 입면이 아님.
    /// 각도: a = max(0, asin(min(1, s·cosθ / w)) - |θ|),  부호는 θ의 부호.
    /// bladeDepthMm = 날 폭 w, spacingMm = 날 간격 s.
    /// </summary>
    public static bool VerticalAngle(double sunAltitudeDeg, double sunAzimuthDeg, double faceAzimuthDeg,
                                     double cutoffDeg, double bladeDepthMm, double spacingMm, out double angleDeg)
    {
        angleDeg = 0.0;
        if (IsNorthFacing(faceAzimuthDeg)) return false;
        if (sunAltitudeDeg < MinSunAltitude) return false;
        double th = Wrap180(sunAzimuthDeg - faceAzimuthDeg);
        if (Math.Abs(th) > cutoffDeg) return false;
        double arg = spacingMm * Math.Cos(th * Math.PI / 180.0) / bladeDepthMm;
        double a = Math.Asin(Math.Min(1.0, arg)) * 180.0 / Math.PI - Math.Abs(th);
        a = Math.Max(0.0, a);
        angleDeg = th >= 0 ? a : -a;
        return true;
    }

    // ---- 겨울 모드(혼합 규칙 C): 난방이 필요한 날에 쓴다. 해와 나란히 눕혀 겨울 햇빛을 받고, 눈부심 시각만 0°로 둔다 ----
    public const double WinterGlareAltitude = 20.0;   // 이보다 해가 낮고
    public const double WinterGlareAzDiff = 60.0;     // 해와 면의 방위차가 이 안이면(정면에 가까운 낮은 해) 0°
    public const double WinterCapAzDiff = 75.0;       // 방위차가 이보다 크면(해가 면과 거의 나란히) 0°

    /// <summary>겨울 모드 수직 루버 각도(°). 해와 나란히 = -θ, 눈부심 시각은 0°. 북쪽 입면은 항상 0°.</summary>
    public static double WinterVertical(double sunAltitudeDeg, double sunAzimuthDeg, double faceAzimuthDeg)
    {
        if (IsNorthFacing(faceAzimuthDeg) || sunAltitudeDeg <= 0.0) return 0.0;
        double th = Wrap180(sunAzimuthDeg - faceAzimuthDeg);
        if (Math.Abs(th) > WinterCapAzDiff) return 0.0;
        if (sunAltitudeDeg < WinterGlareAltitude && Math.Abs(th) <= WinterGlareAzDiff) return 0.0;
        return -th;
    }

    /// <summary>겨울 모드 수평 루버 기울기(°, 바깥 끝이 아래로 +). 해와 나란히 = 바깥 끝을 프로파일각만큼 위로(-β). Radiance로 검증했어요(난방기 11~3월 일사 손실: 남 10%, 동 24%, 서 25%).</summary>
    public static double WinterHorizontal(double sunAltitudeDeg, double sunAzimuthDeg, double faceAzimuthDeg)
    {
        if (IsNorthFacing(faceAzimuthDeg) || sunAltitudeDeg <= 0.0) return 0.0;
        double th = Wrap180(sunAzimuthDeg - faceAzimuthDeg);
        if (Math.Abs(th) > WinterCapAzDiff) return 0.0;
        if (sunAltitudeDeg < WinterGlareAltitude && Math.Abs(th) <= WinterGlareAzDiff) return 0.0;
        double beta = Math.Atan2(Math.Tan(sunAltitudeDeg * Math.PI / 180.0), Math.Cos(th * Math.PI / 180.0)) * 180.0 / Math.PI;
        return -Math.Min(90.0, beta);
    }

    /// <summary>
    /// 직각으로 꺾인 건물(직사각형, ㄱ·ㄷ자)의 회전각. 입면 방위들이 (회전각 + 0/90/180/270°)에 가장 잘 맞는 회전각(-45~45°)을
    /// 폭 가중 원평균으로 구해요. maxDevDeg = 그 격자에서 가장 많이 벗어난 입면의 어긋남(°, 클수록 직각이 아닌 입면이 있다는 뜻).
    /// </summary>
    public static double GridYawDeg(double[] azimuthDeg, double[] weight, out double maxDevDeg)
    {
        double sx = 0, sy = 0;
        for (int i = 0; i < azimuthDeg.Length; i++)
        {
            double w = (weight != null && i < weight.Length && weight[i] > 0) ? weight[i] : 1.0;
            double r = 4.0 * azimuthDeg[i] * Math.PI / 180.0;     // 90° 주기를 한 바퀴로 펴요
            sx += w * Math.Cos(r); sy += w * Math.Sin(r);
        }
        double yaw = (sx == 0 && sy == 0) ? 0.0 : Math.Atan2(sy, sx) * 180.0 / Math.PI / 4.0;   // -45 ~ 45
        maxDevDeg = 0.0;
        for (int i = 0; i < azimuthDeg.Length; i++)
        {
            double d = ((azimuthDeg[i] - yaw) % 90.0 + 135.0) % 90.0 - 45.0;
            maxDevDeg = Math.Max(maxDevDeg, Math.Abs(d));
        }
        return yaw;
    }

    /// <summary>
    /// 같은 자세(±180°)가 되는 값 중에서 직전 각도와 가장 가까운 값을 골라요. 정오에 날이 반 바퀴 돌지 않게 해 줘요.
    /// </summary>
    public static double Unwrap180(double previous, double current)
    {
        double d = ((current - previous) % 180.0 + 270.0) % 180.0 - 90.0;
        return previous + d;
    }

    /// <summary>
    /// 수평 루버 규칙(제안 컷오프 15°). 반환: 기울이면 true와 각도(°, 바깥쪽 끝이 아래로 +), 열림이면 false와 0.
    /// 프로파일각 β = atan(tan(고도) / cos(방위차)),  기울기 = asin(s·cosβ / w) - β (0~90°로 제한).
    /// </summary>
    public static bool HorizontalTilt(double sunAltitudeDeg, double sunAzimuthDeg, double faceAzimuthDeg,
                                      double cutoffProfileDeg, double bladeDepthMm, double spacingMm, out double tiltDeg)
    {
        tiltDeg = 0.0;
        if (IsNorthFacing(faceAzimuthDeg)) return false;
        if (sunAltitudeDeg < MinSunAltitude) return false;
        double th = Wrap180(sunAzimuthDeg - faceAzimuthDeg);
        if (Math.Abs(th) >= 90.0) return false;
        double beta = Math.Atan2(Math.Tan(sunAltitudeDeg * Math.PI / 180.0), Math.Cos(th * Math.PI / 180.0)) * 180.0 / Math.PI;
        if (beta < cutoffProfileDeg) return false;
        double arg = spacingMm * Math.Cos(beta * Math.PI / 180.0) / bladeDepthMm;
        double t = arg >= 1.0 ? 90.0 : Math.Asin(arg) * 180.0 / Math.PI - beta;
        tiltDeg = Math.Max(0.0, Math.Min(90.0, t));
        return tiltDeg > 0.5;
    }
}
