using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider))]
public class GlassProximityWarning : MonoBehaviour
{
    [Header("Warning")]
    [SerializeField] private string warningText = "Cam'a yaklasma!";
    [SerializeField] private float rewarnCooldown = 3f;

    private float _lastShownTime = -999f;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryWarn(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (Time.time - _lastShownTime >= rewarnCooldown)
        {
            TryWarn(other);
        }
    }

    private void TryWarn(Collider other)
    {
        // Only show to the client who owns the entering player
        var no = other.GetComponentInParent<NetworkObject>();
        if (no == null) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.LocalClient == null) return;
        if (no.OwnerClientId != nm.LocalClientId) return;

        if (AnnouncementUI.Instance != null)
        {
            AnnouncementUI.Instance.Show(warningText, 2.0f);
            _lastShownTime = Time.time;
        }
    }
}

