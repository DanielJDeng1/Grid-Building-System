using UnityEngine;

/// <summary>
/// Preview state for grid-based object placement.
/// Handles instantiation, material swaps for placement validity, and rotation around tile pivots.
/// </summary>
public class GridPreview : IPreviewState
{
    private GameObject _previewObject;
    private Material _validMaterial;
    private Material _invalidMaterial;
    private float _yOffset;
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

        _previewRenderers = _previewObject.GetComponentsInChildren<Renderer>();
        ApplyMaterial(_validMaterial);
        DisableColliders(_previewObject);
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

        Vector3 tileCenterPivot = new Vector3(pivot.x + 0.5f, pivot.y, pivot.z + 0.5f);

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