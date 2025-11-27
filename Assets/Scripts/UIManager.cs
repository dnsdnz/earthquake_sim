using DilmerGames.Core.Singletons;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode.Transports.UTP;
using UnityEngine.EventSystems;
using System.Linq;

public class UIManager : Singleton<UIManager>
{
    [SerializeField]
    private Button startServerButton;

    [SerializeField]
    private Button startHostButton;

    [SerializeField]
    private Button startClientButton;

    [SerializeField]
    private TextMeshProUGUI playersInGameText;

    [SerializeField]
    private TMP_InputField joinCodeInput;

    [SerializeField]
    private Button executePhysicsButton;

    [Header("UI Scaling")]
    [SerializeField] private CanvasScaler canvasScaler;
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] private float matchWidthOrHeight = 0.5f;
    [SerializeField] private Canvas uiCanvas;

    private bool hasServerStarted;

    [Header("LAN Defaults (when Relay disabled)")]
    [SerializeField] private string lanAddress = "127.0.0.1";
    [SerializeField] private ushort lanPort = 7777;

    private void Awake()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        EnsureCanvasAndEventSystem();
        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient))
        {
            HideNetworkControls();
        }
        ConfigureCanvasScaler();
    }

    void Update()
    {
        playersInGameText.text = $"Players in game: {PlayersManager.Instance.PlayersInGame}";
    }

    void Start()
    {
        if (playersInGameText != null)
        {
            playersInGameText.raycastTarget = false;
        }
        // START SERVER
        startServerButton?.onClick.AddListener(() =>
        {
            var transport = RelayManager.Instance.Transport;
            if (transport != null && transport.Protocol != UnityTransport.ProtocolType.RelayUnityTransport)
            {
                transport.SetConnectionData(lanAddress, lanPort);
            }
            if (NetworkManager.Singleton.StartServer())
            {
                Logger.Instance.LogInfo("Server started...");
                HideNetworkControls();
            }
            else
                Logger.Instance.LogInfo("Unable to start server...");
        });

        // START HOST
        startHostButton?.onClick.AddListener(async () =>
        {
            // this allows the UnityMultiplayer and UnityMultiplayerRelay scene to work with and without
            // relay features - if the Unity transport is found and is relay protocol then we redirect all the 
            // traffic through the relay, else it just uses a LAN type (UNET) communication.
            if (RelayManager.Instance.IsRelayEnabled) 
                await RelayManager.Instance.SetupRelay();

            var transport = RelayManager.Instance.Transport;
            if (transport != null && transport.Protocol != UnityTransport.ProtocolType.RelayUnityTransport)
            {
                transport.SetConnectionData(lanAddress, lanPort);
            }
            if (NetworkManager.Singleton.StartHost())
            {
                Logger.Instance.LogInfo("Host started...");
                HideNetworkControls();
            }
            else
                Logger.Instance.LogInfo("Unable to start host...");
        });

        // START CLIENT
        startClientButton?.onClick.AddListener(async () =>
        {
            var transport = RelayManager.Instance.Transport;
            if (RelayManager.Instance.IsRelayEnabled)
            {
                if (!string.IsNullOrEmpty(joinCodeInput.text))
                {
                    await RelayManager.Instance.JoinRelay(joinCodeInput.text);
                }
                else
                {
                    Logger.Instance.LogWarning("Relay is enabled but no Join Code provided. Aborting client start.");
                    return;
                }
            }
            else if (transport != null)
            {
                // Ensure LAN connection data is set for client
                transport.SetConnectionData(lanAddress, lanPort);
            }

            if(NetworkManager.Singleton.StartClient())
            {
                Logger.Instance.LogInfo("Client started...");
                HideNetworkControls();
            }
            else
                Logger.Instance.LogInfo("Unable to start client...");
        });

        // STATUS TYPE CALLBACKS
        NetworkManager.Singleton.OnClientConnectedCallback += (id) =>
        {
            Logger.Instance.LogInfo($"{id} just connected...");
            if (NetworkManager.Singleton.LocalClientId == id)
            {
                HideNetworkControls();
            }
        };

        NetworkManager.Singleton.OnServerStarted += () =>
        {
            hasServerStarted = true;
            HideNetworkControls();
        };

        executePhysicsButton.onClick.AddListener(() => 
        {
            if (!hasServerStarted)
            {
                Logger.Instance.LogWarning("Server has not started...");
                return;
            }
            // Don't implicitly create SpawnerControl; only use if present
            var sc = Object.FindFirstObjectByType<SpawnerControl>();
            if (sc != null)
            {
                sc.SpawnObjects();
            }
            else
            {
                Logger.Instance.LogInfo("SpawnerControl not present; physics spawn skipped.");
            }
        });
    }

    private void ConfigureCanvasScaler()
    {
        if (canvasScaler == null)
        {
            canvasScaler = uiCanvas != null ? uiCanvas.GetComponent<CanvasScaler>() : null;
        }
        if (canvasScaler == null && uiCanvas != null)
        {
            canvasScaler = uiCanvas.gameObject.AddComponent<CanvasScaler>();
        }
        if (canvasScaler == null) return;

        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = referenceResolution;
        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvasScaler.matchWidthOrHeight = matchWidthOrHeight;
    }

    private void EnsureCanvasAndEventSystem()
    {
        if (uiCanvas == null)
        {
            uiCanvas = GetComponentInParent<Canvas>();
        }
        if (uiCanvas == null && startHostButton != null)
        {
            uiCanvas = startHostButton.GetComponentInParent<Canvas>();
        }
        if (uiCanvas == null && startServerButton != null)
        {
            uiCanvas = startServerButton.GetComponentInParent<Canvas>();
        }
        if (uiCanvas == null && startClientButton != null)
        {
            uiCanvas = startClientButton.GetComponentInParent<Canvas>();
        }
        if (uiCanvas == null)
        {
            uiCanvas = FindObjectOfType<Canvas>();
        }
        if (uiCanvas != null)
        {
            uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiCanvas.overridePixelPerfect = false;
            var rt = uiCanvas.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = Vector2.zero;
                rt.localScale = Vector3.one;
            }
            if (uiCanvas.GetComponent<GraphicRaycaster>() == null)
            {
                uiCanvas.gameObject.AddComponent<GraphicRaycaster>();
            }
            var cg = uiCanvas.GetComponent<CanvasGroup>();
            if (cg == null) cg = uiCanvas.gameObject.AddComponent<CanvasGroup>();
            cg.interactable = true;
            cg.blocksRaycasts = true;
            cg.ignoreParentGroups = false;
        }

        // Ensure the buttons can receive raycasts (in case target graphics were disabled)
        EnableRaycastOnButtonGraphic(startHostButton);
        EnableRaycastOnButtonGraphic(startServerButton);
        EnableRaycastOnButtonGraphic(startClientButton);
        EnableRaycastOnButtonGraphic(executePhysicsButton);

        if (EventSystem.current == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
            var sim = es.GetComponent<StandaloneInputModule>();
            sim.forceModuleActive = true;
        }
        else
        {
            var sim = EventSystem.current.GetComponent<StandaloneInputModule>();
            if (sim != null)
            {
                sim.forceModuleActive = true;
            }
        }
    }

    private static void EnableRaycastOnButtonGraphic(Button button)
    {
        if (button == null || button.targetGraphic == null) return;
        button.targetGraphic.raycastTarget = true;
    }

    private void HideNetworkControls()
    {
        startServerButton?.gameObject.SetActive(false);
        startHostButton?.gameObject.SetActive(false);
        startClientButton?.gameObject.SetActive(false);
        executePhysicsButton?.gameObject.SetActive(false);
    }
}
