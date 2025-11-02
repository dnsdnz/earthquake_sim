using UnityEngine;
using Unity.Netcode;
using Unity.Cinemachine;

/// Simple local camera mode switcher for first-person view.
/// Attach to the player prefab. Assign a head/eyes anchor if available.
public class FirstPersonCameraController : MonoBehaviour
{
    [Header("Binding")]
    [Tooltip("Anchor point for the first-person camera (e.g., a Head/CameraPivot). If null, one will be created at eye height.")]
    public Transform headAnchor;

    [Header("Mode")]
    public bool startInFirstPerson = true;
    public KeyCode toggleKey = KeyCode.V;

    [Header("First Person Settings")]
    public float eyeHeight = 1.6f;
    public float firstPersonFov = 75f;
    [Tooltip("Base sensitivity multiplier for mouse look (deg/pixel for new input system; scaled for legacy input)")]
    public float mouseSensitivity = 0.8f;
    [Tooltip("Time (seconds) to smooth yaw/pitch toward target; lower is snappier, higher is smoother")]
    public float lookSmoothTime = 0.10f;
    [Tooltip("Maximum up/down pitch (degrees)")]
    public float maxPitch = 85f;

    [Header("Stability While Moving")]
    [Tooltip("Ignore tiny mouse jitter every frame (pixels) when not moving")] public float lookDeadzone = 0.5f;
    [Tooltip("Stronger deadzone (pixels) while moving to keep a straight path")] public float moveLookDeadzone = 1.5f;
    [Tooltip("Require RMB to look around while moving (helps walk straight)")] public bool requireHoldToLookWhileMoving = false;
    public bool lockCursor = true;

    [Header("Third Person Settings")]
    public float thirdPersonFov = 60f;

    [Header("Visibility")]
    [Tooltip("Renderers to hide while in first-person (e.g., head/helmet).")]
    public Renderer[] hideOnFirstPerson = System.Array.Empty<Renderer>();

    private Camera _mainCamera;
    private Transform _originalParent;
    private Vector3 _originalLocalPos;
    private Quaternion _originalLocalRot;
    private float _originalNearClip;
    private CinemachineBrain _brain;

    private bool _isFirstPerson;
    private float _yaw;          // current smoothed yaw
    private float _pitch;        // current smoothed pitch
    private float _targetYaw;    // target yaw from raw input
    private float _targetPitch;  // target pitch from raw input
    private float _yawVel;       // damping velocity (internal)
    private float _pitchVel;     // damping velocity (internal)

    private NetworkObject _netObj;
    private bool _isLocalOwner = true;

    private void Start()
    {
        _netObj = GetComponentInParent<NetworkObject>();
        if (_netObj != null && NetworkManager.Singleton != null)
        {
            _isLocalOwner = _netObj.IsOwner && NetworkManager.Singleton.IsListening;
        }

        _mainCamera = Camera.main;
        if (_mainCamera == null)
        {
            Debug.LogWarning("Main Camera not found.");
            enabled = false;
            return;
        }

        _brain = _mainCamera.GetComponent<CinemachineBrain>();
        _originalParent = _mainCamera.transform.parent;
        _originalLocalPos = _mainCamera.transform.localPosition;
        _originalLocalRot = _mainCamera.transform.localRotation;
        _originalNearClip = _mainCamera.nearClipPlane;

        if (headAnchor == null)
        {
            var tagged = FindChildWithTag(transform, "CinemachineTarget");
            if (tagged != null)
            {
                headAnchor = tagged;
            }
            else
            {
                var pivot = new GameObject("FirstPersonPivot");
                pivot.transform.SetParent(transform, false);
                pivot.transform.localPosition = new Vector3(0f, eyeHeight, 0f);
                pivot.transform.localRotation = Quaternion.identity;
                headAnchor = pivot.transform;
            }
        }

        AutoPopulateHideListIfEmpty();

        if (_isLocalOwner && startInFirstPerson)
        {
            EnterFirstPerson();
        }
        else
        {
            ExitFirstPerson();
        }
    }

    private void Update()
    {
        if (!_isLocalOwner || _mainCamera == null) return;

#if ENABLE_INPUT_SYSTEM
        bool togglePressed = UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.vKey.wasPressedThisFrame;
#else
        bool togglePressed = Input.GetKeyDown(toggleKey);
#endif
        if (togglePressed)
        {
            if (_isFirstPerson) ExitFirstPerson(); else EnterFirstPerson();
        }

        if (_isFirstPerson && headAnchor != null)
        {
            bool isMoving = Mathf.Abs(Input.GetAxis("Vertical")) > 0.01f || Mathf.Abs(Input.GetAxis("Horizontal")) > 0.01f;
            bool lookOverride = false;
#if ENABLE_INPUT_SYSTEM
            lookOverride = UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.rightButton.isPressed;
#else
            lookOverride = Input.GetMouseButton(1);
#endif
            // Read raw input and accumulate target angles
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                var delta = mouse.delta.ReadValue();
                float dz = isMoving && !lookOverride ? moveLookDeadzone : lookDeadzone;
                if (Mathf.Abs(delta.x) >= dz) { _targetYaw   += delta.x * mouseSensitivity; }
                if (Mathf.Abs(delta.y) >= dz) { _targetPitch -= delta.y * mouseSensitivity; }
            }
#else
            // Legacy input returns scaled per-frame deltas; keep small multiplier
            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");
            float dz = isMoving && !lookOverride ? 0.04f : 0.015f; // tuned thresholds for legacy input
            if (Mathf.Abs(dx) >= dz) { _targetYaw   += dx * (mouseSensitivity * 10f); }
            if (Mathf.Abs(dy) >= dz) { _targetPitch -= dy * (mouseSensitivity * 10f); }
#endif
            _targetPitch = Mathf.Clamp(_targetPitch, -maxPitch, maxPitch);

            // Smooth toward target angles
            _yaw   = Mathf.SmoothDampAngle(_yaw, _targetYaw, ref _yawVel, lookSmoothTime);
            _pitch = Mathf.SmoothDampAngle(_pitch, _targetPitch, ref _pitchVel, lookSmoothTime);

            headAnchor.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
    }

    private void EnterFirstPerson()
    {
        _isFirstPerson = true;
        if (_brain != null) _brain.enabled = false;
        if (lockCursor) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        // Parent the camera to head anchor
        _mainCamera.transform.SetParent(headAnchor, false);
        _mainCamera.transform.localPosition = Vector3.zero;
        _mainCamera.transform.localRotation = Quaternion.identity;
        _mainCamera.fieldOfView = firstPersonFov;
        _mainCamera.nearClipPlane = 0.02f;

        SetRenderersVisible(false);
    }

    private void ExitFirstPerson()
    {
        _isFirstPerson = false;
        if (_brain != null) _brain.enabled = true;
        if (lockCursor) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

        // Restore camera parent and pose; Cinemachine will take over
        _mainCamera.transform.SetParent(_originalParent, false);
        _mainCamera.transform.localPosition = _originalLocalPos;
        _mainCamera.transform.localRotation = _originalLocalRot;
        _mainCamera.fieldOfView = thirdPersonFov;
        _mainCamera.nearClipPlane = _originalNearClip;

        SetRenderersVisible(true);
    }

    private void SetRenderersVisible(bool visible)
    {
        if (hideOnFirstPerson == null) return;
        foreach (var r in hideOnFirstPerson)
        {
            if (r == null) continue;
            r.enabled = visible;
        }
    }

    private void AutoPopulateHideListIfEmpty()
    {
        if (hideOnFirstPerson != null && hideOnFirstPerson.Length > 0) return;
        var renderers = GetComponentsInChildren<Renderer>(true);
        var candidates = new System.Collections.Generic.List<Renderer>();
        foreach (var r in renderers)
        {
            var n = r.gameObject.name.ToLowerInvariant();
            if (n.Contains("head") || n.Contains("helmet") || n.Contains("hat"))
            {
                candidates.Add(r);
            }
        }
        hideOnFirstPerson = candidates.ToArray();
    }

    private static Transform FindChildWithTag(Transform root, string tag)
    {
        if (root.CompareTag(tag)) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = FindChildWithTag(root.GetChild(i), tag);
            if (c != null) return c;
        }
        return null;
    }
}
