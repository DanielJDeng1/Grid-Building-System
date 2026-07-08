using UnityEngine;

/// <summary>
/// BUG FIX: The previous version checked `pos == null`, which can never be
/// true for a Vector3 (it's a struct, not a reference type) - the check was
/// dead code that never actually skipped anything. Removed; IsPointerOverUI()
/// is the only guard actually needed here.
/// 
/// MULTI-LEVEL: The grid visualization mesh now tracks the active build
/// height - it raycasts against that height's plane (instead of always
/// world Y=0) and positions itself at that same height, so the visible grid
/// stays glued to whichever floor is currently active instead of staying
/// behind at ground level.
/// 
/// INSPECTOR SETUP (REQUIRED for height-tracking):
/// Assign a PlacementSystem reference in placementSystem. If left
/// unassigned, this logs a warning on Awake and the grid mesh stays fixed
/// at world Y=0 (original behavior) instead of silently doing nothing.
/// </summary>
public class GridSnapToView : MonoBehaviour
{

    [SerializeField] private InputManager target;

    [Tooltip("REQUIRED for the grid mesh to follow build height changes. Leaving this empty logs a warning and keeps the grid fixed at world Y=0.")]
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

        //snaps the position of the grid to each 15 by 15 unit
        int halfGrid = gridSize / 2;
        int xPosition = (int)(position.x + (position.x >= 0 ? halfGrid : -halfGrid)) / gridSize * gridSize;
        int zPosition = (int)(position.z + (position.z >= 0 ? halfGrid : -halfGrid)) / gridSize * gridSize;

        transform.position = new Vector3(xPosition, worldHeight, zPosition);
    }
    
}