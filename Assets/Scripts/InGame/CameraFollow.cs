using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;

    [Header("Camera Offset")]
    public float height = 10f;        // 위로 얼마나 띄울지
    public float distance = 14f;      // 뒤로 얼마나 뺄지
    public float lookAhead = 8f;      // 앞을 얼마나 바라볼지

    [Header("Smooth")]
    public float positionSmooth = 6f;
    public float rotationSmooth = 8f;

    private Rigidbody targetRb;
    private Vector3 currentForward = Vector3.forward;


    private MarbleRaceManager manager;

    private void Start()
    {
        manager = MarbleRaceManager.Instance ?? FindObjectOfType<MarbleRaceManager>();

        if (target != null)
        {
            targetRb = target.GetComponent<Rigidbody>();
        }
    }


    private void LateUpdate()
    {
        if (target == null)
            return;

        if (targetRb == null)
        {
            targetRb = target.GetComponent<Rigidbody>();
        }

        Vector3 targetPos = target.position;

        // 1. 구슬 속도로부터 "앞 방향" 계산 (트랙 방향 X)
        Vector3 vel = targetRb != null ? targetRb.velocity : Vector3.zero;

        // 수평 속도만 사용 (위/아래 성분 제거)
        Vector3 horizVel = new Vector3(vel.x, 0f, vel.z);

        Vector3 desiredForward = currentForward;

        if (horizVel.sqrMagnitude > 0.01f)
        {
            desiredForward = horizVel.normalized;
        }

        // 속도가 거의 없을 때는 이전 방향을 유지
        if (desiredForward.sqrMagnitude < 0.0001f)
        {
            desiredForward = currentForward.sqrMagnitude > 0.0001f
                ? currentForward
                : Vector3.forward;
        }

        // 부드럽게 방향 보간 (카메라 요요 방지)
        currentForward = Vector3.Slerp(
            currentForward,
            desiredForward,
            Time.deltaTime * rotationSmooth
        );

        currentForward.y = 0f;
        if (currentForward.sqrMagnitude < 0.0001f)
            currentForward = Vector3.forward;

        // 2. 카메라 목표 위치 (구슬 뒤 + 위)
        Vector3 desiredPos =
            targetPos
            - currentForward * distance   // 뒤로
            + Vector3.up * height;        // 위로

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPos,
            Time.deltaTime * positionSmooth
        );

        // 3. 바라볼 위치 (구슬보다 약간 앞)
        Vector3 lookTarget =
            targetPos
            + currentForward * lookAhead;

        Vector3 lookDir = (lookTarget - transform.position);
        if (lookDir.sqrMagnitude < 0.0001f)
            lookDir = currentForward;

        Quaternion desiredRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRot,
            Time.deltaTime * rotationSmooth
        );
    }

}
