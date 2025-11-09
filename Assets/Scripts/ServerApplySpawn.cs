using Unity.Netcode;
using UnityEngine;

// Attach this to the Player prefab to ensure server places it at a spawn point on spawn.
public class ServerApplySpawn : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        var no = GetComponent<NetworkObject>();
        var mgr = Object.FindFirstObjectByType<PlayerSpawnManager>();
        if (mgr != null)
        {
            mgr.PlacePlayerObject(no);
            // Ensure owner-authoritative clients also get positioned locally
            var pos = no.transform.position; var rot = no.transform.rotation;
            var sendParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { no.OwnerClientId } } };
            TeleportOwnerClientRpc(pos, rot, sendParams);
            return;
        }

        // Fallback: place by scene markers named/tagged SpawnPoint_* or DropCoverSpot_*
        TryPlaceBySceneMarkers(no);
    }

    private void TryPlaceBySceneMarkers(NetworkObject playerNo)
    {
        if (playerNo == null) return;
        // Collect candidate transforms in priority order: tag SpawnPoint, name SpawnPoint_#, then DropCoverSpot_#
        System.Collections.Generic.List<Transform> list = new System.Collections.Generic.List<Transform>();
        // By tag
        var tagged = GameObject.FindGameObjectsWithTag("SpawnPoint");
        if (tagged != null && tagged.Length > 0)
        {
            list.AddRange(System.Array.ConvertAll(tagged, go => go.transform));
        }
        // By name prefix SpawnPoint_
        foreach (var t in Object.FindObjectsOfType<Transform>())
        {
            if (t.name.StartsWith("SpawnPoint_")) list.Add(t);
        }
        // If still empty, fall back to DropCoverSpot order
        if (list.Count == 0)
        {
            foreach (var t in Object.FindObjectsOfType<Transform>())
            {
                if (t.CompareTag("DropCoverSpot") || t.name.StartsWith("DropCoverSpot_")) list.Add(t);
            }
        }
        if (list.Count == 0)
        {
            // Final fallback: circle
            float angle = (playerNo.OwnerClientId % 16) * Mathf.PI * 2f / 16f;
            Vector3 pos = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 3f;
            var cc = playerNo.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            playerNo.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, -Mathf.Rad2Deg * angle, 0f));
            if (cc != null) cc.enabled = true;
            return;
        }

        // Order by numeric suffix if present
        int ExtractNumericSuffix(string name)
        {
            int val = 0; int mul = 1; bool found = false;
            for (int i = name.Length - 1; i >= 0; i--)
            {
                char c = name[i];
                if (c >= '0' && c <= '9') { found = true; val = (c - '0') * mul + val; mul *= 10; }
                else if (found) break;
            }
            return found ? val : int.MaxValue;
        }
        list.Sort((a, b) =>
        {
            int na = ExtractNumericSuffix(a.name);
            int nb = ExtractNumericSuffix(b.name);
            int cmp = na.CompareTo(nb);
            if (cmp != 0) return cmp;
            return string.CompareOrdinal(a.name, b.name);
        });

        int joinIndex = GetJoinIndex(playerNo.OwnerClientId);
        var chosen = list[joinIndex % list.Count];
        if (chosen != null)
        {
            var cc = playerNo.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            playerNo.transform.SetPositionAndRotation(chosen.position, chosen.rotation);
            if (cc != null) cc.enabled = true;
            // Also set on owner client (client-authoritative transforms)
            var sendParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { playerNo.OwnerClientId } } };
            TeleportOwnerClientRpc(chosen.position, chosen.rotation, sendParams);
        }
    }

    private int GetJoinIndex(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return 0;
        if (clientId == NetworkManager.ServerClientId) return 0;
        var ids = new System.Collections.Generic.List<ulong>(nm.ConnectedClientsIds);
        ids.Sort();
        int rank = 0;
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == NetworkManager.ServerClientId) continue;
            if (ids[i] == clientId) return rank + 1;
            rank++;
        }
        return 0;
    }

    [ClientRpc]
    private void TeleportOwnerClientRpc(Vector3 pos, Quaternion rot, ClientRpcParams clientRpcParams = default)
    {
        var no = GetComponent<NetworkObject>();
        if (no == null) return;
        var isLocal = NetworkManager.Singleton != null && no.IsOwner;
        if (!isLocal) return;
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.SetPositionAndRotation(pos, rot);
        if (cc != null) cc.enabled = true;
    }
}
