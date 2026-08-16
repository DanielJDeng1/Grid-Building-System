using UnityEngine;

/// <summary>
/// Diagnostic status for multi-floor traversal placement validation failures.
/// </summary>
public enum TraversalPlacementStatus
{
    Valid,
    BaseOccupied,
    LandingNeedsFloor,
    LandingOccupied
}

/// <summary>
/// Building state for multi-floor traversals (stairs/elevators).
/// Manages dual-ended placement across adjacent vertical levels and registers direct links with the navigation system.
/// </summary>
public class TraversalState : IBuildingState
{
    // Default pathfinding cost for stair traversals
    private const float StairNavLinkCost = 0.01f;

    private readonly int _selectedObjectIndex;
    private readonly int _ID;
    private readonly Grid _grid;
    private readonly PreviewSystem _previewSystem;
    private readonly ObjectDatabase _database;
    private readonly ObjectPlacer _objectPlacer;
    private readonly GridData _traversalData;
    private readonly INavObstacleChannel _navObstacleChannel;
    private readonly int _buildHeightIncrement;

    public TraversalState(
        int id,
        Grid grid,
        PreviewSystem previewSystem,
        ObjectDatabase database,
        ObjectPlacer objectPlacer,
        GridData traversalData,
        INavObstacleChannel navObstacleChannel,
        int buildHeightIncrement)
    {
        _selectedObjectIndex = database.objectsData.FindIndex(data => data.ID == id);

        if (_selectedObjectIndex < 0)
            throw new System.Exception($"No object with ID {id}");

        _ID = id;
        _grid = grid;
        _previewSystem = previewSystem;
        _database = database;
        _objectPlacer = objectPlacer;
        _traversalData = traversalData;
        _navObstacleChannel = navObstacleChannel;
        _buildHeightIncrement = buildHeightIncrement;

        GameObject prefab = _database.objectsData[_selectedObjectIndex].prefab;
        _previewSystem.StartShowingGridPreview(prefab, Vector3.zero);
    }

    public void EndState()
    {
        _previewSystem.StopShowingPreview();
    }

    // Single-tile placement only; drag-fill unsupported for vertical traversals
    public void OnActionStart(Vector3Int gridPosition) { }

    public void OnHold(Vector3Int gridPosition) => UpdateState(gridPosition);

    public void OnAction(Vector3Int gridPosition)
    {
        if (PlaceDirect(gridPosition))
            UpdateState(gridPosition);
    }

    /// <summary>
    /// Executes placement logic and registers navigation links. Shared by live placement and save replay.
    /// </summary>
    public bool PlaceDirect(Vector3Int gridPosition)
    {
        if (!CheckPlacementValidity(gridPosition, out Vector3Int landingCell))
        {
            Debug.LogWarning($"TraversalState.PlaceDirect: placement rejected at {gridPosition} (ID {_ID}) - " +
                              "save data may be stale or the layout has changed since it was saved.");
            return false;
        }

        int index = _objectPlacer.PlaceObject(
            _database.objectsData[_selectedObjectIndex].prefab,
            _grid.CellToWorld(gridPosition),
            GridRotation.Deg0, // Fixed orientation for V1
            _database.objectsData[_selectedObjectIndex].buildType
        );

        _traversalData.AddObjectAt(
            gridPosition,
            _database.objectsData[_selectedObjectIndex].positionsFilled,
            _database.objectsData[_selectedObjectIndex].ID,
            index,
            GridRotation.Deg0
        );

        NavObstacleId navId = _navObstacleChannel.AllocateId();
        _navObstacleChannel.RegisterNavLink(navId, gridPosition, landingCell, StairNavLinkCost, bidirectional: true);

        return true;
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        bool isValid = CheckPlacementValidity(gridPosition, out _);
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdatePosition(worldPosition, isValid);
    }

    public void Rotate(Vector3Int gridPosition)
    {
        // Unused in V1; vertical alignment is fixed relative to base cell
    }

    #region Helpers

    private bool CheckPlacementValidity(Vector3Int baseCell, out Vector3Int landingCell)
    {
        landingCell = ResolveLandingCell(baseCell);

        bool baseFootprintValid = _traversalData.CanPlaceObjectAt(
            baseCell,
            _database.objectsData[_selectedObjectIndex].positionsFilled,
            GridRotation.Deg0
        );

        // Ensure base cell has supporting floor to prevent unreachable navigation links
        bool baseFloorValid = _navObstacleChannel.IsFloorPresent(baseCell)
                               && !_navObstacleChannel.IsCellBlocked(baseCell);

        // Query obstacle channel directly to bypass NavGrid batching latency
        bool landingValid = _navObstacleChannel.IsFloorPresent(landingCell)
                             && !_navObstacleChannel.IsCellBlocked(landingCell);

        return baseFootprintValid && baseFloorValid && landingValid;
    }

    /// <summary>
    /// Projects upper landing cell position vertically from base cell.
    /// </summary>
    private Vector3Int ResolveLandingCell(Vector3Int baseCell) =>
        baseCell + new Vector3Int(0, _buildHeightIncrement, 0);

    #endregion
}