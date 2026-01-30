using UnityEngine;

/// <summary>
/// 깔대기 본체 + 주둥이를 하나의 Mesh로 생성하되,
/// - 깔대기 본체 → SubMesh 0 (Material Element 0)
/// - 주둥이 부분 → SubMesh 1 (Material Element 1)
/// 
/// 주둥이 축을 Z 방향으로 꺾어서,
/// 아래로 내려가면서 Z 방향으로도 이동하도록 만든다.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class FunnelGenerator : MonoBehaviour
{
    // ───────────────────────────────── 구슬 기준 ─────────────────────────────────
    [Header("Marble 기준")]
    [Tooltip("게임에서 사용하는 구슬의 지름(Scale 기준). 기본 Sphere(1,1,1)이면 1.0으로 두면 됩니다.")]
    public float marbleDiameter = 1.0f;

    [Tooltip("깔대기 윗부분 지름 = marbleDiameter * topDiameterMultiplier")]
    public float topDiameterMultiplier = 30f;

    [Tooltip("깔대기 끝 구멍 지름 = marbleDiameter * bottomDiameterMultiplier")]
    public float bottomDiameterMultiplier = 1.5f;

    // ───────────────────────────────── 크기 ─────────────────────────────────
    [Header("크기")]
    [Tooltip("깔대기(위에서 주둥이 시작까지)의 높이. 위가 y=0, 아래로 음수 방향으로 내려갑니다.")]
    public float funnelHeight = 8f;

    [Tooltip("주둥이(목) 부분의 길이. 깔대기 아래에서 더 아래로 얼마나 뻗을지.")]
    public float spoutLength = 6f;

    [Tooltip("주둥이 반지름 배율. 1이면 깔대기 구멍 크기 그대로, 0.8이면 더 가늘게.")]
    public float spoutRadiusMultiplier = 1.0f;

    [Tooltip("주둥이 끝이 Z 방향으로 얼마나 이동할지(월드 단위).\n양수면 +Z 방향으로 휘어짐.")]
    public float spoutBendOffsetZ = 5f;

    // ───────────────────────────────── 해상도 ─────────────────────────────────
    [Header("해상도")]
    [Tooltip("둘레 방향 분할 수(많을수록 원형이 부드러움)")]
    public int segmentsAround = 64;

    [Tooltip("깔대기 부분(위→아래) 분할 수(많을수록 곡면이 부드러움)")]
    public int funnelSegmentsDown = 16;

    [Tooltip("주둥이 부분(위→아래) 분할 수")]
    public int spoutSegmentsDown = 8;

    // ───────────────────────────────── 곡률 ─────────────────────────────────
    [Header("곡률")]
    [Tooltip("깔대기 곡률. 1이면 직선(원뿔), 값이 클수록 위쪽이 더 완만해지는 곡선 형태.")]
    public float funnelCurvePower = 1.5f;

    // ───────────────────────────────── 에디터 옵션 ─────────────────────────────────
    public bool autoGenerateInEditor = true;

    private MeshFilter _meshFilter;
    private MeshCollider _meshCollider;

    // ───────────────────────────────── Unity 라이프사이클 ─────────────────────────────────

    private void Awake()
    {
        // 런타임에서 깔대기 프리팹이 Instantiate 되었을 때
        // MeshFilter / MeshCollider를 확보하고,
        // 메쉬가 없으면 자동으로 생성해 준다.
        EnsureComponents();

        if (_meshFilter.sharedMesh == null)
        {
            GenerateMesh();
        }
    }


    private void Reset()
    {
        EnsureComponents();
        GenerateMesh();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying && autoGenerateInEditor)
        {
            EnsureComponents();
            GenerateMesh();
        }
    }

    private void EnsureComponents()
    {
        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();
        if (_meshCollider == null)
            _meshCollider = GetComponent<MeshCollider>();
    }

    // ───────────────────────────────── 메쉬 생성 ─────────────────────────────────
    [ContextMenu("Generate Funnel Mesh")]
    [ContextMenu("Generate Funnel Mesh")]
    public void GenerateMesh()
    {
        // ───── 파라미터 보정 ─────
        if (marbleDiameter <= 0f) marbleDiameter = 1.0f;
        if (topDiameterMultiplier <= 0f) topDiameterMultiplier = 1f;
        if (bottomDiameterMultiplier <= 0f) bottomDiameterMultiplier = 0.5f;
        if (funnelHeight <= 0f) funnelHeight = 5f;
        if (spoutLength < 0f) spoutLength = 0f;
        if (segmentsAround < 3) segmentsAround = 3;
        if (funnelSegmentsDown < 1) funnelSegmentsDown = 1;
        if (spoutSegmentsDown < 1) spoutSegmentsDown = 1;
        if (funnelCurvePower <= 0f) funnelCurvePower = 1f;
        if (spoutRadiusMultiplier <= 0f) spoutRadiusMultiplier = 1f;

        EnsureComponents();

        float marbleRadius = marbleDiameter * 0.5f;
        float topRadius = marbleRadius * topDiameterMultiplier * 0.5f;
        float bottomRadius = marbleRadius * bottomDiameterMultiplier * 0.5f;
        float spoutRadius = bottomRadius * spoutRadiusMultiplier;

        int totalDown = funnelSegmentsDown + spoutSegmentsDown;
        int rings = totalDown + 1;
        int ringVerts = segmentsAround + 1;

        Vector3[] vertices = new Vector3[rings * ringVerts];
        Vector2[] uvs = new Vector2[vertices.Length];

        var funnelTris = new System.Collections.Generic.List<int>();
        var spoutTris = new System.Collections.Generic.List<int>();

        int v = 0;

        // ───── 버텍스 생성 ─────
        for (int iy = 0; iy < rings; iy++)
        {
            float centerY;
            float centerZ;
            float radius;

            if (iy <= funnelSegmentsDown)
            {
                float t = (float)iy / funnelSegmentsDown;
                float tc = Mathf.Pow(t, funnelCurvePower);

                radius = Mathf.Lerp(topRadius, bottomRadius, tc);
                centerY = -t * funnelHeight;
                centerZ = 0f;
            }
            else
            {
                int j = iy - funnelSegmentsDown;
                float t = (float)j / spoutSegmentsDown;

                radius = Mathf.Lerp(bottomRadius, spoutRadius, t);

                float y0 = -funnelHeight;
                float z0 = 0f;

                float y2 = -funnelHeight - spoutLength;
                float z2 = spoutBendOffsetZ;

                float y1 = Mathf.Lerp(y0, y2, 0.3f);
                float z1 = 0f;

                float omt = 1f - t;

                centerY =
                    omt * omt * y0 +
                    2f * omt * t * y1 +
                    t * t * y2;

                centerZ =
                    omt * omt * z0 +
                    2f * omt * t * z1 +
                    t * t * z2;
            }

            float vCoord = (float)iy / totalDown;

            for (int ix = 0; ix < ringVerts; ix++)
            {
                float u = (float)ix / segmentsAround;
                float ang = u * Mathf.PI * 2f;

                float x = Mathf.Cos(ang) * radius;
                float z = Mathf.Sin(ang) * radius + centerZ;

                vertices[v] = new Vector3(x, centerY, z);
                uvs[v] = new Vector2(u, vCoord);
                v++;
            }
        }

        // ───── 삼각형 생성 ─────
        for (int iy = 0; iy < totalDown; iy++)
        {
            bool isSpout = iy >= funnelSegmentsDown;
            var target = isSpout ? spoutTris : funnelTris;

            int r0 = iy * ringVerts;
            int r1 = (iy + 1) * ringVerts;

            for (int ix = 0; ix < segmentsAround; ix++)
            {
                int a = r0 + ix;
                int b = a + 1;
                int c = r1 + ix;
                int d = c + 1;

                target.Add(a);
                target.Add(c);
                target.Add(b);

                target.Add(b);
                target.Add(c);
                target.Add(d);
            }
        }

#if UNITY_EDITOR
        // ───── 기존 sharedMesh 정리 (타입 미스매치 방지 핵심) ─────
        if (!Application.isPlaying && _meshFilter.sharedMesh != null)
        {
            DestroyImmediate(_meshFilter.sharedMesh);
        }
#endif

        Mesh mesh = new Mesh();
        mesh.name = "Funnel_WithCurvedSpout";

        mesh.vertices = vertices;
        mesh.uv = uvs;

        mesh.subMeshCount = 2;
        mesh.SetTriangles(funnelTris, 0);
        mesh.SetTriangles(spoutTris, 1);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        _meshFilter.sharedMesh = mesh;
        _meshCollider.sharedMesh = mesh;
        _meshCollider.convex = false;
    }

}
