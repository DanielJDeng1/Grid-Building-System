using UnityEngine;

/// <summary>
/// Preview state for edge-based object removal (walls, fences, railings).
/// Displays a thin plane indicator at the edge position to show removal target.
/// 
/// VISUAL FEEDBACK:
/// - Valid removal (edge exists): invalidMaterial (red) to indicate deletion
/// - Invalid removal (no edge): No preview or transparent indicator
/// 
/// DESIGN RATIONALE:
/// Uses a thin plane primitive positioned at edge locations. Unlike grid removal
/// (which uses a cube on the tile), edge removal needs a directional indicator
/// that aligns with the edge being removed.
/// 
/// EDGE POSITIONING:
/// The plane is positioned at edge midpoints with appropriate rotation:
/// - Horizontal edges (Deg0): Plane aligned along X-axis
/// - Vertical edges (Deg90): Plane aligned along Z-axis
/// 
/// ROTATION:
/// Removal operations don't require rotation, but the preview orientation
/// can be updated if edge detection logic changes orientation.
/// 
/// PERFORMANCE:
/// - Single plane primitive (minimal geometry)
/// - Material swap uses shared materials (no GC)
/// - Destroyed when state ends
/// </summary>
public class EdgeRemovalPreview : IPreviewState
{
    private GameObject _previewObject;
    private Material _validMaterial;
    private Material _invalidMaterial;
    private float _yOffset;

    private Renderer _previewRenderer;

    public EdgeRemovalPreview(Material validMaterial, Material invalidMaterial, float yOffset)
    {
        _validMaterial = validMaterial;
        _invalidMaterial = invalidMaterial;
        _yOffset = yOffset;
    }

    public void StartShowingPreview(GameObject prefab, Vector3 position)
    {
        // Removal preview doesn't use the prefab parameter (always uses primitive plane)
        CreateRemovalIndicator(position);
    }

    public void UpdatePosition(Vector3 position, bool isValid)
    {
        if (_previewObject == null)
            return;

        // Update position with Y offset
        _previewObject.transform.position = new Vector3(
            position.x,
            position.y + _yOffset,
            position.z
        );

        // Show red indicator when hovering over removable edge
        if (_previewRenderer != null)
        {
            _previewRenderer.sharedMaterial = isValid ? _invalidMaterial : _validMaterial;
        }
    }

    public void RotatePreview(Vector3 pivot)
    {
        // Edge removal preview can optionally rotate to show different edge orientations
        // Currently a no-op, but can be implemented if removal logic detects edge direction
        if (_previewObject == null)
            return;

        // Rotate 90 degrees to toggle between horizontal/vertical edge indicator
        _previewObject.transform.RotateAround(_previewObject.transform.position, Vector3.up, 90f);
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
    /// Creates a thin plane primitive as the edge removal indicator.
    /// Positioned and scaled to represent an edge boundary.
    /// </summary>
    private void CreateRemovalIndicator(Vector3 position)
    {
        _previewObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
        _previewObject.name = "EdgeRemovalPreview";
        
        // Position at edge location with Y offset
        _previewObject.transform.position = new Vector3(
            position.x,
            position.y + _yOffset,
            position.z
        );

        // Scale to represent an edge (thin plane along one axis)
        // Default plane is 10x10, scale down to match grid size
        // Rotate 90 degrees on X-axis to make it vertical-facing
        _previewObject.transform.localScale = new Vector3(0.1f, 1f, 0.2f);
        _previewObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

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
