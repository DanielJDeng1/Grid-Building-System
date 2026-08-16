using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Handles single-click and axis-aligned drag removal for edge objects (walls, fences, railings) across floor layers
/// </summary>
public class EdgeRemovalState : IBuildingState
{
    private Grid _grid;
    private PreviewSystem _previewSystem;
    private EdgeDatabase _database;
    private GridData _floorData;
    private GridData _furnitureData;
    private GridData _ceilingData;
    private ObjectPlacer _objectPlacer;

    private EdgeRotation _currentRotation = EdgeRotation.Deg0;

    // Single-segment index cache for edge validation
    private List<int> _singleEdgeCheck = new List<int> { 0 };

    // Cached default prefab for restoring hover preview after drag operations
    private GameObject _previewPrefab;

    // Anchor tile for current drag operation
    private Vector3Int? _dragOrigin = null;

    public EdgeRemovalState(Grid grid, PreviewSystem previewSystem, EdgeDatabase database,
                           ObjectPlacer objectPlacer, GridData floorData, GridData furnitureData, GridData ceilingData)
    {
        _grid = grid;
        _previewSystem = previewSystem;
        _database = database;
        _objectPlacer = objectPlacer;
        _floorData = floorData;
        _furnitureData = furnitureData;
        _ceilingData = ceilingData;

        // Fall back to first database entry for removal indicator
        _previewPrefab = null;
        if (_database.edgeData.Count > 0)
        {
            _previewPrefab = _database.edgeData[0].prefab;
        }

        _previewSystem.StartShowingEdgeRemovalPreview(_previewPrefab, Vector3.zero);
    }

    public void EndState()
    {
        _previewSystem.StopShowingPreview();
    }

    /// <summary>
    /// Captures drag origin and initializes multi-cell selection preview
    /// </summary>
    public void OnActionStart(Vector3Int gridPosition)
    {
        _dragOrigin = gridPosition;
        _previewSystem.StartShowingGridMultiPlacePreview(_grid.CellToWorld(gridPosition));
    }

    /// <summary>
    /// Projects drag target onto locked axis and updates destruction bounds
    /// </summary>
    public void OnHold(Vector3Int gridPosition)
    {
        if (!_dragOrigin.HasValue)
            return;

        Vector3Int lockedCurrent = GetAxisLockedPosition(_dragOrigin.Value, gridPosition);
        Vector3 worldPosition = _grid.CellToWorld(lockedCurrent);
        _previewSystem.UpdatePosition(worldPosition, false);
    }

    /// <summary>
    /// Commits single edge removal or drag run based on total mouse delta
    /// </summary>
    public void OnAction(Vector3Int gridPosition)
    {
        if (_dragOrigin.HasValue)
        {
            Vector3Int origin = _dragOrigin.Value;
            Vector3Int lockedCurrent = GetAxisLockedPosition(origin, gridPosition);
            _dragOrigin = null;

            if (lockedCurrent == origin)
            {
                RemoveSingle(gridPosition);
            }
            else
            {
                RemoveRun(origin, lockedCurrent);
            }

            RestoreHoverPreview(gridPosition);
            return;
        }

        RemoveSingle(gridPosition);
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        Edge targetEdge = CalculateBaseEdge(gridPosition, _currentRotation);

        // Validate target edge presence across layers
        bool isValid = CheckIfEdgeExists(targetEdge);

        // Sync visual feedback to hover state
        Vector3 worldPosition = _grid.CellToWorld(targetEdge.end1);
        _previewSystem.UpdatePosition(worldPosition, isValid);
    }

    public void Rotate(Vector3Int gridPosition)
    {
        // Toggle between two orthogonal edge orientations
        _currentRotation = (EdgeRotation)(((int)_currentRotation + 1) % 2);

        // Re-orient visual preview
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdateRotation(worldPosition);
        UpdateState(gridPosition);
    }

    #region Helper Methods

    /// <summary>
    /// Executes layer-priority edge removal at a single tile coordinate
    /// </summary>
    private void RemoveSingle(Vector3Int gridPosition)
    {
        Edge targetEdge = CalculateBaseEdge(gridPosition, _currentRotation);

        var removalData = GetRemovalDataWithPriority(targetEdge);

        if (removalData.data == null || removalData.edgeIndex == -1)
            return;

        removalData.data.RemoveEdgeAt(targetEdge);
        _objectPlacer.RemoveEdgeAt(removalData.edgeIndex);
    }

    /// <summary>
    /// Iterates along locked axis and removes occupied edges step-by-step
    /// </summary>
    private void RemoveRun(Vector3Int origin, Vector3Int current)
    {
        // Target height active at commit time
        int height = current.y;

        if (_currentRotation == EdgeRotation.Deg0)
        {
            int minX = Mathf.Min(origin.x, current.x);
            int maxX = Mathf.Max(origin.x, current.x);
            int z = origin.z;

            // Iterate inclusive range to ensure full line coverage
            for (int x = minX; x <= maxX; x++)
            {
                RemoveSingle(new Vector3Int(x, height, z));
            }
        }
        else
        {
            int minZ = Mathf.Min(origin.z, current.z);
            int maxZ = Mathf.Max(origin.z, current.z);
            int x = origin.x;

            for (int z = minZ; z <= maxZ; z++)
            {
                RemoveSingle(new Vector3Int(x, height, z));
            }
        }
    }

    /// <summary>
    /// Restores single-tile hover preview and re-syncs orientation after drag commit
    /// </summary>
    private void RestoreHoverPreview(Vector3Int gridPosition)
    {
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);

        _previewSystem.StartShowingEdgeRemovalPreview(_previewPrefab, worldPosition);

        if (_currentRotation == EdgeRotation.Deg90)
        {
            _previewSystem.UpdateRotation(worldPosition);
        }
    }

    /// <summary>
    /// Locks drag movement to active orientation axis while maintaining current Y height
    /// </summary>
    private Vector3Int GetAxisLockedPosition(Vector3Int origin, Vector3Int current)
    {
        if (_currentRotation == EdgeRotation.Deg0)
            return new Vector3Int(current.x, current.y, origin.z);
        else
            return new Vector3Int(origin.x, current.y, current.z);
    }

    /// <summary>
    /// Maps tile coordinate and orientation to absolute edge segment endpoints
    /// </summary>
    private Edge CalculateBaseEdge(Vector3Int tilePosition, EdgeRotation rotation)
    {
        switch (rotation)
        {
            case EdgeRotation.Deg0:
                return new Edge(
                    new Vector3Int(tilePosition.x, tilePosition.y, tilePosition.z),
                    new Vector3Int(tilePosition.x + 1, tilePosition.y, tilePosition.z)
                );

            case EdgeRotation.Deg90:
                return new Edge(
                    new Vector3Int(tilePosition.x, tilePosition.y, tilePosition.z),
                    new Vector3Int(tilePosition.x, tilePosition.y, tilePosition.z + 1)
                );

            default:
                return new Edge(
                    new Vector3Int(tilePosition.x, tilePosition.y, tilePosition.z),
                    new Vector3Int(tilePosition.x + 1, tilePosition.y, tilePosition.z)
                );
        }
    }

    /// <summary>
    /// Evaluates Furniture -> Floor -> Ceiling layers in priority order to return target container and index
    /// </summary>
    private (GridData data, int edgeIndex) GetRemovalDataWithPriority(Edge targetEdge)
    {
        // Priority 1: Furniture
        if (!_furnitureData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation))
        {
            int index = _furnitureData.GetEdgeRepresentationIndex(targetEdge);
            return (_furnitureData, index);
        }

        // Priority 2: Floor
        if (!_floorData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation))
        {
            int index = _floorData.GetEdgeRepresentationIndex(targetEdge);
            return (_floorData, index);
        }

        // Priority 3: Ceiling
        if (!_ceilingData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation))
        {
            int index = _ceilingData.GetEdgeRepresentationIndex(targetEdge);
            return (_ceilingData, index);
        }

        // Target cell empty
        return (null, -1);
    }

    /// <summary>
    /// Returns true if any layer contains an edge at target coordinate
    /// </summary>
    private bool CheckIfEdgeExists(Edge targetEdge)
    {
        return !(_furnitureData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation) &&
                 _floorData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation) &&
                 _ceilingData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation));
    }

    #endregion
}