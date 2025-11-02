using UnityEngine;
using Unity.Netcode;

public class GlobalAnnouncementBootstrap : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var go = new GameObject("__GlobalAnnouncementBootstrap");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<GlobalAnnouncementBootstrap>();
    }

    private void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
            if (NetworkManager.Singleton.IsServer)
            {
                OnServerStarted();
            }
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        }
    }

    private void OnServerStarted()
    {
        // Create and spawn the networked announcement manager on server
        var existing = FindObjectOfType<GlobalAnnouncementManager>();
        if (existing != null && existing.IsSpawned) return;

        var go = new GameObject("GlobalAnnouncementManager");
        Object.DontDestroyOnLoad(go);
        var no = go.AddComponent<NetworkObject>();
        var mgr = go.AddComponent<GlobalAnnouncementManager>();
        no.Spawn();
    }
}
