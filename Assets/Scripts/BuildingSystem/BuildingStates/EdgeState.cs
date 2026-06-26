using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Building state for placing edge objects (walls, fences, railings).
/// Supports single-click placement and rotation of edge structures.
/// 
/// MULTI-LEVEL SUPPORT:
/// Edge placement now correctly preserves Y-coordinates from gridPosition.
/// Edges at different heights are tracked independently in GridData dictionaries.
/// 
/// EDGE PLACEMENT LOGIC:
/// When the player hovers over tile (x, y, z):
/// - Rotation Deg0: Places edge from (x, y, z) to (x+1, y, z) along positive X-axis - Rotated 0° (points East)
/// - Rotation Deg90: Places edge from (x, y, z) to (x, y, z-1) along negative Z-axis - Rotated -90° (points South)
/// 
/// Edge GameObject is positioned at the grid cell world position.
/// Rotation is applied to the parent GameObject's transform.
/// 
/// Multi-edge structures (e.g., 2-tile walls) extend from this base edge:
/// - Deg0: Extends along positive X-axis (horizontally)
/// - Deg90: Extends along negative Z-axis (vertically)
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
    /// MULTI-LEVEL: gridPosition.y is preserved throughout the placement process.
    /// </summary>
    public void OnAction(Vector3Int gridPosition)
    {
        Edge baseEdge = CalculateBaseEdge(gridPosition, _currentRotation);
        EdgeData edgeData = _database.edgeData[_selectedObjectIndex];

        bool placementValidity = CheckPlacementValidity(baseEdge, edgeData.positionsFilled);

        if (!placementValidity)
            return;

        // Position GameObject at grid cell world position
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        
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
        Vector3 worldPosition = _grid.CellToWorld(baseEdge.end1);
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
        // Multi-placement for edges will be implemented later
    }

    #region Helper Methods

    /// <summary>
    /// Calculates the base edge for the given tile position and rotation.
    /// This edge represents the starting point for multi-edge structures.
    /// 
    /// MULTI-LEVEL FIX: Now preserves tilePosition.y in all edge coordinates.
    /// 
    /// Rotation Mapping:
    /// - Deg0: Edge along positive X-axis from (x, y, z) to (x+1, y, z) - 0° rotation (points East)
    /// - Deg90: Edge along negative Z-axis from (x, y, z) to (x, y, z-1) - -90° rotation (points South)
    /// 
    /// The edge GameObject is positioned at end1 (the tile origin).
    /// </summary>
    private Edge CalculateBaseEdge(Vector3Int tilePosition, EdgeRotation rotation)
    {
        switch (rotation)
        {
            case EdgeRotation.Deg0:
                // Horizontal edge along X-axis: from (x, y, z) to (x+1, y, z) - 0° rotation
                return new Edge(
                    new Vector3Int(tilePosition.x, tilePosition.y, tilePosition.z),
                    new Vector3Int(tilePosition.x + 1, tilePosition.y, tilePosition.z)
                );

            case EdgeRotation.Deg90:
                // Vertical edge along negative Z-axis: from (x, y, z) to (x, y, z-1) - -90° rotation
                return new Edge(
                    new Vector3Int(tilePosition.x, tilePosition.y, tilePosition.z),
                    new Vector3Int(tilePosition.x, tilePosition.y, tilePosition.z - 1)
                );

            default:
                return new Edge(
                    new Vector3Int(tilePosition.x, tilePosition.y, tilePosition.z),
                    new Vector3Int(tilePosition.x + 1, tilePosition.y, tilePosition.z)
                );
        }
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
    Deg0,   // Horizontal alignment (X-axis / positive X direction) - 0° rotation
    Deg90   // Vertical alignment (Z-axis / negative Z direction) - -90° rotation
}