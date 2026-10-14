using System.Globalization;

/// <summary>
/// 겨울 모드 날짜표(winter_calendar.csv): 날짜마다 "난방이 필요한 겨울인가"를 알려 줘요.
/// 성남 날씨 기준: 7일 평균 외기온도가 12°C 미만이면 겨울, 14°C를 넘으면 겨울 끝, 그 사이는 이전 상태 유지
/// (대략 10/30 ~ 4/17). 파일이 없으면 11~3월을 겨울로 봐요.
/// </summary>
public class WinterCalendar
{
    static readonly int[] Cum = { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334 };
    readonly bool[] flags = new bool[366];
    bool loaded;

    public static WinterCalendar LoadFromText(string text)
    {
        var c = new WinterCalendar();
        var lines = text.Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            var p = lines[i].Trim().Split(',');
            if (p.Length < 3) continue;
            int m, d, w;
            if (!int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out m)) continue;
            if (!int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out d)) continue;
            if (!int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out w)) continue;
            int idx = Cum[m - 1] + d;
            if (idx >= 1 && idx <= 365) c.flags[idx] = w != 0;
            c.loaded = true;
        }
        return c;
    }

    public bool IsWinter(int month, int day)
    {
        if (!loaded) return month >= 11 || month <= 3;
        int idx = Cum[month - 1] + day;
        if (idx < 1 || idx > 365) return false;
        return flags[idx];
    }
}
