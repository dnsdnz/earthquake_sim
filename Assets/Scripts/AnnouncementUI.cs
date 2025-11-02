using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AnnouncementUI : MonoBehaviour
{
    public static AnnouncementUI Instance { get; private set; }

    [Header("Style")]
    public Vector2 anchorMin = new Vector2(0.2f, 0.85f);
    public Vector2 anchorMax = new Vector2(0.8f, 0.98f);
    public int fontSize = 36;
    public Color textColor = Color.white;
    public Color shadowColor = new Color(0, 0, 0, 0.6f);
    public float fadeIn = 0.15f;
    public float fadeOut = 0.35f;

    private Canvas _canvas;
    private TextMeshProUGUI _text;
    private CanvasGroup _group;
    private Coroutine _routine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("__AnnouncementUI");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<AnnouncementUI>();
        Instance.Build();
    }

    private void Build()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        gameObject.AddComponent<CanvasScaler>();
        gameObject.AddComponent<GraphicRaycaster>();

        var textGO = new GameObject("AnnouncementText");
        textGO.transform.SetParent(transform, false);
        _group = textGO.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _text = textGO.AddComponent<TextMeshProUGUI>();
        var rt = (RectTransform)textGO.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        _text.alignment = TextAlignmentOptions.Center;
        _text.text = "";
        _text.fontSize = fontSize;
        _text.color = textColor;
        var shadow = textGO.AddComponent<Shadow>();
        shadow.effectColor = shadowColor;
        shadow.effectDistance = new Vector2(2, -2);
    }

    public void Show(string message, float durationSeconds = 5f)
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(ShowRoutine(message, durationSeconds));
    }

    private System.Collections.IEnumerator ShowRoutine(string message, float duration)
    {
        _text.text = message;
        // fade in
        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.deltaTime;
            _group.alpha = Mathf.InverseLerp(0f, fadeIn, t);
            yield return null;
        }
        _group.alpha = 1f;

        // hold
        yield return new WaitForSeconds(duration);

        // fade out
        t = 0f;
        while (t < fadeOut)
        {
            t += Time.deltaTime;
            _group.alpha = 1f - Mathf.InverseLerp(0f, fadeOut, t);
            yield return null;
        }
        _group.alpha = 0f;
        _text.text = "";
        _routine = null;
    }
}

