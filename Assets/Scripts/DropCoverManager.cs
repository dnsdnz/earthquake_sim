using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class DropCoverManager : NetworkBehaviour
{
    public static DropCoverManager Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private string promptText = "Çök kapan tutun yapmak için C'ye basın";

    private readonly Dictionary<ulong, int> _assignedIndex = new Dictionary<ulong, int>();
    private readonly HashSet<int> _used = new HashSet<int>();
    private Transform[] _spots;
    private bool _promptActive;
    private readonly Dictionary<ulong, int> _joinIndex = new Dictionary<ulong, int>();
    private int _nextJoinIndex = 0;
    private readonly HashSet<ulong> _completed = new HashSet<ulong>();
    private Coroutine _finalPhaseCo;
    private bool _releaseActive;
    private readonly HashSet<ulong> _inCorridor = new HashSet<ulong>();
    private readonly HashSet<ulong> _exited = new HashSet<ulong>();

    [Header("Quake Settings")]
    [SerializeField] private float strongPosAmp = 0.15f;
    [SerializeField] private float strongRotAmp = 1.5f;
    [SerializeField] private float strongFreq = 12f;
    [SerializeField] private float slightPosAmp = 0.05f;
    [SerializeField] private float slightRotAmp = 0.5f;
    [SerializeField] private float slightFreq = 8f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        void SpawnIfServer()
        {
            if (!nm.IsServer || Instance != null) return;

            // Prefer spawning from a registered prefab so clients can create it too
            var prefab = Resources.Load<GameObject>("Net/DropCoverManager");
            if (prefab != null)
            {
                var inst = Object.Instantiate(prefab);
                Object.DontDestroyOnLoad(inst);
                var no = inst.GetComponent<NetworkObject>();
                var mgr = inst.GetComponent<DropCoverManager>();
                if (no == null || mgr == null)
                {
                    Debug.LogError("[DropCoverManager] Prefab 'Net/DropCoverManager' must include NetworkObject and DropCoverManager components.");
                    Object.Destroy(inst);
                    return;
                }
                no.Spawn(true);
                Instance = mgr;
            }
            else
            {
                Debug.LogError("[DropCoverManager] Missing network prefab Resources/Net/DropCoverManager. Create a prefab with NetworkObject+DropCoverManager and add it to NetworkManager.NetworkPrefabs.");
            }
        }

        // Spawn now if already server/host
        SpawnIfServer();
        // Also spawn when server starts later (e.g., StartHost)
        nm.OnServerStarted += SpawnIfServer;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Instance = this;
        if (IsServer && NetworkManager.Singleton != null)
        {
            // Deterministic join order: host/server is always index 0
            _joinIndex.Clear();
            _nextJoinIndex = 0;
            var nm = NetworkManager.Singleton;
            _joinIndex[NetworkManager.ServerClientId] = 0;
            _nextJoinIndex = 1;
            nm.OnClientConnectedCallback += OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && clientId == NetworkManager.ServerClientId) return; // host already assigned as 0
        if (!_joinIndex.ContainsKey(clientId))
        {
            _joinIndex[clientId] = _nextJoinIndex++;
        }
    }

    private void EnsureSpots()
    {
        if (_spots != null && _spots.Length > 0) return;
        var gos = GameObject.FindGameObjectsWithTag("DropCoverSpot");
        if (gos == null || gos.Length == 0)
        {
            _spots = System.Array.Empty<Transform>();
            return;
        }

        int ExtractNumericSuffix(string name)
        {
            int val = 0;
            int mul = 1;
            bool found = false;
            for (int i = name.Length - 1; i >= 0; i--)
            {
                char c = name[i];
                if (c >= '0' && c <= '9')
                {
                    found = true;
                    val = (c - '0') * mul + val;
                    mul *= 10;
                }
                else if (found)
                {
                    break; // stop at first non-digit after reading digits
                }
            }
            return found ? val : int.MaxValue; // names without numbers go last
        }

        var ordered = gos
            .OrderBy(go => ExtractNumericSuffix(go.name))
            .ThenBy(go => go.name, System.StringComparer.Ordinal)
            .Select(go => go.transform)
            .ToArray();
        _spots = ordered;
    }

    // Host/Server triggers drop-cover flow: lock movement and show prompt
    public void TriggerDropCoverSequence()
    {
        if (!IsServer)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            {
                // In case this object was created without network, allow host to proceed
            }
            else
            {
                return;
            }
        }
        _promptActive = true;
        _completed.Clear();
        _releaseActive = false;
        _inCorridor.Clear();
        _exited.Clear();
        SetMovementLockClientRpc(true);
        ShowPromptClientRpc(promptText);
        // Start strong continuous quake across clients
        SetContinuousQuakeClientRpc(true, strongPosAmp, strongRotAmp, strongFreq);
    }

    [ClientRpc]
    private void ShowPromptClientRpc(string text)
    {
        if (AnnouncementUI.Instance != null)
        {
            AnnouncementUI.Instance.Show(text, 9999f); // show effectively until flow completes
        }
    }

    [ClientRpc]
    private void SetMovementLockClientRpc(bool locked)
    {
        FPSController.GlobalMovementLocked = locked;
    }

    public static void RequestForLocal()
    {
        if (Instance != null)
        {
            Instance.RequestDropCoverServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestDropCoverServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!_promptActive) return;
        ulong sender = rpcParams.Receive.SenderClientId;
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(sender)) return;
        var playerObj = NetworkManager.Singleton.ConnectedClients[sender].PlayerObject;
        if (playerObj == null) return;

        EnsureSpots();

        Vector3 targetPos = playerObj.transform.position;
        Quaternion targetRot = playerObj.transform.rotation;
        int index;
        if (_spots != null && _spots.Length > 0)
        {
            // Determine spot by join order: first player -> index 0, etc. Wrap if more players than spots
            if (!_assignedIndex.TryGetValue(sender, out index))
            {
                int j = GetJoinIndex(sender);
                index = _spots.Length > 0 ? (j % _spots.Length) : 0;
                _assignedIndex[sender] = index;
            }
            var t = _spots[index];
            if (t != null)
            {
                targetPos = t.position;
                targetRot = t.rotation;
            }
        }

        var cc = playerObj.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        playerObj.transform.SetPositionAndRotation(targetPos, targetRot);
        if (cc != null) cc.enabled = true;

        // Ensure all clients move this player to the same position/rotation
        if (playerObj != null)
        {
            NetworkObjectReference noRef = new NetworkObjectReference(playerObj);
            WarpPlayerClientRpc(noRef, targetPos, targetRot);
        }

        // Tell that client to force crouch (and keep movement locked)
        var sendParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { sender } }
        };
        ForceCrouchClientRpc(true, sendParams);

        // Mark this client as completed
        _completed.Add(sender);
        CheckAllCompleted();
    }

    [ClientRpc]
    private void ForceCrouchClientRpc(bool crouch, ClientRpcParams clientRpcParams = default)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return;
        var playerGO = nm.LocalClient.PlayerObject.gameObject;
        var fps = playerGO.GetComponent<FPSController>();
        if (fps == null) fps = playerGO.GetComponentInChildren<FPSController>();
        if (fps != null)
        {
            fps.SetForcedCrouch(crouch);
        }

        if (AnnouncementUI.Instance != null)
        {
            AnnouncementUI.Instance.Show("", 0f);
        }
    }

    [ClientRpc]
    private void WarpPlayerClientRpc(NetworkObjectReference targetRef, Vector3 pos, Quaternion rot)
    {
        if (targetRef.TryGet(out var no))
        {
            var t = no.transform;
            var cc = no.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            t.SetPositionAndRotation(pos, rot);
            if (cc != null) cc.enabled = true;
        }
    }

    private void CheckAllCompleted()
    {
        if (!IsServer || NetworkManager.Singleton == null) return;
        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (!_completed.Contains(id)) return;
        }
        if (_finalPhaseCo == null)
        {
            _finalPhaseCo = StartCoroutine(FinalQuakePhase());
        }
    }

    private System.Collections.IEnumerator FinalQuakePhase()
    {
        // Slight quake for 5 seconds, then stop
        SetContinuousQuakeClientRpc(true, slightPosAmp, slightRotAmp, slightFreq);
        yield return new WaitForSeconds(5f);
        SetContinuousQuakeClientRpc(false, 0f, 0f, slightFreq);
        _promptActive = false;
        _releaseActive = true;
        // Unlock movement for everyone
        SetMovementLockClientRpc(false);
        // Inform everyone they can exit drop-cover with C
        ShowEndPromptClientRpc("Deprem sona erdi. C'ye basip \"Cok kapan tutun pozisyonundan\" cikis yapabilirsiniz.", 6f);
        _finalPhaseCo = null;
    }

    [ClientRpc]
    private void SetContinuousQuakeClientRpc(bool active, float posAmp, float rotAmp, float freq)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return;
        var go = nm.LocalClient.PlayerObject.gameObject;
        var fps = go.GetComponent<FPSController>();
        if (fps == null) fps = go.GetComponentInChildren<FPSController>();
        if (fps != null)
        {
            fps.SetContinuousQuake(active, posAmp, rotAmp, freq);
        }
    }

    [ClientRpc]
    private void ShowEndPromptClientRpc(string text, float seconds)
    {
        if (AnnouncementUI.Instance != null)
        {
            AnnouncementUI.Instance.Show(text, seconds);
        }
    }

    [ClientRpc]
    private void ShowMessageClientRpc(string text, float seconds, ClientRpcParams clientRpcParams = default)
    {
        if (AnnouncementUI.Instance != null)
        {
            AnnouncementUI.Instance.Show(text, seconds);
        }
    }

    private void TryReturnPlayerToSpawn(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) return;
        var playerObj = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;
        if (playerObj == null) return;
        if (!TryGetSpawnPose(clientId, out var spawnPos, out var spawnRot)) return;

        var cc = playerObj.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        playerObj.transform.SetPositionAndRotation(spawnPos, spawnRot);
        if (cc != null) cc.enabled = true;

        NetworkObjectReference noRef = new NetworkObjectReference(playerObj);
        WarpPlayerClientRpc(noRef, spawnPos, spawnRot);
    }

    private bool TryGetSpawnPose(ulong clientId, out Vector3 position, out Quaternion rotation)
    {
        var spawnManager = PlayerSpawnManager.Instance;
        if (spawnManager == null)
        {
            spawnManager = Object.FindFirstObjectByType<PlayerSpawnManager>();
        }
        if (spawnManager != null && spawnManager.TryGetSpawnLocation(clientId, out position, out rotation))
        {
            return true;
        }
        position = default;
        rotation = default;
        return false;
    }

    public static void RequestExitForLocal()
    {
        if (Instance != null)
        {
            Instance.RequestExitServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestExitServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!_releaseActive) return; // only allow after quake fully ended
        ulong sender = rpcParams.Receive.SenderClientId;
        TryReturnPlayerToSpawn(sender);
        var sendParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { sender } }
        };
        // Disable forced crouch for this client
        ForceCrouchClientRpc(false, sendParams);

        // Instruct this player to calmly exit to corridor and wait
        ShowMessageClientRpc("Sakince siniftan cikin ve koridorda bekleyin.", 6f, sendParams);

        // Mark this player as exited from drop-cover
        _exited.Add(sender);
        // If corridor has people and not all exited, nudge them
        WarnCorridorIfNotAllExited();
    }

    // Corridor reporting
    public static void ReportCorridorLocal()
    {
        if (Instance != null)
        {
            Instance.ReportCorridorServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportCorridorServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!_releaseActive) return; // only track after release
        ulong sender = rpcParams.Receive.SenderClientId;
        _inCorridor.Add(sender);
        CheckAllInCorridor();
        // If not everyone has exited yet, warn corridor players
        WarnCorridorIfNotAllExited();
    }

    private void CheckAllInCorridor()
    {
        if (!IsServer || NetworkManager.Singleton == null) return;
        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (!_inCorridor.Contains(id)) return;
        }
        // All players are in corridor; broadcast next instruction
        ShowAllMessageClientRpc("Merdiven ile asagiya inin.", 6f);
    }

    private void WarnCorridorIfNotAllExited()
    {
        if (!IsServer || NetworkManager.Singleton == null) return;
        // If everyone has exited, no warning needed
        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (!_exited.Contains(id))
            {
                // Build target list = current corridor players
                if (_inCorridor.Count == 0) return;
                var targets = new System.Collections.Generic.List<ulong>(_inCorridor);
                var sendParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = targets.ToArray() }
                };
                ShowMessageClientRpc("Daha siniftan cikis yapmayanlar var.", 4f, sendParams);
                return;
            }
        }
    }

    [ClientRpc]
    private void ShowAllMessageClientRpc(string text, float seconds)
    {
        if (AnnouncementUI.Instance != null)
        {
            AnnouncementUI.Instance.Show(text, seconds);
        }
    }

    private int GetJoinIndex(ulong clientId)
    {
        if (_joinIndex.TryGetValue(clientId, out var idx)) return idx;
        // Fallback: derive from sorted client IDs for determinism
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            if (clientId == NetworkManager.ServerClientId)
            {
                _joinIndex[clientId] = 0;
                _nextJoinIndex = Mathf.Max(_nextJoinIndex, 1);
                return 0;
            }
            var ids = new System.Collections.Generic.List<ulong>(nm.ConnectedClientsIds);
            ids.Sort();
            // Ensure server id is considered first
            int rank = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == NetworkManager.ServerClientId) continue; // skip server for now
                if (ids[i] == clientId)
                {
                    _joinIndex[clientId] = rank + 1; // after server
                    _nextJoinIndex = Mathf.Max(_nextJoinIndex, rank + 2);
                    return rank + 1;
                }
                rank++;
            }
        }
        // If unknown, append at end
        int newIdx = _nextJoinIndex++;
        _joinIndex[clientId] = newIdx;
        return newIdx;
    }
}
