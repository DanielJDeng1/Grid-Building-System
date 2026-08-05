using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Building state for removing grid-based objects.
/// Checks for placed objects and removes them on click, or across a dragged
/// rectangle for multi-removal.
/// 
/// MULTI-REMOVAL:
/// Pressing the mouse button records the drag origin cell (OnActionStart),
/// holding and moving the mouse shows a bounding-box preview in the "will be
/// removed" (invalid/red) material (OnHold), and releasing (OnAction) removes
/// whatever occupies each cell in the rectangle, using the existing per-cell
/// priority order (Furniture then Floor then Ceiling), unrestricted by layer.
/// 
/// A single click (no mouse movement) is just a 1x1 rectangle, so single-cell
/// removal behavior is unchanged.
/// 
/// REMOVAL PRIORITY (per cell):
/// Checks layers in order: Furniture -> Floor -> Ceiling
/// Removes the first object found in the priority order.
/// 
/// PREVIEW INTEGRATION:
/// - Single-cell hover: GridRemovalPreview (red indicator cube)
/// - Active drag: GridMultiPlacePreview (resizable bounding-box cube)
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

    // MULTI-REMOVAL: drag origin cell, set on mouse-down, cleared on commit.
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
        // Single-tile check for removal validation
        _positionsToBeFilled = new() { Vector2Int.zero };

        // Initialize removal preview
        _previewSystem.StartShowingGridRemovalPreview(Vector3.zero);
    }

    public void EndState()
    {
        _previewSystem.StopShowingPreview();
    }

    /// <summary>
    /// Called on mouse-down. Records the drag origin and switches to the
    /// rectangle bounds preview.
    /// </summary>
    public void OnActionStart(Vector3Int gridPosition)
    {
        _dragOrigin = gridPosition;
        _previewSystem.StartShowingGridMultiPlacePreview(_grid.CellToWorld(gridPosition));
    }

    /// <summary>
    /// Called every frame while the mouse button is held. Updates the
    /// rectangle bounds preview, always shown in the "will be removed"
    /// (invalid/red) material.
    /// </summary>
    public void OnHold(Vector3Int gridPosition)
    {
        if (!_dragOrigin.HasValue)
            return;

        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdatePosition(worldPosition, false);
    }

    /// <summary>
    /// Commits the action. If a drag is active, removes everything found
    /// across the rectangle. Otherwise falls back to single-cell removal.
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
        // Check if there's a valid object to remove at this position
        bool isValid = CheckIfObjectExists(gridPosition);
        
        // Update preview with position and validity feedback
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdatePosition(worldPosition, isValid);
    }

    public void Rotate(Vector3Int gridPosition)
    {
        // Rotation not applicable for removal
        return;
    }

    #region Helper Methods

    /// <summary>
    /// Original single-cell removal logic, unchanged. Used directly for a
    /// non-drag click, and once per cell when committing a rectangle removal.
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
    /// Removes whatever occupies each cell in the rectangle bounded by origin
    /// and current, reusing the existing single-cell priority removal per cell.
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
                // MULTI-LEVEL: uses current.y (the height active at commit
                // time), not origin.y, so a build-height change mid-drag is
                // respected rather than removing at a stale height.
                RemoveSingle(new Vector3Int(x, current.y, z));
            }
        }
    }

    /// <summary>
    /// PERFORMANCE FIX: Single-pass priority check that returns both GridData and object index.
    /// Eliminates redundant dictionary lookups by combining validation and retrieval.
    /// </summary>
    private (GridData data, int objectIndex) GetRemovalDataWithPriority(Vector3Int gridPosition)
    {
        // Check furniture layer first (highest priority)
        if (!_furnitureData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0))
        {
            int index = _furnitureData.GetRepresentationIndex(gridPosition);
            return (_furnitureData, index);
        }
        
        // Check floor layer (medium priority)
        if (!_floorData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0))
        {
            int index = _floorData.GetRepresentationIndex(gridPosition);
            return (_floorData, index);
        }

        // Check ceiling furniture layer (second lowest priority)
        if (!_ceilingFurnitureData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0))
        {
            int index = _ceilingFurnitureData.GetRepresentationIndex(gridPosition);
            return (_ceilingFurnitureData, index);
        }

        // Check ceiling layer (lowest priority)
        if (!_ceilingData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0))
        {
            int index = _ceilingData.GetRepresentationIndex(gridPosition);
            return (_ceilingData, index);
        }

        // No object found in any layer
        return (null, -1);
    }

    /// <summary>
    /// Checks if there's a valid object to remove at the specified position.
    /// Returns true if ANY layer contains an object at this position.
    /// </summary>
    private bool CheckIfObjectExists(Vector3Int gridPosition)
    {
        // If position is occupied (CanPlace returns false), removal is valid
        return !(_furnitureData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0) && 
                 _floorData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0) &&
                 _ceilingData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0));
    }

    #endregion
}