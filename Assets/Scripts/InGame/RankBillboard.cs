using UnityEngine;

/// <summary>
/// 등수 텍스트가 항상 구슬 위에 떠 있고,
/// 구슬이 아무리 기울어도 수직 + 카메라를 향하도록 만드는 빌보드.
/// </summary>
public class RankBillboard : MonoBehaviour
{
    public Transform target;
    public float heightOffset = 0.6f;

    private void LateUpdate()
    {
        if (target == null)
            return;

        // 위치: 구슬 위치 + 위로 heightOffset
        transform.position = target.position + Vector3.up * heightOffset;

        // 회전: 카메라 쪽을 보되, 월드 Up 기준
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
        else
        {
            transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        }
    }
}
