using Unity.Netcode;
using Unity.Netcode.Samples;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(ClientNetworkTransform))]
public class PlayerControlAuthorative : NetworkBehaviour
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
    private NetworkVariable<PlayerState> networkPlayerState = new NetworkVariable<PlayerState>();

    private CharacterController characterController;

    private Animator animator;

    // client caches animation states
    private PlayerState oldPlayerState = PlayerState.Idle;

    [Header("Turning & Smoothing")]
    [SerializeField] private float turnSmoothTime = 0.12f;
    private float _turnSmoothVelocity;
    [SerializeField] private bool enableEdgeTurn = true;
    [SerializeField] private float edgeZonePercent = 0.12f; // left/right 12% screen edges
    [SerializeField] private float edgeTurnSpeed = 120f;    // deg/sec at outer edge
    [SerializeField] private bool idleAlignToCamera = true;
    [SerializeField] private float idleAlignAngleThreshold = 20f; // deg
    [SerializeField] private float idleAlignSpeed = 90f;          // deg/sec

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

        ClientVisuals();
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
                var smoothYaw = Mathf.SmoothDampAngle(currentYaw, camYaw, ref _turnSmoothVelocity, turnSmoothTime);
                var delta = Mathf.DeltaAngle(currentYaw, smoothYaw);
                inputRotation = new Vector3(0f, delta, 0f);
            }
        }
        else if (hasStrafe)
        {
            inputRotation = Vector3.zero;
        }
        else
        {
            // When idle, turn from screen edges or align to camera when locked
            float yawDelta = 0f;
            if (enableEdgeTurn && Cursor.lockState != CursorLockMode.Locked)
            {
                yawDelta = ComputeEdgeTurnDelta(edgeZonePercent, edgeTurnSpeed);
            }
            else if (idleAlignToCamera && Camera.main != null)
            {
                var camYaw = Camera.main.transform.eulerAngles.y;
                var angle = Mathf.DeltaAngle(currentYaw, camYaw);
                if (Mathf.Abs(angle) > idleAlignAngleThreshold)
                {
                    var step = Mathf.Sign(angle) * Mathf.Min(Mathf.Abs(angle), idleAlignSpeed * Time.deltaTime);
                    yawDelta = step;
                }
            }
            inputRotation = new Vector3(0f, yawDelta, 0f);
        }

        // Animation states
        if (Mathf.Approximately(h, 0f) && Mathf.Approximately(v, 0f))
        {
            UpdatePlayerStateServerRpc(PlayerState.Idle);
        }
        else if (ActiveRunningActionKey() && v >= 0f)
        {
            moveInput = moveInput.normalized * runSpeedOffset;
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

        // Move and rotate locally (authoritative)
        characterController.SimpleMove(moveInput * walkSpeed);
        if (Mathf.Abs(inputRotation.y) > 0.0001f)
        {
            transform.Rotate(inputRotation, Space.World);
        }
    }

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
    public void UpdatePlayerStateServerRpc(PlayerState state)
    {
        networkPlayerState.Value = state;
    }
}
