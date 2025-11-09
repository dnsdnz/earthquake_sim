using Unity.Netcode;
using UnityEngine;

public class CollisionWarningManager : NetworkBehaviour
{
    public static CollisionWarningManager Instance { get; private set; }

    private float _lastShown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        void SpawnIfServer()
        {
            if (nm.IsServer && Instance == null)
            {
                var go = new GameObject("__CollisionWarningManager");
                Object.DontDestroyOnLoad(go);
                var no = go.AddComponent<NetworkObject>();
                var mgr = go.AddComponent<CollisionWarningManager>();
                no.Spawn(true);
                Instance = mgr;
            }
        }

        SpawnIfServer();
        nm.OnServerStarted += SpawnIfServer;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Instance = this;
    }

    public static void ReportCollisionLocal(ulong otherClientId)
    {
        if (Instance != null)
        {
            Instance.ReportCollisionServerRpc(otherClientId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportCollisionServerRpc(ulong otherClientId, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        var targets = new System.Collections.Generic.List<ulong>();
        targets.Add(sender);
        if (otherClientId != sender)
            targets.Add(otherClientId);

        var sendParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = targets.ToArray() }
        };
        ShowCollisionWarningClientRpc(sendParams);
    }

    [ClientRpc]
    private void ShowCollisionWarningClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (AnnouncementUI.Instance == null) return;
        if (Time.time - _lastShown < 1.5f) return;
        AnnouncementUI.Instance.Show("Birbirinize çarpmamaya dikkat edin.", 2.0f);
        _lastShown = Time.time;
    }
}

