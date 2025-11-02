using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class GlobalAnnouncementManager : NetworkBehaviour
{
    [SerializeField] private KeyCode triggerKey = KeyCode.X;
    [SerializeField] private float delaySeconds = 10f;
    [SerializeField] private string announcementText = "Deprem başladı!";
    [SerializeField] private float showSeconds = 5f;

    private bool _pending;

    private void Update()
    {
        if (!IsServer) return; // host/server only listens for key
        if (_pending) return;

        if (Input.GetKeyDown(triggerKey))
        {
            _pending = true;
            StartCoroutine(DelayAndAnnounce());
        }
    }

    private IEnumerator DelayAndAnnounce()
    {
        yield return new WaitForSeconds(delaySeconds);
        ShowAnnouncementClientRpc(announcementText, showSeconds);
        _pending = false;
    }

    [ClientRpc]
    private void ShowAnnouncementClientRpc(string text, float seconds)
    {
        if (AnnouncementUI.Instance != null)
        {
            AnnouncementUI.Instance.Show(text, seconds);
        }

        // Trigger a one-shot quake on the local player's camera when the message appears
        TryTriggerLocalQuake();
    }

    private void TryTriggerLocalQuake()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return;
        var playerGO = nm.LocalClient.PlayerObject.gameObject;
        var fps = playerGO.GetComponent<FPSController>();
        if (fps == null)
        {
            fps = playerGO.GetComponentInChildren<FPSController>();
        }
        if (fps != null)
        {
            fps.StartQuake(fps.quakeDuration);
        }
    }
}
