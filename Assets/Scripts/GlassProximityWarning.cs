using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider))]
public class GlassProximityWarning : MonoBehaviour
{
    [Header("Warning")]
    [SerializeField] private string warningText = "Cam'a yaklasma!";
    [SerializeField] private float rewarnCooldown = 3f;

    private readonly System.Collections.Generic.Dictionary<ulong, float> _lastShownByClient = new System.Collections.Generic.Dictionary<ulong, float>();

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryWarn(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryWarn(other);
    }

    private void TryWarn(Collider other)
    {
        // Only show to the client who owns the entering player
        var no = other.GetComponentInParent<NetworkObject>();
        if (no == null) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.LocalClient == null) return;
        if (no.OwnerClientId != nm.LocalClientId) return;

        float last;
        _lastShownByClient.TryGetValue(no.OwnerClientId, out last);
        if (Time.time - last < rewarnCooldown) return;

        if (AnnouncementUI.Instance != null)
        {
            AnnouncementUI.Instance.Show(warningText, 2.0f);
            _lastShownByClient[no.OwnerClientId] = Time.time;
        }
    }
}
