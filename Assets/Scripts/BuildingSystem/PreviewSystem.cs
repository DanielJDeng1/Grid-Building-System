using UnityEngine;

/// <summary>
/// Context manager for placement and deletion previews
/// Delegates visual rendering and transformation logic to active IPreviewState instances
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

    // Pre-allocated state instances to avoid GC pressure during tool switching
    private GridPreview _gridPreview;
    private EdgePreview _edgePreview;
    private GridRemovalPreview _gridRemovalPreview;
    private EdgeRemovalPreview _edgeRemovalPreview;
    private GridMultiPlacePreview _gridMultiPlacePreview;

    private void Awake()
    {
        _gridPreview = new GridPreview(_validMaterial, _invalidMaterial, _previewYOffset);
        _edgePreview = new EdgePreview(_validMaterial, _invalidMaterial, _previewYOffset);
        _gridRemovalPreview = new GridRemovalPreview(_validMaterial, _invalidMaterial, _previewYOffset);
        _edgeRemovalPreview = new EdgeRemovalPreview(_validMaterial, _invalidMaterial, _previewYOffset);
        _gridMultiPlacePreview = new GridMultiPlacePreview(_validMaterial, _invalidMaterial, _previewYOffset);
    }

    #region State Activation

    /// <summary>
    /// Displays single-tile placement preview for grid objects
    /// </summary>
    public void StartShowingGridPreview(GameObject prefab, Vector3 position)
    {
        StopShowingPreview();
        _currentState = _gridPreview;
        _currentState.StartShowingPreview(prefab, position);
    }

    /// <summary>
    /// Displays placement preview for wall and edge objects
    /// </summary>
    public void StartShowingEdgePreview(GameObject prefab, Vector3 position)
    {
        StopShowingPreview();
        _currentState = _edgePreview;
        _currentState.StartShowingPreview(prefab, position);
    }

    /// <summary>
    /// Displays deletion preview for single-tile grid objects
    /// </summary>
    public void StartShowingGridRemovalPreview(Vector3 position)
    {
        StopShowingPreview();
        _currentState = _gridRemovalPreview;
        _currentState.StartShowingPreview(null, position);
    }

    /// <summary>
    /// Displays deletion preview for edge objects using target mesh visual
    /// </summary>
    public void StartShowingEdgeRemovalPreview(GameObject prefab, Vector3 position)
    {
        StopShowingPreview();
        _currentState = _edgeRemovalPreview;
        _currentState.StartShowingPreview(prefab, position);
    }

    /// <summary>
    /// Displays rectangular bounding box preview for multi-tile batch placement or deletion
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
    /// Updates active preview transform and applies validation material feedback
    /// </summary>
    public void UpdatePosition(Vector3 position, bool isValid = true)
    {
        _currentState?.UpdatePosition(position, isValid);
    }

    /// <summary>
    /// Applies yaw rotation to the active preview around the target grid pivot
    /// </summary>
    public void UpdateRotation(Vector3 pivot)
    {
        _currentState?.RotatePreview(pivot);
    }

    /// <summary>
    /// Cleans up current preview instances and resets state context
    /// </summary>
    public void StopShowingPreview()
    {
        _currentState?.StopShowingPreview();
        _currentState = null;
    }

    #endregion

    #region Legacy Compatibility (Deprecated)

    [System.Obsolete("Use StartShowingGridPreview() instead")]
    public void StartShowingPlacementPreview(GameObject prefab)
    {
        StartShowingGridPreview(prefab, Vector3.zero);
    }

    [System.Obsolete("Use StartShowingGridRemovalPreview() instead")]
    public void StartShowingRemovePreview()
    {
        StartShowingGridRemovalPreview(Vector3.zero);
    }

    #endregion
}

/// <summary>
/// Renders dynamic area selection box during multi-cell drag operations
/// Scales a primitive cube across cells bounded by drag origin and active cursor cell
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

    public void StartShowingPreview(GameObject prefab, Vector3 position)
    {
        _originCell = Vector3Int.RoundToInt(position);

        // Batch preview uses primitive bounds scaling rather than instantiating individual prefabs
        _previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _previewObject.name = "GridMultiPlacePreview";

        _previewRenderer = _previewObject.GetComponent<Renderer>();

        Collider previewCollider = _previewObject.GetComponent<Collider>();
        if (previewCollider != null)
            previewCollider.enabled = false;

        ResizeToCells(_originCell, _originCell, true);
    }

    public void UpdatePosition(Vector3 position, bool isValid)
    {
        if (_previewObject == null)
            return;

        Vector3Int currentCell = Vector3Int.RoundToInt(position);
        ResizeToCells(_originCell, currentCell, isValid);
    }

    public void RotatePreview(Vector3 pivot)
    {
        // Unused: Area selection box is axis-aligned along grid cells
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

        // Use target cell Y (b.y) so vertical elevation updates immediately on floor level changes
        _previewObject.transform.position = new Vector3(centerX, b.y + _yOffset, centerZ);
        _previewObject.transform.localScale = new Vector3(sizeX, 0.1f, sizeZ);

        if (_previewRenderer != null)
        {
            _previewRenderer.sharedMaterial = isValid ? _validMaterial : _invalidMaterial;
        }
    }

    #endregion
}