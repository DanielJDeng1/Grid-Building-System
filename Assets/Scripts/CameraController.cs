using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

/// <summary>
/// CAMERA + BUILD HEIGHT INTEGRATION:
/// Subscribes to PlacementSystem.OnBuildHeightChanged and moves the rig's
/// vertical position toward the new floor's WORLD-SPACE height whenever the
/// player changes build height (Page Up/Down), using Mathf.SmoothDamp for a
/// simple, predictable ease rather than hand-rolled decay math.
/// 
/// INSPECTOR SETUP (REQUIRED for height-follow):
/// Assign a PlacementSystem reference in _placementSystem. If left
/// unassigned, this logs a warning on enable and the camera simply never
/// changes height - it does not silently half-work.
/// </summary>
public class BuilderCameraController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float normalMoveSpeed = 20f;
    [SerializeField] private float sprintMoveSpeed = 40f;

    [Header("Yaw Rotation")]
    [SerializeField] private float yawSpeed = 100f; 
    [SerializeField] private float yawSnappiness = 15f;

    [Header("Pitch")]
    [SerializeField] private float scrollSensitivity = 15f;
    [SerializeField] private float zoomSnappiness = 10f;
    
    [Tooltip("0: Fully Zoomed In / 1: Fully Zoomed Out")]
    [Range(0f, 1f)] 
    [SerializeField] private float initialZoomProgress = 0.5f;

    [Header("Zoom Bounds")]
    [SerializeField] private float minPitchAngle = 25f;   
    [SerializeField] private float maxPitchAngle = 75f;   
    [SerializeField] private float minCameraDepth = 5f;   
    [SerializeField] private float maxCameraDepth = 40f;  

    [Header("Multi-Level Build Height Follow")]
    [Tooltip("REQUIRED for the camera to follow build height changes. Leaving this empty logs a warning and disables height-follow entirely.")]
    [SerializeField] private PlacementSystem _placementSystem;
    [Tooltip("Seconds for the rig to reach the new floor height (Mathf.SmoothDamp convention - lower is snappier).")]
    [SerializeField] private float heightFollowSmoothTime = 0.35f;

    private InputSystem.Controls inputActions;
    private CinemachineCamera vCam;
    private Vector2 moveInput;
    private float yawInput; 
    private bool isSprinting;

    private float targetYaw;
    private float targetZoomProfile; 

    private float currentYaw;
    private float currentZoomProfile;

    // MULTI-LEVEL: target world-space Y the rig eases toward, and the
    // velocity reference SmoothDamp needs to track between frames.
    private float _targetHeight;
    private float _heightVelocity;

    private void Awake()
    {
        inputActions = new InputSystem.Controls();
        vCam = GetComponentInChildren<CinemachineCamera>();

        inputActions.Camera.Pitch.performed += ctx => OnScrollInput(ctx.ReadValue<float>());
    }

    private void OnEnable()
    {
        inputActions.Camera.Enable();

        if (_placementSystem != null)
        {
            _placementSystem.OnBuildHeightChanged += HandleBuildHeightChanged;
        }
        else
        {
            Debug.LogWarning("BuilderCameraController: _placementSystem is not assigned - " +
                              "the camera will NOT follow build height changes. Assign it in the Inspector.");
        }
    }

    private void OnDisable()
    {
        inputActions.Camera.Disable();

        if (_placementSystem != null)
            _placementSystem.OnBuildHeightChanged -= HandleBuildHeightChanged;
    }

    private void Start()
    {
        currentYaw = transform.eulerAngles.y;
        targetYaw = currentYaw;

        currentZoomProfile = initialZoomProgress;
        targetZoomProfile = initialZoomProgress;

        // Start the height target at the rig's current position so it
        // doesn't jump on the very first frame.
        _targetHeight = transform.position.y;
    }

    /// <summary>
    /// Called whenever PlacementSystem's build height changes. worldHeight is
    /// already in world space (PlacementSystem converts via Grid.CellToWorld),
    /// so this class never needs to know about grid cell sizing.
    /// </summary>
    private void HandleBuildHeightChanged(float worldHeight)
    {
        _targetHeight = worldHeight;
    }

    private void OnScrollInput(float value)
    {
        if (Mathf.Abs(value) < 0.01f) return;
        
        float scrollDirection = -Mathf.Sign(value);
        targetZoomProfile += scrollDirection * (scrollSensitivity * 0.01f);
        targetZoomProfile = Mathf.Clamp01(targetZoomProfile);
    }

    private void Update()
    {
        moveInput = inputActions.Camera.Move.ReadValue<Vector2>();
        yawInput = inputActions.Camera.Yaw.ReadValue<float>();
        
        isSprinting = inputActions.Camera.Sprint.IsPressed(); 

        if (Mathf.Abs(yawInput) > 0.01f)
        {
            targetYaw += Mathf.Sign(yawInput) * yawSpeed * Time.deltaTime;
        }

        HandleTransitions();
        HandleRigMovement();
        HandleHeightFollow();
    }

    private void HandleTransitions()
    {
        float yawDecay = 1f - Mathf.Exp(-yawSnappiness * Time.deltaTime);
        float zoomDecay = 1f - Mathf.Exp(-zoomSnappiness * Time.deltaTime);

        currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, yawDecay);
        currentZoomProfile = Mathf.Lerp(currentZoomProfile, targetZoomProfile, zoomDecay);

        transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);

        float activePitch = Mathf.Lerp(minPitchAngle, maxPitchAngle, currentZoomProfile);
        float activeDepth = Mathf.Lerp(minCameraDepth, maxCameraDepth, currentZoomProfile);

        if (vCam != null)
        {
            Quaternion localRotation = Quaternion.Euler(activePitch, 0f, 0f);
            Vector3 offsetVector = localRotation * new Vector3(0f, 0f, -activeDepth);

            vCam.transform.localPosition = offsetVector;
            vCam.transform.localRotation = localRotation;
        }
    }

    private void HandleRigMovement()
    {
        if (moveInput.sqrMagnitude < 0.01f) return;

        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        Vector3 movementDirection = (forward * moveInput.y) + (right * moveInput.x);
        
        float activeSpeed = isSprinting ? sprintMoveSpeed : normalMoveSpeed;

        transform.position += movementDirection.normalized * activeSpeed * Time.deltaTime;
    }

    /// <summary>
    /// MULTI-LEVEL: eases the rig's world-space Y toward _targetHeight (set
    /// via HandleBuildHeightChanged) using Mathf.SmoothDamp. Runs after
    /// HandleRigMovement, which only ever touches X/Z (forward/right are
    /// flattened to y=0), so there's no fight over which method owns the
    /// rig's vertical position.
    /// </summary>
    private void HandleHeightFollow()
    {
        Vector3 position = transform.position;
        position.y = Mathf.SmoothDamp(position.y, _targetHeight, ref _heightVelocity, heightFollowSmoothTime);
        transform.position = position;
    }
}