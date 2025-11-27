using System.Linq;
using DilmerGames.Core.Singletons;
using TMPro;
using UnityEngine;
using System;

public class Logger : Singleton<Logger>
{
    [SerializeField]
    private TextMeshProUGUI debugAreaText = null;

    [SerializeField]
    private bool enableDebug = false;

    [SerializeField]
    private int maxLines = 15;

    // Always keep UI logger hidden for players/host unless explicitly re-enabled for debugging.
    void Awake()
    {
        if (debugAreaText == null)
        {
            debugAreaText = GetComponent<TextMeshProUGUI>();
        }
        enableDebug = false;
        debugAreaText.text = string.Empty;
    }

    void OnEnable()
    {
        debugAreaText.enabled = enableDebug;
        enabled = enableDebug;

        if (enabled)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            debugAreaText.text += $"<color=white>{timestamp} {GetType().Name} enabled</color>\n";
        }
    }

    public void LogInfo(string message)
    {
        if (!enableDebug) return;
        ClearLines();

        debugAreaText.text += $"<color=green>{DateTime.Now:HH:mm:ss.fff} {message}</color>\n";
    }

    public void LogError(string message)
    {
        if (!enableDebug) return;
        ClearLines();
        debugAreaText.text += $"<color=red>{DateTime.Now:HH:mm:ss.fff} {message}</color>\n";
    }

    public void LogWarning(string message)
    {
        if (!enableDebug) return;
        ClearLines();
        debugAreaText.text += $"<color=yellow>{DateTime.Now:HH:mm:ss.fff} {message}</color>\n";
    }

    private void ClearLines()
    {
        if (debugAreaText.text.Split('\n').Count() >= maxLines)
        {
            debugAreaText.text = string.Empty;
        }
    }
}
