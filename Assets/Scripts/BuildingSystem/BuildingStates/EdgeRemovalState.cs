using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Building state for removing edge objects (walls, fences, railings).
/// Supports single-click removal, rotation, and rectangle (straight-run) drag-removal.
/// 
/// MULTI-REMOVAL (straight run):
/// Pressing the mouse records the drag origin tile (OnActionStart). While held,
/// the drag is axis-locked to the current rotation - Deg0 locks Z (run extends
/// along X), Deg90 locks X (run extends along Z) - identical axis-locking to
/// EdgeState's placement drag. Releasing removes whatever occupies each tile
/// step along that run, using the existing per-tile priority order (Furniture
/// then Floor then Ceiling), unrestricted by layer.
/// 
/// A press-and-release with no movement along the locked axis falls through to
/// the original single-click removal, unchanged.
/// 
/// MULTI-LEVEL:
/// The run always uses the CURRENT build height at commit time (not the height
/// active when the drag started), consistent with GridRemovalState.
/// 
/// REMOVAL PRIORITY (per tile):
/// Checks layers in order: Furniture -> Floor -> Ceiling
/// Removes the first edge found in the priority order.
/// 
/// PREVIEW INTEGRATION:
/// - Single-click hover: EdgeRemovalPreview (red indicator using the actual prefab)
/// - Active drag: GridMultiPlacePreview (the same resizable bounding-box cube
///   used by grid multi-placement/removal)
/// </summary>
public class EdgeRemovalState : IBuildingState
{
    private Grid _grid;
    private PreviewSystem _previewSystem;
    private EdgeDatabase _database;
    private GridData _floorData;
    private GridData _furnitureData;
    private GridData _ceilingData;
    private ObjectPlacer _objectPlacer;

    private EdgeRotation _currentRotation = EdgeRotation.Deg0;

    // Single-edge check for removal validation (always {0} for single edge)
    private List<int> _singleEdgeCheck = new List<int> { 0 };

    // Cached so RestoreHoverPreview can re-show the removal preview after a
    // drag ends without re-deriving it from the database.
    private GameObject _previewPrefab;

    // MULTI-REMOVAL: drag origin tile, set on mouse-down, cleared on commit.
    private Vector3Int? _dragOrigin = null;

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
        _previewPrefab = null;
        if (_database.edgeData.Count > 0)
        {
            _previewPrefab = _database.edgeData[0].prefab;
        }

        _previewSystem.StartShowingEdgeRemovalPreview(_previewPrefab, Vector3.zero);
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
    /// current position against the drag origin and updates the rectangle
    /// bounds preview, always shown in the "will be removed" (invalid/red)
    /// material.
    /// </summary>
    public void OnHold(Vector3Int gridPosition)
    {
        if (!_dragOrigin.HasValue)
            return;

        Vector3Int lockedCurrent = GetAxisLockedPosition(_dragOrigin.Value, gridPosition);
        Vector3 worldPosition = _grid.CellToWorld(lockedCurrent);
        _previewSystem.UpdatePosition(worldPosition, false);
    }

    /// <summary>
    /// Commits the action. If a drag with actual movement along the locked
    /// axis is active, removes everything found across the run. A drag with
    /// zero movement (a plain click) falls back to single-cell removal.
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
                RemoveSingle(gridPosition);
            }
            else
            {
                RemoveRun(origin, lockedCurrent);
            }

            RestoreHoverPreview(gridPosition);
            return;
        }

        RemoveSingle(gridPosition);
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

    #region Helper Methods

    /// <summary>
    /// Original single-cell removal logic, unchanged. Used directly for a
    /// plain click, and once per tile when committing a run removal.
    /// </summary>
    private void RemoveSingle(Vector3Int gridPosition)
    {
        Edge targetEdge = CalculateBaseEdge(gridPosition, _currentRotation);

        var removalData = GetRemovalDataWithPriority(targetEdge);

        if (removalData.data == null || removalData.edgeIndex == -1)
            return;

        removalData.data.RemoveEdgeAt(targetEdge);
        _objectPlacer.RemoveEdgeAt(removalData.edgeIndex);
    }

    /// <summary>
    /// Removes whatever occupies each tile step along the straight run bounded
    /// by origin and current (already axis-locked by the caller), reusing the
    /// existing single-tile priority removal per step.
    /// </summary>
    private void RemoveRun(Vector3Int origin, Vector3Int current)
    {
        // MULTI-LEVEL: always use the height active at commit time (current),
        // not whatever height was active when the drag started.
        int height = current.y;

        if (_currentRotation == EdgeRotation.Deg0)
        {
            int minX = Mathf.Min(origin.x, current.x);
            int maxX = Mathf.Max(origin.x, current.x);
            int z = origin.z;

            // BUG FIX: see EdgeState.PlaceRun for the full explanation -
            // inclusive on both ends removes an existing tile per tile
            // touched, matching single-click-per-tile semantics regardless
            // of drag direction.
            for (int x = minX; x <= maxX; x++)
            {
                RemoveSingle(new Vector3Int(x, height, z));
            }
        }
        else
        {
            int minZ = Mathf.Min(origin.z, current.z);
            int maxZ = Mathf.Max(origin.z, current.z);
            int x = origin.x;

            for (int z = minZ; z <= maxZ; z++)
            {
                RemoveSingle(new Vector3Int(x, height, z));
            }
        }
    }

    /// <summary>
    /// After committing a drag removal, restores the normal single-tile
    /// removal preview. EdgeRemovalPreview always resets its internal
    /// rotation to Deg0 on StartShowingPreview, so if the current rotation is
    /// Deg90, one extra UpdateRotation call re-syncs it.
    /// </summary>
    private void RestoreHoverPreview(Vector3Int gridPosition)
    {
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);

        _previewSystem.StartShowingEdgeRemovalPreview(_previewPrefab, worldPosition);

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
    /// This is identical to EdgeState's logic.
    /// 
    /// MULTI-LEVEL FIX: Now preserves tilePosition.y in all edge coordinates.
    /// 
    /// BUG FIX: Deg90 previously extended BACKWARD (tilePosition to
    /// tilePosition.z - 1). See EdgeState.CalculateBaseEdge for the full
    /// explanation - it now extends FORWARD, matching GridData's corrected
    /// multi-segment rotation math and Deg0's pattern.
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

    /// <summary>
    /// PERFORMANCE FIX: Single-pass priority check that returns both GridData and edge index.
    /// Eliminates redundant dictionary lookups by combining validation and retrieval.
    /// </summary>
    private (GridData data, int edgeIndex) GetRemovalDataWithPriority(Edge targetEdge)
    {
        // Check furniture layer first (highest priority)
        if (!_furnitureData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation))
        {
            int index = _furnitureData.GetEdgeRepresentationIndex(targetEdge);
            return (_furnitureData, index);
        }

        // Check floor layer (medium priority)
        if (!_floorData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation))
        {
            int index = _floorData.GetEdgeRepresentationIndex(targetEdge);
            return (_floorData, index);
        }

        // Check ceiling layer (lowest priority)
        if (!_ceilingData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation))
        {
            int index = _ceilingData.GetEdgeRepresentationIndex(targetEdge);
            return (_ceilingData, index);
        }

        // No edge found in any layer
        return (null, -1);
    }

    /// <summary>
    /// Checks if an edge exists at the specified position in ANY layer.
    /// Returns true if the edge can be removed (exists in at least one layer).
    /// </summary>
    private bool CheckIfEdgeExists(Edge targetEdge)
    {
        return !(_furnitureData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation) &&
                 _floorData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation) &&
                 _ceilingData.CanPlaceEdgeAt(targetEdge, _singleEdgeCheck, _currentRotation));
    }

    #endregion
}