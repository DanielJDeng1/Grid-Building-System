using UnityEngine;

/// <summary>
/// Building state for placing grid-based objects (floors, furniture, ceilings).
/// Supports rotation and validity checking with integrated preview feedback.
/// 
/// PREVIEW INTEGRATION:
/// - Activates GridPreview state on construction
/// - Updates preview position and validity every frame
/// - Passes rotation commands to preview system
/// 
/// VALIDATION:
/// Every UpdateState() call checks placement validity and updates preview color:
/// - Green/white: Valid placement location
/// - Red: Invalid placement location (occupied)
/// </summary>
public class GridState : IBuildingState
{
    private int _selectedObjectIndex = -1;
    private int _ID;
    private Grid _grid;
    private PreviewSystem _previewSystem;
    private ObjectDatabase _database;
    private GridData _floorData;
    private GridData _furnitureData;
    private GridData _ceilingData;
    private ObjectPlacer _objectPlacer;
    private GridData _selectedData;

    private GridRotation _currentRotation = GridRotation.Deg0;

    public GridState(int ID, Grid grid, PreviewSystem previewSystem, ObjectDatabase database, 
                    ObjectPlacer objectPlacer, GridData floorData, GridData furnitureData, GridData ceilingData)
    {
        _selectedObjectIndex = database.objectsData.FindIndex(data => data.ID == ID);
        
        if (_selectedObjectIndex < 0)
        {
            throw new System.Exception($"No object with ID {ID}");
        }
        
        _floorData = floorData;
        _furnitureData = furnitureData;
        _ceilingData = ceilingData;
        _database = database;
        _ID = ID;
        _previewSystem = previewSystem;
        _objectPlacer = objectPlacer;
        _grid = grid;

        _selectedData = GetSelectedData(_selectedObjectIndex);

        // Initialize preview with the selected object's prefab
        GameObject prefab = _database.objectsData[_selectedObjectIndex].prefab;
        _previewSystem.StartShowingGridPreview(prefab, Vector3.zero);
    }

    public void EndState()
    {
        _previewSystem.StopShowingPreview();
    }

    public void OnAction(Vector3Int gridPosition)
    {
        bool placementValidity = CheckPlacementValidity(gridPosition, _selectedObjectIndex);

        if (!placementValidity)
            return;
        
        int index = _objectPlacer.PlaceObject(
            _database.objectsData[_selectedObjectIndex].prefab, 
            _grid.CellToWorld(gridPosition), 
            _currentRotation
        );

        _selectedData.AddObjectAt(
            gridPosition, 
            _database.objectsData[_selectedObjectIndex].positionsFilled, 
            _database.objectsData[_selectedObjectIndex].ID, 
            index, 
            _currentRotation
        );
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        // Check if placement is valid at this position
        bool isValid = CheckPlacementValidity(gridPosition, _selectedObjectIndex);
        
        // Update preview with position and validity feedback
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdatePosition(worldPosition, isValid);
    }

    public void Rotate(Vector3Int gridPosition)
    {
        // Cycle through 4 rotation states
        _currentRotation = (GridRotation)(((int)_currentRotation + 1) % 4);
        
        // Update preview rotation
        Vector3 pivot = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdateRotation(pivot);
    }

    public void OnHold(Vector3Int mousePosition)
    {
        // Multi-placement will be implemented in Phase 4
    }

    #region Helper Methods

    private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedObjectIndex)
    {
        return _selectedData.CanPlaceObjectAt(
            gridPosition, 
            _database.objectsData[selectedObjectIndex].positionsFilled, 
            _currentRotation
        );
    }

    private GridData GetSelectedData(int selectedObjectIndex)
    {
        GridData selectedData = _floorData;
        
        if (_database.objectsData[selectedObjectIndex].buildType == ObjectBuildType.Furniture)
            selectedData = _furnitureData;
        else if (_database.objectsData[selectedObjectIndex].buildType == ObjectBuildType.Ceiling)
            selectedData = _ceilingData;
            
        return selectedData;
    }

    #endregion
}

public enum GridRotation
{
    Deg0,
    Deg90,
    Deg180,
    Deg270
}
