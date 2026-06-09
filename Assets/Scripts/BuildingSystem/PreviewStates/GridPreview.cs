using UnityEngine;

/// <summary>
/// Preview state for grid-based object placement.
/// Displays a semi-transparent preview of the object with color-coded validity feedback.
/// 
/// VISUAL FEEDBACK:
/// - Valid placement: validMaterial applied (white/green tint)
/// - Invalid placement: invalidMaterial applied (red tint)
/// 
/// ROTATION:
/// Grid objects rotate 90° around the tile center pivot.
/// Maintains visual consistency with ObjectPlacer rotation logic.
/// 
/// PERFORMANCE:
/// - Preview GameObject instantiated once per state activation
/// - Material swap uses shared materials (no instantiation or GC allocation)
/// - Destroyed when StopShowingPreview() called
/// </summary>
public class GridPreview : IPreviewState
{
    private GameObject _previewObject;
    private Material _validMaterial;
    private Material _invalidMaterial;
    private float _yOffset;

    // Cache all renderer references to avoid GetComponentsInChildren calls each frame
    private Renderer[] _previewRenderers;

    public GridPreview(Material validMaterial, Material invalidMaterial, float yOffset)
    {
        _validMaterial = validMaterial;
        _invalidMaterial = invalidMaterial;
        _yOffset = yOffset;
    }

    public void StartShowingPreview(GameObject prefab, Vector3 position)
    {
        if (prefab == null)
        {
            Debug.LogWarning("GridPreview: Cannot create preview from null prefab");
            return;
        }

        _previewObject = Object.Instantiate(prefab);
        _previewObject.transform.position = position + Vector3.up * _yOffset;

        // Cache all renderers for efficient material swapping
        _previewRenderers = _previewObject.GetComponentsInChildren<Renderer>();

        // Apply initial valid material
        ApplyMaterial(_validMaterial);

        // Disable colliders on preview to prevent physics interactions
        DisableColliders(_previewObject);
    }

    public void UpdatePosition(Vector3 position, bool isValid)
    {
        if (_previewObject == null)
            return;

        // Update position with Y offset to hover above ground
        _previewObject.transform.position = new Vector3(
            position.x,
            position.y + _yOffset,
            position.z
        );

        // Update material based on validity
        ApplyMaterial(isValid ? _validMaterial : _invalidMaterial);
    }

    public void RotatePreview(Vector3 pivot)
    {
        if (_previewObject == null)
            return;

        // Calculate pivot at tile center (matching ObjectPlacer logic)
        Vector3 tileCenterPivot = new Vector3(pivot.x + 0.5f, pivot.y, pivot.z + 0.5f);

        // Rotate all children around the tile center by 90 degrees
        foreach (Transform child in _previewObject.transform)
        {
            child.transform.RotateAround(tileCenterPivot, Vector3.up, 90f);
        }
    }

    public void StopShowingPreview()
    {
        if (_previewObject != null)
        {
            Object.Destroy(_previewObject);
            _previewObject = null;
            _previewRenderers = null;
        }
    }

    #region Helper Methods

    /// <summary>
    /// Applies material to all cached renderers.
    /// Uses shared material to avoid instantiation and GC allocation.
    /// </summary>
    private void ApplyMaterial(Material material)
    {
        if (_previewRenderers == null || material == null)
            return;

        foreach (var renderer in _previewRenderers)
        {
            if (renderer != null)
            {
                // Use sharedMaterial to avoid creating material instances
                renderer.sharedMaterial = material;
            }
        }
    }

    /// <summary>
    /// Disables all colliders on the preview object to prevent physics interactions.
    /// </summary>
    private void DisableColliders(GameObject obj)
    {
        Collider[] colliders = obj.GetComponentsInChildren<Collider>();
        foreach (var collider in colliders)
        {
            collider.enabled = false;
        }
    }

    #endregion
}
