#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 반사광 패널을 Scene 창과 Inspector에서도 쓰게 해요(재생 중에만).
/// 이 파일은 이름이 Editor인 폴더(GlareSim/Editor) 안에 있어야 해요(빌드에는 안 들어가요).
/// </summary>
[InitializeOnLoad]
static class GlareSceneViewPanel
{
    const float Width = 340f;
    const float Top = 110f;     // 오른쪽 위 방향 기즈모 아래
    static Rect panelRect;      // 마지막으로 그린 패널 범위(Scene 창 좌표)
    static int seenVersion = -1;
    static double lastRepaint;
    static GUIStyle sunLabelStyle;

    static GlareSceneViewPanel()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.update += Tick;
    }

    // 계산 진행률과 결과가 Scene 창에 바로 보이게 가끔 다시 그려요
    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        var sim = GlareReflectionSim.Active;
        if (sim == null || !sim.showSceneGui) return;
        double now = EditorApplication.timeSinceStartup;
        bool changed = sim.ResultVersion != seenVersion;
        if (changed || (sim.IsBusy && now - lastRepaint > 0.3))
        {
            seenVersion = sim.ResultVersion;
            lastRepaint = now;
            SceneView.RepaintAll();
        }
    }

    static void OnSceneGUI(SceneView sv)
    {
        if (!EditorApplication.isPlaying) return;
        var sim = GlareReflectionSim.Active;
        if (sim == null || !sim.Ready) return;

        // 태양 궤적 글자(시각·방위)
        var labels = sim.SunPathLabels;
        if (labels != null)
        {
            if (sunLabelStyle == null)
                sunLabelStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            sunLabelStyle.normal.textColor = new Color(1f, 0.95f, 0.7f);
            foreach (var l in labels) Handles.Label(l.pos, l.text, sunLabelStyle);
        }

        float vw = sv.position.width;
        float vh = sv.position.height - 25f;   // 위 툴바만큼 빼요

        Handles.BeginGUI();
        if (!sim.showSceneGui)
        {
            panelRect = new Rect(vw - 100f, Top, 90f, 24f);
            if (GUI.Button(panelRect, "반사광")) sim.showSceneGui = true;
        }
        else
        {
            var area = new Rect(vw - Width - 12f, Top, Width, Mathf.Max(200f, vh - Top - 10f));
            GUILayout.BeginArea(area);
            GUILayout.BeginVertical(EditorStyles.helpBox);
            sim.DrawPanelContents(Width, GlarePanelContext.Scene);
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint)
            {
                var r = GUILayoutUtility.GetLastRect();
                panelRect = new Rect(area.x + r.x, area.y + r.y, r.width, r.height);
            }
            GUILayout.EndArea();
        }
        Handles.EndGUI();

        // 패널 위에서 클릭·휠이 씬 선택이나 카메라 줌으로 넘어가지 않게 막아요
        var e = Event.current;
        if (panelRect.Contains(e.mousePosition))
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            if (e.type == EventType.ScrollWheel) e.Use();
        }
    }
}

/// <summary>재생 중 Inspector 맨 위에 같은 패널을 보여줘요.</summary>
[CustomEditor(typeof(GlareReflectionSim))]
public class GlareReflectionSimEditor : Editor
{
    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }

    public override void OnInspectorGUI()
    {
        var sim = (GlareReflectionSim)target;
        if (Application.isPlaying && sim.Ready)
        {
            EditorGUILayout.LabelField("재생 중 조작", EditorStyles.boldLabel);
            GUILayout.BeginVertical(EditorStyles.helpBox);
            sim.DrawPanelContents(EditorGUIUtility.currentViewWidth - 40f, GlarePanelContext.Inspector);
            GUILayout.EndVertical();
            EditorGUILayout.Space();
        }
        else if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("재생하면 여기와 Scene 창 오른쪽에 반사광 조작 패널이 나와요.", MessageType.Info);
        }
        DrawDefaultInspector();
    }
}
#endif
