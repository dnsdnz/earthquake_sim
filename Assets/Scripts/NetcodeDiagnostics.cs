using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

// Lightweight, read-only diagnostics to find which objects toggle NetworkTransform
// around connect/spawn time. Does NOT add/remove/enable/disable any components.
// It just snapshots enabled/active state changes and logs them.
public class NetcodeDiagnostics : MonoBehaviour
{
    public static NetcodeDiagnostics Instance { get; private set; }

    [Tooltip("How long to watch after a connect/start event (seconds)")]
    [SerializeField] private float monitorWindowSeconds = 6f;

    [Tooltip("Log every frame diff (verbose)")]
    [SerializeField] private bool verbose = false;

    private Coroutine _monitorRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("__NetcodeDiagnostics");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<NetcodeDiagnostics>();
        Instance.HookNetcode();
    }

    private void HookNetcode()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        nm.OnServerStarted += () => StartMonitor("OnServerStarted");
        nm.OnClientConnectedCallback += _ => StartMonitor("OnClientConnected");
    }

    public void StartMonitor(string reason = "Manual")
    {
        if (_monitorRoutine != null) StopCoroutine(_monitorRoutine);
        _monitorRoutine = StartCoroutine(MonitorRoutine(reason));
    }

    private struct Entry
    {
        public int id;
        public string path;
        public bool enabled;
        public bool active;
        public string type;
    }

    private IEnumerator MonitorRoutine(string reason)
    {
        var startTime = Time.time;
        var prev = new Dictionary<int, Entry>(256);
        Debug.Log($"[NetcodeDiagnostics] Start monitor ({reason}) for {monitorWindowSeconds:F1}s");

        while (Time.time - startTime < monitorWindowSeconds)
        {
            var now = Snapshot();

            // Detect changes
            foreach (var kv in now)
            {
                var e = kv.Value;
                if (prev.TryGetValue(kv.Key, out var p))
                {
                    if (p.enabled != e.enabled || p.active != e.active)
                    {
                        Debug.Log($"[NetcodeDiagnostics] NT state change: enabled {p.enabled}->{e.enabled}, active {p.active}->{e.active} | {e.type} | {e.path}");
                    }
                    else if (verbose)
                    {
                        Debug.Log($"[NetcodeDiagnostics] NT unchanged: {e.type} | {e.path}");
                    }
                }
                else
                {
                    Debug.Log($"[NetcodeDiagnostics] NT appeared: enabled {e.enabled}, active {e.active} | {e.type} | {e.path}");
                }
            }

            foreach (var kv in prev)
            {
                if (!now.ContainsKey(kv.Key))
                {
                    Debug.Log($"[NetcodeDiagnostics] NT disappeared: {kv.Value.type} | {kv.Value.path}");
                }
            }

            prev = now;
            yield return null; // next frame
        }

        Debug.Log("[NetcodeDiagnostics] Monitor window complete.");
        _monitorRoutine = null;
    }

    private Dictionary<int, Entry> Snapshot()
    {
        var dict = new Dictionary<int, Entry>(256);
        var nts = FindObjectsOfType<NetworkTransform>(true);
        for (int i = 0; i < nts.Length; i++)
        {
            var nt = nts[i];
            if (nt == null) continue;
            var id = nt.GetInstanceID();
            dict[id] = new Entry
            {
                id = id,
                path = GetPath(nt.transform),
                enabled = nt.enabled,
                active = nt.gameObject.activeInHierarchy,
                type = nt.GetType().FullName
            };
        }
        return dict;
    }

    private static string GetPath(Transform t)
    {
        if (t == null) return "<null>";
        var parts = new System.Collections.Generic.List<string>(8);
        while (t != null)
        {
            parts.Add(t.name);
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }
}

