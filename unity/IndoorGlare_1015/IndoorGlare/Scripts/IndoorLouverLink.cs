using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace IndoorGlare
{
    /// <summary>입면 방향(동 남 서 북). GlareSim과 따로 정의해요(파일·이름이 서로 엉키지 않게).</summary>
    public enum IndoorSide { East = 0, South = 1, West = 2, North = 3 }

    public static class IndoorFacade
    {
        /// <summary>입면 바깥 방향(Unity: +X 동, +Z 북)</summary>
        public static Vector3 NormalOf(IndoorSide s)
        {
            switch (s)
            {
                case IndoorSide.East: return new Vector3(1, 0, 0);
                case IndoorSide.South: return new Vector3(0, 0, -1);
                case IndoorSide.West: return new Vector3(-1, 0, 0);
                default: return new Vector3(0, 0, 1);
            }
        }

        /// <summary>입면을 바깥에서 볼 때 오른쪽(루버 각도 + 방향)</summary>
        public static Vector3 RightOf(IndoorSide s)
        {
            return Vector3.Cross(Vector3.up, -NormalOf(s));
        }
    }

    /// <summary>
    /// 실내 눈부심이 반응형 루버 쪽(SunLouverController, LouverRig)을 읽는 유일한 파일이에요.
    /// GlareSim의 LouverSceneLink와 같은 방식(이름으로 찾고 씬 상태를 읽기)이지만 따로 있어요.
    /// 루버 스크립트도, GlareSim 스크립트도 코드로 부르지 않아서 어느 쪽이 업데이트되어도 컴파일 에러가 나지 않아요.
    ///  - 날 각도: 날(louverRoot의 자식) 회전을 읽어요. LouverRig의 Pivot_은 처음 회전 = 열림(0°)으로 봐요.
    ///  - 날이 있는지: LouverRig.userHidden(입면별로 사용자가 숨김)으로 판단해요. LouverPerformance가 화면을
    ///    가볍게 하려고 끈 날은 있다고 봐요(GlareSim과 같은 규칙). userHidden이 없으면 켜져 있는지로 봐요.
    ///  - 해: 컨트롤러의 sun(Directional Light)
    ///  - 하루 계산: 컨트롤러의 day·hour를 바꿔 가며 해·루버가 실제로 돌아간 상태를 읽어요(끝나면 되돌려요).
    ///    반사광(GlareReflectionSim)이 계산 중이면 끝날 때까지 기다려요(이것도 이름으로만 봐요).
    /// 루버 쪽 이름이 바뀌면 이 파일 맨 위 상수만 고치면 돼요.
    /// </summary>
    public class IndoorLouverLink
    {
        // ---- 루버·반사광 스크립트에서 쓰는 이름 (바뀌면 여기만 고쳐요) ----
        public const string ControllerTypeName = "SunLouverController";
        public const string RigTypeName = "LouverRig";
        public const string ReflectionSimTypeName = "GlareReflectionSim";
        const string FDay = "day";                    // string  대표일(하지 등)
        const string FHour = "hour";                  // float   시각(시)
        const string FExternal = "externalControl";   // bool    켜면 자동 재생·슬라이더를 무시
        const string FSun = "sun";                    // Light   해
        const string FRig = "rig";                    // LouverRig
        const string FRoot = "louverRoot";            // Transform 날들의 부모
        const string FSkip = "skipNameContains";      // string[] 돌리지 않는 것
        const string FReady = "IsReady";              // (있으면) CSV를 다 읽었는지
        const string FData = "data";                  // (있으면) CSV 데이터
        const string FUserHidden = "userHidden";      // (있으면) LouverRig의 bool[4] 입면별 사용자 숨김
        const string FBusy = "IsBusy";                // (있으면) GlareReflectionSim이 계산 중인지
        static readonly string[] SignatureFields = { "day", "winterMode", "northLouver", "useFloorSchedule", "useContinuousAngle", "csvFileName", "floorCsvFileName" };

        /// <summary>날 한 장(louverRoot의 직계 자식 하나)</summary>
        public class Unit
        {
            public Transform t;
            public string name = "";
            public Quaternion q0;   // 열림(0°)일 때 회전. Pivot_이면 identity
            public Vector3 b0;      // 열림일 때 날 방향(수평)
            public Vector3 n, r;    // 입면 바깥 방향, 바깥에서 볼 때 오른쪽
            public Vector3 center;  // 찾을 때 날 중심(월드)
            public Vector3 offset0; // q0 기준에서 transform 위치 → 날 중심
            public Bounds bounds;   // 찾을 때 범위(월드)
            public float height, minY, maxY;
            public int floor = -1;  // 이름에서 읽은 층("09F-123", "9_S_001"), 모르면 -1
            public IndoorSide side;
            public float angle;     // ReadAngles로 읽은 각도(도, 0 = 열림, + = 바깥 끝이 오른쪽)
            public bool active = true;

            public Vector3 CenterNow
            {
                get { return t == null ? center : t.position + (t.rotation * Quaternion.Inverse(q0)) * offset0; }
            }

            public Vector3 CenterOpen
            {
                get { return t == null ? center : t.position + offset0; }
            }
        }

        public MonoBehaviour controller, rig;
        /// <summary>반사광 시뮬레이션(있으면). 계산 중이면 하루 계산을 기다려요</summary>
        public MonoBehaviour otherSim;
        public Light sun;
        public Transform root;
        public readonly List<Unit> units = new List<Unit>();
        public Bounds envelope;
        public readonly List<string> warnings = new List<string>();

        bool driving, savedExternal;
        float savedHour;
        string savedDay;

        // ---------------- 찾기 ----------------

        public void Setup(MonoBehaviour controllerOverride, MonoBehaviour rigOverride, Transform rootOverride, Light sunOverride, string[] skipNames)
        {
            warnings.Clear();
            controller = controllerOverride != null ? controllerOverride : FindByTypeName(ControllerTypeName);
            if (controller == null) warnings.Add(ControllerTypeName + "를 못 찾았어요(하루 계산과 시각 표시가 안 돼요)");

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

            otherSim = FindByTypeName(ReflectionSimTypeName);

            var skip = new List<string>();
            if (skipNames != null) skip.AddRange(skipNames);
            string[] rigSkip;
            if (TryGet(rig, FSkip, out rigSkip) && rigSkip != null) skip.AddRange(rigSkip);
            BuildUnits(skip);

            if (rig != null && HiddenSides() == null)
                warnings.Add(RigTypeName + "." + FUserHidden + "를 못 찾았어요. 꺼진 날은 모두 없다고 봐요. " +
                             "LouverPerformance가 카메라 반대쪽 면을 끄면 그 면을 루버 없는 것으로 계산하니 Cull Back Faces를 꺼 주세요");
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

        static readonly Vector3[] Corners =
        {
            new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(-1, 1, -1), new Vector3(1, 1, -1),
            new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(-1, 1, 1), new Vector3(1, 1, 1)
        };

        /// <summary>
        /// 날 하나의 월드 범위. 메시 범위를 변환해서 구해요(LouverPerformance가 꺼 둔 날도 맞게).
        /// 메시가 없으면 렌더러 범위를 써요.
        /// </summary>
        static bool WorldBounds(Transform t, out Bounds b)
        {
            b = new Bounds();
            bool has = false;
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                Bounds lb = m.bounds;
                var M = mf.transform.localToWorldMatrix;
                foreach (var c in Corners)
                {
                    Vector3 w = M.MultiplyPoint3x4(lb.center + Vector3.Scale(lb.extents, c));
                    if (!has) { b = new Bounds(w, Vector3.zero); has = true; }
                    else b.Encapsulate(w);
                }
            }
            if (has) return true;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            return has;
        }

        void BuildUnits(List<string> skip)
        {
            units.Clear();
            if (root == null) return;
            var ts = new List<Transform>();
            var bs = new List<Bounds>();
            bool has = false;
            foreach (Transform child in root)
            {
                if (ContainsAny(child.name, skip)) continue;
                Bounds b;
                if (!WorldBounds(child, out b)) continue;
                ts.Add(child);
                bs.Add(b);
                if (!has) { envelope = b; has = true; } else envelope.Encapsulate(b);
            }
            for (int i = 0; i < ts.Count; i++)
            {
                var b = bs[i];
                bool pivot = ts[i].name.StartsWith("Pivot_");
                string nm = pivot ? ts[i].name.Substring(6) : ts[i].name;
                IndoorSide side;
                if (!TryParseSide(nm, out side)) side = Classify(b.center, envelope);
                var n = IndoorFacade.NormalOf(side);
                var r = IndoorFacade.RightOf(side);
                float extN = Mathf.Abs(Vector3.Dot(b.size, n)), extR = Mathf.Abs(Vector3.Dot(b.size, r));
                Quaternion q0 = pivot ? Quaternion.identity : ts[i].rotation;
                Quaternion rSetup = ts[i].rotation * Quaternion.Inverse(q0);
                units.Add(new Unit
                {
                    t = ts[i],
                    name = nm,
                    q0 = q0,
                    b0 = pivot ? n : (extN >= extR ? n : r),
                    n = n,
                    r = r,
                    center = b.center,
                    offset0 = Quaternion.Inverse(rSetup) * (b.center - ts[i].position),
                    bounds = b,
                    height = b.size.y,
                    minY = b.min.y,
                    maxY = b.max.y,
                    floor = ParseFloor(nm),
                    side = side,
                });
            }
        }

        static int ParseFloor(string n)
        {
            var m = System.Text.RegularExpressions.Regex.Match(n, @"(\d+)F-");
            if (m.Success) return int.Parse(m.Groups[1].Value);
            var t = n.Split('_');
            int f;
            if (t.Length >= 2 && int.TryParse(t[0].Trim(), out f)) return f;
            return -1;
        }

        static bool TryParseSide(string n, out IndoorSide side)
        {
            side = IndoorSide.South;
            var t = n.Split('_');
            int floor;
            if (t.Length < 2 || !int.TryParse(t[0].Trim(), out floor)) return false;
            switch (t[1].Trim().ToUpperInvariant())
            {
                case "E": case "동": case "동면": side = IndoorSide.East; return true;
                case "S": case "남": case "남면": side = IndoorSide.South; return true;
                case "W": case "서": case "서면": side = IndoorSide.West; return true;
                case "N": case "북": case "북면": side = IndoorSide.North; return true;
            }
            return false;
        }

        static IndoorSide Classify(Vector3 c, Bounds all)
        {
            float dE = all.max.x - c.x, dW = c.x - all.min.x, dN = all.max.z - c.z, dS = c.z - all.min.z;
            float m = Mathf.Min(Mathf.Min(dE, dW), Mathf.Min(dN, dS));
            if (m == dE) return IndoorSide.East;
            if (m == dW) return IndoorSide.West;
            if (m == dN) return IndoorSide.North;
            return IndoorSide.South;
        }

        // ---------------- 읽기 ----------------

        bool[] HiddenSides()
        {
            bool[] h;
            return TryGet(rig, FUserHidden, out h) && h != null && h.Length >= 4 ? h : null;
        }

        /// <summary>사용자가 숨긴 면(북면 루버 끔 등)만 없다고 봐요(GlareSim과 같은 규칙)</summary>
        static bool Present(Unit u, bool[] hidden, bool rootOn)
        {
            if (u.t == null) return false;
            if (hidden != null) return rootOn && !hidden[(int)u.side];
            return u.t.gameObject.activeInHierarchy;
        }

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

        public bool SunUp(out Vector3 sunDir)
        {
            sunDir = Vector3.up;
            if (sun == null || !sun.enabled || !sun.gameObject.activeInHierarchy) return false;
            sunDir = -sun.transform.forward;
            return sunDir.y > 0.001f;
        }

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

        public bool TryHour(out float hour)
        {
            return TryGet(controller, FHour, out hour);
        }

        public string TimeLabel()
        {
            float hr;
            string d = DayName();
            if (!TryHour(out hr)) return d;
            int m = Mathf.RoundToInt(hr * 60f);
            return string.Format("{0} {1:00}:{2:00}", d, m / 60, m % 60);
        }

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

        // ---------------- 하루 계산: 컨트롤러 날·시각 돌리기 ----------------

        public bool CanDrive
        {
            get { return controller != null && Member(controller, FHour) != null && Member(controller, FExternal) != null; }
        }

        public bool ControllerReady()
        {
            if (controller == null) return false;
            bool ready;
            if (TryGet(controller, FReady, out ready)) return ready;
            object data;
            if (Member(controller, FData) != null) return TryGet(controller, FData, out data) && data != null;
            return true;
        }

        /// <summary>반사광 시뮬레이션이 계산 중인지(있을 때만)</summary>
        public bool OtherSimBusy()
        {
            bool b;
            return otherSim != null && otherSim.isActiveAndEnabled && TryGet(otherSim, FBusy, out b) && b;
        }

        /// <summary>날·시각 돌리기를 시작해요. 반사광이 계산 중이면 false(잠시 뒤 다시 불러요)</summary>
        public bool BeginDrive()
        {
            if (driving) return true;
            if (!CanDrive || OtherSimBusy()) return false;
            TryGet(controller, FHour, out savedHour);
            TryGet(controller, FExternal, out savedExternal);
            TryGet(controller, FDay, out savedDay);
            TrySet(controller, FExternal, true);   // 자동 재생·슬라이더가 시각을 바꾸지 않게
            driving = true;
            return true;
        }

        public void DriveDay(string day)
        {
            if (driving && !string.IsNullOrEmpty(day)) TrySet(controller, FDay, day);
        }

        public void DriveTo(float hour)
        {
            if (driving) TrySet(controller, FHour, hour);
        }

        /// <summary>내가 정한 날·시각이 그대로인지(다른 것이 바꿨으면 false)</summary>
        public bool StillAt(string day, float hour)
        {
            float h;
            if (!TryHour(out h)) return true;
            return Mathf.Abs(h - hour) < 1e-3f && DayName() == day;
        }

        /// <summary>원래 날·시각·externalControl로 돌려놔요</summary>
        public void EndDrive()
        {
            if (!driving) return;
            if (savedDay != null) TrySet(controller, FDay, savedDay);
            TrySet(controller, FHour, savedHour);
            TrySet(controller, FExternal, savedExternal);
            driving = false;
        }

        /// <summary>되돌리지 않고 그만둬요(웹·사용자가 새 시각을 정했을 때 그 시각을 지켜요)</summary>
        public void Release()
        {
            driving = false;
        }

        public bool IsDriving { get { return driving; } }

        public string Summary()
        {
            int e = 0, s = 0, w = 0, n = 0, withFloor = 0;
            foreach (var u in units)
            {
                if (u.side == IndoorSide.East) e++; else if (u.side == IndoorSide.South) s++;
                else if (u.side == IndoorSide.West) w++; else n++;
                if (u.floor >= 0) withFloor++;
            }
            return string.Format("[Indoor] 루버 연결: 컨트롤러 {0}, 날 부모 {1}, 해 {2}, 반사광 {3} | 날 {4}장 (동 {5}, 남 {6}, 서 {7}, 북 {8}, 층 이름 {9}장) | 하루 계산 {10}",
                controller != null ? controller.name : "없음", root != null ? root.name : "없음", sun != null ? sun.name : "없음",
                otherSim != null ? otherSim.name : "없음", units.Count, e, s, w, n, withFloor, CanDrive ? "가능" : "불가");
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
                Debug.LogWarning("[Indoor] " + name + " 값을 못 바꿨어요: " + e.Message);
            }
            return false;
        }
    }
}
