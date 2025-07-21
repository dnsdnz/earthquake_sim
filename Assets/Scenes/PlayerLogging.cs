using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerLogging : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            Debug.Log($"{OwnerClientId} bağlandı - Zaman: {Time.time}");
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        // Örnek log: Tahliye süresi, yön değişikliği, güvenli bölgeye ulaştı mı vs.
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Debug.Log($"{OwnerClientId} zıpladı - Zaman: {Time.time}");
        }
    }
}