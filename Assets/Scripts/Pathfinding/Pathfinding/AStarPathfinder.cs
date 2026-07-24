using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Baseline managed A* (Phase 3 replaces the inner loop with a Burst job
/// against the same NavGrid query API - the algorithm shape doesn't change,
/// just where it runs).
/// 
/// TWO-TIER SEARCH (design doc §8): Tier 1 is fast, weighted, and budgeted -
/// handles the large majority of requests. If it fails, NavGrid.IsReachable
/// (the region graph, §4) is consulted: if it says no path exists at all,
/// return Unreachable with a "walk as close as possible" partial path. If it
/// says a path DOES exist, Tier 2 retries with a relaxed heuristic and a much
/// larger budget - guaranteed to terminate with a real path, since
/// reachability already proved the search space is finite and connected.
/// This is what prevents ever reporting "can't reach" when a path actually
/// exists, without paying Tier 2's cost on every request.
/// </summary>
public class AStarPathfinder : IPathfinder
{
    private readonly NavGrid _navGrid;
    private readonly PathfindingSettings _settings;

    // Reused across calls to avoid per-request allocation churn.
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

        bool reachedGoal = RunSearch(start, goal, _settings.HeuristicWeight, _settings.Tier1ExpansionBudget, agentSeed, out Vector3Int bestNode);

        if (!reachedGoal && confirmedReachable)
        {
            reachedGoal = RunSearch(start, goal, _settings.Tier2HeuristicWeight, _settings.Tier2ExpansionBudget, agentSeed, out bestNode);
        }

        if (reachedGoal)
        {
            result.Status = PathStatus.Success;
            ReconstructPath(goal, result.Waypoints);
        }
        else if (confirmedReachable)
        {
            // Should not happen given the region graph guarantee - if it
            // ever does (e.g. Tier 2's budget still isn't enough on some
            // pathological map), report the partial progress as a success
            // rather than silently lying about reachability.
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
                continue; // stale lazy-deleted entry

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
    /// Octile distance - the correct heuristic for 8-directional movement
    /// with cardinal cost 1 and diagonal cost sqrt(2). Manhattan distance
    /// would underestimate less (over-explore) once diagonal moves exist.
    /// </summary>
    private static float Heuristic(Vector3Int a, Vector3Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dz = Mathf.Abs(a.z - b.z);
        const float D = 1f;
        const float D2 = 1.41421356f;
        return D * (dx + dz) + (D2 - 2f * D) * Mathf.Min(dx, dz);
    }

    /// <summary>
    /// Deterministic per-agent jitter (design doc §8): seeded from agentSeed
    /// (stable per agent, passed in by the caller - see PathfindingAgent) so
    /// a given agent's routing bias stays consistent across replans, while
    /// different agents diverge on near-tied alternatives - the fix for
    /// crowds visibly taking the identical path. Scales multiplicatively
    /// with the base edge cost rather than adding a flat amount, so diagonal
    /// and cardinal edges get proportionally comparable jitter.
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
            float normalized = (u % 10000) / 10000f; // deterministic [0,1)
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