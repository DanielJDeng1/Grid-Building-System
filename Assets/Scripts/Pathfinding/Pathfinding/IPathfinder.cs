using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Success: a real path to the goal. Unreachable: the region graph confirmed
/// no path exists at all - Waypoints still contains a "walk as close as
/// possible" partial path rather than being empty. Pending: not used by
/// AStarPathfinder directly (it runs synchronously) but reserved for
/// PathRequestManager's queued results before a request has been drained.
/// </summary>
public enum PathStatus
{
    Pending,
    Success,
    Unreachable
}

/// <summary>
/// Carries an explicit status rather than an ambiguous empty list, so agent
/// logic can distinguish "still computing" from "genuinely no path exists"
/// (design doc §7). Waypoints is reused across requests in Phase 3's pooled
/// version; Phase 1 keeps a plain managed List for simplicity.
/// </summary>
public class PathResult
{
    public PathStatus Status = PathStatus.Pending;
    public readonly List<Vector3Int> Waypoints = new();

    public void Reset()
    {
        Status = PathStatus.Pending;
        Waypoints.Clear();
    }
}

/// <summary>
/// Strategy interface (design doc §7) - lets the algorithm (A* now, a
/// flow-field variant potentially later for shared-destination crowds) swap
/// without touching callers. agentSeed drives the per-agent jitter (§8) -
/// stable per agent, not reseeded per call, so a given agent's routing bias
/// doesn't flicker across replans.
/// </summary>
public interface IPathfinder
{
    PathResult FindPath(Vector3Int start, Vector3Int goal, int agentSeed);
}
