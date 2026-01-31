using UnityEngine;

/// <summary>
/// FinishTrigger에 붙어서, 구슬이 들어오면 RaceResultManager에 알려주는 센서.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class FinishTriggerSensor : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other == null)
            return;

        PlayerMarble pm = other.GetComponent<PlayerMarble>();
        if (pm == null)
            return;

        if (RaceResultManager.Instance == null)
        {
            Debug.LogWarning("[FinishTriggerSensor] RaceResultManager.Instance 가 없습니다.");
            return;
        }

        RaceResultManager.Instance.OnMarbleFinished(pm);
    }
}
