using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace IndoorGlare
{
    public enum IndoorMode { Off, Instant, Daily }
    public enum IndoorDailyMetric { GlareAutonomy, GlareHours, MaxDgp }
    public enum IndoorPanelPlace { Game, Scene, Inspector }
    [System.Serializable] public class IndoorStringEvent : UnityEvent<string> { }

    /// <summary>
    /// 실내 눈부심(DGP) 시뮬레이션. Grasshopper Glare1~3(방사형 센서 → 간이 DGP 엔진 → 색칠)을 유니티로 옮기고,
    /// 태양과 시각은 반응형 루버 컨트롤러(sun_louver.csv)를, 루버는 실제로 돌아간 날 각도를 써요.
    /// 반사광(GlareSim) 폴더와 따로 있어요. 서로의 스크립트를 코드로 부르지 않아요.
    ///
    /// 쓰는 법 (IndoorGlare/README 참고)
    ///  1) 빈 오브젝트 "IndoorGlare"에 이 스크립트를 붙여요. 연결 칸은 비워 두면 자동으로 찾아요.
    ///  2) 센서 영역: IndoorGlareZone을 놓거나(편집 모드 Inspector의 "층 영역 만들기"), 비워 두면 Auto Floor 층 전체로 만들어요.
    ///  3) 재생하면 Game 창 왼쪽 위(H 키)·Scene 창·Inspector에 패널이 나와요.
    ///
    /// 색: 현재 시각 = DGP(초록 → 빨강, 0.35 인지 가능 / 0.40 방해 / 0.45 견디기 어려움), 하루 = GA(빨강 → 초록).
    /// 회색 쐐기 = 창이 전혀 안 보이는 센서.
    /// </summary>
    public class IndoorGlareSim : MonoBehaviour
    {
        [Header("반응형 루버와 연결 (비우면 이름으로 자동으로 찾아요)")]
        [Tooltip("SunLouverController 컴포넌트")] public MonoBehaviour louverController;
        [Tooltip("LouverRig 컴포넌트")] public MonoBehaviour louverRig;
        [Tooltip("날들이 들어 있는 부모. 비우면 LouverRig의 Louver Root")] public Transform louverRootOverride;
        [Tooltip("해(Directional Light). 비우면 컨트롤러의 Sun")] public Light sunOverride;
        public string[] skipNameContains = { "고정" };

        [Header("건물 모델")]
        [Tooltip("유리(창) 오브젝트의 부모들. 비우면 이름·부모 이름·재질 이름에 아래 글자가 있는 메시를 유리로 봐요")]
        public Transform[] glassRoots;
        public string[] glassKeywords = { "glass", "유리", "glazing", "window", "커튼월" };
        [Tooltip("벽·슬래브·기둥·가구·주변 건물의 부모들. 비우면 씬의 메시 전부(유리·루버 날 빼고)를 막는 면으로 써요")]
        public Transform[] opaqueRoots;
        [Tooltip("계산에서 뺄 것(하늘 돔, 사람, 큰 표시용 판 등)")]
        public Transform[] excludeRoots;

        [Header("센서 영역 (씬에 IndoorGlareZone이 없으면 Auto Floor 층 전체로 만들어요)")]
        [Tooltip("0이면 자동으로 만들지 않아요")] public int autoFloor = 9;
        public float floorHeightM = 4.2f;

        [Header("유리·실내·루버")]
        [Tooltip("유리 가시광 투과율(VT). Glare2 기본 0.6")]
        [Range(0.05f, 1f)] public float visibleTransmittance = 0.6f;
        [Tooltip("실내 평균 반사율(상호반사). Glare2 기본 0.5")]
        [Range(0f, 0.95f)] public float interiorReflectance = 0.5f;
        [Range(0f, 1f)] public float groundReflectance = 0.2f;
        [Tooltip("루버 날 표면의 확산 반사율. 날이 가린 부분을 이 밝기로 봐요(근사)")]
        [Range(0f, 1f)] public float bladeReflectance = 0.3f;
        [Tooltip("창 밖으로 보이는 주변 건물 벽의 반사율(근사)")]
        [Range(0f, 1f)] public float contextReflectance = 0.2f;
        public float bladeWidthM = 0.30f, bladeSpacingM = 0.35f;
        [Tooltip("해가 보이는지 확인할 때 쓰는 날 두께(m)")] public float bladeThicknessM = 0.03f;
        [Tooltip("유리면에서 날 회전 중심까지(m). 층 영역 만들 때 씀")] public float bladeCenterToGlassM = 0.25f;

        [Header("외측 유리 (반응형 내부 루버일 때만)")]
        [Tooltip("날 바깥 외측 유리(외측 저반사유리)의 가시광 투과율. 반응형(내부 루버)일 때 창으로 들어오는 빛에 한 번 더 곱해요. " +
                 "기본값은 임의의 값이니 사양서 값을 넣어요")]
        [Range(0.05f, 1f)] public float outerGlassVisibleTransmittance = 0.85f;

        [Header("하늘")]
        public IndoorSkyModel sky = IndoorSkyModel.Clear;
        [Tooltip("맑은 하늘 직달 감쇠 계수(IES 0.21). 미세먼지가 많은 날은 0.3~0.4")]
        public float clearSkyExtinction = 0.21f;
        [Tooltip("Sky = Epw일 때 StreamingAssets 안의 EPW 파일 이름(예: KOR_Seoul.epw)")]
        public string epwFileName = "";

        [Header("계산")]
        public IndoorMode mode = IndoorMode.Instant;
        [Tooltip("ResponsiveInner = 지금 루버 각도 + 외측 유리(내부 루버), ResponsiveOuter = 지금 루버 각도(외부 루버), " +
                 "AllOpen = 모두 0°(열림), NoLouver = 루버 없음. AllOpen·NoLouver는 외측 유리 없이 계산해요(반사광과 같아요)")]
        public IndoorLouverCase louverCase = IndoorLouverCase.ResponsiveInner;
        [Tooltip("천공 패치당 분할(1~3). Glare2 기본 2(패치당 4광선)")]
        [Range(1, 3)] public int skyResolution = 2;
        public int occupiedStart = 8, occupiedEnd = 18;
        [Tooltip("DGP가 이 값 이상이면 글레어(Glare2 기본 0.40)")]
        public float dgpThreshold = 0.40f;
        [Tooltip("켜면 하루 GA를 대표일 4일 모두로 계산해요(꺼지면 컨트롤러의 지금 대표일만)")]
        public bool allRepresentativeDays = false;
        public string[] representativeDays = { "춘분", "하지", "추분", "동지" };
        [Tooltip("한 프레임에 계산에 쓰는 시간(ms)")]
        public float msPerFrame = 12f;

        [Header("표시")]
        public IndoorDailyMetric dailyMetric = IndoorDailyMetric.GlareAutonomy;
        [Tooltip("켜면 센서 쐐기가 벽·슬래브 뒤에서도 보여요")]
        public bool showThroughWalls = true;
        public float dgpScaleMin = 0.20f, dgpScaleMax = 0.50f;
        [Tooltip("켜면 카메라 위치·방향에서 DGP를 계속 계산해요(Game 창 아래쪽에 표시)")]
        public bool cameraDgp = false;
        [Tooltip("비우면 Main Camera")] public Camera viewCamera;
        [Tooltip("Game 창 왼쪽 위 패널(H 키로 숨기기/보기)")] public bool showGui = true;
        [Tooltip("재생 중 Scene 창에도 패널")] public bool showSceneGui = true;
        [Tooltip("Scene 창 패널을 왼쪽에 둬요(오른쪽은 반사광 패널 자리)")] public bool sceneGuiOnLeft = true;
        public Font guiFont;

        [Header("결과 (웹 등으로 보낼 때)")]
        public IndoorStringEvent onResultJson;
        [TextArea(2, 8)] public string lastResultJson;

        // ---------------- 내부 상태 ----------------
        IndoorLouverLink link;
        IndoorGlareScene scene;
        IndoorGlareEngine eng;
        IndoorEpw epw;
        bool epwLoading;
        string epwError;
        readonly List<IndoorGlareZone> zones = new List<IndoorGlareZone>();
        int builtSkyRes = -1, noWindowPoints;

        bool ready, built, buildFailed, dirty = true, quitting, backfacesDefault;
        Coroutine job;
        long instantSig = long.MinValue;
        string dailySig = "";
        float nextCheck, progress;
        string status = "준비 중";
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        float[] resDgp, resEv, resGA, resGlareH, resMaxDgp;
        bool shownDaily;
        IndoorLouverCase shownCase;
        string shownLabel = "", shownStats = "", shownUnit = "";
        float shownLo, shownHi;
        int shownOccSteps;
        readonly List<string> compareLines = new List<string>();
        IndoorCompareJson[] compareJson;

        Mesh wedgeMesh;
        GameObject wedgeGo;
        Material visMat;
        Color32[] colors;
        Texture2D legendDgp, legendGa;
        bool legendIsGa;

        IndoorPoint camPt;
        IndoorSensor camSensor;
        Vector3 camPos = new Vector3(1e9f, 0f, 0f), camDir;
        float camNext, camDgpValue = -1f;
        long camSig;
        string camText = "";

        static readonly Color32 Grey = new Color32(160, 160, 160, 200);
        // Glare3 팔레트: GA(높을수록 좋음) 빨강→초록, DGP(높을수록 나쁨) 초록→빨강
        static readonly Color32[] PalGa = { new Color32(178, 24, 43, 255), new Color32(239, 138, 98, 255), new Color32(253, 219, 121, 255), new Color32(166, 217, 106, 255), new Color32(26, 150, 65, 255) };
        static readonly Color32[] PalDgp = { new Color32(26, 150, 65, 255), new Color32(166, 217, 106, 255), new Color32(253, 219, 121, 255), new Color32(239, 138, 98, 255), new Color32(178, 24, 43, 255) };

        // ---------------- 에디터(Scene 창·Inspector)에서 읽는 값 ----------------
        public static IndoorGlareSim Active { get; private set; }
        public bool Ready { get { return ready; } }
        public bool IsBusy { get { return job != null; } }
        public int ResultVersion { get; private set; }

        // ---------------- 시작 ----------------

        void Start()
        {
            Application.quitting += OnQuit;
            backfacesDefault = Physics.queriesHitBackfaces;
            link = new IndoorLouverLink();
            link.Setup(louverController, louverRig, louverRootOverride, sunOverride, skipNameContains);
            Debug.Log(link.Summary());
            foreach (var w in link.warnings) Debug.LogWarning("[Indoor] " + w);
            if (link.sun == null)
            {
                Debug.LogError("[Indoor] 해(Directional Light)를 못 찾았어요. Sun Override에 넣어 주세요.");
                enabled = false;
                return;
            }
            if (link.units.Count == 0) Debug.LogWarning("[Indoor] 루버 날을 못 찾았어요. 루버 없이(유리만) 계산해요.");
            SetupMaterial();
            ready = true;
            Active = this;
        }

        void OnQuit() { quitting = true; }

        void OnDisable()
        {
            if (link != null) link.EndDrive();
            job = null;
            dirty = true;
            epwLoading = false;
            Physics.queriesHitBackfaces = backfacesDefault;
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
            Application.quitting -= OnQuit;
            if (link != null) link.EndDrive();
            if (!quitting && scene != null) scene.Dispose();
        }

        void OnValidate()
        {
            dirty = true;
            if (epw == null) epwError = null;   // 파일 이름을 고치면 다시 읽어 봐요
            ApplyZTest();
        }

        void SetupMaterial()
        {
            var sh = Shader.Find("IndoorGlare/VertexColorUnlit");
            if (sh == null)
            {
                Debug.LogWarning("[Indoor] IndoorGlare/VertexColorUnlit 셰이더를 못 찾았어요. IndoorGlare/Resources에 IndoorGlareVertexColor.shader가 있는지 확인해 주세요.");
                sh = Shader.Find("Sprites/Default");
            }
            visMat = new Material(sh);
            ApplyZTest();
            legendDgp = LegendTexture(PalDgp);
            legendGa = LegendTexture(PalGa);
        }

        void ApplyZTest()
        {
            if (visMat != null && visMat.HasProperty("_ZTest")) visMat.SetFloat("_ZTest", showThroughWalls ? 8f : 4f);   // 8 Always, 4 LessEqual
        }

        /// <summary>경우별 설정. 반응형(내부 루버)은 외측 유리 가시광 투과율을 한 번 더 곱해요</summary>
        void Configure(IndoorLouverCase lc)
        {
            eng.vt = visibleTransmittance * (lc == IndoorLouverCase.ResponsiveInner ? outerGlassVisibleTransmittance : 1f);
            eng.rhoIn = interiorReflectance;
            eng.bladeWidth = bladeWidthM;
            eng.bladeSpacing = bladeSpacingM;
        }

        // ---------------- 영역 ----------------

        /// <summary>루버 날로 층 바닥 범위를 찾아서 그 층 전체를 덮는 센서 영역을 만들어요(편집 모드에서도 돼요)</summary>
        public IndoorGlareZone CreateFloorZone(int floor)
        {
            var lk = link;
            if (lk == null || lk.units.Count == 0)
            {
                lk = new IndoorLouverLink();
                lk.Setup(louverController, louverRig, louverRootOverride, sunOverride, skipNameContains);
            }
            if (lk.units.Count == 0)
            {
                Debug.LogError("[Indoor] 루버 날을 못 찾아서 층 영역을 만들 수 없어요. IndoorGlareZone을 직접 놓아 주세요.");
                return null;
            }
            var sel = new List<IndoorLouverLink.Unit>();
            foreach (var u in lk.units) if (u.floor == floor) sel.Add(u);
            if (sel.Count == 0)
            {
                float y0 = lk.envelope.min.y + (floor - 1) * floorHeightM;
                foreach (var u in lk.units) if (u.center.y >= y0 && u.center.y < y0 + floorHeightM) sel.Add(u);
            }
            if (sel.Count == 0)
            {
                Debug.LogError("[Indoor] " + floor + "층 날을 못 찾았어요.");
                return null;
            }
            Bounds b = sel[0].bounds;
            foreach (var u in sel) b.Encapsulate(u.bounds);
            float inset = bladeCenterToGlassM + bladeWidthM * 0.5f + 0.1f;   // 날 바깥 끝 → 유리 안쪽
            var go = new GameObject(string.Format("IndoorGlareZone_{0:00}F", floor));
            go.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
            var z = go.AddComponent<IndoorGlareZone>();
            z.size = new Vector3(Mathf.Max(1f, b.size.x - 2f * inset), floorHeightM, Mathf.Max(1f, b.size.z - 2f * inset));
            if (z.size.x * z.size.z > 1000f) z.gridSizeM = 1.5f;   // 층 전체처럼 넓으면 센서를 성기게(준비 시간)
            Debug.Log(string.Format("[Indoor] {0}층 영역을 만들었어요: {1:0.0} × {2:0.0} m, 바닥 y = {3:0.00} (날 {4}장 기준)",
                floor, z.size.x, z.size.z, b.min.y, sel.Count));
            return z;
        }

        // ---------------- 준비(한 번): 모델 → 콜라이더 → 센서 → 보이는 방향 ----------------

        static bool UnderAny(Transform t, HashSet<Transform> set)
        {
            if (set.Count == 0) return false;
            for (var p = t; p != null; p = p.parent)
                if (set.Contains(p)) return true;
            return false;
        }

        bool IsGlassByName(MeshFilter mf, MeshRenderer mr)
        {
            if (glassKeywords == null || glassKeywords.Length == 0) return false;
            for (var p = mf.transform; p != null; p = p.parent)
                if (HasKeyword(p.name)) return true;
            foreach (var m in mr.sharedMaterials)
                if (m != null && HasKeyword(m.name)) return true;
            return false;
        }

        bool HasKeyword(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string low = s.ToLowerInvariant();
            foreach (var k in glassKeywords)
                if (!string.IsNullOrEmpty(k) && low.Contains(k.ToLowerInvariant())) return true;
            return false;
        }

        bool Slice()
        {
            return sw.ElapsedMilliseconds > msPerFrame;
        }

        IEnumerator BuildJob()
        {
            built = false;
            status = "준비 중: 모델 읽는 중";
            progress = 0f;
            ResultVersion++;
            yield return null;

            // 1) 영역
            zones.Clear();
            foreach (var z in FindObjectsOfType<IndoorGlareZone>()) if (z.isActiveAndEnabled) zones.Add(z);
            if (zones.Count == 0 && autoFloor > 0)
            {
                var z = CreateFloorZone(autoFloor);
                if (z != null)
                {
                    zones.Add(z);
                    Debug.Log("[Indoor] 씬에 IndoorGlareZone이 없어서 " + autoFloor + "층 전체 영역을 만들었어요(재생 중에만). " +
                              "편집 모드에서 IndoorGlareSim Inspector의 '층 영역 만들기'로 만들면 저장하고 크기를 고칠 수 있어요.");
                }
            }
            if (zones.Count == 0)
            {
                Fail("센서 영역이 없어요. IndoorGlareZone을 놓거나 Auto Floor를 정해 주세요.");
                yield break;
            }

            // 2) 모델 → 따로 만든 물리 씬
            if (scene != null) scene.Dispose();
            scene = IndoorGlareScene.Create();
            var unitSet = new HashSet<Transform>();
            foreach (var u in link.units) if (u.t != null) unitSet.Add(u.t);
            var excluded = new HashSet<Transform> { transform };
            foreach (var z in zones) excluded.Add(z.transform);
            if (excludeRoots != null) foreach (var t in excludeRoots) if (t != null) excluded.Add(t);
            var glassSet = new HashSet<Transform>();
            if (glassRoots != null) foreach (var t in glassRoots) if (t != null) glassSet.Add(t);
            var opaqueSet = new HashSet<Transform>();
            if (opaqueRoots != null) foreach (var t in opaqueRoots) if (t != null) opaqueSet.Add(t);

            var glassList = new List<MeshFilter>();
            var all = FindObjectsOfType<MeshFilter>();
            sw.Reset(); sw.Start();
            for (int i = 0; i < all.Length; i++)
            {
                var mf = all[i];
                if (mf == null) continue;
                var go = mf.gameObject;
                string sn = go.scene.name ?? "";
                if (sn.StartsWith("GlareReceivers_") || sn.StartsWith("IndoorGlare_")) continue;
                if (go.name.StartsWith("Glare_") || go.name.StartsWith("IndoorGlare_")) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;
                if (UnderAny(mf.transform, unitSet) || UnderAny(mf.transform, excluded)) continue;
                bool inGlass = UnderAny(mf.transform, glassSet);
                if (opaqueSet.Count > 0 && !inGlass && !UnderAny(mf.transform, opaqueSet)) continue;
                bool glass = glassSet.Count > 0 ? inGlass : IsGlassByName(mf, mr);
                if (scene.AddMesh(mf, glass) && glass) glassList.Add(mf);
                if (Slice())
                {
                    progress = 0.15f * i / all.Length;
                    status = "준비 중: 모델 " + Mathf.RoundToInt(progress * 100f) + "%";
                    yield return null;
                    sw.Reset(); sw.Start();
                }
            }
            scene.Sync();
            Debug.Log(string.Format("[Indoor] 모델: 유리 메시 {0}개, 막는 메시 {1}개, 삼각형 {2}개{3}", scene.glassMeshes, scene.opaqueMeshes, scene.triangles,
                glassSet.Count > 0 ? " (유리 = Glass Roots)" : " (유리 = 이름에 " + string.Join("/", glassKeywords) + ")"));
            if (scene.unreadable > 0)
                Debug.LogWarning("[Indoor] Read/Write가 꺼진 메시 " + scene.unreadable + "개. 에디터에서는 되지만 빌드(WebGL)에서는 빠져요. FBX Import Settings → Model → Read/Write를 켜 주세요.");
            if (scene.glassMeshes == 0)
            {
                Fail("유리를 못 찾았어요. Glass Roots에 유리 오브젝트(또는 유리 레이어 부모)를 넣어 주세요.");
                yield break;
            }

            // 3) 계산 엔진, 날 상자, 날 찾기
            eng = new IndoorGlareEngine();
            Configure(louverCase);
            eng.sp = IndoorSkyPatches.Build(skyResolution);
            eng.units = link.units;
            builtSkyRes = skyResolution;
            scene.BuildUnitHash(link.units);
            float yLo = float.MaxValue, yHi = float.MinValue;
            foreach (var z in zones) { yLo = Mathf.Min(yLo, z.FloorY); yHi = Mathf.Max(yHi, z.FloorY + z.size.y); }
            yLo -= floorHeightM; yHi += floorHeightM;   // 위아래 한 층까지
            for (int i = 0; i < link.units.Count; i++)
            {
                var u = link.units[i];
                if (u.maxY < yLo || u.minY > yHi) continue;
                scene.AddBlade(i, new Vector3(bladeThicknessM, Mathf.Max(0.1f, u.height), bladeWidthM));
            }

            // 4) 영역마다 유리 넓이·날(상호반사용)
            var log = new StringBuilder();
            for (int zi = 0; zi < zones.Count; zi++)
            {
                var z = zones[zi];
                var zd = new IndoorZoneData { roomArea = z.RoomArea };
                var raster = new IndoorGlassRaster(z, 1.5f);
                foreach (var mf in glassList) IndoorGlareScene.AddGlassArea(mf, z, raster);
                var ga = raster.Areas();
                for (int k = 0; k < 4; k++) zd.glassArea[k] = ga[k];
                if (Slice()) { yield return null; sw.Reset(); sw.Start(); }
                for (int i = 0; i < link.units.Count; i++)
                    if (z.Contains(link.units[i].center, 2f)) zd.sideUnits[(int)link.units[i].side].Add(i);
                eng.zones.Add(zd);
                log.AppendFormat("\n  {0}: {1:0} × {2:0} m, 유리 동 {3:0} 남 {4:0} 서 {5:0} 북 {6:0} m², 앞 날 {7}장",
                    z.name, z.size.x, z.size.z, zd.glassArea[0], zd.glassArea[1], zd.glassArea[2], zd.glassArea[3],
                    zd.sideUnits[0].Count + zd.sideUnits[1].Count + zd.sideUnits[2].Count + zd.sideUnits[3].Count);
            }

            // 5) 센서
            for (int zi = 0; zi < zones.Count; zi++)
            {
                var z = zones[zi];
                var dirs = z.Directions();
                foreach (var p in z.GridPoints())
                {
                    int pi = eng.points.Count;
                    eng.points.Add(new IndoorPoint { pos = p, zone = zi });
                    foreach (var d in dirs) eng.sensors.Add(new IndoorSensor { pt = pi, view = d });
                }
            }
            if (eng.sensors.Count == 0)
            {
                Fail("센서가 하나도 없어요. 영역 크기·Wall Offset을 확인해 주세요.");
                yield break;
            }
            if (eng.sensors.Count > 40000)
                Debug.LogWarning("[Indoor] 센서가 " + eng.sensors.Count + "개예요. 느리면 영역의 Grid Size를 키우거나 Directions를 줄여 주세요.");

            // 6) 점마다 보이는 방향(창 → 밖)
            int nP = eng.points.Count;
            Physics.queriesHitBackfaces = true;   // 한 면짜리 유리·벽(Rhino 메시)도 맞게
            sw.Reset(); sw.Start();
            for (int i = 0; i < nP; i++)
            {
                eng.TracePoint(eng.points[i], scene);
                if (Slice())
                {
                    progress = 0.15f + 0.75f * i / nP;
                    status = "준비 중: 보이는 하늘 " + Mathf.RoundToInt(progress * 100f) + "%";
                    Physics.queriesHitBackfaces = backfacesDefault;
                    yield return null;
                    Physics.queriesHitBackfaces = true;
                    sw.Reset(); sw.Start();
                }
            }
            Physics.queriesHitBackfaces = backfacesDefault;

            // 7) 센서 계수
            for (int s = 0; s < eng.sensors.Count; s++)
            {
                var se = eng.sensors[s];
                eng.SensorCoeffs(se, eng.points[se.pt]);
                if (Slice())
                {
                    progress = 0.9f + 0.1f * s / eng.sensors.Count;
                    status = "준비 중: 시선 " + Mathf.RoundToInt(progress * 100f) + "%";
                    yield return null;
                    sw.Reset(); sw.Start();
                }
            }

            noWindowPoints = 0;
            foreach (var p in eng.points) if (p.VisibleCount == 0) noWindowPoints++;
            BuildWedges();
            resDgp = resEv = resGA = resGlareH = resMaxDgp = null;
            Debug.Log(string.Format("[Indoor] 센서 {0}개 (점 {1}개, 창이 안 보이는 점 {2}개), 날 상자 {3}개, 천공 표본 {4}개{5}",
                eng.sensors.Count, nP, noWindowPoints, scene.BladeBoxCount, eng.sp.SampDir.Length, log));
            if (noWindowPoints == nP)
                Debug.LogWarning("[Indoor] 어떤 점에서도 창 밖이 안 보여요. 유리 찾기(Glass Roots)와 영역 위치를 확인해 주세요.");

            built = true;
            dirty = true;
            status = "준비 완료";
            progress = 1f;
            job = null;
            ResultVersion++;
        }

        void Fail(string msg)
        {
            Debug.LogError("[Indoor] " + msg);
            status = msg;
            buildFailed = true;
            job = null;
            ResultVersion++;
        }

        // ---------------- 매 프레임 ----------------

        void Update()
        {
            if (!ready) return;
            if (mode == IndoorMode.Off)
            {
                SetVisible(false);
                return;
            }
            if (job != null) return;
            if (!built)
            {
                if (!buildFailed) job = StartCoroutine(BuildJob());
                return;
            }
            if (link.OtherSimBusy()) return;   // 반사광이 계산 중이면(하루 누적이면 시각이 계속 바뀌어요) 기다려요
            if (skyResolution != builtSkyRes) { Rebuild(); return; }
            if (sky == IndoorSkyModel.Epw && epw == null && !epwLoading && epwError == null && !string.IsNullOrEmpty(epwFileName))
                StartCoroutine(LoadEpw());

            if (mode == IndoorMode.Instant)
            {
                if (dirty || Time.unscaledTime >= nextCheck)
                {
                    nextCheck = Time.unscaledTime + 0.2f;
                    long sig = InstantSignature();
                    if (dirty || sig != instantSig)
                    {
                        instantSig = sig;
                        dirty = false;
                        job = StartCoroutine(InstantJob());
                        return;
                    }
                }
            }
            else
            {
                if (!link.CanDrive)
                {
                    status = "하루 계산 불가(" + IndoorLouverLink.ControllerTypeName + "의 hour·externalControl 필요)";
                    return;
                }
                if (!link.ControllerReady()) return;
                string sig = DailySignature();
                if (dirty || sig != dailySig)
                {
                    dailySig = sig;
                    dirty = false;
                    job = StartCoroutine(SweepJob(new[] { louverCase }, false));
                    return;
                }
            }
            UpdateCamera();
        }

        long InstantSignature()
        {
            unchecked
            {
                return link.StateSignature(5) * 31 + (int)louverCase * 7 + (int)sky * 131 + (epw != null ? 1000 : 0) +
                       Mathf.RoundToInt(outerGlassVisibleTransmittance * 1000f) * 100003L;
            }
        }

        string DailySignature()
        {
            return link.DaySignature() + "|" + louverCase + "|" + sky + "|" + allRepresentativeDays + "|" + occupiedStart + "-" + occupiedEnd +
                   "|" + dgpThreshold + "|" + (epw != null);
        }

        // ---------------- 하늘 ----------------

        IndoorSkyState MakeSky(string day, float hour, bool up, Vector3 sd)
        {
            var s = new IndoorSkyState(eng.sp.Count);
            s.SunUp = up;
            s.Sun = up ? sd.normalized : Vector3.down;
            s.AltDeg = up ? Mathf.Asin(Mathf.Clamp(s.Sun.y, -1f, 1f)) * Mathf.Rad2Deg : -90f;
            int mo, dom;
            bool known = IndoorGlareCore.DayToDate(day, out mo, out dom);
            int doy = IndoorGlareCore.DayOfYear(mo, dom);
            var model = sky;
            if (model == IndoorSkyModel.Epw && epw == null)
            {
                model = IndoorSkyModel.Clear;
                s.Note = "EPW 없음 → 맑은 하늘";
            }
            if (model == IndoorSkyModel.Epw)
            {
                int row = epw.Row(mo, dom, hour);
                if (up)
                {
                    s.DnLux = epw.DniLux[row];
                    s.DhLux = epw.DhiLux[row];
                    float eps = IndoorGlareCore.PerezEpsilon(epw.DhiW[row], epw.DniW[row], (90f - s.AltDeg) * Mathf.Deg2Rad);
                    s.ClearWeight = IndoorGlareCore.ClearWeightFromEpsilon(eps);
                }
                s.Note = string.Format("EPW {0}/{1} {2}시 행", epw.Month[row], epw.Day[row], epw.Hour[row]) + (known ? "" : " (날짜를 몰라 6/21)");
            }
            else
            {
                float dn = 0f, dh = 0f, w = 0f;
                if (up) IndoorGlareCore.ModelSky(model, s.AltDeg, doy, clearSkyExtinction, out dn, out dh, out w);
                s.DnLux = dn; s.DhLux = dh; s.ClearWeight = w;
                if (s.Note.Length == 0) s.Note = model == IndoorSkyModel.PartlyCloudy ? "구름 조금(IES)" : model == IndoorSkyModel.Overcast ? "흐림(IES)" : "맑음(IES)";
            }
            IndoorGlareCore.FillSky(s, eng.sp, groundReflectance, bladeReflectance, contextReflectance);
            return s;
        }

        static bool IsOpen(IndoorHit h) { return h == IndoorHit.Sky || h == IndoorHit.Ground; }

        // ---------------- 계산 ----------------

        /// <summary>
        /// 한 시각을 계산해요. 부르기 전에 link.ReadAngles()로 날 각도를 읽어 두세요.
        /// cases마다 outDgp[c][센서]를 채워요(outEv는 null이어도 돼요).
        /// </summary>
        IEnumerator StepCompute(IndoorSkyState skyState, IndoorLouverCase[] cases, float[][] outDgp, float[][] outEv, float p0, float span, string what)
        {
            int nS = eng.sensors.Count, nP = eng.points.Count;
            if (!skyState.Lit)
            {
                for (int c = 0; c < cases.Length; c++)
                    for (int s = 0; s < nS; s++) { outDgp[c][s] = 0f; if (outEv != null) outEv[c][s] = 0f; }
                yield break;
            }
            Physics.queriesHitBackfaces = true;
            bool sunCheck = skyState.SunUp && skyState.DnLux > 0f;
            int posed = -1;   // 날 상자 자세: -1 아직, 0 실제 각도, 1 모두 열림
            int work = nP * (1 + cases.Length) + nS * cases.Length, done = 0;

            // 해: 유리·벽·주변 건물(날 빼고)로 먼저 확인(경우마다 같아요)
            for (int i = 0; i < nP; i++, done++)
            {
                var p = eng.points[i];
                Vector3 ex;
                p.sunStatic = sunCheck && p.VisibleCount > 0 && IsOpen(scene.TraceStatic(p.pos, skyState.Sun, out ex));
                if (Slice())
                {
                    progress = p0 + span * done / work;
                    status = what + Mathf.RoundToInt(progress * 100f) + "%";
                    Physics.queriesHitBackfaces = backfacesDefault;
                    yield return null;
                    Physics.queriesHitBackfaces = true;
                    sw.Reset(); sw.Start();
                }
            }

            for (int c = 0; c < cases.Length; c++)
            {
                var lc = cases[c];
                Configure(lc);
                eng.ZoneIllum(skyState, lc);
                bool blades = lc != IndoorLouverCase.NoLouver && scene.BladeBoxCount > 0;
                int want = lc == IndoorLouverCase.AllOpen ? 1 : 0;
                if (blades && posed != want)
                {
                    scene.PoseBlades(want == 1);
                    scene.Sync();
                    posed = want;
                }
                for (int i = 0; i < nP; i++, done++)
                {
                    var p = eng.points[i];
                    bool sv = p.sunStatic && (!blades || !scene.BladeBlocked(p.pos, skyState.Sun));
                    eng.PointStep(p, lc, sv);
                    if (Slice())
                    {
                        progress = p0 + span * done / work;
                        status = what + Mathf.RoundToInt(progress * 100f) + "%";
                        Physics.queriesHitBackfaces = backfacesDefault;
                        yield return null;
                        Physics.queriesHitBackfaces = true;
                        sw.Reset(); sw.Start();
                    }
                }
                for (int s = 0; s < nS; s++, done++)
                {
                    var se = eng.sensors[s];
                    var p = eng.points[se.pt];
                    float ev;
                    outDgp[c][s] = eng.SensorDgp(se, p, skyState, eng.zones[p.zone].er, out ev);
                    if (outEv != null) outEv[c][s] = ev;
                    if (Slice())
                    {
                        progress = p0 + span * done / work;
                        status = what + Mathf.RoundToInt(progress * 100f) + "%";
                        Physics.queriesHitBackfaces = backfacesDefault;
                        yield return null;
                        Physics.queriesHitBackfaces = true;
                        sw.Reset(); sw.Start();
                    }
                }
            }
            Physics.queriesHitBackfaces = backfacesDefault;
        }

        static void SunAngles(Vector3 d, out float alt, out float az)
        {
            alt = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
            az = Mathf.Repeat(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 360f);
        }

        /// <summary>현재 시각: 화면의 해와 날 각도 그대로</summary>
        IEnumerator InstantJob()
        {
            int nS = eng.sensors.Count;
            Vector3 sd;
            bool up = link.SunUp(out sd);
            link.ReadAngles();
            float hour = 12f;
            link.TryHour(out hour);
            var skyState = MakeSky(link.DayName(), hour, up, sd);
            var dg = new[] { new float[nS] };
            var ev = new[] { new float[nS] };
            sw.Reset(); sw.Start();
            var it = StepCompute(skyState, new[] { louverCase }, dg, ev, 0f, 1f, "계산 중 ");
            while (it.MoveNext()) yield return it.Current;

            resDgp = dg[0];
            resEv = ev[0];
            shownDaily = false;
            shownCase = louverCase;
            float alt, az;
            SunAngles(skyState.Sun, out alt, out az);
            shownLabel = link.TimeLabel() + (up ? string.Format(" · 고도 {0:0}° 방위 {1:0}°", alt, az) : " · 해 없음") + "\n" + SkyLabel(skyState);
            Show();
            job = null;
        }

        string SkyLabel(IndoorSkyState s)
        {
            if (!s.Lit) return "하늘: 빛 없음";
            return string.Format("하늘: {0} · 직달 {1:0.0} klx, 확산 {2:0.0} klx", s.Note, s.DnLux / 1000f, s.DhLux / 1000f);
        }

        List<string> SweepDays()
        {
            var days = new List<string>();
            if (allRepresentativeDays && representativeDays != null)
                foreach (var d in representativeDays) if (!string.IsNullOrEmpty(d)) days.Add(d.Trim());
            if (days.Count == 0) days.Add(link.DayName());
            return days;
        }

        /// <summary>
        /// 하루 GA: 컨트롤러 시각을 재실 시간 동안 10분씩 돌려서 해·루버가 실제로 움직인 상태로 DGP를 구하고,
        /// GA = 재실 시각 중 DGP &lt; 기준인 비율. 해가 없는 시각은 글레어 없음으로 세요(Glare2와 같아요).
        /// </summary>
        IEnumerator SweepJob(IndoorLouverCase[] cases, bool compare)
        {
            var days = SweepDays();
            int nS = eng.sensors.Count, nC = cases.Length;
            var glare = new int[nC][];
            var maxD = new float[nC][];
            var dg = new float[nC][];
            for (int c = 0; c < nC; c++) { glare[c] = new int[nS]; maxD[c] = new float[nS]; dg[c] = new float[nS]; }

            while (!link.BeginDrive())
            {
                status = "반사광이 계산 중이라 끝나길 기다려요";
                yield return null;
            }
            const int dt = 10;
            int m0 = occupiedStart * 60, m1 = occupiedEnd * 60;
            int perDay = Mathf.Max(1, (m1 - m0 + dt - 1) / dt);
            int total = days.Count * perDay, done = 0, occSteps = 0, litSteps = 0;
            string what = compare ? "4가지 비교 " : "하루 GA ";
            foreach (var day in days)
            {
                link.DriveDay(day);
                for (int m = m0; m < m1; m += dt, done++)
                {
                    link.DriveTo(m / 60f);
                    yield return null;   // 다음 프레임에 컨트롤러가 이 시각으로 해와 루버를 돌려요
                    if (!link.StillAt(day, m / 60f))
                    {
                        // 다른 것이 날·시각을 바꿨어요(반사광 하루 누적, 웹 등) → 결과가 섞이지 않게 멈춰요
                        StopForConflict();
                        yield break;
                    }
                    sw.Reset(); sw.Start();
                    Vector3 sd;
                    bool up = link.SunUp(out sd);
                    link.ReadAngles();
                    var skyState = MakeSky(day, m / 60f, up, sd);
                    occSteps++;
                    if (skyState.Lit) litSteps++;
                    var it = StepCompute(skyState, cases, dg, null, (float)done / total, 1f / total, what);
                    while (it.MoveNext()) yield return it.Current;
                    for (int c = 0; c < nC; c++)
                        for (int s = 0; s < nS; s++)
                        {
                            float v = dg[c][s];
                            if (v >= dgpThreshold) glare[c][s]++;
                            if (v > maxD[c][s]) maxD[c][s] = v;
                        }
                }
            }
            link.EndDrive();
            yield return null;   // 컨트롤러가 원래 날·시각으로 돌아갈 때까지
            yield return null;
            instantSig = long.MinValue;

            var ga = new float[nC][];
            var gh = new float[nC][];
            for (int c = 0; c < nC; c++)
            {
                ga[c] = new float[nS];
                gh[c] = new float[nS];
                for (int s = 0; s < nS; s++)
                {
                    ga[c][s] = occSteps > 0 ? 100f * (occSteps - glare[c][s]) / occSteps : 100f;
                    gh[c][s] = glare[c][s] * dt / 60f;
                }
            }
            string label = string.Format("{0} · 재실 {1}~{2}시 · {3}분 간격 · {4}개 시각(낮 {5}개)",
                string.Join("·", days.ToArray()), occupiedStart, occupiedEnd, dt, occSteps, litSteps);
            int cur = System.Array.IndexOf(cases, louverCase);
            if (mode == IndoorMode.Daily && cur >= 0)
            {
                resGA = ga[cur]; resGlareH = gh[cur]; resMaxDgp = maxD[cur];
                shownDaily = true;
                shownCase = louverCase;
                shownOccSteps = occSteps;
                shownLabel = label + "\n하늘: " + (sky == IndoorSkyModel.Epw && epw == null ? "EPW 없음 → 맑은 하늘" : sky.ToString());
                Show();
                dailySig = DailySignature();
            }
            if (compare) BuildCompare(cases, ga, gh, maxD, label, occSteps);
            status = compare ? "비교 완료" : "완료";
            ResultVersion++;
            job = null;
        }

        /// <summary>
        /// 하루 계산 중에 다른 것이 날·시각을 바꿨을 때.
        ///  - 반사광이 계산 중이면: 그쪽이 끝난 뒤 원래 날·시각으로 되돌리고 다시 계산해요
        ///  - 아니면(웹·사용자가 새 시각을 정함): 그 시각을 지키고 멈춰요. 다시 계산 버튼으로 다시 해요
        /// </summary>
        void StopForConflict()
        {
            if (link.OtherSimBusy())
            {
                status = "반사광 계산과 겹쳐서 멈췄어요. 그쪽이 끝나면 시각을 되돌리고 다시 계산해요";
                job = StartCoroutine(RestoreAfterOther());
            }
            else
            {
                link.Release();
                status = "다른 곳(웹 등)에서 날짜·시각을 바꿔서 하루 계산을 멈췄어요. '다시 계산'으로 다시 해요";
                job = null;
            }
            ResultVersion++;
        }

        IEnumerator RestoreAfterOther()
        {
            while (link.OtherSimBusy()) yield return null;
            link.EndDrive();
            yield return null;
            dirty = true;
            job = null;
        }

        void BuildCompare(IndoorLouverCase[] cases, float[][] ga, float[][] gh, float[][] maxD, string label, int occSteps)
        {
            compareLines.Clear();
            compareLines.Add("4가지 비교: " + label);
            var json = new List<IndoorCompareJson>();
            var logb = new StringBuilder("[Indoor] 4가지 비교 (" + label + ")\n");
            for (int c = 0; c < cases.Length; c++)
            {
                double sGa = 0, sGh = 0, sMx = 0;
                float mn = 100f;
                int under = 0, n = 0;
                for (int s = 0; s < ga[c].Length; s++)
                {
                    if (eng.points[eng.sensors[s].pt].VisibleCount == 0) continue;
                    n++;
                    sGa += ga[c][s]; sGh += gh[c][s]; sMx += maxD[c][s];
                    if (ga[c][s] < mn) mn = ga[c][s];
                    if (ga[c][s] < 95f) under++;
                }
                if (n == 0) n = 1;
                string line = string.Format("{0}: GA 평균 {1:0.0}% · 최소 {2:0.0}% · GA<95% 센서 {3:0.0}% · 글레어 평균 {4:0.0}시간",
                    CaseName(cases[c]), sGa / n, mn, 100.0 * under / n, sGh / n);
                compareLines.Add(line);
                logb.AppendLine("  " + line);
                json.Add(new IndoorCompareJson
                {
                    louverCase = cases[c].ToString(), gaMean = (float)(sGa / n), gaMin = mn,
                    gaUnder95Pct = (float)(100.0 * under / n), meanGlareHours = (float)(sGh / n), meanMaxDgp = (float)(sMx / n)
                });
            }
            compareJson = json.ToArray();
            Debug.Log(logb.ToString());
            PublishJson();
        }

        static string CaseName(IndoorLouverCase lc)
        {
            switch (lc)
            {
                case IndoorLouverCase.ResponsiveOuter: return "반응형(외부 루버)";
                case IndoorLouverCase.AllOpen: return "모두 열림(0°)";
                case IndoorLouverCase.NoLouver: return "루버 없음";
                default: return "반응형(내부 루버)";
            }
        }

        // 패널 버튼 순서 = IndoorLouverCase 순서 (반사광 패널과 같아요)
        static readonly string[] CaseLabels = { "반응형(내부 루버)", "반응형(외부 루버)", "모두 열림", "루버 없음" };
        static readonly string[] CaseKeys = { "inner", "outer", "open", "none" };

        // ---------------- 카메라 시점 ----------------

        int ZoneOf(Vector3 p)
        {
            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] == null) continue;
                if (zones[i].Contains(p, 0f)) return i;
                float d = (zones[i].Center - p).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        void UpdateCamera()
        {
            if (!cameraDgp || !built || eng == null) { camText = ""; return; }
            if (Time.unscaledTime < camNext) return;
            camNext = Time.unscaledTime + 0.25f;
            var cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null) { camText = "카메라 시점: 카메라가 없어요(View Camera)"; return; }
            Vector3 pos = cam.transform.position, dir = cam.transform.forward;
            bool moved = camPt == null || (pos - camPos).sqrMagnitude > 0.01f;
            bool turned = camSensor == null || Vector3.Angle(dir, camDir) > 2f;
            long sig = InstantSignature();
            if (!moved && !turned && sig == camSig) return;
            camSig = sig;
            Configure(louverCase);
            Physics.queriesHitBackfaces = true;
            if (moved)
            {
                camPt = new IndoorPoint { pos = pos, zone = ZoneOf(pos) };
                eng.TracePoint(camPt, scene);
                camPos = pos;
                turned = true;
            }
            if (turned)
            {
                camSensor = new IndoorSensor { view = dir };
                eng.SensorCoeffs(camSensor, camPt);
                camDir = dir;
            }
            Vector3 sd;
            bool up = link.SunUp(out sd);
            link.ReadAngles();
            float hour = 12f;
            link.TryHour(out hour);
            var s = MakeSky(link.DayName(), hour, up, sd);
            eng.ZoneIllum(s, louverCase);
            bool sv = false;
            if (s.Lit && s.SunUp && s.DnLux > 0f && camPt.VisibleCount > 0)
            {
                Vector3 ex;
                sv = IsOpen(scene.TraceStatic(pos, s.Sun, out ex));
                if (sv && louverCase != IndoorLouverCase.NoLouver && scene.BladeBoxCount > 0)
                {
                    scene.PoseBlades(louverCase == IndoorLouverCase.AllOpen);
                    scene.Sync();
                    sv = !scene.BladeBlocked(pos, s.Sun);
                }
            }
            eng.PointStep(camPt, louverCase, sv);
            float ev;
            float d = eng.SensorDgp(camSensor, camPt, s, eng.zones.Count > 0 ? eng.zones[camPt.zone].er : 0f, out ev);
            Physics.queriesHitBackfaces = backfacesDefault;
            camDgpValue = d;
            camText = camPt.VisibleCount == 0
                ? "카메라 시점: 창 밖이 안 보여요(실내가 아니거나 유리를 못 찾음)"
                : string.Format("카메라 시점 DGP {0:0.00} ({1}) · 눈 조도 {2:N0} lx · 해 {3}", d, IndoorGlareCore.DgpClass(d), ev, sv ? "보임" : "안 보임");
            ResultVersion++;
        }

        // ---------------- 표시 ----------------

        static Vector3 Polar(float a, float r) { return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r); }

        /// <summary>Glare1처럼 센서 하나 = 쐐기 하나(눈높이 수평 디스크를 방향 수로 나눈 조각)</summary>
        void BuildWedges()
        {
            if (wedgeGo == null)
            {
                wedgeGo = new GameObject("IndoorGlare_Sensors");
                wedgeMesh = new Mesh { name = "IndoorGlare_Sensors" };
                wedgeMesh.MarkDynamic();
                wedgeGo.AddComponent<MeshFilter>().sharedMesh = wedgeMesh;
                var mr = wedgeGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = visMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            int nS = eng.sensors.Count;
            var v = new List<Vector3>(nS * 4);
            var idx = new int[nS * 6];
            colors = new Color32[nS * 4];
            for (int s = 0; s < nS; s++)
            {
                var se = eng.sensors[s];
                var p = eng.points[se.pt];
                var z = zones[p.zone];
                int n = Mathf.Max(1, z.directions);
                float r = 0.42f * z.gridSizeM, half = Mathf.PI / n * 0.88f;
                float th = Mathf.Atan2(se.view.z, se.view.x);
                Vector3 c = p.pos;
                int b = v.Count;
                v.Add(c + Polar(th, r * 0.12f));
                v.Add(c + Polar(th - half, r));
                v.Add(c + Polar(th, r));
                v.Add(c + Polar(th + half, r));
                idx[s * 6] = b; idx[s * 6 + 1] = b + 1; idx[s * 6 + 2] = b + 2;
                idx[s * 6 + 3] = b; idx[s * 6 + 4] = b + 2; idx[s * 6 + 5] = b + 3;
                for (int k = 0; k < 4; k++) colors[b + k] = Grey;
            }
            wedgeMesh.Clear();
            wedgeMesh.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            wedgeMesh.SetVertices(v);
            wedgeMesh.SetIndices(idx, MeshTopology.Triangles, 0);
            wedgeMesh.colors32 = colors;
            wedgeMesh.RecalculateBounds();
        }

        static Color32 ColorAt(float v, float lo, float hi, Color32[] pal)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return Grey;
            float t = Mathf.Clamp01((v - lo) / Mathf.Max(1e-6f, hi - lo));
            float x = t * (pal.Length - 1);
            int i = Mathf.Min(Mathf.FloorToInt(x), pal.Length - 2);
            return Color32.Lerp(pal[i], pal[i + 1], x - i);
        }

        static Texture2D LegendTexture(Color32[] pal)
        {
            var tex = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int x = 0; x < 256; x++) tex.SetPixel(x, 0, ColorAt(x / 255f, 0f, 1f, pal));
            tex.Apply();
            return tex;
        }

        void SetVisible(bool on)
        {
            if (wedgeGo != null && wedgeGo.activeSelf != on) wedgeGo.SetActive(on);
        }

        void Show()
        {
            Recolor();
            status = "완료";
            PublishJson();
            ResultVersion++;
        }

        float[] CurrentValues(out float lo, out float hi, out Color32[] pal, out string unit)
        {
            lo = dgpScaleMin; hi = dgpScaleMax; pal = PalDgp; unit = "DGP";
            if (!shownDaily) return resDgp;
            switch (dailyMetric)
            {
                case IndoorDailyMetric.GlareHours:
                    float mx = 1f;
                    if (resGlareH != null) foreach (var x in resGlareH) if (x > mx) mx = x;
                    lo = 0f; hi = mx; unit = "글레어 시간";
                    return resGlareH;
                case IndoorDailyMetric.MaxDgp:
                    unit = "최대 DGP";
                    return resMaxDgp;
                default:
                    lo = 0f; hi = 100f; pal = PalGa; unit = "GA %";
                    return resGA;
            }
        }

        void Recolor()
        {
            if (eng == null || wedgeMesh == null || colors == null) return;
            float lo, hi;
            Color32[] pal;
            string unit;
            var vals = CurrentValues(out lo, out hi, out pal, out unit);
            if (vals == null || vals.Length != eng.sensors.Count) return;
            for (int s = 0; s < vals.Length; s++)
            {
                bool sees = eng.points[eng.sensors[s].pt].VisibleCount > 0;
                Color32 c = sees ? ColorAt(vals[s], lo, hi, pal) : Grey;
                for (int k = 0; k < 4; k++) colors[s * 4 + k] = c;
            }
            wedgeMesh.colors32 = colors;
            shownLo = lo; shownHi = hi; shownUnit = unit;
            legendIsGa = pal == PalGa;
            shownStats = Stats();
            SetVisible(true);
        }

        string Stats()
        {
            int n = 0;
            if (!shownDaily)
            {
                if (resDgp == null) return "";
                double sum = 0;
                float mx = 0f;
                var cls = new int[4];
                for (int s = 0; s < resDgp.Length; s++)
                {
                    if (eng.points[eng.sensors[s].pt].VisibleCount == 0) continue;
                    float d = resDgp[s];
                    n++; sum += d; if (d > mx) mx = d;
                    cls[d < 0.35f ? 0 : d < 0.40f ? 1 : d < 0.45f ? 2 : 3]++;
                }
                if (n == 0) return "창이 보이는 센서가 없어요";
                return string.Format("평균 DGP {0:0.00} · 최대 {1:0.00} ({2})\n인지 불가 {3:0}% · 인지 가능 {4:0}% · 방해 {5:0}% · 견디기 어려움 {6:0}%",
                    sum / n, mx, IndoorGlareCore.DgpClass(mx), 100f * cls[0] / n, 100f * cls[1] / n, 100f * cls[2] / n, 100f * cls[3] / n);
            }
            if (resGA == null) return "";
            double sGa = 0, sGh = 0;
            float mn = 100f;
            int under = 0;
            for (int s = 0; s < resGA.Length; s++)
            {
                if (eng.points[eng.sensors[s].pt].VisibleCount == 0) continue;
                n++; sGa += resGA[s]; sGh += resGlareH[s];
                if (resGA[s] < mn) mn = resGA[s];
                if (resGA[s] < 95f) under++;
            }
            if (n == 0) return "창이 보이는 센서가 없어요";
            return string.Format("GA 평균 {0:0.0}% · 최소 {1:0.0}% · GA<95% 센서 {2:0.0}%\n글레어(DGP ≥ {3:0.00}) 평균 {4:0.0}시간 / 재실 {5:0.#}시간",
                sGa / n, mn, 100.0 * under / n, dgpThreshold, sGh / n, shownOccSteps * 10 / 60f);
        }

        // ---------------- 웹·외부에서 부르는 함수 ----------------

        /// <summary>"off", "instant", "daily"</summary>
        public void SetMode(string m)
        {
            switch ((m ?? "").ToLowerInvariant())
            {
                case "off": mode = IndoorMode.Off; break;
                case "daily": mode = IndoorMode.Daily; break;
                default: mode = IndoorMode.Instant; break;
            }
            CancelJob();
            dirty = true;
        }

        /// <summary>"inner"(반응형 내부 루버), "outer"(반응형 외부 루버), "open", "none". "responsive"는 inner로 봐요(반사광과 같아요)</summary>
        public void SetLouverCase(string c)
        {
            switch ((c ?? "").ToLowerInvariant())
            {
                case "outer": case "responsiveouter": case "responsive_outer": louverCase = IndoorLouverCase.ResponsiveOuter; break;
                case "open": case "allopen": louverCase = IndoorLouverCase.AllOpen; break;
                case "none": case "nolouver": louverCase = IndoorLouverCase.NoLouver; break;
                default: louverCase = IndoorLouverCase.ResponsiveInner; break;
            }
            CancelJob();
            dirty = true;
        }

        /// <summary>"clear", "partly", "overcast", "epw"</summary>
        public void SetSky(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "partly": case "partlycloudy": sky = IndoorSkyModel.PartlyCloudy; break;
                case "overcast": sky = IndoorSkyModel.Overcast; break;
                case "epw": sky = IndoorSkyModel.Epw; break;
                default: sky = IndoorSkyModel.Clear; break;
            }
            CancelJob();
            dirty = true;
        }

        /// <summary>결과만 다시 계산(모델·센서는 그대로)</summary>
        public void Recompute()
        {
            CancelJob();
            dirty = true;
        }

        /// <summary>모델·센서·보이는 방향부터 다시 준비(영역을 옮기거나 모델을 바꾼 뒤)</summary>
        public void Rebuild()
        {
            StopJobs();
            built = false;
            buildFailed = false;
            camPt = null;
            camSensor = null;
            dirty = true;
        }

        /// <summary>하루를 루버 없음 / 모두 열림 / 반응형(외부 루버) / 반응형(내부 루버)으로 한 번에 계산해요</summary>
        public void RunCompare()
        {
            if (!ready || !built) return;
            if (!link.CanDrive || !link.ControllerReady())
            {
                status = "비교 불가(루버 컨트롤러 필요)";
                return;
            }
            CancelJob();
            if (job != null)
            {
                status = "반사광 계산이 끝나길 기다리는 중이에요. 잠시 뒤 다시 눌러 주세요";
                return;
            }
            job = StartCoroutine(SweepJob(new[] { IndoorLouverCase.NoLouver, IndoorLouverCase.AllOpen, IndoorLouverCase.ResponsiveOuter, IndoorLouverCase.ResponsiveInner }, true));
        }

        /// <summary>지금 계산을 멈추고 다시 계산하게 해요. 준비(BuildJob) 중이면 준비는 그대로 두고 끝난 뒤 계산해요</summary>
        void CancelJob()
        {
            dirty = true;
            if (!built) return;
            StopJobs();
            status = "취소됨";
        }

        void StopJobs()
        {
            StopAllCoroutines();
            Physics.queriesHitBackfaces = backfacesDefault;
            job = null;
            epwLoading = false;
            if (link != null && link.IsDriving)
            {
                // 반사광이 하루 계산 중이면 그쪽이 끝난 뒤에 되돌려요(지금 되돌리면 그쪽 결과가 섞여요)
                if (link.OtherSimBusy()) job = StartCoroutine(RestoreAfterOther());
                else link.EndDrive();
            }
        }

        IEnumerator LoadEpw()
        {
            epwLoading = true;
            string path = Path.Combine(Application.streamingAssetsPath, epwFileName);
            string url = path.Contains("://") ? path : "file://" + path;
            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    epwError = req.error;
                    Debug.LogWarning("[Indoor] EPW를 읽을 수 없어요(맑은 하늘로 계산): " + url + " (" + req.error + ")");
                }
                else
                {
                    try
                    {
                        epw = IndoorEpw.Parse(req.downloadHandler.text);
                        Debug.Log(string.Format("[Indoor] EPW {0}: {1}행, 위도 {2:0.00}", epw.City, epw.Count, epw.Lat));
                        dirty = true;
                    }
                    catch (System.Exception e)
                    {
                        epwError = e.Message;
                        Debug.LogWarning("[Indoor] EPW 형식 오류: " + e.Message);
                    }
                }
            }
            epwLoading = false;
        }

        // ---------------- 결과 JSON / CSV ----------------

        void PublishJson()
        {
            if (eng == null) return;
            var j = new IndoorGlareResultJson
            {
                mode = shownDaily ? "daily" : "instant",
                louverCase = shownCase.ToString(),
                sky = sky.ToString(),
                label = shownLabel,
                sensors = eng.sensors.Count,
                points = eng.points.Count,
                pointsNoWindow = noWindowPoints,
                threshold = dgpThreshold,
                cameraDgp = cameraDgp ? camDgpValue : -1f,
                compare = compareJson
            };
            int n = 0;
            if (!shownDaily && resDgp != null)
            {
                j.dgpClassPct = new float[4];
                for (int s = 0; s < resDgp.Length; s++)
                {
                    if (eng.points[eng.sensors[s].pt].VisibleCount == 0) continue;
                    float d = resDgp[s];
                    n++; j.meanDgp += d; if (d > j.maxDgp) j.maxDgp = d;
                    j.dgpClassPct[d < 0.35f ? 0 : d < 0.40f ? 1 : d < 0.45f ? 2 : 3]++;
                }
                if (n > 0) { j.meanDgp /= n; for (int k = 0; k < 4; k++) j.dgpClassPct[k] *= 100f / n; }
            }
            if (shownDaily && resGA != null)
            {
                j.gaMin = 100f;
                for (int s = 0; s < resGA.Length; s++)
                {
                    if (eng.points[eng.sensors[s].pt].VisibleCount == 0) continue;
                    n++; j.gaMean += resGA[s]; j.meanGlareHours += resGlareH[s];
                    if (resGA[s] < j.gaMin) j.gaMin = resGA[s];
                    if (resGA[s] < 95f) j.gaUnder95Pct++;
                }
                if (n > 0) { j.gaMean /= n; j.meanGlareHours /= n; j.gaUnder95Pct *= 100f / n; }
            }
            lastResultJson = JsonUtility.ToJson(j);
            if (onResultJson != null) onResultJson.Invoke(lastResultJson);
        }

        public void ExportCsv()
        {
    #if UNITY_WEBGL && !UNITY_EDITOR
            Debug.LogWarning("[Indoor] WebGL에서는 파일 저장이 안 돼요. 에디터나 PC 빌드에서 저장하세요.");
    #else
            if (eng == null || eng.sensors.Count == 0) return;
            string dir = Application.isEditor ? Path.Combine(Application.dataPath, "../GlareResults") : Path.Combine(Application.persistentDataPath, "GlareResults");
            Directory.CreateDirectory(dir);
            string stamp = "indoor_" + (shownDaily ? "daily_" : "instant_") + shownCase + "_" + System.DateTime.Now.ToString("MMdd_HHmmss");
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("sensor,zone,x,y,z,dx,dy,dz,sees_window,dgp,ev_lx,ga_pct,glare_h,max_dgp");
            for (int s = 0; s < eng.sensors.Count; s++)
            {
                var se = eng.sensors[s];
                var p = eng.points[se.pt];
                sb.AppendLine(string.Format(inv, "{0},{1},{2:0.000},{3:0.000},{4:0.000},{5:0.000},{6:0.000},{7:0.000},{8},{9},{10},{11},{12},{13}",
                    s, zones[p.zone] != null ? zones[p.zone].name.Replace(",", " ") : "?", p.pos.x, p.pos.y, p.pos.z, se.view.x, se.view.y, se.view.z, p.VisibleCount > 0 ? 1 : 0,
                    Fmt(resDgp, s, "0.000"), Fmt(resEv, s, "0"), Fmt(resGA, s, "0.0"), Fmt(resGlareH, s, "0.00"), Fmt(resMaxDgp, s, "0.000")));
            }
            string path = Path.Combine(dir, "glare_" + stamp + ".csv");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Debug.Log("[Indoor] 저장했어요: " + Path.GetFullPath(path));
            status = "CSV 저장함";
    #endif
        }

        static string Fmt(float[] a, int i, string f)
        {
            return a != null && i < a.Length ? a[i].ToString(f, System.Globalization.CultureInfo.InvariantCulture) : "";
        }

        // ---------------- 패널 ----------------

        [ContextMenu("실내 눈부심: 다시 준비")] void MenuRebuild() { Rebuild(); }
        [ContextMenu("실내 눈부심: 4가지 비교")] void MenuCompare() { RunCompare(); }
        [ContextMenu("실내 눈부심: CSV 저장")] void MenuExport() { ExportCsv(); }

        void OnGUI()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.H) { showGui = !showGui; e.Use(); }
            if (guiFont != null) GUI.skin.font = guiFont;
            if (cameraDgp && camText.Length > 0)
                GUI.Box(new Rect(Screen.width * 0.5f - 260f, Screen.height - 46f, 520f, 30f), camText);
            if (!showGui)
            {
                if (GUI.Button(new Rect(10, 10, 100, 24), "실내 눈부심")) showGui = true;
                return;
            }
            if (!ready) return;
            float w = 360f;
            GUILayout.BeginArea(new Rect(10, 10, w, Screen.height - 70));
            GUILayout.BeginVertical("box");
            DrawPanelContents(w, IndoorPanelPlace.Game);
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        /// <summary>패널 안쪽. Game 창(OnGUI), Scene 창, Inspector에서 같이 써요</summary>
        public void DrawPanelContents(float width, IndoorPanelPlace ctx)
        {
            if (!ready) { GUILayout.Label("준비 중"); return; }

            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>실내 눈부심 (DGP)</b>", Rich());
            if (ctx == IndoorPanelPlace.Game && GUILayout.Button("숨기기 (H)", GUILayout.Width(80))) showGui = false;
            if (ctx == IndoorPanelPlace.Scene && GUILayout.Button("숨기기", GUILayout.Width(60))) showSceneGui = false;
            GUILayout.EndHorizontal();

            int m = GUILayout.Toolbar((int)mode, new[] { "끔", "현재 시각", "하루 GA" });
            if (m != (int)mode) SetMode(m == 0 ? "off" : m == 1 ? "instant" : "daily");
            int lc = GUILayout.SelectionGrid((int)louverCase, CaseLabels, 2);
            if (lc != (int)louverCase) SetLouverCase(CaseKeys[lc]);
            int sk = GUILayout.Toolbar((int)sky, new[] { "맑음", "구름 조금", "흐림", "EPW" });
            if (sk != (int)sky) SetSky(sk == 0 ? "clear" : sk == 1 ? "partly" : sk == 2 ? "overcast" : "epw");

            if (mode == IndoorMode.Daily)
            {
                int dm = GUILayout.Toolbar((int)dailyMetric, new[] { "GA", "글레어 시간", "최대 DGP" });
                if (dm != (int)dailyMetric) { dailyMetric = (IndoorDailyMetric)dm; Recolor(); ResultVersion++; }
                bool all = GUILayout.Toggle(allRepresentativeDays, "대표일 4일 모두(춘분·하지·추분·동지)");
                if (all != allRepresentativeDays) { allRepresentativeDays = all; Recompute(); }
            }

            GUILayout.BeginHorizontal();
            bool tw = GUILayout.Toggle(showThroughWalls, "벽 뒤에서도 보기");
            if (tw != showThroughWalls) { showThroughWalls = tw; ApplyZTest(); }
            bool cd = GUILayout.Toggle(cameraDgp, "카메라 시점 DGP");
            if (cd != cameraDgp) { cameraDgp = cd; camSig = 0; camNext = 0f; if (!cd) camText = ""; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("다시 준비")) Rebuild();
            if (GUILayout.Button("4가지 비교")) RunCompare();
            if (GUILayout.Button("CSV 저장")) ExportCsv();
            GUILayout.EndHorizontal();

            GUILayout.Label("상태: " + status);
            if (built && eng != null)
                GUILayout.Label(string.Format("영역 {0}개 · 센서 {1}개(점 {2} × 방향) · 창이 안 보이는 점 {3}개", zones.Count, eng.sensors.Count, eng.points.Count, noWindowPoints));

            if (mode != IndoorMode.Off && shownStats.Length > 0)
            {
                GUILayout.Label(CaseName(shownCase) + (shownCase == IndoorLouverCase.ResponsiveInner
                    ? string.Format(" · 투과율 {0:0.00} × 외측 {1:0.00}", visibleTransmittance, outerGlassVisibleTransmittance) : ""));
                GUILayout.Label(shownLabel);
                GUILayout.Label(shownStats);
                GUILayout.Label(string.Format("색: {0} {1} ~ {2}{3}", shownUnit, shownLo.ToString(shownHi < 10f ? "0.00" : "0"), shownHi.ToString(shownHi < 10f ? "0.00" : "0"),
                    legendIsGa ? " (빨강 = 글레어 잦음)" : " (초록 → 빨강)"));
                Rect bar = GUILayoutUtility.GetRect(Mathf.Max(60f, width - 30f), 12f);
                var tex = legendIsGa ? legendGa : legendDgp;
                if (tex != null) GUI.DrawTexture(bar, tex, ScaleMode.StretchToFill);
                if (!legendIsGa) GUILayout.Label("DGP 0.35 인지 가능 · 0.40 방해 · 0.45 견디기 어려움 · 회색 = 창이 안 보임");
            }
            foreach (var line in compareLines) GUILayout.Label(line);
            if (cameraDgp && camText.Length > 0) GUILayout.Label(camText);
        }

        GUIStyle rich;
        GUISkin richSkin;
        GUIStyle Rich()
        {
            if (rich == null || richSkin != GUI.skin)
            {
                rich = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 };
                richSkin = GUI.skin;
            }
            return rich;
        }
    }

    // ---------------- JSON 형식 ----------------

    [System.Serializable]
    public class IndoorCompareJson
    {
        public string louverCase;
        public float gaMean, gaMin, gaUnder95Pct, meanGlareHours, meanMaxDgp;
    }

    [System.Serializable]
    public class IndoorGlareResultJson
    {
        public string mode, louverCase, sky, label;
        public int sensors, points, pointsNoWindow;
        public float threshold;
        public float meanDgp, maxDgp;
        public float[] dgpClassPct;       // 인지 불가 / 인지 가능 / 방해 / 견디기 어려움 (%)
        public float gaMean, gaMin, gaUnder95Pct, meanGlareHours;
        public float cameraDgp;
        public IndoorCompareJson[] compare;
    }
}
