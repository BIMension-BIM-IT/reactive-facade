using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>louver_angle_by_floor.csv 한 줄: 한 시각, 한 층의 입면별 각도</summary>
public class FloorScheduleRow
{
    public int floor;
    // 입면 순서: 0 동, 1 남, 2 서, 3 북 (SunLouverData와 같아요)
    public float[] angle = new float[4];   // 도, 그늘이면 0(열림)
    public float[] cont = new float[4];    // 연속각(정오에 반 바퀴 돌지 않게 이어 붙인 값)
    public float[] sun = new float[4];     // 해를 받는 비율 0~1
}

/// <summary>
/// 층별 루버 각도 표 (주변 건물 그림자 반영). 대표일 이름(춘분, 하지...) 또는 날짜(6/21)와
/// 하루 중 분으로 그 시각의 모든 층 행을 찾아요.
/// </summary>
public class FloorScheduleData
{
    public int maxFloor;
    // 대표일/날짜 -> 분 -> 층 번호로 찾는 행 배열(0번 칸은 비어 있어요)
    readonly Dictionary<string, SortedDictionary<int, FloorScheduleRow[]>> table =
        new Dictionary<string, SortedDictionary<int, FloorScheduleRow[]>>();

    static float F(string s)
    {
        return float.Parse(s.Trim(), CultureInfo.InvariantCulture);
    }

    static int ToMinutes(string hhmm)
    {
        var p = hhmm.Trim().Split(':');
        return int.Parse(p[0]) * 60 + int.Parse(p[1]);
    }

    public static FloorScheduleData LoadFromText(string text)
    {
        var data = new FloorScheduleData();
        if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (lines.Length < 2) return data;

        var header = lines[0].Split(',');
        int Col(string name)
        {
            int i = Array.IndexOf(header, name);
            if (i < 0) throw new Exception("층별 CSV에 열이 없어요: " + name);
            return i;
        }

        int cDay = Col("day"), cDate = Col("date"), cTime = Col("time"), cFloor = Col("floor");
        string[] sides = { "E", "S", "W", "N" };
        var cAng = new int[4];
        var cCont = new int[4];
        var cSun = new int[4];
        for (int k = 0; k < 4; k++)
        {
            cAng[k] = Col(sides[k] + "_angle");
            cCont[k] = Col(sides[k] + "_cont");
            cSun[k] = Col(sides[k] + "_sun");
        }

        // 먼저 (대표일, 분) 별로 층 행을 모은 뒤 배열로 바꿔요
        var tmp = new Dictionary<string, SortedDictionary<int, List<FloorScheduleRow>>>();
        for (int li = 1; li < lines.Length; li++)
        {
            if (string.IsNullOrWhiteSpace(lines[li])) continue;
            var c = lines[li].Split(',');
            var r = new FloorScheduleRow();
            r.floor = int.Parse(c[cFloor].Trim());
            for (int k = 0; k < 4; k++)
            {
                r.angle[k] = F(c[cAng[k]]);
                r.cont[k] = F(c[cCont[k]]);
                r.sun[k] = F(c[cSun[k]]);
            }
            if (r.floor > data.maxFloor) data.maxFloor = r.floor;
            int minutes = ToMinutes(c[cTime]);
            foreach (var key in new[] { c[cDay].Trim(), c[cDate].Trim() })
            {
                SortedDictionary<int, List<FloorScheduleRow>> byTime;
                if (!tmp.TryGetValue(key, out byTime))
                {
                    byTime = new SortedDictionary<int, List<FloorScheduleRow>>();
                    tmp[key] = byTime;
                }
                List<FloorScheduleRow> list;
                if (!byTime.TryGetValue(minutes, out list))
                {
                    list = new List<FloorScheduleRow>();
                    byTime[minutes] = list;
                }
                list.Add(r);
            }
        }
        foreach (var kv in tmp)
        {
            var byTime = new SortedDictionary<int, FloorScheduleRow[]>();
            foreach (var kt in kv.Value)
            {
                var arr = new FloorScheduleRow[data.maxFloor + 1];
                foreach (var r in kt.Value) arr[r.floor] = r;
                byTime[kt.Key] = arr;
            }
            data.table[kv.Key] = byTime;
        }
        return data;
    }

    /// <summary>대표일(이름 또는 날짜)과 하루 중 분으로 가장 가까운 시각(10분 안)의 층 배열을 찾아요. 없으면 null.</summary>
    public FloorScheduleRow[] Find(string day, int minutes)
    {
        SortedDictionary<int, FloorScheduleRow[]> byTime;
        if (!table.TryGetValue(day, out byTime)) return null;
        FloorScheduleRow[] best = null;
        int bestDiff = int.MaxValue;
        foreach (var kv in byTime)
        {
            int d = Mathf.Abs(kv.Key - minutes);
            if (d < bestDiff) { bestDiff = d; best = kv.Value; }
        }
        return (best != null && bestDiff <= 10) ? best : null;
    }
}
