using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class Marble : MonoBehaviour
{
    public int laneIndex;
    public float forwardAcceleration = 15f;  // 기본 전방 가속
    public float minSpeed = 1.5f;            // 이 속도보다 느려지면 '끼인' 상태로 판단
    public float stuckNudgeStrength = 4f;    // 끼였을 때 앞으로 밀어주는 힘
    public float sideNudgeStrength = 1f;     // 끼였을 때 좌우로 살짝 밀어주는 힘
    public float maxHeight = 3f;   // ★ 이 높이 이상으로는 못 올라가게

    private Rigidbody rb;
    private float lowSpeedTimer = 0f;

    private MarbleRaceManager manager;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        manager = MarbleRaceManager.Instance ?? FindObjectOfType<MarbleRaceManager>();
    }

    private void FixedUpdate()
    {
        // 1. 트랙 곡선 진행 방향 구하기 (없으면 기본 z+)
        Vector3 forwardDir = Vector3.forward;
        if (manager != null)
            forwardDir = manager.GetTrackForwardDirection(rb.position);

        // 2. 곡선을 따라 앞으로 계속 밀어주는 힘
        rb.AddForce(forwardDir * forwardAcceleration, ForceMode.Acceleration);

        // 3. 너무 느리면 "끼인" 걸로 보고 탈출용 힘 추가
        float speed = rb.velocity.magnitude;
        if (speed < minSpeed)
        {
            lowSpeedTimer += Time.fixedDeltaTime;
            if (lowSpeedTimer > 0.5f)
            {
                // 진행 방향으로 강하게 한 번 밀어주고
                Vector3 forwardNudge = forwardDir * stuckNudgeStrength;

                // 진행 방향 기준 좌/우 중 하나로 살짝 튕기기
                float sideSign = Random.value < 0.5f ? -1f : 1f;
                Vector3 sideDirVec = Vector3.Cross(Vector3.up, forwardDir).normalized * sideSign;
                Vector3 sideNudge = sideDirVec * sideNudgeStrength;

                rb.AddForce(forwardNudge + sideNudge, ForceMode.VelocityChange);
                lowSpeedTimer = 0f;
            }
        }
        else
        {
            lowSpeedTimer = 0f;
        }

        // 4. 너무 높이 튀는 것 방지 (최대 높이 클램프)
        Vector3 pos = rb.position;
        if (pos.y > maxHeight)
        {
            pos.y = maxHeight;
            rb.position = pos;

            Vector3 v = rb.velocity;
            if (v.y > 0f)
                v.y = 0f;
            rb.velocity = v;
        }
    }

}