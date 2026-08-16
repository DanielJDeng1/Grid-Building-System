using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Building state for wall-embedded elements (doors, windows). Cuts host wall mesh geometry on placement.
/// Single-click execution using prefab footprint offsets; no drag-fill behavior.
/// Requires existing chunked wall edges along footprint tiles matching target rotation.
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

    // Single-click placement only; drag input ignored
    public void OnActionStart(Vector3Int gridPosition) { }
    public void OnHold(Vector3Int gridPosition) { }

    public void OnAction(Vector3Int gridPosition) => TryPlace(gridPosition);

    public void UpdateState(Vector3Int gridPosition)
    {
        bool isValid = CanPlace(gridPosition, _currentRotation, out _, out _);
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

    private void TryPlace(Vector3Int gridPosition) => PlaceDirect(gridPosition, _currentRotation);

    /// <summary>
    /// Primary placement execution for interactive clicks and save/load replay.
    /// </summary>
    public void PlaceDirect(Vector3Int gridPosition, EdgeRotation rotation)
    {
        if (!CanPlace(gridPosition, rotation, out List<Edge> wallEdges, out List<(EdgeRotation rotation, Vector3Int tile, GameObject originalWallPrefab)> wallTiles))
        {
            Debug.LogWarning($"WallOpeningState.PlaceDirect: no valid host wall at {gridPosition} (rotation {rotation}) - " +
                              "save data may be stale or the layout has changed since it was saved.");
            return;
        }

        WallOpeningData openingData = _openingDatabase.openingData[_selectedIndex];
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);

        CutHostWallTiles(openingData, wallTiles);

        int handle = openingData.shouldChunk
            ? _wallChunkManager.AttachOpeningEntry(openingData.prefab, worldPosition, rotation, gridPosition)
            : _objectPlacer.PlaceEdge(openingData.prefab, worldPosition, rotation, shouldChunk: false);

        _linkService.Register(handle, openingData.ID, gridPosition, rotation, wallEdges, wallTiles);
    }

    /// <summary>
    /// Swaps host wall segments with procedurally sliced geometry variants matching opening bounds.
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
    /// Verifies footprint edges contain chunkable walls without existing opening collisions.
    /// </summary>
    private bool CanPlace(Vector3Int baseTile, EdgeRotation rotation, out List<Edge> wallEdges, out List<(EdgeRotation rotation, Vector3Int tile, GameObject originalWallPrefab)> wallTiles)
    {
        wallEdges = new List<Edge>();
        wallTiles = new List<(EdgeRotation, Vector3Int, GameObject)>();

        WallOpeningData openingData = _openingDatabase.openingData[_selectedIndex];

        foreach (int offset in openingData.positionsFilled)
        {
            Vector3Int tile = rotation == EdgeRotation.Deg0
                ? baseTile + new Vector3Int(offset, 0, 0)
                : baseTile + new Vector3Int(0, 0, offset);

            Edge edge = CalculateBaseEdge(tile, rotation);

            if (!_selectedData.TryGetEdgeInfo(edge, out int edgeID, out _))
                return false; // Missing edge data

            int wallIndex = _edgeDatabase.edgeData.FindIndex(d => d.ID == edgeID);
            if (wallIndex < 0 || !_edgeDatabase.edgeData[wallIndex].shouldChunk)
                return false; // Host is not a chunkable wall (fence/railing/opening)

            if (_linkService.HasOpeningAt(edge))
                return false; // Existing opening collision

            wallEdges.Add(edge);
            wallTiles.Add((rotation, tile, _edgeDatabase.edgeData[wallIndex].prefab));
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