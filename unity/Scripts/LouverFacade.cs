using UnityEngine;

public enum FacadeSide { East = 0, South = 1, West = 2, North = 3 }

/// <summary>
/// 입면 하나의 수직 루버(날)를 만들고 각도를 적용해요.
/// 이 오브젝트의 위치 = 입면을 바깥에서 봤을 때 "왼쪽 아래 끝"이에요. 날은 그 점에서 오른쪽으로 늘어서요.
/// 좌표: Unity에서 북쪽 = +Z, 동쪽 = +X, 위 = +Y (Rhino의 Y=북, Z=위와 다름)
/// </summary>
public class LouverFacade : MonoBehaviour
{
    public FacadeSide side = FacadeSide.South;
    [Tooltip("입면 길이(m)")] public float lengthM = 10f;
    [Tooltip("날 폭(m) = 300mm")] public float bladeWidthM = 0.3f;
    [Tooltip("날 간격(m) = 300mm")] public float spacingM = 0.3f;
    [Tooltip("날 높이(m) = 4000mm")] public float heightM = 4f;
    [Tooltip("날 두께(m) = 25mm")] public float thicknessM = 0.025f;
    public Material bladeMaterial;

    Transform[] blades;
    Vector3 normal;  // 입면 바깥 방향
    Vector3 right;   // 입면을 바깥에서 볼 때 오른쪽

    public static Vector3 NormalOf(FacadeSide s)
    {
        switch (s)
        {
            case FacadeSide.East: return new Vector3(1, 0, 0);
            case FacadeSide.South: return new Vector3(0, 0, -1);
            case FacadeSide.West: return new Vector3(-1, 0, 0);
            default: return new Vector3(0, 0, 1);
        }
    }

    void Awake()
    {
        normal = NormalOf(side);
        right = Vector3.Cross(Vector3.up, -normal); // 바깥에서 입면을 바라볼 때의 오른쪽
        int n = Mathf.Max(1, Mathf.FloorToInt(lengthM / spacingM));
        blades = new Transform[n];
        for (int i = 0; i < n; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Blade_" + i;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, true);
            go.transform.position = transform.position + right * (spacingM * (i + 0.5f)) + Vector3.up * (heightM * 0.5f);
            // 로컬 Z가 날의 폭 방향이에요 (날을 입면에 수직으로 세우면 로컬 Z = 바깥 방향)
            go.transform.localScale = new Vector3(thicknessM, heightM, bladeWidthM);
            if (bladeMaterial != null) go.GetComponent<Renderer>().sharedMaterial = bladeMaterial;
            blades[i] = go.transform;
        }
        Apply(0f);
    }

    /// <summary>
    /// angleDeg: 부호 있는 각도. 0이면 날이 입면에 수직(열림).
    /// 양수 = 날의 바깥쪽 끝이 입면을 바깥에서 볼 때 오른쪽으로 기울어요.
    /// </summary>
    public void Apply(float angleDeg)
    {
        if (blades == null) return;
        float a = angleDeg * Mathf.Deg2Rad;
        Vector3 b = normal * Mathf.Cos(a) + right * Mathf.Sin(a);
        Quaternion q = Quaternion.LookRotation(b, Vector3.up);
        for (int i = 0; i < blades.Length; i++) blades[i].rotation = q;
    }
}
