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
    private int _gameObjectIndex = -1;
    private Grid _grid;
    private PreviewSystem _previewSystem;
    private GridData _floorData;
    private GridData _furnitureData;
    private GridData _ceilingData;
    private ObjectPlacer _objectPlacer;
    private GridData _selectedData;

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
        _selectedData = null;
        
        // Check furniture layer first
        if (!_furnitureData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0))
        {
            _selectedData = _furnitureData;
        }
        // If nothing in furniture, check floor layer
        else if (!_floorData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0))
        {
            _selectedData = _floorData;
        }
        // If nothing in floor, check ceiling layer
        else if (!_ceilingData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0))
        {
            _selectedData = _ceilingData;
        }

        // If no object found in any layer, return
        if (_selectedData == null)
            return;
        
        _gameObjectIndex = _selectedData.GetRepresentationIndex(gridPosition);

        if (_gameObjectIndex == -1)
            return;

        _selectedData.RemoveObjectAt(gridPosition);  
        _objectPlacer.RemoveObjectAt(_gameObjectIndex);
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        // Check if there's a valid object to remove at this position
        bool isValid = CheckIfSelectionIsValid(gridPosition);
        
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
        // Multi-deletion will be implemented in Phase 4
    }

    #region Helper Methods

    /// <summary>
    /// Checks if there's a valid object to remove at the specified position.
    /// Returns true if ANY layer contains an object at this position.
    /// </summary>
    private bool CheckIfSelectionIsValid(Vector3Int gridPosition)
    {
        // If position is occupied (CanPlace returns false), removal is valid
        return !(_furnitureData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0) && 
                 _floorData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0) &&
                 _ceilingData.CanPlaceObjectAt(gridPosition, _positionsToBeFilled, GridRotation.Deg0));
    }

    #endregion
}
