using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Building state for edge object placement like walls and fences with drag-fill and collision checks
/// </summary>
public class EdgeState : IBuildingState
{
    private int _selectedObjectIndex = -1;
    private int _ID;
    private Grid _grid;
    private PreviewSystem _previewSystem;
    private EdgeDatabase _database;
    private GridData _floorData;
    private GridData _furnitureData;
    private GridData _ceilingData;
    private ObjectPlacer _objectPlacer;
    private GridData _selectedData;

    private EdgeRotation _currentRotation = EdgeRotation.Deg0;

    // Track drag origin tile during active multi-placement runs
    private Vector3Int? _dragOrigin = null;

    public EdgeState(int ID, Grid grid, PreviewSystem previewSystem, EdgeDatabase database, 
                     ObjectPlacer objectPlacer, GridData floorData, GridData furnitureData, GridData ceilingData)
    {
        _selectedObjectIndex = database.edgeData.FindIndex(data => data.ID == ID);

        if (_selectedObjectIndex < 0)
        {
            throw new System.Exception($"No object with ID {ID}");
        }

        _ID = ID;
        _grid = grid;
        _previewSystem = previewSystem;
        _database = database;
        _floorData = floorData;
        _furnitureData = furnitureData;
        _ceilingData = ceilingData;
        _objectPlacer = objectPlacer;

        _selectedData = GetSelectedData(_selectedObjectIndex);

        GameObject prefab = _database.edgeData[_selectedObjectIndex].prefab;
        _previewSystem.StartShowingEdgePreview(prefab, Vector3.zero);
    }

    public void EndState()
    {
        _previewSystem.StopShowingPreview();
    }

    /// <summary>
    /// Captures drag origin and enables multi-placement preview
    /// </summary>
    public void OnActionStart(Vector3Int gridPosition)
    {
        _dragOrigin = gridPosition;
        _previewSystem.StartShowingGridMultiPlacePreview(_grid.CellToWorld(gridPosition));
    }

    /// <summary>
    /// Axis-locks drag position and validates edge run against object intersections
    /// </summary>
    public void OnHold(Vector3Int gridPosition)
    {
        if (!_dragOrigin.HasValue)
            return;

        Vector3Int lockedCurrent = GetAxisLockedPosition(_dragOrigin.Value, gridPosition);
        Vector3 worldPosition = _grid.CellToWorld(lockedCurrent);

        bool isValid = !IsRunBlockedByObject(_dragOrigin.Value, lockedCurrent);
        _previewSystem.UpdatePosition(worldPosition, isValid);
    }

    /// <summary>
    /// Commits straight run or falls back to single-tile placement
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
                PlaceSingle(gridPosition, _currentRotation);
            }
            else
            {
                PlaceRun(origin, lockedCurrent);
            }

            RestoreHoverPreview(gridPosition);
            return;
        }

        PlaceSingle(gridPosition, _currentRotation);
    }

    /// <summary>
    /// Updates hover preview position and validates object intersections
    /// </summary>
    public void UpdateState(Vector3Int gridPosition)
    {
        Edge baseEdge = CalculateBaseEdge(gridPosition, _currentRotation);
        EdgeData edgeData = _database.edgeData[_selectedObjectIndex];

        bool isValid = !_selectedData.WouldEdgeIntersectObject(baseEdge, edgeData.positionsFilled, _currentRotation);

        Vector3 worldPosition = _grid.CellToWorld(baseEdge.end1);
        _previewSystem.UpdatePosition(worldPosition, isValid);
    }

    public void Rotate(Vector3Int gridPosition)
    {
        // Edge objects swap between Deg0 and Deg90
        _currentRotation = (EdgeRotation)(((int)_currentRotation + 1) % 2);
        
        // Refresh preview orientation
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdateRotation(worldPosition);
        UpdateState(gridPosition);
    }

    /// <summary>
    /// Direct placement primitive for save state restoration
    /// </summary>
    public void PlaceDirect(Vector3Int gridPosition, EdgeRotation rotation)
    {
        if (!PlaceSingle(gridPosition, rotation))
        {
            Debug.LogWarning($"EdgeState.PlaceDirect: placement rejected at {gridPosition} (ID {_ID}, rotation {rotation}) - " +
                             "save data may be stale or the layout has changed since it was saved.");
        }
    }

    #region Helper Methods

    // Places single edge and removes existing overlapping edge structures
    private bool PlaceSingle(Vector3Int gridPosition, EdgeRotation rotation)
    {
        Edge baseEdge = CalculateBaseEdge(gridPosition, rotation);
        EdgeData edgeData = _database.edgeData[_selectedObjectIndex];

        if (_selectedData.WouldEdgeIntersectObject(baseEdge, edgeData.positionsFilled, rotation))
            return false;

        List<int> removedIndices = _selectedData.ClearEdgesInFootprint(baseEdge, edgeData.positionsFilled, rotation);
        foreach (int removedIndex in removedIndices)
        {
            _objectPlacer.RemoveEdgeAt(removedIndex);
        }

        Vector3 worldPosition = _grid.CellToWorld(gridPosition);

        int index = _objectPlacer.PlaceEdge(edgeData.prefab, worldPosition, rotation, edgeData.shouldChunk);

        _selectedData.AddEdgeAt(baseEdge, edgeData.positionsFilled, edgeData.ID, index, rotation);

        return true;
    }

    // Iterates across locked axis to place edge segments line-by-line
    private void PlaceRun(Vector3Int origin, Vector3Int current)
    {
        EdgeData edgeData = _database.edgeData[_selectedObjectIndex];

        if (edgeData.positionsFilled.Count != 1 || edgeData.positionsFilled[0] != 0)
        {
            Debug.LogWarning($"EdgeState: '{edgeData.name}' does not have a single-segment footprint. " +
                             "Rectangle drag-fill requires positionsFilled == {{ 0 }}. Falling back to single placement.");
            PlaceSingle(current, _currentRotation);
            return;
        }

        int height = current.y;

        if (_currentRotation == EdgeRotation.Deg0)
        {
            int minX = Mathf.Min(origin.x, current.x);
            int maxX = Mathf.Max(origin.x, current.x);
            int z = origin.z;

            for (int x = minX; x <= maxX; x++)
            {
                PlaceEdgeSegment(new Vector3Int(x, height, z));
            }
        }
        else
        {
            int minZ = Mathf.Min(origin.z, current.z);
            int maxZ = Mathf.Max(origin.z, current.z);
            int x = origin.x;

            for (int z = minZ; z <= maxZ; z++)
            {
                PlaceEdgeSegment(new Vector3Int(x, height, z));
            }
        }
    }

    // Evaluates object collision before placing individual edge segment
    private void PlaceEdgeSegment(Vector3Int tilePosition)
    {
        Edge baseEdge = CalculateBaseEdge(tilePosition, _currentRotation);
        EdgeData edgeData = _database.edgeData[_selectedObjectIndex];

        if (_selectedData.WouldEdgeIntersectObject(baseEdge, edgeData.positionsFilled, _currentRotation))
            return;

        List<int> removedIndices = _selectedData.ClearEdgesInFootprint(baseEdge, edgeData.positionsFilled, _currentRotation);
        foreach (int removedIndex in removedIndices)
        {
            _objectPlacer.RemoveEdgeAt(removedIndex);
        }

        Vector3 worldPosition = _grid.CellToWorld(tilePosition);
        int newIndex = _objectPlacer.PlaceEdge(edgeData.prefab, worldPosition, _currentRotation, edgeData.shouldChunk);

        _selectedData.AddEdgeAt(baseEdge, edgeData.positionsFilled, edgeData.ID, newIndex, _currentRotation);
    }

    // Restores default hover preview state after committing drag placement
    private void RestoreHoverPreview(Vector3Int gridPosition)
    {
        GameObject prefab = _database.edgeData[_selectedObjectIndex].prefab;
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);

        _previewSystem.StartShowingEdgePreview(prefab, worldPosition);

        if (_currentRotation == EdgeRotation.Deg90)
        {
            _previewSystem.UpdateRotation(worldPosition);
        }
    }

    // Pre-validates full drag run for UI feedback without mutating grid state
    private bool IsRunBlockedByObject(Vector3Int origin, Vector3Int current)
    {
        EdgeData edgeData = _database.edgeData[_selectedObjectIndex];
        int height = current.y;

        if (_currentRotation == EdgeRotation.Deg0)
        {
            int minX = Mathf.Min(origin.x, current.x);
            int maxX = Mathf.Max(origin.x, current.x);
            int z = origin.z;

            for (int x = minX; x <= maxX; x++)
            {
                Edge segmentEdge = CalculateBaseEdge(new Vector3Int(x, height, z), _currentRotation);
                if (_selectedData.WouldEdgeIntersectObject(segmentEdge, edgeData.positionsFilled, _currentRotation))
                    return true;
            }
        }
        else
        {
            int minZ = Mathf.Min(origin.z, current.z);
            int maxZ = Mathf.Max(origin.z, current.z);
            int x = origin.x;

            for (int z = minZ; z <= maxZ; z++)
            {
                Edge segmentEdge = CalculateBaseEdge(new Vector3Int(x, height, z), _currentRotation);
                if (_selectedData.WouldEdgeIntersectObject(segmentEdge, edgeData.positionsFilled, _currentRotation))
                    return true;
            }
        }

        return false;
    }

    // Locks drag coordinates to active rotation axis
    private Vector3Int GetAxisLockedPosition(Vector3Int origin, Vector3Int current)
    {
        if (_currentRotation == EdgeRotation.Deg0)
            return new Vector3Int(current.x, current.y, origin.z);
        else
            return new Vector3Int(origin.x, current.y, current.z);
    }

    // Maps tile position and rotation to edge directional vectors
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

    private GridData GetSelectedData(int selectedObjectIndex)
    {
        GridData selectedData = _floorData;
        
        if (_database.edgeData[selectedObjectIndex].buildType == ObjectBuildType.Furniture)
            selectedData = _furnitureData;
        else if (_database.edgeData[selectedObjectIndex].buildType == ObjectBuildType.Ceiling)
            selectedData = _ceilingData;
            
        return selectedData;
    }

    #endregion
}

public enum EdgeRotation
{
    Deg0,   // X-axis alignment
    Deg90   // Z-axis alignment
}