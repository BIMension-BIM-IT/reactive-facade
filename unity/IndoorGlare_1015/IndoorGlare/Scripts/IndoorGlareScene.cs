using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IndoorGlare
{
    /// <summary>
    /// 실내 눈부심용 광선 검사. 원래 씬은 건드리지 않고, 따로 만든 물리 씬 두 개에 콜라이더를 복사해요.
    ///  - 모델 씬: 유리(레이어 1)와 불투명(레이어 0: 벽·슬래브·기둥·가구·주변 건물·지면). 루버 날은 넣지 않아요.
    ///  - 날 씬: 센서 근처 층의 루버 날을 얇은 상자로. 시각마다 실제 날 각도로 자세를 맞춰요(해가 보이는지 확인용).
    /// 재생 중 Hierarchy에 "IndoorGlare_Model…", "IndoorGlare_Blades…" 씬이 보여요.
    /// </summary>
    public class IndoorGlareScene : IIndoorTracer
    {
        const int OpaqueLayer = 0, GlassLayer = 1;
        const int AllMask = (1 << OpaqueLayer) | (1 << GlassLayer);
        const int GlassMask = 1 << GlassLayer;

        Scene modelScene, bladeScene;
        PhysicsScene modelPhys, bladePhys;
        Transform modelHolder, bladeHolder;

        /// <summary>유리와 이만큼(m) 붙어 있는 벽은 유리로 봐요(유리와 같은 면에 있는 벽 처리, Glare2와 같아요)</summary>
        public float eps = 0.05f;
        public float maxDist = 2000f;
        public float unitSearchM = 1.2f;   // 유리 위 점에서 이 거리 안의 날을 찾아요

        public int glassMeshes, opaqueMeshes, unreadable, triangles;
        readonly List<Transform> boxes = new List<Transform>();
        readonly List<int> boxUnit = new List<int>();
        List<IndoorLouverLink.Unit> units;
        readonly Dictionary<long, List<int>> hash = new Dictionary<long, List<int>>();

        public int BladeBoxCount { get { return boxes.Count; } }

        public static IndoorGlareScene Create()
        {
            var g = new IndoorGlareScene();
            long tick = System.DateTime.Now.Ticks;
            g.modelScene = SceneManager.CreateScene("IndoorGlare_Model_" + tick, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            g.bladeScene = SceneManager.CreateScene("IndoorGlare_Blades_" + tick, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            g.modelPhys = g.modelScene.GetPhysicsScene();
            g.bladePhys = g.bladeScene.GetPhysicsScene();
            var a = new GameObject("Model (실내 눈부심 계산용 콜라이더)");
            SceneManager.MoveGameObjectToScene(a, g.modelScene);
            g.modelHolder = a.transform;
            var b = new GameObject("Blades (루버 날 상자)");
            SceneManager.MoveGameObjectToScene(b, g.bladeScene);
            g.bladeHolder = b.transform;
            return g;
        }

        /// <summary>메시 하나를 유리 또는 불투명으로 넣어요</summary>
        public bool AddMesh(MeshFilter mf, bool glass)
        {
            var mesh = mf.sharedMesh;
            if (mesh == null || mesh.vertexCount == 0) return false;
    #if !UNITY_EDITOR
            if (!mesh.isReadable) { unreadable++; return false; }
    #else
            if (!mesh.isReadable) unreadable++;
    #endif
            var t = mf.transform;
            var go = new GameObject(mf.name);
            go.transform.SetParent(modelHolder, false);
            go.transform.position = t.position;
            go.transform.rotation = t.rotation;
            go.transform.localScale = t.lossyScale;
            go.layer = glass ? GlassLayer : OpaqueLayer;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            if (glass) glassMeshes++; else opaqueMeshes++;
            for (int s = 0; s < mesh.subMeshCount; s++) triangles += (int)(mesh.GetIndexCount(s) / 3);
            return true;
        }

        /// <summary>날 하나를 얇은 상자로 넣어요. size = (두께, 높이, 폭)</summary>
        public void AddBlade(int unitIndex, Vector3 size)
        {
            var go = new GameObject("Blade_" + unitIndex);
            go.transform.SetParent(bladeHolder, false);
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            boxes.Add(go.transform);
            boxUnit.Add(unitIndex);
        }

        /// <summary>
        /// 날 상자 자세를 맞춰요. open = true면 모두 0°(열림), 아니면 ReadAngles로 읽어 둔 각도.
        /// 숨겨진 날(북면 루버 끔 등)은 상자도 꺼요. 끝나면 Sync()를 불러요.
        /// </summary>
        public void PoseBlades(bool open)
        {
            if (units == null) return;
            for (int i = 0; i < boxes.Count; i++)
            {
                var u = units[boxUnit[i]];
                bool act = u.active;
                var go = boxes[i].gameObject;
                if (go.activeSelf != act) go.SetActive(act);
                if (!act) continue;
                float a = open ? 0f : u.angle * Mathf.Deg2Rad;
                Vector3 dir = u.n * Mathf.Cos(a) + u.r * Mathf.Sin(a);
                boxes[i].SetPositionAndRotation(open ? u.CenterOpen : u.CenterNow, Quaternion.LookRotation(dir, Vector3.up));
            }
        }

        public void Sync()
        {
            Physics.SyncTransforms();
        }

        // ---------------- 날 찾기(유리 위 점 → 바로 앞 날) ----------------

        static long Key(int x, int z) { return ((long)x << 32) ^ (uint)z; }

        public void BuildUnitHash(List<IndoorLouverLink.Unit> list)
        {
            units = list;
            hash.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                Vector3 c = list[i].center;
                long k = Key(Mathf.FloorToInt(c.x), Mathf.FloorToInt(c.z));
                List<int> l;
                if (!hash.TryGetValue(k, out l)) { l = new List<int>(); hash[k] = l; }
                l.Add(i);
            }
        }

        public int UnitNear(Vector3 exit)
        {
            if (units == null || units.Count == 0) return -1;
            int r = Mathf.CeilToInt(unitSearchM);
            int cx = Mathf.FloorToInt(exit.x), cz = Mathf.FloorToInt(exit.z);
            int best = -1;
            float bd = unitSearchM * unitSearchM;
            for (int x = cx - r; x <= cx + r; x++)
                for (int z = cz - r; z <= cz + r; z++)
                {
                    List<int> l;
                    if (!hash.TryGetValue(Key(x, z), out l)) continue;
                    foreach (int i in l)
                    {
                        var u = units[i];
                        if (exit.y < u.minY - 0.3f || exit.y > u.maxY + 0.3f) continue;
                        float dx = u.center.x - exit.x, dz = u.center.z - exit.z, d2 = dx * dx + dz * dz;
                        if (d2 < bd) { bd = d2; best = i; }
                    }
                }
            return best;
        }

        // ---------------- 광선 ----------------

        /// <summary>
        /// Glare2의 RayCaster.Visible과 같은 규칙에 "창 밖에서 무엇에 닿았는지"를 더했어요.
        ///  - 유리를 지나기 전에 불투명 면 → Interior(창이 아님)
        ///  - 유리와 eps 안에 붙은 불투명 면은 무시(유리와 같은 면의 벽)
        ///  - 유리를 지난 뒤 불투명 면 → 아래로 가다 수평면이면 Ground, 아니면 Wall
        ///  - 유리를 지나 아무것도 없으면 Sky
        /// </summary>
        public IndoorHit TraceStatic(Vector3 p, Vector3 d, out Vector3 exit)
        {
            exit = p;
            Vector3 o = p;
            float traveled = 0f, lastGlass = -1e9f;
            bool passed = false;
            for (int it = 0; it < 32; it++)
            {
                RaycastHit h;
                if (!modelPhys.Raycast(o, d, out h, maxDist - traveled, AllMask, QueryTriggerInteraction.Ignore))
                    return passed ? IndoorHit.Sky : IndoorHit.Interior;
                float t = traveled + h.distance;
                bool glass = h.collider != null && h.collider.gameObject.layer == GlassLayer;
                if (glass)
                {
                    passed = true;
                    lastGlass = t;
                    exit = h.point;
                }
                else
                {
                    bool near = t - lastGlass < eps;
                    if (!near)
                    {
                        RaycastHit g;
                        near = modelPhys.Raycast(h.point - d * eps, d, out g, 2f * eps, GlassMask, QueryTriggerInteraction.Ignore);
                    }
                    if (!near)
                    {
                        if (!passed) return IndoorHit.Interior;
                        if (d.y < 0f && Mathf.Abs(h.normal.y) > 0.7f) return IndoorHit.Ground;
                        return IndoorHit.Wall;
                    }
                }
                traveled = t + 1e-3f;
                if (traveled >= maxDist) break;
                o = h.point + d * 1e-3f;
            }
            return passed ? IndoorHit.Wall : IndoorHit.Interior;
        }

        public bool BladeBlocked(Vector3 p, Vector3 d)
        {
            if (boxes.Count == 0) return false;
            RaycastHit h;
            return bladePhys.Raycast(p, d, out h, 500f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        // ---------------- 유리 넓이 ----------------

        /// <summary>
        /// 유리 메시 하나를 영역 상자(가로로 search m 넓힌 것)로 잘라서, 입면별 평면(입면 방향 × 높이)에 10 cm 칸으로 칠해요.
        /// 같은 자리를 여러 번 칠해도 한 번만 세서, 두께 있는 유리(앞뒷면)·이중 유리·뒤집힌 면도 넓이가 한 번만 들어가요.
        /// 여러 층에 걸친 큰 커튼월 메시도 그 층 몫만 세요.
        /// </summary>
        public static bool AddGlassArea(MeshFilter mf, IndoorGlareZone zone, IndoorGlassRaster raster)
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) return false;
    #if !UNITY_EDITOR
            if (!mesh.isReadable) return false;
    #endif
            var verts = new List<Vector3>();
            var tris = new List<int>();
            try { mesh.GetVertices(verts); }
            catch (System.Exception) { return false; }
            var M = mf.transform.localToWorldMatrix;
            Quaternion inv = Quaternion.Inverse(zone.Yaw);
            Vector3 origin = zone.transform.position;
            for (int i = 0; i < verts.Count; i++) verts[i] = inv * (M.MultiplyPoint3x4(verts[i]) - origin);   // 영역 좌표(바닥 가운데 원점)
            Vector3 lo = new Vector3(-zone.size.x * 0.5f - raster.search, 0f, -zone.size.z * 0.5f - raster.search);
            Vector3 hi = new Vector3(zone.size.x * 0.5f + raster.search, zone.size.y, zone.size.z * 0.5f + raster.search);
            Vector3 zc = new Vector3(0f, zone.size.y * 0.5f, 0f);
            var poly = new List<Vector3>(8);
            var tmp = new List<Vector3>(8);
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                tris.Clear();
                try { mesh.GetTriangles(tris, s); }
                catch (System.Exception) { return false; }
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                    Vector3 cr = Vector3.Cross(b - a, c - a);
                    float full = cr.magnitude;
                    if (full < 2e-6f) continue;
                    Vector3 nrm = cr / full;
                    if (Mathf.Abs(nrm.y) > 0.3f) continue;            // 수직 면만
                    poly.Clear(); poly.Add(a); poly.Add(b); poly.Add(c);
                    for (int axis = 0; axis < 3 && poly.Count >= 3; axis++)
                    {
                        Clip(poly, tmp, axis, Get(lo, axis), true);
                        Clip(tmp, poly, axis, Get(hi, axis), false);
                    }
                    if (poly.Count < 3) continue;
                    Vector3 cen = Vector3.zero;
                    foreach (var v in poly) cen += v;
                    cen /= poly.Count;
                    // 입면: 법선이 가리키는 수평 축 + 영역 가운데 기준 위치(영역 축 = 루버 입면 축이라고 봐요)
                    Vector3 nW = zone.Yaw * nrm, rel = zone.Yaw * (cen - zc);
                    int side = Mathf.Abs(nW.x) >= Mathf.Abs(nW.z)
                        ? (rel.x >= 0 ? (int)IndoorSide.East : (int)IndoorSide.West)
                        : (rel.z >= 0 ? (int)IndoorSide.North : (int)IndoorSide.South);
                    raster.Fill(side, poly);
                }
            }
            return true;
        }

        static float Get(Vector3 v, int axis) { return axis == 0 ? v.x : axis == 1 ? v.y : v.z; }

        /// <summary>다각형을 한 축의 평면으로 잘라요(keepAbove = 그 값 이상을 남김)</summary>
        static void Clip(List<Vector3> src, List<Vector3> dst, int axis, float k, bool keepAbove)
        {
            dst.Clear();
            int n = src.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = src[i], q = src[(i + 1) % n];
                float dp = keepAbove ? Get(p, axis) - k : k - Get(p, axis);
                float dq = keepAbove ? Get(q, axis) - k : k - Get(q, axis);
                if (dp >= 0) dst.Add(p);
                if ((dp >= 0) != (dq >= 0)) dst.Add(p + (q - p) * (dp / (dp - dq)));
            }
        }

        public void Dispose()
        {
            if (modelScene.IsValid() && modelScene.isLoaded) SceneManager.UnloadSceneAsync(modelScene);
            if (bladeScene.IsValid() && bladeScene.isLoaded) SceneManager.UnloadSceneAsync(bladeScene);
        }
    }

    /// <summary>영역의 네 입면 평면(입면 방향 좌표 × 높이)에 유리를 칠하는 10 cm 격자</summary>
    public class IndoorGlassRaster
    {
        public readonly float cell, search;
        readonly float[] u0 = new float[4];
        readonly int[] nu = new int[4];
        readonly int nv;
        readonly bool[][] filled = new bool[4][];

        public IndoorGlassRaster(IndoorGlareZone zone, float search, float cell = 0.1f)
        {
            this.cell = cell;
            this.search = search;
            nv = Mathf.Max(1, Mathf.CeilToInt(zone.size.y / cell));
            for (int k = 0; k < 4; k++)
            {
                bool ew = k == (int)IndoorSide.East || k == (int)IndoorSide.West;   // 동·서면은 입면 방향 = 영역 z
                float len = (ew ? zone.size.z : zone.size.x) + 2f * search;
                u0[k] = -len * 0.5f;
                nu[k] = Mathf.Max(1, Mathf.CeilToInt(len / cell));
                filled[k] = new bool[nu[k] * nv];
            }
        }

        static float U(int side, Vector3 p)
        {
            return side == (int)IndoorSide.East || side == (int)IndoorSide.West ? p.z : p.x;
        }

        /// <summary>볼록 다각형(영역 좌표)을 그 입면 평면에 투영해서 칠해요</summary>
        public void Fill(int side, List<Vector3> poly)
        {
            int n = poly.Count;
            var pu = new float[n];
            var pv = new float[n];
            float umin = float.MaxValue, umax = float.MinValue, vmin = float.MaxValue, vmax = float.MinValue, area2 = 0f;
            for (int i = 0; i < n; i++)
            {
                pu[i] = U(side, poly[i]); pv[i] = poly[i].y;
                umin = Mathf.Min(umin, pu[i]); umax = Mathf.Max(umax, pu[i]);
                vmin = Mathf.Min(vmin, pv[i]); vmax = Mathf.Max(vmax, pv[i]);
            }
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; area2 += pu[i] * pv[j] - pu[j] * pv[i]; }
            if (Mathf.Abs(area2) < 1e-8f) return;                  // 이 평면에서 보면 선(면이 아님)
            float sg = area2 > 0 ? 1f : -1f;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((umin - u0[side]) / cell)), i1 = Mathf.Min(nu[side] - 1, Mathf.CeilToInt((umax - u0[side]) / cell));
            int j0 = Mathf.Max(0, Mathf.FloorToInt(vmin / cell)), j1 = Mathf.Min(nv - 1, Mathf.CeilToInt(vmax / cell));
            var f = filled[side];
            for (int j = j0; j <= j1; j++)
            {
                float y = (j + 0.5f) * cell;
                for (int i = i0; i <= i1; i++)
                {
                    if (f[j * nu[side] + i]) continue;
                    float x = u0[side] + (i + 0.5f) * cell;
                    bool inside = true;
                    for (int a = 0; a < n && inside; a++)
                    {
                        int b = (a + 1) % n;
                        float cr = (pu[b] - pu[a]) * (y - pv[a]) - (pv[b] - pv[a]) * (x - pu[a]);
                        if (cr * sg < 0f) inside = false;
                    }
                    if (inside) f[j * nu[side] + i] = true;
                }
            }
        }

        /// <summary>입면별 칠해진 넓이(m²)</summary>
        public float[] Areas()
        {
            var a = new float[4];
            for (int k = 0; k < 4; k++)
            {
                int c = 0;
                foreach (bool b in filled[k]) if (b) c++;
                a[k] = c * cell * cell;
            }
            return a;
        }
    }
}
