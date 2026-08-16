using UnityEngine;

/// <summary>
/// Non-grid traversal edge (stairs, elevators) connecting cells across floors.
/// Uses edge cost weighting to allow standard A* pathfinding to balance stair vs. elevator usage without branching logic.
/// Transient data container used strictly for registration via INavObstacleChannel.
/// </summary>
public readonly struct NavLink
{
    public readonly Vector3Int CellA;
    public readonly Vector3Int CellB;
    public readonly float Cost;
    public readonly bool Bidirectional;

    /// <summary>
    /// Optional runtime predicate for dynamic passability (e.g., elevator capacity checks). 
    /// Null defaults to always passable.
    /// </summary>
    public readonly System.Func<bool> IsPassable;

    public NavLink(Vector3Int cellA, Vector3Int cellB, float cost, bool bidirectional, System.Func<bool> isPassable = null)
    {
        CellA = cellA;
        CellB = cellB;
        Cost = cost;
        Bidirectional = bidirectional;
        IsPassable = isPassable;
    }
}