using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class BGMEntry
{
    public string key;               // "Hit", "Jump" 이런 이름 (기본은 파일명)
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

        // 효과음은 2D가 편함
        audioSource.playOnAwake = false;
        audioSource.loop = false;
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
    /// 문자열 key로 재생 ("Hit", "Goal" 등)
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

        if (entry.clip == null)
        {
            Debug.LogWarning($"[BGMManager] key={key} 의 clip이 없음");
            return;
        }

        audioSource.PlayOneShot(entry.clip, entry.volume);
    }

    /// <summary>
    /// 인스펙터에서 index로 재생할 때 사용 (커스텀 에디터에서 호출)
    /// </summary>
    public void PlayByIndex(int index)
    {
        if (audioSource == null) return;
        if (index < 0 || index >= bgmList.Count) return;

        var entry = bgmList[index];
        if (entry?.clip == null) return;

        audioSource.PlayOneShot(entry.clip, entry.volume);
    }
}
