using UnityEngine;

/// <summary>
/// snaps the grid visualizer mesh to cursor position on the active build floor height
/// </summary>
public class GridSnapToView : MonoBehaviour
{

    [SerializeField] private InputManager target;

    [Tooltip("Reference to the placement system and used to sync visual grid height with multi-story building floors.")]
    [SerializeField] private PlacementSystem placementSystem;

    int gridSize => Mathf.RoundToInt(gameObject.transform.localScale.x * 10);

    private void Awake()
    {
        if (placementSystem == null)
        {
            Debug.LogWarning("GridSnapToView: placementSystem is not assigned - the grid " +
                              "visualization will stay fixed at world Y=0 instead of following build height. " +
                              "Assign it in the Inspector to enable height tracking.");
        }
    }

    void Update(){
        if (target.IsPointerOverUI())
            return;

        float worldHeight = placementSystem != null ? placementSystem.GetCurrentBuildWorldHeight() : 0f;

        Vector3 pos = target.GetSelectedMapPositionAtHeight(worldHeight);
        Vector3Int position = Vector3Int.RoundToInt(pos);

        // snap coordinates to nearest cell increment based on grid size
        int halfGrid = gridSize / 2;
        int xPosition = (int)(position.x + (position.x >= 0 ? halfGrid : -halfGrid)) / gridSize * gridSize;
        int zPosition = (int)(position.z + (position.z >= 0 ? halfGrid : -halfGrid)) / gridSize * gridSize;

        transform.position = new Vector3(xPosition, worldHeight, zPosition);
    }
    
}