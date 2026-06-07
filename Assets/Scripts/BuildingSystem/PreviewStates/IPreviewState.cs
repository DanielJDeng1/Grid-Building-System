using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IPreviewState
{

    public void StartShowingPreview(GameObject prefab);

    public void RotatePreview(Vector3Int pivot);

    public void StopShowingPreview();

}
