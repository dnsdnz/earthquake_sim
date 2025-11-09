using DilmerGames.Core.Singletons;
using Unity.Netcode;
using UnityEngine;

public class SpawnerControl : NetworkSingleton<SpawnerControl>
{
    [SerializeField]
    private GameObject objectPrefab;

    [SerializeField]
    private int maxObjectInstanceCount = 3;

    [Header("Diagnostics / Toggle")]
    [SerializeField]
    private bool enablePhysicsObjects = false;

    private void Awake()
    {
        if (enablePhysicsObjects && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += () =>
            {
                NetworkObjectPool.Instance.InitializePool();
            };
        }
    }

    public void SpawnObjects()
    {
        if (!enablePhysicsObjects) return;
        if (!IsServer) return;

        for (int i = 0; i < maxObjectInstanceCount; i++)
        {
            //GameObject go = Instantiate(objectPrefab, 
            //    new Vector3(Random.Range(-10, 10), 10.0f, Random.Range(-10, 10)), Quaternion.identity);
            GameObject go = NetworkObjectPool.Instance.GetNetworkObject(objectPrefab).gameObject;
            go.transform.position = new Vector3(Random.Range(-10, 10), 10.0f, Random.Range(-10, 10));
            go.GetComponent<NetworkObject>().Spawn();
        }
    }
}

