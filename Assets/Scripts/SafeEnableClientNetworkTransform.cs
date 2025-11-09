using System.Collections;
using UnityEngine;
using Unity.Netcode.Samples;

// Workaround for NGO NetworkTransform enumeration bug on connect:
// Keep ClientNetworkTransform disabled on the prefab, then enable it a
// frame or two after spawn so registration does not occur during a tick.
[DisallowMultipleComponent]
public class SafeEnableClientNetworkTransform : MonoBehaviour
{
    [Tooltip("Frames to wait before enabling ClientNetworkTransform")] 
    public int delayFrames = 2;

    private ClientNetworkTransform _cnt;
    private bool _started;

    private void Awake()
    {
        _cnt = GetComponent<ClientNetworkTransform>();
    }

    private void OnEnable()
    {
        if (_started) return;
        _started = true;
        StartCoroutine(EnableLater());
    }

    private IEnumerator EnableLater()
    {
        // Ensure CNT exists; it should be disabled on the prefab
        if (_cnt == null) yield break;

        // Wait until after initial network update stages
        for (int i = 0; i < Mathf.Max(1, delayFrames); i++)
        {
            yield return new WaitForEndOfFrame();
        }
        _cnt.enabled = true;
    }
}

