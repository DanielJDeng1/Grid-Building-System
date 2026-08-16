using UnityEngine;

/// <summary>
/// Preview state for grid-based object removal.
/// Displays a primitive cube indicator at the targeted tile position.
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
        CreateRemovalIndicator(position);
    }

    public void UpdatePosition(Vector3 position, bool isValid)
    {
        if (_previewObject == null)
            return;

        _previewObject.transform.position = new Vector3(
            position.x + 0.5f,
            position.y + _yOffset,
            position.z + 0.5f
        );

        if (_previewRenderer != null)
        {
            _previewRenderer.sharedMaterial = isValid ? _invalidMaterial : _validMaterial;
        }
    }

    public void RotatePreview(Vector3 pivot)
    {
        // Removal indicators do not rotate
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

    private void CreateRemovalIndicator(Vector3 position)
    {
        _previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _previewObject.name = "GridRemovalPreview";

        _previewObject.transform.position = new Vector3(
            position.x + 0.5f,
            position.y + _yOffset,
            position.z + 0.5f
        );

        _previewObject.transform.localScale = new Vector3(1f, 0.1f, 1f);

        _previewRenderer = _previewObject.GetComponent<Renderer>();
        if (_previewRenderer != null)
        {
            _previewRenderer.sharedMaterial = _invalidMaterial;
        }

        Collider collider = _previewObject.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
        }
    }
}