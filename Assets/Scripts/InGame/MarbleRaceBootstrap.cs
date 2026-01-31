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

        // 1) PhysicsManager 없으면 자동 생성 (전역 공용 매니저)
        EnsurePhysicsManager();

        // 2) 이후 로드되는 씬들마다 MarbleRaceManager 자동 생성
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>
    /// 전역 PhysicsManager가 씬 어디에도 없다면 새로 만든다.
    /// DontDestroyOnLoad 로 모든 씬에서 공유.
    /// </summary>
    private static void EnsurePhysicsManager()
    {
        // 혹시 씬에 이미 들어 있는 경우(에디터 테스트용 등) 중복 생성 방지
        var existing = Object.FindObjectOfType<PhysicsManager>();
        if (existing != null)
        {
            // 혹시나 부트스트랩이 먼저 도는 케이스를 대비해 플래그만 찍어둠
            Debug.Log("[Bootstrap] PhysicsManager already exists in scene.");
            return;
        }

        var go = new GameObject("PhysicsManager");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<PhysicsManager>();

        Debug.Log("[Bootstrap] PhysicsManager created (DontDestroyOnLoad).");
    }

    /// <summary>
    /// 씬이 로드될 때마다 호출.
    /// 해당 씬에 MarbleRaceManager 가 없으면 자동으로 하나 생성.
    /// </summary>
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 이미 씬 안에 MarbleRaceManager가 있으면 새로 안 만든다
        if (Object.FindObjectOfType<MarbleRaceManager>() != null)
        {
            //Debug.Log($"[Bootstrap] MarbleRaceManager already exists in scene: {scene.name}");
            return;
        }

        var go = new GameObject("MarbleRaceManager");
        go.AddComponent<MarbleRaceManager>();

        Debug.Log($"[Bootstrap] MarbleRaceManager created in scene: {scene.name}");
    }
}
