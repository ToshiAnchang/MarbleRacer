using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;


public class MarbleRaceManager : MonoBehaviour
{
    public static MarbleRaceManager Instance { get; private set; }

    private Canvas startCanvas;

    private static Font cachedFont;

    [Header("Race Settings")]
    public int laneCount = 4;           // 레인 수
    public float laneWidth = 2f;        // 레인 폭
    public float trackLength = 500f;    // 트랙 전체 길이
    public float startZ = 0f;           // 출발선 위치
    public float finishZ = 460f;        // 결승선 위치
    public float startImpulse = 5f;     // 초반에 살짝 밀어주는 힘
    // ---- 트랙 곡선용 코너 포인트 ----                                        
    private float[] cornerZ;
    private float[] cornerX;

    [Header("Obstacles")]
    public int obstacleRows = 35;       // 트랙 전체를 따라 몇 줄의 장애물 라인을 만들지
    public float obstacleSize = 0.45f;   // 기본 장애물 크기

    [Header("Obstacles Placement")]
    public float obstacleRowSpacing = 10f;     // 각 장애물 라인 간 Z 간격
    public float obstacleStartOffset = 40f;    // 스타트에서 얼마나 떨어진 지점부터 장애물 시작
    public float obstacleEndMargin = 40f;    // 피니시 앞의 비워두는 여유

    [Header("Walls / Safety")]
    public float wallHeight = 4.5f;        // ★ 기존 2f 정도였던 걸 4 정도로 올림
    public float maxMarbleHeight = 2f;   // ★ 구슬이 올라갈 수 있는 최대 높이

    [Range(0f, 1f)]
    public float obstacleDensity = 0.5f;   // ★ 한 칸을 실제로 채울 확률 (0.5면 절반 정도)

    // --- 장애물 스트리밍용 상태 값들 ---

    // WFC로 생성한 전체 장애물 그리드 (논리적인 전 구간)
    private bool[,] obstacleGrid;
    // 각 row의 월드 z 위치
    private float[] obstacleRowZ;
    // row 인덱스 -> 실제 생성된 장애물 GameObject 리스트
    private Dictionary<int, List<GameObject>> obstacleRowObjects = new();

    // 장애물 배치 z 구간 (StartRace에서 설정)
    private float startObstacleZ;
    private float endObstacleZ;
    private float obstacleZRange;

    // 현재 "보이는" 장애물 z 범위
    private float visibleBackZ;
    private float visibleFrontZ;

    // 스트리밍 규칙 상수
    private const float FORWARD_NEED_THRESHOLD = 100f;   // 앞으로 남은 길이 200 미만이면
    private const float FORWARD_ADD_LENGTH = 300f;       // 500 만큼 더 생성
    private const float BACK_MAX_DISTANCE = 100f;        // 뒤로 100 이상 멀어지면
    private const float BACK_KEEP_DISTANCE = 20f;        // 20만 남기고 삭제


    private readonly List<Marble> marbles = new();

    private bool winnerAnnounced = false;
    private HashSet<Marble> finishedMarbles = new HashSet<Marble>();

    // 미니맵용
    private Canvas raceCanvas;
    private RectTransform minimapRect;
    private Dictionary<Marble, RectTransform> minimapIcons = new();


    // ▶ 미니맵에서 한 화면에 보여줄 월드 범위 (기준 구슬 중심)
    [Header("Minimap Settings")]
    public float minimapWorldHalfWidth = 10f;   // X 방향으로 좌우 10 유닛
    public float minimapWorldHalfHeight = 25f;  // Z 방향으로 앞뒤 25 유닛

    // 기준 구슬(플레이어)
    private Marble focusMarble;

    // 트랙 곡선을 정의하는 기준 전체 길이 (시드 당 고정)
    // 이 안에서 z 값을 매핑하기 때문에, trackLength를 바꿔도 앞부분 곡선은 동일.
    private const float CurveTotalLength = 5000f;


    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }


    private void Start()
    {
        // 시작 UI에서 값 입력 후 StartRace()가 호출될 때까지 대기
        CreateStartUI();
    }

    private void StartRace(int seed, int lanes, float length)
    {
        laneCount = Mathf.Clamp(lanes, 2, 8);

        // ✅ 최소 맵 길이는 500 보장, 상한(2000)은 제거
        trackLength = Mathf.Max(500f, length);

        // 피니시 라인은 트랙 끝에서 40만큼 앞에서
        finishZ = startZ + trackLength - 40f;

        targetTrackLength = trackLength;

        Random.InitState(seed);

        // 나중에 필요하면 seed를 필드에 저장해도 됨
        // currentSeed = seed;

        //👇레이스상태 초기화
        winnerAnnounced = false;
        finishedMarbles = new HashSet<Marble>();

        GenerateTrackCurve();
        CreateGround();
        //CreateWalls();
        CreateLanesAndMarbles();
        CreateObstacles();
        CreateFinishTrigger();
        CreateCamera();
        CreateMinimap();
    }

    private static Font _uiFont;

    private Font GetUIFont()
    {
        if (_uiFont != null)
            return _uiFont;

        // 1순위: OS에서 Arial 폰트를 직접 동적 생성 (Inspector 필요 없음)
        _uiFont = Font.CreateDynamicFontFromOSFont("Arial", 18);

        // 혹시 OS에서 Arial 못 찾을 경우를 대비한 2순위 (있어도 되고 없어도 됨)
        if (_uiFont == null)
        {
            // 만약 나중에 Resources/Fonts/ARIAL 이런 식으로 옮기면 여기서 잡힘
            _uiFont = Resources.Load<Font>("Fonts/ARIAL");
        }

        if (_uiFont == null)
        {
            Debug.LogError("UI 폰트를 찾을 수 없습니다. OS Arial 도 없고 Resources/Fonts/ARIAL 도 없습니다.");
        }

        return _uiFont;
    }

    /// <summary>
    /// 시드에 따라 트랙 곡선을 결정하는 큰 코너 포인트들을 생성.
    /// cornerZ : 트랙 진행(z) 위치
    /// cornerX : 각 위치에서 좌우 오프셋(x)
    /// 
    /// ✅ CurveTotalLength 기준으로 곡선을 만들기 때문에,
    ///    같은 시드면 trackLength를 바꿔도 z 기준 곡선 모양은 동일.
    /// </summary>
    private void GenerateTrackCurve()
    {
        // 코너 개수 (값이 클수록 코너가 많아지고 복잡해짐)
        int cornerCount = 12;

        cornerZ = new float[cornerCount];
        cornerX = new float[cornerCount];

        float start = startZ;
        // ❗여기서부터는 trackLength 대신 고정된 CurveTotalLength 사용
        float end = startZ + CurveTotalLength;

        // 트랙 중앙 기준에서 좌우로 최대 얼마나 휘어질지
        float maxOffset = laneWidth * (laneCount * 0.5f + 1.5f);

        for (int i = 0; i < cornerCount; i++)
        {
            float t = (float)i / (cornerCount - 1);

            // z 위치는 "곡선 전체"에 균등하게 분포 (CurveTotalLength 기준)
            cornerZ[i] = Mathf.Lerp(start, end, t);

            // x 오프셋은 랜덤, 다만 시작/끝은 0으로 해서 자연스럽게 들어오고 나가게
            if (i == 0 || i == cornerCount - 1)
            {
                cornerX[i] = 0f;
            }
            else
            {
                float x = Random.Range(-maxOffset, maxOffset);

                // 너무 직전 코너와 비슷하면 조금 더 차이를 줘서 단조로움 방지
                float prev = cornerX[i - 1];
                if (Mathf.Abs(x - prev) < laneWidth)
                {
                    x += Mathf.Sign(x - prev) * laneWidth * 1.2f;
                }

                cornerX[i] = Mathf.Clamp(x, -maxOffset, maxOffset);
            }
        }
    }

    // 슬로프/세그먼트 다 버리고, 그냥 큰 평지 하나로 처리
    private void CreateGround()
    {
        // 레인 기준 기본 폭
        float totalWidth = laneCount * laneWidth;

        // 트랙이 좌우로 휘어지는 최대폭(GenerateTrackCurve에서 쓰는 값과 맞춰서 넉넉히)
        float maxCurveAbs = laneWidth * (laneCount * 1.0f + 3f);

        // "트랙 + 커브" 전체가 차지할 절반 폭
        float halfTrackWidth = totalWidth * 0.5f + maxCurveAbs + 4f;   // 좌우 여유 4

        float centerZ = startZ + trackLength * 0.5f;

        // ─────────────────────────────
        // 1) 트랙 바깥 전체를 덮는 검은 바닥(배경용, 충돌 없음)
        // ─────────────────────────────
        var voidGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        voidGo.name = "OuterVoid";
        voidGo.transform.position = new Vector3(0f, -0.75f, centerZ); // 트랙보다 살짝 아래
        voidGo.transform.localScale = new Vector3(
            halfTrackWidth * 4f,   // 트랙보다 훨씬 넓게
            0.5f,
            trackLength * 4f       // 앞뒤도 넉넉하게
        );

        var voidMat = new Material(Shader.Find("Standard"));
        voidMat.color = Color.black;
        voidGo.GetComponent<Renderer>().material = voidMat;

        // 배경용이니까 콜라이더 제거
        Destroy(voidGo.GetComponent<Collider>());
    }


    /// <summary>
    /// z 위치에 따라 트랙의 "전체 반폭(halfWidth)"을 돌려준다.
    /// - t=0(스타트)와 t=1(피니시)에서는 레인 폭과 딱 맞게
    /// - 중간(t=0.5 근처)에서는 넓어졌다가 다시 좁아지는 Slope 연출
    /// </summary>
    private float GetTrackHalfWidth(float z)
    {
        float baseHalf = laneCount * laneWidth * 0.5f;   // 레인들만 딱 들어가는 기본 폭

        float zMin = startZ;
        float zMax = startZ + trackLength;
        float t = Mathf.InverseLerp(zMin, zMax, z);      // 0~1

        // 가운데에서 가장 넓고, 시작/끝은 0인 사인 곡선
        // t=0   -> 0
        // t=0.5 -> 1
        // t=1   -> 0
        float widenFactor = Mathf.Sin(t * Mathf.PI);     // 0~1

        // 얼마나 더 넓어질지 (레인 수에 따라 자동으로 조금 더 크게)
        float extra = laneWidth * (laneCount * 0.5f + 2.5f);

        return baseHalf + extra * widenFactor;
    }


    // z 위치에 따라 트랙의 좌우 곡선 오프셋을 돌려주는 함수
    private float GetCurveOffset(float z)
    {
        // 코너 정보가 아직 없으면 (안전장치) – 간단한 사인파 방식으로 fallback
        if (cornerZ == null || cornerX == null || cornerZ.Length < 2)
        {
            // ✅ 여기서도 trackLength 대신 CurveTotalLength 사용
            float tFallback = (z - startZ) / CurveTotalLength;
            float amp1 = laneWidth * 2f;
            float amp2 = laneWidth * 1.2f;
            float curveFallback =
                Mathf.Sin(tFallback * Mathf.PI * 1.5f) * amp1 +
                Mathf.Sin(tFallback * Mathf.PI * 3f + 1.2f) * amp2 * 0.5f;
            return curveFallback;
        }

        float start = startZ;
        float end = startZ + CurveTotalLength;  // ✅ 고정된 곡선 길이 기준

        // 범위 밖이면 끝값 사용 (트랙이 CurveTotalLength보다 길어져도 앞부분 모양은 동일)
        if (z <= cornerZ[0]) return cornerX[0];
        if (z >= cornerZ[cornerZ.Length - 1]) return cornerX[cornerZ.Length - 1];

        // z를 곡선 전체 구간(0~1)으로 매핑
        float t = Mathf.InverseLerp(start, end, z);
        int cornerCount = cornerZ.Length;

        // 전체 구간을 [0, cornerCount-1] 로 보고, 그 안에서 자신의 위치 찾기
        float f = t * (cornerCount - 1);
        int idx = Mathf.Clamp(Mathf.FloorToInt(f), 0, cornerCount - 2);
        float localT = f - idx;   // 0~1

        // 두 코너 사이를 보간해서 기본 곡선 생성
        float baseX = Mathf.Lerp(cornerX[idx], cornerX[idx + 1], localT);

        // 여기에 작은 물결 느낌을 추가해서 너무 “각진” 느낌을 줄임
        float wiggle = Mathf.Sin(t * Mathf.PI * 4f) * laneWidth * 0.4f;

        return baseX + wiggle;
    }

    /// <summary>
    /// 월드 위치(worldPos)의 z값 기준으로
    /// 트랙 곡선이 진행하는 방향(탄젠트)을 돌려준다.
    /// 결과는 XZ 평면에서 정규화된 벡터.
    /// </summary>
    public Vector3 GetTrackForwardDirection(Vector3 worldPos)
    {
        float zMin = startZ;
        float zMax = startZ + trackLength;

        // z 클램프
        float z = Mathf.Clamp(worldPos.z, zMin, zMax);

        // 양 옆으로 조금 떨어진 두 점으로 탄젠트 계산
        float dz = 0.5f;
        float z1 = Mathf.Clamp(z - dz, zMin, zMax);
        float z2 = Mathf.Clamp(z + dz, zMin, zMax);

        float x1 = GetCurveOffset(z1);
        float x2 = GetCurveOffset(z2);

        Vector3 p1 = new Vector3(x1, 0f, z1);
        Vector3 p2 = new Vector3(x2, 0f, z2);

        Vector3 t = p2 - p1;
        t.y = 0f;

        if (t.sqrMagnitude < 0.0001f)
            t = Vector3.forward;

        return t.normalized;
    }


    /// <summary>
    /// sideSign : +1 → 트랙 진행 방향 기준 왼쪽 벽, -1 → 오른쪽 벽
    /// offsetFromCenter : 트랙 중앙에서 벽까지의 거리
    /// </summary>
    private void CreateWallSide(string name, float sideSign, float offsetFromCenter, float dz, int samples)
    {
        // ─────────────────────
        // 1) 시각용 메쉬(얇은 리본)
        // ─────────────────────
        int vertCount = (samples + 1) * 2;   // 아래/위
        Vector3[] vertices = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        int[] triangles = new int[samples * 6];   // 사각형 1개당 2삼각형

        float zStart = startZ;
        float zEnd = startZ + trackLength;

        for (int i = 0; i <= samples; i++)
        {
            float z = zStart + dz * i;
            if (z > zEnd) z = zEnd;

            // 트랙 중앙선 위치
            float centerX = GetCurveOffset(z);
            Vector3 center = new Vector3(centerX, 0f, z);

            // 진행 방향(접선) 계산용 샘플
            float z2 = z + 0.5f;
            if (z2 > zEnd) z2 = z - 0.5f;

            float centerX2 = GetCurveOffset(z2);
            Vector3 center2 = new Vector3(centerX2, 0f, z2);

            Vector3 tangent = (center2 - center).normalized;
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.forward;

            // 진행 방향 기준 왼쪽 벡터
            Vector3 perpLeft = new Vector3(-tangent.z, 0f, tangent.x).normalized;
            Vector3 sideDir = (sideSign > 0f) ? perpLeft : -perpLeft;

            // 실제 벽 위치
            Vector3 basePos = center + sideDir * offsetFromCenter;

            int v = i * 2;

            vertices[v + 0] = basePos;
            vertices[v + 1] = basePos + Vector3.up * wallHeight;

            float u = (float)i / samples;
            uvs[v + 0] = new Vector2(u, 0f);
            uvs[v + 1] = new Vector2(u, 1f);
        }

        int ti = 0;
        for (int i = 0; i < samples; i++)
        {
            int v = i * 2;
            int vn = (i + 1) * 2;

            // 아래-위-다음아래
            triangles[ti++] = v + 0;
            triangles[ti++] = v + 1;
            triangles[ti++] = vn + 0;

            // 다음아래-위-다음위
            triangles[ti++] = vn + 0;
            triangles[ti++] = v + 1;
            triangles[ti++] = vn + 1;
        }

        var wallGo = new GameObject(name);
        wallGo.transform.position = Vector3.zero;
        wallGo.transform.rotation = Quaternion.identity;

        var mf = wallGo.AddComponent<MeshFilter>();
        var mr = wallGo.AddComponent<MeshRenderer>();

        var mesh = new Mesh { name = name + "_Mesh" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        mf.sharedMesh = mesh;

        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.8f, 0.8f, 0.8f);
        mr.sharedMaterial = mat;

        // 시각용 메쉬에는 콜라이더 안 달아요 (너무 얇아서 관통 나기 쉬움)
        // ─────────────────────
        // 2) 실제 충돌용 BoxCollider 세그먼트들
        // ─────────────────────
        float thickness = 0.6f;   // 벽 두께 (원하는 만큼 조절 가능)

        for (int i = 0; i < samples; i++)
        {
            int v = i * 2;
            int vn = (i + 1) * 2;

            Vector3 p0 = vertices[v + 0];   // 현재 구간 바닥점
            Vector3 p1 = vertices[vn + 0];  // 다음 구간 바닥점

            Vector3 dir = p1 - p0;
            float length = dir.magnitude;
            if (length < 0.001f) continue;

            Vector3 center = (p0 + p1) * 0.5f;

            var segObj = new GameObject($"{name}_Col_{i}");
            segObj.transform.SetParent(wallGo.transform, false);

            // 월드 기준 위치/회전 그대로 사용 (wallGo가 회전 0이니까 그대로 로컬 = 월드)
            segObj.transform.position = center + Vector3.up * (wallHeight * 0.5f);
            segObj.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

            var boxCol = segObj.AddComponent<BoxCollider>();
            boxCol.size = new Vector3(thickness, wallHeight, length);
        }
    }

    private void CreateLanesAndMarbles()
    {
        float totalWidth = laneCount * laneWidth;
        float leftMostX = -totalWidth * 0.5f + laneWidth * 0.5f;

        for (int i = 0; i < laneCount; i++)
        {
            float laneBaseX = leftMostX + i * laneWidth;
            float curveOffset = GetCurveOffset(startZ);
            float laneX = laneBaseX + curveOffset;

            // 구슬 생성
            var marbleGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marbleGo.name = $"Marble_{i + 1}";
            marbleGo.transform.position = new Vector3(laneX, 0.5f, startZ);

            var rb = marbleGo.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.drag = 0.03f;
            rb.angularDrag = 0.02f;

            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var marble = marbleGo.AddComponent<Marble>();
            marble.laneIndex = i;
            marble.maxHeight = maxMarbleHeight;
            marbleGo.GetComponent<Renderer>().material = CreateColoredMaterial(i);

            rb.AddForce(Vector3.forward * startImpulse, ForceMode.Impulse);
            marbles.Add(marble);

            // ★ 기준 구슬 지정 (0번 레인)
            if (i == 0)
                focusMarble = marble;

            // 레인 가이드...
            var laneLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            laneLine.name = $"Lane_{i + 1}_Guide";
            laneLine.transform.position = new Vector3(leftMostX + i * laneWidth, 0.01f, trackLength * 0.5f);
            laneLine.transform.localScale = new Vector3(0.05f, 0.02f, trackLength);
            var lr = laneLine.GetComponent<Renderer>();
            lr.material = new Material(Shader.Find("Standard"));
            lr.material.color = new Color(1f, 1f, 1f, 0.25f);
        }
    }


    private Material CreateColoredMaterial(int index)
    {
        var mat = new Material(Shader.Find("Standard"));
        Color color = (index % 6) switch
        {
            0 => Color.red,
            1 => Color.blue,
            2 => Color.green,
            3 => Color.yellow,
            4 => Color.magenta,
            _ => Color.cyan
        };
        mat.color = color;
        return mat;
    }
   
    private bool[,] GenerateObstacleGridWfc(int rows)
    {
        int lanes = laneCount;

        bool[,] hasObstacle = new bool[rows, lanes];

        // 각 row에서 쓸 수 있는 패턴들을 비트마스크로 정의 (1 = 장애물)
        // laneCount <= 8 정도를 가정
        List<int> patterns = new List<int>();

        // 0: 전부 비어 있음
        patterns.Add(0b0000);

        // 한 레인만 막힌 패턴
        if (lanes >= 1) patterns.Add(0b0001);
        if (lanes >= 2) patterns.Add(0b0010);
        if (lanes >= 3) patterns.Add(0b0100);
        if (lanes >= 4) patterns.Add(0b1000);

        // 인접 2레인 막기 (조금 어려운 구간)
        if (lanes >= 2) patterns.Add(0b0011);
        if (lanes >= 3) patterns.Add(0b0110);
        if (lanes >= 4) patterns.Add(0b1100);

        // 3레인 막고 한 레인만 열어두는 패턴 (상당히 어려운 구간)
        if (lanes >= 3) patterns.Add(0b0111);
        if (lanes >= 4) patterns.Add(0b1110);

        // 각 row에서 "도달 가능한 레인" 집합을 추적
        bool[] reachablePrev = new bool[lanes];
        bool[] reachableNext = new bool[lanes];

        // 시작 구간: 모든 레인이 도달 가능하다고 가정
        for (int lane = 0; lane < lanes; lane++)
            reachablePrev[lane] = true;

        for (int row = 0; row < rows; row++)
        {
            // 패턴 순서를 랜덤으로 섞어서 다양한 맵이 나오도록
            List<int> shuffled = new List<int>(patterns);
            for (int i = 0; i < shuffled.Count; i++)
            {
                int swap = Random.Range(i, shuffled.Count);
                (shuffled[i], shuffled[swap]) = (shuffled[swap], shuffled[i]);
            }

            bool rowAssigned = false;

            foreach (int mask in shuffled)
            {
                // lanes 수에 맞게 마스크 잘라주기
                int effectiveMask = mask & ((1 << lanes) - 1);

                // 모든 레인이 막힌 패턴은 사용하지 않음
                if (effectiveMask == ((1 << lanes) - 1))
                    continue;

                // 이번 row에 이 패턴을 임시로 적용
                for (int lane = 0; lane < lanes; lane++)
                    hasObstacle[row, lane] = ((effectiveMask >> lane) & 1) == 1;

                // reachableNext 계산 초기화
                for (int lane = 0; lane < lanes; lane++)
                    reachableNext[lane] = false;

                // 이전 row에서 도달 가능했던 레인 기준으로,
                // 현재 row에서 같은 레인, 좌/우 한 칸까지 이동 가능하다고 가정
                for (int lane = 0; lane < lanes; lane++)
                {
                    if (!reachablePrev[lane]) continue;

                    // 같은 레인
                    if (!hasObstacle[row, lane])
                        reachableNext[lane] = true;

                    // 좌/우 인접 레인
                    int left = lane - 1;
                    int right = lane + 1;

                    if (left >= 0 && !hasObstacle[row, left])
                        reachableNext[left] = true;

                    if (right < lanes && !hasObstacle[row, right])
                        reachableNext[right] = true;
                }

                // 이 패턴 적용 후에도 한 레인도 도달 불가능하면 → 이 패턴 버리고 다음 패턴 시도
                bool anyReachable = false;
                for (int lane = 0; lane < lanes; lane++)
                {
                    if (reachableNext[lane])
                    {
                        anyReachable = true;
                        break;
                    }
                }

                if (!anyReachable)
                    continue;

                // OK 패턴: 이 row는 이 패턴으로 확정
                (reachablePrev, reachableNext) = (reachableNext, reachablePrev);
                rowAssigned = true;
                break;
            }

            // 만약 어떤 패턴도 안 맞으면, 안전장치: 이 row 전부 비우기
            if (!rowAssigned)
            {
                for (int lane = 0; lane < lanes; lane++)
                {
                    hasObstacle[row, lane] = false;
                    reachablePrev[lane] = true;
                }
            }
        }

        return hasObstacle;
    }

    // 기존 CreateObstacles() 전체 교체
    private void CreateObstacles()
    {
        // 1) 이 플레이에서 장애물 배치할 Z 구간 계산
        startObstacleZ = startZ + obstacleStartOffset;
        endObstacleZ = startZ + trackLength - obstacleEndMargin;
        obstacleZRange = endObstacleZ - startObstacleZ;

        if (obstacleZRange <= 0f)
        {
            Debug.LogWarning("장애물을 배치할 Z 구간이 없습니다.");
            return;
        }

        // 2) 이 길이를 obstacleRowSpacing 간격으로 나누어 row 개수 계산
        int rows = Mathf.CeilToInt(obstacleZRange / obstacleRowSpacing);
        if (rows <= 0) rows = 1;

        // 3) WFC 스타일로 전체 구간 그리드 생성 (시드에만 의존)
        obstacleGrid = GenerateObstacleGridWfc(rows);

        // 4) 각 row의 실제 z 위치 기록
        obstacleRowZ = new float[rows];
        for (int row = 0; row < rows; row++)
        {
            obstacleRowZ[row] = startObstacleZ + row * obstacleRowSpacing;
        }

        // 5) 런타임에 생성된 장애물 목록 초기화
        obstacleRowObjects.Clear();

        // 6) 처음 보이는 구간 설정
        visibleBackZ = startObstacleZ;
        visibleFrontZ = Mathf.Min(startObstacleZ + FORWARD_ADD_LENGTH, endObstacleZ);

        // 7) 처음 500 유닛(또는 끝까지가 500보다 짧다면 거기까지)만 실제로 생성
        SpawnObstacleRowsInRange(visibleBackZ, visibleFrontZ);
    }

    /// <summary>
    /// [fromZ, toZ] 구간에 해당하는 장애물 row를 실제 GameObject로 생성한다.
    /// 이미 생성된 row는 건드리지 않는다.
    /// </summary>
    private void SpawnObstacleRowsInRange(float fromZ, float toZ)
    {
        if (obstacleGrid == null || obstacleRowZ == null)
            return;

        int rows = obstacleGrid.GetLength(0);

        float totalWidth = laneCount * laneWidth;
        float leftMostX = -totalWidth * 0.5f + laneWidth * 0.5f;

        for (int row = 0; row < rows; row++)
        {
            float rowZ = obstacleRowZ[row];
            if (rowZ < fromZ || rowZ > toZ)
                continue;

            // 이미 생성된 row라면 스킵
            if (obstacleRowObjects.ContainsKey(row))
                continue;

            var list = new List<GameObject>();
            float curveOffset = GetCurveOffset(rowZ);

            for (int lane = 0; lane < laneCount; lane++)
            {
                // 이 칸은 WFC 그리드에서 비워둔 칸이면 넘어감
                if (!obstacleGrid[row, lane])
                    continue;

                // 전체 밀도 조절
                if (Random.value > obstacleDensity)
                    continue;

                float laneBaseX = leftMostX + lane * laneWidth;

                // 레인 안에서 약간 좌우 랜덤
                float offsetX = Random.Range(-laneWidth * 0.25f, laneWidth * 0.25f);
                float finalX = laneBaseX + offsetX + curveOffset;
                Vector3 pos = new Vector3(finalX, 0.5f, rowZ);

                GameObject obs;
                float roll = Random.value;

                if (roll < 0.45f)
                {
                    // 1) 원기둥 범퍼
                    obs = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    obs.transform.position = pos;
                    obs.transform.localScale = new Vector3(0.9f, 0.5f, 0.9f);
                }
                else if (roll < 0.8f)
                {
                    // 2) 낮은 경사/범프
                    obs = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    obs.transform.position = new Vector3(pos.x, 0.2f, pos.z);
                    obs.transform.localScale = new Vector3(laneWidth * 0.8f, 0.3f, 2.0f);
                    obs.transform.rotation = Quaternion.Euler(
                        Random.Range(8f, 15f),
                        Random.Range(-10f, 10f),
                        0f);
                }
                else
                {
                    // 3) 대각선 바
                    obs = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    obs.transform.position = new Vector3(pos.x, 0.4f, pos.z);
                    obs.transform.localScale = new Vector3(laneWidth * 1.1f, 0.5f, 0.6f);
                    float yaw = Random.value < 0.5f ? -25f : 25f;
                    obs.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                }

                var rb = obs.AddComponent<Rigidbody>();
                rb.isKinematic = true;

                // 회전형 장애물(옵션)
                if (roll > 0.45f && Random.value < 0.2f)
                {
                    var rot = obs.AddComponent<Rotator>();
                    rot.rotationAxis = new Vector3(0f, 1f, 0f);
                    rot.rotationSpeed = Random.Range(20f, 60f);
                }

                list.Add(obs);
            }

            if (list.Count > 0)
                obstacleRowObjects[row] = list;
        }
    }

    /// <summary>
    /// minZ 보다 뒤(작은 z)에 있는 장애물 row들을 전부 삭제한다.
    /// </summary>
    private void DespawnObstacleRowsBefore(float minZ)
    {
        if (obstacleRowZ == null)
            return;

        List<int> rowsToRemove = new List<int>();

        foreach (var kv in obstacleRowObjects)
        {
            int row = kv.Key;
            float rowZ = obstacleRowZ[row];

            if (rowZ < minZ)
            {
                foreach (var go in kv.Value)
                {
                    if (go != null)
                        Destroy(go);
                }
                rowsToRemove.Add(row);
            }
        }

        foreach (int row in rowsToRemove)
        {
            obstacleRowObjects.Remove(row);
        }
    }

    /// <summary>
    /// 플레이어(기준 구슬) 위치를 기준으로
    /// 1) 앞으로 남은 물리 맵 길이가 200 미만이면 500만큼 더 생성
    /// 2) 뒤쪽이 100 이상 멀어지면 20만 남기고 삭제
    /// </summary>
    private void UpdateObstacleStreaming()
    {
        if (obstacleGrid == null || obstacleRowZ == null)
            return;

        // 기준 구슬 보정
        if (focusMarble == null && marbles.Count > 0)
            focusMarble = marbles[0];
        if (focusMarble == null)
            return;

        float playerZ = focusMarble.transform.position.z;

        // ─────────────────────────────
        // 1) 앞쪽: 남은 길이 < 200 → 500 더 생성
        // ─────────────────────────────
        float forwardRemaining = visibleFrontZ - playerZ;
        if (forwardRemaining < FORWARD_NEED_THRESHOLD)
        {
            float targetFront = visibleFrontZ + FORWARD_ADD_LENGTH;
            float maxFront = endObstacleZ;
            float newFront = Mathf.Min(targetFront, maxFront);

            if (newFront > visibleFrontZ + 0.1f)
            {
                SpawnObstacleRowsInRange(visibleFrontZ, newFront);
                visibleFrontZ = newFront;
            }
        }

        // ─────────────────────────────
        // 2) 뒤쪽: 플레이어와의 거리 > 100 → 20만 남기고 삭제
        // ─────────────────────────────
        float backDistance = playerZ - visibleBackZ;
        if (backDistance > BACK_MAX_DISTANCE)
        {
            float newBack = playerZ - BACK_KEEP_DISTANCE;
            newBack = Mathf.Max(newBack, startObstacleZ);

            if (newBack > visibleBackZ + 0.1f)
            {
                DespawnObstacleRowsBefore(newBack);
                visibleBackZ = newBack;
            }
        }
    }

    private void CreateFinishTrigger()
    {
        float totalWidth = laneCount * laneWidth;

        // 트랙이 휘어지는 만큼 피니시의 x 위치도 맞춰준다
        float curveOffset = GetCurveOffset(finishZ);

        // ===== 1) 눈에 보이는 피니시 라인 (노란 바 + 트리거) =====
        var finishGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        finishGo.name = "FinishLine";
        finishGo.transform.position = new Vector3(curveOffset, 0.25f, finishZ);
        finishGo.transform.localScale = new Vector3(totalWidth + 2f, 0.5f, 0.5f);

        var box = finishGo.GetComponent<BoxCollider>();
        box.isTrigger = true; // 이 큐브 전체가 트리거

        // 색 눈에 띄게
        var rend = finishGo.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(1f, 0.9f, 0.2f); // 노란색 계열
        mat.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.1f));
        rend.material = mat;

        // FinishTrigger 스크립트 붙이기
        var finishTrigger = finishGo.AddComponent<FinishTrigger>();
        finishTrigger.manager = this;

        // ===== 2) 피니시 뒤쪽에 '엔드 벽' 설치해서 더 이상 떨어지지 않게 =====
        float endWallZ = finishZ + 4f;

        var endWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        endWall.name = "EndWall";
        endWall.transform.position = new Vector3(curveOffset, 1.5f, endWallZ);
        endWall.transform.localScale = new Vector3(totalWidth + 4f, 3f, 1f);

        // 살짝 회색으로
        var endWallRenderer = endWall.GetComponent<Renderer>();
        var endWallMat = new Material(Shader.Find("Standard"));
        endWallMat.color = new Color(0.9f, 0.9f, 0.9f);
        endWallRenderer.material = endWallMat;

        // EndWall는 일반 콜라이더(트리거 아님) 그대로 둬서 구슬이 여기서 멈춤
    }

    private void CreateCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera");
            cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
        }

        // 카메라 기본 세팅
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;

        // 기본 위치는 코스를 비스듬히 내려다보는 각도
        cam.transform.position = new Vector3(0f, trackLength * 0.35f, -40f);
        cam.transform.LookAt(new Vector3(0f, 0f, trackLength * 0.6f));
        cam.fieldOfView = 50f;

        // 카메라가 1번(빨간) 구슬을 따라가게 설정
        var follow = cam.gameObject.AddComponent<CameraFollow>();
        if (marbles.Count > 0)
        {
            follow.target = marbles[0].transform; // Marble_1 (빨간 구슬)
        }
    }

    private void CreateStartUI()
    {
        // EventSystem 없으면 자동 생성
        if (FindObjectOfType<EventSystem>() == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
        }

        startCanvas = new GameObject("StartCanvas").AddComponent<Canvas>();
        startCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = startCanvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);

        startCanvas.gameObject.AddComponent<GraphicRaycaster>();

        // 어두운 반투명 패널
        var panel = new GameObject("Panel");
        panel.transform.SetParent(startCanvas.transform, false);
        var img = panel.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.5f);
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        int seed = Random.Range(0, 99999);

        // ===== 시드 =====
        CreateLabel("시드 (Seed, 아무 정수 값)", new Vector2(0, 260));
        var seedInput = CreateInput("Seed", "예: 12345", seed.ToString(), new Vector2(0, 200));

        // ===== 레인 수 =====
        CreateLabel("레인 수 (Lanes, 2 ~ 8)", new Vector2(0, 120));
        var laneInput = CreateInput("Lanes", "예: 4", laneCount.ToString(), new Vector2(0, 60));

        // ===== 트랙 길이 =====
        CreateLabel("트랙 길이 (Length, 100 ~ 2000)", new Vector2(0, -20));
        var lengthInput = CreateInput("Length", "예: 500", trackLength.ToString(), new Vector2(0, -80));

        // ===== START 버튼 =====
        var startBtn = CreateButton("START", new Vector2(0, -200));
        startBtn.onClick.AddListener(() =>
        {
            int s = int.Parse(seedInput.text);
            int l = int.Parse(laneInput.text);
            float len = float.Parse(lengthInput.text);

            Destroy(startCanvas.gameObject);
            StartRace(s, l, len);
        });

        // 처음 포커스는 시드 입력칸
        seedInput.Select();
        seedInput.ActivateInputField();
    }

    private InputField CreateInput(string name, string placeholder, string value, Vector2 pos)
    {
        var go = new GameObject(name,
            typeof(RectTransform),
            typeof(Image),
            typeof(InputField));
        go.transform.SetParent(startCanvas.transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(600, 70);  // ★ 입력 칸 크게
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;

        var image = go.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.25f);

        var input = go.GetComponent<InputField>();
        input.targetGraphic = image;

        Font font = GetUIFont();   // OS Arial에서 가져오는 함수

        // ===== 실제 입력 텍스트 =====
        var textGO = new GameObject("Text",
            typeof(RectTransform),
            typeof(Text));
        textGO.transform.SetParent(go.transform, false);

        var textRT = textGO.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(20, 10);
        textRT.offsetMax = new Vector2(-20, -10);

        var text = textGO.GetComponent<Text>();
        text.font = font;
        text.fontSize = 30;                 // ★ 폰트 크게
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.supportRichText = false;

        // ===== Placeholder =====
        var phGO = new GameObject("Placeholder",
            typeof(RectTransform),
            typeof(Text));
        phGO.transform.SetParent(go.transform, false);

        var phRT = phGO.GetComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(20, 10);
        phRT.offsetMax = new Vector2(-20, -10);

        var ph = phGO.GetComponent<Text>();
        ph.font = font;
        ph.fontSize = 26;
        ph.text = placeholder;              // 예: "예: 500"
        ph.color = new Color(1f, 1f, 1f, 0.45f);
        ph.alignment = TextAnchor.MiddleLeft;
        ph.supportRichText = false;

        input.textComponent = text;
        input.placeholder = ph;
        input.text = value;

        return input;
    }

    private void CreateLabel(string labelText, Vector2 pos)
    {
        var go = new GameObject(labelText + "_Label",
            typeof(RectTransform),
            typeof(Text));
        go.transform.SetParent(startCanvas.transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(800, 40);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;

        var txt = go.GetComponent<Text>();
        txt.font = GetUIFont();
        txt.fontSize = 26;
        txt.text = labelText;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleCenter;
    }

    private Button CreateButton(string label, Vector2 pos)
    {
        var go = new GameObject(label,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));

        go.transform.SetParent(startCanvas.transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(200, 50);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;

        var img = go.GetComponent<Image>();
        img.color = Color.white;

        var btn = go.GetComponent<Button>();

        Font font = GetUIFont();

        var txtGO = new GameObject("Text",
            typeof(RectTransform),
            typeof(Text));
        txtGO.transform.SetParent(go.transform, false);

        var txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;

        var txt = txtGO.GetComponent<Text>();
        txt.font = font;
        txt.fontSize = 20;
        txt.text = label;
        txt.color = Color.black;
        txt.alignment = TextAnchor.MiddleCenter;

        return btn;
    }

    /// <summary>
    /// 화면 오른쪽 상단에 미니맵 UI + 전용 카메라 생성
    /// </summary>
    private void CreateMinimap()
    {
        // Canvas 생성
        if (raceCanvas == null)
        {
            var canvasGO = new GameObject("RaceCanvas");
            raceCanvas = canvasGO.AddComponent<Canvas>();
            raceCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            canvasGO.AddComponent<GraphicRaycaster>();
        }

        // 미니맵 배경
        var bgGO = new GameObject("MinimapBG",
            typeof(RectTransform),
            typeof(Image));
        bgGO.transform.SetParent(raceCanvas.transform, false);

        minimapRect = bgGO.GetComponent<RectTransform>();
        minimapRect.anchorMin = minimapRect.anchorMax = new Vector2(1f, 1f);
        minimapRect.pivot = new Vector2(1f, 1f);
        minimapRect.sizeDelta = new Vector2(180f, 300f);
        minimapRect.anchoredPosition = new Vector2(-20f, -20f);

        var bgImg = bgGO.GetComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.5f);

        // 트랙 중앙선 (단순 선)
        var lineGO = new GameObject("TrackLine",
            typeof(RectTransform),
            typeof(Image));
        lineGO.transform.SetParent(minimapRect, false);

        var lineRT = lineGO.GetComponent<RectTransform>();
        lineRT.anchorMin = new Vector2(0.5f, 0f);
        lineRT.anchorMax = new Vector2(0.5f, 1f);
        lineRT.sizeDelta = new Vector2(3f, 0f);

        var lineImg = lineGO.GetComponent<Image>();
        lineImg.color = Color.white;

        // 구슬 아이콘 생성
        foreach (var marble in marbles)
        {
            CreateMinimapIcon(marble);
        }
    }

    private void CreateMinimapIcon(Marble marble)
    {
        var iconGO = new GameObject(
            marble.name + "_MinimapIcon",
            typeof(RectTransform),
            typeof(Image));

        iconGO.transform.SetParent(minimapRect, false);

        var rt = iconGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(14f, 14f);

        var img = iconGO.GetComponent<Image>();
        img.color = marble.GetComponent<Renderer>().material.color;

        minimapIcons.Add(marble, rt);
    }

    private float targetTrackLength;   // 유저가 입력한 최종 길이
    private Dictionary<int, GameObject> chunks = new();
    private const float ChunkLength = 500f;  // 청크 하나의 길이 (지금은 더미값)
    private const float keepBehind = 20f;    // 플레이어 뒤로 이만큼만 남기고 삭제 (지금은 사용 X)

    private void Update()
    {
        // 1) 미니맵 아이콘 위치 갱신
        UpdateMinimapIcons();

        // 2) 기준 구슬 확보
        if (focusMarble == null && marbles.Count > 0)
            focusMarble = marbles[0];

        if (focusMarble == null)
            return;

        float z = focusMarble.transform.position.z;

        // 3) 장애물 스트리밍 (앞/뒤 생성·삭제)
        UpdateObstacleStreaming();

        // 4) Ground / Wall 청크 스트리밍
        //    - 플레이어 주변 몇 개만 유지
        if (targetTrackLength <= 0f)
            return;

        int maxChunkIndex = Mathf.FloorToInt((targetTrackLength - 0.01f) / ChunkLength);
        if (maxChunkIndex < 0)
            return;

        // startZ를 기준으로 현재 청크 인덱스 계산
        int currentChunk = Mathf.FloorToInt((z - startZ) / ChunkLength);
        currentChunk = Mathf.Clamp(currentChunk, 0, maxChunkIndex);

        // 앞뒤로 어느 정도 범위를 유지할지
        int desiredMin = Mathf.Max(0, currentChunk - 1);
        int desiredMax = Mathf.Min(maxChunkIndex, currentChunk + 2);

        // 필요 청크 생성
        for (int i = desiredMin; i <= desiredMax; i++)
        {
            if (!chunks.ContainsKey(i))
            {
                CreateChunk(i);
            }
        }

        // 너무 먼 청크는 삭제
        List<int> toRemove = new List<int>();
        foreach (var kv in chunks)
        {
            int idx = kv.Key;
            float chunkStartZ = startZ + idx * ChunkLength;
            float chunkEndZ = chunkStartZ + ChunkLength;

            // 플레이어 기준 뒤쪽으로 keepBehind 이상 멀어졌으면 제거
            if (chunkEndZ < z - keepBehind)
            {
                if (kv.Value != null)
                    Destroy(kv.Value);
                toRemove.Add(idx);
            }
        }

        foreach (int idx in toRemove)
            chunks.Remove(idx);
    }


    /// <summary>
    /// 청크 한 개(500 단위)에 대한
    ///  - 흰색 트랙 바닥
    ///  - 좌/우 벽
    /// 을 생성합니다.
    /// </summary>
    private void CreateChunk(int index)
    {
        if (chunks.ContainsKey(index))
            return;

        // 이 청크가 담당하는 Z 범위
        float zStart = startZ + index * ChunkLength;
        float zEnd = zStart + ChunkLength;

        // 이 플레이의 트랙 전체 끝
        float trackEndZ = startZ + targetTrackLength;

        // 트랙 범위를 완전히 벗어나면 생성 X
        if (zStart > trackEndZ)
            return;

        // 마지막 청크는 트랙 끝까지만 잘라서 사용
        if (zEnd > trackEndZ)
            zEnd = trackEndZ;

        float segmentLength = zEnd - zStart;
        if (segmentLength <= 0f)
            return;

        // 청크 루트 오브젝트
        var chunkRoot = new GameObject($"Chunk_{index}");
        chunks[index] = chunkRoot;

        // ─────────────────────────────
        // 1) 흰색 트랙 바닥
        // ─────────────────────────────
        float totalWidth = laneCount * laneWidth;
        float maxCurveAbs = laneWidth * (laneCount * 1.0f + 3f);
        float halfTrackWidth = totalWidth * 0.5f + maxCurveAbs + 4f;   // CreateGround와 동일 공식

        float centerZ = (zStart + zEnd) * 0.5f;

        var trackGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trackGround.name = $"TrackGround_{index}";
        trackGround.transform.SetParent(chunkRoot.transform, false);
        trackGround.transform.position = new Vector3(0f, -0.25f, centerZ);
        trackGround.transform.localScale = new Vector3(
            halfTrackWidth * 2f,
            0.5f,
            segmentLength      // 이 청크가 담당하는 z 길이만큼
        );

        var trackMat = new Material(Shader.Find("Standard"));
        trackMat.color = Color.white;
        trackGround.GetComponent<Renderer>().material = trackMat;

        var rb = trackGround.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        // ─────────────────────────────
        // 2) 좌/우 벽 생성 (이 청크 구간에 해당하는 부분만)
        // ─────────────────────────────
        CreateWallsForChunk(chunkRoot.transform, index, zStart, zEnd);
    }

    /// <summary>
    /// 특정 z 구간 [zStart, zEnd]에 대해
    /// 좌/우 벽을 한 청크 단위로 생성합니다.
    /// </summary>
    private void CreateWallsForChunk(Transform parent, int chunkIndex, float zStart, float zEnd)
    {
        float totalWidth = laneCount * laneWidth;
        float halfTrackWidth = totalWidth * 0.5f;

        // 트랙 중앙에서 레인 밖으로 약간 여유를 둔 위치에 벽을 세움
        float wallOffsetFromCenter = halfTrackWidth + laneWidth * 0.5f;

        // 이 구간 안에서 몇 개의 샘플로 곡선을 따라갈지
        // (대략 5 유닛마다 하나씩 샘플)
        int samples = Mathf.Max(4, Mathf.CeilToInt((zEnd - zStart) / 5f));
        float dz = (zEnd - zStart) / samples;

        CreateWallSideSegment(
            parent,
            $"LeftWall_{chunkIndex}",
            +1f,
            wallOffsetFromCenter,
            zStart,
            zEnd,
            dz,
            samples
        );

        CreateWallSideSegment(
            parent,
            $"RightWall_{chunkIndex}",
            -1f,
            wallOffsetFromCenter,
            zStart,
            zEnd,
            dz,
            samples
        );
    }

    /// <summary>
    /// 하나의 벽(좌 또는 우)에 대한 메쉬 + 콜라이더를
    /// [zStart, zEnd] 구간만큼 생성합니다.
    /// </summary>
    private void CreateWallSideSegment(
        Transform parent,
        string name,
        float sideSign,
        float offsetFromCenter,
        float zStart,
        float zEnd,
        float dz,
        int samples)
    {
        int vertCount = (samples + 1) * 2;
        Vector3[] vertices = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        int[] triangles = new int[samples * 6];

        for (int i = 0; i <= samples; i++)
        {
            float z = zStart + dz * i;
            if (z > zEnd) z = zEnd;

            // 트랙 중앙선 위치
            float centerX = GetCurveOffset(z);
            Vector3 center = new Vector3(centerX, 0f, z);

            // 진행 방향(접선) 계산용 샘플
            float z2 = z + 0.5f;
            if (z2 > zEnd) z2 = z - 0.5f;
            if (z2 < zStart) z2 = z;

            float centerX2 = GetCurveOffset(z2);
            Vector3 center2 = new Vector3(centerX2, 0f, z2);

            Vector3 tangent = (center2 - center).normalized;
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.forward;

            // 진행 방향 기준 왼쪽 벡터
            Vector3 perpLeft = new Vector3(-tangent.z, 0f, tangent.x).normalized;
            Vector3 sideDir = (sideSign > 0f) ? perpLeft : -perpLeft;

            // 실제 벽 위치
            Vector3 basePos = center + sideDir * offsetFromCenter;

            int v = i * 2;

            vertices[v + 0] = basePos;
            vertices[v + 1] = basePos + Vector3.up * wallHeight;

            float u = (float)i / samples;
            uvs[v + 0] = new Vector2(u, 0f);
            uvs[v + 1] = new Vector2(u, 1f);
        }

        int ti = 0;
        for (int i = 0; i < samples; i++)
        {
            int v = i * 2;
            int vn = (i + 1) * 2;

            // 아래-위-다음아래
            triangles[ti++] = v + 0;
            triangles[ti++] = v + 1;
            triangles[ti++] = vn + 0;

            // 다음아래-위-다음위
            triangles[ti++] = vn + 0;
            triangles[ti++] = v + 1;
            triangles[ti++] = vn + 1;
        }

        var wallGo = new GameObject(name);
        wallGo.transform.SetParent(parent, false);
        wallGo.transform.position = Vector3.zero;
        wallGo.transform.rotation = Quaternion.identity;

        var mf = wallGo.AddComponent<MeshFilter>();
        var mr = wallGo.AddComponent<MeshRenderer>();

        var mesh = new Mesh { name = name + "_Mesh" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        mf.sharedMesh = mesh;

        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.8f, 0.8f, 0.8f);
        mr.sharedMaterial = mat;

        // ─────────────────────────────
        // 콜라이더 세그먼트들
        // ─────────────────────────────
        float thickness = 0.6f;   // 벽 두께

        for (int i = 0; i < samples; i++)
        {
            int v = i * 2;
            int vn = (i + 1) * 2;

            Vector3 p0 = vertices[v + 0];   // 현재 구간 바닥점
            Vector3 p1 = vertices[vn + 0];  // 다음 구간 바닥점

            Vector3 dir = p1 - p0;
            float length = dir.magnitude;
            if (length < 0.001f) continue;

            Vector3 center = (p0 + p1) * 0.5f;

            var segObj = new GameObject($"{name}_Col_{i}");
            segObj.transform.SetParent(wallGo.transform, false);

            segObj.transform.position = center + Vector3.up * (wallHeight * 0.5f);
            segObj.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

            var boxCol = segObj.AddComponent<BoxCollider>();
            boxCol.size = new Vector3(thickness, wallHeight, length);
        }
    }

    private void UpdateMinimapIcons()
    {
        if (minimapRect == null) return;
        if (marbles.Count == 0) return;

        // 기준 구슬(플레이어) 없으면 0번 구슬로 다시 잡기
        if (focusMarble == null)
        {
            focusMarble = marbles[0];
            if (focusMarble == null) return;
        }

        float mapWidth = minimapRect.rect.width;
        float mapHeight = minimapRect.rect.height;

        // 미니맵의 중심 기준 위치 (UI 좌표에서 0,0 이 부모의 중심)
        Vector2 mapCenter = Vector2.zero;

        Vector3 centerPos = focusMarble.transform.position;

        foreach (var kv in minimapIcons)
        {
            Marble marble = kv.Key;
            RectTransform icon = kv.Value;
            if (marble == null || icon == null) continue;

            Vector3 pos = marble.transform.position;

            // 기준 구슬 기준 상대 위치 (월드)
            float dx = pos.x - centerPos.x;
            float dz = pos.z - centerPos.z;

            // 월드 범위를 [-1,1]로 정규화
            float nx = dx / minimapWorldHalfWidth;
            float nz = dz / minimapWorldHalfHeight;

            // [-1,1] 범위로 클램프 → 범위 밖이면 미니맵 가장자리로 붙음
            nx = Mathf.Clamp(nx, -1f, 1f);
            nz = Mathf.Clamp(nz, -1f, 1f);

            // UI 좌표로 변환 (가로/세로 절반 * 정규값)
            float x = nx * (mapWidth * 0.5f);
            float y = nz * (mapHeight * 0.5f);

            icon.anchoredPosition = mapCenter + new Vector2(x, y);
        }
    }
    
    public void OnMarbleFinished(Marble marble)
    {
        // 같은 구슬이 여러 번 트리거에 들어와도 한 번만 처리
        if (finishedMarbles.Contains(marble))
            return;

        finishedMarbles.Add(marble);

        // 1등 한 번만 발표
        if (!winnerAnnounced)
        {
            winnerAnnounced = true;
            Debug.Log($"🏁 Winner: Lane {marble.laneIndex + 1} ({marble.gameObject.name})");
        }

        // 모든 구슬이 결승선 통과했는지 확인
        if (finishedMarbles.Count >= marbles.Count)
        {
            Debug.Log("모든 구슬이 결승선에 도착했습니다. 3초 후 다시 시작합니다.");
            Invoke(nameof(ReloadScene), 3f);   // 3초 후 씬 리셋
        }
    }

    private void ReloadScene()
    {
        // 현재 활성 씬 이름을 다시 로드 → Start()가 다시 실행되고
        // 처음 시드 입력 UI 상태로 돌아감
        Scene current = SceneManager.GetActiveScene();
        SceneManager.LoadScene(current.name);
    }
}