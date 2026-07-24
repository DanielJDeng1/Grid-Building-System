using UnityEngine;

/// <summary>
/// Building state for placing grid-based objects (floors, furniture, ceilings).
/// Supports rotation and validity checking with integrated preview feedback.
/// 
/// MULTI-PLACEMENT (Floor/Ceiling only):
/// Floor and Ceiling objects support rectangle drag-fill: pressing the mouse
/// button records the drag origin cell (OnActionStart), holding and moving the
/// mouse updates a bounding-box preview (OnHold), and releasing (OnAction)
/// places one instance of the selected object per cell in the rectangle.
/// 
/// Furniture objects are intentionally excluded from drag-fill and retain the
/// exact original single-click placement behavior, since furniture footprints
/// are not guaranteed to be single-cell and tiling them naively would produce
/// overlapping placements.
/// 
/// OVERRIDE BEHAVIOR:
/// Drag-fill placement replaces whatever is already occupying a given cell on
/// the SAME layer (floor overrides floor, ceiling overrides ceiling). It does
/// not touch other layers. This means drag placement is always "valid" from a
/// preview standpoint - there's no rejected cell, only a replaced one.
/// 
/// PREVIEW INTEGRATION:
/// - Single-click hover: GridPreview (an instance of the actual prefab)
/// - Active drag: GridMultiPlacePreview (a resizable bounding-box cube)
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

    // MULTI-PLACEMENT: Only Floor/Ceiling objects support rectangle drag-fill.
    private bool _isDraggableType;
    private Vector3Int? _dragOrigin = null;

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

        ObjectBuildType buildType = _database.objectsData[_selectedObjectIndex].buildType;
        _isDraggableType = buildType == ObjectBuildType.Floor || buildType == ObjectBuildType.Ceiling;

        // Initialize preview with the selected object's prefab
        GameObject prefab = _database.objectsData[_selectedObjectIndex].prefab;
        _previewSystem.StartShowingGridPreview(prefab, Vector3.zero);
    }

    public void EndState()
    {
        _previewSystem.StopShowingPreview();
    }

    /// <summary>
    /// Called on mouse-down. Records the drag origin for Floor/Ceiling objects
    /// and switches the preview to the rectangle bounds preview. Furniture
    /// objects ignore this entirely - they keep single-click behavior.
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
    /// Called every frame while the mouse button is held. Furniture falls back
    /// to the normal hover preview (unaffected by dragging). Floor/Ceiling
    /// update the rectangle bounds preview.
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

        // Override system: drag placement always replaces whatever is on this
        // layer, so there's no "invalid" rectangle - always show as valid.
        _previewSystem.UpdatePosition(worldPosition, true);
    }

    /// <summary>
    /// Commits the action. If a drag is active for a draggable type, places the
    /// full rectangle. Otherwise falls back to the original single-cell placement.
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

        PlaceSingle(gridPosition);
        UpdateState(gridPosition);
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
        UpdateState(gridPosition);
    }

    #region Helper Methods

    /// <summary>
    /// Original single-cell placement logic, unchanged. Used directly by
    /// Furniture objects, and as a safety fallback if a Floor/Ceiling object's
    /// footprint isn't drag-fill compatible (see PlaceRectangle).
    /// </summary>
    private void PlaceSingle(Vector3Int gridPosition)
    {
        bool placementValidity = CheckPlacementValidity(gridPosition, _selectedObjectIndex);

        if (!placementValidity)
            return;

        int index = _objectPlacer.PlaceObject(
            _database.objectsData[_selectedObjectIndex].prefab,
            _grid.CellToWorld(gridPosition),
            _currentRotation,
            _database.objectsData[_selectedObjectIndex].buildType
        );

        _selectedData.AddObjectAt(
            gridPosition,
            _database.objectsData[_selectedObjectIndex].positionsFilled,
            _database.objectsData[_selectedObjectIndex].ID,
            index,
            _currentRotation
        );
    }

    /// <summary>
    /// Places one instance of the selected object per cell in the rectangle
    /// bounded by origin and current, overriding any existing object on this
    /// layer at each cell.
    /// 
    /// ASSUMPTION: the selected object has a single-cell footprint
    /// (positionsFilled == { Vector2Int.zero }). If it doesn't, rectangle
    /// fill would produce overlapping/undefined placements, so this logs a
    /// warning and falls back to a single placement at the current cell.
    /// </summary>
    private void PlaceRectangle(Vector3Int origin, Vector3Int current)
    {
        ObjectData selectedObject = _database.objectsData[_selectedObjectIndex];

        if (selectedObject.positionsFilled.Count != 1 || selectedObject.positionsFilled[0] != Vector2Int.zero)
        {
            Debug.LogWarning($"GridState: '{selectedObject.name}' does not have a single-cell footprint. " +
                              "Rectangle drag-fill requires positionsFilled == {{ (0,0) }}. Falling back to single placement.");
            PlaceSingle(current);
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
                // MULTI-LEVEL: uses current.y (the height active at commit
                // time), not origin.y, so that if build height changes mid-drag,
                // the placed rectangle matches what the preview showed.
                Vector3Int cellPosition = new Vector3Int(x, current.y, z);

                // OVERRIDE: destroy and unregister whatever currently occupies
                // this cell on this layer before placing the new object.
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

    /// <summary>
    /// After committing a drag placement, restores the normal single-object
    /// hover preview so the player continues to see a live preview at the
    /// cursor's current cell.
    /// </summary>
    private void RestoreHoverPreview(Vector3Int gridPosition)
    {
        GameObject prefab = _database.objectsData[_selectedObjectIndex].prefab;
        _previewSystem.StartShowingGridPreview(prefab, _grid.CellToWorld(gridPosition));
    }

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