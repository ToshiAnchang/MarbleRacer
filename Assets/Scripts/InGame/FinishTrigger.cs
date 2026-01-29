using UnityEngine;

public class FinishTrigger : MonoBehaviour
{
    public MarbleRaceManager manager;

    private void OnTriggerEnter(Collider other)
    {
        var marble = other.GetComponent<Marble>();
        if (marble != null && manager != null)
        {
            manager.OnMarbleFinished(marble);
            Debug.Log($"[Finish] {marble.gameObject.name} 골인!");
        }
    }
}
