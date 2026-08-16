using UnityEngine;

/// <summary>
/// Preview state for edge-based object removal (walls, fences, railings).
/// Displays a semi-transparent preview indicating deletion target and handles orientation toggling.
/// </summary>
public class EdgeRemovalPreview : IPreviewState
{
    private GameObject _previewObject;
    private Material _validMaterial;
    private Material _invalidMaterial;
    private float _yOffset;
    private Renderer[] _previewRenderers;
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

        _previewRenderers = _previewObject.GetComponentsInChildren<Renderer>();
        ApplyMaterial(_invalidMaterial);
        DisableColliders(_previewObject);

        _isRotated = false;
    }

    public void UpdatePosition(Vector3 position, bool isValid)
    {
        if (_previewObject == null)
            return;

        _previewObject.transform.position = new Vector3(
            position.x,
            position.y + _yOffset,
            position.z
        );

        ApplyMaterial(isValid ? _invalidMaterial : _validMaterial);
    }

    public void RotatePreview(Vector3 pivot)
    {
        if (_previewObject == null)
            return;

        _isRotated = !_isRotated;

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

        _isRotated = false;
    }

    private void ApplyMaterial(Material material)
    {
        if (_previewRenderers == null || material == null)
            return;

        foreach (var renderer in _previewRenderers)
        {
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }
    }

    private void DisableColliders(GameObject obj)
    {
        Collider[] colliders = obj.GetComponentsInChildren<Collider>();
        foreach (var collider in colliders)
        {
            collider.enabled = false;
        }
    }
}