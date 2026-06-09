using UnityEngine;

/// <summary>
/// Preview state for grid-based object removal.
/// Displays a simple visual indicator (cube) at the tile position to show removal target.
/// 
/// VISUAL FEEDBACK:
/// - Valid removal (object exists): invalidMaterial (red) to indicate deletion
/// - Invalid removal (no object): No preview shown or transparent indicator
/// 
/// DESIGN RATIONALE:
/// Unlike placement previews that show the actual object, removal previews use
/// a simple primitive shape since the actual placed object already exists in the scene.
/// The preview serves only as a targeting indicator.
/// 
/// ROTATION:
/// Removal operations don't require rotation, so RotatePreview() is a no-op.
/// 
/// PERFORMANCE:
/// - Primitive cube created once (minimal vertices/triangles)
/// - Single material swap per frame
/// - Destroyed immediately when state ends
/// </summary>
public class GridRemovalPreview : IPreviewState
{
    private GameObject _previewObject;
    private Material _validMaterial;
    private Material _invalidMaterial;
    private float _yOffset;

    private Renderer _previewRenderer;

    public GridRemovalPreview(Material validMaterial, Material invalidMaterial, float yOffset)
    {
        _validMaterial = validMaterial;
        _invalidMaterial = invalidMaterial;
        _yOffset = yOffset;
    }

    public void StartShowingPreview(GameObject prefab, Vector3 position)
    {
        // Removal preview doesn't use the prefab parameter (always uses primitive cube)
        CreateRemovalIndicator(position);
    }

    public void UpdatePosition(Vector3 position, bool isValid)
    {
        if (_previewObject == null)
            return;

        // Update position with Y offset
        _previewObject.transform.position = new Vector3(
            position.x + 0.5f, // Center on tile
            position.y + _yOffset,
            position.z + 0.5f  // Center on tile
        );

        // Show red indicator when hovering over removable object
        // Could optionally hide preview entirely when isValid is false
        if (_previewRenderer != null)
        {
            _previewRenderer.sharedMaterial = isValid ? _invalidMaterial : _validMaterial;
        }
    }

    public void RotatePreview(Vector3 pivot)
    {
        // Removal previews don't rotate - no-op
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

    /// <summary>
    /// Creates a simple cube primitive as the removal indicator.
    /// Cube dimensions match a single grid tile for clear visual feedback.
    /// </summary>
    private void CreateRemovalIndicator(Vector3 position)
    {
        _previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _previewObject.name = "GridRemovalPreview";
        
        // Position at tile center with Y offset
        _previewObject.transform.position = new Vector3(
            position.x + 0.5f,
            position.y + _yOffset,
            position.z + 0.5f
        );

        // Scale to tile size (Unity grid default is 1x1)
        _previewObject.transform.localScale = new Vector3(1f, 0.1f, 1f);

        // Cache renderer for material swapping
        _previewRenderer = _previewObject.GetComponent<Renderer>();

        // Apply initial invalid material (red indicator for deletion)
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
    }

    #endregion
}
