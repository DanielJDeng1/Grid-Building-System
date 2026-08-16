using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Instantiated and exposed by NavigationService. Main-thread execution only.
/// </summary>
public class NavObstacleChannel : INavObstacleChannel
{
    // Refcount tracking for static cell obstacles
    private readonly Dictionary<Vector3Int, int> _cellObstacleRefCounts = new();
    private readonly HashSet<Vector3Int> _floorPresentCells = new();

    // Refcount tracking for static wall edges
    private readonly Dictionary<NavEdge, int> _edgeObstacleRefCounts = new();

    // Dynamic edge state tracking (e.g., doors) keyed by ID
    private readonly Dictionary<NavObstacleId, NavEdge> _idToEdge = new();
    private readonly Dictionary<NavObstacleId, bool> _idPassableState = new();

    // Inter-level links (stairs, elevators)
    private readonly Dictionary<NavObstacleId, NavLinkRecord> _navLinks = new();

    private int _nextId = 0;

    public event Action<Vector3Int> OnCellDirty;
    public event Action<Vector3Int, Vector3Int> OnEdgeDirty;
    public event Action<NavObstacleId, Vector3Int, Vector3Int, float, bool> OnNavLinkRegistered;
    public event Action<NavObstacleId> OnNavLinkUnregistered;

    #region Write side - cell/edge obstacles

    public void RegisterFloorPresence(Vector3Int cell, bool present)
    {
        if (present)
            _floorPresentCells.Add(cell);
        else
            _floorPresentCells.Remove(cell);

        OnCellDirty?.Invoke(cell);
    }

    public void RegisterCellObstacle(Vector3Int cell)
    {
        _cellObstacleRefCounts.TryGetValue(cell, out int count);
        _cellObstacleRefCounts[cell] = count + 1;
        OnCellDirty?.Invoke(cell);
    }

    public void UnregisterCellObstacle(Vector3Int cell)
    {
        if (!_cellObstacleRefCounts.TryGetValue(cell, out int count))
            return;

        if (count <= 1)
            _cellObstacleRefCounts.Remove(cell);
        else
            _cellObstacleRefCounts[cell] = count - 1;

        OnCellDirty?.Invoke(cell);
    }

    public void RegisterEdgeObstacle(Vector3Int cellA, Vector3Int cellB)
    {
        NavEdge edge = new NavEdge(cellA, cellB);
        _edgeObstacleRefCounts.TryGetValue(edge, out int count);
        _edgeObstacleRefCounts[edge] = count + 1;
        NavDebug.Log($"[NavObstacleChannel] RegisterEdgeObstacle: {cellA} <-> {cellB}, refcount now {count + 1}");
        OnEdgeDirty?.Invoke(cellA, cellB);
    }

    public void UnregisterEdgeObstacle(Vector3Int cellA, Vector3Int cellB)
    {
        NavEdge edge = new NavEdge(cellA, cellB);
        if (!_edgeObstacleRefCounts.TryGetValue(edge, out int count))
            return;

        if (count <= 1)
            _edgeObstacleRefCounts.Remove(edge);
        else
            _edgeObstacleRefCounts[edge] = count - 1;

        OnEdgeDirty?.Invoke(cellA, cellB);
    }

    #endregion

    #region Write side - stateful registrations

    public NavObstacleId AllocateId() => new NavObstacleId(_nextId++);

    public void RegisterEdgeObstacle(NavObstacleId id, Vector3Int cellA, Vector3Int cellB)
    {
        NavEdge edge = new NavEdge(cellA, cellB);
        _idToEdge[id] = edge;
        // Binds ID to edge topology; passability initialization and dirty notification are deferred to SetObstaclePassable.
    }

    public void UnregisterEdgeObstacle(NavObstacleId id)
    {
        if (!_idToEdge.TryGetValue(id, out NavEdge edge))
            return;

        _idToEdge.Remove(id);

        bool wasBlocking = _idPassableState.TryGetValue(id, out bool passable) && !passable;
        _idPassableState.Remove(id);

        if (wasBlocking)
            OnEdgeDirty?.Invoke(edge.A, edge.B);
    }

    public void SetObstaclePassable(NavObstacleId id, bool passable)
    {
        if (!_idToEdge.TryGetValue(id, out NavEdge edge))
        {
            Debug.LogWarning($"NavObstacleChannel: SetObstaclePassable called with an id that was never " +
                             $"registered via the id-aware RegisterEdgeObstacle overload ({id}). Ignored.");
            return;
        }

        _idPassableState[id] = passable;
        OnEdgeDirty?.Invoke(edge.A, edge.B);
    }

    public void RegisterNavLink(NavObstacleId id, Vector3Int cellA, Vector3Int cellB, float cost, bool bidirectional)
    {
        _navLinks[id] = new NavLinkRecord(cellA, cellB, cost, bidirectional);
        NavDebug.Log($"[NavObstacleChannel] RegisterNavLink({id}): {cellA} <-> {cellB}, cost={cost}, bidirectional={bidirectional}");
        OnNavLinkRegistered?.Invoke(id, cellA, cellB, cost, bidirectional);
    }

    public void UnregisterNavLink(NavObstacleId id)
    {
        if (_navLinks.Remove(id))
            OnNavLinkUnregistered?.Invoke(id);
    }

    #endregion

    #region Query side

    public bool IsFloorPresent(Vector3Int cell) => _floorPresentCells.Contains(cell);

    public bool IsCellBlocked(Vector3Int cell) =>
        _cellObstacleRefCounts.TryGetValue(cell, out int count) && count > 0;

    public bool IsEdgeBlocked(Vector3Int cellA, Vector3Int cellB)
    {
        NavEdge edge = new NavEdge(cellA, cellB);

        if (_edgeObstacleRefCounts.TryGetValue(edge, out int count) && count > 0)
        {
            NavDebug.Log($"[NavObstacleChannel] IsEdgeBlocked({cellA}, {cellB}) = TRUE (refcount={count})");
            return true;
        }

        // scan over dynamic edges.
        foreach (var kvp in _idToEdge)
        {
            if (!kvp.Value.Equals(edge))
                continue;

            if (_idPassableState.TryGetValue(kvp.Key, out bool passable) && !passable)
                return true;
        }

        return false;
    }

    #endregion

    #region Save System Support

    /// <summary>
    /// Resets all obstacle registries without raising dirty events. Preserves _nextId to prevent ID collisions on load.
    /// </summary>
    public void Clear()
    {
        _cellObstacleRefCounts.Clear();
        _floorPresentCells.Clear();
        _edgeObstacleRefCounts.Clear();
        _idToEdge.Clear();
        _idPassableState.Clear();
        _navLinks.Clear();
    }

    #endregion

    private readonly struct NavLinkRecord
    {
        public readonly Vector3Int CellA;
        public readonly Vector3Int CellB;
        public readonly float Cost;
        public readonly bool Bidirectional;

        public NavLinkRecord(Vector3Int cellA, Vector3Int cellB, float cost, bool bidirectional)
        {
            CellA = cellA;
            CellB = cellB;
            Cost = cost;
            Bidirectional = bidirectional;
        }
    }
}