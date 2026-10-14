using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 반사광이 닿는지 확인하는 광선 검사(테스트용으로 바꿔 끼울 수 있게 인터페이스로 둬요).
/// receiverId: 받는 쪽 번호, -1 = 모름, GlareReceivers.BlockerId = 막기만 하는 것(대지·도로 등)
/// </summary>
public interface IGlareRaycaster
{
    bool Raycast(Vector3 origin, Vector3 dir, float maxDist, out Vector3 point, out Vector3 normal, out int receiverId);
    string ReceiverName(int id);
}

/// <summary>
/// 반사광을 받는 쪽(주변 건물, 지면)을 "따로 만든 물리 씬"에 콜라이더로 복사해 두고 광선을 쏴요.
///  - 원래 씬의 콜라이더·레이어는 건드리지 않아요. 루버 날이나 우리 건물은 여기에 넣지 않으니 광선이 안 맞아요.
///  - Hierarchy에 "GlareReceivers..." 씬이 하나 더 보여요(재생 중에만).
///  - WebGL·PC 빌드에서는 주변 건물 FBX의 Import Settings → Model → Read/Write를 켜야 콜라이더를 만들 수 있어요.
/// </summary>
public class GlareReceivers : IGlareRaycaster
{
    /// <summary>막기만 하고 결과에는 넣지 않는 것(대지·도로 등)에 맞았을 때의 번호</summary>
    public const int BlockerId = -2;

    Scene scene;
    PhysicsScene physics;
    Transform holder;
    readonly Dictionary<Collider, int> colliderToReceiver = new Dictionary<Collider, int>();
    readonly Dictionary<string, int> nameToId = new Dictionary<string, int>();
    public readonly List<string> names = new List<string>();
    public int colliderCount;
    public int blockerCount;     // 그중 막기만 하는 것
    public int unreadableCount;
    public int triangleCount;

    public bool IsValid { get { return scene.IsValid(); } }

    public static GlareReceivers Create()
    {
        var g = new GlareReceivers();
        string name = "GlareReceivers_" + System.DateTime.Now.Ticks;
        g.scene = SceneManager.CreateScene(name, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        g.physics = g.scene.GetPhysicsScene();
        var go = new GameObject("Receivers (반사광 계산용 콜라이더)");
        SceneManager.MoveGameObjectToScene(go, g.scene);
        g.holder = go.transform;
        return g;
    }

    int IdOf(string receiverName)
    {
        int id;
        if (!nameToId.TryGetValue(receiverName, out id))
        {
            id = names.Count;
            names.Add(receiverName);
            nameToId[receiverName] = id;
        }
        return id;
    }

    public string ReceiverName(int id)
    {
        return (id >= 0 && id < names.Count) ? names[id] : "?";
    }

    /// <summary>
    /// 메시 하나를 받는 쪽으로 넣어요. 같은 이름(receiverName)끼리는 한 건물로 묶어서 집계해요.
    /// blocker = true면 광선을 막기만 하고 결과에는 넣지 않아요(receiverName은 안 써요).
    /// </summary>
    public bool AddMesh(MeshFilter mf, string receiverName, bool blocker = false)
    {
        var mesh = mf.sharedMesh;
        if (mesh == null || mesh.vertexCount == 0) return false;
#if !UNITY_EDITOR
        if (!mesh.isReadable) { unreadableCount++; return false; }
#else
        if (!mesh.isReadable) unreadableCount++;   // 에디터에서는 되지만 빌드에서는 안 돼요(경고만)
#endif
        var t = mf.transform;
        var go = new GameObject(mf.name);
        go.transform.SetParent(holder, false);
        go.transform.position = t.position;
        go.transform.rotation = t.rotation;
        go.transform.localScale = t.lossyScale;
        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        colliderToReceiver[mc] = blocker ? BlockerId : IdOf(receiverName);
        colliderCount++;
        if (blocker) blockerCount++;
        for (int s = 0; s < mesh.subMeshCount; s++) triangleCount += (int)(mesh.GetIndexCount(s) / 3);
        return true;
    }

    public bool AddTerrain(Terrain terrain, string receiverName, bool blocker = false)
    {
        if (terrain == null || terrain.terrainData == null) return false;
        var go = new GameObject(terrain.name);
        go.transform.SetParent(holder, false);
        go.transform.position = terrain.transform.position;
        var tc = go.AddComponent<TerrainCollider>();
        tc.terrainData = terrain.terrainData;
        colliderToReceiver[tc] = blocker ? BlockerId : IdOf(receiverName);
        colliderCount++;
        if (blocker) blockerCount++;
        return true;
    }

    /// <summary>평평한 지면(윗면 높이 y, 한 변 2 × halfSize)을 넣어요.</summary>
    public void AddGround(Vector3 center, float y, float halfSize, string receiverName)
    {
        var go = new GameObject("Ground");
        go.transform.SetParent(holder, false);
        go.transform.position = new Vector3(center.x, y - 0.5f, center.z);
        var bc = go.AddComponent<BoxCollider>();
        bc.size = new Vector3(halfSize * 2f, 1f, halfSize * 2f);
        colliderToReceiver[bc] = IdOf(receiverName);
        colliderCount++;
    }

    public void Sync()
    {
        Physics.SyncTransforms();
    }

    public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, out Vector3 point, out Vector3 normal, out int receiverId)
    {
        RaycastHit hit;
        if (physics.Raycast(origin, dir, out hit, maxDist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            normal = hit.normal;
            int id;
            receiverId = (hit.collider != null && colliderToReceiver.TryGetValue(hit.collider, out id)) ? id : -1;
            return true;
        }
        point = Vector3.zero;
        normal = Vector3.up;
        receiverId = -1;
        return false;
    }

    public void Dispose()
    {
        if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
    }
}
