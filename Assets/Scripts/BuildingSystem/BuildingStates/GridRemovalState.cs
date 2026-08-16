using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles single-cell and area drag removal for grid-aligned objects across floor layers
/// </summary>
public class GridRemovalState : IBuildingState
{
    private Grid _grid;
    private PreviewSystem _previewSystem;
    private GridData _floorData;
    private GridData _furnitureData;
    private GridData _ceilingData;
    private GridData _ceilingFurnitureData;
    private ObjectPlacer _objectPlacer;

    private List<Vector2Int> _positionsToBeFilled;

    // Anchor cell for area drag selection
    private Vector3Int? _dragOrigin = null;

    public GridRemovalState(Grid grid, PreviewSystem previewSystem, ObjectPlacer objectPlacer, 
                           GridData floorData, GridData furnitureData, GridData ceilingData, GridData ceilingFurnitureData)
    {
        _grid = grid;
        _previewSystem = previewSystem;
        _objectPlacer = objectPlacer;
        _floorData = floorData;
        _furnitureData = furnitureData;
        _ceilingData = ceilingData;
        _ceilingFurnitureData = ceilingFurnitureData;
        
        // Single-tile origin offset for occupancy queries
        _positionsToBeFilled = new() { Vector2Int.zero };

        _previewSystem.StartShowingGridRemovalPreview(Vector3.zero);
    }

    public void EndState()
    {
        _previewSystem.StopShowingPreview();
    }

    /// <summary>
    /// Captures drag origin cell and initializes bounding box preview
    /// </summary>
    public void OnActionStart(Vector3Int gridPosition)
    {
        _dragOrigin = gridPosition;
        _previewSystem.StartShowingGridMultiPlacePreview(_grid.CellToWorld(gridPosition));
    }

    /// <summary>
    /// Updates bounding box selection visuals during active drag
    /// </summary>
    public void OnHold(Vector3Int gridPosition)
    {
        if (!_dragOrigin.HasValue)
            return;

        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdatePosition(worldPosition, false);
    }

    /// <summary>
    /// Commits single-cell removal or area drag selection
    /// </summary>
    public void OnAction(Vector3Int gridPosition)
    {
        if (_dragOrigin.HasValue)
        {
            RemoveRectangle(_dragOrigin.Value, gridPosition);
            _dragOrigin = null;
            _previewSystem.StartShowingGridRemovalPreview(_grid.CellToWorld(gridPosition));
            return;
        }

        RemoveSingle(gridPosition);
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        // Check object occupancy at hover target
        bool isValid = CheckIfObjectExists(gridPosition);
        
        // Sync hover preview position and state
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdatePosition(worldPosition, isValid);
    }

    public void Rotate(Vector3Int gridPosition)
    {
        // No-op for grid removal operations
        return;
    }

    #region Helper Methods

    /// <summary>
    /// Removes highest priority object at target coordinate
    /// </summary>
    private void RemoveSingle(Vector3Int gridPosition)
    {
        var removalData = GetRemovalDataWithPriority(gridPosition);

        if (removalData.data == null || removalData.objectIndex == -1)
            return;

        removalData.data.RemoveObjectAt(gridPosition);
        _objectPlacer.RemoveObjectAt(removalData.objectIndex);
    }

    /// <summary>
    /// Iterates through 2D bounding area and processes single-cell removals at current height
    /// </summary>
    private void RemoveRectangle(Vector3Int origin, Vector3Int current)
    {
        int minX = Mathf.Min(origin.x, current.x);
        int maxX = Mathf.Max(origin.x, current.x);
        int minZ = Mathf.Min(origin.z, current.z);
        int maxZ = Mathf.Max(origin.z, current.z);

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                // Target active height at commit time
                RemoveSingle(new Vector3Int(x, current.y, z));
            }
        }
    }

    /// <summary>
    /// Evaluates layers top-down (Furniture -> Floor -> CeilingFurniture -> Ceiling) to return target layer and object index
    /// </summary>
    private (GridData data, int objectIndex) GetRemovalDataWithPriority(Vector3Int gridPosition)
    {
        // Priority 1: Furniture
        if (!_furnitureData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0))
        {
            int index = _furnitureData.GetRepresentationIndex(gridPosition);
            return (_furnitureData, index);
        }
        
        // Priority 2: Floor
        if (!_floorData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0))
        {
            int index = _floorData.GetRepresentationIndex(gridPosition);
            return (_floorData, index);
        }

        // Priority 3: Ceiling Furniture
        if (!_ceilingFurnitureData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0))
        {
            int index = _ceilingFurnitureData.GetRepresentationIndex(gridPosition);
            return (_ceilingFurnitureData, index);
        }

        // Priority 4: Ceiling
        if (!_ceilingData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0))
        {
            int index = _ceilingData.GetRepresentationIndex(gridPosition);
            return (_ceilingData, index);
        }

        // Cell unoccupied across all layers
        return (null, -1);
    }

    /// <summary>
    /// Returns true if target coordinate is occupied in any layer
    /// </summary>
    private bool CheckIfObjectExists(Vector3Int gridPosition)
    {
        return !(_furnitureData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0) && 
                 _floorData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0) &&
                 _ceilingData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0));
    }

    #endregion
}