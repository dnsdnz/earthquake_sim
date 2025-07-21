using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class NetworkManagerSetup : MonoBehaviour
{
    public Button hostBtn, clientBtn;

    void Start()
    {
        hostBtn.onClick.AddListener(() => NetworkManager.Singleton.StartHost());
        clientBtn.onClick.AddListener(() => NetworkManager.Singleton.StartClient());
    }
}