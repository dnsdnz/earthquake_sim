using Unity.Netcode;
using UnityEngine;

// Attach this to the Player prefab to ensure server places it at a spawn point on spawn.
public class ServerApplySpawn : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        var mgr = Object.FindFirstObjectByType<PlayerSpawnManager>();
        if (mgr != null)
        {
            mgr.PlacePlayerObject(GetComponent<NetworkObject>());
        }
    }
}

