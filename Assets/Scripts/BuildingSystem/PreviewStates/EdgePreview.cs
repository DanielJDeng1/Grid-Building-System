using UnityEngine;

/// <summary>
/// Preview state for edge-based object placement (walls, fences, railings).
/// Displays a semi-transparent preview with color-coded validity feedback.
/// 
/// VISUAL FEEDBACK:
/// - Valid placement: validMaterial applied (white/green tint)
/// - Invalid placement: invalidMaterial applied (red tint)
/// 
/// ROTATION (FIXED):
/// Edge objects use absolute rotation to match ObjectPlacer behavior:
/// - Deg0: 0° rotation (horizontal alignment along positive X-axis)
/// - Deg90: -90° rotation (vertical alignment along negative Z-axis)
/// 
/// Previous implementation used incremental rotation (+90° per call) which caused
/// desynchronization between preview and placed object rotation.
/// 
/// PERFORMANCE:
/// - Preview instantiated once per state activation
/// - Material swap uses shared materials (no GC allocation)
/// - Colliders disabled to prevent unintended interactions
/// </summary>
public class EdgePreview : IPreviewState
{
    private GameObject _previewObject;
    private Material _validMaterial;
    private Material _invalidMaterial;
    private float _yOffset;

    // Cache renderer references for efficient material swapping
    private Renderer[] _previewRenderers;

    // ROTATION FIX: Track rotation state to match ObjectPlacer's absolute rotation
    private EdgeRotation _currentRotation = EdgeRotation.Deg0;

    public EdgePreview(Material validMaterial, Material invalidMaterial, float yOffset)
    {
        _validMaterial = validMaterial;
        _invalidMaterial = invalidMaterial;
        _yOffset = yOffset;
    }

    public void StartShowingPreview(GameObject prefab, Vector3 position)
    {
        if (prefab == null)
        {
            Debug.LogWarning("EdgePreview: Cannot create preview from null prefab");
            return;
        }

        _previewObject = Object.Instantiate(prefab);
        _previewObject.transform.position = position + Vector3.up * _yOffset;

        // Cache all renderers for efficient material operations
        _previewRenderers = _previewObject.GetComponentsInChildren<Renderer>();

        // Apply initial valid material
        ApplyMaterial(_validMaterial);

        // Disable colliders to prevent physics interactions with preview
        DisableColliders(_previewObject);

        // Reset rotation state
        _currentRotation = EdgeRotation.Deg0;
    }

    public void UpdatePosition(Vector3 position, bool isValid)
    {
        if (_previewObject == null)
            return;

        // Update position with Y offset for visual clarity
        _previewObject.transform.position = new Vector3(
            position.x,
            position.y + _yOffset,
            position.z
        );

        // Update material based on placement validity
        ApplyMaterial(isValid ? _validMaterial : _invalidMaterial);
    }

    public void RotatePreview(Vector3 pivot)
    {
        if (_previewObject == null)
            return;

        // ROTATION FIX: Toggle rotation state
        _currentRotation = (EdgeRotation)(((int)_currentRotation + 1) % 2);

        // Apply absolute rotation to match ObjectPlacer.PlaceEdge() behavior
        // Deg0: 0° (horizontal - along positive X-axis)
        // Deg90: -90° (vertical - along negative Z-axis)
        float targetRotation = _currentRotation == EdgeRotation.Deg0 ? 0f : -90f;
        _previewObject.transform.rotation = Quaternion.Euler(0f, targetRotation, 0f);
    }

    public void StopShowingPreview()
    {
        if (_previewObject != null)
        {
            Object.Destroy(_previewObject);
            _previewObject = null;
            _previewRenderers = null;
        }

        // Reset rotation state
        _currentRotation = EdgeRotation.Deg0;
    }

    #region Helper Methods

    /// <summary>
    /// Applies material to all cached renderers.
    /// Uses shared material to avoid creating material instances (GC allocation).
    /// </summary>
    private void ApplyMaterial(Material material)
    {
        if (_previewRenderers == null || material == null)
            return;

        foreach (var renderer in _previewRenderers)
        {
            if (renderer != null)
            {
                // sharedMaterial avoids instantiation, preventing GC pressure
                renderer.sharedMaterial = material;
            }
        }
    }

    /// <summary>
    /// Disables all colliders on the preview GameObject and its children.
    /// Prevents the preview from interfering with raycasts or physics.
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