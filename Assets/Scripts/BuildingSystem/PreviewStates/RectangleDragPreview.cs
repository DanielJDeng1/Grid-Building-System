using UnityEngine;

/// <summary>
/// Preview state for multi-placement of grid objects (floors and ceilings only).
/// Displays a green bounding box scaled to fit the dragged rectangle.
/// 
/// VISUAL DESIGN:
/// - Simple primitive cube scaled to rectangle dimensions
/// - Always uses validMaterial (green/white) - no validity checking
/// - Semi-transparent to see underlying grid
/// - Height is thin (0.2 units) for clear visual feedback
/// 
/// OVERRIDE BEHAVIOR:
/// Multi-placement has no "invalid" state - it removes conflicts and places new objects.
/// Preview simply shows the operation area, not validity.
/// 
/// PERFORMANCE:
/// - Single primitive cube created once
/// - Scale/position updated per frame during drag (minimal overhead)
/// - Uses shared material (no GC allocation)
/// </summary>
public class MultiPlacementPreview : IPreviewState
{
    private GameObject _previewObject;
    private Material _validMaterial;
    private float _yOffset;
    private Renderer _previewRenderer;

    private const float BOX_HEIGHT = 0.2f;

    public MultiPlacementPreview(Material validMaterial, float yOffset)
    {
        _validMaterial = validMaterial;
        _yOffset = yOffset;
    }

    public void StartShowingPreview(GameObject prefab, Vector3 position)
    {
        // Create bounding box cube
        _previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _previewObject.name = "MultiPlacementPreview";

        // Cache renderer
        _previewRenderer = _previewObject.GetComponent<Renderer>();
        if (_previewRenderer != null)
        {
            _previewRenderer.sharedMaterial = _validMaterial;
        }

        // Disable collider to prevent raycast interference
        Collider collider = _previewObject.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
        }

        // Initial position (will be updated by UpdateBounds)
        _previewObject.transform.position = position + Vector3.up * _yOffset;
        _previewObject.transform.localScale = new Vector3(1f, BOX_HEIGHT, 1f);
    }

    public void UpdatePosition(Vector3 position, bool isValid)
    {
        // Multi-placement doesn't use validity checking
        // Position updates are handled by UpdateBounds instead
        if (_previewObject == null)
            return;

        // Update Y-level for build height changes
        Vector3 currentPos = _previewObject.transform.position;
        _previewObject.transform.position = new Vector3(
            currentPos.x,
            position.y + _yOffset,
            currentPos.z
        );
    }

    public void RotatePreview(Vector3 pivot)
    {
        // Bounding box doesn't rotate - no-op
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

    /// <summary>
    /// Updates bounding box scale and position to match dragged rectangle.
    /// </summary>
    /// <param name="start">Drag start grid position</param>
    /// <param name="end">Current drag end grid position</param>
    public void UpdateBounds(Vector3Int start, Vector3Int end)
    {
        if (_previewObject == null)
            return;

        // Calculate rectangle dimensions (inclusive)
        int width = Mathf.Abs(end.x - start.x) + 1;
        int depth = Mathf.Abs(end.z - start.z) + 1;

        // Calculate center position (world space)
        // +0.5f to center on tile grid
        float centerX = (start.x + end.x) / 2f + 0.5f;
        float centerZ = (start.z + end.z) / 2f + 0.5f;
        float centerY = start.y + _yOffset; // Use start.y (current build height)

        // Update scale and position
        _previewObject.transform.localScale = new Vector3(width, BOX_HEIGHT, depth);
        _previewObject.transform.position = new Vector3(centerX, centerY, centerZ);
    }
}

/// <summary>
/// Preview state for multi-deletion of grid objects.
/// Displays a red bounding box scaled to fit the dragged rectangle.
/// 
/// VISUAL DESIGN:
/// - Simple primitive cube scaled to rectangle dimensions
/// - Always uses invalidMaterial (red) to indicate deletion
/// - Semi-transparent to see objects being deleted
/// - Height is thin (0.2 units) for clear visual feedback
/// 
/// PRIORITY-BASED DELETION:
/// Preview shows deletion area. Actual deletion uses priority system:
/// Furniture → Floor → Ceiling (per tile, independently)
/// 
/// PERFORMANCE:
/// - Single primitive cube created once
/// - Scale/position updated per frame during drag (minimal overhead)
/// - Uses shared material (no GC allocation)
/// </summary>
public class MultiDeletionPreview : IPreviewState
{
    private GameObject _previewObject;
    private Material _invalidMaterial;
    private float _yOffset;
    private Renderer _previewRenderer;

    private const float BOX_HEIGHT = 0.2f;

    public MultiDeletionPreview(Material invalidMaterial, float yOffset)
    {
        _invalidMaterial = invalidMaterial;
        _yOffset = yOffset;
    }

    public void StartShowingPreview(GameObject prefab, Vector3 position)
    {
        // Create bounding box cube
        _previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _previewObject.name = "MultiDeletionPreview";

        // Cache renderer
        _previewRenderer = _previewObject.GetComponent<Renderer>();
        if (_previewRenderer != null)
        {
            _previewRenderer.sharedMaterial = _invalidMaterial;
        }

        // Disable collider to prevent raycast interference
        Collider collider = _previewObject.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
        }

        // Initial position (will be updated by UpdateBounds)
        _previewObject.transform.position = position + Vector3.up * _yOffset;
        _previewObject.transform.localScale = new Vector3(1f, BOX_HEIGHT, 1f);
    }

    public void UpdatePosition(Vector3 position, bool isValid)
    {
        // Multi-deletion doesn't use validity checking
        // Position updates are handled by UpdateBounds instead
        if (_previewObject == null)
            return;

        // Update Y-level for build height changes
        Vector3 currentPos = _previewObject.transform.position;
        _previewObject.transform.position = new Vector3(
            currentPos.x,
            position.y + _yOffset,
            currentPos.z
        );
    }

    public void RotatePreview(Vector3 pivot)
    {
        // Bounding box doesn't rotate - no-op
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

    /// <summary>
    /// Updates bounding box scale and position to match dragged rectangle.
    /// </summary>
    /// <param name="start">Drag start grid position</param>
    /// <param name="end">Current drag end grid position</param>
    public void UpdateBounds(Vector3Int start, Vector3Int end)
    {
        if (_previewObject == null)
            return;

        // Calculate rectangle dimensions (inclusive)
        int width = Mathf.Abs(end.x - start.x) + 1;
        int depth = Mathf.Abs(end.z - start.z) + 1;

        // Calculate center position (world space)
        // +0.5f to center on tile grid
        float centerX = (start.x + end.x) / 2f + 0.5f;
        float centerZ = (start.z + end.z) / 2f + 0.5f;
        float centerY = start.y + _yOffset; // Use start.y (current build height)

        // Update scale and position
        _previewObject.transform.localScale = new Vector3(width, BOX_HEIGHT, depth);
        _previewObject.transform.position = new Vector3(centerX, centerY, centerZ);
    }
}
