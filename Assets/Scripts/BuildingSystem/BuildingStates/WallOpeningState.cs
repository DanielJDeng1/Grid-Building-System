using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Building state for placing wall openings (doors, windows) - objects that embed into an
/// already-placed wall edge and cut a hole in it, rather than occupying grid space on their
/// own. Single-click only; no drag-fill - an opening's footprint comes from its own prefab's
/// positionsFilled (like a multi-segment edge object), not a user-dragged run, so there's
/// nothing for a drag gesture to add.
///
/// VALIDITY: every tile the opening's footprint touches must already have a WALL edge (an
/// EdgeData with shouldChunk == true - see EdgeDatabase's distinction between chunked
/// walls/fences/railings and non-chunked doors/edge furniture) on the matching GridData layer,
/// in the CURRENT rotation, with no other opening already registered there. Rotation must be
/// set (via Rotate, same R-key binding as EdgeState/EdgeRemovalState) to match the target wall's
/// own orientation - this state has no way to detect a wall's rotation automatically, since
/// GridData's Edge key is itself orientation-agnostic (bidirectional equality).
/// </summary>
public class WallOpeningState : IBuildingState
{
    private int _selectedIndex;
    private Grid _grid;
    private PreviewSystem _previewSystem;
    private WallOpeningDatabase _openingDatabase;
    private EdgeDatabase _edgeDatabase;
    private ObjectPlacer _objectPlacer;
    private WallChunkManager _wallChunkManager;
    private WallOpeningLinkService _linkService;
    private GridData _floorData;
    private GridData _furnitureData;
    private GridData _ceilingData;
    private GridData _selectedData;

    private EdgeRotation _currentRotation = EdgeRotation.Deg0;

    public WallOpeningState(int ID, Grid grid, PreviewSystem previewSystem, WallOpeningDatabase openingDatabase,
                             EdgeDatabase edgeDatabase, ObjectPlacer objectPlacer, WallChunkManager wallChunkManager,
                             WallOpeningLinkService linkService, GridData floorData, GridData furnitureData, GridData ceilingData)
    {
        _selectedIndex = openingDatabase.openingData.FindIndex(d => d.ID == ID);
        if (_selectedIndex < 0)
            throw new System.Exception($"No wall opening with ID {ID}");

        _grid = grid;
        _previewSystem = previewSystem;
        _openingDatabase = openingDatabase;
        _edgeDatabase = edgeDatabase;
        _objectPlacer = objectPlacer;
        _wallChunkManager = wallChunkManager;
        _linkService = linkService;
        _floorData = floorData;
        _furnitureData = furnitureData;
        _ceilingData = ceilingData;

        _selectedData = GetSelectedData();

        GameObject prefab = _openingDatabase.openingData[_selectedIndex].prefab;
        _previewSystem.StartShowingEdgePreview(prefab, Vector3.zero);
    }

    public void EndState() => _previewSystem.StopShowingPreview();

    // No drag support - see class doc.
    public void OnActionStart(Vector3Int gridPosition) { }
    public void OnHold(Vector3Int gridPosition) { }

    public void OnAction(Vector3Int gridPosition) => TryPlace(gridPosition);

    public void UpdateState(Vector3Int gridPosition)
    {
        bool isValid = CanPlace(gridPosition, out _, out _);
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdatePosition(worldPosition, isValid);
    }

    public void Rotate(Vector3Int gridPosition)
    {
        _currentRotation = (EdgeRotation)(((int)_currentRotation + 1) % 2);
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdateRotation(worldPosition);
        UpdateState(gridPosition);
    }

    #region Placement

    private void TryPlace(Vector3Int gridPosition)
    {
        if (!CanPlace(gridPosition, out List<Edge> wallEdges, out List<(EdgeRotation rotation, Vector3Int tile, GameObject originalWallPrefab)> wallTiles))
            return;

        WallOpeningData openingData = _openingDatabase.openingData[_selectedIndex];
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);

        CutHostWallTiles(openingData, wallTiles);

        int handle = openingData.shouldChunk
            ? _wallChunkManager.AttachOpeningEntry(openingData.prefab, worldPosition, _currentRotation, gridPosition)
            : _objectPlacer.PlaceEdge(openingData.prefab, worldPosition, _currentRotation, shouldChunk: false);

        _linkService.Register(handle, wallEdges, wallTiles);
    }

    /// <summary>
    /// Swaps each affected wall tile's ChunkEntry to a procedurally-cut variant sized from the
    /// opening's own collider bounds - see WallOpeningCutPlanner/WallSegmentCutCache. A tile the
    /// plan doesn't cover (footprint tile the opening's collider doesn't actually reach) is left
    /// untouched, matching WallOpeningCutPlanner.BuildPlan's omission behaviour.
    /// </summary>
    private void CutHostWallTiles(WallOpeningData openingData, List<(EdgeRotation rotation, Vector3Int tile, GameObject originalWallPrefab)> wallTiles)
    {
        PrefabColliderBounds openingBounds = PrefabColliderCache.Get(openingData.prefab);
        List<WallOpeningCutPlanner.TileCut> cutPlan = WallOpeningCutPlanner.BuildPlan(openingData.positionsFilled, openingBounds);

        foreach (WallOpeningCutPlanner.TileCut cut in cutPlan)
        {
            int tileIndex = openingData.positionsFilled.IndexOf(cut.tileOffset);
            var (rotation, tile, originalWallPrefab) = wallTiles[tileIndex];

            GameObject cutPrefab = WallSegmentCutCache.GetOrCreateCut(originalWallPrefab, cut.localXRange, cut.localYRange);

            _wallChunkManager.TrySetTilePrefab(rotation, tile, cutPrefab);
        }
    }

    /// <summary>
    /// Validates every tile in the opening's footprint has a chunked wall edge, on the correct
    /// layer, in the current rotation, with no opening already registered there. Also returns
    /// the resolved wall edges/tiles (each tile paired with its ORIGINAL prefab) so TryPlace and
    /// CutHostWallTiles don't need to re-derive them from scratch.
    /// </summary>
    private bool CanPlace(Vector3Int baseTile, out List<Edge> wallEdges, out List<(EdgeRotation rotation, Vector3Int tile, GameObject originalWallPrefab)> wallTiles)
    {
        wallEdges = new List<Edge>();
        wallTiles = new List<(EdgeRotation, Vector3Int, GameObject)>();

        WallOpeningData openingData = _openingDatabase.openingData[_selectedIndex];

        foreach (int offset in openingData.positionsFilled)
        {
            Vector3Int tile = _currentRotation == EdgeRotation.Deg0
                ? baseTile + new Vector3Int(offset, 0, 0)
                : baseTile + new Vector3Int(0, 0, offset);

            Edge edge = CalculateBaseEdge(tile, _currentRotation);

            if (!_selectedData.TryGetEdgeInfo(edge, out int edgeID, out _))
                return false; // no wall here at all

            int wallIndex = _edgeDatabase.edgeData.FindIndex(d => d.ID == edgeID);
            if (wallIndex < 0 || !_edgeDatabase.edgeData[wallIndex].shouldChunk)
                return false; // occupied, but not a wall - fences/railings/other openings aren't valid hosts

            if (_linkService.HasOpeningAt(edge))
                return false; // already has an opening

            wallEdges.Add(edge);
            wallTiles.Add((_currentRotation, tile, _edgeDatabase.edgeData[wallIndex].prefab));
        }

        return true;
    }

    private Edge CalculateBaseEdge(Vector3Int tilePosition, EdgeRotation rotation)
    {
        return rotation == EdgeRotation.Deg0
            ? new Edge(tilePosition, tilePosition + new Vector3Int(1, 0, 0))
            : new Edge(tilePosition, tilePosition + new Vector3Int(0, 0, 1));
    }

    private GridData GetSelectedData()
    {
        WallOpeningData data = _openingDatabase.openingData[_selectedIndex];
        if (data.buildType == ObjectBuildType.Furniture) return _furnitureData;
        if (data.buildType == ObjectBuildType.Ceiling) return _ceilingData;
        return _floorData;
    }

    #endregion
}