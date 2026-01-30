using UnityEngine;

/// <summary>
/// 플레이어 구슬의 "이동 방향" 기준으로
/// 항상 뒤에서 따라오는 카메라
/// (회전/스핀에 영향받지 않음)
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [Header("추적 대상")]
    public Transform target;

    [Header("거리 / 높이")]
    public float distance = 8f;
    public float height = 4f;

    [Header("부드러움")]
    public float positionSmooth = 6f;
    public float rotationSmooth = 8f;

    private Rigidbody targetRb;
    private Vector3 lastMoveDir = Vector3.forward;

    private void LateUpdate()
    {
        if (target == null)
            return;

        if (targetRb == null)
            targetRb = target.GetComponent<Rigidbody>();

        // ───── 이동 방향 계산 (velocity 기반) ─────
        Vector3 moveDir = lastMoveDir;

        if (targetRb != null)
        {
            Vector3 vel = targetRb.velocity;
            vel.y = 0f;

            if (vel.sqrMagnitude > 0.01f)
            {
                moveDir = vel.normalized;
                lastMoveDir = moveDir;
            }
        }

        // ───── 카메라 위치 계산 (항상 뒤에서) ─────
        Vector3 desiredPos =
            target.position
            - moveDir * distance
            + Vector3.up * height;

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPos,
            Time.deltaTime * positionSmooth
        );

        // ───── 카메라 회전 (항상 구슬을 바라봄) ─────
        Quaternion desiredRot = Quaternion.LookRotation(
            target.position - transform.position,
            Vector3.up
        );

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRot,
            Time.deltaTime * rotationSmooth
        );
    }
}
