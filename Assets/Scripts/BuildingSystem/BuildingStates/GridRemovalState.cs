using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Building state for removing grid-based objects.
/// Checks for placed objects and removes them on click.
/// 
/// REMOVAL PRIORITY:
/// Checks layers in order: Furniture → Floor → Ceiling
/// Removes the first object found in the priority order.
/// 
/// PERFORMANCE FIX:
/// Optimized priority check to avoid redundant dictionary lookups.
/// Now performs single-pass validation that returns both GridData reference
/// and object index, eliminating duplicate lookups.
/// 
/// PREVIEW INTEGRATION:
/// - Activates GridRemovalPreview state on construction
/// - Shows red indicator cube when hovering over removable object
/// - Updates validity based on whether object exists at position
/// 
/// VALIDATION:
/// Uses inverted CanPlaceObjectAt() logic:
/// - If position is occupied, removal is valid (shows red preview)
/// - If position is empty, removal is invalid (no object to remove)
/// </summary>
public class GridRemovalState : IBuildingState
{
    private Grid _grid;
    private PreviewSystem _previewSystem;
    private GridData _floorData;
    private GridData _furnitureData;
    private GridData _ceilingData;
    private ObjectPlacer _objectPlacer;

    private List<Vector2Int> _positionsToBeFilled;

    public GridRemovalState(Grid grid, PreviewSystem previewSystem, ObjectPlacer objectPlacer, 
                           GridData floorData, GridData furnitureData, GridData ceilingData)
    {
        _grid = grid;
        _previewSystem = previewSystem;
        _objectPlacer = objectPlacer;
        _floorData = floorData;
        _furnitureData = furnitureData;
        _ceilingData = ceilingData;
        
        // Single-tile check for removal validation
        _positionsToBeFilled = new() { Vector2Int.zero };

        // Initialize removal preview
        _previewSystem.StartShowingGridRemovalPreview(Vector3.zero);
    }

    public void EndState()
    {
        _previewSystem.StopShowingPreview();
    }

    public void OnAction(Vector3Int gridPosition)
    {
        // PERFORMANCE FIX: Single-pass priority check with object index retrieval
        var removalData = GetRemovalDataWithPriority(gridPosition);

        if (removalData.data == null || removalData.objectIndex == -1)
            return;

        removalData.data.RemoveObjectAt(gridPosition);  
        _objectPlacer.RemoveObjectAt(removalData.objectIndex);
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

    public void OnHold(Vector3Int mousePosition)
    {
        // Multi-deletion will be implemented in next phase
    }

    #region Helper Methods

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