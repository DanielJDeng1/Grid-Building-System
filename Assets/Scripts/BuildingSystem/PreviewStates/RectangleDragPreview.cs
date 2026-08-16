using UnityEngine;

/// <summary>
/// Preview state for multi-placement rectangle selection (floors and ceilings).
/// Displays a semi-transparent box scaled to fit the selected grid area.
/// </summary>
public class MultiPlacementPreview : IPreviewState
{
    private GameObject _previewObject;
    private Material _validMaterial;
    private float _yOffset;
    private Renderer _previewRenderer;

    private const float BOX_HEIGHT = 0.2f;

    public MultiPlacementPreview(Material validMaterial, float yOffset)
    {
        _validMaterial = validMaterial;
        _yOffset = yOffset;
    }

    public void StartShowingPreview(GameObject prefab, Vector3 position)
    {
        _previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _previewObject.name = "MultiPlacementPreview";

        _previewRenderer = _previewObject.GetComponent<Renderer>();
        if (_previewRenderer != null)
        {
            _previewRenderer.sharedMaterial = _validMaterial;
        }

        Collider collider = _previewObject.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
        }

        _previewObject.transform.position = position + Vector3.up * _yOffset;
        _previewObject.transform.localScale = new Vector3(1f, BOX_HEIGHT, 1f);
    }

    public void UpdatePosition(Vector3 position, bool isValid)
    {
        if (_previewObject == null)
            return;

        Vector3 currentPos = _previewObject.transform.position;
        _previewObject.transform.position = new Vector3(
            currentPos.x,
            position.y + _yOffset,
            currentPos.z
        );
    }

    public void RotatePreview(Vector3 pivot)
    {
        // Selection bounding box does not rotate
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

    /// <summary>
    /// Updates bounding box scale and position to match the dragged rectangle coordinates.
    /// </summary>
    public void UpdateBounds(Vector3Int start, Vector3Int end)
    {
        if (_previewObject == null)
            return;

        int width = Mathf.Abs(end.x - start.x) + 1;
        int depth = Mathf.Abs(end.z - start.z) + 1;

        float centerX = (start.x + end.x) / 2f + 0.5f;
        float centerZ = (start.z + end.z) / 2f + 0.5f;
        float centerY = start.y + _yOffset;

        _previewObject.transform.localScale = new Vector3(width, BOX_HEIGHT, depth);
        _previewObject.transform.position = new Vector3(centerX, centerY, centerZ);
    }
}

/// <summary>
/// Preview state for multi-deletion rectangle selection.
/// Displays a semi-transparent red box scaled to fit the selected grid area.
/// </summary>
public class MultiDeletionPreview : IPreviewState
{
    private GameObject _previewObject;
    private Material _invalidMaterial;
    private float _yOffset;
    private Renderer _previewRenderer;

    private const float BOX_HEIGHT = 0.2f;

    public MultiDeletionPreview(Material invalidMaterial, float yOffset)
    {
        _invalidMaterial = invalidMaterial;
        _yOffset = yOffset;
    }

    public void StartShowingPreview(GameObject prefab, Vector3 position)
    {
        _previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _previewObject.name = "MultiDeletionPreview";

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

        _previewObject.transform.position = position + Vector3.up * _yOffset;
        _previewObject.transform.localScale = new Vector3(1f, BOX_HEIGHT, 1f);
    }

    public void UpdatePosition(Vector3 position, bool isValid)
    {
        if (_previewObject == null)
            return;

        Vector3 currentPos = _previewObject.transform.position;
        _previewObject.transform.position = new Vector3(
            currentPos.x,
            position.y + _yOffset,
            currentPos.z
        );
    }

    public void RotatePreview(Vector3 pivot)
    {
        // Selection bounding box does not rotate
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

    /// <summary>
    /// Updates bounding box scale and position to match the dragged rectangle coordinates.
    /// </summary>
    public void UpdateBounds(Vector3Int start, Vector3Int end)
    {
        if (_previewObject == null)
            return;

        int width = Mathf.Abs(end.x - start.x) + 1;
        int depth = Mathf.Abs(end.z - start.z) + 1;

        float centerX = (start.x + end.x) / 2f + 0.5f;
        float centerZ = (start.z + end.z) / 2f + 0.5f;
        float centerY = start.y + _yOffset;

        _previewObject.transform.localScale = new Vector3(width, BOX_HEIGHT, depth);
        _previewObject.transform.position = new Vector3(centerX, centerY, centerZ);
    }
}