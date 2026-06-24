using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Building state for removing edge objects (walls, fences, railings).
/// Supports single-click removal and rotation to target different edge orientations.
/// 
/// EDGE REMOVAL LOGIC:
/// When the player hovers over tile (x, z):
/// - Rotation Deg0: Targets edge from (x, z) to (x+1, z) along positive X-axis - 0° rotation
/// - Rotation Deg90: Targets edge from (x, z) to (x, z-1) along negative Z-axis - -90° rotation
/// 
/// REMOVAL PRIORITY:
/// Checks layers in order: Furniture → Floor → Ceiling
/// Removes the first edge found in the priority order.
/// 
/// PREVIEW INTEGRATION:
/// - Activates EdgeRemovalPreview state on construction
/// - Shows red indicator when hovering over removable edge
/// - Updates preview position and validity every frame
/// - Rotates preview when player presses rotation key
/// </summary>
public class EdgeRemovalState : IBuildingState
{
    private int _gameObjectIndex = -1;
    private Grid _grid;
    private PreviewSystem _previewSystem;
    private EdgeDatabase _database;
    private GridData _floorData;
    private GridData _furnitureData;
    private GridData _ceilingData;
    private ObjectPlacer _objectPlacer;
    private GridData _selectedData;

    private EdgeRotation _currentRotation = EdgeRotation.Deg0;

    // Single-edge check for removal validation (always {0} for single edge)
    private List<int> _singleEdgeCheck = new List<int> { 0 };

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

        // Initialize removal preview
        // We need a prefab for the preview - using first edge in database as default
        GameObject previewPrefab = null;
        if (_database.edgeData.Count > 0)
        {
            previewPrefab = _database.edgeData[0].prefab;
        }

        _previewSystem.StartShowingEdgeRemovalPreview(previewPrefab, Vector3.zero);
    }

    public void EndState()
    {
        _previewSystem.StopShowingPreview();
    }

    /// <summary>
    /// Attempts to remove the edge at the specified grid position.
    /// Checks all three layers (furniture, floor, ceiling) in priority order.
    /// </summary>
    public void OnAction(Vector3Int gridPosition)
    {
        Edge targetEdge = CalculateBaseEdge(gridPosition, _currentRotation);
        _selectedData = null;

        // Check furniture layer first
        if (!_furnitureData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation))
        {
            _selectedData = _furnitureData;
        }
        // If nothing in furniture, check floor layer
        else if (!_floorData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation))
        {
            _selectedData = _floorData;
        }
        // If nothing in floor, check ceiling layer
        else if (!_ceilingData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation))
        {
            _selectedData = _ceilingData;
        }

        // If no edge found in any layer, return
        if (_selectedData == null)
            return;

        _gameObjectIndex = _selectedData.GetEdgeRepresentationIndex(targetEdge);

        if (_gameObjectIndex == -1)
            return;

        _selectedData.RemoveEdgeAt(targetEdge);
        _objectPlacer.RemoveEdgeAt(_gameObjectIndex);
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        Edge targetEdge = CalculateBaseEdge(gridPosition, _currentRotation);

        // Check if there's a valid edge to remove at this position
        bool isValid = CheckIfEdgeExists(targetEdge);

        // Update preview with position and validity feedback
        Vector3 worldPosition = _grid.CellToWorld(targetEdge.end1);
        _previewSystem.UpdatePosition(worldPosition, isValid);
    }

    public void Rotate(Vector3Int gridPosition)
    {
        // Toggle between Deg0 and Deg90 (edges only have 2 rotation states)
        _currentRotation = (EdgeRotation)(((int)_currentRotation + 1) % 2);

        // Update preview rotation
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdateRotation(worldPosition);
        UpdateState(gridPosition);
    }

    public void OnHold(Vector3Int gridPosition)
    {
        // Multi-deletion for edges will be implemented in Phase 3
    }

    #region Helper Methods

    /// <summary>
    /// Calculates the base edge for the given tile position and rotation.
    /// This is identical to EdgeState's logic.
    /// 
    /// Rotation Mapping:
    /// - Deg0: Edge along positive X-axis from (x, z) to (x+1, z) - 0° rotation (points East)
    /// - Deg90: Edge along negative Z-axis from (x, z) to (x, z-1) - -90° rotation (points South)
    /// 
    /// The edge GameObject is positioned at end1 (the tile origin).
    /// </summary>
    private Edge CalculateBaseEdge(Vector3Int tilePosition, EdgeRotation rotation)
    {
        switch (rotation)
        {
            case EdgeRotation.Deg0:
                // Horizontal edge along X-axis: from (x, z) to (x+1, z) - 0° rotation
                return new Edge(
                    new Vector3Int(tilePosition.x, 0, tilePosition.z),
                    new Vector3Int(tilePosition.x + 1, 0, tilePosition.z)
                );

            case EdgeRotation.Deg90:
                // Vertical edge along negative Z-axis: from (x, z) to (x, z-1) - -90° rotation
                return new Edge(
                    new Vector3Int(tilePosition.x, 0, tilePosition.z),
                    new Vector3Int(tilePosition.x, 0, tilePosition.z - 1)
                );

            default:
                return new Edge(
                    new Vector3Int(tilePosition.x, 0, tilePosition.z),
                    new Vector3Int(tilePosition.x + 1, 0, tilePosition.z)
                );
        }
    }

    /// <summary>
    /// Checks if an edge exists at the specified position in ANY layer.
    /// Returns true if the edge can be removed (exists in at least one layer).
    /// </summary>
    private bool CheckIfEdgeExists(Edge targetEdge)
    {
        // If CanPlaceEdgeAt returns false, it means the edge is occupied (exists)
        // We want to return true if the edge EXISTS (can be removed)
        return !(_furnitureData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation) &&
                 _floorData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation) &&
                 _ceilingData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation));
    }

    #endregion
}
