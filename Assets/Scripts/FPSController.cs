using UnityEngine;
using Unity.Netcode;
using Unity.Cinemachine;

[RequireComponent(typeof(CharacterController))]
public class FPSController : MonoBehaviour
{
    [Header("Netcode")]
    [Tooltip("If true, this script only runs for the local owner when a NetworkObject is present.")]
    public bool ownerOnly = true;

    [Header("Camera")]
    [Tooltip("Anchor for the camera. If null, one is created at eyeHeight.")]
    public Transform cameraPivot;
    [Tooltip("Create and use a dedicated Camera instead of Main Camera if none is assigned.")]
    public bool createOwnCameraIfMissing = true;
    public float eyeHeight = 1.7f;
    public float fov = 75f;
    [Tooltip("Forward offset applied to the camera when standing (local Z, meters).")]
    public float standCameraForwardOffset = 0.02f;
    [Tooltip("Forward offset applied to the camera when crouching (local Z, meters).")]
    public float crouchCameraForwardOffset = 0.06f;

    [Header("Look")]
    public bool lockCursor = true;
    public float mouseSensitivity = 0.8f;     // base multiplier
    public float lookSmoothTime = 0.08f;      // s
    public float maxPitch = 85f;
    public float lookDeadzone = 0.5f;         // px (new input) or ~axis units (legacy threshold in code)
    public float moveLookDeadzone = 1.5f;     // stronger while moving
    public bool requireHoldToLookWhileMoving = false; // hold RMB to look while moving

    [Header("Move")]
    public float walkSpeed = 4.5f;
    public float sprintSpeed = 7.0f;
    public float accelerationTime = 0.10f;    // s
    public float decelerationTime = 0.12f;    // s
    public float airControl = 0.5f;           // 0..1

    [Header("Jump/Gravity")]
    public float jumpHeight = 1.2f;
    public float gravity = -19.62f;           // m/s^2 (a bit stronger than earth for game feel)
    public float groundedGravity = -2f;       // small downward force to keep grounded
    public float coyoteTime = 0.1f;           // seconds grace after leaving ground

    [Header("Crouch")]
    public bool enableCrouch = true;
    public float crouchHeight = 1.15f;
    public float crouchSpeed = 3.0f;
    public float crouchLerp = 12f;

    private CharacterController _cc;
    private Camera _cam;
    private Transform _originalCamParent;
    private float _yaw, _pitch, _targetYaw, _targetPitch, _yawVel, _pitchVel;
    private Vector3 _velocity;          // y velocity for gravity
    private Vector3 _moveVel;           // smoothed xz velocity
    private Vector3 _moveVelRef;        // damping ref
    private float _lastGroundedTime;
    private float _standEyeHeight;
    private bool _isCrouching;
    private bool _activeForThisClient = true;

    // Animation
    private FPSAnimatorSync _animSync;
    private PlayerState _lastState = PlayerState.Idle;

    [Header("Earthquake Shake")]
    public KeyCode quakeKey = KeyCode.M;
    public float quakeDuration = 2.0f;
    public float quakePositionAmplitude = 0.15f;   // meters
    public float quakeRotationAmplitude = 1.5f;    // degrees
    public float quakeFrequency = 12f;             // Hz
    public AnimationCurve quakeDamping = AnimationCurve.EaseInOut(0,1,1,0);
    private float _quakeTime;
    private float _quakeTotal;
    private Vector3 _shakePosOffset;
    private Vector3 _shakeRotOffset;
    private float _shakeSeedX, _shakeSeedY, _shakeSeedZ;
    private float _cameraBaseHeight;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _standEyeHeight = eyeHeight;

        var netObj = GetComponentInParent<NetworkObject>();
        if (ownerOnly && netObj != null && NetworkManager.Singleton != null)
        {
            _activeForThisClient = netObj.IsOwner && NetworkManager.Singleton.IsListening;
        }
    }

    private void Start()
    {
        if (!_activeForThisClient)
        {
            enabled = false; return;
        }

        // Camera setup
        _cam = Camera.main;
        if (cameraPivot == null)
        {
            var pivot = new GameObject("FPS_CameraPivot");
            pivot.transform.SetParent(transform, false);
            pivot.transform.localPosition = new Vector3(0, eyeHeight, 0);
            pivot.transform.localRotation = Quaternion.identity;
            cameraPivot = pivot.transform;
        }

        if (_cam == null && createOwnCameraIfMissing)
        {
            var go = new GameObject("FPS_Camera");
            _cam = go.AddComponent<Camera>();
            _cam.tag = "MainCamera";
        }
        if (_cam != null)
        {
            _originalCamParent = _cam.transform.parent;
            _cam.transform.SetParent(cameraPivot, false);
            _cam.transform.localPosition = Vector3.zero;
            _cam.transform.localRotation = Quaternion.identity;
            _cam.fieldOfView = fov;

            var brain = _cam.GetComponent<CinemachineBrain>();
            if (brain != null) brain.enabled = false;
        }

        var fpc = GetComponent<FirstPersonCameraController>();
        if (fpc)
        {
            fpc.enabled = false; // avoid competing controls
        }
        var follow = FindObjectOfType<PlayerCameraFollow>();
        if (follow) follow.enabled = false;

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }

        // Initialize look state to current body yaw
        _yaw = transform.eulerAngles.y;
        _targetYaw = _yaw;
        _pitch = 0f;
        _targetPitch = 0f;

        _animSync = GetComponent<FPSAnimatorSync>();
        if (_animSync == null)
        {
            _animSync = gameObject.AddComponent<FPSAnimatorSync>();
        }

        _cameraBaseHeight = eyeHeight;
    }

    private void OnDisable()
    {
        if (_cam && _originalCamParent)
        {
            _cam.transform.SetParent(_originalCamParent, false);
        }
    }

    private void Update()
    {
        if (!_activeForThisClient) return;

        // Trigger quake
        if (Input.GetKeyDown(quakeKey))
        {
            StartQuake(quakeDuration);
        }

        UpdateQuake(Time.deltaTime);
        HandleLook();
        HandleMove();
    }

    private void HandleLook()
    {
        bool isMoving = (Mathf.Abs(Input.GetAxis("Vertical")) + Mathf.Abs(Input.GetAxis("Horizontal"))) > 0.01f;
        bool lookOverride = false;
#if ENABLE_INPUT_SYSTEM
        lookOverride = UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.rightButton.isPressed;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null)
        {
            var delta = mouse.delta.ReadValue();
            float dz = (isMoving && requireHoldToLookWhileMoving && !lookOverride) ? moveLookDeadzone : (isMoving ? moveLookDeadzone : lookDeadzone);
            if (!requireHoldToLookWhileMoving || lookOverride || !isMoving)
            {
                if (Mathf.Abs(delta.x) >= dz) _targetYaw   += delta.x * mouseSensitivity;
                if (Mathf.Abs(delta.y) >= dz) _targetPitch -= delta.y * mouseSensitivity;
            }
        }
#else
        bool rmb = Input.GetMouseButton(1);
        float dx = Input.GetAxis("Mouse X");
        float dy = Input.GetAxis("Mouse Y");
        float dz = (isMoving && requireHoldToLookWhileMoving && !rmb) ? 0.04f : (isMoving ? 0.04f : 0.015f);
        if (!requireHoldToLookWhileMoving || rmb || !isMoving)
        {
            if (Mathf.Abs(dx) >= dz) _targetYaw   += dx * (mouseSensitivity * 10f);
            if (Mathf.Abs(dy) >= dz) _targetPitch -= dy * (mouseSensitivity * 10f);
        }
#endif
        _targetPitch = Mathf.Clamp(_targetPitch, -maxPitch, maxPitch);

        _yaw   = Mathf.SmoothDampAngle(_yaw, _targetYaw, ref _yawVel, lookSmoothTime);
        _pitch = Mathf.SmoothDampAngle(_pitch, _targetPitch, ref _pitchVel, lookSmoothTime);

        if (cameraPivot)
        {
            // Apply pitch to camera pivot only; add shake rotational offset
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f) * Quaternion.Euler(_shakeRotOffset);
        }
        // Apply yaw to body directly (no dependence on pivot world euler)
        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
    }

    private void HandleMove()
    {
        bool grounded = _cc.isGrounded;
        if (grounded) _lastGroundedTime = Time.time;

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 input = new Vector3(h, 0f, v);
        input = Vector3.ClampMagnitude(input, 1f);

        // Camera-relative
        Vector3 camF = transform.forward; // body yaw already matches camera yaw
        Vector3 camR = transform.right;
        Vector3 desired = (camF * input.z + camR * input.x);

        bool sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float targetSpeed = sprint ? sprintSpeed : walkSpeed;
        Vector3 desiredVel = desired * targetSpeed;

        float smooth = (desired.sqrMagnitude > 0.001f) ? accelerationTime : decelerationTime;
        float smoothT = Mathf.Max(0.0001f, smooth);
        _moveVel = Vector3.SmoothDamp(_moveVel, desiredVel, ref _moveVelRef, smoothT);

        // Gravity and jump
        if (grounded)
        {
            _velocity.y = groundedGravity;
            if (Input.GetKeyDown(KeyCode.Space))
            {
                _velocity.y = Mathf.Sqrt(-2f * gravity * jumpHeight);
            }
        }
        else
        {
            // coyote time
            if ((Time.time - _lastGroundedTime) <= coyoteTime && Input.GetKeyDown(KeyCode.Space))
            {
                _velocity.y = Mathf.Sqrt(-2f * gravity * jumpHeight);
            }
            _velocity.y += gravity * Time.deltaTime;
            // limited air control
            _moveVel = Vector3.Lerp(_moveVel, desiredVel, airControl * Time.deltaTime);
        }

        // Crouch
        if (enableCrouch)
        {
            bool crouch = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
            float targetHeight = crouch ? crouchHeight : _standEyeHeight;
            float camY = Mathf.Lerp(cameraPivot.localPosition.y, targetHeight, Time.deltaTime * crouchLerp);
            _cameraBaseHeight = camY;
            float camZ = crouch ? crouchCameraForwardOffset : standCameraForwardOffset;
            cameraPivot.localPosition = new Vector3(0f, _cameraBaseHeight, camZ) + _shakePosOffset;
            _isCrouching = crouch;
            if (_isCrouching) _moveVel = Vector3.ClampMagnitude(_moveVel, crouchSpeed);
        }
        else
        {
            _cameraBaseHeight = _standEyeHeight;
            cameraPivot.localPosition = new Vector3(0f, _cameraBaseHeight, standCameraForwardOffset) + _shakePosOffset;
        }

        Vector3 motion = _moveVel * Time.deltaTime;
        motion.y = 0f; // horizontal motion only
        motion.y += _velocity.y * Time.deltaTime;
        _cc.Move(motion);

        // Determine and push animator parameters (moveX/moveY/isCrouching)
        // Map WASD to blend tree directly: X = strafe, Y = forward
        float moveX = Mathf.Clamp(input.x, -1f, 1f);
        float moveY = Mathf.Clamp(input.z, -1f, 1f);
        if (_animSync != null)
        {
            _animSync.SetMovement(moveX, moveY, _isCrouching);
        }
    }

    public void StartQuake(float duration)
    {
        _quakeTotal = Mathf.Max(0.01f, duration);
        _quakeTime = _quakeTotal;
        _shakeSeedX = Random.Range(0f, 1000f);
        _shakeSeedY = Random.Range(0f, 1000f);
        _shakeSeedZ = Random.Range(0f, 1000f);
    }

    private void UpdateQuake(float dt)
    {
        if (_quakeTime <= 0f)
        {
            _shakePosOffset = Vector3.zero;
            _shakeRotOffset = Vector3.zero;
            return;
        }

        _quakeTime -= dt;
        float progress = Mathf.Clamp01(1f - (_quakeTime / _quakeTotal)); // 0->1
        float damper = Mathf.Max(0f, quakeDamping.Evaluate(progress));    // 1->0
        float time = (_quakeTotal - _quakeTime);

        // Position noise
        float nx = (Mathf.PerlinNoise(_shakeSeedX, time * quakeFrequency) - 0.5f) * 2f;
        float ny = (Mathf.PerlinNoise(_shakeSeedY, (time + 13.37f) * quakeFrequency) - 0.5f) * 2f;
        float nz = (Mathf.PerlinNoise(_shakeSeedZ, (time + 29.17f) * quakeFrequency) - 0.5f) * 2f;
        _shakePosOffset = new Vector3(nx, ny, nz) * (quakePositionAmplitude * damper);

        // Rotation noise (degrees)
        float rx = (Mathf.PerlinNoise(_shakeSeedX + 101f, time * quakeFrequency) - 0.5f) * 2f;
        float ry = (Mathf.PerlinNoise(_shakeSeedY + 233f, (time + 7.77f) * quakeFrequency) - 0.5f) * 2f;
        float rz = (Mathf.PerlinNoise(_shakeSeedZ + 307f, (time + 19.19f) * quakeFrequency) - 0.5f) * 2f;
        _shakeRotOffset = new Vector3(rx, ry, rz) * (quakeRotationAmplitude * damper);
    }
}
