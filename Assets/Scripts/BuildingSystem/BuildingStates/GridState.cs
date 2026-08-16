using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Handles grid-based placement for floors, furniture, and ceilings with drag-fill support
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
    private GridData _ceilingFurnitureData;
    private ObjectPlacer _objectPlacer;
    private GridData _selectedData;

    private GridRotation _currentRotation = GridRotation.Deg0;

    // Restrict multi-tile selection to surface elements
    private bool _isDraggableType;
    private Vector3Int? _dragOrigin = null;

    public GridState(int ID, Grid grid, PreviewSystem previewSystem, ObjectDatabase database, 
                    ObjectPlacer objectPlacer, GridData floorData, GridData furnitureData, GridData ceilingData, GridData ceilingFurnitureData)
    {
        _selectedObjectIndex = database.objectsData.FindIndex(data => data.ID == ID);
        
        if (_selectedObjectIndex < 0)
        {
            throw new System.Exception($"No object with ID {ID}");
        }
        
        _floorData = floorData;
        _furnitureData = furnitureData;
        _ceilingData = ceilingData;
        _ceilingFurnitureData = ceilingFurnitureData;
        _database = database;
        _ID = ID;
        _previewSystem = previewSystem;
        _objectPlacer = objectPlacer;
        _grid = grid;

        _selectedData = GetSelectedData(_selectedObjectIndex);

        ObjectBuildType buildType = _database.objectsData[_selectedObjectIndex].buildType;
        _isDraggableType = buildType == ObjectBuildType.Floor || buildType == ObjectBuildType.Ceiling;

        GameObject prefab = _database.objectsData[_selectedObjectIndex].prefab;
        _previewSystem.StartShowingGridPreview(prefab, Vector3.zero);
    }

    public void EndState()
    {
        _previewSystem.StopShowingPreview();
    }

    /// <summary>
    /// Captures drag origin for expandable surface elements
    /// </summary>
    public void OnActionStart(Vector3Int gridPosition)
    {
        if (!_isDraggableType)
        {
            _dragOrigin = null;
            return;
        }

        _dragOrigin = gridPosition;
        _previewSystem.StartShowingGridMultiPlacePreview(_grid.CellToWorld(gridPosition));
    }

    /// <summary>
    /// Refreshes bounds preview during active selection drag
    /// </summary>
    public void OnHold(Vector3Int gridPosition)
    {
        if (!_isDraggableType)
        {
            UpdateState(gridPosition);
            return;
        }

        if (!_dragOrigin.HasValue)
            return;

        Vector3 worldPosition = _grid.CellToWorld(gridPosition);

        // Surface placement overwrites existing layer contents
        _previewSystem.UpdatePosition(worldPosition, true);
    }

    /// <summary>
    /// Commits multi-cell area or falls back to single-tile placement
    /// </summary>
    public void OnAction(Vector3Int gridPosition)
    {
        if (_isDraggableType && _dragOrigin.HasValue)
        {
            PlaceRectangle(_dragOrigin.Value, gridPosition);
            _dragOrigin = null;
            RestoreHoverPreview(gridPosition);
            return;
        }

        PlaceSingle(gridPosition, _currentRotation);
        UpdateState(gridPosition);
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        bool isValid = CheckPlacementValidity(gridPosition, _selectedObjectIndex, _currentRotation);
        
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdatePosition(worldPosition, isValid);
    }

    public void Rotate(Vector3Int gridPosition)
    {
        _currentRotation = (GridRotation)(((int)_currentRotation + 1) % 4);
        
        Vector3 pivot = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdateRotation(pivot);
        UpdateState(gridPosition);
    }

    /// <summary>
    /// Direct placement entry point for layout reconstruction and save files
    /// </summary>
    public void PlaceDirect(Vector3Int gridPosition, GridRotation rotation)
    {
        if (!PlaceSingle(gridPosition, rotation))
        {
            Debug.LogWarning($"GridState.PlaceDirect: placement rejected at {gridPosition} (ID {_ID}, rotation {rotation}) - " +
                              "save data may be stale or the layout has changed since it was saved.");
        }
    }

    #region Helper Methods

    // Evaluates grid validity before instantiating single object instance
    private bool PlaceSingle(Vector3Int gridPosition, GridRotation rotation)
    {
        bool placementValidity = CheckPlacementValidity(gridPosition, _selectedObjectIndex, rotation);

        if (!placementValidity)
            return false;

        int index = _objectPlacer.PlaceObject(
            _database.objectsData[_selectedObjectIndex].prefab,
            _grid.CellToWorld(gridPosition),
            rotation,
            _database.objectsData[_selectedObjectIndex].buildType
        );

        _selectedData.AddObjectAt(
            gridPosition,
            _database.objectsData[_selectedObjectIndex].positionsFilled,
            _database.objectsData[_selectedObjectIndex].ID,
            index,
            rotation
        );

        return true;
    }

    // Iterates across 2D cell region to place tiled surfaces and replace occupants
    private void PlaceRectangle(Vector3Int origin, Vector3Int current)
    {
        ObjectData selectedObject = _database.objectsData[_selectedObjectIndex];

        if (selectedObject.positionsFilled.Count != 1 || selectedObject.positionsFilled[0] != Vector2Int.zero)
        {
            Debug.LogWarning($"GridState: '{selectedObject.name}' does not have a single-cell footprint. " +
                              "Rectangle drag-fill requires positionsFilled == {{ (0,0) }}. Falling back to single placement.");
            PlaceSingle(current, _currentRotation);
            return;
        }

        int minX = Mathf.Min(origin.x, current.x);
        int maxX = Mathf.Max(origin.x, current.x);
        int minZ = Mathf.Min(origin.z, current.z);
        int maxZ = Mathf.Max(origin.z, current.z);

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                Vector3Int cellPosition = new Vector3Int(x, current.y, z);

                int existingIndex = _selectedData.GetRepresentationIndex(cellPosition);
                if (existingIndex != -1)
                {
                    _selectedData.RemoveObjectAt(cellPosition);
                    _objectPlacer.RemoveObjectAt(existingIndex);
                }

                Vector3 worldPosition = _grid.CellToWorld(cellPosition);
                int newIndex = _objectPlacer.PlaceObject(selectedObject.prefab, worldPosition, _currentRotation, selectedObject.buildType);

                _selectedData.AddObjectAt(cellPosition, selectedObject.positionsFilled, selectedObject.ID, newIndex, _currentRotation);
            }
        }
    }

    // Swaps active multi-select visualization back to standard hover prefab
    private void RestoreHoverPreview(Vector3Int gridPosition)
    {
        GameObject prefab = _database.objectsData[_selectedObjectIndex].prefab;
        _previewSystem.StartShowingGridPreview(prefab, _grid.CellToWorld(gridPosition));
    }

    private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedObjectIndex, GridRotation rotation)
    {
        return _selectedData.CanPlaceObjectAt(
            gridPosition, 
            _database.objectsData[selectedObjectIndex].positionsFilled, 
            rotation
        );
    }

    private GridData GetSelectedData(int selectedObjectIndex)
    {
        GridData selectedData = _floorData;
        
        if (_database.objectsData[selectedObjectIndex].buildType == ObjectBuildType.Furniture)
            selectedData = _furnitureData;
        else if (_database.objectsData[selectedObjectIndex].buildType == ObjectBuildType.Ceiling)
            selectedData = _ceilingData;
        else if (_database.objectsData[selectedObjectIndex].buildType == ObjectBuildType.CeilingFurniture)
            selectedData = _ceilingFurnitureData;
            
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