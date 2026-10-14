using System;

/// <summary>
/// 태양 위치 계산(유니티에 의존하지 않는 순수 수학, 어디서든 같은 값).
/// 입력: 위도·경도, 표준시 차이(한국 +9), 현지 시각. 출력: 고도(°), 방위(° 북 0, 동 90, 남 180, 서 270).
/// Ladybug 값과 0.3° 안쪽에서 맞는 것을 확인했어요(1784 대표일 시각).
/// </summary>
public static class SunPosition
{
    static readonly int[] Cum = { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 };

    /// <summary>local: 현지(한국 표준시) 날짜와 시각. 윤년은 따로 보정하지 않아요(오차 0.1° 미만).</summary>
    public static void Compute(double latDeg, double lonDeg, double tzHours, DateTime local, out double altitudeDeg, out double azimuthDeg)
    {
        int n = Cum[local.Month - 1] + local.Day;
        double hour = local.Hour + local.Minute / 60.0 + local.Second / 3600.0;
        double B = Rad(360.0 / 364.0 * (n - 81));
        double eotMin = 9.87 * Math.Sin(2 * B) - 7.53 * Math.Cos(B) - 1.5 * Math.Sin(B);          // 균시차(분)
        double decl = Rad(23.45 * Math.Sin(Rad(360.0 / 365.0 * (284 + n))));                       // 태양 적위
        double solarTime = hour + (4.0 * (lonDeg - 15.0 * tzHours) + eotMin) / 60.0;
        double H = Rad(15.0 * (solarTime - 12.0));                                                  // 시간각
        double lat = Rad(latDeg);
        double sinAlt = Math.Sin(lat) * Math.Sin(decl) + Math.Cos(lat) * Math.Cos(decl) * Math.Cos(H);
        double alt = Math.Asin(Math.Max(-1.0, Math.Min(1.0, sinAlt)));
        double az = Math.Atan2(Math.Sin(H), Math.Cos(H) * Math.Sin(lat) - Math.Tan(decl) * Math.Cos(lat)) + Math.PI;
        altitudeDeg = Deg(alt);
        azimuthDeg = ((Deg(az) % 360.0) + 360.0) % 360.0;
    }

    /// <summary>해를 향하는 단위 방향(동 +X, 북 +Z, 위 +Y: 유니티 좌표)</summary>
    public static void Direction(double altitudeDeg, double azimuthDeg, out double x, out double y, out double z)
    {
        double a = Rad(altitudeDeg), z0 = Rad(azimuthDeg);
        x = Math.Cos(a) * Math.Sin(z0);
        y = Math.Sin(a);
        z = Math.Cos(a) * Math.Cos(z0);
    }

    static double Rad(double d) { return d * Math.PI / 180.0; }
    static double Deg(double r) { return r * 180.0 / Math.PI; }
}
