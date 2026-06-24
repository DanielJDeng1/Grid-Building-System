using UnityEngine;

/// <summary>
/// State machine context for preview visualization system.
/// Delegates preview operations to the active IPreviewState implementation.
/// 
/// ARCHITECTURE PATTERN: State Pattern
/// - PreviewSystem: Context (this class)
/// - IPreviewState: State interface
/// - GridPreview, EdgePreview, etc.: Concrete states
/// 
/// MATERIAL SYSTEM:
/// Materials are configured in the Inspector and passed to states for valid/invalid feedback.
/// - validMaterial: Default appearance (white/green tint)
/// - invalidMaterial: Error appearance (red tint)
/// 
/// LIFECYCLE:
/// 1. Building state calls SetPreviewState() to activate appropriate preview
/// 2. UpdatePosition() called every frame with validity check
/// 3. StopShowingPreview() called when building state ends
/// 
/// INSPECTOR SETUP:
/// - Assign validMaterial (e.g., transparent white shader)
/// - Assign invalidMaterial (e.g., transparent red shader)
/// </summary>
public class PreviewSystem : MonoBehaviour
{
    [Header("Preview Materials")]
    [SerializeField] private Material _validMaterial;
    [SerializeField] private Material _invalidMaterial;

    [Header("Preview Offset")]
    [SerializeField] private float _previewYOffset = 0.06f;

    [SerializeField] private float _previewObjectScale = 1.1f;

    private IPreviewState _currentState;

    // Cached state instances to avoid repeated allocations
    private GridPreview _gridPreview;
    private EdgePreview _edgePreview;
    private GridRemovalPreview _gridRemovalPreview;
    private EdgeRemovalPreview _edgeRemovalPreview;

    private void Awake()
    {
        // Initialize all state instances with shared materials
        _gridPreview = new GridPreview(_validMaterial, _invalidMaterial, _previewYOffset);
        _edgePreview = new EdgePreview(_validMaterial, _invalidMaterial, _previewYOffset);
        _gridRemovalPreview = new GridRemovalPreview(_validMaterial, _invalidMaterial, _previewYOffset);
        _edgeRemovalPreview = new EdgeRemovalPreview(_validMaterial, _invalidMaterial, _previewYOffset);
    }

    #region State Activation

    /// <summary>
    /// Activates grid object placement preview.
    /// </summary>
    public void StartShowingGridPreview(GameObject prefab, Vector3 position)
    {
        StopShowingPreview();
        _currentState = _gridPreview;
        _currentState.StartShowingPreview(prefab, position);
    }

    /// <summary>
    /// Activates edge object placement preview.
    /// </summary>
    public void StartShowingEdgePreview(GameObject prefab, Vector3 position)
    {
        StopShowingPreview();
        _currentState = _edgePreview;
        _currentState.StartShowingPreview(prefab, position);
    }

    /// <summary>
    /// Activates grid object removal preview (red indicator cube).
    /// </summary>
    public void StartShowingGridRemovalPreview(Vector3 position)
    {
        StopShowingPreview();
        _currentState = _gridRemovalPreview;
        _currentState.StartShowingPreview(null, position);
    }

    /// <summary>
    /// Activates edge object removal preview.
    /// Uses the provided prefab with red material to indicate deletion target.
    /// </summary>
    public void StartShowingEdgeRemovalPreview(GameObject prefab, Vector3 position)
    {
        StopShowingPreview();
        _currentState = _edgeRemovalPreview;
        _currentState.StartShowingPreview(prefab, position);
    }

    #endregion

    #region State Delegation

    /// <summary>
    /// Updates preview position and validity indicator.
    /// Called every frame by building states.
    /// </summary>
    /// <param name="position">World position for preview</param>
    /// <param name="isValid">Whether placement is valid at this position</param>
    public void UpdatePosition(Vector3 position, bool isValid = true)
    {
        _currentState?.UpdatePosition(position, isValid);
    }

    /// <summary>
    /// Rotates the preview around the specified pivot.
    /// Called when player presses rotation key (R).
    /// </summary>
    public void UpdateRotation(Vector3 pivot)
    {
        _currentState?.RotatePreview(pivot);

    }

    /// <summary>
    /// Destroys current preview and clears state.
    /// Called when building state ends.
    /// </summary>
    public void StopShowingPreview()
    {
        _currentState?.StopShowingPreview();
        _currentState = null;
    }

    #endregion

    #region Legacy Compatibility (Deprecated)

    /// <summary>
    /// DEPRECATED: Legacy method for backward compatibility.
    /// Use StartShowingGridPreview() instead.
    /// </summary>
    [System.Obsolete("Use StartShowingGridPreview() instead")]
    public void StartShowingPlacementPreview(GameObject prefab)
    {
        StartShowingGridPreview(prefab, Vector3.zero);
    }

    /// <summary>
    /// DEPRECATED: Legacy method for backward compatibility.
    /// Use StartShowingGridRemovalPreview() instead.
    /// </summary>
    [System.Obsolete("Use StartShowingGridRemovalPreview() instead")]
    public void StartShowingRemovePreview()
    {
        StartShowingGridRemovalPreview(Vector3.zero);
    }

    #endregion
}
