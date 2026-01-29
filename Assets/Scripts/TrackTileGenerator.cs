using System;
using UnityEngine;

/// <summary>
/// 반원형 U자 트랙 타일 하나를 생성하는 제너레이터.
/// - 에디터에서 값 바꾸면 자동으로 메쉬 재생성(옵션)
/// - 런타임에서 프리팹이 Instantiate 되면 메쉬 / 콜라이더 자동 생성
/// - Entry / Middle / Exit 프로파일을 t(0~1)에 따라 블렌딩
/// - MarbleRaceManager는 이 클래스의 tileType / tileLength / curveAngleDeg 만 사용해서
///   경로(center line)를 따라가도록 맞춰둔 상태.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class TrackTileGenerator : MonoBehaviour
{
    // ───────────────────────────────── 타일 종류 ─────────────────────────────────

    public enum TileType
    {
        Straight,
        CurveLeft,
        CurveRight
    }

    [Header("경사 설정")]
    [Tooltip("타일 시작점 대비 끝점의 높이 차이 (양수 = 오르막, 음수 = 내리막)")]
    public float slopeDeltaHeight = 0f;


    [Header("타일 경로 설정")]
    [Tooltip("타일 종류: 직선 / 좌커브 / 우커브")]
    public TileType tileType = TileType.Straight;

    [Tooltip("타일 중심 경로의 길이 (월드 단위)")]
    public float tileLength = 50f;

    [Tooltip("커브 타일일 때 회전 각도(도). Straight에서는 무시됨.")]
    public float curveAngleDeg = 45f;

    // ───────────────────────────────── 메쉬 해상도 ─────────────────────────────────

    [Header("메쉬 해상도")]
    [Tooltip("진행 방향(앞뒤) 분할 수. 높을수록 곡선이 부드럽지만 버텍스 수가 늘어납니다.")]
    public int segmentsAlong = 32;

    [Tooltip("가로 방향(왼↔오른쪽) 분할 수. 높을수록 바닥 곡면이 부드럽습니다.")]
    public int segmentsAcross = 10;

    // ───────────────────────────────── 두께 / 옵션 ─────────────────────────────────

    [Header("두께 설정")]
    [Tooltip("트랙 두께. (아래쪽 쉘 두께, 엠보싱에 사용)")]
    public float shellThickness = 0.2f;

    [Tooltip("에디터에서 값 변경 시 자동으로 메쉬를 재생성할지 여부")]
    public bool autoGenerateInEditor = true;

    // ───────────────────────────────── 프로파일 ─────────────────────────────────

    [Serializable]
    public class TrackProfile
    {
        [Tooltip("Entry/Exit 프로파일 매칭용 ID. 같은 ID끼리만 이어 붙일 수 있게 하는 용도.")]
        public string profileId = "0";

        [Tooltip("트랙의 안쪽 폭(왼↔오른쪽). 값이 커질수록 넓어집니다.")]
        public float trackWidth = 4f;

        [Tooltip("중앙 바닥이 내려가는 깊이. 값이 클수록 U자 홈이 깊어집니다.")]
        public float floorDepth = 2f;

        [Tooltip("U자 곡률의 날카로움. 1 = 완만, 값이 클수록 중앙이 더 깊고 양 끝이 급해짐.")]
        public float profilePower = 2f;

        [Tooltip("좌우 벽 높이. 값이 클수록 가드레일이 높아집니다.")]
        public float wallHeight = 2f;

        [Tooltip("엠보싱(울퉁불퉁) 세기. 0이면 평평, 값이 클수록 굴곡이 커집니다.")]
        public float embossAmplitude = 0f;

        [Tooltip("엠보싱을 진행 방향으로 몇 주기 반복할지")]
        public float embossFreqAlong = 2f;

        [Tooltip("엠보싱을 가로 방향으로 몇 주기 반복할지")]
        public float embossFreqAcross = 2f;

        public TrackProfile Clone()
        {
            return (TrackProfile)MemberwiseClone();
        }
    }

    [Header("프로파일 (Entry / Middle / Exit)")]
    [Tooltip("타일 시작 부분 프로파일")]
    public TrackProfile entryProfile;

    [Tooltip("타일 중간 부분 프로파일")]
    public TrackProfile middleProfile;

    [Tooltip("타일 끝 부분 프로파일")]
    public TrackProfile exitProfile;

    [Header("프로파일 전환 구간")]
    [Range(0f, 0.5f)]
    [Tooltip("0~1 중 타일 앞쪽에서 Entry → Middle 로 블렌딩되는 비율")]
    public float entryBlendRatio = 0.2f;

    [Range(0f, 0.5f)]
    [Tooltip("0~1 중 타일 뒤쪽에서 Middle → Exit 로 블렌딩되는 비율")]
    public float exitBlendRatio = 0.2f;

    // ───────────────────────────────── 내부 캐시 ─────────────────────────────────

    private MeshFilter _meshFilter;
    private MeshCollider _meshCollider;
    internal float wallHeight;

    // =====================================================
    // Unity 라이프사이클
    // =====================================================

    private void Awake()
    {
        // 런타임에서 프리팹이 Instantiate 되면 메쉬/콜라이더 보장
        if (Application.isPlaying)
        {
            EnsureProfiles();
            EnsureRuntimeMesh(forceRemesh: false);
        }
    }

    private void Reset()
    {
        EnsureProfiles();
        EnsureRuntimeMesh(forceRemesh: true);
    }

    private void OnValidate()
    {
        segmentsAlong = Mathf.Max(4, segmentsAlong);
        segmentsAcross = Mathf.Max(4, segmentsAcross);
        tileLength = Mathf.Max(0.1f, tileLength);
        curveAngleDeg = Mathf.Max(0.01f, curveAngleDeg);
        shellThickness = Mathf.Max(0.001f, shellThickness);

        EnsureProfiles();

        // 에디터에서 값 바꾸면 자동 재생성
        if (!Application.isPlaying && autoGenerateInEditor)
        {
            EnsureRuntimeMesh(forceRemesh: true);
        }
    }

    // =====================================================
    // 프로파일 / 메쉬 보장
    // =====================================================

    private void EnsureProfiles()
    {
        if (entryProfile == null)
            entryProfile = new TrackProfile { profileId = "0" };
        if (middleProfile == null)
            middleProfile = new TrackProfile { profileId = "0" };
        if (exitProfile == null)
            exitProfile = new TrackProfile { profileId = "0" };
    }

    /// <summary>
    /// 에디터 / 런타임 공통으로 MeshFilter / MeshCollider를 보장하고,
    /// 필요하면 메쉬를 새로 생성한다.
    /// </summary>
    private void EnsureRuntimeMesh(bool forceRemesh)
    {
        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();
        if (_meshCollider == null)
            _meshCollider = GetComponent<MeshCollider>();

        if (_meshFilter == null)
            _meshFilter = gameObject.AddComponent<MeshFilter>();
        if (_meshCollider == null)
            _meshCollider = gameObject.AddComponent<MeshCollider>();

        if (forceRemesh || _meshFilter.sharedMesh == null)
        {
            GenerateMesh();
        }

        _meshCollider.sharedMesh = _meshFilter.sharedMesh;
        _meshCollider.convex = false; // 고정 오브젝트라 비-Convex OK
    }

    // =====================================================
    // 프로파일 보간 / 샘플링
    // =====================================================

    private TrackProfile LerpProfile(TrackProfile a, TrackProfile b, float k)
    {
        k = Mathf.Clamp01(k);
        TrackProfile p = new TrackProfile();

        // profileId는 어느 쪽에 더 가까운지로 결정
        p.profileId = (k < 0.5f) ? a.profileId : b.profileId;

        p.trackWidth = Mathf.Lerp(a.trackWidth, b.trackWidth, k);
        p.floorDepth = Mathf.Lerp(a.floorDepth, b.floorDepth, k);
        p.profilePower = Mathf.Lerp(a.profilePower, b.profilePower, k);
        p.wallHeight = Mathf.Lerp(a.wallHeight, b.wallHeight, k);
        p.embossAmplitude = Mathf.Lerp(a.embossAmplitude, b.embossAmplitude, k);
        p.embossFreqAlong = Mathf.Lerp(a.embossFreqAlong, b.embossFreqAlong, k);
        p.embossFreqAcross = Mathf.Lerp(a.embossFreqAcross, b.embossFreqAcross, k);

        return p;
    }

    /// <summary>
    /// t(0~1)에 해당하는 위치에서 사용할 TrackProfile 반환.
    /// 앞쪽 entryBlendRatio 구간: Entry→Middle, 뒤쪽 exitBlendRatio 구간: Middle→Exit
    /// 나머지는 Middle 그대로.
    /// </summary>
    private TrackProfile GetProfileAtT(float t)
    {
        t = Mathf.Clamp01(t);

        float eBlendEnd = entryBlendRatio;
        float xBlendStart = 1f - exitBlendRatio;

        if (t < eBlendEnd && entryBlendRatio > 0f)
        {
            float k = t / Mathf.Max(0.0001f, entryBlendRatio);
            return LerpProfile(entryProfile, middleProfile, k);
        }

        if (t > xBlendStart && exitBlendRatio > 0f)
        {
            float k = (t - xBlendStart) / Mathf.Max(0.0001f, exitBlendRatio);
            return LerpProfile(middleProfile, exitProfile, k);
        }

        // 가운데는 Middle 그대로
        return middleProfile;
    }

    // =====================================================
    // 경로(센터라인) 프레임 샘플링
    // MarbleRaceManager 의 GetTilePathFrameLocal 과 같은 수식이어야 함
    // =====================================================

    /// <summary>
    /// t(0~1)에 해당하는 경로 중심점/전방/오른쪽 벡터를 로컬 좌표계에서 반환.
    /// - x,z 경로: 직선/커브(기존과 동일)
    /// - y 경로: 시작 높이 0 → 끝 높이 slopeDeltaHeight 로 변하는 경사 (양 끝 기울기 0)
    /// </summary>
    public void GetPathFrameLocal(float t, out Vector3 center, out Vector3 forward, out Vector3 right)
    {
        t = Mathf.Clamp01(t);

        // ───────── 1) x,z 경로 (기존 로직 그대로) ─────────
        Vector3 baseCenter;
        Vector3 baseForward; // 수평 forward

        if (tileType == TileType.Straight)
        {
            baseCenter = new Vector3(0f, 0f, t * tileLength);
            baseForward = Vector3.forward;  // (0,0,1)
        }
        else
        {
            float totalAngleRad = Mathf.Deg2Rad * curveAngleDeg;
            float radius = tileLength / totalAngleRad;    // s = rθ → r = s/θ

            float angleRad = totalAngleRad * t;

            float x = radius * (1f - Mathf.Cos(angleRad));
            float z = radius * Mathf.Sin(angleRad);

            if (tileType == TileType.CurveRight)
                x = -x;

            baseCenter = new Vector3(x, 0f, z);

            float dx = radius * Mathf.Sin(angleRad);
            float dz = radius * Mathf.Cos(angleRad);
            if (tileType == TileType.CurveRight)
                dx = -dx;

            baseForward = new Vector3(dx, 0f, dz).normalized;
        }

        // ───────── 2) y(높이) – 시작 0 → 끝 slopeDeltaHeight, 양 끝 기울기 0 ─────────
        float height = 0f;
        float dHeightDt = 0f;

        if (Mathf.Abs(slopeDeltaHeight) > 0.0001f)
        {
            // Hermite: h(t) = H * (3t^2 - 2t^3)
            // h'(t)  = H * (6t - 6t^2)
            float t2 = t * t;
            float t3 = t2 * t;

            height = slopeDeltaHeight * (3f * t2 - 2f * t3);
            dHeightDt = slopeDeltaHeight * (6f * t - 6f * t2);
        }

        center = baseCenter;
        center.y = height;

        // ───────── 3) forward: 수평 + 수직 기울기 모두 포함한 접선 벡터 ─────────
        // baseForward는 이미 단위벡터(수평). 거기에 dy/dt 를 더해서 3D 접선 만든다.
        Vector3 tangent = new Vector3(
            baseForward.x,
            dHeightDt,          // 세로 기울기
            baseForward.z
        );

        if (tangent.sqrMagnitude < 1e-6f)
            tangent = baseForward; // slope 없는 경우

        forward = tangent.normalized;

        // ───────── 4) right: XZ 평면에서의 오른쪽 방향 ─────────
        Vector3 flatFwd = new Vector3(forward.x, 0f, forward.z);
        if (flatFwd.sqrMagnitude < 1e-6f)
            flatFwd = Vector3.forward;

        flatFwd.Normalize();
        right = new Vector3(flatFwd.z, 0f, -flatFwd.x).normalized;
    }



    // =====================================================
    // 단면(U자) 생성
    // =====================================================

    /// <summary>
    /// 가로 방향 인덱스 u(0~1)에 따라,
    /// 트랙 중심선 기준 offset(x, y)을 반환한다. (y는 위/아래)
    /// </summary>
    private Vector2 GetCrossSectionOffset(TrackProfile p, float u, float tAlong)
    {
        // u: 0(left wall top) → 1(right wall top)
        float width = p.trackWidth;
        float half = width * 0.5f;

        // 좌우 끝 10%는 수직 벽, 나머지 80%는 U자 바닥
        const float edgePortion = 0.1f;

        float xFromCenter;
        float y;

        if (u <= edgePortion)
        {
            // 왼쪽 벽 상단 → 바닥 접합점까지
            float k = u / edgePortion; // 0(top) ~ 1(bottom edge)
            xFromCenter = -half;
            y = Mathf.Lerp(p.wallHeight, 0f, k);
        }
        else if (u >= 1f - edgePortion)
        {
            // 오른쪽 벽
            float k = (1f - u) / edgePortion;
            xFromCenter = half;
            y = Mathf.Lerp(p.wallHeight, 0f, k);
        }
        else
        {
            // 바닥(U자) : 중앙이 가장 깊고 양 끝으로 갈수록 올라온다.
            float k = (u - edgePortion) / (1f - 2f * edgePortion); // 0~1
            float xLinear = Mathf.Lerp(-half, half, k);

            float normalized = Mathf.InverseLerp(0f, half, Mathf.Abs(xLinear));
            float depthFactor = Mathf.Pow(1f - Mathf.Clamp01(normalized), p.profilePower);
            float baseY = -p.floorDepth * depthFactor;

            xFromCenter = xLinear;
            y = baseY;
        }

        // 엠보싱 추가
        if (Mathf.Abs(p.embossAmplitude) > 0.0001f)
        {
            float emboss =
                Mathf.Sin(tAlong * Mathf.PI * 2f * p.embossFreqAlong) *
                Mathf.Sin((u - 0.5f) * Mathf.PI * 2f * p.embossFreqAcross);

            y += emboss * p.embossAmplitude;
        }

        return new Vector2(xFromCenter, y);
    }

    // =====================================================
    // 메쉬 생성
    // =====================================================

    [ContextMenu("Generate Mesh")]
    public void GenerateMesh()
    {
        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();
        if (_meshCollider == null)
            _meshCollider = GetComponent<MeshCollider>();

        Mesh mesh = new Mesh();
        mesh.name = "TrackTileMesh";

        int vAlong = Mathf.Max(2, segmentsAlong);
        int vAcross = Mathf.Max(2, segmentsAcross);

        int vertexCount = vAlong * vAcross;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];

        int quadCount = (vAlong - 1) * (vAcross - 1);
        int[] triangles = new int[quadCount * 6];

        int vertIndex = 0;
        int triIndex = 0;

        for (int ia = 0; ia < vAlong; ia++)
        {
            // 0~1 원래 t (겹침 없이)
            float tRaw = (vAlong == 1) ? 0f : (float)ia / (vAlong - 1);

            // 겹침 제거: tAlong = tRaw 그대로 사용
            float tAlong = tRaw;

            // 프로파일은 0~1 기준 tRaw 사용
            TrackProfile profile = GetProfileAtT(Mathf.Clamp01(tRaw));

            // 경로 프레임 (center / forward / right)
            GetPathFrameLocal(tAlong, out Vector3 center, out Vector3 forward, out Vector3 right);

            for (int ix = 0; ix < vAcross; ix++)
            {
                float u = (vAcross == 1) ? 0f : (float)ix / (vAcross - 1);

                Vector2 offset = GetCrossSectionOffset(profile, u, tAlong);

                Vector3 pos = center
                              + right * offset.x
                              + Vector3.up * offset.y;

                vertices[vertIndex] = pos;
                normals[vertIndex] = Vector3.up; // 대충 위로
                uvs[vertIndex] = new Vector2(u, tAlong);

                // 인덱스 계산 (quad)
                if (ia < vAlong - 1 && ix < vAcross - 1)
                {
                    int a = ia * vAcross + ix;
                    int b = a + 1;
                    int c = a + vAcross;
                    int d = c + 1;

                    // 두 삼각형 (a,c,b) (b,c,d)
                    triangles[triIndex++] = a;
                    triangles[triIndex++] = c;
                    triangles[triIndex++] = b;

                    triangles[triIndex++] = b;
                    triangles[triIndex++] = c;
                    triangles[triIndex++] = d;
                }

                vertIndex++;
            }
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        _meshFilter.sharedMesh = mesh;
        _meshCollider.sharedMesh = mesh;
        _meshCollider.convex = false;
    }

}
