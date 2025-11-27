using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Attach to a scene GameObject and assign spawn points.
public class PlayerSpawnManager : MonoBehaviour
{
    public static PlayerSpawnManager Instance { get; private set; }
    [Header("Spawn Points")]
    [Tooltip("Optional root to scan children for spawn points when Auto Populate is enabled.")]
    [SerializeField] private Transform spawnRoot;

    [Tooltip("Populate spawn points from children of Spawn Root (or this GameObject) automatically.")]
    [SerializeField] private bool autoPopulateFromChildren = true;

    [Tooltip("Include inactive children while populating.")]
    [SerializeField] private bool includeInactive = true;

    [Tooltip("Ordered list of spawn locations for players.")]
    [SerializeField] private List<Transform> spawnPoints = new List<Transform>();

    [Header("Assignment Mode")]
    [Tooltip("If true, assigns spawn points in connection order (round-robin). If false, uses ClientId modulo.")]
    public bool useRoundRobin = true;

    private int _nextIndex = 0;
    private readonly System.Collections.Generic.Dictionary<ulong, int> _assigned = new System.Collections.Generic.Dictionary<ulong, int>();
    private readonly System.Collections.Generic.Dictionary<ulong, Pose> _spawnPoses = new System.Collections.Generic.Dictionary<ulong, Pose>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PlayerSpawnManager] Multiple instances detected. Latest instance will be used.");
        }
        Instance = this;
    }

    private void Reset()
    {
        spawnRoot = transform;
    }

    private void OnValidate()
    {
        if (spawnRoot == null) spawnRoot = transform;
        if (autoPopulateFromChildren)
        {
            PopulateFromChildren();
        }
    }

    [ContextMenu("Populate From Children")]
    public void PopulateFromChildren()
    {
        if (spawnRoot == null) spawnRoot = transform;
        spawnPoints.Clear();
        foreach (Transform child in spawnRoot.GetComponentsInChildren<Transform>(includeInactive))
        {
            if (child == spawnRoot) continue;
            spawnPoints.Add(child);
        }
    }

    private void OnEnable()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnServerStarted()
    {
        if (!NetworkManager.Singleton.IsServer) return;
        // Handle already-connected host player immediately (or next frame if needed)
        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            TryPlacePlayer(kvp.Key);
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        TryPlacePlayer(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        _assigned.Remove(clientId);
        _spawnPoses.Remove(clientId);
    }

    private void TryPlacePlayer(ulong clientId)
    {
        var client = NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)
            ? NetworkManager.Singleton.ConnectedClients[clientId]
            : null;
        if (client == null || client.PlayerObject == null)
        {
            // PlayerObject may not be spawned yet; try next frame
            StartCoroutine(PlaceWhenReady(clientId));
            return;
        }

        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            if (autoPopulateFromChildren) PopulateFromChildren();
        }

        var t = client.PlayerObject.transform;
        if (spawnPoints != null && spawnPoints.Count > 0)
        {
            if (_assigned.ContainsKey(clientId)) return; // already placed
            int index = SelectIndex(clientId);
            _assigned[clientId] = index;

            var sp = spawnPoints[index];
            if (sp != null)
            {
                Debug.Log($"[SpawnManager] Placing client {clientId} at spawn index {index}: {sp.name}");
                ApplyPlacement(t, sp.position, sp.rotation, clientId);
                return;
            }
        }

        // Fallback: place on a simple circle around origin based on clientId
        float angle = (clientId % 16) * Mathf.PI * 2f / 16f;
        Vector3 pos = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 3f;
        Debug.Log($"[SpawnManager] Fallback placement for client {clientId}");
        ApplyPlacement(t, pos, Quaternion.Euler(0f, -Mathf.Rad2Deg * angle, 0f), clientId);
    }

    // Public API to be called by player prefab on spawn (server-side)
    public void PlacePlayerObject(NetworkObject playerObject)
    {
        if (playerObject == null) return;
        var clientId = playerObject.OwnerClientId;
        // If we can, just place directly
        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            if (autoPopulateFromChildren) PopulateFromChildren();
        }

        var t = playerObject.transform;
        if (spawnPoints != null && spawnPoints.Count > 0)
        {
            if (_assigned.ContainsKey(clientId)) return; // already placed
            int index = SelectIndex(clientId);
            _assigned[clientId] = index;

            var sp = spawnPoints[index];
            if (sp != null)
            {
                Debug.Log($"[SpawnManager] Placing client {clientId} at spawn index {index}: {sp.name}");
                ApplyPlacement(t, sp.position, sp.rotation, clientId);
                return;
            }
        }

        // Fallback
        float angle = (clientId % 16) * Mathf.PI * 2f / 16f;
        Vector3 pos = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 3f;
        Debug.Log($"[SpawnManager] Fallback placement for client {clientId}");
        ApplyPlacement(t, pos, Quaternion.Euler(0f, -Mathf.Rad2Deg * angle, 0f), clientId);
    }

    private int SelectIndex(ulong clientId)
    {
        if (spawnPoints == null || spawnPoints.Count == 0) return 0;
        if (useRoundRobin)
        {
            int idx = _nextIndex % spawnPoints.Count;
            _nextIndex = (idx + 1) % spawnPoints.Count;
            return idx;
        }
        else
        {
            return (int)(clientId % (ulong)spawnPoints.Count);
        }
    }

    private void ApplyPlacement(Transform target, Vector3 position, Quaternion rotation, ulong clientId)
    {
        var cc = target.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        target.SetPositionAndRotation(position, rotation);
        if (cc != null) cc.enabled = true;
        _spawnPoses[clientId] = new Pose(position, rotation);
    }

    private System.Collections.IEnumerator PlaceWhenReady(ulong clientId)
    {
        // wait few frames for PlayerObject to spawn
        for (int i = 0; i < 10; i++)
        {
            yield return null;
            var client = NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)
                ? NetworkManager.Singleton.ConnectedClients[clientId]
                : null;
            if (client != null && client.PlayerObject != null)
            {
                TryPlacePlayer(clientId);
                yield break;
            }
        }
    }

    public bool TryGetSpawnLocation(ulong clientId, out Vector3 position, out Quaternion rotation)
    {
        if (_spawnPoses.TryGetValue(clientId, out var pose))
        {
            position = pose.position;
            rotation = pose.rotation;
            return true;
        }
        position = default;
        rotation = default;
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (spawnPoints == null) return;
        Gizmos.color = Color.green;
        for (int i = 0; i < spawnPoints.Count; i++)
        {
            var sp = spawnPoints[i];
            if (sp == null) continue;
            Gizmos.DrawSphere(sp.position, 0.15f);
            var dir = sp.forward * 0.5f;
            Gizmos.DrawLine(sp.position, sp.position + dir);
        }
    }
}
