using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 최소 골격만 남긴 MarbleRaceManager:
/// 1) 게임 시작 시 Seed / TileCount 입력 UI 생성
/// 2) 입력값에 따라 TrackTile 프리팹으로 맵 생성
/// 3) 트랙 경로(pathPoints) 정보 저장 (맵 모양/크기 정보)
/// 4) 마지막 타일 기준 Finish 콜라이더 생성
/// 
/// 구슬 생성/카메라/장애물/결승 처리 등은 모두 제거(또는 빈 메서드로 처리).
/// </summary>
public class MarbleRaceManager : MonoBehaviour
{
    public static MarbleRaceManager Instance { get; private set; }

    // -------------------- UI --------------------
    private Canvas startCanvas;
    private InputField seedInput;
    private InputField tileCountInput;
    private Button startButton;

    private static Font _uiFont;


    [Header("기본 값 (UI 입력 실패 시 사용)")]
    public int defaultSeed = 0;
    public int defaultTileCount = 20;
    public int defaultLaneCount = 4;   // LaneCount 프로퍼티용 (Marble/카메라가 참조할 수 있음)


    // -------------------- 스타트 깔대기 --------------------
    [Header("스타트 깔대기 설정")]
    [Tooltip("Start Funnel 프리팹(Resources 기준 경로). 예: \"ETC/StartFunnel\"")]
    public string startFunnelPrefabPath = "ETC/StartFunnel";

    [Tooltip("깔대기 주둥이 끝에서 첫 타일 entry까지의 오프셋 (x=좌우, y=위/아래, z=앞/뒤)")]
    
    public Vector3 firstTileOffsetFromSpoutExit = new Vector3(0f, -1.5f, 0f);
    // 생성된 깔대기 인스턴스 캐시 (씬 리셋 시 삭제용)
    
    private GameObject startFunnelInstance;


    // -------------------- 트랙타일/경로 설정 --------------------
    [Header("타일 샘플 해상도")]
    [Tooltip("타일 하나당 경로를 몇 개의 샘플 포인트로 저장할지 (경로 곡선 정밀도)")]
    public int samplesPerTile = 8;

    // -------------------- 플레이어 구슬 설정 --------------------
    [Header("플레이어 구슬 설정")]
    [Tooltip("플레이어 수 (StartPos1~StartPosN 을 사용)")]
    public int playerCount = 6;

    // 생성된 플레이어 구슬들을 보관 (카메라 등에서 사용할 수 있음)
    private List<PlayerMarble> playerMarbles = new List<PlayerMarble>();

    /// <summary>
    /// 현재 레이스에 참여 중인 플레이어 구슬 목록 (읽기 전용).
    /// 미니맵, 연출 등에서 사용합니다.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<PlayerMarble> PlayerMarbles
    {
        get { return playerMarbles; }
    }



    // -------------------- 내부 상태 --------------------
    private readonly List<GameObject> spawnedTiles = new List<GameObject>();
    private readonly List<Vector3> pathPoints = new List<Vector3>();


    private Vector3 startCenter;
    private Vector3 startForward;
    private Vector3 startRight;

    private Vector3 finishPosition;

    [Header("Finish 비주얼 설정")]
    [Tooltip("Finish 텍스트의 위치 조절 조절합니다.")]
    public float finishVisualHeightOffset = 0.5f;

    [Tooltip("결승 깃발에 사용할 Sprite 리소스 경로(Resources 기준). 비어 있으면 단색 깃발로 표시됩니다.")]
    public string finishFlagSpritePath = "UI/FinishFlag";

    [Tooltip("결승점 텍스트")]
    public string finishText = "FINISH";

    private int laneCount;


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
        CreateStartUI();
    }

    // =====================================================
    // UI용 폰트
    // =====================================================

    private Font GetUIFont()
    {
        if (_uiFont != null)
            return _uiFont;

        // 1순위: OS Arial
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
    // 시작 UI 생성 (Seed / TileCount 입력)
    // =====================================================

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

        // Canvas
        GameObject canvasGO = new GameObject("StartCanvas");
        startCanvas = canvasGO.AddComponent<Canvas>();
        startCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        canvasGO.AddComponent<GraphicRaycaster>();

        Font font = GetUIFont();

        // 반투명 배경
        GameObject bgGO = new GameObject("DimBackground");
        bgGO.transform.SetParent(canvasGO.transform, false);
        RectTransform bgRect = bgGO.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        Image bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.6f);

        // 중앙 패널
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

        // 제목
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

        // 설명
        GameObject descGO = new GameObject("Description");
        descGO.transform.SetParent(panelGO.transform, false);
        LayoutElement descLE = descGO.AddComponent<LayoutElement>();
        descLE.preferredHeight = 40;

        Text descText = descGO.AddComponent<Text>();
        descText.font = font;
        descText.fontSize = 18;
        descText.text = "Seed와 Tile Count를 입력하고 START를 눌러 맵을 생성하세요.";
        descText.alignment = TextAnchor.MiddleCenter;
        descText.color = new Color(0.9f, 0.9f, 0.9f, 1f);

        // 라벨+인풋 생성용 로컬 함수
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

        // 여백
        GameObject spacer = new GameObject("Spacer");
        spacer.transform.SetParent(panelGO.transform, false);
        LayoutElement spacerLE = spacer.AddComponent<LayoutElement>();
        spacerLE.preferredHeight = 10;

        // START 버튼
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

        // 버튼 텍스트
        GameObject btnTextGO = new GameObject("Text");
        btnTextGO.transform.SetParent(btnGO.transform, false);
        Text btnText = btnTextGO.AddComponent<Text>();
        btnText.font = font;
        btnText.text = "START";
        btnText.fontSize = 24;
        btnText.fontStyle = FontStyle.Bold;
        btnText.alignment = TextAnchor.MiddleCenter;
        btnText.color = Color.white;

        RectTransform btnTextRect = btnText.GetComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.offsetMin = Vector2.zero;
        btnTextRect.offsetMax = Vector2.zero;

        // 처음 포커스
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
    // 레이스(트랙) 생성 뼈대
    // =====================================================
    private void StartRace(int seed, int tileCount)
    {
        // 1) 이전 트랙 / 깔대기 / 플레이어 정리
        ClearTiles();
        playerMarbles.Clear();

        // 2) 트랙 + 깔대기 생성
        BuildTrackFromTiles(seed, tileCount);

        // 3) 플레이어 구슬 생성 (StartPos1~N)
        PlayerMarbleSpawner.SpawnPlayersAtFunnel(
            startFunnelInstance,
            playerCount,
            out playerMarbles
        );

        // 3-1) 결과 매니저에 플레이어 목록 등록
        if (RaceResultManager.Instance != null)
        {
            RaceResultManager.Instance.RegisterPlayers(playerMarbles);
        }

        // 4) 카메라를 1번 구슬에 붙인다
        if (playerMarbles.Count > 0)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                CameraFollow follow = cam.GetComponent<CameraFollow>();
                if (follow == null)
                    follow = cam.gameObject.AddComponent<CameraFollow>();

                follow.target = playerMarbles[0].transform;
            }
        }

        // 5) 게임 시작 시 미니맵 생성 (한 번만)
        if (FindObjectOfType<MinimapController>() == null)
        {
            var minimapGO = new GameObject("MinimapController");
            minimapGO.AddComponent<MinimapController>();
        }
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

        // 스타트 깔대기도 같이 정리
        if (startFunnelInstance != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Object.DestroyImmediate(startFunnelInstance);
            else
#endif
                Object.Destroy(startFunnelInstance);

            startFunnelInstance = null;
        }
    }


    /// <summary>
    /// Seed와 TileCount에 따라 TrackTile 프리팹들을 이어 붙여 트랙을 생성하고,
    /// 각 타일 경로를 샘플링해서 pathPoints에 저장합니다.
    /// 마지막 타일 기준으로 Finish 콜라이더도 생성합니다.
    /// </summary>
    private void BuildTrackFromTiles(int seed, int tileCount)
    {
        Random.InitState(seed);

        pathPoints.Clear();
        spawnedTiles.Clear();

        // Resources/TrackTiles 안에서 TrackTileGenerator가 붙은 프리팹 검색
        GameObject[] prefabGOs = Resources.LoadAll<GameObject>("TrackTiles");
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

        // ───────── 스타트 깔대기 생성 및 첫 타일 시작 위치 계산 ─────────
        // (필요하면 이전 깔대기 삭제는 ClearTiles()에서 처리)
        startFunnelInstance = StartPosCalculator.CreateStartFunnel(transform, startFunnelPrefabPath);

        FunnelGenerator funnelGen = null;
        if (startFunnelInstance != null)
            funnelGen = startFunnelInstance.GetComponent<FunnelGenerator>();

        Vector3 currentPos;
        Vector3 currentForward;

        StartPosCalculator.GetFirstTileStart(
            funnelGen,
            firstTileOffsetFromSpoutExit,
            out currentPos,
            out currentForward
        );

        string currentExitProfileId = null;


        GameObject lastTile = null;
        TrackTileGenerator lastGen = null;

        for (int i = 0; i < tileCount; i++)
        {
            // ProfileId 기준 인접 규칙 (Entry/Exit profileId 매칭)
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

            // Y축 회전만 사용해서 진행 방향 정렬
            Vector3 flatForward = currentForward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Vector3.forward;
            flatForward.Normalize();

            tileGO.transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up);

            // Entry(시작점)를 currentPos에 맞추기
            genInst.GetPathFrameLocal(
                0f,
                out Vector3 entryCenterLocal,
                out _,
                out _
            );

            Vector3 entryCenterWorld = tileGO.transform.TransformPoint(entryCenterLocal);
            tileGO.transform.position = currentPos - entryCenterWorld;

            // 경로 샘플링 (맵 모양/각도 정보 저장)
            int samples = Mathf.Max(2, samplesPerTile);
            for (int s = 0; s < samples; s++)
            {
                float t = (float)s / (samples - 1);

                genInst.GetPathFrameLocal(
                    t,
                    out Vector3 centerLocal,
                    out Vector3 forwardLocal,
                    out _
                );

                Vector3 worldCenter = tileGO.transform.TransformPoint(centerLocal);
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

            // 2타일마다 하나씩 체크포인트 생성
            if (i % 2 == 0)
            {
                CreateCheckpointForTile(tileGO, genInst, i);
            }

            // Exit 기준으로 다음 타일 시작 위치/방향 갱신
            genInst.GetPathFrameLocal(
                1f,
                out Vector3 exitCenterLocal,
                out Vector3 exitForwardLocal,
                out _
            );

            Vector3 exitCenterWorld = tileGO.transform.TransformPoint(exitCenterLocal);
            Vector3 exitForwardWorld = tileGO.transform.TransformDirection(exitForwardLocal);
            exitForwardWorld.y = 0f;

            currentPos = exitCenterWorld;
            currentForward = exitForwardWorld.sqrMagnitude > 0.0001f
                ? exitForwardWorld.normalized
                : Vector3.forward;

            finishPosition = exitCenterWorld;
            currentExitProfileId = genInst.exitProfile.profileId;

            lastTile = tileGO;
            lastGen = genInst;
        }

        // 마지막 타일 기준 Finish 콜라이더 생성
        if (lastTile != null && lastGen != null)
        {
            SetupFinishTrigger(lastTile, lastGen);
        }
    }

    /// <summary>
    /// 해당 타일 중간 지점에 체크포인트 콜라이더를 생성.
    /// </summary>
    private void CreateCheckpointForTile(GameObject tileGO, TrackTileGenerator genInst, int tileIndex)
    {
        if (tileGO == null || genInst == null)
            return;

        // 타일 중간 지점 기준
        genInst.GetPathFrameLocal(
            0.5f,
            out Vector3 centerLocal,
            out Vector3 forwardLocal,
            out _
        );

        Vector3 centerWorld = tileGO.transform.TransformPoint(centerLocal);
        Vector3 forwardWorld = tileGO.transform.TransformDirection(forwardLocal);
        forwardWorld.y = 0f;
        if (forwardWorld.sqrMagnitude < 0.0001f)
            forwardWorld = Vector3.forward;
        forwardWorld.Normalize();

        GameObject cpGO = new GameObject($"Checkpoint_{tileIndex:D3}");
        cpGO.transform.SetParent(transform);
        cpGO.transform.position = centerWorld;
        cpGO.transform.rotation = Quaternion.LookRotation(forwardWorld, Vector3.up);

        BoxCollider col = cpGO.AddComponent<BoxCollider>();
        col.isTrigger = true;

        float trackWidth = 4f;
        if (genInst.middleProfile != null)
            trackWidth = genInst.middleProfile.trackWidth;
        else if (genInst.entryProfile != null)
            trackWidth = genInst.entryProfile.trackWidth;

        // 트랙 단면 전체를 넉넉하게 덮도록 사이즈 설정
        col.size = new Vector3(trackWidth + 1f, 5f, 5f);
        col.center = Vector3.zero;

        CheckpointZone zone = cpGO.AddComponent<CheckpointZone>();
        zone.checkpointIndex = tileIndex;
        // respawnHeightOffset / respawnForwardOffset 는 인스펙터에서 조절 가능
    }

    // =====================================================
    // 트랙 경로 관련 보조 함수 (다른 클래스에서 사용할 수 있도록 유지)
    // =====================================================

    public int PathPointCount
    {
        get { return pathPoints != null ? pathPoints.Count : 0; }
    }

    public int LaneCount
    {
        get { return laneCount; }
    }

    public Vector3 GetPathPointByIndex(int index)
    {
        if (pathPoints == null || pathPoints.Count == 0)
            return Vector3.zero;

        index = Mathf.Clamp(index, 0, pathPoints.Count - 1);
        return pathPoints[index];
    }

    /// <summary>
    /// worldPos에서 가장 가까운 경로 포인트의 인덱스
    /// </summary>
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

        return dir.normalized;
    }

    /// <summary>
    /// 경사 포함 트랙 탄젠트 방향
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

        return dir.normalized;
    }

    /// <summary>
    /// 카메라/연출용 수평 진행 방향
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
    /// worldPos에 가장 가까운 트랙 중심 경로상의 점
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
    // Finish 콜라이더 생성
    // =====================================================
    private void SetupFinishTrigger(GameObject lastTile, TrackTileGenerator gen)
    {
        // 마지막 타일의 끝(center) + 진행 방향을 얻는다
        gen.GetPathFrameLocal(
            1f,
            out Vector3 exitCenterLocal,
            out Vector3 exitForwardLocal,
            out _
        );

        Vector3 exitCenterWorld = lastTile.transform.TransformPoint(exitCenterLocal);
        Vector3 forward = lastTile.transform.TransformDirection(exitForwardLocal);
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        forward.Normalize();

        // 트랙 폭/높이/길이 계산
        float trackWidthAtExit = 4f;

        if (gen.exitProfile != null)
            trackWidthAtExit = gen.exitProfile.trackWidth;
        else if (gen.middleProfile != null)
            trackWidthAtExit = gen.middleProfile.trackWidth;
        else if (gen.entryProfile != null)
            trackWidthAtExit = gen.entryProfile.trackWidth;

        float triggerWidth = trackWidthAtExit + 0.5f;

        float maxWallHeight = 0f;
        if (gen.entryProfile != null) maxWallHeight = Mathf.Max(maxWallHeight, gen.entryProfile.wallHeight);
        if (gen.middleProfile != null) maxWallHeight = Mathf.Max(maxWallHeight, gen.middleProfile.wallHeight);
        if (gen.exitProfile != null) maxWallHeight = Mathf.Max(maxWallHeight, gen.exitProfile.wallHeight);

        float triggerHeight = maxWallHeight + 1f;
        float triggerLength = Mathf.Max(4f, gen.tileLength * 0.25f);

        // ───── floorDepth(바닥 깊이)를 반영해서 "실제 바닥 높이" 계산 ─────
        float floorDepthAtExit = 0f;
        if (gen.exitProfile != null) floorDepthAtExit = Mathf.Max(floorDepthAtExit, gen.exitProfile.floorDepth);
        if (floorDepthAtExit <= 0f && gen.middleProfile != null) floorDepthAtExit = Mathf.Max(floorDepthAtExit, gen.middleProfile.floorDepth);
        if (floorDepthAtExit <= 0f && gen.entryProfile != null) floorDepthAtExit = Mathf.Max(floorDepthAtExit, gen.entryProfile.floorDepth);

        // center line(0)에서 floorDepth만큼 내려간 y가 실제 바닥 높이
        Vector3 floorBaseWorld = exitCenterWorld - Vector3.up * floorDepthAtExit;

        // 콜라이더 중심 = 바닥에서 절반 높이 + 진행 방향으로 절반 길이만큼 앞
        Vector3 triggerCenter =
            floorBaseWorld
            + forward * (triggerLength * 0.5f)
            + Vector3.up * (triggerHeight * 0.5f);

        GameObject go = new GameObject("FinishTrigger");
        go.transform.position = triggerCenter;
        go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        BoxCollider col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(triggerWidth, triggerHeight, triggerLength);
        col.center = Vector3.zero;

        // FinishTrigger가 결승 도착을 감지할 수 있도록 센서 추가
        go.AddComponent<FinishTriggerSensor>();

        // 결승점 비주얼 생성 (텍스트 + 깃발)
        CreateFinishVisual(go, triggerHeight);
    }

    /// <summary>
    /// FinishTrigger 위에 텍스트 + 깃발 비주얼을 생성합니다.
    /// </summary>
    /// <summary>
    /// FinishTrigger 위에 텍스트 + 깃발 비주얼을 생성합니다.
    /// </summary>
    private void CreateFinishVisual(GameObject triggerGO, float triggerHeight)
    {
        if (triggerGO == null) return;

        Transform t = triggerGO.transform;

        // 콜라이더 상단 기준 위치 계산
        Vector3 up = t.up;
        Vector3 visualPos =
            t.position
            + up * (triggerHeight * 0.5f + finishVisualHeightOffset);

        // 루트 오브젝트
        GameObject root = new GameObject("FinishVisual");
        root.transform.SetParent(triggerGO.transform);
        root.transform.position = visualPos;
        root.transform.rotation = t.rotation;

        // ───── FINISH 텍스트 (3D TextMesh) ─────
        GameObject textGO = new GameObject("FinishText");
        textGO.transform.SetParent(root.transform, false);
        textGO.transform.localPosition = Vector3.zero;

        TextMesh tm = textGO.AddComponent<TextMesh>();
        tm.text = string.IsNullOrEmpty(finishText) ? "FINISH" : finishText;
        tm.fontSize = 64;
        tm.characterSize = 0.1f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.yellow;

        // ───── 깃발(Quad) ─────
        GameObject flagGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        flagGO.name = "FinishFlag";
        flagGO.transform.SetParent(root.transform, false);

        // 텍스트 오른쪽 옆, 약간 위에 배치
        flagGO.transform.localPosition = t.right * 0.8f + up * 0.3f;
        flagGO.transform.localRotation = Quaternion.identity;
        flagGO.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);

        // 충돌 필요 없으니 콜라이더 제거
        Collider flagCol = flagGO.GetComponent<Collider>();
        if (flagCol != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(flagCol);
            else
#endif
                Destroy(flagCol);
        }

        MeshRenderer rend = flagGO.GetComponent<MeshRenderer>();
        if (rend == null)
            return;

        // Resources에서 스프라이트 로드 시도
        Sprite flagSprite = null;
        if (!string.IsNullOrEmpty(finishFlagSpritePath))
        {
            flagSprite = Resources.Load<Sprite>(finishFlagSpritePath);
        }

        // ※ 여기서는 새 Material(Shader)을 만들지 않고,
        //    Quad가 원래 가지고 있는 material만 수정합니다.
        Material mat = rend.material;

        if (flagSprite != null && flagSprite.texture != null)
        {
            mat.mainTexture = flagSprite.texture;
            // 필요하면 타일링 등 추가 조정 가능
        }
        else
        {
            // 스프라이트 없으면 단색 재질
            mat.color = Color.red;
        }
    }



}
