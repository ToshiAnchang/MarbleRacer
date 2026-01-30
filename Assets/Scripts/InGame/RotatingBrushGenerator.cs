using UnityEngine;

/// <summary>
/// 회전 브러시 장애물을 타일 위에 배치하는 전담 헬퍼.
/// 
/// - MarbleRaceManager 의 설정값들을 참조해서,
///   어떤 타일에 브러시를 둘지 / 어떤 파라미터로 둘지 결정.
/// - seed 기반 Random 에 따라 항상 동일한 패턴이 나오도록 설계.
/// - lastTileHasBrush 플래그로 “연속 타일 배치 방지” (간이 WFC 인접 제약).
/// </summary>
public static class RotatingBrushObstacleGenerator
{
    /// <summary>
    /// 하나의 TrackTile 위에 회전 브러시를 배치할지 결정하고,
    /// 배치하기로 했다면 해당 타일의 경로/폭/높이에 맞춰 RotatingBrush를 생성한다.
    /// 
    /// manager : 설정값을 들고 있는 MarbleRaceManager
    /// tileGO  : 현재 타일 GameObject
    /// genInst : 타일의 TrackTileGenerator 컴포넌트
    /// tileIndex / totalTileCount : 시작/마지막 타일 보호용 및 규칙용
    /// lastTileHasBrush : 이전 타일에 브러시가 있었는지를 나타내는 ref 플래그
    /// </summary>
    public static void TryPlaceRotatingBrushOnTile(
        MarbleRaceManager manager,
        GameObject tileGO,
        TrackTileGenerator genInst,
        int tileIndex,
        int totalTileCount,
        ref bool lastTileHasBrush)
    {
        if (manager == null || tileGO == null || genInst == null)
        {
            lastTileHasBrush = false;
            return;
        }

        if (!manager.enableRotatingBrushes)
        {
            lastTileHasBrush = false;
            return;
        }

        // 첫 타일 / 마지막 타일에는 장애물 두지 않음 (스타트/피니시 보호)
        if (tileIndex <= 0 || tileIndex >= totalTileCount - 1)
        {
            lastTileHasBrush = false;
            return;
        }

        // 직전 타일에 이미 브러시가 있으면 이번 타일은 강제 비움
        // → 인접 제약: 장애물이 연속해서 붙지 않게
        if (lastTileHasBrush)
        {
            lastTileHasBrush = false;
            return;
        }

        // 전체 밀도가 0 이면 아무것도 두지 않음
        if (manager.rotatingBrushDensity <= 0f)
        {
            lastTileHasBrush = false;
            return;
        }

        // 확률 체크
        if (Random.value > manager.rotatingBrushDensity)
        {
            lastTileHasBrush = false;
            return;
        }

        // ===== 여기까지 왔으면 이 타일에 브러시를 배치 =====
        lastTileHasBrush = true;

        // ───── 타일 중앙의 center / forward 구하기 (경사 포함) ─────
        genInst.GetPathFrameLocal(
            0.5f, // 타일 중앙
            out Vector3 centerLocal,
            out Vector3 forwardLocal,
            out _
        );

        // 로컬 → 월드
        Vector3 centerWorld = tileGO.transform.TransformPoint(centerLocal);

        // 트랙 진행 방향 (경사 포함)
        Vector3 forwardWorld = tileGO.transform.TransformDirection(forwardLocal);
        if (forwardWorld.sqrMagnitude < 0.0001f)
            forwardWorld = tileGO.transform.forward;
        forwardWorld.Normalize();

        // 진행 방향의 수평 투영
        Vector3 forwardHoriz = new Vector3(forwardWorld.x, 0f, forwardWorld.z);
        if (forwardHoriz.sqrMagnitude < 0.0001f)
            forwardHoriz = Vector3.forward;
        forwardHoriz.Normalize();

        // 수평 기준 오른쪽 (트랙 폭 방향)
        Vector3 rightWorld = Vector3.Cross(Vector3.up, forwardHoriz);
        if (rightWorld.sqrMagnitude < 0.0001f)
            rightWorld = Vector3.right;
        rightWorld.Normalize();

        // ===== 실제 트랙 표면 법선(normal) 근사 =====
        //  - 한 방향(t1) = 트랙 진행 방향 forwardWorld (경사 포함)
        //  - 다른 방향(t2) = 폭 방향 rightWorld (대부분 수평)
        //  - normal = t2 × t1
        Vector3 upWorld = Vector3.Cross(rightWorld, forwardWorld);
        if (upWorld.sqrMagnitude < 0.0001f)
            upWorld = Vector3.up;
        upWorld.Normalize();

        // ───── 트랙 폭 기준 가져오기 (브러시 길이용) ─────
        float trackWidth = 4f;
        if (genInst.middleProfile != null)
            trackWidth = genInst.middleProfile.trackWidth;
        else if (genInst.exitProfile != null)
            trackWidth = genInst.exitProfile.trackWidth;
        else if (genInst.entryProfile != null)
            trackWidth = genInst.entryProfile.trackWidth;

        // ───── 브러시 크기 계산 ─────
        float coverage = Mathf.Clamp01(manager.rotatingBrushTrackCoverage);
        float brushLength = trackWidth * coverage;
        float brushThickness = Mathf.Max(0.05f, manager.rotatingBrushThickness);

        // ───── 좌우 방향 벡터 계산 ─────
        // forwardWorld, upWorld 는 이미 위에서 구해져 있다고 가정
        rightWorld = Vector3.Cross(upWorld, forwardWorld).normalized;

        // ───── 좌우 오프셋 계산 ─────
        // 트랙 반폭 * 비율 안에서 랜덤으로 좌/우 위치 선정
        float halfWidth = trackWidth * 0.5f;
        float lateralRatio = Mathf.Clamp01(manager.rotatingBrushLateralOffsetRatio);
        float lateralOffset = Random.Range(-halfWidth * lateralRatio,
                                            halfWidth * lateralRatio);

        // ───── 브러시 위치: 중앙 + 좌우 오프셋 + 높이 오프셋 ─────
        Vector3 brushWorldPos =
            centerWorld
            + rightWorld * lateralOffset
            + upWorld * manager.rotatingBrushHeightOffset;

        // ───── 브러시 생성 ─────
        float oscSpeed = Random.Range(manager.rotatingBrushOscillateSpeedMin,
                                      manager.rotatingBrushOscillateSpeedMax);
        float phase = Random.Range(0f, 360f);

        GameObject brushGO = RotatingBrush.CreateBrush(
            $"RotatingBrush_{tileIndex:D3}",
            tileGO.transform,
            brushWorldPos,
            new Vector3(brushLength, brushThickness, brushThickness),
            localAxis: Vector3.up,                  // Y축 기준 회전
            swingAngle: manager.rotatingBrushSwingAngle,
            oscillateSpeed: oscSpeed,
            phaseOffsetDegrees: phase
        );

        // ※ 추가 회전은 주지 않습니다.
        //    프리팹에서 세팅한 자세 그대로 두고,
        //    RotatingBrush 가 localAxis 기준으로만 회전시키도록 둠.
    }

}
