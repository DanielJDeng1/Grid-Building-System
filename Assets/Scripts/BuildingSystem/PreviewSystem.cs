
using UnityEngine;

public class PreviewSystem : MonoBehaviour
{
    [SerializeField] private float previewYOffset = 0.06f;

    private GameObject previewObject;

    public void StartShowingPlacementPreview(GameObject prefab)
    {
        previewObject = Instantiate(prefab);
    }

    public void StopShowingPreview()
    {
        if(previewObject!= null)
            Destroy(previewObject );
    }

    public void UpdatePosition(Vector3 position)
    {
        if(previewObject != null)
        {
            MovePreview(position);
        }

    }
    private void MovePreview(Vector3 position)
    {
        previewObject.transform.position = new Vector3(
            position.x, 
            position.y + previewYOffset, 
            position.z);
    }

    public void StartShowingRemovePreview()
    {
        
    }

    public void UpdateRotation(Vector3 position)
    {
        if (previewObject == null)
            return;
        
        Vector3 pivot = previewObject.transform.position + new Vector3(0.5f, 0, 0.5f);
        foreach (Transform child in previewObject.transform)
        {
            child.transform.RotateAround(pivot, Vector3.up, 90f);
        }
    }

    
}