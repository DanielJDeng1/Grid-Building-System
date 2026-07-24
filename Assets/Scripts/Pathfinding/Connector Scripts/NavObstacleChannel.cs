using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plain C# implementation of INavObstacleChannel - not a MonoBehaviour and
/// not backed by ScriptableObject/UnityEvent, per the design doc's
/// performance rationale (§3.4): this channel can see many calls in a single
/// frame during a large multi-placement drag, and reflection-backed
/// UnityEvent invocation lists aren't worth paying for here.
/// 
/// Hosted and exposed via NavigationService, not a bare static singleton, so
/// other MonoBehaviours can wire it up through the Inspector the same way
/// every other system in this project references its dependencies.
/// 
/// THREAD SAFETY: none yet - all calls are expected on the main thread. This
/// is fine through Phase 1; Phase 3 introducing Burst jobs against a NavGrid
/// snapshot needs revisiting how job-thread reads interact with main-thread
/// writes here, but that's a NavGrid-side concern (double-buffering), not
/// something this class needs to solve itself.
/// </summary>
public class NavObstacleChannel : INavObstacleChannel
{
    // Cell obstacles: refcounted by key, no id needed (see interface docs).
    private readonly Dictionary<Vector3Int, int> _cellObstacleRefCounts = new();
    private readonly HashSet<Vector3Int> _floorPresentCells = new();

    // Anonymous edge obstacles (ordinary walls): refcounted by key.
    private readonly Dictionary<NavEdge, int> _edgeObstacleRefCounts = new();

    // Id-aware edge obstacles (doors): tracked separately since their
    // contribution to "is this edge blocked" is a toggle, not a count.
    private readonly Dictionary<NavObstacleId, NavEdge> _idToEdge = new();
    private readonly Dictionary<NavObstacleId, bool> _idPassableState = new();

    // Nav links (stairs/elevators).
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
        Debug.Log($"[DEBUG][NavObstacleChannel] RegisterEdgeObstacle: {cellA} <-> {cellB}, refcount now {count + 1}");
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
        // Deliberately no default passable state here - caller must call
        // SetObstaclePassable to establish it (see interface docs). No dirty
        // event fired yet either, since nothing about the edge's blocked
        // state has actually changed until that first SetObstaclePassable.
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
            Debug.Log($"[DEBUG][NavObstacleChannel] IsEdgeBlocked({cellA}, {cellB}) = TRUE (refcount={count})");
            return true;
        }

        // Linear scan over id-registered edges (doors) - fine while door
        // counts are small (Phase 4 scope). If this ever shows up in a
        // profile once doors are numerous, add a NavEdge -> NavObstacleId
        // reverse lookup alongside _idToEdge rather than optimizing
        // pre-emptively now.
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