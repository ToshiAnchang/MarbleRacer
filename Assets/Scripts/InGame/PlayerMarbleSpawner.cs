using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StartFunnel 하위의 StartPos1~6 포인트에서
/// 플레이어 구슬들을 생성하는 전담 스패너.
/// </summary>
public static class PlayerMarbleSpawner
{
    /// <summary>
    /// StartFunnel 오브젝트의 자식 중 "StartPos1" ~ "StartPosN" 을 찾아
    /// 각 위치에서 플레이어 구슬을 생성한다.
    /// 
    /// createdMarbles : 생성된 PlayerMarble 리스트 (카메라 연출 등에서 활용 가능)
    /// </summary>
    public static void SpawnPlayersAtFunnel(
        GameObject startFunnel,
        int playerCount,
        out List<PlayerMarble> createdMarbles)
    {
        createdMarbles = new List<PlayerMarble>();

        if (startFunnel == null)
        {
            Debug.LogError("[PlayerMarbleSpawner] startFunnel 이 null 입니다. 깔대기 인스턴스를 먼저 생성해야 합니다.");
            return;
        }

        Transform root = startFunnel.transform;

        for (int i = 1; i <= playerCount; i++)
        {
            string childName = $"StartLane/StartPos{i}";
            Transform spawnPoint = root.Find(childName);

            if (spawnPoint == null)
            {
                Debug.LogWarning($"[PlayerMarbleSpawner] {childName} 을(를) 찾을 수 없습니다. 해당 플레이어는 스킵됩니다.");
                continue;
            }

            // === 실제 구슬 GameObject 생성 ===
            GameObject marbleGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marbleGO.name = $"PlayerMarble_{i:D2}";

            // 위치 / 회전은 StartPos 를 그대로 따라감
            marbleGO.transform.position = spawnPoint.position;
            marbleGO.transform.rotation = spawnPoint.rotation;
            marbleGO.transform.localScale = Vector3.one * 1.0f; // 구슬 크기는 여기서 조절

            // Rigidbody + PlayerMarble 스크립트 부착
            Rigidbody rb = marbleGO.AddComponent<Rigidbody>();

            PlayerMarble pm = marbleGO.AddComponent<PlayerMarble>();
            pm.radius = 0.5f;           // 필요시 수정 가능

            // ▶ 깔대기 중심 Transform 넘겨주기 (회전용)
            if (startFunnel != null)
                pm.funnelCenter = startFunnel.transform;

            // 머티리얼 색상 간단하게 플레이어마다 다르게
            Renderer rend = marbleGO.GetComponent<Renderer>();
            if (rend != null)
            {
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = GetColorByIndex(i - 1);
                rend.material = mat;
            }

            createdMarbles.Add(pm);
        }
    }

    private static Color GetColorByIndex(int idx)
    {
        Color[] colors =
        {
            Color.red,
            Color.blue,
            Color.green,
            Color.yellow,
            Color.magenta,
            Color.cyan
        };

        if (colors.Length == 0)
            return Color.white;

        return colors[idx % colors.Length];
    }
}
