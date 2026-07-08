using UnityEngine;

/// <summary>
/// State machine context for preview visualization system.
/// Delegates preview operations to the active IPreviewState implementation.
/// 
/// ARCHITECTURE PATTERN: State Pattern
/// - PreviewSystem: Context (this class)
/// - IPreviewState: State interface
/// - GridPreview, EdgePreview, GridRemovalPreview, EdgeRemovalPreview,
///   GridMultiPlacePreview: Concrete states
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
    private GridMultiPlacePreview _gridMultiPlacePreview;

    private void Awake()
    {
        // Initialize all state instances with shared materials
        _gridPreview = new GridPreview(_validMaterial, _invalidMaterial, _previewYOffset);
        _edgePreview = new EdgePreview(_validMaterial, _invalidMaterial, _previewYOffset);
        _gridRemovalPreview = new GridRemovalPreview(_validMaterial, _invalidMaterial, _previewYOffset);
        _edgeRemovalPreview = new EdgeRemovalPreview(_validMaterial, _invalidMaterial, _previewYOffset);
        _gridMultiPlacePreview = new GridMultiPlacePreview(_validMaterial, _invalidMaterial, _previewYOffset);
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

    /// <summary>
    /// Activates the rectangle drag-bounds preview used by grid multi-placement
    /// and multi-removal. originPosition is the drag's starting cell, in world
    /// space (typically Grid.CellToWorld(dragOriginCell)).
    /// </summary>
    public void StartShowingGridMultiPlacePreview(Vector3 originPosition)
    {
        StopShowingPreview();
        _currentState = _gridMultiPlacePreview;
        _currentState.StartShowingPreview(null, originPosition);
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

/// <summary>
/// Preview state for rectangle drag-fill placement and removal of grid objects.
/// Displays a single resizable cube spanning the drag rectangle between the
/// drag origin (set via StartShowingPreview) and the current cursor cell
/// (set via each UpdatePosition call).
///
/// DESIGN RATIONALE:
/// Reuses the existing valid/invalid materials rather than introducing new
/// Inspector fields. Implements IPreviewState so it plugs into PreviewSystem's
/// existing state-pattern machinery unchanged - no changes to the interface
/// or to PreviewSystem's delegation logic were required.
///
/// ASSUMPTION:
/// Grid cell size is 1 world unit in X/Z, matching the tile-center (+0.5)
/// convention already used by GridRemovalPreview and GridSnapToView. If your
/// Grid's cellSize differs, the cube math below needs a matching cellSize
/// multiplier.
/// </summary>
public class GridMultiPlacePreview : IPreviewState
{
    private GameObject _previewObject;
    private Material _validMaterial;
    private Material _invalidMaterial;
    private float _yOffset;

    private Renderer _previewRenderer;
    private Vector3Int _originCell;

    public GridMultiPlacePreview(Material validMaterial, Material invalidMaterial, float yOffset)
    {
        _validMaterial = validMaterial;
        _invalidMaterial = invalidMaterial;
        _yOffset = yOffset;
    }

    /// <summary>
    /// Begins the drag preview. The prefab parameter is intentionally unused -
    /// the bounds preview is always a primitive cube, never the actual object
    /// prefab, since it may span many cells with different objects underneath.
    /// </summary>
    public void StartShowingPreview(GameObject prefab, Vector3 position)
    {
        _originCell = Vector3Int.RoundToInt(position);

        _previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _previewObject.name = "GridMultiPlacePreview";

        _previewRenderer = _previewObject.GetComponent<Renderer>();

        Collider previewCollider = _previewObject.GetComponent<Collider>();
        if (previewCollider != null)
            previewCollider.enabled = false;

        ResizeToCells(_originCell, _originCell, true);
    }

    /// <summary>
    /// Resizes and repositions the cube to span from the drag origin to the
    /// cell nearest the given world position, and swaps the valid/invalid
    /// material based on isValid.
    /// </summary>
    public void UpdatePosition(Vector3 position, bool isValid)
    {
        if (_previewObject == null)
            return;

        Vector3Int currentCell = Vector3Int.RoundToInt(position);
        ResizeToCells(_originCell, currentCell, isValid);
    }

    public void RotatePreview(Vector3 pivot)
    {
        // Rectangle drag previews don't rotate - out of scope for this phase.
    }

    public void StopShowingPreview()
    {
        if (_previewObject != null)
        {
            Object.Destroy(_previewObject);
            _previewObject = null;
            _previewRenderer = null;
        }
    }

    #region Helper Methods

    private void ResizeToCells(Vector3Int a, Vector3Int b, bool isValid)
    {
        int minX = Mathf.Min(a.x, b.x);
        int maxX = Mathf.Max(a.x, b.x);
        int minZ = Mathf.Min(a.z, b.z);
        int maxZ = Mathf.Max(a.z, b.z);

        int sizeX = (maxX - minX) + 1;
        int sizeZ = (maxZ - minZ) + 1;

        float centerX = minX + (sizeX * 0.5f);
        float centerZ = minZ + (sizeZ * 0.5f);

        // BUG FIX: previously used `a.y` (the drag origin's height, captured
        // once at StartShowingPreview and never revisited), so if build
        // height changed while this preview was active, the cube's vertical
        // position stayed frozen at the OLD height. `b` is always the most
        // recent cell passed into UpdatePosition, so using b.y here means
        // the cube's height updates immediately whenever build height
        // changes - whether that change comes from mouse movement or from
        // PlacementSystem recomputing the position after a Page Up/Down.
        _previewObject.transform.position = new Vector3(centerX, b.y + _yOffset, centerZ);
        _previewObject.transform.localScale = new Vector3(sizeX, 0.1f, sizeZ);

        if (_previewRenderer != null)
        {
            _previewRenderer.sharedMaterial = isValid ? _validMaterial : _invalidMaterial;
        }
    }

    #endregion
}