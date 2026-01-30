using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class Marble : MonoBehaviour
{
    public int laneIndex;

    [Header("트랙 이탈 / 점프 방지")]
    [Tooltip("이 높이 이상으로 올라가면 위로 튀는 힘을 잘라버림")]
    public float maxHeight = 2f;

    [Header("벽 / 구슬 충돌 연출")]
    [Tooltip("옆 벽에 부딪혔을 때 안쪽으로 튕겨주는 힘 (VelocityChange)")]
    public float wallBounceStrength = 0f;

    [Tooltip("뒤에서 박았을 때 앞에 있는 구슬에 줄 부스트 크기")]
    public float rearHitBoost = 1f;

    [Tooltip("앞 구슬을 위/옆으로 살짝 튕겨줄 때의 힘")]
    public float rearHitSideBounce = 1f;

    [Header("Vertical Velocity Clamp")]
    [Tooltip("위로 튀는 최대 속도")]
    public float maxUpwardVelocity = 0.1f;

    [Header("멈춤 방지 (경사 어시스트)")]
    [Tooltip("이 속도보다 느릴 때만 경사 어시스트를 건다")]
    public float minMoveSpeed = 0.5f;

    [Tooltip("경사 방향으로 추가로 밀어주는 힘의 세기")]
    public float slopeAssistStrength = 20f;

    [Tooltip("이 값보다 작을 때만 '경사가 있다'고 보고 어시스트를 넣음 (1이면 완전 평지)")]
    [Range(0.0f, 1.0f)]
    public float minSlopeNormalY = 0.98f;

    [Header("트랙 이탈 판정 / 리스폰")]
    [Tooltip("트랙 중심선에서 이 거리 이상 벗어나면 트랙 이탈로 판정")]
    public float outOfTrackDistance = 50f;

    [Tooltip("리스폰 후 이 시간(초) 동안은 다시 리스폰하지 않음")]
    public float respawnCooldown = 2.0f;

    // 마지막으로 샘플링된 경로 인덱스
    private int lastClosestPathIndex = -1;

    // 마지막으로 '통과 완료한' 타일의 exit 지점에 해당하는 경로 인덱스
    private int lastExitPathIndex = -1;
    private bool hasLastExitPathIndex = false;

    // 리스폰 쿨타임용
    private float lastRespawnTime = -999f;


    private float lastFloorY;
    private bool hasLastFloorY = false;

    private Rigidbody rb;
    private MarbleRaceManager manager;

    public Rigidbody Rb => rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        manager = MarbleRaceManager.Instance ?? FindObjectOfType<MarbleRaceManager>();
    }

    void FixedUpdate()
    {
        // 1) 추가 중력 (이미 쓰던 값)
        float extraGravityMultiplier = 5.0f;
        Vector3 extraGravity =
            (extraGravityMultiplier - 1f) * Physics.gravity;
        rb.AddForce(extraGravity, ForceMode.Acceleration);

        // 2) 위로 튀는 속도 제한
        Vector3 v = rb.velocity;
        if (v.y > maxUpwardVelocity)
        {
            v.y = maxUpwardVelocity;
            rb.velocity = v;
        }

        // 3) 경사 어시스트 – 거의 멈췄을 때만 바닥 경사 방향으로 살짝 밀어줌
        AssistOnSlopeIfStuck();

        // 4) 트랙 이탈 감지 및 마지막 타일 exit 지점으로 리스폰
        HandleOutOfTrackAndRespawn();
    }

    /// <summary>
    /// 트랙 밖으로 나갔는지 판단하고,
    /// 나갔다면 "마지막으로 지나온 트랙 타일의 exit 지점"에서
    /// 같은 레인 위치로 리스폰한다.
    /// </summary>
    private void HandleOutOfTrackAndRespawn()
    {
        if (manager == null)
        {
            manager = MarbleRaceManager.Instance ?? FindObjectOfType<MarbleRaceManager>();
            if (manager == null)
                return;
        }

        if (manager.PathPointCount <= 0 || manager.LaneCount <= 0)
            return;

        // ───────── 1) 현재 경로 인덱스 계산 및 "마지막으로 지난 타일 exit 인덱스" 갱신 ─────────
        int closestIndex = manager.GetClosestPathIndex(transform.position);
        if (closestIndex < 0)
            closestIndex = 0;

        int samples = Mathf.Max(2, manager.samplesPerTile);

        if (lastClosestPathIndex < 0)
        {
            // 첫 프레임이면 그냥 현재 인덱스를 기록만
            lastClosestPathIndex = closestIndex;
        }
        else
        {
            int prevTileIndex = lastClosestPathIndex / samples;
            int currTileIndex = closestIndex / samples;

            // 타일 경계 (exit)를 넘어 다음 타일로 진입했을 때
            if (currTileIndex > prevTileIndex)
            {
                int exitedTileIndex = currTileIndex - 1;
                int exitIndex = (exitedTileIndex + 1) * samples - 1;

                exitIndex = Mathf.Clamp(exitIndex, 0, manager.PathPointCount - 1);

                lastExitPathIndex = exitIndex;
                hasLastExitPathIndex = true;
            }

            lastClosestPathIndex = closestIndex;
        }

        // ───────── 2) 트랙 이탈 여부 판정 (중심선으로부터의 수평 거리) ─────────
        Vector3 nearest = manager.GetNearestPathPoint(transform.position);

        Vector2 pos2 = new Vector2(transform.position.x, transform.position.z);
        Vector2 nearest2 = new Vector2(nearest.x, nearest.z);
        float horizontalDist = Vector2.Distance(pos2, nearest2);

        // 아직 트랙 안이라고 판단되면 리턴
        if (horizontalDist <= outOfTrackDistance)
            return;

        // 너무 자주 리스폰되는 것 방지
        if (Time.time - lastRespawnTime < respawnCooldown)
            return;

        // ───────── 3) 리스폰 위치 계산 ─────────
        int respawnIndex = hasLastExitPathIndex ? lastExitPathIndex : closestIndex;
        respawnIndex = Mathf.Clamp(respawnIndex, 0, manager.PathPointCount - 1);

        // 타일 exit 중심점
        Vector3 basePos = manager.GetPathPointByIndex(respawnIndex);

        // 진행 방향 → 오른쪽 벡터 계산
        Vector3 forward = manager.GetForwardByPathIndex(respawnIndex, 1);
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        // laneIndex에 따라 레인 오프셋 계산
        float totalWidth = manager.laneWidth * manager.LaneCount;
        float leftMost = -totalWidth * 0.5f + manager.laneWidth * 0.5f;
        float laneOffset = leftMost + laneIndex * manager.laneWidth;

        // 최종 리스폰 위치 = 해당 타일 exit 중심 + 레인 오프셋 + 살짝 위로
        Vector3 newPos =
            basePos
            + right * laneOffset
            + Vector3.up * manager.marbleStartHeight;

        transform.position = newPos;

        // 속도는 진행 방향으로만 남기고, 너무 세게 튀지 않게 조정
        float speed = rb.velocity.magnitude;
        rb.velocity = forward.normalized * speed * 0.5f;

        lastRespawnTime = Time.time;
    }


    /// <summary>
    /// 속도가 거의 0 근처이고, 바닥이 평지가 아니면
    /// 바닥 노멀을 기준으로 "아래쪽(경사 방향)"으로 힘을 조금 넣어서
    /// 경사면에서 멈춰버리는 걸 방지.
    /// </summary>
    private void AssistOnSlopeIfStuck()
    {
        // 충분히 빨리 움직이면 아무것도 안 함
        if (rb.velocity.sqrMagnitude > minMoveSpeed * minMoveSpeed)
            return;

        // 아래로 레이캐스트해서 바닥 노멀을 구한다
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore))
            return;

        Vector3 normal = hit.normal;

        // 거의 평지면(거의 위쪽을 바라보면) 그냥 멈추게 둔다
        if (normal.y >= minSlopeNormalY)
            return;

        // 중력(Vector3.down)을 바닥 평면에 투영해서 "실제 내려가는 방향" 구하기
        Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, normal);
        float mag = downhill.magnitude;
        if (mag < 0.0001f)
            return;

        downhill /= mag;

        // 경사 방향으로 가속도 추가
        rb.AddForce(downhill * slopeAssistStrength, ForceMode.Acceleration);
    }

    private void OnCollisionEnter(Collision collision)
    {
        // 1) 다른 구슬이면 → 구슬-구슬 처리
        var otherMarble = collision.collider.GetComponent<Marble>();
        if (otherMarble != null && otherMarble != this)
        {
            HandleMarbleToMarbleHit(otherMarble);
            return;
        }

        // 2) 그 외(트랙, 장애물)는 “벽일 수도 있음”
        HandleWallHit(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        foreach (var contact in collision.contacts)
        {
            // 거의 위쪽(바닥)에서 오는 노멀만 바닥으로 인정
            if (contact.normal.y > 0.7f)
            {
                float y = contact.point.y;

                // 여러 바닥과 닿을 수 있으니, 가장 높은 바닥 기준으로 잡는다.
                if (!hasLastFloorY || y > lastFloorY)
                {
                    lastFloorY = y;
                    hasLastFloorY = true;
                }
            }
        }
    }

    // ───────────────── 벽에 부딪힐 때 : 옆면일 때만 안쪽으로 튕기기 ─────────────────
    private void HandleWallHit(Collision collision)
    {
        // 여러 접점의 평균 노멀
        Vector3 avgNormal = Vector3.zero;
        foreach (var c in collision.contacts)
            avgNormal += c.normal;

        if (avgNormal == Vector3.zero)
            return;

        avgNormal.Normalize();

        // 위쪽을 많이 향하면 바닥/경사로 간주 → 무시
        if (avgNormal.y > 0.5f)
            return;

        // 수평 성분이 충분히 커야 “옆 벽”으로 인정
        Vector3 horizontalNormal = new Vector3(avgNormal.x, 0f, avgNormal.z);
        float horizMag = horizontalNormal.magnitude;
        if (horizMag < 0.4f)
            return;        // 거의 바닥이거나 모서리 → 벽 튕김 안함

        horizontalNormal /= horizMag;

        // 현재 속도 중 바깥쪽(벽 밖)으로 나가려는 성분
        float outwardSpeed = Vector3.Dot(rb.velocity, -horizontalNormal);
        if (outwardSpeed <= 0f)
            return;        // 이미 안쪽으로 가거나 평행 이동

        // 1) 바깥쪽 속도 제거
        rb.velocity -= (-horizontalNormal * outwardSpeed);

        // 2) 안쪽으로 VelocityChange 튕김
        rb.AddForce(horizontalNormal * wallBounceStrength, ForceMode.VelocityChange);

        // 3) 위로 너무 튀는 건 살짝 줄임
        if (rb.velocity.y > 0f)
        {
            rb.velocity = new Vector3(
                rb.velocity.x,
                rb.velocity.y * 0.4f,
                rb.velocity.z
            );
        }
    }

    // ───────────────── 구슬-구슬 충돌 : 앞에 있는 애만 부스트 ─────────────────
    private void HandleMarbleToMarbleHit(Marble other)
    {
        // 두 구슬 중 더 앞에 있는 애 = 충돌선 방향 기준으로 앞에 있는 애
        Vector3 toOther = other.transform.position - transform.position;

        Marble front;
        Marble rear;

        if (toOther.z > 0f)   // 대략 +Z 방향을 "앞"으로 본다 (원하는 축으로 바꿔도 됨)
        {
            front = other;
            rear = this;
        }
        else
        {
            front = this;
            rear = other;
        }

        Rigidbody frontRb = front.Rb;
        Rigidbody rearRb = rear.Rb;

        // 1) 충돌선 방향 기준으로 앞쪽으로 살짝 부스트
        Vector3 dir = (front.transform.position - rear.transform.position).normalized;
        frontRb.AddForce(dir * rearHitBoost, ForceMode.VelocityChange);

        // 2) 위/옆으로 살짝 튕김
        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
        float sideSign = Random.value < 0.5f ? -1f : 1f;
        Vector3 bounceDir = (Vector3.up * 0.3f + side * sideSign).normalized;

        frontRb.AddForce(bounceDir * rearHitSideBounce, ForceMode.VelocityChange);

        // 3) 뒤에서 박은 애는 살짝 감속
        rearRb.velocity *= 0.9f;
    }
}
