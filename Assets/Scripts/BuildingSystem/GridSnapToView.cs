using UnityEngine;

public class GridSnapToView : MonoBehaviour
{

    [SerializeField] private GameObject target;

    int gridSize => Mathf.RoundToInt(gameObject.transform.localScale.x * 10);

    void Update(){
        Vector3Int position = Vector3Int.RoundToInt(target.transform.position);

        //snaps the position of the grid to each 15 by 15 unit
        int halfGrid = gridSize / 2;
        int xPosition = (int)(position.x + (position.x >= 0 ? halfGrid : -halfGrid)) / gridSize * gridSize;
        int zPosition = (int)(position.z + (position.z >= 0 ? halfGrid : -halfGrid)) / gridSize * gridSize;

        transform.position = new Vector3(xPosition, position.y, zPosition);
    }
    
}
