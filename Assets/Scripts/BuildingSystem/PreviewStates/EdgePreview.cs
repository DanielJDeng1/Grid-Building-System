using UnityEngine;

public class EdgePreview : MonoBehaviour
{

    private GameObject previewObject;

    public void StartShowingPreview(GameObject prefab)
    {
        
    }

    public void RotatePreview(Vector3Int pivot)
    {
        
    }

    public void StopShowingPreview()
    {
        
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
            position.y, 
            position.z);
    }
}
