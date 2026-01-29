using UnityEngine;
using UnityEngine.SceneManagement;

public static class MarbleRaceBootstrap
{
    private static bool _initialized = false;

    // ▶ 첫 씬이 로드되기 "전"에 한 번만 실행
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        // 이제 이후에 로드되는 씬들(첫 씬 포함)에 대해 OnSceneLoaded가 호출됨
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 이미 씬 안에 MarbleRaceManager가 있으면 새로 안 만든다
        if (Object.FindObjectOfType<MarbleRaceManager>() != null)
            return;

        var go = new GameObject("MarbleRaceManager");
        go.AddComponent<MarbleRaceManager>();

        Debug.Log($"[Bootstrap] MarbleRaceManager created in scene: {scene.name}");
    }
}
