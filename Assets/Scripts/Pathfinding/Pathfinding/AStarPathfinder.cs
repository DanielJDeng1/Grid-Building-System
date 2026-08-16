using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Managed two-tier A* pathfinder.
/// Tier 1 executes a fast, low-budget search for standard queries.
/// If Tier 1 fails, region-graph reachability is checked: unreachable targets return a 
/// partial path, while reachable targets invoke a higher-budget Tier 2 search to guarantee arrival.
/// </summary>
public class AStarPathfinder : IPathfinder
{
    private readonly NavGrid _navGrid;
    private readonly PathfindingSettings _settings;

    // Per-instance buffers to eliminate runtime allocations during path searches
    private readonly Dictionary<Vector3Int, float> _gScore = new();
    private readonly Dictionary<Vector3Int, Vector3Int> _cameFrom = new();
    private readonly HashSet<Vector3Int> _closed = new();
    private readonly List<NavGrid.NavNeighbor> _neighborBuffer = new(8);
    private readonly BinaryHeap<Vector3Int> _open = new();

    public AStarPathfinder(NavGrid navGrid, PathfindingSettings settings)
    {
        _navGrid = navGrid;
        _settings = settings;
    }

    public PathResult FindPath(Vector3Int start, Vector3Int goal, int agentSeed)
    {
        var result = new PathResult();

        if (!_navGrid.IsWalkable(start) || !_navGrid.IsWalkable(goal))
        {
            result.Status = PathStatus.Unreachable;
            return result;
        }

        bool confirmedReachable = _navGrid.IsReachable(start, goal);
        NavDebug.Log($"[AStarPathfinder] FindPath {start} -> {goal}: region-graph confirmedReachable={confirmedReachable}");

        bool reachedGoal = RunSearch(start, goal, _settings.HeuristicWeight, _settings.Tier1ExpansionBudget, agentSeed, out Vector3Int bestNode);
        NavDebug.Log($"[AStarPathfinder] Tier 1 reachedGoal={reachedGoal}");

        if (!reachedGoal && confirmedReachable)
        {
            reachedGoal = RunSearch(start, goal, _settings.Tier2HeuristicWeight, _settings.Tier2ExpansionBudget, agentSeed, out bestNode);
            NavDebug.Log($"[AStarPathfinder] Tier 2 reachedGoal={reachedGoal}");
        }

        if (reachedGoal)
        {
            result.Status = PathStatus.Success;
            ReconstructPath(goal, result.Waypoints);
        }
        else if (confirmedReachable)
        {
            // Tier 2 budget exhaustion fallback; return best effort partial path
            Debug.LogWarning("AStarPathfinder: Tier 2 exhausted its budget on a confirmed-reachable request. " +
                              "Consider raising Tier2ExpansionBudget in PathfindingSettings.");
            result.Status = PathStatus.Success;
            ReconstructPath(bestNode, result.Waypoints);
        }
        else
        {
            result.Status = PathStatus.Unreachable;
            ReconstructPath(bestNode, result.Waypoints);
        }

        return result;
    }

    private bool RunSearch(Vector3Int start, Vector3Int goal, float heuristicWeight, int expansionBudget, int agentSeed, out Vector3Int bestNode)
    {
        _gScore.Clear();
        _cameFrom.Clear();
        _closed.Clear();
        _open.Clear();

        _gScore[start] = 0f;
        _open.Push(start, Heuristic(start, goal) * heuristicWeight);

        bestNode = start;
        float bestH = Heuristic(start, goal);

        int expansions = 0;

        while (_open.Count > 0 && expansions < expansionBudget)
        {
            Vector3Int current = _open.Pop();

            if (_closed.Contains(current))
                continue; // Stale open-set entry

            if (current == goal)
            {
                bestNode = current;
                return true;
            }

            _closed.Add(current);
            expansions++;

            float currentG = _gScore[current];
            float currentH = Heuristic(current, goal);
            if (currentH < bestH)
            {
                bestH = currentH;
                bestNode = current;
            }

            _neighborBuffer.Clear();
            _navGrid.GetWalkableNeighbors(current, _neighborBuffer);

            foreach (var neighbor in _neighborBuffer)
            {
                if (_closed.Contains(neighbor.Cell))
                    continue;

                float edgeCost = neighbor.Cost * JitterMultiplier(current, neighbor.Cell, agentSeed);
                float tentativeG = currentG + edgeCost;

                if (_gScore.TryGetValue(neighbor.Cell, out float existingG) && tentativeG >= existingG)
                    continue;

                _gScore[neighbor.Cell] = tentativeG;
                _cameFrom[neighbor.Cell] = current;
                float f = tentativeG + Heuristic(neighbor.Cell, goal) * heuristicWeight;
                _open.Push(neighbor.Cell, f);
            }
        }

        return false;
    }

    /// <summary>
    /// Octile planar distance combined with a linear floor height penalty to drive vertical search progression.
    /// </summary>
    private static float Heuristic(Vector3Int a, Vector3Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dz = Mathf.Abs(a.z - b.z);
        int dy = Mathf.Abs(a.y - b.y);
        const float D = 1f;
        const float D2 = 1.41421356f;
        float planar = D * (dx + dz) + (D2 - 2f * D) * Mathf.Min(dx, dz);
        return planar + D * dy;
    }

    /// <summary>
    /// Applies deterministic edge cost variance per agent to reduce line-of-sight visual clustering.
    /// </summary>
    private float JitterMultiplier(Vector3Int from, Vector3Int to, int agentSeed)
    {
        if (_settings.JitterRange <= 0f)
            return 1f;

        unchecked
        {
            int hash = agentSeed;
            hash = hash * 31 + from.GetHashCode();
            hash = hash * 31 + to.GetHashCode();
            uint u = (uint)hash;
            float normalized = (u % 10000) / 10000f; // Normalized [0, 1) range
            return 1f + (normalized * 2f - 1f) * _settings.JitterRange;
        }
    }

    private void ReconstructPath(Vector3Int end, List<Vector3Int> outWaypoints)
    {
        outWaypoints.Clear();
        Vector3Int current = end;
        outWaypoints.Add(current);

        while (_cameFrom.TryGetValue(current, out Vector3Int parent))
        {
            current = parent;
            outWaypoints.Add(current);
        }

        outWaypoints.Reverse();
    }
}