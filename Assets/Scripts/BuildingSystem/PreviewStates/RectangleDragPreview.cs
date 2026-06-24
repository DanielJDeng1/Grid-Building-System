using UnityEngine;

/// <summary>
/// Helper class for visualizing rectangular drag selections during multi-placement/deletion.
/// Creates a dynamic plane GameObject that scales to cover the dragged area.
/// 
/// VISUAL FEEDBACK:
/// - Green/white material: Valid selection (all tiles can be placed/removed)
/// - Red material: Invalid selection (one or more tiles blocked)
/// 
/// PERFORMANCE:
/// - Single GameObject (1 draw call)
/// - Reuses same instance throughout drag
/// - Scale operation: negligible CPU cost
/// - Uses shared materials (no GC allocation)
/// 
/// LIFECYCLE:
/// 1. StartDrag() - Creates plane at drag start position
/// 2. UpdateDrag() - Scales plane to cover current selection
/// 3. EndDrag() - Destroys plane GameObject
/// </summary>
public class RectangleDragPreview
{
    private GameObject _rectangleObject;
    private Renderer _renderer;
    private Material _validMaterial;
    private Material _invalidMaterial;
    private float _yOffset;

    /// <summary>
    /// Initializes the rectangular preview at the drag start position.
    /// </summary>
    /// <param name="worldStart">World position where drag began</param>
    /// <param name="validMat">Material to show when selection is valid</param>
    /// <param name="invalidMat">Material to show when selection is invalid</param>
    /// <param name="yOffset">Y offset above ground to prevent z-fighting</param>
    public void StartDrag(Vector3 worldStart, Material validMat, Material invalidMat, float yOffset)
    {
        // Create primitive plane (10x10 units default size)
        _rectangleObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
        _rectangleObject.name = "MultiPlacementPreview";
        
        // Disable collider to prevent raycast interference
        Object.Destroy(_rectangleObject.GetComponent<Collider>());
        
        // Cache renderer for material swapping
        _renderer = _rectangleObject.GetComponent<Renderer>();
        
        // Position at start with Y offset
        _rectangleObject.transform.position = worldStart + Vector3.up * yOffset;
        
        // Store materials for validity feedback
        _validMaterial = validMat;
        _invalidMaterial = invalidMat;
        _yOffset = yOffset;
        
        // Apply initial valid material
        _renderer.sharedMaterial = _validMaterial;
    }

    /// <summary>
    /// Updates the rectangle to span from start to end position.
    /// Automatically scales to cover all tiles in the selection.
    /// </summary>
    /// <param name="worldStart">World position of drag start tile</param>
    /// <param name="worldEnd">World position of current mouse tile</param>
    /// <param name="isValid">Whether the entire selection is valid for placement/removal</param>
    public void UpdateDrag(Vector3 worldStart, Vector3 worldEnd, bool isValid)
    {
        if (_rectangleObject == null)
            return;

        // Calculate center point of rectangle
        Vector3 center = (worldStart + worldEnd) * 0.5f;
        
        // Calculate dimensions (+1 to include both start and end tiles)
        float width = Mathf.Abs(worldEnd.x - worldStart.x) + 1f;
        float depth = Mathf.Abs(worldEnd.z - worldStart.z) + 1f;

        // Unity Plane primitive is 10x10 units, so scale by 0.1
        _rectangleObject.transform.position = new Vector3(center.x, worldStart.y + _yOffset, center.z);
        _rectangleObject.transform.localScale = new Vector3(width * 0.1f, 1f, depth * 0.1f);

        // Update material based on validity
        _renderer.sharedMaterial = isValid ? _validMaterial : _invalidMaterial;
    }

    /// <summary>
    /// Destroys the rectangular preview GameObject.
    /// Called when drag ends (mouse release or building mode exit).
    /// </summary>
    public void EndDrag()
    {
        if (_rectangleObject != null)
        {
            Object.Destroy(_rectangleObject);
            _rectangleObject = null;
            _renderer = null;
        }
    }

    /// <summary>
    /// Checks if the preview is currently active.
    /// </summary>
    public bool IsActive => _rectangleObject != null;
}
