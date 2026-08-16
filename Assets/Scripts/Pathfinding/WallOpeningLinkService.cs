using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Links placed wall openings (doors/windows) to their embedded wall edges and host tiles.
/// Handles cascade deletion when host walls change and restores uncut wall geometry.
/// </summary>
public class WallOpeningLinkService
{
    private readonly WallChunkManager _wallChunkManager;
    private readonly ObjectPlacer _objectPlacer;
    private readonly GridData[] _subscribedLayers;

    private readonly Dictionary<Edge, OpeningRecord> _byWallEdge = new();
    private readonly HashSet<OpeningRecord> _allRecords = new();

    public event Action<int, List<Edge>> OnOpeningRegistered;
    public event Action<int, List<Edge>> OnOpeningRemoved;

    public WallOpeningLinkService(WallChunkManager wallChunkManager, ObjectPlacer objectPlacer, params GridData[] wallHostLayers)
    {
        _wallChunkManager = wallChunkManager;
        _objectPlacer = objectPlacer;
        _subscribedLayers = wallHostLayers;

        foreach (GridData layer in _subscribedLayers)
        {
            if (layer != null)
                layer.OnEdgeOccupancyChanged += HandleEdgeOccupancyChanged;
        }
    }

    public void Dispose()
    {
        foreach (GridData layer in _subscribedLayers)
        {
            if (layer != null)
                layer.OnEdgeOccupancyChanged -= HandleEdgeOccupancyChanged;
        }
    }

    public bool HasOpeningAt(Edge wallEdge) => _byWallEdge.ContainsKey(wallEdge);

    public void Register(int openingHandle, int openingID, Vector3Int basePosition, EdgeRotation rotation,
                         List<Edge> wallEdges, List<(EdgeRotation rotation, Vector3Int tile, GameObject originalWallPrefab)> wallTiles)
    {
        var record = new OpeningRecord(openingHandle, openingID, basePosition, rotation, wallEdges, wallTiles);

        foreach (Edge edge in wallEdges)
            _byWallEdge[edge] = record;

        _allRecords.Add(record);

        OnOpeningRegistered?.Invoke(openingHandle, wallEdges);
    }

    public void RemoveOpening(Edge anyLinkedWallEdge)
    {
        if (!_byWallEdge.TryGetValue(anyLinkedWallEdge, out OpeningRecord record))
            return;

        RestoreHostTiles(record);

        _objectPlacer.RemoveEdgeAt(record.openingHandle);
        OnOpeningRemoved?.Invoke(record.openingHandle, record.wallEdges);
        Unlink(record);
    }

    private void HandleEdgeOccupancyChanged(Edge edge, bool occupied)
    {
        if (occupied)
            return;

        if (!_byWallEdge.TryGetValue(edge, out OpeningRecord record))
            return;

        RestoreHostTiles(record);

        _objectPlacer.RemoveEdgeAt(record.openingHandle);
        OnOpeningRemoved?.Invoke(record.openingHandle, record.wallEdges);
        Unlink(record);
    }

    private void RestoreHostTiles(OpeningRecord record)
    {
        foreach (var (rotation, tile, originalPrefab) in record.wallTiles)
        {
            _wallChunkManager.TrySetTilePrefab(rotation, tile, originalPrefab);
        }
    }

    private void Unlink(OpeningRecord record)
    {
        foreach (Edge edge in record.wallEdges)
            _byWallEdge.Remove(edge);

        _allRecords.Remove(record);
    }

    #region Save System Support

    public IEnumerable<(int openingID, Vector3Int basePosition, EdgeRotation rotation)> GetAllOpenings()
    {
        foreach (OpeningRecord record in _allRecords)
            yield return (record.openingID, record.basePosition, record.rotation);
    }

    public void Clear()
    {
        _byWallEdge.Clear();
        _allRecords.Clear();
    }

    #endregion

    private class OpeningRecord
    {
        public readonly int openingHandle;
        public readonly int openingID;
        public readonly Vector3Int basePosition;
        public readonly EdgeRotation rotation;
        public readonly List<Edge> wallEdges;
        public readonly List<(EdgeRotation rotation, Vector3Int tile, GameObject originalWallPrefab)> wallTiles;

        public OpeningRecord(int openingHandle, int openingID, Vector3Int basePosition, EdgeRotation rotation,
                              List<Edge> wallEdges, List<(EdgeRotation, Vector3Int, GameObject)> wallTiles)
        {
            this.openingHandle = openingHandle;
            this.openingID = openingID;
            this.basePosition = basePosition;
            this.rotation = rotation;
            this.wallEdges = wallEdges;
            this.wallTiles = wallTiles;
        }
    }
}