using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Status classification for pathfinding queries.
/// </summary>
public enum PathStatus
{
    Pending,
    Success,
    Unreachable
}

/// <summary>
/// Encapsulates the results of a pathfinding request, including status and generated waypoints.
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
/// Core contract for pathfinding strategy implementations.
/// </summary>
public interface IPathfinder
{
    PathResult FindPath(Vector3Int start, Vector3Int goal, int agentSeed);
}