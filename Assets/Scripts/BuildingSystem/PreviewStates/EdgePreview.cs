using UnityEngine;

/// <summary>
/// Preview state for edge-based object placement (walls, fences, railings).
/// Handles material updates based on placement validity and absolute rotation alignment.
/// </summary>
public class EdgePreview : IPreviewState
{
    private GameObject _previewObject;
    private Material _validMaterial;
    private Material _invalidMaterial;
    private float _yOffset;
    private Renderer[] _previewRenderers;
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

        _previewRenderers = _previewObject.GetComponentsInChildren<Renderer>();
        ApplyMaterial(_validMaterial);
        DisableColliders(_previewObject);

        _currentRotation = EdgeRotation.Deg0;
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

        ApplyMaterial(isValid ? _validMaterial : _invalidMaterial);
    }

    public void RotatePreview(Vector3 pivot)
    {
        if (_previewObject == null)
            return;

        _currentRotation = (EdgeRotation)(((int)_currentRotation + 1) % 2);

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

        _currentRotation = EdgeRotation.Deg0;
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