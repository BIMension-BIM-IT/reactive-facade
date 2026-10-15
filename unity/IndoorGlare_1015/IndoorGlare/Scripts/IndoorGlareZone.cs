using System.Collections.Generic;
using UnityEngine;

namespace IndoorGlare
{
    /// <summary>
    /// 실내 눈부심 센서를 깔 영역(방) 하나. Grasshopper Glare1(방사형 센서 격자)을 옮겼어요.
    /// 이 오브젝트 위치 = 바닥 한가운데, 크기 = size(가로 X, 천장 높이 Y, 세로 Z). Y축 회전만 써요.
    /// 격자는 영역 안에 균등하게 놓고, 벽(영역 가장자리)에서 wallOffsetM보다 가까운 점은 빼요.
    /// 점마다 시선 방향 directions개(수평, startDirection부터 위에서 볼 때 반시계로 360/n씩).
    /// </summary>
    public class IndoorGlareZone : MonoBehaviour
    {
        [Tooltip("영역 크기(m): X 가로, Y 바닥~천장, Z 세로")]
        public Vector3 size = new Vector3(20f, 4.2f, 20f);
        [Tooltip("센서 간격(m). Glare1 기본 0.5, 넓은 층은 1.0 이상 권장")]
        public float gridSizeM = 1.0f;
        [Tooltip("바닥에서 눈높이(m). 앉은 자세 1.2")]
        public float eyeHeightM = 1.2f;
        [Tooltip("점마다 시선 방향 수")]
        [Range(1, 32)] public int directions = 8;
        [Tooltip("영역 가장자리에서 이보다 가까운 점은 빼요(m)")]
        public float wallOffsetM = 0.5f;
        [Tooltip("첫 시선 방향(이 오브젝트 기준, 수평으로 맞춰요). 기본 = 앞(+Z)")]
        public Vector3 startDirection = Vector3.forward;
        public Color gizmoColor = new Color(1f, 0.55f, 0.1f, 0.9f);

        public Quaternion Yaw { get { return Quaternion.Euler(0f, transform.eulerAngles.y, 0f); } }
        public float FloorY { get { return transform.position.y; } }
        public Vector3 Center { get { return transform.position + Vector3.up * (size.y * 0.5f); } }
        public float RoomArea { get { return 2f * (size.x * size.z + size.x * size.y + size.z * size.y); } }

        /// <summary>p가 영역 안인지(가로로 expandXZ m만큼 넓혀서)</summary>
        public bool Contains(Vector3 p, float expandXZ)
        {
            Vector3 l = Quaternion.Inverse(Yaw) * (p - transform.position);
            return Mathf.Abs(l.x) <= size.x * 0.5f + expandXZ && Mathf.Abs(l.z) <= size.z * 0.5f + expandXZ &&
                   l.y >= -0.01f && l.y <= size.y + 0.01f;
        }

        /// <summary>눈높이의 격자점(월드)</summary>
        public List<Vector3> GridPoints()
        {
            var res = new List<Vector3>();
            float g = Mathf.Max(0.05f, gridSizeM);
            float W = size.x, H = size.z;
            int nx = Mathf.Max(1, Mathf.FloorToInt(W / g));
            int nz = Mathf.Max(1, Mathf.FloorToInt(H / g));
            float x0 = -W * 0.5f + 0.5f * (W - (nx - 1) * g);   // 남는 여백을 양쪽에 똑같이
            float z0 = -H * 0.5f + 0.5f * (H - (nz - 1) * g);
            var q = Yaw;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = x0 + i * g, z = z0 + j * g;
                    if (Mathf.Abs(x) > W * 0.5f - wallOffsetM || Mathf.Abs(z) > H * 0.5f - wallOffsetM) continue;
                    res.Add(transform.position + q * new Vector3(x, eyeHeightM, z));
                }
            return res;
        }

        /// <summary>시선 방향들(월드, 수평 단위벡터)</summary>
        public Vector3[] Directions()
        {
            int n = Mathf.Max(1, directions);
            Vector3 sv = Yaw * new Vector3(startDirection.x, 0f, startDirection.z);
            if (sv.sqrMagnitude < 1e-8f) sv = Yaw * Vector3.forward;
            sv.Normalize();
            var res = new Vector3[n];
            for (int i = 0; i < n; i++) res[i] = (Quaternion.AngleAxis(-360f * i / n, Vector3.up) * sv).normalized;   // 위에서 볼 때 반시계
            return res;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = gizmoColor;
            Gizmos.matrix = Matrix4x4.TRS(Center, Yaw, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, size);
            // 눈높이 면
            var c = gizmoColor; c.a *= 0.35f;
            Gizmos.color = c;
            Gizmos.DrawWireCube(new Vector3(0f, eyeHeightM - size.y * 0.5f, 0f), new Vector3(size.x - 2f * wallOffsetM, 0.001f, size.z - 2f * wallOffsetM));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
