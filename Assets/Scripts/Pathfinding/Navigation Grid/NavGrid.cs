using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Core navigation grid managing spatial walkability, dirty chunk flag rebuilding, 
/// and graph query interfaces. Connects to INavObstacleChannel updates and drives 
/// the underlying NavRegionGraph.
/// </summary>
public class NavGrid : IDisposable
{
    public readonly struct NavNeighbor
    {
        public readonly Vector3Int Cell;
        public readonly float Cost;
        public NavNeighbor(Vector3Int cell, float cost) { Cell = cell; Cost = cost; }
    }

    private const float DiagonalCost = 1.41421356f;

    private static readonly Vector3Int[] CardinalOffsets =
    {
        new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
    };

    private static readonly Vector3Int[] DiagonalOffsets =
    {
        new Vector3Int(1, 0, 1), new Vector3Int(1, 0, -1),
        new Vector3Int(-1, 0, 1), new Vector3Int(-1, 0, -1)
    };

    private readonly INavObstacleChannel _channel;
    private readonly int _chunkSize;
    private readonly Dictionary<int, NavFloor> _floors = new();

    private NavRegionGraph _regionGraph;

    private readonly HashSet<(int height, Vector2Int chunkCoord)> _dirtyChunks = new();

    // Origin cell to target links and costs
    private readonly Dictionary<Vector3Int, List<(Vector3Int target, float cost)>> _linksFrom = new();

    public NavGrid(INavObstacleChannel channel, int chunkSize)
    {
        _channel = channel;
        _chunkSize = chunkSize;
        _regionGraph = new NavRegionGraph(this);

        _channel.OnCellDirty += HandleCellDirty;
        _channel.OnEdgeDirty += HandleEdgeDirty;
        _channel.OnNavLinkRegistered += HandleNavLinkRegistered;
        _channel.OnNavLinkUnregistered += HandleNavLinkUnregistered;
    }

    public void Dispose()
    {
        _channel.OnCellDirty -= HandleCellDirty;
        _channel.OnEdgeDirty -= HandleEdgeDirty;
        _channel.OnNavLinkRegistered -= HandleNavLinkRegistered;
        _channel.OnNavLinkUnregistered -= HandleNavLinkUnregistered;
    }

    #region Floor access

    public NavFloor GetFloorOrNull(int height) => _floors.TryGetValue(height, out var floor) ? floor : null;

    private NavFloor GetOrCreateFloor(int height)
    {
        if (!_floors.TryGetValue(height, out NavFloor floor))
        {
            floor = new NavFloor(_chunkSize);
            _floors[height] = floor;
        }
        return floor;
    }

    #endregion

    #region Obstacle channel event handlers

    private void HandleCellDirty(Vector3Int cell) => MarkChunkDirty(cell);

    private void HandleEdgeDirty(Vector3Int cellA, Vector3Int cellB)
    {
        MarkChunkDirty(cellA);
        MarkChunkDirty(cellB);
    }

    private void HandleNavLinkRegistered(NavObstacleId id, Vector3Int cellA, Vector3Int cellB, float cost, bool bidirectional)
    {
        AddLink(cellA, cellB, cost);
        if (bidirectional)
            AddLink(cellB, cellA, cost);

        bool floorAExists = GetFloorOrNull(cellA.y) != null;
        bool floorBExists = GetFloorOrNull(cellB.y) != null;
        NavDebug.Log($"[NavGrid] HandleNavLinkRegistered({id}): {cellA}(floor exists={floorAExists}) <-> " +
                  $"{cellB}(floor exists={floorBExists}).");

        _regionGraph.RegisterNavLink(id, cellA, cellB);
    }

    private void HandleNavLinkUnregistered(NavObstacleId id)
    {
        _regionGraph.UnregisterNavLink(id);
    }

    private void AddLink(Vector3Int from, Vector3Int to, float cost)
    {
        if (!_linksFrom.TryGetValue(from, out var list))
            _linksFrom[from] = list = new List<(Vector3Int, float)>();
        list.Add((to, cost));
    }

    private void MarkChunkDirty(Vector3Int cell)
    {
        NavFloor floor = GetOrCreateFloor(cell.y);
        Vector2Int chunkCoord = floor.GetChunkCoord(cell.x, cell.z);
        _dirtyChunks.Add((cell.y, chunkCoord));

        // Boundaries affect neighboring chunks for corner-cutting and regional checks
        floor.GetLocalCoord(cell.x, cell.z, out int lx, out int lz);
        int size = floor.ChunkSize;
        if (lx == 0) _dirtyChunks.Add((cell.y, chunkCoord + new Vector2Int(-1, 0)));
        if (lx == size - 1) _dirtyChunks.Add((cell.y, chunkCoord + new Vector2Int(1, 0)));
        if (lz == 0) _dirtyChunks.Add((cell.y, chunkCoord + new Vector2Int(0, -1)));
        if (lz == size - 1) _dirtyChunks.Add((cell.y, chunkCoord + new Vector2Int(0, 1)));
    }

    #endregion

    #region Dirty processing

    /// <summary>
    /// Rebuilds queued dirty chunk flags and updates the region graph in two distinct passes.
    /// </summary>
    public void ProcessDirtyChunks()
    {
        if (_dirtyChunks.Count == 0)
            return;

        foreach (var (height, chunkCoord) in _dirtyChunks)
        {
            RebuildChunkFlags(height, chunkCoord);
            _regionGraph.RebuildIntraChunkComponents(height, chunkCoord);
        }

        foreach (var (height, chunkCoord) in _dirtyChunks)
        {
            _regionGraph.ConnectChunkToNeighbors(height, chunkCoord);
        }

        _dirtyChunks.Clear();
    }

    private void RebuildChunkFlags(int height, Vector2Int chunkCoord)
    {
        NavFloor floor = GetOrCreateFloor(height);
        NavChunk chunk = floor.GetOrCreateChunk(chunkCoord);

        for (int lz = 0; lz < chunk.Size; lz++)
        {
            for (int lx = 0; lx < chunk.Size; lx++)
            {
                int worldX = chunkCoord.x * chunk.Size + lx;
                int worldZ = chunkCoord.y * chunk.Size + lz;
                Vector3Int cell = new Vector3Int(worldX, height, worldZ);

                bool walkable = _channel.IsFloorPresent(cell) && !_channel.IsCellBlocked(cell);
                chunk.SetWalkable(lx, lz, walkable);

                bool blockedEast = _channel.IsEdgeBlocked(cell, cell + new Vector3Int(1, 0, 0));
                bool blockedWest = _channel.IsEdgeBlocked(cell, cell + new Vector3Int(-1, 0, 0));
                bool blockedNorth = _channel.IsEdgeBlocked(cell, cell + new Vector3Int(0, 0, 1));
                bool blockedSouth = _channel.IsEdgeBlocked(cell, cell + new Vector3Int(0, 0, -1));

                if (blockedEast || blockedWest || blockedNorth || blockedSouth)
                {
                    NavDebug.Log($"[NavGrid] RebuildChunkFlags: cell {cell} edge flags -> " +
                              $"E={blockedEast} W={blockedWest} N={blockedNorth} S={blockedSouth}");
                }

                chunk.SetCardinalEdgeBlocked(lx, lz, NavChunk.FlagEdgeBlockedEast, blockedEast);
                chunk.SetCardinalEdgeBlocked(lx, lz, NavChunk.FlagEdgeBlockedWest, blockedWest);
                chunk.SetCardinalEdgeBlocked(lx, lz, NavChunk.FlagEdgeBlockedNorth, blockedNorth);
                chunk.SetCardinalEdgeBlocked(lx, lz, NavChunk.FlagEdgeBlockedSouth, blockedSouth);
            }
        }
    }

    #endregion

    #region Query API

    public bool IsWalkable(Vector3Int cell)
    {
        NavFloor floor = GetFloorOrNull(cell.y);
        NavChunk chunk = floor?.GetChunkOrNull(floor.GetChunkCoord(cell.x, cell.z));
        if (chunk == null)
            return false;

        floor.GetLocalCoord(cell.x, cell.z, out int lx, out int lz);
        return chunk.IsWalkable(lx, lz);
    }

    /// <summary>
    /// Checks traversal legality between two cardinally adjacent cells.
    /// </summary>
    public bool CanTraverseCardinal(Vector3Int a, Vector3Int b)
    {
        if (!IsWalkable(a) || !IsWalkable(b))
            return false;

        NavFloor floor = GetFloorOrNull(a.y);
        NavChunk chunk = floor.GetChunkOrNull(floor.GetChunkCoord(a.x, a.z));
        floor.GetLocalCoord(a.x, a.z, out int lx, out int lz);

        byte directionFlag = CardinalDirectionFlag(b - a);
        bool blocked = directionFlag != 0 && chunk.IsCardinalEdgeBlocked(lx, lz, directionFlag);

        if (blocked)
            NavDebug.Log($"[NavGrid] CanTraverseCardinal({a} -> {b}) BLOCKED by edge flag {directionFlag}");

        return directionFlag != 0 && !blocked;
    }

    private static byte CardinalDirectionFlag(Vector3Int delta)
    {
        if (delta.x == 1 && delta.z == 0) return NavChunk.FlagEdgeBlockedEast;
        if (delta.x == -1 && delta.z == 0) return NavChunk.FlagEdgeBlockedWest;
        if (delta.z == 1 && delta.z == 0) return NavChunk.FlagEdgeBlockedNorth; // Note: maintaining original mapping
        if (delta.z == -1 && delta.x == 0) return NavChunk.FlagEdgeBlockedSouth;
        return 0;
    }

    /// <summary>
    /// Verifies diagonal movement, enforcing that flanking cardinal tiles and edges are unblocked.
    /// </summary>
    public bool CanTraverseDiagonal(Vector3Int a, Vector3Int b)
    {
        if (!IsWalkable(a) || !IsWalkable(b))
            return false;

        Vector3Int flankX = new Vector3Int(b.x, a.y, a.z);
        Vector3Int flankZ = new Vector3Int(a.x, a.y, b.z);

        if (!IsWalkable(flankX) || !IsWalkable(flankZ))
            return false;

        return CanTraverseCardinal(a, flankX) && CanTraverseCardinal(flankX, b)
            && CanTraverseCardinal(a, flankZ) && CanTraverseCardinal(flankZ, b);
    }

    /// <summary>
    /// Populates valid neighbor steps for pathfinding and flood-fill routines.
    /// </summary>
    public void GetWalkableNeighbors(Vector3Int cell, List<NavNeighbor> results, bool includeNavLinks = true)
    {
        foreach (var offset in CardinalOffsets)
        {
            Vector3Int neighbor = cell + offset;
            if (CanTraverseCardinal(cell, neighbor))
                results.Add(new NavNeighbor(neighbor, 1f));
        }

        foreach (var offset in DiagonalOffsets)
        {
            Vector3Int neighbor = cell + offset;
            if (CanTraverseDiagonal(cell, neighbor))
                results.Add(new NavNeighbor(neighbor, DiagonalCost));
        }

        if (includeNavLinks && _linksFrom.TryGetValue(cell, out var links))
        {
            foreach (var (target, cost) in links)
                results.Add(new NavNeighbor(target, cost));
        }
    }

    public RegionNodeId GetRegionNode(Vector3Int cell)
    {
        NavFloor floor = GetFloorOrNull(cell.y);
        NavChunk chunk = floor?.GetChunkOrNull(floor.GetChunkCoord(cell.x, cell.z));
        if (chunk == null)
            return RegionNodeId.Invalid;

        floor.GetLocalCoord(cell.x, cell.z, out int lx, out int lz);
        int localRegion = chunk.GetLocalRegionId(lx, lz);
        return localRegion == NavChunk.NoRegion
            ? RegionNodeId.Invalid
            : new RegionNodeId(cell.y, floor.GetChunkCoord(cell.x, cell.z), localRegion);
    }

    /// <summary>
    /// Performs coarse reachability check using the high-level region graph.
    /// </summary>
    public bool IsReachable(Vector3Int from, Vector3Int to) =>
        _regionGraph.AreConnected(GetRegionNode(from), GetRegionNode(to));

    #endregion

    #region Save System Support

    /// <summary>
    /// Clears internal floor caches, dirty tracking queues, links, and reinstantiates the region graph.
    /// </summary>
    public void Clear()
    {
        _floors.Clear();
        _dirtyChunks.Clear();
        _linksFrom.Clear();
        _regionGraph = new NavRegionGraph(this);
    }

    #endregion
}