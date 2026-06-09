using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Building state for placing edge objects (walls, fences, railings).
/// Supports single-click placement and rotation of edge structures.
/// 
/// EDGE PLACEMENT LOGIC:
/// When the player hovers over tile (x, z):
/// - Rotation Deg0: Places edge on North side from (x, z+1) to (x+1, z+1)
/// - Rotation Deg90: Places edge on East side from (x+1, z+1) to (x+1, z)
/// 
/// Multi-edge structures (e.g., 2-tile walls) extend from this base edge:
/// - Deg0: Extends along X-axis (horizontally)
/// - Deg90: Extends along Z-axis (vertically)
/// 
/// PREVIEW INTEGRATION:
/// - Activates EdgePreview state on construction
/// - Updates preview position and validity every frame
/// - Rotates preview when player presses rotation key
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

        // Initialize preview with the selected edge object's prefab
        GameObject prefab = _database.edgeData[_selectedObjectIndex].prefab;
        _previewSystem.StartShowingEdgePreview(prefab, Vector3.zero);
    }

    public void EndState()
    {
        _previewSystem.StopShowingPreview();
    }

    /// <summary>
    /// Attempts to place the edge structure at the specified grid position.
    /// Calculates the base edge based on current rotation and validates all segments.
    /// </summary>
    public void OnAction(Vector3Int gridPosition)
    {
        Edge baseEdge = CalculateBaseEdge(gridPosition, _currentRotation);
        EdgeData edgeData = _database.edgeData[_selectedObjectIndex];

        bool placementValidity = CheckPlacementValidity(baseEdge, edgeData.positionsFilled);

        if (!placementValidity)
            return;

        // Calculate world position for the edge
        // For visual consistency, place at the midpoint of the first edge segment
        Vector3 worldPosition = CalculateEdgeWorldPosition(baseEdge);
        
        int index = _objectPlacer.PlaceEdge(edgeData.prefab, worldPosition, _currentRotation);

        _selectedData.AddEdgeAt(baseEdge, edgeData.positionsFilled, edgeData.ID, index, _currentRotation);
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        Edge baseEdge = CalculateBaseEdge(gridPosition, _currentRotation);
        EdgeData edgeData = _database.edgeData[_selectedObjectIndex];
        
        // Check if placement is valid at this edge position
        bool isValid = CheckPlacementValidity(baseEdge, edgeData.positionsFilled);
        
        // Update preview with position and validity feedback
        Vector3 worldPosition = CalculateEdgeWorldPosition(baseEdge);
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
        // Multi-placement for edges will be implemented in Phase 4
    }

    #region Helper Methods

    /// <summary>
    /// Calculates the base edge for the given tile position and rotation.
    /// This edge represents the starting point for multi-edge structures.
    /// 
    /// Rotation Mapping:
    /// - Deg0: North edge of the tile (horizontal, along X-axis)
    /// - Deg90: East edge of the tile (vertical, along Z-axis)
    /// </summary>
    private Edge CalculateBaseEdge(Vector3Int tilePosition, EdgeRotation rotation)
    {
        switch (rotation)
        {
            case EdgeRotation.Deg0:
                // North edge: from (x, z+1) to (x+1, z+1)
                return new Edge(
                    new Vector3Int(tilePosition.x, 0, tilePosition.z + 1),
                    new Vector3Int(tilePosition.x + 1, 0, tilePosition.z + 1)
                );

            case EdgeRotation.Deg90:
                // East edge: from (x+1, z+1) to (x+1, z)
                return new Edge(
                    new Vector3Int(tilePosition.x + 1, 0, tilePosition.z + 1),
                    new Vector3Int(tilePosition.x + 1, 0, tilePosition.z)
                );

            default:
                return new Edge(
                    new Vector3Int(tilePosition.x, 0, tilePosition.z + 1),
                    new Vector3Int(tilePosition.x + 1, 0, tilePosition.z + 1)
                );
        }
    }

    /// <summary>
    /// Calculates the world position for edge GameObject placement.
    /// Returns the midpoint of the edge for centered alignment.
    /// </summary>
    private Vector3 CalculateEdgeWorldPosition(Edge edge)
    {
        Vector3 end1World = _grid.CellToWorld(edge.end1);
        Vector3 end2World = _grid.CellToWorld(edge.end2);

        // Return midpoint of the edge
        return (end1World + end2World) * 0.5f;
    }

    private bool CheckPlacementValidity(Edge baseEdge, List<int> positionsFilled)
    {
        return _selectedData.CanPlaceEdgeAt(baseEdge, positionsFilled, _currentRotation);
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
    Deg0,   // Horizontal alignment (X-axis / East-West)
    Deg90   // Vertical alignment (Z-axis / North-South)
}
