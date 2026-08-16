using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

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
    
    [Range(0f, 1f)] 
    [SerializeField] private float initialZoomProgress = 0.5f;

    [Header("Zoom Bounds")]
    [SerializeField] private float minPitchAngle = 25f;   
    [SerializeField] private float maxPitchAngle = 75f;   
    [SerializeField] private float minCameraDepth = 5f;   
    [SerializeField] private float maxCameraDepth = 40f;  

    [Header("Multi Level Build Height Follow")]
    [SerializeField] private PlacementSystem _placementSystem;
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
            _placementSystem.OnBuildHeightChanged += HandleBuildHeightChanged;
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
        _targetHeight = transform.position.y;
    }

    private void HandleBuildHeightChanged(float worldHeight)
    {
        _targetHeight = worldHeight;
    }

    private void OnScrollInput(float value)
    {
        if (Mathf.Abs(value) < 0.01f) return;
        float scrollDirection = -Mathf.Sign(value);
        targetZoomProfile = Mathf.Clamp01(targetZoomProfile + scrollDirection * (scrollSensitivity * 0.01f));
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
            vCam.transform.localPosition = localRotation * new Vector3(0f, 0f, -activeDepth);
            vCam.transform.localRotation = localRotation;
        }
    }

    private void HandleRigMovement()
    {
        if (moveInput.sqrMagnitude < 0.01f) return;

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        Vector3 movementDirection = (forward * moveInput.y) + (right * moveInput.x);
        
        transform.position += movementDirection.normalized * (isSprinting ? sprintMoveSpeed : normalMoveSpeed) * Time.deltaTime;
    }

    private void HandleHeightFollow()
    {
        Vector3 position = transform.position;
        position.y = Mathf.SmoothDamp(position.y, _targetHeight, ref _heightVelocity, heightFollowSmoothTime);
        transform.position = position;
    }
}