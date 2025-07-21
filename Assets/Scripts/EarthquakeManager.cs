using Unity.Netcode;
using UnityEngine;

public class EarthquakeManager : NetworkBehaviour
{
    public float quakeDuration = 5f;
    private bool isQuaking = false;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            Invoke(nameof(StartQuake), 3f); // başlatmadan 3 saniye sonra
    }

    void StartQuake()
    {
        isQuaking = true;
        Invoke(nameof(EndQuake), quakeDuration);
        Debug.Log("Deprem başladı!");
        // Oyunculara RPC ile deprem etkisi verilebilir
    }

    void EndQuake()
    {
        isQuaking = false;
        Debug.Log("Deprem bitti!");
    }
}