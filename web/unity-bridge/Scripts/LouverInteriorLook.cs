// 실내 카메라 둘러보기 (외관과 같은 조작으로 맞춤)
// - 마우스: 왼쪽 끌기 = 둘러보기, 휠 = 확대 · 축소 (외관 = LouverViewSetup 과 같은 버튼)
// - 휴대폰 · 태블릿: 한 손가락 끌기 = 둘러보기, 두 손가락 벌리기 · 오므리기 = 확대 · 축소
// - 나란히 화면일 때는 실내 화면(오른쪽 반) 안에서 할 때만 움직인다
// 씬에 붙이지 않아도 스스로 생긴다 (RuntimeInitializeOnLoadMethod). 실내 카메라는 LouverBridge.interiorCamera 를 쓴다
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

public class LouverInteriorLook : MonoBehaviour
{
    [Tooltip("마우스 끌기 감도 (도 / 픽셀)")] public float mouseSensitivity = 0.2f;
    [Tooltip("손가락 끌기 감도 (도 / 픽셀)")] public float touchSensitivity = 0.15f;
    [Tooltip("확대 · 축소 범위 (시야각)")] public float minFov = 30f, maxFov = 90f;
    [Tooltip("위아래로 볼 수 있는 각도")] public float pitchLimit = 60f;

    LouverBridge bridge;
    Camera cam;
    float yaw, pitch, lastPinch = -1f;
    bool started;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<LouverInteriorLook>() == null) new GameObject("LouverInteriorLook").AddComponent<LouverInteriorLook>();
    }

    void LateUpdate()
    {
        if (bridge == null) bridge = FindAnyObjectByType<LouverBridge>();
        cam = bridge != null ? bridge.interiorCamera : null;
        if (cam == null || !cam.enabled) { started = false; lastPinch = -1f; return; }
        if (!started)
        {
            // 지금 실내 카메라 방향에서 시작 (LouverViewSetup 이 남서쪽 창을 보게 둔 방향)
            var e = cam.transform.eulerAngles;
            yaw = e.y; pitch = e.x > 180f ? e.x - 360f : e.x;
            started = true;
        }
        Vector2 look = Vector2.zero; float zoom = 0f;
#if ENABLE_INPUT_SYSTEM
        var ts = Touchscreen.current;
        TouchControl t0 = null, t1 = null;
        if (ts != null)
            foreach (var t in ts.touches)
            {
                if (!t.press.isPressed) continue;
                if (t0 == null) t0 = t; else if (t1 == null) t1 = t;
            }
        if (t0 != null)
        {
            // 손가락: 한 개 = 둘러보기, 두 개 = 확대 · 축소
            if (t1 == null)
            {
                lastPinch = -1f;
                if (Inside(t0.position.ReadValue())) look = t0.delta.ReadValue() * touchSensitivity;
            }
            else
            {
                Vector2 a = t0.position.ReadValue(), b = t1.position.ReadValue();
                float d = Vector2.Distance(a, b);
                if (lastPinch > 0f && Inside((a + b) * 0.5f)) zoom = (lastPinch - d) * 0.08f;
                lastPinch = d;
            }
        }
        else
        {
            lastPinch = -1f;
            var m = Mouse.current;
            if (m != null && Inside(m.position.ReadValue()))
            {
                if (m.leftButton.isPressed) look = m.delta.ReadValue() * mouseSensitivity;
                float w = m.scroll.ReadValue().y;
                if (Mathf.Abs(w) > 0.01f) zoom = w > 0f ? -3f : 3f;
            }
        }
#endif
        if (look != Vector2.zero)
        {
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, -pitchLimit, pitchLimit);
            cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        if (zoom != 0f) cam.fieldOfView = Mathf.Clamp(cam.fieldOfView + zoom, minFov, maxFov);
    }

    // 화면 좌표(아래가 0)가 실내 카메라 화면 안인지. 나란히면 오른쪽 반
    bool Inside(Vector2 p) => cam.pixelRect.Contains(p);
}
