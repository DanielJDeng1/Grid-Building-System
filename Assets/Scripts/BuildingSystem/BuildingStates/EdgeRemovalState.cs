using UnityEngine;

/// <summary>
/// Building state for removing edge objects (walls, fences, railings).
/// Detects edges under the cursor and removes the entire multi-edge structure
/// when clicked.
/// 
/// REMOVAL LOGIC:
/// Checks both possible edge orientations at the hovered tile:
/// - North edge (horizontal)
/// - East edge (vertical)
/// 
/// Removes the first valid edge found, along with all segments in its
/// multi-edge structure.
/// 
/// PREVIEW INTEGRATION:
/// - Activates EdgeRemovalPreview state on construction
/// - Shows red plane indicator when hovering over removable edge
/// - Updates validity based on whether edge exists at position
/// </summary>
public class EdgeRemovalState : IBuildingState
{
    private int _gameObjectIndex = -1;
    private Grid _grid;
    private PreviewSystem _previewSystem;
    private GridData _floorData;
    private GridData _furnitureData;
    private GridData _ceilingData;
    private ObjectPlacer _objectPlacer;
    private GridData _selectedData;

    public EdgeRemovalState(Grid grid, PreviewSystem previewSystem, ObjectPlacer objectPlacer, 
                           GridData floorData, GridData furnitureData, GridData ceilingData)
    {
        _grid = grid;
        _previewSystem = previewSystem;
        _objectPlacer = objectPlacer;
        _floorData = floorData;
        _furnitureData = furnitureData;
        _ceilingData = ceilingData;

        // Initialize edge removal preview
        _previewSystem.StartShowingEdgeRemovalPreview(Vector3.zero);
    }

    public void EndState()
    {
        _previewSystem.StopShowingPreview();
    }

    /// <summary>
    /// Attempts to remove an edge at the specified grid position.
    /// Checks all three build layers (floor, furniture, ceiling) and both
    /// edge orientations (North and East edges of the tile).
    /// </summary>
    public void OnAction(Vector3Int gridPosition)
    {
        _selectedData = null;
        Edge edgeToRemove = null;

        // Check both possible edge orientations at this tile position
        Edge northEdge = new Edge(
            new Vector3Int(gridPosition.x, 0, gridPosition.z + 1),
            new Vector3Int(gridPosition.x + 1, 0, gridPosition.z + 1)
        );

        Edge eastEdge = new Edge(
            new Vector3Int(gridPosition.x + 1, 0, gridPosition.z + 1),
            new Vector3Int(gridPosition.x + 1, 0, gridPosition.z)
        );

        // Check furniture layer first
        edgeToRemove = FindEdgeInLayer(_furnitureData, northEdge, eastEdge);
        if (edgeToRemove != null)
        {
            _selectedData = _furnitureData;
        }
        
        // If not found, check floor layer
        if (_selectedData == null)
        {
            edgeToRemove = FindEdgeInLayer(_floorData, northEdge, eastEdge);
            if (edgeToRemove != null)
            {
                _selectedData = _floorData;
            }
        }

        // If still not found, check ceiling layer
        if (_selectedData == null)
        {
            edgeToRemove = FindEdgeInLayer(_ceilingData, northEdge, eastEdge);
            if (edgeToRemove != null)
            {
                _selectedData = _ceilingData;
            }
        }

        // If no edge found in any layer, return
        if (_selectedData == null || edgeToRemove == null)
            return;
        
        _gameObjectIndex = _selectedData.GetEdgeRepresentationIndex(edgeToRemove);

        if (_gameObjectIndex == -1)
            return;

        _selectedData.RemoveEdgeAt(edgeToRemove);  
        _objectPlacer.RemoveEdgeAt(_gameObjectIndex);
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        // Check if there's a valid edge to remove at this position
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

    public void OnHold(Vector3Int gridPosition)
    {
        // Multi-deletion for edges will be implemented in Phase 4
    }

    #region Helper Methods

    /// <summary>
    /// Searches for an edge in the specified layer.
    /// Checks both the north edge and east edge of the tile.
    /// Returns the first edge found, or null if neither exists.
    /// </summary>
    private Edge FindEdgeInLayer(GridData layer, Edge northEdge, Edge eastEdge)
    {
        if (layer.GetEdgeRepresentationIndex(northEdge) != -1)
            return northEdge;
        
        if (layer.GetEdgeRepresentationIndex(eastEdge) != -1)
            return eastEdge;

        return null;
    }

    /// <summary>
    /// Checks if there's a valid edge to remove at the specified position.
    /// Used for preview system to show valid/invalid feedback.
    /// </summary>
    private bool CheckIfSelectionIsValid(Vector3Int gridPosition)
    {
        Edge northEdge = new Edge(
            new Vector3Int(gridPosition.x, 0, gridPosition.z + 1),
            new Vector3Int(gridPosition.x + 1, 0, gridPosition.z + 1)
        );

        Edge eastEdge = new Edge(
            new Vector3Int(gridPosition.x + 1, 0, gridPosition.z + 1),
            new Vector3Int(gridPosition.x + 1, 0, gridPosition.z)
        );

        // Check if any edge exists in any layer
        return FindEdgeInLayer(_furnitureData, northEdge, eastEdge) != null ||
               FindEdgeInLayer(_floorData, northEdge, eastEdge) != null ||
               FindEdgeInLayer(_ceilingData, northEdge, eastEdge) != null;
    }

    #endregion
}
