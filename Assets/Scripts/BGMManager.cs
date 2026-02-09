using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class BGMEntry
{
    public string key;               // "Main", "Title" 등
    public AudioClip clip;           // 실제 오디오 클립
    [Range(0f, 1f)]
    public float volume = 1f;        // 개별 볼륨
}

public class BGMManager : MonoBehaviour
{
    public static BGMManager Instance { get; private set; }

    [Header("모든 BGM를 재생할 AudioSource (2D 추천)")]
    public AudioSource audioSource;

    [Header("Resources/BGM 에서 자동으로 채워지는 리스트")]
    public List<BGMEntry> bgmList = new List<BGMEntry>();

    // 내부 검색용 딕셔너리
    private Dictionary<string, BGMEntry> bgmDict;

    private void Awake()
    {
        // 싱글톤 세팅
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 씬 넘어가도 유지하고 싶으면 주석 해제
        //DontDestroyOnLoad(gameObject);

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        // 에디터에서 인스펙터로 채울 수도 있지만,
        // 기본은 Resources/BGM 에서 자동 로드
        if (bgmList == null || bgmList.Count == 0)
        {
            RefreshFromResources();
        }
        else
        {
            BuildDictionary();
        }

        // BGM은 보통 2D + 루프
        audioSource.playOnAwake = false;
        audioSource.loop = true;       // BGM이니까 기본적으로 루프
        audioSource.spatialBlend = 0f;
    }

    /// <summary>
    /// Resources/BGM 아래의 모든 AudioClip을 다시 로드해서 리스트를 갱신함
    /// 인스펙터 컨텍스트 메뉴 + 에디터 커스텀 버튼으로도 호출 가능
    /// </summary>
    [ContextMenu("Refresh From Resources/BGM")]
    public void RefreshFromResources()
    {
        bgmList = new List<BGMEntry>();

        AudioClip[] clips = Resources.LoadAll<AudioClip>("BGM"); // Resources/BGM 폴더
        foreach (var clip in clips)
        {
            if (clip == null) continue;

            var entry = new BGMEntry
            {
                key = clip.name,   // 기본 키 = 파일명
                clip = clip,
                volume = 1f
            };
            bgmList.Add(entry);
        }

        BuildDictionary();
        Debug.Log($"[BGMManager] Resources/BGM 에서 {bgmList.Count}개 BGM 로드됨.");
    }

    private void BuildDictionary()
    {
        bgmDict = new Dictionary<string, BGMEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in bgmList)
        {
            if (entry == null || entry.clip == null || string.IsNullOrWhiteSpace(entry.key))
                continue;

            if (bgmDict.ContainsKey(entry.key))
            {
                Debug.LogWarning($"[BGMManager] 중복 key: {entry.key} 는 무시됨");
                continue;
            }

            bgmDict.Add(entry.key, entry);
        }
    }

    /// <summary>
    /// 내부 공용 함수: BGMEntry 하나를 재생
    /// - 기존 BGM은 무조건 정지
    /// - 항상 1개만 재생
    /// </summary>
    private void PlayEntry(BGMEntry entry)
    {
        if (audioSource == null || entry == null || entry.clip == null)
            return;

        // 이전에 재생 중이던 건 무조건 정지
        audioSource.Stop();

        // 새 BGM 설정
        audioSource.clip = entry.clip;
        audioSource.volume = entry.volume;

        // BGM은 기본적으로 루프 (필요 없으면 false로 바꾸셔도 됩니다)
        audioSource.loop = true;

        // 재생
        audioSource.Play();
    }

    /// <summary>
    /// 문자열 key로 재생 ("Main", "Title" 등)
    /// </summary>
    public void Play(string key)
    {
        if (audioSource == null)
        {
            Debug.LogWarning("[BGMManager] AudioSource 없음");
            return;
        }

        if (string.IsNullOrEmpty(key)) return;

        if (!bgmDict.TryGetValue(key, out var entry))
        {
            Debug.LogWarning($"[BGMManager] 등록되지 않은 BGM key: {key}");
            return;
        }

        PlayEntry(entry);
    }

    /// <summary>
    /// 인덱스로 재생 (커스텀 에디터에서 호출)
    /// </summary>
    public void PlayByIndex(int index)
    {
        if (audioSource == null) return;
        if (index < 0 || index >= bgmList.Count) return;

        var entry = bgmList[index];
        PlayEntry(entry);
    }

    /// <summary>
    /// 현재 재생 중인 BGM 정지
    /// </summary>
    public void Stop()
    {
        if (audioSource == null) return;

        audioSource.Stop();
        audioSource.clip = null; // 선택사항 (완전 초기화)
    }

    /// <summary>
    /// 현재 BGM 일시정지
    /// </summary>
    public void Pause()
    {
        if (audioSource == null) return;
        if (!audioSource.isPlaying) return;

        audioSource.Pause();
    }

    /// <summary>
    /// 일시정지된 BGM 다시 재생
    /// </summary>
    public void Resume()
    {
        if (audioSource == null) return;
        if (audioSource.clip == null) return;

        audioSource.UnPause();
    }

    public void Stop(string key)
    {
        if (audioSource == null) return;
        if (audioSource.clip == null) return;

        if (string.Equals(audioSource.clip.name, key, StringComparison.OrdinalIgnoreCase))
        {
            Stop();
        }
    }

}
