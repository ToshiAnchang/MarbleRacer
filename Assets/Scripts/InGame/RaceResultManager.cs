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

    private int _finishOrder = 0;
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
        _finishOrder = 0;
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

        // 이미 들어온 구슬이면 무시
        if (_finished.Contains(marble))
            return;

        _finished.Add(marble);
        _finishOrder++;
        int rank = _finishOrder;

        // 1) 구슬의 이동 멈추기 - 즉시가 아니라 1초 후에 멈추도록 코루틴
        StartCoroutine(StopMarbleAfterDelay(marble, 0.3f));

        // 2) 구슬에 등수 숫자 표시
        ShowRankOnMarble(marble.transform, rank);

        // 3) 디버그 로그
        Debug.Log($"[RaceResult] {marble.name} finished as #{rank}");

        // 4) 전부 도착했으면 3초 후 재시작
        if (!_restartScheduled && _players.Count > 0 && _finished.Count >= _players.Count)
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
