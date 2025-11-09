using DilmerGames.Core.Singletons;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode.Transports.UTP;

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

    private bool hasServerStarted;

    [Header("LAN Defaults (when Relay disabled)")]
    [SerializeField] private string lanAddress = "127.0.0.1";
    [SerializeField] private ushort lanPort = 7777;

    private void Awake()
    {
        Cursor.visible = true;
    }

    void Update()
    {
        playersInGameText.text = $"Players in game: {PlayersManager.Instance.PlayersInGame}";
    }

    void Start()
    {
        // START SERVER
        startServerButton?.onClick.AddListener(() =>
        {
            var transport = RelayManager.Instance.Transport;
            if (transport != null && transport.Protocol != UnityTransport.ProtocolType.RelayUnityTransport)
            {
                transport.SetConnectionData(lanAddress, lanPort);
            }
            if (NetworkManager.Singleton.StartServer())
                Logger.Instance.LogInfo("Server started...");
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
                Logger.Instance.LogInfo("Host started...");
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
                Logger.Instance.LogInfo("Client started...");
            else
                Logger.Instance.LogInfo("Unable to start client...");
        });

        // STATUS TYPE CALLBACKS
        NetworkManager.Singleton.OnClientConnectedCallback += (id) =>
        {
            Logger.Instance.LogInfo($"{id} just connected...");
        };

        NetworkManager.Singleton.OnServerStarted += () =>
        {
            hasServerStarted = true;
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
}
