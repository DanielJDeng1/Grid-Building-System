using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

public class BuilderCameraController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float normalMoveSpeed = 20f;
    [SerializeField] private float sprintMoveSpeed = 40f;

    [Header("Yaw (Q / E Hold to Rotate)")]
    [SerializeField] private float yawSpeed = 100f; 
    [SerializeField] private float yawSnappiness = 15f;

    [Header("Dynamic Pitch & Zoom (Scroll Wheel)")]
    [SerializeField] private float scrollSensitivity = 15f;
    [SerializeField] private float zoomSnappiness = 10f;
    
    [Tooltip("0 = Fully Zoomed In (Close & Flat) | 1 = Fully Zoomed Out (High & Overlooking)")]
    [Range(0f, 1f)] 
    [SerializeField] private float initialZoomProgress = 0.5f;

    [Header("Zoom Bounds Calibration")]
    [SerializeField] private float minPitchAngle = 25f;   
    [SerializeField] private float maxPitchAngle = 75f;   
    [SerializeField] private float minCameraDepth = 5f;   
    [SerializeField] private float maxCameraDepth = 40f;  

    private InputSystem.Controls inputActions;
    private CinemachineCamera vCam;
    private Vector2 moveInput;
    private float yawInput; 
    private bool isSprinting;

    // Targeted states
    private float targetYaw;
    private float targetZoomProfile; 

    // Current states during transitions
    private float currentYaw;
    private float currentZoomProfile;

    private void Awake()
    {
        inputActions = new InputSystem.Controls();
        vCam = GetComponentInChildren<CinemachineCamera>();

        // Scroll wheel event hook
        inputActions.Camera.Pitch.performed += ctx => OnScrollInput(ctx.ReadValue<float>());
    }

    private void OnEnable() => inputActions.Camera.Enable();
    private void OnDisable() => inputActions.Camera.Disable();

    private void Start()
    {
        currentYaw = transform.eulerAngles.y;
        targetYaw = currentYaw;

        currentZoomProfile = initialZoomProgress;
        targetZoomProfile = initialZoomProgress;
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
        // 1. READ CONTINUOUS INPUTS
        moveInput = inputActions.Camera.Move.ReadValue<Vector2>();
        yawInput = inputActions.Camera.Yaw.ReadValue<float>();
        
        // Read button state (returns true if held down)
        isSprinting = inputActions.Camera.Sprint.IsPressed(); 

        // 2. PROCESS YAW INPUT
        if (Mathf.Abs(yawInput) > 0.01f)
        {
            targetYaw += Mathf.Sign(yawInput) * yawSpeed * Time.deltaTime;
        }

        // 3. APPLY SMOOTH TRANSITIONS & POSITIONING
        HandleFluidTransitions();
        HandleRigMovement();
    }

    private void HandleFluidTransitions()
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
        
        // Switch execution speed baseline dynamically based on key state
        float activeSpeed = isSprinting ? sprintMoveSpeed : normalMoveSpeed;

        transform.position += movementDirection.normalized * activeSpeed * Time.deltaTime;
    }
}