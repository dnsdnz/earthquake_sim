using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(NetworkTransform))]
[RequireComponent(typeof(NetworkObject))]
public class PlayerWithRaycastControl : NetworkBehaviour
{
    [SerializeField]
    private float walkSpeed = 3.5f;

    [SerializeField]
    private float runSpeedOffset = 2.0f;

    [SerializeField]
    private float rotationSpeed = 3.5f;

    [SerializeField]
    private Vector2 defaultInitialPositionOnPlane = new Vector2(-4, 4);

    [SerializeField]
    private NetworkVariable<Vector3> networkPositionDirection = new NetworkVariable<Vector3>();

    [SerializeField]
    private NetworkVariable<Vector3> networkRotationDirection = new NetworkVariable<Vector3>();

    [SerializeField]
    private NetworkVariable<PlayerState> networkPlayerState = new NetworkVariable<PlayerState>();


    [SerializeField]
    private NetworkVariable<float> networkPlayerHealth = new NetworkVariable<float>(1000);

    [SerializeField]
    private NetworkVariable<float> networkPlayerPunchBlend = new NetworkVariable<float>();

    [SerializeField]
    private GameObject leftHand;

    [SerializeField]
    private GameObject rightHand;

    [SerializeField]
    private float minPunchDistance = 1.0f;

    private CharacterController characterController;

    // client caches positions
    private Vector3 oldInputPosition = Vector3.zero;
    private Vector3 oldInputRotation = Vector3.zero;
    private PlayerState oldPlayerState = PlayerState.Idle;

    private Animator animator;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
    }

    void Start()
    {
        if (IsClient && IsOwner)
        {
            transform.position = new Vector3(Random.Range(defaultInitialPositionOnPlane.x, defaultInitialPositionOnPlane.y), 0,
                   Random.Range(defaultInitialPositionOnPlane.x, defaultInitialPositionOnPlane.y));

            PlayerCameraFollow.Instance.FollowPlayer(transform.Find("PlayerCameraRoot"));
        }
    }

    void Update()
    {
        if (IsClient && IsOwner)
        {
            ClientInput();
        }

        ClientMoveAndRotate();
        ClientVisuals();
    }

    private void FixedUpdate()
    {
        if (IsClient && IsOwner)
        {
            if (networkPlayerState.Value == PlayerState.Punch && ActivePunchActionKey())
            {
                CheckPunch(leftHand.transform, Vector3.up);
                CheckPunch(rightHand.transform, Vector3.down);
            }
        }
    }

    private void CheckPunch(Transform hand, Vector3 aimDirection)
    {
        RaycastHit hit;

        int layerMask = LayerMask.GetMask("Player");

        if (Physics.Raycast(hand.position, hand.transform.TransformDirection(aimDirection), out hit, minPunchDistance, layerMask))
        {
            Debug.DrawRay(hand.position, hand.transform.TransformDirection(aimDirection) * minPunchDistance, Color.yellow);

            var playerHit = hit.transform.GetComponent<NetworkObject>();
            if (playerHit != null)
            { 
                UpdateHealthServerRpc(1, playerHit.OwnerClientId);
            }
        }
        else
        {
            Debug.DrawRay(hand.position, hand.transform.TransformDirection(aimDirection) * minPunchDistance, Color.red);
        }
    }


    private void ClientMoveAndRotate()
    {
        if (networkPositionDirection.Value != Vector3.zero)
        {
            characterController.SimpleMove(networkPositionDirection.Value);
        }
        if (networkRotationDirection.Value != Vector3.zero)
        {
            transform.Rotate(networkRotationDirection.Value, Space.World);
        }
    }

    private void ClientVisuals()
    {
        if (oldPlayerState != networkPlayerState.Value)
        {
            oldPlayerState = networkPlayerState.Value;
            animator.SetTrigger($"{networkPlayerState.Value}");
            if (networkPlayerState.Value == PlayerState.Punch)
            {
                animator.SetFloat($"{networkPlayerState.Value}Blend", networkPlayerPunchBlend.Value);
            }
        }
    }

    private void ClientInput()
    {
        // Smoothing state
        if (!_turnVelInit) { _turnVelInit = true; _turnSmoothVelocity = 0f; }
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        // Camera-relative movement
        Vector3 camF = Vector3.forward;
        Vector3 camR = Vector3.right;
        var cam = Camera.main;
        if (cam != null)
        {
            camF = cam.transform.forward; camF.y = 0f; camF.Normalize();
            camR = cam.transform.right;   camR.y = 0f; camR.Normalize();
        }
        else
        {
            camF = transform.forward;
            camR = transform.right;
        }

        Vector3 moveInput = (camF * v + camR * h);
        Vector3 inputPosition = moveInput;

        // change fighting states
        if (ActivePunchActionKey() && Mathf.Approximately(v, 0f) && Mathf.Approximately(h, 0f))
        {
            UpdatePlayerStateServerRpc(PlayerState.Punch);
            return;
        }

        // Rotation handling: face camera on forward, don't rotate on strafe-only, idle edge/align
        Vector3 inputRotation = Vector3.zero;
        var currentYaw = transform.eulerAngles.y;
        bool hasForward = Mathf.Abs(v) > 0.01f;
        bool hasStrafe = Mathf.Abs(h) > 0.01f;
        if (hasForward)
        {
            float camYaw = cam != null ? cam.transform.eulerAngles.y : currentYaw;
            var yawDelta = Mathf.DeltaAngle(currentYaw, camYaw);
            if (Mathf.Abs(yawDelta) < 1.5f)
            {
                inputRotation = Vector3.zero;
            }
            else
            {
                var smoothYaw = Mathf.SmoothDampAngle(currentYaw, camYaw, ref _turnSmoothVelocity, 0.12f);
                var step = Mathf.DeltaAngle(currentYaw, smoothYaw);
                if (Mathf.Abs(step) < 0.05f) step = 0f;
                inputRotation = new Vector3(0f, step / Mathf.Max(rotationSpeed, 0.0001f), 0f);
            }
        }
        else if (hasStrafe)
        {
            inputRotation = Vector3.zero;
        }
        else
        {
            float yawDelta = 0f;
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                yawDelta = ComputeEdgeTurnDelta(0.12f, rotationSpeed * 3f);
            }
            else if (Camera.main != null)
            {
                var camYaw = Camera.main.transform.eulerAngles.y;
                var angle = Mathf.DeltaAngle(currentYaw, camYaw);
                if (Mathf.Abs(angle) > 20f)
                {
                    var step = Mathf.Sign(angle) * Mathf.Min(Mathf.Abs(angle), 90f * Time.deltaTime);
                    yawDelta = step;
                }
            }
            inputRotation = new Vector3(0f, yawDelta / Mathf.Max(rotationSpeed, 0.0001f), 0f);
        }

        // change motion states
        if (Mathf.Approximately(h, 0f) && Mathf.Approximately(v, 0f))
            UpdatePlayerStateServerRpc(PlayerState.Idle);
        else if (ActiveRunningActionKey() && v >= 0f)
        {
            inputPosition = moveInput.normalized * runSpeedOffset;
            UpdatePlayerStateServerRpc(PlayerState.Run);
        }
        else if (v < 0f && !ActiveRunningActionKey())
            UpdatePlayerStateServerRpc(PlayerState.ReverseWalk);
        else
            UpdatePlayerStateServerRpc(PlayerState.Walk);

        // let server know about position and rotation client changes
        if (oldInputPosition != inputPosition || oldInputRotation != inputRotation)
        {
            oldInputPosition = inputPosition;
            oldInputRotation = inputRotation;
            UpdateClientPositionAndRotationServerRpc(inputPosition * walkSpeed, inputRotation * rotationSpeed);
        }
    }

    private float _turnSmoothVelocity = 0f;
    private bool _turnVelInit = false;

    private static float ComputeEdgeTurnDelta(float edgePct, float maxSpeedDegPerSec)
    {
        if (Screen.width <= 0) return 0f;
        float x = Input.mousePosition.x;
        float w = Screen.width;
        float leftZone = w * edgePct;
        float rightZone = w * (1f - edgePct);
        float t = 0f;
        if (x < leftZone)
        {
            t = (leftZone - x) / leftZone; // 0..1
            t = t * t; // ease
            return -maxSpeedDegPerSec * t * Time.deltaTime;
        }
        else if (x > rightZone)
        {
            t = (x - rightZone) / (w - rightZone); // 0..1
            t = t * t; // ease
            return maxSpeedDegPerSec * t * Time.deltaTime;
        }
        return 0f;
    }

    private static bool ActiveRunningActionKey()
    {
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }

    private static bool ActivePunchActionKey()
    {
        return Input.GetKey(KeyCode.Space);
    }

    [ServerRpc]
    public void UpdateClientPositionAndRotationServerRpc(Vector3 newPosition, Vector3 newRotation)
    {
        networkPositionDirection.Value = newPosition;
        networkRotationDirection.Value = newRotation;
    }

    [ServerRpc]
    public void UpdateHealthServerRpc(int takeAwayPoint, ulong clientId)
    {
        var clientWithDamaged = NetworkManager.Singleton.ConnectedClients[clientId]
            .PlayerObject.GetComponent<PlayerWithRaycastControl>();

        if (clientWithDamaged != null && clientWithDamaged.networkPlayerHealth.Value > 0)
        {
            clientWithDamaged.networkPlayerHealth.Value -= takeAwayPoint;
        }

        // execute method on a client getting punch
        NotifyHealthChangedClientRpc(takeAwayPoint, new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { clientId }
            }
        });
    }

    [ClientRpc]
    public void NotifyHealthChangedClientRpc(int takeAwayPoint, ClientRpcParams clientRpcParams = default)
    {
        if (IsOwner) return;

        Logger.Instance.LogInfo($"Client got punch {takeAwayPoint}");
    }

    [ServerRpc]
    public void UpdatePlayerStateServerRpc(PlayerState state)
    {
        networkPlayerState.Value = state;
        if (state == PlayerState.Punch)
        {
            networkPlayerPunchBlend.Value = Random.Range(0.0f, 1.0f);
        }
    }
}
