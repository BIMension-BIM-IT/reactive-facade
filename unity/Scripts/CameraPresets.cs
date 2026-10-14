using UnityEngine;

/// <summary>
/// 카메라 시점 버튼용(동·남·서·북면 / 전경 / 9층 실내). 시점마다 빈 오브젝트를 씬에 놓고 이름과 함께 등록하세요.
/// 버튼 OnClick에 GoToName("남면") 또는 GoToIndex(0)을 연결해요. 이동은 부드럽게 따라가요.
/// </summary>
public class CameraPresets : MonoBehaviour
{
    [System.Serializable]
    public class Preset
    {
        public string name;
        [Tooltip("이 오브젝트의 위치·방향이 카메라가 가는 자리예요")] public Transform pose;
    }

    public Camera cam;
    public Preset[] presets;
    [Tooltip("이동 속도(클수록 빠름)")] public float speed = 3f;

    Vector3 targetPos;
    Quaternion targetRot;
    bool moving;

    void Start()
    {
        if (cam == null) cam = Camera.main;
    }

    public void GoToIndex(int i)
    {
        if (presets == null || i < 0 || i >= presets.Length || presets[i].pose == null) return;
        targetPos = presets[i].pose.position;
        targetRot = presets[i].pose.rotation;
        moving = true;
    }

    public void GoToName(string n)
    {
        if (presets == null) return;
        for (int i = 0; i < presets.Length; i++)
            if (presets[i].name == n) { GoToIndex(i); return; }
        Debug.LogWarning("[CameraPresets] 이런 이름의 시점이 없어요: " + n);
    }

    void LateUpdate()
    {
        if (!moving || cam == null) return;
        float t = 1f - Mathf.Exp(-speed * Time.deltaTime);
        cam.transform.position = Vector3.Lerp(cam.transform.position, targetPos, t);
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, targetRot, t);
        if ((cam.transform.position - targetPos).sqrMagnitude < 0.0004f) moving = false;
    }
}
