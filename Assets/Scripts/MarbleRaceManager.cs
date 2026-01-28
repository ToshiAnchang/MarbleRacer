using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;


public class MarbleRaceManager : MonoBehaviour
{
    public static MarbleRaceManager Instance { get; private set; }

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

    [Header("Walls / Safety")]
    public float wallHeight = 4.5f;        // ★ 기존 2f 정도였던 걸 4 정도로 올림
    public float maxMarbleHeight = 2f;   // ★ 구슬이 올라갈 수 있는 최대 높이

    [Range(0f, 1f)]
    public float obstacleDensity = 0.5f;   // ★ 한 칸을 실제로 채울 확률 (0.5면 절반 정도)

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
        trackLength = Mathf.Clamp(length, 100f, 2000f);
        finishZ = trackLength - 40f;

        Random.InitState(seed);

        //👇레이스상태 초기화
        winnerAnnounced = false;
        finishedMarbles = new HashSet<Marble>();

        GenerateTrackCurve();
        CreateGround();
        CreateWalls();
        CreateLanesAndMarbles();
        CreateObstacles();
        CreateFinishTrigger();

        CreateOuterShell();

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
    /// </summary>
    private void GenerateTrackCurve()
    {
        // 코너 개수 (값이 클수록 코너가 많아지고 복잡해짐)
        int cornerCount = 12;

        cornerZ = new float[cornerCount];
        cornerX = new float[cornerCount];

        float start = startZ;
        float end = startZ + trackLength;

        // 트랙 중앙 기준에서 좌우로 최대 얼마나 휘어질지
        // laneWidth * (레인 절반 + 여유) 정도
        float maxOffset = laneWidth * (laneCount * 0.5f + 1.5f);

        for (int i = 0; i < cornerCount; i++)
        {
            float t = (float)i / (cornerCount - 1);

            // z 위치는 트랙 전체에 균등하게 분포
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
                if (Mathf.Abs(x - prev) < laneWidth) // 차이가 너무 작으면
                {
                    x += Mathf.Sign(x - prev) * laneWidth * 1.2f;
                }

                cornerX[i] = Mathf.Clamp(x, -maxOffset, maxOffset);
            }
        }
    }


    /// <summary>
    ///   -긴 슬로프 바닥 생성 (시각적으로는 직선, 곡선 느낌은 벽/장애물/레인으로 연출)
    ///   -곡선 트랙 바닥 생성 (세그먼트 방식)
    /// </summary>
    private void CreateGround()
    {
        int segments = 80;
        float segmentLength = trackLength / segments;
        float totalWidth = laneCount * laneWidth;

        // ─────────────────────────────
        // 1) 시각용 타일 세그먼트 (콜라이더 제거)
        //    → 서로 더 많이 겹치게 해서 틈 안 보이게
        // ─────────────────────────────
        for (int i = 0; i < segments; i++)
        {
            float zCenter = startZ + (i + 0.5f) * segmentLength;
            float curveOffset = GetCurveOffset(zCenter);

            var groundSeg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundSeg.name = $"Ground_{i}";
            groundSeg.transform.position = new Vector3(curveOffset, -0.25f, zCenter);

            // ① 좌우 폭을 넉넉히 → 벽보다 충분히 넓게
            // ② 앞뒤 길이를 1.4배 → 서로 많이 겹치게 해서 틈 방지
            groundSeg.transform.localScale = new Vector3(
                totalWidth + 6f,
                0.5f,
                segmentLength * 1.4f
            );

            // 이 세그먼트는 "그림"만 담당 → 물리 충돌 X
            Destroy(groundSeg.GetComponent<Collider>());
        }

        // ─────────────────────────────
        // 2) 실제 물리용 바닥 콜라이더 한 장
        // ─────────────────────────────
        var groundCol = new GameObject("GroundCollider");
        groundCol.transform.position = new Vector3(0f, -0.25f, startZ + trackLength * 0.5f);

        // GetCurveOffset에서 쓰는 최대 진폭 대충 계산
        float maxCurveAbs = laneWidth * (2f + 1.2f * 0.5f); // amp1 + amp2 * 0.5f ≒ 2.6 * laneWidth
        float halfWidth = totalWidth * 0.5f + maxCurveAbs + 2f; // 레인 폭 + 곡선 여유 + 안전 마진

        var box = groundCol.AddComponent<BoxCollider>();
        box.size = new Vector3(halfWidth * 2f, 1.0f, trackLength + 20f); // X를 넉넉하게, Z도 여유 있게

        var rb = groundCol.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        // ─────────────────────────────
        // 3) 트랙 바깥은 전부 "검은 바닥"으로 깔아버리기
        //    → 구조물 삐져나온 거 / 틈 아래가 다 검정으로 보이게
        // ─────────────────────────────
        var voidGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        voidGo.name = "OuterVoid";

        float voidSizeX = halfWidth * 4f;
        float voidSizeZ = trackLength * 4f;

        // 살짝 더 아래로 내려서 실제 바닥 아래에 깔기
        voidGo.transform.position = new Vector3(0f, -1.0f, startZ + trackLength * 0.5f);
        voidGo.transform.localScale = new Vector3(voidSizeX, 1.0f, voidSizeZ);

        var voidMat = new Material(Shader.Find("Standard"));
        voidMat.color = Color.black;
        var voidRenderer = voidGo.GetComponent<Renderer>();
        voidRenderer.material = voidMat;

        // 이건 그냥 배경용이니까 콜라이더 제거
        Destroy(voidGo.GetComponent<Collider>());
    }



    // z 위치에 따라 트랙의 좌우 곡선 오프셋을 돌려주는 함수
    // z 위치에 따라 트랙의 좌우 곡선 오프셋을 돌려주는 함수
    private float GetCurveOffset(float z)
    {
        // 코너 정보가 아직 없으면 (안전장치) – 예전 사인파 방식으로 fallback
        if (cornerZ == null || cornerX == null || cornerZ.Length < 2)
        {
            float tFallback = z / trackLength;
            float amp1 = laneWidth * 2f;
            float amp2 = laneWidth * 1.2f;
            float curveFallback =
                Mathf.Sin(tFallback * Mathf.PI * 1.5f) * amp1 +
                Mathf.Sin(tFallback * Mathf.PI * 3f + 1.2f) * amp2 * 0.5f;
            return curveFallback;
        }

        float start = startZ;
        float end = startZ + trackLength;

        // 범위 밖이면 끝값 사용
        if (z <= cornerZ[0]) return cornerX[0];
        if (z >= cornerZ[cornerZ.Length - 1]) return cornerX[cornerZ.Length - 1];

        float t = Mathf.InverseLerp(start, end, z);             // 0~1
        int cornerCount = cornerZ.Length;

        // 전체 구간을 [0, cornerCount-1] 로 보고, 그 안에서 자신의 위치 찾기
        float f = t * (cornerCount - 1);
        int idx = Mathf.Clamp(Mathf.FloorToInt(f), 0, cornerCount - 2);
        float localT = f - idx;                                 // 0~1

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


    // 양옆 가드레일 생성 – "큐브 여러 개"가 아니라
    // 곡선을 따라 이어지는 한 장짜리 메쉬로 만든 벽
    private void CreateWalls()
    {
        float totalWidth = laneCount * laneWidth;
        float halfWidth = totalWidth * 0.5f;

        int samples = 160;                     // 샘플 개수 (값 높일수록 곡선이 더 부드러워짐)
        float dz = trackLength / samples;

        // 왼쪽/오른쪽 벽 각각 메쉬 생성
        CreateWallSide("LeftWall", +1f, halfWidth, dz, samples);
        CreateWallSide("RightWall", -1f, halfWidth, dz, samples);
    }

    /// <summary>
    /// sideSign : +1 → 왼쪽 벽, -1 → 오른쪽 벽
    /// halfWidth : 트랙 중앙에서 레인 반폭
    /// </summary>
    private void CreateWallSide(string name, float sideSign, float halfWidth, float dz, int samples)
    {
        // 🔹 각 z 샘플당 4개의 버텍스 (바깥 아래/위 + 안쪽 아래/위)
        int vertCount = (samples + 1) * 4;
        Vector3[] vertices = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];

        // 샘플당 6개의 삼각형(18 인덱스): 바깥면 2, 안쪽면 2, 옆면 2
        int[] triangles = new int[samples * 18];

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
            if (z2 > zEnd) z2 = z - 0.5f;   // 끝에서는 반대쪽으로 샘플링

            float centerX2 = GetCurveOffset(z2);
            Vector3 center2 = new Vector3(centerX2, 0f, z2);

            Vector3 tangent = (center2 - center).normalized;
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.forward;

            // 좌/우 방향 법선
            Vector3 normal = new Vector3(-tangent.z, 0f, tangent.x).normalized * sideSign;

            // 바깥/안쪽 바닥 위치
            Vector3 basePos = center + normal * (halfWidth + 0.2f);
            Vector3 basePosInner = basePos - normal * 0.25f; // 두께

            int v = i * 4;

            // 바깥면
            vertices[v + 0] = basePos;
            vertices[v + 1] = basePos + Vector3.up * wallHeight;

            // 안쪽면
            vertices[v + 2] = basePosInner;
            vertices[v + 3] = basePosInner + Vector3.up * wallHeight;

            // UV (길이 방향 u, 높이 방향 v)
            float u = (float)i / samples;
            uvs[v + 0] = new Vector2(u, 0f);
            uvs[v + 1] = new Vector2(u, 1f);
            uvs[v + 2] = new Vector2(u, 0f);
            uvs[v + 3] = new Vector2(u, 1f);
        }

        // 삼각형 인덱스
        int ti = 0;
        for (int i = 0; i < samples; i++)
        {
            int v = i * 4;
            int vn = (i + 1) * 4;

            // 1️⃣ 바깥면
            triangles[ti++] = v + 0;
            triangles[ti++] = v + 1;
            triangles[ti++] = vn + 0;

            triangles[ti++] = vn + 0;
            triangles[ti++] = v + 1;
            triangles[ti++] = vn + 1;

            // 2️⃣ 안쪽면 (반대 방향)
            triangles[ti++] = v + 2;
            triangles[ti++] = vn + 2;
            triangles[ti++] = v + 3;

            triangles[ti++] = vn + 2;
            triangles[ti++] = vn + 3;
            triangles[ti++] = v + 3;

            // 3️⃣ 옆면 (두께 연결)
            triangles[ti++] = v + 0;
            triangles[ti++] = vn + 0;
            triangles[ti++] = v + 2;

            triangles[ti++] = vn + 0;
            triangles[ti++] = vn + 2;
            triangles[ti++] = v + 2;
        }

        // 게임오브젝트 + 메쉬 컴포넌트 생성
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

        var mc = wallGo.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = false;
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

    /// <summary>
    /// WFC 스타일로 장애물 그리드를 생성한다.
    /// 결과: hasObstacle[row, lane] = true 이면 해당 칸에 장애물이 있음.
    /// 항상 최소 한 레인은 끝까지 도달 가능하도록 제약을 둔다.
    /// </summary>
    private bool[,] GenerateObstacleGridWfc()
    {
        int rows = obstacleRows;
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


    // 장애물 생성: WFC 스타일 그리드 결과를 이용해 배치
    // 장애물 생성: WFC 스타일 그리드 결과를 이용해 배치
    private void CreateObstacles()
    {
        float totalWidth = laneCount * laneWidth;
        float leftMostX = -totalWidth * 0.5f + laneWidth * 0.5f;

        // 시작/끝 z 범위 안에서만 장애물을 배치 (피니시 앞뒤는 비워두기)
        float startObstacleZ = startZ + 40f;
        float endObstacleZ = finishZ - 40f;
        float zRange = Mathf.Max(1f, endObstacleZ - startObstacleZ);

        // 여기서 WFC 방식으로 장애물 그리드 생성
        bool[,] hasObstacle = GenerateObstacleGridWfc();

        for (int row = 0; row < obstacleRows; row++)
        {
            float t = (row + 1f) / (obstacleRows + 1f);
            float baseZ = startObstacleZ + zRange * t;
            float curveOffset = GetCurveOffset(baseZ);

            for (int lane = 0; lane < laneCount; lane++)
            {
                // 이 칸은 원래 비워두기로 한 경우
                if (!hasObstacle[row, lane])
                    continue;

                // ★ 전체 밀도 조절: obstacleDensity 확률로만 실제 배치
                if (Random.value > obstacleDensity)
                    continue;

                float laneBaseX = leftMostX + lane * laneWidth;

                // 레인 안에서 약간 좌우 랜덤
                float offsetX = Random.Range(-laneWidth * 0.25f, laneWidth * 0.25f);
                float finalX = laneBaseX + offsetX + curveOffset;
                Vector3 pos = new Vector3(finalX, 0.5f, baseZ);

                GameObject obs;
                float roll = Random.value;

                // ===== 정육면체 “벽”은 제거하고, 모두 롤링 가능한 형태로만 구성 =====

                if (roll < 0.45f)
                {
                    // 1) 원기둥 범퍼 (구슬이 맞으면 튕겨나가는 기둥)
                    obs = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    obs.transform.position = pos;
                    obs.transform.localScale = new Vector3(0.9f, 0.5f, 0.9f);
                }
                else if (roll < 0.8f)
                {
                    // 2) 낮은 경사/범프 – 구슬이 올라탔다가 굴러내리는 느낌
                    obs = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    obs.transform.position = new Vector3(pos.x, 0.2f, pos.z);
                    obs.transform.localScale = new Vector3(laneWidth * 0.8f, 0.3f, 2.0f);
                    obs.transform.rotation = Quaternion.Euler(
                        Random.Range(8f, 15f),                    // 살짝 기울기
                        Random.Range(-10f, 10f),                  // 아주 약간 비틀기
                        0f);
                }
                else
                {
                    // 3) 대각선 바 – 살짝 방향만 꺾이게, 완전 차단은 아님
                    obs = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    obs.transform.position = new Vector3(pos.x, 0.4f, pos.z);
                    obs.transform.localScale = new Vector3(laneWidth * 1.1f, 0.5f, 0.6f);
                    float yaw = Random.value < 0.5f ? -25f : 25f;
                    obs.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                }

                var rb = obs.AddComponent<Rigidbody>();
                rb.isKinematic = true;

                // 회전형 장애물(옵션) – 개수도 줄어들게 확률 낮게
                if (roll > 0.45f && Random.value < 0.2f)
                {
                    var rot = obs.AddComponent<Rotator>();
                    rot.rotationAxis = new Vector3(0f, 1f, 0f);
                    rot.rotationSpeed = Random.Range(20f, 60f);
                }
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

    private Canvas startCanvas;

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
    private void Update()
    {
        UpdateMinimapIcons();
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

    /// <summary>
    /// 트랙 전체를 감싸는 큰 외곽 박스를 만든다.
    /// → 벽 너머 / 위쪽에 세상이 안 보이고, 전부 어두운 벽/천장만 보이게.
    /// 물리 충돌은 전부 끄고, 시각용으로만 사용.
    /// </summary>
    private void CreateOuterShell()
    {
        float totalWidth = laneCount * laneWidth;

        // 트랙이 좌우로 휘어지는 최대 폭을 대충 넉넉하게 잡음
        float maxCurveAbs = laneWidth * (laneCount * 0.5f + 2.5f);
        float halfWidth = totalWidth * 0.5f + maxCurveAbs + 4f;   // 좌우 여유 4
        float halfLength = trackLength * 0.5f + 20f;              // 앞뒤 여유 20
        float centerZ = startZ + trackLength * 0.5f;

        // 카메라 높이보다 충분히 높은 벽/천장
        float camHeight = trackLength * 0.35f;
        float shellHeight = Mathf.Max(40f, camHeight + 10f);      // 최소 40, 카메라보다 10 높게

        float wallThickness = 1f;

        Color shellColor = new Color(0.03f, 0.03f, 0.03f);        // 거의 검정에 가까운 회색

        GameObject MakePanel(string name, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;

            var rend = go.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Standard"));
            mat.color = shellColor;
            rend.material = mat;

            // 물리 충돌은 필요 없으니 제거
            Destroy(go.GetComponent<Collider>());

            return go;
        }

        // ▸ 앞쪽(스타트 쪽) 큰 벽
        MakePanel(
            "WorldWall_Front",
            new Vector3(0f, shellHeight * 0.5f, startZ - 10f),
            new Vector3(halfWidth * 2f, shellHeight, wallThickness)
        );

        // ▸ 뒤쪽(피니시 뒤) 큰 벽
        MakePanel(
            "WorldWall_Back",
            new Vector3(0f, shellHeight * 0.5f, startZ + trackLength + 10f),
            new Vector3(halfWidth * 2f, shellHeight, wallThickness)
        );

        // ▸ 왼쪽 바깥 큰 벽
        MakePanel(
            "WorldWall_LeftOuter",
            new Vector3(-halfWidth - 1f, shellHeight * 0.5f, centerZ),
            new Vector3(wallThickness, shellHeight, halfLength * 2f)
        );

        // ▸ 오른쪽 바깥 큰 벽
        MakePanel(
            "WorldWall_RightOuter",
            new Vector3(halfWidth + 1f, shellHeight * 0.5f, centerZ),
            new Vector3(wallThickness, shellHeight, halfLength * 2f)
        );

        // ▸ 천장 – 위에서 하늘이 안 보이게 막기
        MakePanel(
            "WorldCeiling",
            new Vector3(0f, shellHeight, centerZ),
            new Vector3(halfWidth * 2f, wallThickness, halfLength * 2f)
        );
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