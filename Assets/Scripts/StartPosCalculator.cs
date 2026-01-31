using UnityEngine;

/// <summary>
/// 스타트 깔대기 프리팹 생성 +
/// 깔대기 주둥이 끝 기준으로 첫 트랙 타일의 시작 위치/방향을 계산하는 유틸리티.
/// </summary>
public static class StartPosCalculator
{
    /// <summary>
    /// Resources 폴더에서 깔대기 프리팹을 찾아 생성하고, parent 아래에 붙인다.
    /// prefabPath 예: "StartFunnel"
    /// </summary>
    public static GameObject CreateStartFunnel(Transform parent, string prefabPath)
    {
        if (string.IsNullOrEmpty(prefabPath))
            prefabPath = "StartFunnel";

        GameObject prefab = Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[StartPosCalculator] Resources/{prefabPath} 프리팹을 찾을 수 없습니다.");
            return null;
        }

        GameObject go = Object.Instantiate(prefab, parent);
        go.name = "StartFunnel";
        return go;
    }

    /// <summary>
    /// 깔대기 주둥이 끝 기준으로
    /// "첫 타일의 entry가 위치해야 할 좌표"와 "트랙 진행 방향"을 계산.
    /// 
    /// offsetFromSpoutExit:
    ///   x = 좌우 (오른쪽 +)
    ///   y = 위/아래 (위 +)
    ///   z = 앞뒤 (트랙 진행 방향 +)
    /// </summary>
    public static void GetFirstTileStart(
    FunnelGenerator funnel,
    Vector3 offsetFromSpoutExit,
    out Vector3 startPos,
    out Vector3 startForward)
    {
        // 깔대기 없으면 그냥 원점 기준 기본값
        if (funnel == null)
        {
            startPos = Vector3.zero;
            startForward = Vector3.forward;
            return;
        }

        // 주둥이 끝의 위치/방향 계산
        GetSpoutExitFrame(funnel, out Vector3 exitPos, out Vector3 exitForward);

        // 트랙 진행 방향은 수평 성분만 사용
        Vector3 flatForward = new Vector3(exitForward.x, 0f, exitForward.z);
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        // 수평 기준 좌/우 벡터
        Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
        Vector3 up = Vector3.up;

        // Z 방향으로 1.5만큼 더 깔대기 쪽(반대 방향)으로 당긴 보정 오프셋
        Vector3 adjustedOffset = offsetFromSpoutExit;
        adjustedOffset.z -= 1.5f;

        // 오프셋 적용
        startPos =
            exitPos
            + right * adjustedOffset.x
            + up * adjustedOffset.y
            + flatForward * adjustedOffset.z;

        startForward = flatForward;
    }


    /// <summary>
    /// FunnelGenerator.GenerateMesh 에서 사용하는 것과 같은 수식을 이용해
    /// 주둥이 끝(center) 위치와 진행 방향(tangent)을 계산.
    /// </summary>
    private static void GetSpoutExitFrame(
        FunnelGenerator funnel,
        out Vector3 exitPosWorld,
        out Vector3 exitForwardWorld)
    {
        // FunnelGenerator 의 파라미터와 일치해야 함
        float marbleDiameter = Mathf.Max(0.0001f, funnel.marbleDiameter);
        float marbleRadius = marbleDiameter * 0.5f;

        float topRadius = marbleRadius * funnel.topDiameterMultiplier * 0.5f;
        float bottomRadius = marbleRadius * funnel.bottomDiameterMultiplier * 0.5f;
        float spoutRadius = bottomRadius * Mathf.Max(0.0001f, funnel.spoutRadiusMultiplier);

        // YZ 평면 상의 3개 포인트 (2차 베지어)
        float y0 = -funnel.funnelHeight;

        float y2 = -funnel.funnelHeight - funnel.spoutLength;
        float z2 = funnel.spoutBendOffsetZ;

        float y1 = Mathf.Lerp(y0, y2, 0.3f);
        float z1 = 0f;

        // t = 1 에서의 위치 (P2)
        float exitY = y2;
        float exitZ = z2;

        // t = 1 에서의 접선: B'(1) = 2 * (P2 - P1)
        float tanY = 2f * (y2 - y1);
        float tanZ = 2f * (z2 - z1);

        Vector3 localExitPos = new Vector3(0f, exitY, exitZ);
        Vector3 localTangent = new Vector3(0f, tanY, tanZ);

        exitPosWorld = funnel.transform.TransformPoint(localExitPos);
        exitForwardWorld = funnel.transform.TransformDirection(localTangent).normalized;
    }
}
