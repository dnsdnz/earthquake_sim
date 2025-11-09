using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class PlayerControl : NetworkBehaviour
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
        // Position is assigned by server-side spawn system.
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
        }
    }

    private void ClientInput()
    {
        // Smoothing params
        const float turnSmoothTime = 0.12f; // seconds
        static float SmoothStep(float current, float target, ref float vel)
        {
            return Mathf.SmoothDampAngle(current, target, ref vel, turnSmoothTime);
        }
        // keep across calls
        if (_turnVelInit == false) { _turnVelInit = true; _turnSmoothVelocity = 0f; }

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        // Camera-relative movement (WASD relative to where the camera looks)
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
            // fallback to character forward/right
            camF = transform.forward;
            camR = transform.right;
        }

        Vector3 moveInput = (camF * v + camR * h);
        Vector3 inputPosition = moveInput;

        // Determine rotation: face camera when moving forward, don't rotate on strafe-only, idle uses edge/align
        Vector3 inputRotation = Vector3.zero;
        var currentYaw = transform.eulerAngles.y;
        bool hasForward = Mathf.Abs(v) > 0.01f;
        bool hasStrafe = Mathf.Abs(h) > 0.01f;
        if (hasForward)
        {
            float camYaw = cam != null ? cam.transform.eulerAngles.y : currentYaw;
            // small deadzone to avoid micro corrections when walking straight in FP
            var yawDelta = Mathf.DeltaAngle(currentYaw, camYaw);
            if (Mathf.Abs(yawDelta) < 1.5f) {
                // keep heading
                inputRotation = Vector3.zero;
            } else {
                var smoothYaw = Mathf.SmoothDampAngle(currentYaw, camYaw, ref _turnSmoothVelocity, 0.12f);
                var step = Mathf.DeltaAngle(currentYaw, smoothYaw);
                if (Mathf.Abs(step) < 0.05f) step = 0f;
                inputRotation = new Vector3(0f, step / Mathf.Max(rotationSpeed, 0.0001f), 0f);
            }
        }
        else if (hasStrafe)
        {
            inputRotation = Vector3.zero; // strafe without turning
        }
        else
        {
            // Idle: edge-turn or align-to-camera
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

        // Animation state selection
        if (Mathf.Approximately(h, 0f) && Mathf.Approximately(v, 0f))
        {
            UpdatePlayerStateServerRpc(PlayerState.Idle);
        }
        else if (ActiveRunningActionKey() && v >= 0f)
        {
            inputPosition = moveInput.normalized * runSpeedOffset;
            UpdatePlayerStateServerRpc(PlayerState.Run);
        }
        else if (v < 0f && !ActiveRunningActionKey())
        {
            UpdatePlayerStateServerRpc(PlayerState.ReverseWalk);
        }
        else
        {
            UpdatePlayerStateServerRpc(PlayerState.Walk);
        }

        // Send to server when changed
        if (oldInputPosition != inputPosition || oldInputRotation != inputRotation)
        {
            oldInputPosition = inputPosition;
            oldInputRotation = inputRotation;
            UpdateClientPositionAndRotationServerRpc(inputPosition * walkSpeed, inputRotation * rotationSpeed);
        }
    }

    // backing field for smoothing
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

    [ServerRpc]
    public void UpdateClientPositionAndRotationServerRpc(Vector3 newPosition, Vector3 newRotation)
    {
        networkPositionDirection.Value = newPosition;
        networkRotationDirection.Value = newRotation;
    }

    [ServerRpc]
    public void UpdatePlayerStateServerRpc(PlayerState state)
    {
        networkPlayerState.Value = state;
    }
}
