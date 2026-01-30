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
    public void GenerateMesh()
    {
        // 파라미터 보정
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

        float marbleRadius = marbleDiameter * 0.5f;

        float topRadius = marbleRadius * topDiameterMultiplier * 0.5f;       // 윗 입구 반지름
        float bottomRadius = marbleRadius * bottomDiameterMultiplier * 0.5f; // 깔대기 구멍 반지름
        float spoutRadius = bottomRadius * spoutRadiusMultiplier;            // 주둥이 반지름

        int totalDown = funnelSegmentsDown + spoutSegmentsDown;
        int rings = totalDown + 1;
        int ringVerts = segmentsAround + 1;

        Vector3[] vertices = new Vector3[rings * ringVerts];
        Vector2[] uvs = new Vector2[vertices.Length];

        // SubMesh별 삼각형 인덱스 리스트
        var funnelTris = new System.Collections.Generic.List<int>(); // SubMesh 0 (깔대기)
        var spoutTris = new System.Collections.Generic.List<int>();  // SubMesh 1 (주둥이)

        int v = 0;

        // ───────── 버텍스 생성 ─────────
        for (int iy = 0; iy < rings; iy++)
        {
            float centerY;
            float centerZ;
            float radius;

            if (iy <= funnelSegmentsDown)
            {
                // ── 깔대기 구간 (위 → 아래로 곡면) ──
                float tFunnel = (float)iy / funnelSegmentsDown;  // 0~1
                float tCurve = Mathf.Pow(tFunnel, funnelCurvePower);

                radius = Mathf.Lerp(topRadius, bottomRadius, tCurve);
                centerY = -tFunnel * funnelHeight;   // 위 0 → 아래 -funnelHeight
                centerZ = 0f;                        // 깔대기는 Z로 안 휘어짐
            }
            else
            {
                // ── 주둥이 구간: 곡선으로 휘어지는 파이프 ──
                int j = iy - funnelSegmentsDown;
                float tSpout = (float)j / spoutSegmentsDown; // 0~1

                // 반지름은 직선 보간 (원 기둥이 점점 가늘어지거나 그대로)
                radius = Mathf.Lerp(bottomRadius, spoutRadius, tSpout);

                // --- 여기서 YZ 평면에서 곡선으로 휘게 만든다 (2차 베지어) ---

                // P0 = 깔대기 끝점 (시작점)
                float y0 = -funnelHeight;
                float z0 = 0f;

                // P2 = 주둥이 끝점 (목 끝)
                float y2 = -funnelHeight - spoutLength;
                float z2 = spoutBendOffsetZ;

                // P1 = 중간 컨트롤 포인트
                //  - z1 = 0 으로 두어 시작점에서는 z 방향 기울기가 0 (세로 방향)
                //  - y1 은 살짝 아래쪽으로 내려서 부드러운 곡률
                float y1 = Mathf.Lerp(y0, y2, 0.3f);  // 직선보다 살짝 위쪽에 둬서 부드럽게
                float z1 = 0f;

                float oneMinusT = 1f - tSpout;

                // 2차 베지어 곡선: B(t) = (1-t)^2 P0 + 2(1-t)t P1 + t^2 P2
                centerY =
                    oneMinusT * oneMinusT * y0 +
                    2f * oneMinusT * tSpout * y1 +
                    tSpout * tSpout * y2;

                centerZ =
                    oneMinusT * oneMinusT * z0 +
                    2f * oneMinusT * tSpout * z1 +
                    tSpout * tSpout * z2;
            }

            float vCoord = (float)iy / totalDown;

            for (int ix = 0; ix < ringVerts; ix++)
            {
                float u = (float)ix / segmentsAround;
                float angle = u * Mathf.PI * 2f;

                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                float x = radius * cos;
                float z = radius * sin + centerZ; // 중심을 Z 곡선에 따라 이동

                vertices[v] = new Vector3(x, centerY, z);
                uvs[v] = new Vector2(u, vCoord);
                v++;
            }
        }

        // ───────── 삼각형 생성 ─────────
        for (int iy = 0; iy < totalDown; iy++)
        {
            bool isSpout = iy >= funnelSegmentsDown;
            var target = isSpout ? spoutTris : funnelTris;

            int rowStart = iy * ringVerts;
            int nextRowStart = (iy + 1) * ringVerts;

            for (int ix = 0; ix < segmentsAround; ix++)
            {
                int a = rowStart + ix;
                int b = a + 1;
                int c = nextRowStart + ix;
                int d = c + 1;

                target.Add(a);
                target.Add(c);
                target.Add(b);

                target.Add(b);
                target.Add(c);
                target.Add(d);
            }
        }

        // ───────── Mesh 구성 ─────────
        Mesh mesh = new Mesh();
        mesh.name = "Funnel_WithCurvedSpout";

        mesh.vertices = vertices;
        mesh.uv = uvs;

        mesh.subMeshCount = 2;
        mesh.SetTriangles(funnelTris, 0); // 깔대기 본체 → Material Element 0
        mesh.SetTriangles(spoutTris, 1);  // 주둥이 → Material Element 1

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
        if (_meshCollider == null) _meshCollider = GetComponent<MeshCollider>();

        _meshFilter.sharedMesh = mesh;
        _meshCollider.sharedMesh = mesh;
        _meshCollider.convex = false;
    }

}
