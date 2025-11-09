using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
public class FPSAnimatorSync : NetworkBehaviour
{
    [SerializeField] private Animator animator;

    private NetworkVariable<float> networkMoveX = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<float> networkMoveY = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> networkIsCrouching = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float _lastX = float.NaN;
    private float _lastY = float.NaN;
    private bool _lastCrouch = false;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
        if (animator != null)
        {
            animator.applyRootMotion = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        networkMoveX.OnValueChanged += OnMoveXChanged;
        networkMoveY.OnValueChanged += OnMoveYChanged;
        networkIsCrouching.OnValueChanged += OnCrouchChanged;
        ApplyAnimator(networkMoveX.Value, networkMoveY.Value, networkIsCrouching.Value);
    }

    private void OnDestroy()
    {
        networkMoveX.OnValueChanged -= OnMoveXChanged;
        networkMoveY.OnValueChanged -= OnMoveYChanged;
        networkIsCrouching.OnValueChanged -= OnCrouchChanged;
    }

    private void OnMoveXChanged(float oldVal, float newVal)
    {
        ApplyAnimator(newVal, networkMoveY.Value, networkIsCrouching.Value);
    }

    private void OnMoveYChanged(float oldVal, float newVal)
    {
        ApplyAnimator(networkMoveX.Value, newVal, networkIsCrouching.Value);
    }

    private void OnCrouchChanged(bool oldVal, bool newVal)
    {
        ApplyAnimator(networkMoveX.Value, networkMoveY.Value, newVal);
    }

    private void ApplyAnimator(float x, float y, bool crouch)
    {
        if (animator == null) return;
        if (_lastX != x) animator.SetFloat("MoveX", x);
        if (_lastY != y) animator.SetFloat("MoveY", y);
        if (_lastCrouch != crouch) animator.SetBool("IsCrouching", crouch);
        _lastX = x; _lastY = y; _lastCrouch = crouch;
    }

    [ServerRpc]
    private void SetMovementServerRpc(float x, float y, bool crouch)
    {
        networkMoveX.Value = x;
        networkMoveY.Value = y;
        networkIsCrouching.Value = crouch;
    }

    public void SetMovement(float x, float y, bool crouch)
    {
        x = Mathf.Clamp(x, -1f, 1f);
        y = Mathf.Clamp(y, -1f, 1f);

        if (!IsSpawned)
        {
            ApplyAnimator(x, y, crouch);
            return;
        }

        if (IsServer)
        {
            networkMoveX.Value = x;
            networkMoveY.Value = y;
            networkIsCrouching.Value = crouch;
        }
        else if (IsOwner)
        {
            ApplyAnimator(x, y, crouch);
            SetMovementServerRpc(x, y, crouch);
        }
    }
}
