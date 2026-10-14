using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 반응형 루버 쪽(SunLouverController, LouverRig)과 반사광 쪽을 잇는 유일한 파일이에요.
///
/// 루버 스크립트를 코드로 직접 부르지 않아요(컴파일 의존 없음). 이름으로 컴포넌트를 찾고 씬 상태를 읽어요.
/// 그래서 루버 스크립트가 업데이트되어도 반사광 스크립트는 다시 받을 필요가 없어요.
///  - 날 각도: 루버 날(louverRoot의 자식) 회전을 직접 읽어요. 각도 규칙이 바뀌어도 화면에 보이는 그대로 계산해요.
///  - 해: 컨트롤러의 sun(Directional Light) 방향. 빛이 꺼져 있으면 해 없음.
///  - 하루 누적: 컨트롤러의 hour를 10분씩 바꿔 가며 루버·해가 실제로 돌아간 상태를 읽어요(끝나면 원래 시각으로 돌려놔요).
///
/// 루버 쪽에서 아래 이름(클래스·필드)이 바뀌면 이 파일 맨 위 상수만 고치면 돼요.
/// 못 찾으면 컴파일 에러 대신 Console에 경고가 떠요.
/// </summary>
public class LouverSceneLink
{
    // ---- 루버 스크립트에서 쓰는 이름 (바뀌면 여기만 고쳐요) ----
    public const string ControllerTypeName = "SunLouverController";
    public const string RigTypeName = "LouverRig";
    const string FDay = "day";                    // string  대표일(하지 등)
    const string FHour = "hour";                  // float   시각(시)
    const string FExternal = "externalControl";   // bool    켜면 자동 재생·슬라이더를 무시
    const string FSun = "sun";                    // Light   해
    const string FRig = "rig";                    // LouverRig
    const string FRoot = "louverRoot";            // Transform 날들의 부모
    const string FSkip = "skipNameContains";      // string[] 돌리지 않는 것
    const string FReady = "IsReady";              // (있으면) CSV를 다 읽었는지
    const string FData = "data";                  // (있으면) CSV 데이터, null이면 아직 못 읽음
    const string FUserHidden = "userHidden";      // (있으면) LouverRig의 bool[4] 입면별로 사용자가 숨겼는지(북면 루버 끔 등)
    // 하루 누적을 다시 계산할지 판단할 때 보는 값(없는 이름은 그냥 건너뛰어요)
    static readonly string[] SignatureFields = { "day", "winterMode", "northLouver", "useFloorSchedule", "useContinuousAngle", "csvFileName", "floorCsvFileName" };

    /// <summary>날 한 장(louverRoot의 직계 자식 하나)</summary>
    public class Unit
    {
        public Transform t;
        public Quaternion q0;   // 시작할 때 회전
        public Vector3 b0;      // 시작할 때 날 방향(수평)
        public Vector3 n, r;    // 입면 바깥 방향, 바깥에서 볼 때 오른쪽
        public Vector3 center;
        public float height;
        public GlareSide side;
        public bool glass = true;
        public float angle;     // ReadAngles로 읽은 지금 각도(도, 0 = 열림)
        public bool active = true;
    }

    public MonoBehaviour controller, rig;
    public Light sun;
    public Transform root;
    public readonly List<Unit> units = new List<Unit>();
    public Bounds envelope;
    public readonly List<string> warnings = new List<string>();

    bool driving, savedExternal;
    float savedHour;

    // ---------------- 찾기 ----------------

    public void Setup(MonoBehaviour controllerOverride, MonoBehaviour rigOverride, Transform rootOverride, Light sunOverride,
                      string[] skipNames, string[] noGlassNames)
    {
        warnings.Clear();
        controller = controllerOverride != null ? controllerOverride : FindByTypeName(ControllerTypeName);
        if (controller == null) warnings.Add(ControllerTypeName + "를 못 찾았어요(하루 누적과 시각 표시가 안 돼요)");

        rig = rigOverride;
        if (rig == null) { MonoBehaviour r; if (TryGet(controller, FRig, out r)) rig = r; }
        if (rig == null) rig = FindByTypeName(RigTypeName);

        root = rootOverride;
        if (root == null && rig != null) { Transform t; if (TryGet(rig, FRoot, out t)) root = t; }
        if (root == null && rig != null) root = rig.transform;
        if (root == null) warnings.Add("루버 날의 부모(louverRoot)를 못 찾았어요. Louver Root Override에 넣어 주세요");

        sun = sunOverride;
        if (sun == null) { Light l; if (TryGet(controller, FSun, out l)) sun = l; }
        if (sun == null) sun = RenderSettings.sun;
        if (sun == null)
            foreach (var l in Object.FindObjectsOfType<Light>())
                if (l.type == LightType.Directional) { sun = l; break; }
        if (sun == null) warnings.Add("해(Directional Light)를 못 찾았어요. Sun Override에 넣어 주세요");

        var skip = new List<string>();
        if (skipNames != null) skip.AddRange(skipNames);
        string[] rigSkip;
        if (TryGet(rig, FSkip, out rigSkip) && rigSkip != null) skip.AddRange(rigSkip);
        BuildUnits(skip, noGlassNames);

        if (rig != null && HiddenSides() == null)
            warnings.Add(RigTypeName + "." + FUserHidden + "를 못 찾았어요. 꺼진 날은 모두 없다고 봐요. " +
                         "LouverPerformance가 카메라 반대쪽 면을 끄면 그 면을 유리만 있는 것으로 계산하니 Cull Back Faces를 꺼 주세요");
    }

    static MonoBehaviour FindByTypeName(string typeName)
    {
        foreach (var mb in Object.FindObjectsOfType<MonoBehaviour>())
            if (mb != null && mb.GetType().Name == typeName) return mb;
        return null;
    }

    static bool ContainsAny(string name, IList<string> keys)
    {
        if (keys == null) return false;
        foreach (var k in keys)
            if (!string.IsNullOrEmpty(k) && name.Contains(k)) return true;
        return false;
    }

    void BuildUnits(List<string> skip, string[] noGlass)
    {
        units.Clear();
        if (root == null) return;

        var ts = new List<Transform>();
        var bs = new List<Bounds>();
        bool has = false;
        foreach (Transform child in root)
        {
            if (ContainsAny(child.name, skip)) continue;
            var rs = child.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            ts.Add(child);
            bs.Add(b);
            if (!has) { envelope = b; has = true; } else envelope.Encapsulate(b);
        }

        for (int i = 0; i < ts.Count; i++)
        {
            var b = bs[i];
            GlareSide side;
            string nm = ts[i].name.StartsWith("Pivot_") ? ts[i].name.Substring(6) : ts[i].name;
            if (!TryParseSide(nm, out side)) side = Classify(b.center, envelope);
            var n = GlareOptics.NormalOf(side);
            var r = GlareOptics.RightOf(side);
            // 시작할 때 날 방향: 수평 범위가 더 긴 쪽(열림이면 입면에 수직 = n, 닫혀 있으면 r)
            float extN = Mathf.Abs(Vector3.Dot(b.size, n)), extR = Mathf.Abs(Vector3.Dot(b.size, r));
            units.Add(new Unit
            {
                t = ts[i],
                q0 = ts[i].rotation,
                b0 = extN >= extR ? n : r,
                n = n,
                r = r,
                center = b.center,
                height = b.size.y,
                side = side,
                glass = !ContainsAny(ts[i].name, noGlass),
            });
        }
    }

    /// <summary>이름이 "층_입면_번호"(5_S_001, 5_남_001 등)이면 입면을 읽어요(LouverRig와 같은 규칙)</summary>
    static bool TryParseSide(string n, out GlareSide side)
    {
        side = GlareSide.South;
        var t = n.Split('_');
        int floor;
        if (t.Length < 2 || !int.TryParse(t[0].Trim(), out floor)) return false;
        switch (t[1].Trim().ToUpperInvariant())
        {
            case "E": case "동": case "동면": side = GlareSide.East; return true;
            case "S": case "남": case "남면": side = GlareSide.South; return true;
            case "W": case "서": case "서면": side = GlareSide.West; return true;
            case "N": case "북": case "북면": side = GlareSide.North; return true;
        }
        return false;
    }

    /// <summary>모든 날을 감싸는 범위에서 가장 가까운 면을 그 날의 입면으로 정해요(직사각형 건물 기준)</summary>
    static GlareSide Classify(Vector3 c, Bounds all)
    {
        float dE = all.max.x - c.x, dW = c.x - all.min.x, dN = all.max.z - c.z, dS = c.z - all.min.z;
        float m = Mathf.Min(Mathf.Min(dE, dW), Mathf.Min(dN, dS));
        if (m == dE) return GlareSide.East;
        if (m == dW) return GlareSide.West;
        if (m == dN) return GlareSide.North;
        return GlareSide.South;
    }

    // ---------------- 읽기 ----------------

    /// <summary>날마다 지금 각도와 날이 있는지를 읽어요.</summary>
    public void ReadAngles()
    {
        var hidden = HiddenSides();
        bool rootOn = root != null && root.gameObject.activeInHierarchy;
        foreach (var u in units)
        {
            u.active = Present(u, hidden, rootOn);
            if (u.t == null) continue;
            Vector3 b = (u.t.rotation * Quaternion.Inverse(u.q0)) * u.b0;
            u.angle = Mathf.Atan2(Vector3.Dot(b, u.r), Vector3.Dot(b, u.n)) * Mathf.Rad2Deg;
        }
    }

    /// <summary>LouverRig의 입면별 "사용자가 숨김"(동 남 서 북). 없으면 null</summary>
    bool[] HiddenSides()
    {
        bool[] h;
        return TryGet(rig, FUserHidden, out h) && h != null && h.Length >= 4 ? h : null;
    }

    /// <summary>
    /// 날이 있다고 볼지 정해요. 사용자가 숨긴 면(북면 루버 끔 등)만 없다고 봐요.
    /// LouverPerformance가 화면을 가볍게 하려고 카메라 반대쪽 면의 날을 꺼도, 실제로는 날이 있으니 있다고 봐요
    /// (꺼진 날도 컨트롤러가 계속 돌려서 각도는 맞아요). userHidden을 못 찾으면 켜져 있는지로 판단해요.
    /// </summary>
    static bool Present(Unit u, bool[] hidden, bool rootOn)
    {
        if (u.t == null) return false;
        if (hidden != null) return rootOn && !hidden[(int)u.side];
        return u.t.gameObject.activeInHierarchy;
    }

    /// <summary>해가 떠 있으면 해를 향하는 방향을 줘요.</summary>
    public bool SunUp(out Vector3 sunDir)
    {
        sunDir = Vector3.up;
        if (sun == null || !sun.enabled || !sun.gameObject.activeInHierarchy) return false;
        sunDir = -sun.transform.forward;
        return sunDir.y > 0.001f;
    }

    /// <summary>태양 궤적 한 줄(대표일 하나의 해 방향, 시각 순서)</summary>
    public class SunPathDay
    {
        public string day;
        public readonly List<int> minutes = new List<int>();      // 하루 중 분
        public readonly List<Vector3> dirs = new List<Vector3>();  // 해를 향하는 방향(Unity 좌표)
    }

    /// <summary>
    /// 컨트롤러가 읽은 sun_louver.csv(data.rows)에서 대표일마다 해 방향을 읽어요.
    /// 화면의 해(Directional Light)와 같은 데이터라서 궤적과 해가 어긋나지 않아요. 못 읽으면 false.
    /// </summary>
    public bool TryGetSunPaths(List<SunPathDay> days)
    {
        days.Clear();
        object data;
        System.Collections.IEnumerable rows;
        if (!TryGet(controller, FData, out data) || !TryGet(data, "rows", out rows)) return false;
        var byDay = new Dictionary<string, SunPathDay>();
        var tmp = new Dictionary<string, List<KeyValuePair<int, Vector3>>>();
        foreach (var r in rows)
        {
            string d;
            int m;
            Vector3 v;
            if (!TryGet(r, FDay, out d) || !TryGet(r, "minutes", out m) || !TryGet(r, "sunDir", out v)) continue;
            if (!byDay.ContainsKey(d))
            {
                byDay[d] = new SunPathDay { day = d };
                days.Add(byDay[d]);                       // CSV에 나온 순서(춘분·하지·추분·동지)
                tmp[d] = new List<KeyValuePair<int, Vector3>>();
            }
            tmp[d].Add(new KeyValuePair<int, Vector3>(m, v.normalized));
        }
        foreach (var day in days)
        {
            var list = tmp[day.day];
            list.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (var kv in list) { day.minutes.Add(kv.Key); day.dirs.Add(kv.Value); }
        }
        return days.Count > 0;
    }

    /// <summary>해·날 상태가 바뀌었는지 보는 값(날은 stride장마다 하나씩만 봐요)</summary>
    public long StateSignature(int stride)
    {
        unchecked
        {
            long h = 17;
            if (sun != null)
            {
                var q = sun.transform.rotation;
                h = h * 31 + (sun.enabled && sun.gameObject.activeInHierarchy ? 1 : 0);
                h = h * 31 + Mathf.RoundToInt(q.x * 10000f);
                h = h * 31 + Mathf.RoundToInt(q.y * 10000f);
                h = h * 31 + Mathf.RoundToInt(q.z * 10000f);
                h = h * 31 + Mathf.RoundToInt(q.w * 10000f);
            }
            stride = Mathf.Max(1, stride);
            var hidden = HiddenSides();
            bool rootOn = root != null && root.gameObject.activeInHierarchy;
            for (int i = 0; i < units.Count; i += stride)
            {
                var t = units[i].t;
                if (t == null) continue;
                var q = t.rotation;
                h = h * 31 + Mathf.RoundToInt(q.y * 10000f);
                h = h * 31 + Mathf.RoundToInt(q.w * 10000f);
                h = h * 31 + (Present(units[i], hidden, rootOn) ? 1 : 0);
            }
            return h;
        }
    }

    public string DayName()
    {
        string d;
        return TryGet(controller, FDay, out d) ? d : "";
    }

    /// <summary>"하지 08:30" 같은 지금 시각 글자</summary>
    public string TimeLabel()
    {
        float hr;
        string d = DayName();
        if (!TryGet(controller, FHour, out hr)) return d;
        int m = Mathf.RoundToInt(hr * 60f);
        return string.Format("{0} {1:00}:{2:00}", d, m / 60, m % 60);
    }

    /// <summary>하루 누적을 다시 계산해야 하는지 보는 글자(대표일, 겨울 모드, 북면 루버 등)</summary>
    public string DaySignature()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var f in SignatureFields)
        {
            object v;
            if (TryGet(controller, f, out v)) sb.Append(f).Append('=').Append(v).Append('|');
        }
        return sb.ToString();
    }

    // ---------------- 하루 누적: 컨트롤러 시각 돌리기 ----------------

    /// <summary>컨트롤러의 hour와 externalControl을 바꿀 수 있는지</summary>
    public bool CanDrive
    {
        get { return controller != null && Member(controller, FHour) != null && Member(controller, FExternal) != null; }
    }

    /// <summary>컨트롤러가 CSV를 다 읽었는지(IsReady 또는 data로 확인, 둘 다 없으면 읽었다고 봐요)</summary>
    public bool ControllerReady()
    {
        if (controller == null) return false;
        bool ready;
        if (TryGet(controller, FReady, out ready)) return ready;
        object data;
        if (Member(controller, FData) != null) return TryGet(controller, FData, out data) && data != null;
        return true;
    }

    public void BeginDrive()
    {
        if (driving || !CanDrive) return;
        TryGet(controller, FHour, out savedHour);
        TryGet(controller, FExternal, out savedExternal);
        TrySet(controller, FExternal, true);   // 자동 재생·슬라이더가 시각을 바꾸지 않게
        driving = true;
    }

    public void DriveTo(float hour)
    {
        if (driving) TrySet(controller, FHour, hour);
    }

    /// <summary>원래 시각과 externalControl로 돌려놔요(다음 프레임에 컨트롤러가 루버를 원래대로 돌려요)</summary>
    public void EndDrive()
    {
        if (!driving) return;
        TrySet(controller, FHour, savedHour);
        TrySet(controller, FExternal, savedExternal);
        driving = false;
    }

    public bool IsDriving { get { return driving; } }

    public string Summary()
    {
        int sides0 = 0, sides1 = 0, sides2 = 0, sides3 = 0, noGlass = 0;
        foreach (var u in units)
        {
            if (u.side == GlareSide.East) sides0++; else if (u.side == GlareSide.South) sides1++;
            else if (u.side == GlareSide.West) sides2++; else sides3++;
            if (!u.glass) noGlass++;
        }
        return string.Format("[Glare] 루버 연결: 컨트롤러 {0}, 날 부모 {1}, 해 {2} | 날 {3}장 (동 {4}, 남 {5}, 서 {6}, 북 {7}, 유리 없음 {8}) | 하루 누적 {9}",
            controller != null ? controller.name : "없음", root != null ? root.name : "없음", sun != null ? sun.name : "없음",
            units.Count, sides0, sides1, sides2, sides3, noGlass, CanDrive ? "가능" : "불가");
    }

    // ---------------- 이름으로 읽고 쓰기 ----------------

    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static MemberInfo Member(object o, string name)
    {
        if (o == null) return null;
        var t = o.GetType();
        var f = t.GetField(name, Flags);
        if (f != null) return f;
        return t.GetProperty(name, Flags);
    }

    public static bool TryGet<T>(object o, string name, out T value)
    {
        value = default(T);
        var m = Member(o, name);
        object v;
        var f = m as FieldInfo;
        var p = m as PropertyInfo;
        if (f != null) v = f.GetValue(o);
        else if (p != null && p.CanRead && p.GetIndexParameters().Length == 0) v = p.GetValue(o, null);
        else return false;
        if (v is T) { value = (T)v; return true; }
        return false;
    }

    public static bool TrySet(object o, string name, object value)
    {
        var m = Member(o, name);
        var f = m as FieldInfo;
        var p = m as PropertyInfo;
        try
        {
            if (f != null) { f.SetValue(o, value); return true; }
            if (p != null && p.CanWrite) { p.SetValue(o, value, null); return true; }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Glare] " + name + " 값을 못 바꿨어요: " + e.Message);
        }
        return false;
    }
}
