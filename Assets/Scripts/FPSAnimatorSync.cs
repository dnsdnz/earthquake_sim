using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class FPSAnimatorSync : NetworkBehaviour
{
    [SerializeField] private Animator animator;

    private NetworkVariable<PlayerState> networkPlayerState = new NetworkVariable<PlayerState>(
        PlayerState.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private PlayerState _lastAppliedState = PlayerState.Idle;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
    }

    public override void OnNetworkSpawn()
    {
        networkPlayerState.OnValueChanged += OnStateChanged;
        // Apply initial
        OnStateChanged(PlayerState.Idle, networkPlayerState.Value);
    }

    private void OnDestroy()
    {
        networkPlayerState.OnValueChanged -= OnStateChanged;
    }

    private void OnStateChanged(PlayerState oldState, PlayerState newState)
    {
        if (animator == null) return;
        if (_lastAppliedState == newState) return;
        _lastAppliedState = newState;
        animator.SetTrigger(newState.ToString());
    }

    [ServerRpc]
    private void SetStateServerRpc(PlayerState newState)
    {
        networkPlayerState.Value = newState;
    }

    public void SetState(PlayerState newState)
    {
        if (!IsSpawned)
        {
            // Fallback if not yet spawned
            _lastAppliedState = newState;
            if (animator != null) animator.SetTrigger(newState.ToString());
            return;
        }

        if (IsServer)
        {
            networkPlayerState.Value = newState;
        }
        else if (IsOwner)
        {
            // Apply locally for instant feedback, then replicate
            if (_lastAppliedState != newState && animator != null)
            {
                _lastAppliedState = newState;
                animator.SetTrigger(newState.ToString());
            }
            SetStateServerRpc(newState);
        }
    }
}

