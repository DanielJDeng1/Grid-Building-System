using UnityEngine;

/// <summary>
/// Why a candidate base cell failed CheckPlacementValidity. Exists so
/// OnAction can log a specific, actionable reason instead of a silent
/// no-op - without this, "base already has a stair" and "no floor built on
/// the landing level yet" both looked identical to the player (a plain
/// invalid/red preview), which is exactly the confusing feedback reported
/// when testing this on a fresh build with nothing on the upper floor yet.
/// </summary>
public enum TraversalPlacementStatus
{
    Valid,
    BaseOccupied,
    LandingNeedsFloor,
    LandingOccupied
}

/// <summary>
/// Building state for placing stairs/elevators (design doc §5). Deliberately
/// its own state rather than a GridState variant, because placement
/// semantics genuinely differ: GridState resolves ONE footprint on ONE
/// floor; TraversalState resolves TWO endpoints on TWO adjacent floors - the
/// base cell the player clicks, and a landing cell one floor level above,
/// auto-resolved rather than separately clicked (design doc §5 V1 scope:
/// "click the base tile, auto-resolve the landing tile directly above").
/// Offset/angled stairs (landing NOT directly above the base) are a valid
/// future extension, not V1 scope - see ResolveLandingCell.
///
/// THE ONE PLACE THE BUILDING SYSTEM TALKS TO NAV ON PURPOSE:
/// Every other interaction between the building and navigation systems goes
/// through BuildingNavBridge, translating ordinary GridData occupancy events
/// - because ordinary walkability changes ARE just "is this cell/edge
/// occupied". A traversal link isn't expressible that way: "a link exists
/// between these two specific cells, possibly on different floors, at a
/// specific cost" has no equivalent in GridData's occupancy model. So this
/// class calls INavObstacleChannel.RegisterNavLink directly - intentionally
/// the one exception to "the building system never references anything
/// nav-related" (design doc §5, §13 decision 3 context).
///
/// FOOTPRINT / VALIDITY:
/// Uses the same single-cell CanPlaceObjectAt/AddObjectAt pattern GridState
/// uses for Furniture, but against a dedicated _traversalData layer (kept
/// separate from Floor/Furniture/Ceiling so a stair placement can't collide
/// with unrelated furniture-validity rules, and so a future
/// TraversalRemovalState mirroring GridRemovalState has its own layer to
/// query - not built here, see the removal note below). Only the BASE cell
/// is footprint-checked against _traversalData; the landing cell only needs
/// to be a sane place to arrive, checked directly against floor
/// presence/obstruction.
///
/// KNOWN GAP - NAV LINK REMOVAL:
/// This state allocates a NavObstacleId at placement time but nothing here
/// persists the mapping from "this placed stair" back to that id once this
/// state instance goes away (states are short-lived - reconstructed per
/// selection, same as GridState). Removal (TraversalRemovalState calling
/// INavObstacleChannel.UnregisterNavLink) needs SOME durable place to look
/// that id up from - e.g. a Dictionary&lt;Vector3Int, NavObstacleId&gt; owned by
/// whatever object survives placement (a small manager, or extending
/// GridData's per-cell record). Deliberately not invented here since it
/// touches GridData's existing placed-object bookkeeping, which I haven't
/// seen - flagging so it isn't silently forgotten before removal is built.
///
/// ASSUMPTIONS FLAGGED FOR PLACEMENTSYSTEM INTEGRATION (no PlacementSystem.cs
/// available when this was written - true these up against the real file):
/// 1. `buildHeightIncrement` is the number of grid-cell Y units equal to one
///    floor level, per the design doc's mention of an existing
///    _buildHeightIncrement elsewhere in the height system. If PlacementSystem
///    tracks height as a world-space float rather than a cell-space int,
///    ResolveLandingCell and this constructor parameter need to change.
/// 2. ObjectBuildType needs a new `Traversal` case, and PlacementSystem's
///    state-switching logic needs a branch constructing TraversalState
///    instead of GridState when the selected object's buildType is
///    Traversal.
/// 3. A dedicated _traversalData GridData instance needs to be added
///    alongside PlacementSystem's existing Floor/Furniture/CeilingData and
///    exposed the same way. BuildingNavBridge deliberately does NOT
///    subscribe to it - NavLink registration is handled directly here
///    rather than through the generic occupancy-event translation path.
///
/// INSPECTOR SETUP:
/// Not a MonoBehaviour - constructed by PlacementSystem the same way it
/// constructs GridState. No separate Inspector setup beyond PlacementSystem
/// already holding valid references to Grid, PreviewSystem, ObjectDatabase,
/// ObjectPlacer, a _traversalData GridData instance, and (new)
/// NavigationService.ObstacleChannel.
/// </summary>
public class TraversalState : IBuildingState
{
    // Stairs get near-zero cost per design doc §5 ("walking up is just part
    // of the path"). Not yet exposed via PathfindingSettings since only one
    // traversal type exists in V1 - promote to a per-ObjectData field once
    // elevators (higher, variable cost representing wait+ride time) exist.
    private const float StairNavLinkCost = 0.01f;

    private readonly int _selectedObjectIndex;
    private readonly Grid _grid;
    private readonly PreviewSystem _previewSystem;
    private readonly ObjectDatabase _database;
    private readonly ObjectPlacer _objectPlacer;
    private readonly GridData _traversalData;
    private readonly INavObstacleChannel _navObstacleChannel;
    private readonly int _buildHeightIncrement;

    public TraversalState(
        int id,
        Grid grid,
        PreviewSystem previewSystem,
        ObjectDatabase database,
        ObjectPlacer objectPlacer,
        GridData traversalData,
        INavObstacleChannel navObstacleChannel,
        int buildHeightIncrement)
    {
        _selectedObjectIndex = database.objectsData.FindIndex(data => data.ID == id);

        if (_selectedObjectIndex < 0)
            throw new System.Exception($"No object with ID {id}");

        _grid = grid;
        _previewSystem = previewSystem;
        _database = database;
        _objectPlacer = objectPlacer;
        _traversalData = traversalData;
        _navObstacleChannel = navObstacleChannel;
        _buildHeightIncrement = buildHeightIncrement;

        GameObject prefab = _database.objectsData[_selectedObjectIndex].prefab;
        _previewSystem.StartShowingGridPreview(prefab, Vector3.zero);
    }

    public void EndState()
    {
        _previewSystem.StopShowingPreview();
    }

    // Single-click placement only, matching Furniture's behavior in
    // GridState - a stair is one object spanning two floors, not something
    // that makes sense to rectangle drag-fill.
    public void OnActionStart(Vector3Int gridPosition) { }

    public void OnHold(Vector3Int gridPosition) => UpdateState(gridPosition);

    public void OnAction(Vector3Int gridPosition)
    {
        if (!CheckPlacementValidity(gridPosition, out Vector3Int landingCell))
            return;

        int index = _objectPlacer.PlaceObject(
            _database.objectsData[_selectedObjectIndex].prefab,
            _grid.CellToWorld(gridPosition),
            GridRotation.Deg0, // stairs don't rotate in V1 - landing is always directly above
            _database.objectsData[_selectedObjectIndex].buildType
        );

        _traversalData.AddObjectAt(
            gridPosition,
            _database.objectsData[_selectedObjectIndex].positionsFilled,
            _database.objectsData[_selectedObjectIndex].ID,
            index,
            GridRotation.Deg0
        );

        NavObstacleId navId = _navObstacleChannel.AllocateId();
        _navObstacleChannel.RegisterNavLink(navId, gridPosition, landingCell, StairNavLinkCost, bidirectional: true);
        // See class-level "KNOWN GAP" note - navId isn't persisted anywhere
        // yet, which blocks removal until that's addressed.

        UpdateState(gridPosition);
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        bool isValid = CheckPlacementValidity(gridPosition, out _);
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdatePosition(worldPosition, isValid);
    }

    public void Rotate(Vector3Int gridPosition)
    {
        // No-op in V1: the landing cell is always directly above the base,
        // so there's no rotation concept yet. Offset/angled stairs (design
        // doc §5, "a valid extension later") would give this a real body.
    }

    #region Helpers

    private bool CheckPlacementValidity(Vector3Int baseCell, out Vector3Int landingCell)
    {
        landingCell = ResolveLandingCell(baseCell);

        bool baseFootprintValid = _traversalData.CanPlaceObjectAt(
            baseCell,
            _database.objectsData[_selectedObjectIndex].positionsFilled,
            GridRotation.Deg0
        );

        // The base cell needs floor presence too, same reasoning as the
        // landing check below - a stair whose own foot has no floor beneath
        // it is unwalkable in NavGrid terms (IsFloorPresent is required for
        // IsWalkable), which makes the stair itself unreachable by ordinary
        // movement even though placement would otherwise succeed silently.
        bool baseFloorValid = _navObstacleChannel.IsFloorPresent(baseCell)
                               && !_navObstacleChannel.IsCellBlocked(baseCell);

        // Checked directly against the obstacle channel rather than
        // NavGrid.IsWalkable: NavGrid's dirty-chunk batching (ProcessDirtyChunks
        // runs once per frame, not synchronously on write) means NavGrid could
        // still reflect stale data at the moment of placement, producing a
        // false-negative validity check right after an adjacent edit.
        // INavObstacleChannel's query side is always current.
        bool landingValid = _navObstacleChannel.IsFloorPresent(landingCell)
                             && !_navObstacleChannel.IsCellBlocked(landingCell);

        return baseFootprintValid && baseFloorValid && landingValid;
    }

    /// <summary>
    /// V1 scope: landing is always directly above the base cell, offset by
    /// _buildHeightIncrement grid-Y-units - see class-level assumption #1,
    /// true this up against PlacementSystem's actual height representation.
    /// </summary>
    private Vector3Int ResolveLandingCell(Vector3Int baseCell) =>
        baseCell + new Vector3Int(0, _buildHeightIncrement, 0);

    #endregion
}