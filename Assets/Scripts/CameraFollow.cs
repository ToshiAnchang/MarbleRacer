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

    private MarbleRaceManager manager;

    private void Start()
    {
        manager = MarbleRaceManager.Instance ?? FindObjectOfType<MarbleRaceManager>();
    }

    private void LateUpdate()
    {
        if (target == null || manager == null)
            return;

        Vector3 targetPos = target.position;

        // 1. 플레이어 기준 트랙 진행 방향
        Vector3 forwardDir = manager.GetTrackForwardDirection(targetPos);

        // 2. 카메라 목표 위치 (플레이어 뒤 + 위)
        Vector3 desiredPos =
            targetPos
            - forwardDir * distance
            + Vector3.up * height;

        // 3. 부드럽게 이동
        transform.position = Vector3.Lerp(
            transform.position,
            desiredPos,
            Time.deltaTime * positionSmooth
        );

        // 4. 바라볼 위치 (플레이어보다 약간 앞)
        Vector3 lookTarget =
            targetPos
            + forwardDir * lookAhead;

        Quaternion desiredRot =
            Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);

        // 5. 부드럽게 회전
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRot,
            Time.deltaTime * rotationSmooth
        );
    }
}
