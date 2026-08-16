using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Path post-processor handling cell-space line-of-sight smoothing and world-space lane offsets.
/// Methods are intentionally split between cell space (NavGrid dependent) and world space (pure geometry)
/// </summary>
public class PathPostProcessor
{
    private readonly NavGrid _navGrid;
    private readonly int _maxShortcutAttemptsPerStep;

    public PathPostProcessor(NavGrid navGrid, int maxShortcutAttemptsPerStep = 6)
    {
        _navGrid = navGrid;
        _maxShortcutAttemptsPerStep = maxShortcutAttemptsPerStep;
    }

    /// <summary>
    /// Smooths grid staircasing by skipping intermediate waypoints when line of sight exists.
    /// Search depth is capped per waypoint to bound processing time.
    /// </summary>
    public List<Vector3Int> SimplifyLineOfSight(List<Vector3Int> waypoints)
    {
        if (waypoints.Count <= 2)
            return new List<Vector3Int>(waypoints);

        var result = new List<Vector3Int> { waypoints[0] };
        int currentIndex = 0;

        while (currentIndex < waypoints.Count - 1)
        {
            int furthestVisible = currentIndex + 1;

            int maxTestIndex = Mathf.Min(waypoints.Count - 1, currentIndex + 1 + _maxShortcutAttemptsPerStep);
            for (int testIndex = currentIndex + 2; testIndex <= maxTestIndex; testIndex++)
            {
                if (HasLineOfSight(waypoints[currentIndex], waypoints[testIndex]))
                    furthestVisible = testIndex;
                else
                    break; // Stop at first occlusion
            }

            result.Add(waypoints[furthestVisible]);
            currentIndex = furthestVisible;
        }

        return result;
    }

    /// <summary>
    /// Performs a Bresenham 2D grid raycast on same-elevation cells using standard A* traversal checks.
    /// </summary>
    private bool HasLineOfSight(Vector3Int a, Vector3Int b)
    {
        if (a.y != b.y)
            return false;

        int x = a.x, z = a.z;
        int x1 = b.x, z1 = b.z;
        int dx = Mathf.Abs(x1 - x), dz = Mathf.Abs(z1 - z);
        int sx = x < x1 ? 1 : -1;
        int sz = z < z1 ? 1 : -1;
        int err = dx - dz;

        while (x != x1 || z != z1)
        {
            int e2 = 2 * err;
            int nextX = x, nextZ = z;

            if (e2 > -dz) { err -= dz; nextX += sx; }
            if (e2 < dx) { err += dx; nextZ += sz; }

            Vector3Int current = new Vector3Int(x, a.y, z);
            Vector3Int next = new Vector3Int(nextX, a.y, nextZ);

            bool diagonalStep = nextX != x && nextZ != z;
            bool traversable = diagonalStep
                ? _navGrid.CanTraverseDiagonal(current, next)
                : _navGrid.CanTraverseCardinal(current, next);

            if (!traversable)
                return false;

            x = nextX;
            z = nextZ;
        }

        return true;
    }

    /// <summary>
    /// Offsets waypoints perpendicular to movement vectors using an agent seed to stagger multi-agent movement across corridors.
    /// </summary>
    public List<Vector3> ApplyLaneOffset(List<Vector3> worldWaypoints, int agentSeed, float maxOffset)
    {
        if (worldWaypoints.Count < 2 || maxOffset <= 0f)
            return new List<Vector3>(worldWaypoints);

        float offsetAmount = SeededOffset(agentSeed, maxOffset);
        var result = new List<Vector3>(worldWaypoints.Count);

        for (int i = 0; i < worldWaypoints.Count; i++)
        {
            Vector3 point = worldWaypoints[i];

            Vector3 direction;
            if (i == 0)
                direction = (worldWaypoints[1] - worldWaypoints[0]).normalized;
            else if (i == worldWaypoints.Count - 1)
                direction = (worldWaypoints[i] - worldWaypoints[i - 1]).normalized;
            else
                direction = (worldWaypoints[i + 1] - worldWaypoints[i - 1]).normalized;

            Vector3 perpendicular = new Vector3(-direction.z, 0f, direction.x);
            result.Add(point + perpendicular * offsetAmount);
        }

        return result;
    }

    private static float SeededOffset(int agentSeed, float maxOffset)
    {
        unchecked
        {
            uint u = (uint)agentSeed * 2654435761u; // Knuth hash
            float normalized = (u % 10000) / 10000f;
            return (normalized * 2f - 1f) * maxOffset;
        }
    }
}