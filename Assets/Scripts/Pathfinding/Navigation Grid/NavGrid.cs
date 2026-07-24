using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns chunked walkability storage for every floor, subscribes to
/// INavObstacleChannel to know what to rebuild, and exposes the query API
/// both AStarPathfinder and NavRegionGraph rely on. NavGrid never
/// references anything building-system-related - only INavObstacleChannel.
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
    private readonly NavRegionGraph _regionGraph;

    private readonly HashSet<(int height, Vector2Int chunkCoord)> _dirtyChunks = new();

    // NavLinks: origin cell -> list of (target cell, cost). Populated from
    // OnNavLinkRegistered; see HandleNavLinkUnregistered for a known gap.
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

        _regionGraph.RegisterNavLink(id, cellA, cellB);
    }

    private void HandleNavLinkUnregistered(NavObstacleId id)
    {
        // KNOWN GAP: _linksFrom isn't cleaned up by id here yet - fine while
        // nothing unregisters a link (no TraversalState exists until Phase
        // 2), but flagged now so it isn't quietly forgotten once stairs can
        // be removed. The region graph side (below) IS fully handled.
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

        // A cell right at a chunk boundary affects the neighbor chunk's
        // connectivity/corner-cutting checks too - mark all 4 neighbors
        // dirty as well (design doc's boundary rule). Over-marking a clean
        // chunk just rebuilds it to the same result; under-marking would be
        // a stale-boundary bug.
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
    /// Drains the dirty-chunk queue - call once per frame (e.g. from
    /// NavigationService.LateUpdate). Two passes, deliberately: every dirty
    /// chunk's flags AND intra-chunk region components are rebuilt first,
    /// THEN every dirty chunk connects to its neighbors. Doing this in one
    /// pass would mean processing order within the batch determines whether
    /// a chunk reads a still-stale neighbor - see NavRegionGraph's
    /// RebuildIntraChunkComponents/ConnectChunkToNeighbors split.
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
                    Debug.Log($"[DEBUG][NavGrid] RebuildChunkFlags: cell {cell} edge flags -> " +
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
    /// Assumes a and b are cardinally adjacent on the same floor. Chunk
    /// lookups are always recomputed fresh from world coordinates here,
    /// never cached - so this resolves transparently whether a and b share
    /// a chunk or straddle a boundary.
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
            Debug.Log($"[DEBUG][NavGrid] CanTraverseCardinal({a} -> {b}) BLOCKED by edge flag {directionFlag}");

        return directionFlag != 0 && !blocked;
    }

    private static byte CardinalDirectionFlag(Vector3Int delta)
    {
        if (delta.x == 1 && delta.z == 0) return NavChunk.FlagEdgeBlockedEast;
        if (delta.x == -1 && delta.z == 0) return NavChunk.FlagEdgeBlockedWest;
        if (delta.z == 1 && delta.x == 0) return NavChunk.FlagEdgeBlockedNorth;
        if (delta.z == -1 && delta.x == 0) return NavChunk.FlagEdgeBlockedSouth;
        return 0;
    }

    /// <summary>
    /// Strict corner-cutting rule (design doc §8): legal only if both
    /// flanking cardinal cells are walkable AND neither of the two relevant
    /// cardinal edges is blocked. This check spans chunk boundaries
    /// transparently for the same reason CanTraverseCardinal does.
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
    /// Shared by AStarPathfinder and NavRegionGraph's flood-fill so the two
    /// can never disagree about what's traversable. includeNavLinks is
    /// false for the region graph's intra-chunk flood-fill, since links are
    /// cross-region by definition and reattached separately there.
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
    /// Cheap reachability check (design doc §8's two-tier search gate) -
    /// answers "does any path exist" via the coarse region graph, without
    /// running A*.
    /// </summary>
    public bool IsReachable(Vector3Int from, Vector3Int to) =>
        _regionGraph.AreConnected(GetRegionNode(from), GetRegionNode(to));

    #endregion
}