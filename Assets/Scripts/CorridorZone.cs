using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider))]
public class CorridorZone : MonoBehaviour
{
    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        var no = other.GetComponentInParent<NetworkObject>();
        if (no == null) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.LocalClient == null) return;
        if (no.OwnerClientId != nm.LocalClientId) return;

        DropCoverManager.ReportCorridorLocal();
    }
}

