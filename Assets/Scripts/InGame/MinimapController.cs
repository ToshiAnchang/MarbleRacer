using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 우측 상단에 미니맵을 표시하는 HUD 컨트롤러.
/// - 중심: 현재 플레이어(카메라가 따라가는 구슬)
/// - 모든 플레이어 구슬 표시
/// - 미니맵 범위를 벗어난 구슬은,
///   현재 플레이어 기준으로 y축으로 위/아래인지 표시
/// </summary>
public class MinimapController : MonoBehaviour
{
    [Header("미니맵 크기 (픽셀)")]
    public Vector2 minimapSize = new Vector2(260f, 260f);

    [Header("미니맵에 표시할 Y축 범위")]
    [Tooltip("현재 플레이어를 기준으로 위/아래로 이만큼의 Y 거리까지 미니맵 안에 표시합니다.")]
    public float worldRange = 40f;

    [Header("마커 설정")]
    [Tooltip("마커 반지름 (픽셀 단위)")]
    public float markerRadius = 6f;

    public Color currentPlayerColor = Color.yellow;
    public Color otherPlayerColor = Color.cyan;

    [Tooltip("미니맵 범위를 벗어난 구슬이 현재 플레이어보다 위에 있을 때 색상")]
    public Color aboveColor = new Color(0.4f, 1f, 0.4f, 1f);

    [Tooltip("미니맵 범위를 벗어난 구슬이 현재 플레이어보다 아래에 있을 때 색상")]
    public Color belowColor = new Color(1f, 0.4f, 0.4f, 1f);

    [Tooltip("y 높이 차이가 이 값보다 작으면 위/아래 표시를 생략합니다.")]
    public float yDifferenceThreshold = 0.5f;

    private RectTransform _minimapPanel;
    private Dictionary<PlayerMarble, Image> _markerImages = new Dictionary<PlayerMarble, Image>();
    private Dictionary<PlayerMarble, Color> _markerBaseColors = new Dictionary<PlayerMarble, Color>();

    private MarbleRaceManager _manager;
    private CameraFollow _cameraFollow;

    private void Start()
    {
        _manager = MarbleRaceManager.Instance;
        if (Camera.main != null)
        {
            _cameraFollow = Camera.main.GetComponent<CameraFollow>();
        }

        CreateMinimapUI();
        InitMarkers();
    }

    private void LateUpdate()
    {
        if (_manager == null || _minimapPanel == null)
            return;

        // 플레이어가 추가/변경되었으면 마커 다시 구성
        if (_manager.PlayerMarbles != null &&
            _markerImages.Count != _manager.PlayerMarbles.Count)
        {
            InitMarkers();
        }

        UpdateMarkers();
    }

    // ───────────────────────────────── UI 생성 ─────────────────────────────────

    private void CreateMinimapUI()
    {
        // 이미 만들어진 캔버스가 있으면 재사용, 없으면 새로 생성
        GameObject canvasGO = GameObject.Find("HUDCanvas_Minimap");
        Canvas canvas;

        if (canvasGO == null)
        {
            canvasGO = new GameObject("HUDCanvas_Minimap");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            canvasGO.AddComponent<GraphicRaycaster>();
        }
        else
        {
            canvas = canvasGO.GetComponent<Canvas>();
            if (canvas == null)
                canvas = canvasGO.AddComponent<Canvas>();
        }

        // 미니맵 패널 생성 (오른쪽 상단)
        GameObject panelGO = new GameObject("MinimapPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);

        _minimapPanel = panelGO.AddComponent<RectTransform>();
        _minimapPanel.sizeDelta = minimapSize;
        _minimapPanel.anchorMin = new Vector2(1f, 1f);
        _minimapPanel.anchorMax = new Vector2(1f, 1f);
        _minimapPanel.pivot = new Vector2(1f, 1f);
        _minimapPanel.anchoredPosition = new Vector2(-20f, -20f);

        Image bg = panelGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);

        Outline outline = panelGO.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.2f);
        outline.effectDistance = new Vector2(2f, -2f);
    }

    private void InitMarkers()
    {
        // 기존 마커 모두 제거
        foreach (var kv in _markerImages)
        {
            if (kv.Value != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(kv.Value.gameObject);
                else
#endif
                    Destroy(kv.Value.gameObject);
            }
        }
        _markerImages.Clear();
        _markerBaseColors.Clear();

        if (_manager == null || _minimapPanel == null)
            return;

        var list = _manager.PlayerMarbles;
        if (list == null)
            return;

        foreach (var pm in list)
        {
            if (pm == null)
                continue;

            GameObject markerGO = new GameObject($"Marker_{pm.name}");
            markerGO.transform.SetParent(_minimapPanel, false);

            RectTransform rt = markerGO.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(markerRadius * 2f, markerRadius * 2f);

            Image img = markerGO.AddComponent<Image>();

            // ▶ 기본 색은 해당 구슬의 머티리얼 색을 사용 (없으면 otherPlayerColor)
            Color baseColor = otherPlayerColor;
            Renderer rend = pm.GetComponent<Renderer>();
            if (rend != null && rend.material != null && rend.material.HasProperty("_Color"))
            {
                baseColor = rend.material.color;
            }

            img.color = baseColor;

            _markerImages[pm] = img;
            _markerBaseColors[pm] = baseColor;
        }
    }

    // ───────────────────────────────── 마커 갱신 ─────────────────────────────────
    private void UpdateMarkers()
    {
        if (_manager == null || _minimapPanel == null)
            return;

        var list = _manager.PlayerMarbles;
        if (list == null || list.Count == 0)
            return;

        // 기준이 되는 "현재 플레이어" = 카메라가 따라가는 타겟
        Transform centerTr = null;

        if (_cameraFollow != null && _cameraFollow.target != null)
        {
            centerTr = _cameraFollow.target;
        }
        else
        {
            // 카메라 정보가 없으면 0번 플레이어를 기준으로 사용
            if (list[0] != null)
                centerTr = list[0].transform;
        }

        if (centerTr == null)
            return;

        Vector3 centerPos = centerTr.position;
        float centerY = centerPos.y;

        // 미니맵 패널 반지름(픽셀)
        float halfW = _minimapPanel.rect.width * 0.5f;
        float halfH = _minimapPanel.rect.height * 0.5f;

        // 마커가 빠져나가지 않도록 안쪽 여백
        float innerW = halfW - markerRadius - 2f;
        float innerH = halfH - markerRadius - 2f;

        // worldRange 는 Y축 표시 범위
        float yRange = Mathf.Max(1f, worldRange);

        for (int i = 0; i < list.Count; i++)
        {
            PlayerMarble pm = list[i];
            if (pm == null)
                continue;

            Image img;
            if (!_markerImages.TryGetValue(pm, out img) || img == null)
                continue;

            RectTransform rt = img.rectTransform;

            // 이 구슬의 기본 색 (구슬 머티리얼에서 가져온 색)
            Color baseColor = otherPlayerColor;
            _markerBaseColors.TryGetValue(pm, out baseColor);

            // 현재 플레이어와의 Y 차이
            float dy = pm.transform.position.y - centerY;

            // 마커들이 완전 겹치지 않도록 약간 가로로 벌려주기
            float xOffset = 0f;
            if (list.Count > 1)
            {
                float t = (list.Count == 1) ? 0.5f : (float)i / (list.Count - 1);
                xOffset = Mathf.Lerp(-innerW + markerRadius, innerW - markerRadius, t) * 0.4f;
            }

            // ───── 내 구슬(현재 플레이어) 처리 ─────
            if (pm.transform == centerTr)
            {
                rt.anchoredPosition = Vector2.zero;
                // 내 구슬은 더 크게 표시
                rt.sizeDelta = new Vector2(markerRadius * 2.4f, markerRadius * 2.4f);
                img.color = baseColor;   // 내 구슬 색 그대로
                img.enabled = true;
                continue;
            }

            // ───── 다른 구슬 처리 ─────

            bool insideRange = Mathf.Abs(dy) <= yRange;

            if (insideRange)
            {
                // 범위 안: y 비율에 따라 위/아래 위치
                // dy > 0 : 기준 플레이어보다 위쪽 (덜 내려옴)
                // dy < 0 : 기준 플레이어보다 아래쪽 (더 내려옴, 선두 쪽)
                float ny = Mathf.Clamp(dy / yRange, -1f, 1f);
                float py = ny * innerH;

                rt.anchoredPosition = new Vector2(xOffset, py);
                rt.sizeDelta = new Vector2(markerRadius * 2f, markerRadius * 2f);

                // 다른 구슬은 자기 구슬 색으로 표시
                img.color = baseColor;
                img.enabled = true;
            }
            else
            {
                // 범위를 벗어나면 위/아래만 표시
                if (Mathf.Abs(dy) < yDifferenceThreshold)
                {
                    img.enabled = false;
                    continue;
                }

                float py = (dy > 0f) ? innerH : -innerH;
                rt.anchoredPosition = new Vector2(xOffset, py);
                rt.sizeDelta = new Vector2(markerRadius * 1.6f, markerRadius * 1.6f);
                Color c = baseColor;
                c.a = 0.5f;
                img.color = c;
                img.enabled = true;
            }
        }
    }

}
