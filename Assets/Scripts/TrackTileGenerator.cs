using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class TrackTileGenerator : MonoBehaviour
{
    public enum TileType
    {
        Straight,
        CurveLeft,
        CurveRight
    }

    [Header("타일 경로 설정")]
    [Tooltip("타일의 기본 진행 형태 (직선 / 좌커브 / 우커브)")]
    public TileType tileType = TileType.Straight;

    [Tooltip("타일의 대략적인 길이 (직선 길이 또는 커브의 호 길이 느낌)")]
    public float tileLength = 20f;

    [Tooltip("CurveLeft/CurveRight일 때 회전 각도 (도 단위). Straight에서는 무시")]
    public float curveAngleDeg = 45f;

    [Header("메쉬 해상도")]
    [Tooltip("진행 방향으로 몇 구간으로 나눌지 (많을수록 부드럽지만 버텍스 증가)")]
    public int segmentsAlong = 32;

    [Tooltip("폭 방향(좌↔우)으로 몇 구간으로 나눌지 (U자 단면 품질)")]
    public int segmentsAcross = 10;

    [Header("두께 설정")]
    [Tooltip("트랙 쉘 두께. 값이 클수록 바깥 껍데기가 더 두꺼워짐")]
    public float shellThickness = 0.2f;

    // =========================
    // PROFILE DATA
    // =========================
    [System.Serializable]
    public class TrackProfile
    {
        [Tooltip("이 단면 프로파일의 ID. Exit.profileId == 다음 타일 Entry.profileId 일 때만 이어붙이기 허용")]
        public string profileId = "DEFAULT";

        [Tooltip("U자 홈 안쪽 폭 (좌/우 벽 사이 거리)")]
        public float trackWidth = 4f;

        [Tooltip("바닥 중앙이 얼마나 내려갈지 (U자 깊이). 값이 클수록 깊게 패임")]
        public float floorDepth = 0.5f;

        [Tooltip("U자 곡률. 1이면 완만, 값이 커질수록 중앙이 더 급하게 깊어짐")]
        public float profilePower = 2f;

        [Tooltip("좌/우 벽 높이")]
        public float wallHeight = 2f;

        [Tooltip("바닥 표면 울퉁불퉁한 엠보싱 세기. 0이면 매끈")]
        public float embossAmplitude = 0f;

        [Tooltip("진행 방향(앞/뒤)으로의 엠보싱 노이즈 주기")]
        public float embossFreqAlong = 2f;

        [Tooltip("폭 방향(좌/우)으로의 엠보싱 노이즈 주기")]
        public float embossFreqAcross = 2f;
    }

    [Header("프로파일 (Entry / Middle / Exit)")]
    [Tooltip("타일 시작 부분(입구)의 단면 프로파일")]
    public TrackProfile entryProfile = new TrackProfile();

    [Tooltip("타일 중간 부분의 단면 프로파일 (이 타일의 성격/난이도)")]
    public TrackProfile middleProfile = new TrackProfile();

    [Tooltip("타일 끝 부분(출구)의 단면 프로파일")]
    public TrackProfile exitProfile = new TrackProfile();

    [Header("프로파일 전환 구간")]
    [Range(0.05f, 0.45f)]
    [Tooltip("0~1 중, 앞쪽 몇 %를 Entry→Middle로 부드럽게 섞을지 (예: 0.2 = 앞 20%)")]
    public float entryBlendRatio = 0.2f;

    [Range(0.05f, 0.45f)]
    [Tooltip("0~1 중, 뒤쪽 몇 %를 Middle→Exit로 부드럽게 섞을지 (예: 0.2 = 뒤 20%)")]
    public float exitBlendRatio = 0.2f;

    [Header("에디터 옵션")]
    [Tooltip("체크하면 인스펙터 값이 바뀔 때마다 자동으로 메쉬를 다시 생성합니다.")]
    public bool autoGenerateInEditor = true;

    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private MeshRenderer meshRenderer;

    // 내부에서 쓸 임시 프로파일
    private TrackProfile _tempProfile = new TrackProfile();

    // ======================================
    // 공통: 프로파일 / 컴포넌트 null 방지
    // ======================================
    private void EnsureProfiles()
    {
        if (entryProfile == null) entryProfile = new TrackProfile();
        if (middleProfile == null) middleProfile = new TrackProfile();
        if (exitProfile == null) exitProfile = new TrackProfile();
    }

    private void EnsureComponents()
    {
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshCollider == null) meshCollider = GetComponent<MeshCollider>();
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
    }

    private void OnValidate()
    {
        segmentsAlong = Mathf.Max(4, segmentsAlong);
        segmentsAcross = Mathf.Max(4, segmentsAcross);
        tileLength = Mathf.Max(0.1f, tileLength);
        curveAngleDeg = Mathf.Max(0.01f, curveAngleDeg);
        shellThickness = Mathf.Max(0.001f, shellThickness);

        EnsureProfiles();

        if (!Application.isPlaying && autoGenerateInEditor)
            GenerateMesh();
    }

    [ContextMenu("Generate Mesh")]
    public void GenerateMesh()
    {
        EnsureProfiles();
        EnsureComponents();

        // 1) 먼저 "안쪽 표면" 메쉬를 만든다.
        Mesh innerMesh = GenerateInnerSurfaceMesh();

        // 2) 그 메쉬를 기반으로 노멀 방향으로 shellThickness 만큼 밀어낸
        //    "두꺼운 쉘" 메쉬를 만든다.
        Mesh thickMesh = BuildThickShell(innerMesh, shellThickness);

        meshFilter.sharedMesh = thickMesh;
        meshCollider.sharedMesh = thickMesh;

        // 머티리얼 없으면 자동 생성
        if (meshRenderer.sharedMaterial == null)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = Color.white;
            meshRenderer.sharedMaterial = mat;
        }
    }

    // ======================================
    // 1) 안쪽 U자 표면 + 벽 메쉬 생성
    // ======================================
    private Mesh GenerateInnerSurfaceMesh()
    {
        int vAlong = segmentsAlong + 1;
        int vAcross = segmentsAcross + 1;

        int stride = vAcross + 4;      // 바닥 vAcross + 벽 4
        int totalVerts = vAlong * stride;

        Vector3[] vertices = new Vector3[totalVerts];
        Vector2[] uvs = new Vector2[totalVerts];
        List<int> triangles = new List<int>();

        int vertIndex = 0;

        // ===== 버텍스 생성 =====
        for (int i = 0; i < vAlong; i++)
        {
            float t = (float)i / segmentsAlong;
            TrackProfile p = GetProfileAtT(t);

            float halfWidth = p.trackWidth * 0.5f;

            GetPathFrame(t, out Vector3 center, out Vector3 forward, out Vector3 right);

            // 바닥 그리드 (좌↔우)
            for (int j = 0; j < vAcross; j++)
            {
                float u = (float)j / segmentsAcross;
                float x = (u - 0.5f) * 2f; // -1 ~ +1

                float curve = 1f - Mathf.Pow(Mathf.Abs(x), p.profilePower);
                float y = -p.floorDepth * curve;

                if (p.embossAmplitude > 0f)
                {
                    float n = Mathf.PerlinNoise(
                        t * p.embossFreqAlong,
                        u * p.embossFreqAcross
                    );
                    y += (n - 0.5f) * 2f * p.embossAmplitude;
                }

                Vector3 pos = center + right * (x * halfWidth);
                pos.y += y;

                vertices[vertIndex] = pos;
                uvs[vertIndex] = new Vector2(u, t);
                vertIndex++;
            }

            // 좌/우 벽 (각 2개: bottom/top)
            Vector3 leftBase = vertices[vertIndex - vAcross];
            Vector3 rightBase = vertices[vertIndex - 1];

            int wallStart = vertIndex;

            vertices[wallStart + 0] = leftBase;
            vertices[wallStart + 1] = leftBase + Vector3.up * p.wallHeight;
            vertices[wallStart + 2] = rightBase;
            vertices[wallStart + 3] = rightBase + Vector3.up * p.wallHeight;

            uvs[wallStart + 0] = Vector2.zero;
            uvs[wallStart + 1] = Vector2.zero;
            uvs[wallStart + 2] = Vector2.zero;
            uvs[wallStart + 3] = Vector2.zero;

            vertIndex += 4;
        }

        // ===== 삼각형 인덱스 생성 =====
        for (int i = 0; i < segmentsAlong; i++)
        {
            int rowA = i * (vAcross + 4);
            int rowB = (i + 1) * (vAcross + 4);

            // 1) 바닥
            for (int j = 0; j < segmentsAcross; j++)
            {
                int a = rowA + j;
                int b = rowA + j + 1;
                int c = rowB + j;
                int d = rowB + j + 1;

                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(c); triangles.Add(b); triangles.Add(d);
            }

            // 2) 왼쪽 벽
            int la0 = rowA + vAcross;
            int la1 = la0 + 1;
            int lb0 = rowB + vAcross;
            int lb1 = lb0 + 1;

            triangles.Add(la0); triangles.Add(lb0); triangles.Add(la1);
            triangles.Add(la1); triangles.Add(lb0); triangles.Add(lb1);

            // 3) 오른쪽 벽
            int ra0 = rowA + vAcross + 2;
            int ra1 = ra0 + 1;
            int rb0 = rowB + vAcross + 2;
            int rb1 = rb0 + 1;

            triangles.Add(ra0); triangles.Add(ra1); triangles.Add(rb0);
            triangles.Add(rb0); triangles.Add(ra1); triangles.Add(rb1);
        }

        Mesh mesh = new Mesh();
        mesh.name = "TrackInnerSurface";
        mesh.vertices = vertices;
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    // ======================================
    // 2) 안쪽 메쉬를 기반으로 두꺼운 쉘 만들기
    // ======================================
    private Mesh BuildThickShell(Mesh innerMesh, float thickness)
    {
        Vector3[] innerVerts = innerMesh.vertices;
        Vector3[] innerNormals = innerMesh.normals;
        Vector2[] innerUV = innerMesh.uv;
        int[] innerTris = innerMesh.triangles;

        int n = innerVerts.Length;

        Vector3[] verts = new Vector3[n * 2];
        Vector2[] uvs = new Vector2[n * 2];
        List<int> tris = new List<int>();

        // 안쪽 버텍스 & 바깥 버텍스
        for (int i = 0; i < n; i++)
        {
            verts[i] = innerVerts[i];                               // 안쪽
            verts[i + n] = innerVerts[i] - innerNormals[i] * thickness; // 바깥쪽
            uvs[i] = innerUV[i];
            uvs[i + n] = innerUV[i];
        }

        // 1) 안쪽 표면 삼각형 (원래 것 그대로)
        tris.AddRange(innerTris);

        // 2) 바깥 표면 삼각형 (뒤집어서)
        for (int i = 0; i < innerTris.Length; i += 3)
        {
            int a = innerTris[i];
            int b = innerTris[i + 1];
            int c = innerTris[i + 2];

            // winding 뒤집어서 바깥쪽을 향하게
            tris.Add(c + n);
            tris.Add(b + n);
            tris.Add(a + n);
        }

        // (테두리 캡은 생략 – 일반적인 플레이 시점에서는 잘 안 보이는 부분이라
        //  일단 이렇게만 해도 “두께 있는 반파이프” 느낌은 충분히 남)

        Mesh m = new Mesh();
        m.name = "TrackTileThick";
        m.vertices = verts;
        m.triangles = tris.ToArray();
        m.uv = uvs;
        m.RecalculateNormals();
        m.RecalculateBounds();

        return m;
    }

    // =========================
    // PROFILE BLENDING
    // =========================
    private TrackProfile GetProfileAtT(float t)
    {
        EnsureProfiles();

        float entryEnd = Mathf.Clamp01(entryBlendRatio);
        float exitStart = 1f - Mathf.Clamp01(exitBlendRatio);

        if (t <= entryEnd)
        {
            float k = entryEnd <= 0f ? 0f : Mathf.InverseLerp(0f, entryEnd, t);
            return LerpProfile(entryProfile, middleProfile, k);
        }

        if (t >= exitStart)
        {
            float k = exitStart >= 1f ? 0f : Mathf.InverseLerp(exitStart, 1f, t);
            return LerpProfile(middleProfile, exitProfile, k);
        }

        return middleProfile;
    }

    private TrackProfile LerpProfile(TrackProfile a, TrackProfile b, float k)
    {
        EnsureProfiles();

        if (a == null && b == null)
            return _tempProfile;
        if (a == null)
            return b;
        if (b == null)
            return a;

        _tempProfile.profileId = a.profileId;
        _tempProfile.trackWidth = Mathf.Lerp(a.trackWidth, b.trackWidth, k);
        _tempProfile.floorDepth = Mathf.Lerp(a.floorDepth, b.floorDepth, k);
        _tempProfile.profilePower = Mathf.Lerp(a.profilePower, b.profilePower, k);
        _tempProfile.wallHeight = Mathf.Lerp(a.wallHeight, b.wallHeight, k);
        _tempProfile.embossAmplitude = Mathf.Lerp(a.embossAmplitude, b.embossAmplitude, k);
        _tempProfile.embossFreqAlong = Mathf.Lerp(a.embossFreqAlong, b.embossFreqAlong, k);
        _tempProfile.embossFreqAcross = Mathf.Lerp(a.embossFreqAcross, b.embossFreqAcross, k);

        return _tempProfile;
    }

    // =========================
    // PATH (중심선 + 진행 방향)
    // =========================
    private void GetPathFrame(
        float t,
        out Vector3 center,
        out Vector3 forward,
        out Vector3 right
    )
    {
        if (tileType == TileType.Straight)
        {
            center = new Vector3(0f, 0f, t * tileLength);
            forward = Vector3.forward;
        }
        else
        {
            float totalAngleRad = Mathf.Deg2Rad * curveAngleDeg;
            float radius = tileLength / totalAngleRad; // s = r * θ → r = s / θ

            float angleRad = totalAngleRad * t;

            float x = radius * (1f - Mathf.Cos(angleRad));
            float z = radius * Mathf.Sin(angleRad);

            if (tileType == TileType.CurveRight)
                x = -x;

            center = new Vector3(x, 0f, z);

            float dx = radius * Mathf.Sin(angleRad);
            float dz = radius * Mathf.Cos(angleRad);

            if (tileType == TileType.CurveRight)
                dx = -dx;

            forward = new Vector3(dx, 0f, dz).normalized;
        }

        right = new Vector3(forward.z, 0f, -forward.x).normalized;
    }
}
