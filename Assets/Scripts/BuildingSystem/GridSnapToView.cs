using UnityEngine;

public class GridSnapToView : MonoBehaviour
{

    [SerializeField] private InputManager target;

    int gridSize => Mathf.RoundToInt(gameObject.transform.localScale.x * 10);

    void Update(){
        Vector3 pos = target.GetSelectedMapPosition();
        if (pos == null || target.IsPointerOverUI())
            return;
            
        Vector3Int position = Vector3Int.RoundToInt(pos);

        //snaps the position of the grid to each 15 by 15 unit
        int halfGrid = gridSize / 2;
        int xPosition = (int)(position.x + (position.x >= 0 ? halfGrid : -halfGrid)) / gridSize * gridSize;
        int zPosition = (int)(position.z + (position.z >= 0 ? halfGrid : -halfGrid)) / gridSize * gridSize;

        transform.position = new Vector3(xPosition, 0, zPosition);
    }
    
}
