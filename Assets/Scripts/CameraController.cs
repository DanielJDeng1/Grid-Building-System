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
    
    [Tooltip("0: Fully Zoomed In / 1: Fully Zoomed Out")]
    [Range(0f, 1f)] 
    [SerializeField] private float initialZoomProgress = 0.5f;

    [Header("Zoom Bounds")]
    [SerializeField] private float minPitchAngle = 25f;   
    [SerializeField] private float maxPitchAngle = 75f;   
    [SerializeField] private float minCameraDepth = 5f;   
    [SerializeField] private float maxCameraDepth = 40f;  

    private InputSystem.Controls inputActions;
    private CinemachineCamera vCam;
    private Vector2 moveInput;
    private float yawInput; 
    private bool isSprinting;

    private float targetYaw;
    private float targetZoomProfile; 

    private float currentYaw;
    private float currentZoomProfile;

    private void Awake()
    {
        inputActions = new InputSystem.Controls();
        vCam = GetComponentInChildren<CinemachineCamera>();

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
        moveInput = inputActions.Camera.Move.ReadValue<Vector2>();
        yawInput = inputActions.Camera.Yaw.ReadValue<float>();
        
        isSprinting = inputActions.Camera.Sprint.IsPressed(); 

        if (Mathf.Abs(yawInput) > 0.01f)
        {
            targetYaw += Mathf.Sign(yawInput) * yawSpeed * Time.deltaTime;
        }

        HandleTransitions();
        HandleRigMovement();
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
}