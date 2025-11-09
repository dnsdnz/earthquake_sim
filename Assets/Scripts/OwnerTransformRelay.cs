using Unity.Netcode;
using UnityEngine;

// Server-authoritative transform relay: owner sends pose to server via RPC,
// server applies to transform, NetworkTransform (server-authoritative) replicates to others.
// Use this if ClientNetworkTransform causes issues.
[RequireComponent(typeof(NetworkObject))]
public class OwnerTransformRelay : NetworkBehaviour
{
    [Header("Update Thresholds")]
    public float positionThreshold = 0.001f;   // meters (tighter for smoother motion)
    public float rotationThreshold = 0.2f;     // degrees
    public float maxSendRate = 60f;            // Hz

    private Vector3 _lastSentPos;
    private Quaternion _lastSentRot;
    private float _nextSend;

    public override void OnNetworkSpawn()
    {
        _lastSentPos = transform.position;
        _lastSentRot = transform.rotation;
        _nextSend = 0f;
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (Time.time < _nextSend) return;

        var pos = transform.position;
        var rot = transform.rotation;
        if ((pos - _lastSentPos).sqrMagnitude >= positionThreshold * positionThreshold ||
            Quaternion.Angle(rot, _lastSentRot) >= rotationThreshold)
        {
            _lastSentPos = pos;
            _lastSentRot = rot;
            SendPoseServerRpc(pos, rot);
            _nextSend = Time.time + (1f / Mathf.Max(1f, maxSendRate));
        }
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    private void SendPoseServerRpc(Vector3 pos, Quaternion rot)
    {
        // Apply on server; NetworkTransform (server-authoritative) will replicate to all
        var cc = GetComponent<CharacterController>();
        if (cc) cc.enabled = false;
        transform.SetPositionAndRotation(pos, rot);
        if (cc) cc.enabled = true;

        // Broadcast to non-owner clients (custom replication to avoid NetworkTransform usage)
        if (NetworkManager.Singleton != null)
        {
            var targets = new System.Collections.Generic.List<ulong>();
            foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (id == OwnerClientId) continue;
                targets.Add(id);
            }
            if (targets.Count > 0)
            {
                var sendParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = targets.ToArray() }
                };
                ApplyPoseClientRpc(pos, rot, sendParams);
            }
        }
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void ApplyPoseClientRpc(Vector3 pos, Quaternion rot, ClientRpcParams clientRpcParams = default)
    {
        if (IsOwner) return; // owner drives locally
        var cc = GetComponent<CharacterController>();
        if (cc) cc.enabled = false;
        transform.SetPositionAndRotation(pos, rot);
        if (cc) cc.enabled = true;
    }
}
