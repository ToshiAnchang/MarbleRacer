using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 레이스 결과/등수/재시작을 관리하는 전담 매니저.
/// - 플레이어 구슬 등록
/// - 결승선 도착 순서 기록
/// - 구슬 멈추기 + 등수 숫자 표시
/// - 전체 도착 후 3초 기다렸다가 씬 재시작
/// </summary>
public class RaceResultManager : MonoBehaviour
{
    public static RaceResultManager Instance { get; private set; }

    private readonly List<PlayerMarble> _players = new List<PlayerMarble>();
    private readonly HashSet<PlayerMarble> _finished = new HashSet<PlayerMarble>();
    private readonly HashSet<PlayerMarble> _disqualified = new HashSet<PlayerMarble>();

    // 완주한 구슬 수
    private int _finishCount = 0;

    // 실격된 구슬 수
    private int _dqCount = 0;

    private bool _restartScheduled = false;


    public int TotalPlayerCount => _players.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// 이번 레이스에 참여하는 플레이어 구슬 목록을 등록.
    /// (씬이 새로 시작될 때마다 다시 호출됨)
    /// </summary>
    public void RegisterPlayers(List<PlayerMarble> players)
    {
        _players.Clear();
        _finished.Clear();
        _disqualified.Clear();

        _finishCount = 0;
        _dqCount = 0;
        _restartScheduled = false;

        if (players != null)
            _players.AddRange(players);
    }


    /// <summary>
    /// FinishTrigger에서 구슬이 들어왔다고 알려 줄 때 호출.
    /// </summary>
    public void OnMarbleFinished(PlayerMarble marble)
    {
        if (marble == null)
            return;

        // 이미 결승/실격된 구슬이면 무시
        if (_finished.Contains(marble) || _disqualified.Contains(marble))
            return;

        _finished.Add(marble);
        _finishCount++;
        int rank = _finishCount;   // 1등부터 위에서부터 채움

        // 플레이어에게도 "완주" 상태 전달
        marble.MarkFinished();

        // 1초 뒤에 멈추도록 (앞에 구슬 때문에 피니쉬 못 찍는 문제 방지)
        StartCoroutine(StopMarbleAfterDelay(marble, 1f));

        // 등수 숫자 표시
        ShowRankOnMarble(marble.transform, rank);

        // 로그
        Debug.Log($"[RaceResult] {marble.name} finished as #{rank}");

        // 모든 구슬이 (완주 or 실격) 처리되었으면 3초 뒤 재시작
        if (!_restartScheduled && _players.Count > 0 &&
            _finished.Count + _disqualified.Count >= _players.Count)
        {
            _restartScheduled = true;
            StartCoroutine(RestartAfterDelay(3f));
        }
    }

    /// <summary>
    /// 플레이어가 트랙 이탈을 여러 번 해서 "실격" 처리될 때 호출.
    /// 랭킹은 뒤에서부터 채워 넣는다.
    /// </summary>
    public void OnMarbleDisqualified(PlayerMarble marble)
    {
        if (marble == null)
            return;

        // 이미 완주/실격 처리된 경우 무시
        if (_finished.Contains(marble) || _disqualified.Contains(marble))
            return;

        _disqualified.Add(marble);
        _dqCount++;

        int total = _players.Count > 0 ? _players.Count : (_finished.Count + _disqualified.Count);
        // 예: 총 6명 → 첫 실격은 6등, 두 번째 실격은 5등 ...
        int rank = total - _dqCount + 1;

        // 플레이어 쪽에도 실격 상태 전달
        marble.MarkDisqualified();

        // 등수 숫자 표시
        ShowRankOnMarble(marble.transform, rank);

        Debug.Log($"[RaceResult] {marble.name} DISQUALIFIED, rank #{rank}");

        // 모든 구슬이 (완주 or 실격) 처리되었으면 3초 뒤 재시작
        if (!_restartScheduled && _players.Count > 0 &&
            _finished.Count + _disqualified.Count >= _players.Count)
        {
            _restartScheduled = true;
            StartCoroutine(RestartAfterDelay(3f));
        }
    }

    private IEnumerator StopMarbleAfterDelay(PlayerMarble marble, float delay)
    {
        yield return new WaitForSeconds(delay);
        StopMarble(marble);
    }


    private void StopMarble(PlayerMarble marble)
    {
        Rigidbody rb = marble.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // PlayerMarble 스크립트 비활성화해서 더 이상 중력/힘 안 받도록
        marble.enabled = false;
    }

    private void ShowRankOnMarble(Transform marbleTr, int rank)
    {
        if (marbleTr == null)
            return;

        Transform child = marbleTr.Find("RankText");
        TextMesh tm;
        RankBillboard billboard;

        if (child == null)
        {
            GameObject go = new GameObject("RankText");
            go.transform.SetParent(marbleTr, false);

            tm = go.AddComponent<TextMesh>();
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = 0.1f;
            tm.color = Color.white;

            billboard = go.AddComponent<RankBillboard>();
            billboard.target = marbleTr;
            billboard.heightOffset = 0.6f;
        }
        else
        {
            tm = child.GetComponent<TextMesh>();
            if (tm == null)
                tm = child.gameObject.AddComponent<TextMesh>();

            billboard = child.GetComponent<RankBillboard>();
            if (billboard == null)
            {
                billboard = child.gameObject.AddComponent<RankBillboard>();
                billboard.target = marbleTr;
                billboard.heightOffset = 0.6f;
            }
        }

        tm.text = rank.ToString();
    }


    private IEnumerator RestartAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        var scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.buildIndex);
        // 씬이 다시 로드되면 MarbleRaceManager.Start()에서
        // Seed/Count 입력 캔버스를 다시 만들어주므로
        // 따로 손댈 필요 없음.
    }
}
