using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Building state for placing edge objects (walls, fences, railings).
/// Supports single-click placement, rotation, and rectangle (straight-run) drag-fill.
/// 
/// MULTI-PLACEMENT (straight run):
/// Pressing the mouse records the drag origin tile (OnActionStart). While held,
/// the drag is axis-locked to the current rotation - Deg0 locks the Z coordinate
/// (run extends along X), Deg90 locks the X coordinate (run extends along Z) -
/// matching how edges are already axis-locked for single placement. Releasing
/// places one instance of the selected edge object per tile step along that run.
/// 
/// A press-and-release with no movement along the locked axis falls through to
/// the original single-click path unchanged (including any multi-segment
/// positionsFilled the object itself defines) - drag-fill only ever engages when
/// there's an actual run to fill.
/// 
/// OVERRIDE BEHAVIOR:
/// Identical in spirit to GridState's rectangle fill: drag-fill placement
/// replaces whatever already occupies each segment on the SAME layer. It
/// assumes a single-segment footprint (positionsFilled == {0}); if the selected
/// object doesn't have one, it logs a warning and falls back to a single
/// placement rather than producing overlapping/undefined segments.
/// 
/// MULTI-LEVEL:
/// The run always uses the CURRENT build height at commit time (not the height
/// active when the drag started), consistent with GridState/GridRemovalState.
/// 
/// PREVIEW INTEGRATION:
/// - Single-click hover: EdgePreview (an instance of the actual prefab)
/// - Active drag: GridMultiPlacePreview (the same resizable bounding-box cube
///   used by grid multi-placement/removal - a straight run is just a 1-wide
///   rectangle, so no separate preview class is needed)
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

    // MULTI-PLACEMENT: drag origin tile, set on mouse-down, cleared on commit.
    private Vector3Int? _dragOrigin = null;

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
    /// Called on mouse-down. Records the drag origin tile and switches the
    /// preview to the rectangle bounds preview.
    /// </summary>
    public void OnActionStart(Vector3Int gridPosition)
    {
        _dragOrigin = gridPosition;
        _previewSystem.StartShowingGridMultiPlacePreview(_grid.CellToWorld(gridPosition));
    }

    /// <summary>
    /// Called every frame while the mouse button is held. Axis-locks the
    /// current position against the drag origin (per _currentRotation) and
    /// updates the rectangle bounds preview.
    /// </summary>
    public void OnHold(Vector3Int gridPosition)
    {
        if (!_dragOrigin.HasValue)
            return;

        Vector3Int lockedCurrent = GetAxisLockedPosition(_dragOrigin.Value, gridPosition);
        Vector3 worldPosition = _grid.CellToWorld(lockedCurrent);

        // Override system: drag placement always replaces whatever is on this
        // layer, so there's no "invalid" run - always show as valid.
        _previewSystem.UpdatePosition(worldPosition, true);
    }

    /// <summary>
    /// Commits the action. If a drag with actual movement along the locked
    /// axis is active, places the full run. A drag with zero movement (a
    /// plain click) falls back to the original single-cell placement.
    /// </summary>
    public void OnAction(Vector3Int gridPosition)
    {
        if (_dragOrigin.HasValue)
        {
            Vector3Int origin = _dragOrigin.Value;
            Vector3Int lockedCurrent = GetAxisLockedPosition(origin, gridPosition);
            _dragOrigin = null;

            if (lockedCurrent == origin)
            {
                PlaceSingle(gridPosition);
            }
            else
            {
                PlaceRun(origin, lockedCurrent);
            }

            RestoreHoverPreview(gridPosition);
            return;
        }

        PlaceSingle(gridPosition);
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

    #region Helper Methods

    /// <summary>
    /// Original single-cell placement logic, unchanged. Used directly for a
    /// plain click, and as a safety fallback if the selected object's
    /// footprint isn't drag-fill compatible (see PlaceRun).
    /// </summary>
    private void PlaceSingle(Vector3Int gridPosition)
    {
        Edge baseEdge = CalculateBaseEdge(gridPosition, _currentRotation);
        EdgeData edgeData = _database.edgeData[_selectedObjectIndex];

        bool placementValidity = CheckPlacementValidity(baseEdge, edgeData.positionsFilled);

        if (!placementValidity)
            return;

        // Position GameObject at grid cell world position
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        
        int index = _objectPlacer.PlaceEdge(edgeData.prefab, worldPosition, _currentRotation, edgeData.shouldChunk);

        _selectedData.AddEdgeAt(baseEdge, edgeData.positionsFilled, edgeData.ID, index, _currentRotation);
    }

    /// <summary>
    /// Places one instance of the selected edge object per tile step along the
    /// straight run bounded by origin and current (already axis-locked by the
    /// caller), overriding any existing edge on this layer at each segment.
    /// 
    /// ASSUMPTION: the selected object has a single-segment footprint
    /// (positionsFilled == { 0 }). If it doesn't, a tiled run would produce
    /// overlapping/undefined segments, so this logs a warning and falls back
    /// to a single placement at the current tile instead.
    /// </summary>
    private void PlaceRun(Vector3Int origin, Vector3Int current)
    {
        EdgeData edgeData = _database.edgeData[_selectedObjectIndex];

        if (edgeData.positionsFilled.Count != 1 || edgeData.positionsFilled[0] != 0)
        {
            Debug.LogWarning($"EdgeState: '{edgeData.name}' does not have a single-segment footprint. " +
                              "Rectangle drag-fill requires positionsFilled == {{ 0 }}. Falling back to single placement.");
            PlaceSingle(current);
            return;
        }

        // MULTI-LEVEL: always use the height active at commit time (current),
        // not whatever height was active when the drag started.
        int height = current.y;

        if (_currentRotation == EdgeRotation.Deg0)
        {
            int minX = Mathf.Min(origin.x, current.x);
            int maxX = Mathf.Max(origin.x, current.x);
            int z = origin.z;

            // BUG FIX: previously looped x < maxX (exclusive), which always
            // skipped the higher-coordinate tile regardless of which end was
            // origin vs current - so depending on drag direction, either the
            // first or last tile you dragged over silently got no segment.
            // Inclusive on both ends places one segment per tile touched,
            // exactly as if each tile had been clicked individually.
            for (int x = minX; x <= maxX; x++)
            {
                PlaceEdgeSegment(new Vector3Int(x, height, z));
            }
        }
        else
        {
            int minZ = Mathf.Min(origin.z, current.z);
            int maxZ = Mathf.Max(origin.z, current.z);
            int x = origin.x;

            // BUG FIX: previously looped from (minZ + 1) (exclusive of minZ),
            // which always skipped the lower-coordinate tile - the opposite
            // end from the Deg0 bug above, which is exactly why the missing
            // edge appeared on "the first or last position depending on
            // direction/rotation." Inclusive on both ends fixes it the same
            // way as Deg0.
            for (int z = minZ; z <= maxZ; z++)
            {
                PlaceEdgeSegment(new Vector3Int(x, height, z));
            }
        }
    }

    /// <summary>
    /// Places (or overrides) a single edge segment at the given tile position,
    /// using the existing single-segment override pattern: look up any
    /// existing occupant first, destroy and unregister it, then place new.
    /// </summary>
    private void PlaceEdgeSegment(Vector3Int tilePosition)
    {
        Edge baseEdge = CalculateBaseEdge(tilePosition, _currentRotation);
        EdgeData edgeData = _database.edgeData[_selectedObjectIndex];

        int existingIndex = _selectedData.GetEdgeRepresentationIndex(baseEdge);
        if (existingIndex != -1)
        {
            _selectedData.RemoveEdgeAt(baseEdge);
            _objectPlacer.RemoveEdgeAt(existingIndex);
        }

        Vector3 worldPosition = _grid.CellToWorld(tilePosition);
        int newIndex = _objectPlacer.PlaceEdge(edgeData.prefab, worldPosition, _currentRotation, edgeData.shouldChunk);

        _selectedData.AddEdgeAt(baseEdge, edgeData.positionsFilled, edgeData.ID, newIndex, _currentRotation);
    }

    /// <summary>
    /// After committing a drag placement, restores the normal single-object
    /// hover preview. EdgePreview always resets its internal rotation to
    /// Deg0 on StartShowingPreview, so if EdgeState's current rotation is
    /// Deg90, one extra UpdateRotation call re-syncs it - otherwise the
    /// restored preview would silently show the wrong orientation.
    /// </summary>
    private void RestoreHoverPreview(Vector3Int gridPosition)
    {
        GameObject prefab = _database.edgeData[_selectedObjectIndex].prefab;
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);

        _previewSystem.StartShowingEdgePreview(prefab, worldPosition);

        if (_currentRotation == EdgeRotation.Deg90)
        {
            _previewSystem.UpdateRotation(worldPosition);
        }
    }

    /// <summary>
    /// Projects `current` onto the axis the drag is allowed to move along,
    /// locking the perpendicular coordinate to `origin`'s value. Deg0 runs
    /// extend along X (Z locked); Deg90 runs extend along Z (X locked).
    /// Height (Y) always comes from `current` (the live build height).
    /// </summary>
    private Vector3Int GetAxisLockedPosition(Vector3Int origin, Vector3Int current)
    {
        if (_currentRotation == EdgeRotation.Deg0)
            return new Vector3Int(current.x, current.y, origin.z);
        else
            return new Vector3Int(origin.x, current.y, current.z);
    }

    /// <summary>
    /// Calculates the base edge for the given tile position and rotation.
    /// This edge represents the starting point for multi-edge structures.
    /// 
    /// MULTI-LEVEL FIX: Now preserves tilePosition.y in all edge coordinates.
    /// 
    /// BUG FIX: Deg90 previously extended BACKWARD (tilePosition to
    /// tilePosition.z - 1), which was inconsistent with a true 90 degree
    /// rotation for multi-segment (positionsFilled with more than one entry)
    /// objects - verified numerically against an actual rotation of the mesh's
    /// endpoints around the pivot. It now extends FORWARD, structurally
    /// identical to Deg0 just on the Z axis, so offset o always maps to
    /// interval [o, o+1] regardless of which axis is active. This also
    /// shifts single-segment Deg90 placement by one tile compared to before.
    /// 
    /// Rotation Mapping:
    /// - Deg0: Edge along positive X-axis from (x, y, z) to (x+1, y, z) - 0° rotation (points East)
    /// - Deg90: Edge along positive Z-axis from (x, y, z) to (x, y, z+1) - -90° rotation (points North)
    /// 
    /// The edge GameObject is positioned at end1 (the tile origin).
    /// </summary>
    private Edge CalculateBaseEdge(Vector3Int tilePosition, EdgeRotation rotation)
    {
        switch (rotation)
        {
            case EdgeRotation.Deg0:
                return new Edge(
                    new Vector3Int(tilePosition.x, tilePosition.y, tilePosition.z),
                    new Vector3Int(tilePosition.x + 1, tilePosition.y, tilePosition.z)
                );

            case EdgeRotation.Deg90:
                return new Edge(
                    new Vector3Int(tilePosition.x, tilePosition.y, tilePosition.z),
                    new Vector3Int(tilePosition.x, tilePosition.y, tilePosition.z + 1)
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