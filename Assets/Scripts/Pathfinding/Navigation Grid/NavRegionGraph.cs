using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Identifies a local connected component within a single spatial chunk.
/// Transient handle local to chunk state; node references are discarded and regenerated during chunk rebuilds.
/// </summary>
public readonly struct RegionNodeId
{
    public readonly int Height;
    public readonly Vector2Int ChunkCoord;
    public readonly int LocalComponent;

    public RegionNodeId(int height, Vector2Int chunkCoord, int localComponent)
    {
        Height = height;
        ChunkCoord = chunkCoord;
        LocalComponent = localComponent;
    }

    public static readonly RegionNodeId Invalid = new RegionNodeId(int.MinValue, default, -1);
    public bool IsValid => LocalComponent >= 0;

    public bool Equals(RegionNodeId other) =>
        Height == other.Height && ChunkCoord == other.ChunkCoord && LocalComponent == other.LocalComponent;
    public override bool Equals(object obj) => obj is RegionNodeId other && Equals(other);
    public override int GetHashCode() => System.HashCode.Combine(Height, ChunkCoord, LocalComponent);
    public static bool operator ==(RegionNodeId a, RegionNodeId b) => a.Equals(b);
    public static bool operator !=(RegionNodeId a, RegionNodeId b) => !a.Equals(b);
}

/// <summary>
/// Coarse topological adjacency graph mapping connectivity across chunk boundaries and navigation links.
/// Supports cheap BFS reachability validation prior to detailed A* search execution.
/// </summary>
public class NavRegionGraph
{
    private readonly NavGrid _navGrid;
    private readonly Dictionary<RegionNodeId, HashSet<RegionNodeId>> _adjacency = new();

    // Active cross-chunk or vertical links awaiting attachment during chunk rebuilds
    private readonly List<(NavObstacleId id, Vector3Int cellA, Vector3Int cellB)> _navLinks = new();

    public NavRegionGraph(NavGrid navGrid)
    {
        _navGrid = navGrid;
    }

    public void RegisterNavLink(NavObstacleId id, Vector3Int cellA, Vector3Int cellB)
    {
        _navLinks.Add((id, cellA, cellB));
        RebuildChunkContaining(cellA);
        RebuildChunkContaining(cellB);
    }

    public void UnregisterNavLink(NavObstacleId id)
    {
        for (int i = _navLinks.Count - 1; i >= 0; i--)
        {
            if (_navLinks[i].id != id)
                continue;

            var (_, cellA, cellB) = _navLinks[i];
            _navLinks.RemoveAt(i);
            RebuildChunkContaining(cellA);
            RebuildChunkContaining(cellB);
        }
    }

    private void RebuildChunkContaining(Vector3Int cell)
    {
        NavFloor floor = _navGrid.GetFloorOrNull(cell.y);
        if (floor == null)
            return;

        Vector2Int chunkCoord = floor.GetChunkCoord(cell.x, cell.z);
        RebuildIntraChunkComponents(cell.y, chunkCoord);
        ConnectChunkToNeighbors(cell.y, chunkCoord);
    }

    /// <summary>
    /// Phase 1 rebuild: Computes isolated local components within chunk boundaries via local flood-fill.
    /// </summary>
    public void RebuildIntraChunkComponents(int height, Vector2Int chunkCoord)
    {
        NavFloor floor = _navGrid.GetFloorOrNull(height);
        NavChunk chunk = floor?.GetChunkOrNull(chunkCoord);
        if (chunk == null)
            return;

        RemoveAllNodesFor(height, chunkCoord);
        chunk.ClearAllRegionIds();

        int nextLocalComponent = 0;
        var neighborBuffer = new List<NavGrid.NavNeighbor>(8);
        var stack = new Stack<Vector2Int>();

        for (int lz = 0; lz < chunk.Size; lz++)
        {
            for (int lx = 0; lx < chunk.Size; lx++)
            {
                if (!chunk.IsWalkable(lx, lz) || chunk.GetLocalRegionId(lx, lz) != NavChunk.NoRegion)
                    continue;

                int componentId = nextLocalComponent++;
                _adjacency[new RegionNodeId(height, chunkCoord, componentId)] = new HashSet<RegionNodeId>();

                stack.Push(new Vector2Int(lx, lz));
                chunk.SetLocalRegionId(lx, lz, componentId);

                while (stack.Count > 0)
                {
                    Vector2Int local = stack.Pop();
                    int worldX = chunkCoord.x * chunk.Size + local.x;
                    int worldZ = chunkCoord.y * chunk.Size + local.y;
                    Vector3Int worldCell = new Vector3Int(worldX, height, worldZ);

                    neighborBuffer.Clear();
                    _navGrid.GetWalkableNeighbors(worldCell, neighborBuffer, includeNavLinks: false);

                    foreach (var neighbor in neighborBuffer)
                    {
                        if (neighbor.Cell.y != height)
                            continue;

                        Vector2Int neighborChunkCoord = floor.GetChunkCoord(neighbor.Cell.x, neighbor.Cell.z);
                        if (neighborChunkCoord != chunkCoord)
                            continue;

                        floor.GetLocalCoord(neighbor.Cell.x, neighbor.Cell.z, out int nlx, out int nlz);
                        if (chunk.GetLocalRegionId(nlx, nlz) != NavChunk.NoRegion)
                            continue;

                        chunk.SetLocalRegionId(nlx, nlz, componentId);
                        stack.Push(new Vector2Int(nlx, nlz));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Phase 2 rebuild: Binds local chunk nodes to surrounding chunk components and registered NavLinks.
    /// </summary>
    public void ConnectChunkToNeighbors(int height, Vector2Int chunkCoord)
    {
        NavFloor floor = _navGrid.GetFloorOrNull(height);
        NavChunk chunk = floor?.GetChunkOrNull(chunkCoord);
        if (chunk == null)
            return;

        ConnectToNeighborChunks(height, chunkCoord, chunk, floor);
        ReattachNavLinksTouching(height, chunkCoord, chunk, floor);
    }

    private void ConnectToNeighborChunks(int height, Vector2Int chunkCoord, NavChunk chunk, NavFloor floor)
    {
        Vector2Int[] neighborOffsets =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1)
        };

        var neighborBuffer = new List<NavGrid.NavNeighbor>(8);

        foreach (var offset in neighborOffsets)
        {
            Vector2Int neighborChunkCoord = chunkCoord + offset;
            NavChunk neighborChunk = floor.GetChunkOrNull(neighborChunkCoord);
            if (neighborChunk == null)
                continue;

            for (int lz = 0; lz < chunk.Size; lz++)
            {
                for (int lx = 0; lx < chunk.Size; lx++)
                {
                    int localRegion = chunk.GetLocalRegionId(lx, lz);
                    if (localRegion == NavChunk.NoRegion)
                        continue;

                    int worldX = chunkCoord.x * chunk.Size + lx;
                    int worldZ = chunkCoord.y * chunk.Size + lz;
                    Vector3Int worldCell = new Vector3Int(worldX, height, worldZ);

                    neighborBuffer.Clear();
                    _navGrid.GetWalkableNeighbors(worldCell, neighborBuffer, includeNavLinks: false);

                    foreach (var neighbor in neighborBuffer)
                    {
                        if (neighbor.Cell.y != height)
                            continue;

                        Vector2Int otherChunkCoord = floor.GetChunkCoord(neighbor.Cell.x, neighbor.Cell.z);
                        if (otherChunkCoord != neighborChunkCoord)
                            continue;

                        floor.GetLocalCoord(neighbor.Cell.x, neighbor.Cell.z, out int onlx, out int onlz);
                        int otherRegion = neighborChunk.GetLocalRegionId(onlx, onlz);
                        if (otherRegion == NavChunk.NoRegion)
                            continue;

                        AddEdge(
                            new RegionNodeId(height, chunkCoord, localRegion),
                            new RegionNodeId(height, neighborChunkCoord, otherRegion));
                    }
                }
            }
        }
    }

    private void ReattachNavLinksTouching(int height, Vector2Int chunkCoord, NavChunk chunk, NavFloor floor)
    {
        foreach (var (_, cellA, cellB) in _navLinks)
        {
            RegionNodeId nodeA = _navGrid.GetRegionNode(cellA);
            RegionNodeId nodeB = _navGrid.GetRegionNode(cellB);

            if (nodeA.IsValid && nodeB.IsValid)
                AddEdge(nodeA, nodeB);
        }
    }

    private void AddEdge(RegionNodeId a, RegionNodeId b)
    {
        if (!_adjacency.TryGetValue(a, out var setA))
            _adjacency[a] = setA = new HashSet<RegionNodeId>();
        if (!_adjacency.TryGetValue(b, out var setB))
            _adjacency[b] = setB = new HashSet<RegionNodeId>();

        setA.Add(b);
        setB.Add(a);
    }

    private void RemoveAllNodesFor(int height, Vector2Int chunkCoord)
    {
        var toRemove = new List<RegionNodeId>();
        foreach (var key in _adjacency.Keys)
        {
            if (key.Height == height && key.ChunkCoord == chunkCoord)
                toRemove.Add(key);
        }

        foreach (var node in toRemove)
        {
            if (_adjacency.TryGetValue(node, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    _adjacency.TryGetValue(neighbor, out var neighborSet);
                    neighborSet?.Remove(node);
                }
            }
            _adjacency.Remove(node);
        }
    }

    /// <summary>
    /// Performs BFS across component nodes to verify macroscopic reachability without tile-level pathfinding.
    /// </summary>
    public bool AreConnected(RegionNodeId a, RegionNodeId b)
    {
        if (!a.IsValid || !b.IsValid)
            return false;

        if (a.Equals(b))
            return true;

        var visited = new HashSet<RegionNodeId> { a };
        var queue = new Queue<RegionNodeId>();
        queue.Enqueue(a);

        while (queue.Count > 0)
        {
            RegionNodeId current = queue.Dequeue();
            if (!_adjacency.TryGetValue(current, out var neighbors))
                continue;

            foreach (var neighbor in neighbors)
            {
                if (neighbor.Equals(b))
                    return true;

                if (visited.Add(neighbor))
                    queue.Enqueue(neighbor);
            }
        }

        return false;
    }
}