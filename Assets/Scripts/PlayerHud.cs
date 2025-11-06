using TMPro;
using Unity.Netcode;
using UnityEngine;

public class PlayerHud : NetworkBehaviour
{
    [SerializeField]
    private NetworkVariable<NetworkString> playerNetworkName = new NetworkVariable<NetworkString>();

    [SerializeField]
    private TextMeshProUGUI overlayText;

    private bool overlaySet = false;

    public override void OnNetworkSpawn()
    {
        if(IsServer)
        {
            playerNetworkName.Value = $"Player {OwnerClientId}";
        }
    }

    public void SetOverlay()
    {
        if (overlayText == null)
        {
            overlayText = gameObject.GetComponentInChildren<TextMeshProUGUI>();
        }
        if (overlayText == null)
        {
            Debug.LogWarning("PlayerHud: No TextMeshProUGUI found in children to set player name overlay.");
            return;
        }
        overlayText.text = $"{playerNetworkName.Value}";
    }

    public void Update()
    {
        if(!overlaySet && !string.IsNullOrEmpty(playerNetworkName.Value))
        {
            SetOverlay();
            overlaySet = true;
        }
    }
}
