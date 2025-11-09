using UnityEngine;
using Unity.Netcode;

// Local-only proximity scanner to show warnings reliably on client.
// Configure tags and radii for hazards.
public class PlayerProximityScanner : NetworkBehaviour
{
    [Header("Glass Warning")] public string glassTag = "GlassWarning"; public float glassRadius = 1.2f; public string glassText = "Cam'a yaklasma!";
    [Header("Cabinet Warning")] public string cabinetTag = "CabinetWarning"; public float cabinetRadius = 1.2f; public string cabinetText = "Dolaplarin yanina yaklasma!";
    [Header("Cooldown")] public float rewarnCooldown = 2.0f;

    private float _lastGlass; private float _lastCabinet;

    private void Update()
    {
        if (!IsOwner) return; // only local player shows UI
        if (AnnouncementUI.Instance == null) return;

        var p = transform.position + Vector3.up * 0.5f;
        // Glass
        if (Time.time - _lastGlass >= rewarnCooldown && IsAnyWithTagWithin(glassTag, p, glassRadius))
        {
            AnnouncementUI.Instance.Show(glassText, 1.6f);
            _lastGlass = Time.time;
        }
        // Cabinet
        if (Time.time - _lastCabinet >= rewarnCooldown && IsAnyWithTagWithin(cabinetTag, p, cabinetRadius))
        {
            AnnouncementUI.Instance.Show(cabinetText, 1.6f);
            _lastCabinet = Time.time;
        }
    }

    private bool IsAnyWithTagWithin(string tag, Vector3 pos, float radius)
    {
        if (string.IsNullOrEmpty(tag)) return false;
        var gos = GameObject.FindGameObjectsWithTag(tag);
        for (int i = 0; i < gos.Length; i++)
        {
            var go = gos[i]; if (!go || !go.activeInHierarchy) continue;
            if ((go.transform.position - pos).sqrMagnitude <= radius * radius) return true;
        }
        return false;
    }
}

