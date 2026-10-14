using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>sun_louver.csv 한 행 (Rhino 좌표를 Unity 좌표로 바꿔서 보관)</summary>
public class SunLouverRow
{
    public string day;        // 대표일 이름 또는 날짜 글자
    public string date;       // 예: 6/21
    public float hoy;
    public int minutes;       // 하루 중 분 (예: 12:30 -> 750)
    public float altitude;    // 도
    public float azimuth;     // 도, 정북 0 시계방향
    public Vector3 sunDir;    // 태양을 향하는 단위벡터 (Unity 좌표)
    public float cutoff;
    // 입면 순서: 0 동, 1 남, 2 서, 3 북
    public bool[] closed = new bool[4];
    public float[] angle = new float[4]; // 도, 부호 있음 (양수 = 바깥쪽 끝이 입면을 바깥에서 볼 때 오른쪽)
    // 연속각: 날은 180도 대칭이라 -90도와 +90도가 같은 모양이에요. 정오에 부호가 바뀌어도 눕힌 채로 이어지게
    // 하루 동안 이어 붙인 값이에요. CSV에 열이 없으면 angle과 같아요.
    public float[] contAngle = new float[4];
}

public class SunLouverData
{
    public List<SunLouverRow> rows = new List<SunLouverRow>();

    static float F(string s)
    {
        return float.Parse(s.Trim(), CultureInfo.InvariantCulture);
    }

    static int ToMinutes(string hhmm)
    {
        var p = hhmm.Trim().Split(':');
        return int.Parse(p[0]) * 60 + int.Parse(p[1]);
    }

    public static SunLouverData Load(string path)
    {
        return LoadFromText(File.ReadAllText(path)); // UTF-8(BOM)도 자동 처리
    }

    /// <summary>CSV 글자(문자열)를 바로 읽어요. 웹(WebGL)에서도 돼요.</summary>
    public static SunLouverData LoadFromText(string text)
    {
        var data = new SunLouverData();
        if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (lines.Length < 2) return data;

        var header = lines[0].Split(',');
        int Col(string name)
        {
            int i = Array.IndexOf(header, name);
            if (i < 0) throw new Exception("CSV에 열이 없어요: " + name);
            return i;
        }

        int cDay = Col("대표일"), cDate = Col("날짜"), cHoy = Col("hoy"), cTime = Col("시각");
        int cAlt = Col("고도"), cAz = Col("방위");
        int cVx = Col("벡터X_동"), cVy = Col("벡터Y_북"), cVz = Col("벡터Z_위");
        int cCut = Col("컷오프");
        string[] sideNames = { "동면", "남면", "서면", "북면" };
        var cState = new int[4];
        var cAngle = new int[4];
        var cCont = new int[4];
        bool hasCont = true;
        for (int k = 0; k < 4; k++)
        {
            cState[k] = Col(sideNames[k]);
            cAngle[k] = Col(sideNames[k] + "_각도");
            cCont[k] = Array.IndexOf(header, sideNames[k] + "_연속각");
            if (cCont[k] < 0) hasCont = false;
        }

        for (int li = 1; li < lines.Length; li++)
        {
            if (string.IsNullOrWhiteSpace(lines[li])) continue;
            var c = lines[li].Split(',');
            var r = new SunLouverRow();
            r.day = c[cDay].Trim();
            r.date = c[cDate].Trim();
            r.hoy = F(c[cHoy]);
            r.minutes = ToMinutes(c[cTime]);
            r.altitude = F(c[cAlt]);
            r.azimuth = F(c[cAz]);
            // Rhino(X=동, Y=북, Z=위) -> Unity(X=동, Y=위, Z=북)
            var v = new Vector3(F(c[cVx]), F(c[cVz]), F(c[cVy]));
            r.sunDir = v.normalized;
            r.cutoff = F(c[cCut]);
            for (int k = 0; k < 4; k++)
            {
                r.closed[k] = c[cState[k]].Trim() == "닫힘";
                r.angle[k] = F(c[cAngle[k]]);
                r.contAngle[k] = hasCont ? F(c[cCont[k]]) : r.angle[k];
            }
            data.rows.Add(r);
        }
        return data;
    }

    /// <summary>대표일(이름 또는 날짜)과 하루 중 분으로 가장 가까운 행을 찾아요. 10분 넘게 차이나면 null(해가 없는 시각).</summary>
    public SunLouverRow Find(string day, int minutes)
    {
        SunLouverRow best = null;
        int bestDiff = int.MaxValue;
        foreach (var r in rows)
        {
            if (r.day != day && r.date != day) continue;
            int d = Mathf.Abs(r.minutes - minutes);
            if (d < bestDiff) { bestDiff = d; best = r; }
        }
        return (best != null && bestDiff <= 10) ? best : null;
    }
}
