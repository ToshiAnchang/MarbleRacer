using System;
using UnityEngine;

/// <summary>
/// 반원형 U자 트랙 타일 하나를 생성하는 제너레이터.
/// - 경로(center line): 직선 / 좌커브 / 우커브 + 경사(slopeDeltaHeight)
/// - 단면: U자 홈 + 좌우 벽 (폭/깊이/벽높이)
/// - Entry / Middle / Exit 프로파일을 t(0~1)에 따라 블렌딩
/// 
/// 물리 재질/엠보싱/두께 등은 제거하고,
/// 순수하게 "맵 모양(곡률, 경사, 폭, 깊이, 벽 높이)"만 정의.
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

    [Header("타일 경로 설정")]
    [Tooltip("타일 종류: 직선 / 좌커브 / 우커브")]
    public TileType tileType = TileType.Straight;

    [Tooltip("타일 중심 경로의 길이 (월드 단위)")]
    public float tileLength = 50f;

    [Tooltip("커브 타일일 때 회전 각도(도). Straight에서는 무시됨.")]
    public float curveAngleDeg = 45f;

    [Header("경사 설정")]
    [Tooltip("타일의 진행 방향 경사 각도(도 단위). 양수 = 오르막, 음수 = 내리막")]
    [Range(-45f, 45f)]
    public float slopeAngleDeg = -12f;   // 기본값은 적당히 내리막 느낌으로


    // ───────────────────────────────── 메쉬 해상도 ─────────────────────────────────

    [Header("메쉬 해상도")]
    [Tooltip("진행 방향(앞뒤) 분할 수. 높을수록 곡선이 부드럽지만 버텍스 수가 늘어납니다.")]
    public int segmentsAlong = 64;

    [Tooltip("가로 방향(왼↔오른쪽) 분할 수. 높을수록 바닥 곡면이 부드럽습니다.")]
    public int segmentsAcross = 48;

    [Header("에디터 옵션")]
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

    // =====================================================
    // Unity 라이프사이클
    // =====================================================

    private void Awake()
    {
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

        // 경사 각도는 -45° ~ 45° 사이로 제한
        slopeAngleDeg = Mathf.Clamp(slopeAngleDeg, -45f, 45f);

        EnsureProfiles();

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

        // 메쉬 콜라이더에 메쉬 적용
        _meshCollider.sharedMesh = _meshFilter.sharedMesh;
        _meshCollider.convex = false;

        // ───────── 여기서 트랙용 PhysicMaterial 적용 ─────────
        PhysicsManager pm = PhysicsManager.Instance;
        if (pm != null)
        {
            PhysicMaterial trackMat = pm.GetTrackMaterial();
            if (trackMat != null)
            {
                _meshCollider.sharedMaterial = trackMat;
            }
        }
    }


    // =====================================================
    // 프로파일 보간 / 샘플링
    // =====================================================

    private TrackProfile LerpProfile(TrackProfile a, TrackProfile b, float k)
    {
        k = Mathf.Clamp01(k);
        TrackProfile p = new TrackProfile();

        p.profileId = (k < 0.5f) ? a.profileId : b.profileId;

        p.trackWidth = Mathf.Lerp(a.trackWidth, b.trackWidth, k);
        p.floorDepth = Mathf.Lerp(a.floorDepth, b.floorDepth, k);
        p.profilePower = Mathf.Lerp(a.profilePower, b.profilePower, k);
        p.wallHeight = Mathf.Lerp(a.wallHeight, b.wallHeight, k);

        return p;
    }

    /// <summary>
    /// t(0~1)에 해당하는 위치에서 사용할 TrackProfile 반환.
    /// 앞쪽 entryBlendRatio 구간: Entry→Middle, 뒤쪽 exitBlendRatio 구간: Middle→Exit,
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

        return middleProfile;
    }

    // =====================================================
    // 경로(센터라인) 프레임 샘플링
    // =====================================================

    /// <summary>
    /// t(0~1)에 해당하는 경로 중심점/전방/오른쪽 벡터를 로컬 좌표계에서 반환.
    /// - XZ 경로: 직선 / 좌커브 / 우커브
    /// - Y 경로(경사):
    ///   ▷ slopeAngleDeg(도 단위)로 경사 강도 결정
    ///   ▷ 기본은 "전체 직선 경사"
    ///   ▷ entryBlendRatio > 0 이면: 시작 구간만 부드럽게 이어주고
    ///   ▷ exitBlendRatio  > 0 이면: 끝 구간만 부드럽게 이어줌
    ///   ▷ 둘 다 0이면: 타일 전체가 완전히 일정한 경사
    /// </summary>
    public void GetPathFrameLocal(float t, out Vector3 center, out Vector3 forward, out Vector3 right)
    {
        t = Mathf.Clamp01(t);

        Vector3 baseCenter;
        Vector3 baseForward;

        // ─────────────────────────────────
        // 수평 경로(XZ): 직선 / 좌커브 / 우커브
        // ─────────────────────────────────
        if (tileType == TileType.Straight)
        {
            // 직선: Z 방향으로 tileLength 만큼
            baseCenter = new Vector3(0f, 0f, t * tileLength);
            baseForward = Vector3.forward;
        }
        else
        {
            // 커브: 호 길이 = tileLength, 회전 각도 = curveAngleDeg
            float totalAngleRad = Mathf.Deg2Rad * curveAngleDeg;
            float radius = tileLength / totalAngleRad;    // s = rθ → r = s/θ

            float angleRad = totalAngleRad * t;

            float x = radius * (1f - Mathf.Cos(angleRad));
            float z = radius * Mathf.Sin(angleRad);

            if (tileType == TileType.CurveRight)
                x = -x;

            baseCenter = new Vector3(x, 0f, z);

            // 탄젠트(수평 방향)
            float dx = radius * Mathf.Sin(angleRad);
            float dz = radius * Mathf.Cos(angleRad);
            if (tileType == TileType.CurveRight)
                dx = -dx;

            baseForward = new Vector3(dx, 0f, dz).normalized;
        }

        // ─────────────────────────────────
        // 높이(Y): 각도 기반 경사
        //   - slopeAngleDeg(도)를 가지고 전체 높이 변화량(slopeDeltaHeight)을 계산
        //   - 기본은 "직선 경사"
        //   - entryBlendRatio > 0 → 시작 부분만 부드럽게
        //   - exitBlendRatio  > 0 → 끝 부분만 부드럽게
        // ─────────────────────────────────
        float height = 0f;
        float dHeightDt = 0f;

        if (Mathf.Abs(slopeAngleDeg) > 0.01f)
        {
            // 각도(도) → 라디안
            float rad = slopeAngleDeg * Mathf.Deg2Rad;

            // 전체 높이 변화량: "길이 * tan(각도)"
            float slopeDeltaHeight = Mathf.Tan(rad) * tileLength;

            // 기본값: 시작~끝까지 완전한 직선 경사
            height = slopeDeltaHeight * t;
            dHeightDt = slopeDeltaHeight;

            // 엔트리/엑싯 블렌드 구간 (0~1 사이)
            float eBlendEnd = Mathf.Clamp01(entryBlendRatio);
            float xBlendStart = 1f - Mathf.Clamp01(exitBlendRatio);

            // ───── 엔트리 쪽 부드러운 연결 (0 ~ eBlendEnd) ─────
            if (entryBlendRatio > 0f && t < eBlendEnd)
            {
                // 구간 [0, eBlendEnd] 를 s ∈ [0,1] 로 노멀라이즈
                float te = Mathf.Max(0.0001f, eBlendEnd);
                float s = t / te;
                float s2 = s * s;
                float s3 = s2 * s;

                // 엔드포인트:
                //  s=0 → y=0, dy/dt=0
                //  s=1 → y = slopeDeltaHeight * te, dy/dt = slopeDeltaHeight
                //
                // 유도된 Hermite 식:
                //  h(s)      = slopeDeltaHeight * te * (2 s^2 - s^3)
                //  dh/dt(s)  = slopeDeltaHeight * (4 s - 3 s^2)
                height = slopeDeltaHeight * te * (2f * s2 - s3);
                dHeightDt = slopeDeltaHeight * (4f * s - 3f * s2);
            }

            // ───── 엑싯 쪽 부드러운 연결 (xBlendStart ~ 1) ─────
            if (exitBlendRatio > 0f && t > xBlendStart)
            {
                float tx = xBlendStart;
                float oneMinusTx = Mathf.Max(0.0001f, 1f - tx);

                // 구간 [tx, 1] 를 s ∈ [0,1] 로 노멀라이즈
                float s = (t - tx) / oneMinusTx;
                float s2 = s * s;
                float s3 = s2 * s;

                // 엔드포인트:
                //  s=0 (t=tx) → y0 = slopeDeltaHeight * tx, dy/dt = slopeDeltaHeight
                //  s=1 (t=1)  → y1 = slopeDeltaHeight * 1 , dy/dt = 0
                //
                // 유도된 Hermite 식:
                //  h(s) = slopeDeltaHeight * ( s^3*tx - s^3 - s^2*tx + s^2 - s*tx + s + tx )
                //  dh/dt(s) = slopeDeltaHeight * ( 3*s^2*tx - 3*s^2 - 2*s*tx + 2*s - tx + 1 ) / (1 - tx)
                float term =
                    s3 * tx - s3 -
                    s2 * tx + s2 -
                    s * tx + s +
                    tx;

                height = slopeDeltaHeight * term;

                float dhdsFactor =
                    3f * s2 * tx - 3f * s2 -
                    2f * s * tx + 2f * s -
                    tx + 1f;

                dHeightDt = slopeDeltaHeight * (dhdsFactor / oneMinusTx);
            }
        }

        // 최종 중심점 = 수평 경로 + 높이
        center = baseCenter;
        center.y = height;

        // 경사까지 포함한 탄젠트 벡터
        Vector3 tangent = new Vector3(
            baseForward.x,
            dHeightDt,       // y는 t에 대한 변화량을 그대로 사용
            baseForward.z
        );

        if (tangent.sqrMagnitude < 1e-6f)
            tangent = baseForward;

        forward = tangent.normalized;

        // 수평 기준 right 벡터 (단면 생성/카메라용)
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
    private Vector2 GetCrossSectionOffset(TrackProfile p, float u)
    {
        // u: 0(left wall top) → 1(right wall top)
        float width = p.trackWidth;
        float half = width * 0.5f;

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
            float tRaw = (vAlong == 1) ? 0f : (float)ia / (vAlong - 1);
            float tAlong = tRaw;

            TrackProfile profile = GetProfileAtT(Mathf.Clamp01(tRaw));

            GetPathFrameLocal(tAlong, out Vector3 center, out Vector3 forward, out Vector3 right);

            for (int ix = 0; ix < vAcross; ix++)
            {
                float u = (vAcross == 1) ? 0f : (float)ix / (vAcross - 1);

                Vector2 offset = GetCrossSectionOffset(profile, u);

                Vector3 pos = center
                              + right * offset.x
                              + Vector3.up * offset.y;

                vertices[vertIndex] = pos;
                normals[vertIndex] = Vector3.up;
                uvs[vertIndex] = new Vector2(u, tAlong);

                if (ia < vAlong - 1 && ix < vAcross - 1)
                {
                    int a = ia * vAcross + ix;
                    int b = a + 1;
                    int c = a + vAcross;
                    int d = c + 1;

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
