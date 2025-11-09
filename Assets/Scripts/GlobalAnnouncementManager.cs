using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class GlobalAnnouncementManager : NetworkBehaviour
{
    [SerializeField] private KeyCode triggerKey = KeyCode.M;
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
            // Start drop-cover flow: lock and prompt immediately
            if (DropCoverManager.Instance != null)
            {
                DropCoverManager.Instance.TriggerDropCoverSequence();
            }
            else
            {
                // Fallback: lock and prompt directly
                SetMovementLockClientRpc(true);
                ShowAnnouncementClientRpc("Çök kapan tutun yapmak için C'ye basın", 10f);
            }
            // Optionally still run delayed announcement if desired
            // StartCoroutine(DelayAndAnnounce());
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

        // No longer auto-unlocking here; drop-cover flow manages lock state
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

    [ClientRpc]
    private void SetMovementLockClientRpc(bool locked)
    {
        SetMovementLock(locked);
    }

    private void SetMovementLock(bool locked)
    {
        FPSController.GlobalMovementLocked = locked;
    }
}
