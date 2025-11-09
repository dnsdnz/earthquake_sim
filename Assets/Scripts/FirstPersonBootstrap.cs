using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

/// Ensures the local player's GameObject has a FirstPersonCameraController attached
/// and configures its head anchor automatically. Runs across scenes.
public class FirstPersonBootstrap : MonoBehaviour
{
    private bool _attached;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var go = new GameObject("__FirstPersonBootstrap");
        DontDestroyOnLoad(go);
        go.AddComponent<FirstPersonBootstrap>();
    }

    private void OnEnable()
    {
        SceneManager.activeSceneChanged += OnSceneChanged;
        TryAttach();
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
    }

    private void OnSceneChanged(Scene oldScene, Scene newScene)
    {
        _attached = false;
        TryAttach();
    }

    private void Update()
    {
        if (!_attached)
        {
            TryAttach();
        }
    }

    private void TryAttach()
    {
        if (_attached) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return;

        var player = nm.LocalClient.PlayerObject.gameObject;
        // Prefer the new FPSController
        var fps = player.GetComponent<FPSController>();
        if (fps == null)
        {
            fps = player.AddComponent<FPSController>();
        }

        // Keep transform components consistent with prefab across server and clients.
        // Do not swap NetworkTransform types at runtime to avoid authority mismatches.


        // Disable older camera controllers to avoid conflicts
        var oldFpc = player.GetComponent<FirstPersonCameraController>();
        if (oldFpc) oldFpc.enabled = false;
        var follow = FindObjectOfType<PlayerCameraFollow>();
        if (follow) follow.enabled = false;

        // Disable legacy movement scripts on all player objects to avoid double-driving
        var allLegacy1 = FindObjectsOfType<PlayerControl>(true);
        foreach (var c in allLegacy1) c.enabled = false;
        var allLegacy2 = FindObjectsOfType<PlayerControlAuthorative>(true);
        foreach (var c in allLegacy2) c.enabled = false;
        var allLegacy3 = FindObjectsOfType<PlayerWithRaycastControl>(true);
        foreach (var c in allLegacy3) c.enabled = false;

        _attached = true;
    }

    // Intentionally no ownership enforcement toggling at runtime here.

    private static Transform FindChildWithTag(Transform root, string tag)
    {
        if (root.CompareTag(tag)) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = FindChildWithTag(root.GetChild(i), tag);
            if (c != null) return c;
        }
        return null;
    }
}
