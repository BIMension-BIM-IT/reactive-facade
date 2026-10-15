#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace IndoorGlare
{
    /// <summary>
    /// 실내 눈부심 패널을 Scene 창에서도 쓰게 해요(재생 중에만). 반사광(GlareSim) 패널과 따로예요.
    /// 기본은 Scene 창 왼쪽(오른쪽은 반사광 패널 자리). IndoorGlareSim의 Scene Gui On Left로 바꿀 수 있어요.
    /// 이 파일은 이름이 Editor인 폴더(IndoorGlare/Editor) 안에 있어야 해요(빌드에는 안 들어가요).
    /// </summary>
    [InitializeOnLoad]
    static class IndoorGlareSceneViewPanel
    {
        const float Width = 360f;
        const float Top = 40f;
        static Rect panelRect;
        static int seen = -1;
        static double lastRepaint;
        static Vector2 scroll;

        static IndoorGlareSceneViewPanel()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            var sim = IndoorGlareSim.Active;
            if (sim == null || !sim.showSceneGui) return;
            double now = EditorApplication.timeSinceStartup;
            if (sim.ResultVersion != seen || (sim.IsBusy && now - lastRepaint > 0.3))
            {
                seen = sim.ResultVersion;
                lastRepaint = now;
                SceneView.RepaintAll();
            }
        }

        static void OnSceneGUI(SceneView sv)
        {
            if (!EditorApplication.isPlaying) return;
            var sim = IndoorGlareSim.Active;
            if (sim == null || !sim.Ready) return;

            float vw = sv.position.width;
            float vh = sv.position.height - 25f;
            float x = sim.sceneGuiOnLeft ? 50f : vw - Width - 12f;   // 왼쪽은 도구 막대 옆

            Handles.BeginGUI();
            if (!sim.showSceneGui)
            {
                panelRect = new Rect(sim.sceneGuiOnLeft ? 50f : vw - 110f, Top, 100f, 24f);
                if (GUI.Button(panelRect, "실내 눈부심")) sim.showSceneGui = true;
            }
            else
            {
                var area = new Rect(x, Top, Width, Mathf.Max(200f, vh - Top - 10f));
                GUILayout.BeginArea(area);
                scroll = GUILayout.BeginScrollView(scroll);
                GUILayout.BeginVertical(EditorStyles.helpBox);
                sim.DrawPanelContents(Width - 30f, IndoorPanelPlace.Scene);
                GUILayout.EndVertical();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                panelRect = area;
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

    /// <summary>재생 중 패널, 편집 모드에서 "층 영역 만들기" 버튼</summary>
    [CustomEditor(typeof(IndoorGlareSim))]
    public class IndoorGlareSimInspector : Editor
    {
        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }

        public override void OnInspectorGUI()
        {
            var sim = (IndoorGlareSim)target;
            if (Application.isPlaying && sim.Ready)
            {
                EditorGUILayout.LabelField("재생 중 조작", EditorStyles.boldLabel);
                GUILayout.BeginVertical(EditorStyles.helpBox);
                sim.DrawPanelContents(EditorGUIUtility.currentViewWidth - 40f, IndoorPanelPlace.Inspector);
                GUILayout.EndVertical();
                EditorGUILayout.Space();
            }
            else if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("재생하면 여기, Scene 창 왼쪽, Game 창 왼쪽 위에 실내 눈부심 패널이 나와요.\n" +
                                        "센서 영역(IndoorGlareZone)이 없으면 재생할 때 Auto Floor 층 전체로 만들어요.", MessageType.Info);
                if (GUILayout.Button(string.Format("{0}층 영역 만들기 (루버 날 범위로)", sim.autoFloor)))
                {
                    var z = sim.CreateFloorZone(sim.autoFloor);
                    if (z != null)
                    {
                        Undo.RegisterCreatedObjectUndo(z.gameObject, "층 영역 만들기");
                        Selection.activeObject = z.gameObject;
                    }
                }
            }
            DrawDefaultInspector();
        }
    }
}
#endif
