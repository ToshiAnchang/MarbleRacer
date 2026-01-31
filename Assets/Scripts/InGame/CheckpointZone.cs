using UnityEngine;

/// <summary>
/// 트랙 위 체크포인트 존.
/// PlayerMarble이 통과하면 이후 리스폰 시 이 위치를 기준으로 복귀.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class CheckpointZone : MonoBehaviour
{
    [Tooltip("체크포인트 순서 인덱스(0부터). 필요 없으면 안 써도 됩니다.")]
    public int checkpointIndex = 0;

    [Tooltip("리스폰 시 트랙 바닥보다 얼마나 위에 위치할지")]
    public float respawnHeightOffset = 0.8f;

    [Tooltip("리스폰 시 진행 방향으로 얼마나 앞에 위치할지")]
    public float respawnForwardOffset = 1.0f;

    private void Reset()
    {
        var col = GetComponent<BoxCollider>();
        col.isTrigger = true;
    }

    /// <summary>
    /// 리스폰 시 사용할 위치/회전 반환
    /// </summary>
    public void GetRespawnTransform(out Vector3 pos, out Quaternion rot)
    {
        Transform t = transform;
        Vector3 forward = t.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        pos = t.position
              + Vector3.up * respawnHeightOffset
              + forward * respawnForwardOffset;

        rot = Quaternion.LookRotation(forward, Vector3.up);
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerMarble pm = other.GetComponent<PlayerMarble>();
        if (pm != null)
        {
            pm.SetCheckpoint(this);
        }
    }
}
