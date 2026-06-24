using UnityEngine;

/// <summary>
/// Preview state for edge-based object removal (walls, fences, railings).
/// Displays a semi-transparent preview with red material to indicate deletion target.
/// 
/// VISUAL FEEDBACK:
/// - Valid removal (edge exists): invalidMaterial applied (red tint)
/// - Invalid removal (no edge): validMaterial applied (white/transparent)
/// 
/// ROTATION:
/// Edge removal previews toggle between two orientations to match placement:
/// - Deg0: Horizontal (along positive X-axis) - 0° rotation
/// - Deg90: Vertical (along negative Z-axis) - -90° rotation
/// 
/// DESIGN RATIONALE:
/// Uses the actual edge prefab (with translucent material) to show exactly
/// what will be removed, unlike grid removal which uses a primitive cube.
/// 
/// PERFORMANCE:
/// - Preview instantiated once per state activation
/// - Material swap uses shared materials (no GC allocation)
/// - Colliders disabled to prevent unintended interactions
/// </summary>
public class EdgeRemovalPreview : IPreviewState
{
    private GameObject _previewObject;
    private Material _validMaterial;
    private Material _invalidMaterial;
    private float _yOffset;

    // Cache renderer references for efficient material swapping
    private Renderer[] _previewRenderers;

    // Track rotation state to toggle between 0° and -90°
    private bool _isRotated = false;

    public EdgeRemovalPreview(Material validMaterial, Material invalidMaterial, float yOffset)
    {
        _validMaterial = validMaterial;
        _invalidMaterial = invalidMaterial;
        _yOffset = yOffset;
    }

    public void StartShowingPreview(GameObject prefab, Vector3 position)
    {
        if (prefab == null)
        {
            Debug.LogWarning("EdgeRemovalPreview: Cannot create preview from null prefab");
            return;
        }

        _previewObject = Object.Instantiate(prefab);
        _previewObject.transform.position = position + Vector3.up * _yOffset;

        // Cache all renderers for efficient material operations
        _previewRenderers = _previewObject.GetComponentsInChildren<Renderer>();

        // Apply initial invalid material (red indicator for deletion)
        ApplyMaterial(_invalidMaterial);

        // Disable colliders to prevent physics interactions with preview
        DisableColliders(_previewObject);

        // Reset rotation state
        _isRotated = false;
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

        // Update material based on removal validity
        // isValid = true means edge exists and can be removed (show red)
        // isValid = false means no edge exists (show transparent/white)
        ApplyMaterial(isValid ? _invalidMaterial : _validMaterial);
    }

    public void RotatePreview(Vector3 pivot)
    {
        if (_previewObject == null)
            return;

        // Toggle rotation state
        _isRotated = !_isRotated;

        // Set absolute rotation: 0° or -90°
        float targetRotation = _isRotated ? -90f : 0f;
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

        // Reset rotation state for next preview
        _isRotated = false;
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
