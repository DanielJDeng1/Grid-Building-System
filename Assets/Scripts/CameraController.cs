using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using System.Collections.Generic;

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

    [Header("Multi-Level Build Height Follow")]
    [SerializeField] private PlacementSystem _placementSystem;
    [SerializeField] private float heightFollowSmoothTime = 0.35f;

    [Header("Wall Occlusion Hiding")]
    [SerializeField] private LayerMask occlusionLayerMask;
    [SerializeField] private Transform focusPoint;
    [Tooltip("The horizontal half-width and vertical half-height of the upright sweeping box.")]
    [SerializeField] private float occlusionVolumeRadius = 0.5f;
    
    [Tooltip("The camera pitch angle (in degrees) below which wall hiding is allowed. If pitch is higher than this, walls stay visible.")]
    [SerializeField] private float fadeAngleThreshold = 50f;

    private InputSystem.Controls inputActions;
    private CinemachineCamera vCam;
    private Camera mainCam;
    private Vector2 moveInput;
    private float yawInput; 
    private bool isSprinting;

    private float targetYaw;
    private float targetZoomProfile; 
    private float currentYaw;
    private float currentZoomProfile;
    private float _targetHeight;
    private float _heightVelocity;

    private readonly HashSet<Renderer> _currentlyHiddenRenderers = new HashSet<Renderer>();
    private readonly HashSet<Renderer> _hitThisFrame = new HashSet<Renderer>();
    private readonly List<Renderer> _restoreList = new List<Renderer>();

    // Gizmo Caching Variables
    private Vector3 _gizmoStart;
    private Vector3 _gizmoEnd;
    private Quaternion _gizmoRotation;
    private bool _canDrawGizmo;

    private void Awake()
    {
        inputActions = new InputSystem.Controls();
        vCam = GetComponentInChildren<CinemachineCamera>();
        mainCam = Camera.main;

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

        RestoreAllHiddenRenderers();
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

    private void LateUpdate()
    {
        HandleOcclusionToggle();
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

    private void HandleOcclusionToggle()
    {
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return;

        float currentPitchAngle = mainCam.transform.eulerAngles.x;
        if (currentPitchAngle > 180f) currentPitchAngle -= 360f;

        // Pitch exceeded threshold; restore all renderers and stop occlusion checks
        if (currentPitchAngle > fadeAngleThreshold)
        {
            _canDrawGizmo = false;
            _hitThisFrame.Clear();
            ProcessOcclusionState();
            return;
        }

        Vector3 rayStart = mainCam.transform.position;
        Vector3 rayDirection = mainCam.transform.forward;
        Vector3 targetPos = focusPoint != null ? focusPoint.position : transform.position;

        Quaternion uprightRotation = Quaternion.Euler(0f, currentYaw, 0f);
        float targetDistance = Vector3.Distance(rayStart, targetPos);

        _hitThisFrame.Clear();

        if (targetDistance > 0.2f)
        {
            Vector3 boxHalfExtents = new Vector3(occlusionVolumeRadius, occlusionVolumeRadius, 0.01f);
            Vector3 correctedStart = rayStart + (rayDirection * 0.1f);
            float castLength = targetDistance - 0.2f;

            _gizmoStart = correctedStart;
            _gizmoEnd = correctedStart + (rayDirection * castLength);
            _gizmoRotation = uprightRotation;
            _canDrawGizmo = true;

            RaycastHit[] hits = Physics.BoxCastAll(
                correctedStart, 
                boxHalfExtents, 
                rayDirection, 
                uprightRotation, 
                castLength, 
                occlusionLayerMask
            );

            Plane cutoffPlane = new Plane(-rayDirection, targetPos);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider col = hits[i].collider;
                if (col == null) continue;

                if (!cutoffPlane.GetSide(col.bounds.min) && !cutoffPlane.GetSide(col.bounds.center))
                {
                    continue;
                }

                // Target ONLY renderers directly on the hit chunk collider
                Renderer[] structuralRenderers = col.GetComponentsInChildren<Renderer>();

                foreach (Renderer hitRenderer in structuralRenderers)
                {
                    if (hitRenderer == null) continue;

                    _hitThisFrame.Add(hitRenderer);

                    if (!_currentlyHiddenRenderers.Contains(hitRenderer))
                    {
                        hitRenderer.enabled = false;
                        _currentlyHiddenRenderers.Add(hitRenderer);
                    }
                }
            }
        }
        else
        {
            _canDrawGizmo = false;
        }

        ProcessOcclusionState();
    }

    private void ProcessOcclusionState()
    {
        _restoreList.Clear();

        foreach (Renderer renderer in _currentlyHiddenRenderers)
        {
            if (renderer == null)
            {
                _restoreList.Add(renderer);
                continue;
            }

            if (!_hitThisFrame.Contains(renderer))
            {
                renderer.enabled = true;
                _restoreList.Add(renderer);
            }
        }

        foreach (Renderer renderer in _restoreList)
        {
            _currentlyHiddenRenderers.Remove(renderer);
        }
    }

    private void RestoreAllHiddenRenderers()
    {
        foreach (Renderer renderer in _currentlyHiddenRenderers)
        {
            if (renderer != null)
            {
                renderer.enabled = true;
            }
        }
        _currentlyHiddenRenderers.Clear();
    }

    private void OnDrawGizmos()
    {
        if (!_canDrawGizmo || !Application.isPlaying || mainCam == null) return;

        Gizmos.color = Color.cyan;
        Gizmos.matrix = Matrix4x4.TRS(_gizmoStart, _gizmoRotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(occlusionVolumeRadius * 2f, occlusionVolumeRadius * 2f, 0.02f));

        Gizmos.color = Color.red;
        Gizmos.matrix = Matrix4x4.TRS(_gizmoEnd, _gizmoRotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(occlusionVolumeRadius * 2f, occlusionVolumeRadius * 2f, 0.02f));
        
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(_gizmoStart, _gizmoEnd);
    }
}