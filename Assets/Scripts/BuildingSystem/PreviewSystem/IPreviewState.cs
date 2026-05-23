using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IPreviewState
{

    public void StartShowingPreview(GameObject prefab, Vector2Int size, Vector2Int pivot);

    public void RotatePreviews(Vector3Int pivot, int rotation);

    public void SetRotation(Vector3Int pivot, int rotation);

    public void StopShowingPreview();

    public void UpdateGridPreview(List<(Vector3Int, bool canPlace)> map, bool isHolding);

    public void UpdateEdgePreview(List<Vector3Int> map, bool isHolding);

}
