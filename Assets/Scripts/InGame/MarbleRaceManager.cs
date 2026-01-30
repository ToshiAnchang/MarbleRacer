using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// 트랙/맵은 전부 TrackTile 프리팹(Track_S / Track_L / Track_R …)으로만 생성.
/// 시작 UI에서 Seed, Tile Count(타일 개수)를 입력 받아 레이스 시작.
/// 기존 Ground / Walls / Obstacles 생성 코드는 전부 제거.
/// </summary>
public class MarbleRaceManager : MonoBehaviour
{
    public static MarbleRaceManager Instance { get; private set; }

    // -------------------- UI --------------------
    private Canvas startCanvas;
    private InputField seedInput;
    private InputField tileCountInput;
    private Button startButton;

    // 기존 방식 폰트 캐시
    private static Font _uiFont;

    [Header("기본 값 (UI 입력 실패 시 사용)")]
    public int defaultSeed = 0;
    public int defaultTileCount = 20;
    public int defaultLaneCount = 4;

    // -------------------- 레이스 설정 --------------------
    [Header("레이스 설정")]
    public float laneWidth = 2.0f;
    public float marbleStartHeight = 0.6f;
    public float marbleStartImpulse = 5f; // ★ 시작 부스터는 0으로 두고, Marble 쪽 로직으로만 가속
    public float maxMarbleHeight = 3f;
    [Tooltip("스타트 모서리에서 트랙 진행 방향으로 얼마나 앞에서 시작할지(단위: 미터)")]
    public float marbleStartForwardOffset = 1.5f;


    private PhysicMaterial trackPhysicMaterial;   // 트랙(바닥+벽)용 물리 재질
    private PhysicMaterial marblePhysicMaterial;  // 구슬용 물리 재질


    [Header("타일 프리팹 이름 (Resources/Prefabs 안)")]
    public string[] trackTilePrefabNames = { "Track_S", "Track_L", "Track_R" };

    [Header("타일 샘플 해상도")]
    [Tooltip("타일 하나당 몇 개의 경로 샘플을 찍을지 (높을수록 곡선이 부드러움)")]
    public int samplesPerTile = 8;

    // -------------------- 내부 상태 --------------------
    private readonly List<GameObject> spawnedTiles = new List<GameObject>();
    private readonly List<Vector3> pathPoints = new List<Vector3>();
    private readonly List<Marble> marbles = new List<Marble>();
    private readonly HashSet<Marble> finishedMarbles = new HashSet<Marble>();

    // -------------------- 장애물 설정 --------------------
    [Header("장애물 - 회전 브러시")]
    public bool enableRotatingBrushes = true;
    [Range(0f, 1f)]
    public float rotatingBrushLateralOffsetRatio = 0.5f; //트랙의 가로폭 대비 얼마나 퍼트려 생성시킬지
    public float rotatingBrushDensity = 0.35f;
    public float rotatingBrushTrackCoverage = 0.9f;
    public float rotatingBrushThickness = 0.4f;
    public float rotatingBrushHeightOffset = 0.2f;
    public float rotatingBrushSwingAngle = 90f;
    public float rotatingBrushOscillateSpeedMin = 30.5f;
    public float rotatingBrushOscillateSpeedMax = 100.0f;


    private Vector3 startCenter;
    private Vector3 startForward;
    private Vector3 startRight;
    private Vector3 finishPosition;

    private int laneCount;
    private bool winnerAnnounced = false;

    // =====================================================
    // Unity 생명주기
    // =====================================================

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
        CreatePhysicMaterials();  // ★ 낮은 마찰 물리 재질 생성
        CreateStartUI();          // 기존 UI 생성
    }

    // 낮은 마찰 + 거의 안 튀게 만드는 물리 재질 설정
    private void CreatePhysicMaterials()
    {
        // ───── 트랙용 (바닥 + 벽) ─────
        trackPhysicMaterial = new PhysicMaterial("TrackLowFriction");
        trackPhysicMaterial.dynamicFriction = 0.01f;   // 움직이는 마찰
        trackPhysicMaterial.staticFriction = 0.001f;   // 정지 마찰
        trackPhysicMaterial.bounciness = 0.0f;    // 바닥/벽은 전혀 안 튀게
        trackPhysicMaterial.frictionCombine = PhysicMaterialCombine.Minimum;
        trackPhysicMaterial.bounceCombine = PhysicMaterialCombine.Minimum;

        // ───── 구슬용 (아주 약간만 튀게) ─────
        marblePhysicMaterial = new PhysicMaterial("MarbleLowBounce");
        marblePhysicMaterial.dynamicFriction = 0.02f;
        marblePhysicMaterial.staticFriction = 0.02f;
        marblePhysicMaterial.bounciness = 0.2f;   // 0.2 → 0.05 로 크게 감소
        marblePhysicMaterial.frictionCombine = PhysicMaterialCombine.Minimum;
        marblePhysicMaterial.bounceCombine = PhysicMaterialCombine.Minimum;
    }



    // =====================================================
    // 폰트: 기존 코드 방식
    // =====================================================
    private Font GetUIFont()
    {
        if (_uiFont != null)
            return _uiFont;

        // 1순위: OS에서 Arial 폰트를 직접 동적 생성
        _uiFont = Font.CreateDynamicFontFromOSFont("Arial", 18);

        // 2순위: Resources/Fonts/ARIAL
        if (_uiFont == null)
        {
            _uiFont = Resources.Load<Font>("Fonts/ARIAL");
        }

        if (_uiFont == null)
        {
            Debug.LogError("UI 폰트를 찾을 수 없습니다. OS Arial 도 없고 Resources/Fonts/ARIAL 도 없습니다.");
        }

        return _uiFont;
    }

    // =====================================================
    // 시작 UI 생성
    // =====================================================
    // MarbleRaceManager.cs 안의 CreateStartUI 전체를 이걸로 교체
    private void CreateStartUI()
    {
        if (startCanvas != null) return;

        // EventSystem 없으면 생성
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        // ===== Canvas =====
        GameObject canvasGO = new GameObject("StartCanvas");
        startCanvas = canvasGO.AddComponent<Canvas>();
        startCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        canvasGO.AddComponent<GraphicRaycaster>();

        Font font = GetUIFont();

        // ===== 반투명 배경 =====
        GameObject bgGO = new GameObject("DimBackground");
        bgGO.transform.SetParent(canvasGO.transform, false);
        RectTransform bgRect = bgGO.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        Image bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.6f);

        // ===== 중앙 패널 =====
        GameObject panelGO = new GameObject("StartPanel");
        panelGO.transform.SetParent(bgGO.transform, false);
        RectTransform panelRect = panelGO.AddComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(650, 340);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;

        Image panelImage = panelGO.AddComponent<Image>();
        panelImage.color = new Color(0.1f, 0.1f, 0.12f, 0.96f);

        Outline outline = panelGO.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.15f);
        outline.effectDistance = new Vector2(2, -2);

        VerticalLayoutGroup vLayout = panelGO.AddComponent<VerticalLayoutGroup>();
        vLayout.childControlHeight = true;
        vLayout.childControlWidth = true;
        vLayout.childForceExpandHeight = false;
        vLayout.childForceExpandWidth = true;
        vLayout.spacing = 12;
        vLayout.padding = new RectOffset(20, 20, 20, 20);

        ContentSizeFitter fitter = panelGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ===== 제목 =====
        GameObject titleGO = new GameObject("Title");
        titleGO.transform.SetParent(panelGO.transform, false);
        LayoutElement titleLE = titleGO.AddComponent<LayoutElement>();
        titleLE.preferredHeight = 60;

        Text titleText = titleGO.AddComponent<Text>();
        titleText.font = font;
        titleText.text = "MARBLE RACE";
        titleText.fontSize = 32;
        titleText.fontStyle = FontStyle.Bold;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = Color.white;

        // ===== 설명 =====
        GameObject descGO = new GameObject("Description");
        descGO.transform.SetParent(panelGO.transform, false);
        LayoutElement descLE = descGO.AddComponent<LayoutElement>();
        descLE.preferredHeight = 40;

        Text descText = descGO.AddComponent<Text>();
        descText.font = font;
        descText.fontSize = 18;
        descText.text = "Seed와 Tile Count를 입력하고 START를 눌러 레이스를 시작하세요.";
        descText.alignment = TextAnchor.MiddleCenter;
        descText.color = new Color(0.9f, 0.9f, 0.9f, 1f);

        // ===== 라벨+인풋 생성용 로컬 함수 =====
        void CreateLabeledInput(string labelText, out InputField inputField, string placeholder, string defaultValue)
        {
            GameObject row = new GameObject(labelText);
            row.transform.SetParent(panelGO.transform, false);

            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.childControlHeight = true;
            h.childControlWidth = true;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = true;
            h.spacing = 10;

            LayoutElement rowLE = row.AddComponent<LayoutElement>();
            rowLE.preferredHeight = 40;

            // Label
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(row.transform, false);
            Text label = labelGO.AddComponent<Text>();
            label.text = labelText;
            label.font = font;
            label.fontSize = 20;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.sizeDelta = new Vector2(160, 40);

            // Input
            GameObject inputGO = new GameObject("InputField");
            inputGO.transform.SetParent(row.transform, false);
            Image inputImage = inputGO.AddComponent<Image>();
            inputImage.color = Color.white;
            RectTransform inputRect = inputGO.GetComponent<RectTransform>();
            inputRect.sizeDelta = new Vector2(260, 40);

            inputField = inputGO.AddComponent<InputField>();

            // Text
            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(inputGO.transform, false);
            Text text = textGO.AddComponent<Text>();
            text.font = font;
            text.fontSize = 18;
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 0);
            textRect.offsetMax = new Vector2(-10, 0);

            // Placeholder
            GameObject phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(inputGO.transform, false);
            Text phText = phGO.AddComponent<Text>();
            phText.font = font;
            phText.fontSize = 18;
            phText.color = new Color(0.5f, 0.5f, 0.5f, 0.9f);
            phText.text = placeholder;
            phText.alignment = TextAnchor.MiddleLeft;
            RectTransform phRect = phText.GetComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = new Vector2(10, 0);
            phRect.offsetMax = new Vector2(-10, 0);

            inputField.textComponent = text;
            inputField.placeholder = phText;
            inputField.text = defaultValue;
        }

        // Seed
        CreateLabeledInput("Seed", out seedInput, "정수 시드 값", defaultSeed.ToString());

        // Tile Count
        CreateLabeledInput("Tile Count", out tileCountInput, "이어붙일 타일 개수", defaultTileCount.ToString());

        // ===== 여백 =====
        GameObject spacer = new GameObject("Spacer");
        spacer.transform.SetParent(panelGO.transform, false);
        LayoutElement spacerLE = spacer.AddComponent<LayoutElement>();
        spacerLE.preferredHeight = 10;

        // ===== START 버튼 =====
        GameObject btnGO = new GameObject("StartButton");
        btnGO.transform.SetParent(panelGO.transform, false);

        LayoutElement btnLE = btnGO.AddComponent<LayoutElement>();
        btnLE.preferredHeight = 60;

        Image btnImage = btnGO.AddComponent<Image>();
        btnImage.color = new Color(0.15f, 0.7f, 0.35f, 1f);

        Outline btnOutline = btnGO.AddComponent<Outline>();
        btnOutline.effectColor = new Color(0f, 0f, 0f, 0.6f);
        btnOutline.effectDistance = new Vector2(2, -2);

        startButton = btnGO.AddComponent<Button>();
        startButton.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = startButton.colors;
        colors.normalColor = new Color(0.15f, 0.7f, 0.35f, 1f);
        colors.highlightedColor = new Color(0.2f, 0.8f, 0.45f, 1f);
        colors.pressedColor = new Color(0.1f, 0.5f, 0.25f, 1f);
        colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.8f);
        startButton.colors = colors;

        startButton.onClick.AddListener(OnClickStart);

        // ★ 버튼 안의 텍스트 (자식 GameObject로 별도 생성)
        GameObject btnTextGO = new GameObject("Text");
        btnTextGO.transform.SetParent(btnGO.transform, false);
        Text btnText = btnTextGO.AddComponent<Text>();
        btnText.font = font;
        btnText.text = "START RACE";
        btnText.fontSize = 24;
        btnText.fontStyle = FontStyle.Bold;
        btnText.alignment = TextAnchor.MiddleCenter;
        btnText.color = Color.white;

        RectTransform btnTextRect = btnText.GetComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.offsetMin = Vector2.zero;
        btnTextRect.offsetMax = Vector2.zero;

        // 포커스
        seedInput.Select();
        seedInput.ActivateInputField();
    }


    private void OnClickStart()
    {
        int seed = defaultSeed;
        int tileCount = defaultTileCount;

        if (!string.IsNullOrEmpty(seedInput.text))
            int.TryParse(seedInput.text, out seed);
        if (!string.IsNullOrEmpty(tileCountInput.text))
            int.TryParse(tileCountInput.text, out tileCount);

        laneCount = Mathf.Max(1, defaultLaneCount);

        if (startCanvas != null)
            startCanvas.gameObject.SetActive(false);

        StartRace(seed, tileCount);
    }

    // =====================================================
    // 레이스 시작 / 리셋
    // =====================================================

    private void StartRace(int seed, int tileCount)
    {
        winnerAnnounced = false;
        finishedMarbles.Clear();

        ClearTiles();
        ClearMarbles();

        BuildTrackFromTiles(seed, tileCount);
        SpawnMarbles();
        SetupCamera();
        SetupFinishTrigger();
    }

    private void ClearTiles()
    {
        foreach (var tile in spawnedTiles)
        {
            if (tile != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    Object.DestroyImmediate(tile);
                else
#endif
                    Object.Destroy(tile);
            }
        }
        spawnedTiles.Clear();
        pathPoints.Clear();
    }

    private void ClearMarbles()
    {
        foreach (var m in marbles)
        {
            if (m != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    Object.DestroyImmediate(m.gameObject);
                else
#endif
                    Object.Destroy(m.gameObject);
            }
        }
        marbles.Clear();
    }

    // =====================================================
    // 타일 기반 트랙 생성
    // =====================================================
    // =====================================================
    // 타일 기반 트랙 생성 (+ 회전 브러시 장애물 WFC 스타일 배치)
    // =====================================================
    private void BuildTrackFromTiles(int seed, int tileCount)
    {
        Random.InitState(seed);

        pathPoints.Clear();
        spawnedTiles.Clear();

        // ───────────────── 프리팹 로드 ─────────────────
        GameObject[] prefabGOs = Resources.LoadAll<GameObject>("Prefabs");
        List<GameObject> tilePrefabs = new List<GameObject>();

        foreach (var go in prefabGOs)
        {
            if (go.GetComponent<TrackTileGenerator>() != null)
                tilePrefabs.Add(go);
        }

        if (tilePrefabs.Count == 0)
        {
            Debug.LogError("[MarbleRace] TrackTileGenerator 프리팹을 찾지 못했습니다.");
            return;
        }

        Vector3 currentPos = Vector3.zero;
        Vector3 currentForward = Vector3.forward;
        string currentExitProfileId = null;

        // 회전 브러시 인접 제약 플래그
        bool lastTileHasBrush = false;

        for (int i = 0; i < tileCount; i++)
        {
            // ───── ProfileId 기준 후보 선택 (WFC 스타일 인접 규칙) ─────
            List<GameObject> candidates = new List<GameObject>();

            foreach (var prefab in tilePrefabs)
            {
                var gen = prefab.GetComponent<TrackTileGenerator>();
                if (i == 0 || gen.entryProfile.profileId == currentExitProfileId)
                    candidates.Add(prefab);
            }

            if (candidates.Count == 0)
                candidates.AddRange(tilePrefabs);

            GameObject prefabGO = candidates[Random.Range(0, candidates.Count)];
            GameObject tileGO = Instantiate(prefabGO, transform);
            tileGO.name = $"{prefabGO.name}_{i:D3}";
            spawnedTiles.Add(tileGO);

            TrackTileGenerator genInst = tileGO.GetComponent<TrackTileGenerator>();

            // ★ 트랙 타일에 낮은 마찰 물리 재질 적용
            if (trackPhysicMaterial != null)
            {
                MeshCollider mc = tileGO.GetComponent<MeshCollider>();
                if (mc != null)
                    mc.sharedMaterial = trackPhysicMaterial;
            }

            // ───── 회전: Y축만 사용 ─────
            Vector3 flatForward = currentForward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Vector3.forward;
            flatForward.Normalize();

            tileGO.transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up);

            // ───── 위치: Entry 맞추기 ─────
            genInst.GetPathFrameLocal(0f,
                out Vector3 entryCenterLocal,
                out _,
                out _);

            Vector3 entryCenterWorld =
                tileGO.transform.TransformPoint(entryCenterLocal);

            tileGO.transform.position = currentPos - entryCenterWorld;

            // ───── 경로 샘플링 ─────
            int samples = Mathf.Max(2, samplesPerTile);
            for (int s = 0; s < samples; s++)
            {
                float t = (float)s / (samples - 1);

                genInst.GetPathFrameLocal(t,
                    out Vector3 centerLocal,
                    out Vector3 forwardLocal,
                    out _);

                Vector3 worldCenter =
                    tileGO.transform.TransformPoint(centerLocal);

                pathPoints.Add(worldCenter);

                if (i == 0 && s == 0)
                {
                    startCenter = worldCenter;

                    Vector3 wf = tileGO.transform.TransformDirection(forwardLocal);
                    wf.y = 0f;
                    startForward = wf.normalized;
                    startRight = Vector3.Cross(Vector3.up, startForward).normalized;
                }
            }

            // ───── 이 타일 위에 회전 브러시를 둘지 결정 & 생성 (헬퍼 클래스 호출) ─────
            RotatingBrushObstacleGenerator.TryPlaceRotatingBrushOnTile(
                this,          // 설정/상태를 가진 매니저
                tileGO,
                genInst,
                i,
                tileCount,
                ref lastTileHasBrush
            );

            // ───── Exit → 다음 타일 기준 갱신 ─────
            genInst.GetPathFrameLocal(1f,
                out Vector3 exitCenterLocal,
                out Vector3 exitForwardLocal,
                out _);

            Vector3 exitCenterWorld =
                tileGO.transform.TransformPoint(exitCenterLocal);

            Vector3 exitForwardWorld =
                tileGO.transform.TransformDirection(exitForwardLocal);
            exitForwardWorld.y = 0f;

            currentPos = exitCenterWorld;
            currentForward = exitForwardWorld.normalized;
            finishPosition = exitCenterWorld;

            currentExitProfileId = genInst.exitProfile.profileId;
        }
    }



    // =====================================================
    // 트랙 진행 방향 / 중심선 보조 함수
    // =====================================================

    /// <summary>
    /// 트랙 중심 경로를 따라가는 3D 탄젠트(경사 포함) 방향.
    /// </summary>
    public Vector3 GetTrackTangent(Vector3 worldPos)
    {
        if (pathPoints == null || pathPoints.Count < 2)
            return Vector3.forward;

        int closestIndex = 0;
        float bestSqr = float.MaxValue;

        Vector2 pos2 = new Vector2(worldPos.x, worldPos.z);

        for (int i = 0; i < pathPoints.Count; i++)
        {
            Vector3 p = pathPoints[i];
            Vector2 p2 = new Vector2(p.x, p.z);
            float sqr = (p2 - pos2).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                closestIndex = i;
            }
        }

        int nextIndex = Mathf.Min(closestIndex + 1, pathPoints.Count - 1);
        if (nextIndex == closestIndex && closestIndex > 0)
            closestIndex--;

        Vector3 dir = pathPoints[nextIndex] - pathPoints[closestIndex];
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector3.forward;

        return dir.normalized; // y 포함
    }

    public int GetClosestPathIndex(Vector3 worldPos)
    {
        if (pathPoints == null || pathPoints.Count == 0)
            return 0;

        int closestIndex = 0;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < pathPoints.Count; i++)
        {
            float sqr = (pathPoints[i] - worldPos).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    public Vector3 GetForwardByPathIndex(int index, int lookAhead)
    {
        if (pathPoints == null || pathPoints.Count < 2)
            return Vector3.forward;

        int from = Mathf.Clamp(index, 0, pathPoints.Count - 1);
        int to = Mathf.Clamp(index + lookAhead, 0, pathPoints.Count - 1);

        Vector3 dir = pathPoints[to] - pathPoints[from];
        if (dir.sqrMagnitude < 0.0001f)
            return Vector3.forward;

        // 수평 기준 방향만 쓰고 싶으면 아래 두 줄 추가
        // dir.y = 0f;
        // if (dir.sqrMagnitude < 0.0001f) return Vector3.forward;

        return dir.normalized;
    }



    /// <summary>
    /// 카메라/연출용 수평 진행 방향.
    /// </summary>
    public Vector3 GetTrackForwardDirection(Vector3 worldPos)
    {
        Vector3 dir = GetTrackTangent(worldPos);
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector3.forward;

        return dir.normalized;
    }

    /// <summary>
    /// 트랙 중심 경로 상에서 worldPos와 가장 가까운 점.
    /// 구슬이 너무 밖으로 나갈 때 안쪽으로 당길 때 사용.
    /// </summary>
    public Vector3 GetNearestPathPoint(Vector3 worldPos)
    {
        if (pathPoints == null || pathPoints.Count == 0)
            return worldPos;

        int closestIndex = 0;
        float bestSqr = float.MaxValue;

        Vector2 pos2 = new Vector2(worldPos.x, worldPos.z);

        for (int i = 0; i < pathPoints.Count; i++)
        {
            Vector3 p = pathPoints[i];
            Vector2 p2 = new Vector2(p.x, p.z);
            float sqr = (p2 - pos2).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                closestIndex = i;
            }
        }

        return pathPoints[closestIndex];
    }

    // =====================================================
    // 구슬 생성
    // =====================================================

    private void SpawnMarbles()
    {
        Debug.Log("[MarbleRace] SpawnMarbles start");

        if (pathPoints.Count < 2)
        {
            Debug.LogWarning("[MarbleRace] 경로가 없어서 구슬을 배치할 수 없습니다. pathPoints.Count = " + pathPoints.Count);
            return;
        }

        float totalWidth = laneWidth * laneCount;
        float leftMost = -totalWidth * 0.5f + laneWidth * 0.5f;

        Vector3 baseStartPos = startCenter
                               + startForward.normalized * marbleStartForwardOffset;

        for (int i = 0; i < laneCount; i++)
        {
            float offset = leftMost + i * laneWidth;

            Vector3 laneStartPos =
                baseStartPos
                + startRight * (offset / 2f)
                + Vector3.up * marbleStartHeight;

            Debug.Log($"[MarbleRace] Marble {i} spawn pos = {laneStartPos}");

            // 1) 구슬 GameObject 생성
            GameObject marbleGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (marbleGO == null)
            {
                Debug.LogError($"[MarbleRace] Marble {i}: CreatePrimitive(Sphere) 실패");
                continue;
            }

            marbleGO.name = $"Marble_{i + 1}";
            marbleGO.transform.position = laneStartPos;
            marbleGO.transform.localScale = Vector3.one * 1.0f;

            // 2) 콜라이더 확인
            SphereCollider sc = marbleGO.GetComponent<SphereCollider>();
            if (sc == null)
            {
                Debug.LogError($"[MarbleRace] Marble {i}: SphereCollider 가 없습니다! Collider 타입 = {marbleGO.GetComponent<Collider>()?.GetType().Name ?? "없음"}");

                // 혹시라도 진짜로 SphereCollider 를 못 붙이는 상황이면
                // 최소한 다른 Collider 하나는 붙여서 굴러가게는 해보자
                Collider anyCol = marbleGO.GetComponent<Collider>();
                if (anyCol == null)
                {
                    Debug.LogWarning($"[MarbleRace] Marble {i}: BoxCollider 로 대체 시도");
                    anyCol = marbleGO.AddComponent<BoxCollider>();
                }
            }
            else if (marblePhysicMaterial != null)
            {
                sc.sharedMaterial = marblePhysicMaterial;
            }

            // 3) Rigidbody
            Rigidbody rb = marbleGO.AddComponent<Rigidbody>();
            if (rb == null)
            {
                Debug.LogError($"[MarbleRace] Marble {i}: Rigidbody 추가 실패");
                continue;
            }

            rb.mass = 1f;
            rb.drag = 0f;
            rb.angularDrag = 0.01f;

            // 4) Marble 스크립트
            Marble marble = marbleGO.AddComponent<Marble>();
            if (marble == null)
            {
                Debug.LogError($"[MarbleRace] Marble {i}: Marble 컴포넌트 추가 실패");
                continue;
            }

            marble.laneIndex = i;
            marble.maxHeight = maxMarbleHeight;

            // 5) 색상
            Renderer r = marbleGO.GetComponent<Renderer>();
            if (r != null)
            {
                var mat = r.material;
                mat.color = GetMarbleColor(i);
                r.material = mat;
            }
            else
            {
                Debug.LogWarning($"[MarbleRace] Marble {i}: Renderer 없음");
            }

            // 6) 시작 힘
            if (marbleStartImpulse != 0f)
            {
                rb.AddForce(startForward * marbleStartImpulse, ForceMode.Impulse);
            }

            marbles.Add(marble);

            Debug.Log($"[MarbleRace] Marble {i} 생성 완료. collider={marbleGO.GetComponent<Collider>()?.GetType().Name ?? "없음"}");
        }

        Debug.Log($"[MarbleRace] SpawnMarbles finished. marbles.Count={marbles.Count}");
    }


    private Color GetMarbleColor(int index)
    {
        Color[] colors =
        {
        Color.red,
        Color.blue,
        Color.green,
        Color.yellow,
        Color.magenta,
        Color.cyan,
        new Color(1f, 0.5f, 0f),   // orange
        new Color(0.5f, 0f, 1f)    // purple
    };

        return colors[index % colors.Length];
    }



    // 현재 worldPos 에서 가장 가까운 트랙 센터라인의 높이(y)를 반환
    public float GetTrackCenterHeight(Vector3 worldPos)
    {
        if (pathPoints == null || pathPoints.Count == 0)
            return worldPos.y;

        int closestIndex = 0;
        float bestSqr = float.MaxValue;

        // XZ 평면 거리를 기준으로 가장 가까운 pathPoint 찾기
        for (int i = 0; i < pathPoints.Count; i++)
        {
            Vector3 p = pathPoints[i];
            float dx = p.x - worldPos.x;
            float dz = p.z - worldPos.z;
            float sqr = dx * dx + dz * dz;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                closestIndex = i;
            }
        }

        return pathPoints[closestIndex].y;
    }



    private Color GetLaneColor(int lane)
    {
        switch (lane)
        {
            case 0: return Color.red;
            case 1: return Color.blue;
            case 2: return Color.green;
            case 3: return Color.yellow;
            case 4: return Color.magenta;
            default: return Color.cyan;
        }
    }

    // =====================================================
    // 카메라 & 피니시
    // =====================================================

    private void SetupCamera()
    {
        if (marbles.Count == 0) return;

        Marble focusMarble = marbles[0];

        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camGO = new GameObject("Main Camera");
            cam = camGO.AddComponent<Camera>();
            cam.tag = "MainCamera";
        }

        CameraFollow follow = cam.GetComponent<CameraFollow>();
        if (follow == null)
            follow = cam.gameObject.AddComponent<CameraFollow>();

        follow.target = focusMarble.transform;
    }

    private void SetupFinishTrigger()
    {
        // 트랙이 하나도 없으면 생성하지 않음
        if (spawnedTiles == null || spawnedTiles.Count == 0)
            return;

        // 마지막 트랙 타일
        GameObject lastTile = spawnedTiles[spawnedTiles.Count - 1];
        TrackTileGenerator gen = lastTile.GetComponent<TrackTileGenerator>();

        // 예외 상황: TrackTileGenerator 없으면 대충 만들어서라도 동작만 하게
        if (gen == null)
        {
            Vector3 pos = finishPosition + Vector3.up * 0.5f;

            GameObject fallback = new GameObject("FinishTrigger");
            fallback.transform.position = pos;

            BoxCollider fallbackCol = fallback.AddComponent<BoxCollider>();
            fallbackCol.isTrigger = true;
            fallbackCol.size = new Vector3(10f, 3f, 4f);

            FinishTrigger fallbackTrigger = fallback.AddComponent<FinishTrigger>();
            fallbackTrigger.manager = this;
            return;
        }

        // 1) 마지막 타일의 "끝점(center) + 진행 방향"을 얻는다
        gen.GetPathFrameLocal(
            1f, // t = 1 : Exit 쪽
            out Vector3 exitCenterLocal,
            out Vector3 exitForwardLocal,
            out _
        );

        // 로컬 → 월드 변환
        Vector3 exitCenterWorld = lastTile.transform.TransformPoint(exitCenterLocal);
        Vector3 forward = lastTile.transform.TransformDirection(exitForwardLocal);
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        forward.Normalize();

        // 2) 트랙 폭 / 높이 / 길이 계산
        // ── 폭: 마지막 지점의 trackWidth 기준 ──
        float trackWidthAtExit = 4f; // 기본값

        if (gen.exitProfile != null)
            trackWidthAtExit = gen.exitProfile.trackWidth;
        else if (gen.middleProfile != null)
            trackWidthAtExit = gen.middleProfile.trackWidth;
        else if (gen.entryProfile != null)
            trackWidthAtExit = gen.entryProfile.trackWidth;

        // 좌우로 약간 여유만 더 줌
        float triggerWidth = trackWidthAtExit + 0.5f;

        // ── 높이: 프로파일들의 wallHeight 중 최댓값 기준 ──
        float maxWallHeight = 0f;
        if (gen.entryProfile != null) maxWallHeight = Mathf.Max(maxWallHeight, gen.entryProfile.wallHeight);
        if (gen.middleProfile != null) maxWallHeight = Mathf.Max(maxWallHeight, gen.middleProfile.wallHeight);
        if (gen.exitProfile != null) maxWallHeight = Mathf.Max(maxWallHeight, gen.exitProfile.wallHeight);

        float triggerHeight = maxWallHeight + 1f;            // 벽보다 약간 높게
        float triggerLength = Mathf.Max(4f, gen.tileLength * 0.25f);  // 타일 길이의 1/4 정도

        // 3) 트리거 중심 위치
        //    - 타일 끝점에서 forward 방향으로 절반만큼 밀고
        //    - 높이의 절반만큼 위로 올려서 박스 중심 맞춤
        Vector3 triggerCenter =
            exitCenterWorld
            + forward * (triggerLength * 0.5f)
            + Vector3.up * (triggerHeight * 0.5f);

        // 4) 오브젝트 생성 및 회전/위치 설정
        GameObject go = new GameObject("FinishTrigger");
        go.transform.position = triggerCenter;
        go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        // 5) BoxCollider를 트랙 폭/높이/길이에 맞게 세팅
        BoxCollider col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(triggerWidth, triggerHeight, triggerLength);
        col.center = Vector3.zero;

        // 6) FinishTrigger 스크립트 연결
        FinishTrigger trigger = go.AddComponent<FinishTrigger>();
        trigger.manager = this;
    }

    // 트랙 경로 관련 보조 프로퍼티/메소드 추가

    /// <summary>
    /// 현재 생성된 경로의 포인트 개수
    /// </summary>
    public int PathPointCount
    {
        get { return pathPoints != null ? pathPoints.Count : 0; }
    }

    /// <summary>
    /// 현재 사용 중인 레인 개수 (내부 laneCount 읽기 전용)
    /// </summary>
    public int LaneCount
    {
        get { return laneCount; }
    }

    /// <summary>
    /// 경로 인덱스로 직접 포인트를 가져오기
    /// </summary>
    public Vector3 GetPathPointByIndex(int index)
    {
        if (pathPoints == null || pathPoints.Count == 0)
            return Vector3.zero;

        index = Mathf.Clamp(index, 0, pathPoints.Count - 1);
        return pathPoints[index];
    }



    // =====================================================
    // 골인 / 리셋
    // =====================================================

    public void OnMarbleFinished(Marble marble)
    {
        if (finishedMarbles.Contains(marble))
            return;

        finishedMarbles.Add(marble);

        if (!winnerAnnounced)
        {
            winnerAnnounced = true;
            Debug.Log($"🏁 Winner: Lane {marble.laneIndex + 1} ({marble.gameObject.name})");
        }

        if (finishedMarbles.Count >= marbles.Count)
        {
            Debug.Log("모든 구슬이 결승선에 도착했습니다. 3초 후 다시 시작합니다.");
            Invoke(nameof(ReloadScene), 3f);
        }
    }

    private void ReloadScene()
    {
        Scene current = SceneManager.GetActiveScene();
        SceneManager.LoadScene(current.name);
    }
}
