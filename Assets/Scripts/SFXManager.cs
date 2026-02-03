using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SFXEntry
{
    public string key;               // "Hit", "Jump" 이런 이름 (기본은 파일명)
    public AudioClip clip;           // 실제 오디오 클립
    [Range(0f, 1f)]
    public float volume = 1f;        // 개별 볼륨
}

public class SFXManager : MonoBehaviour
{
    public static SFXManager Instance { get; private set; }

    [Header("모든 SFX를 재생할 AudioSource (2D 추천)")]
    public AudioSource audioSource;

    [Header("Resources/SFX 에서 자동으로 채워지는 리스트")]
    public List<SFXEntry> sfxList = new List<SFXEntry>();

    // 내부 검색용 딕셔너리
    private Dictionary<string, SFXEntry> sfxDict;

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
        // 기본은 Resources/SFX 에서 자동 로드
        if (sfxList == null || sfxList.Count == 0)
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
    /// Resources/SFX 아래의 모든 AudioClip을 다시 로드해서 리스트를 갱신함
    /// 인스펙터 컨텍스트 메뉴 + 에디터 커스텀 버튼으로도 호출 가능
    /// </summary>
    [ContextMenu("Refresh From Resources/SFX")]
    public void RefreshFromResources()
    {
        sfxList = new List<SFXEntry>();

        AudioClip[] clips = Resources.LoadAll<AudioClip>("SFX"); // Resources/SFX 폴더
        foreach (var clip in clips)
        {
            if (clip == null) continue;

            var entry = new SFXEntry
            {
                key = clip.name,   // 기본 키 = 파일명
                clip = clip,
                volume = 1f
            };
            sfxList.Add(entry);
        }

        BuildDictionary();
        Debug.Log($"[SFXManager] Resources/SFX 에서 {sfxList.Count}개 SFX 로드됨.");
    }

    private void BuildDictionary()
    {
        sfxDict = new Dictionary<string, SFXEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in sfxList)
        {
            if (entry == null || entry.clip == null || string.IsNullOrWhiteSpace(entry.key))
                continue;

            if (sfxDict.ContainsKey(entry.key))
            {
                Debug.LogWarning($"[SFXManager] 중복 key: {entry.key} 는 무시됨");
                continue;
            }

            sfxDict.Add(entry.key, entry);
        }
    }

    /// <summary>
    /// 문자열 key로 재생 ("Hit", "Goal" 등)
    /// </summary>
    public void Play(string key)
    {
        if (audioSource == null)
        {
            Debug.LogWarning("[SFXManager] AudioSource 없음");
            return;
        }

        if (string.IsNullOrEmpty(key)) return;

        if (!sfxDict.TryGetValue(key, out var entry))
        {
            Debug.LogWarning($"[SFXManager] 등록되지 않은 SFX key: {key}");
            return;
        }

        if (entry.clip == null)
        {
            Debug.LogWarning($"[SFXManager] key={key} 의 clip이 없음");
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
        if (index < 0 || index >= sfxList.Count) return;

        var entry = sfxList[index];
        if (entry?.clip == null) return;

        audioSource.PlayOneShot(entry.clip, entry.volume);
    }
}
