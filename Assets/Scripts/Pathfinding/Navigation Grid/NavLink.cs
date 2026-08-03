using UnityEngine;

/// <summary>
/// A traversal edge between two cells that isn't an ordinary grid-adjacent
/// step - stairs and elevators (design doc §5). Deliberately the SAME
/// primitive for both, differentiated only by Cost: stairs get near-zero
/// cost (walking up is just part of the path), elevators get a higher cost
/// representing wait+ride time. This is what lets A* naturally prefer
/// stairs for short trips and elevators for long vertical runs without any
/// special-cased branching in the search.
/// 
/// This struct is a plain data record - NavGrid is what actually stores
/// traversability (via INavObstacleChannel.RegisterNavLink, subscribed to
/// in NavGrid's constructor). Nothing holds onto a NavLink instance
/// long-term; TraversalState constructs one only to read out its fields
/// when calling RegisterNavLink.
/// 
/// CellA/CellB may be on different floors (that's the whole point) - unlike
/// NavEdge, ordering is NOT treated as equivalent here, since Bidirectional
/// already carries that meaning explicitly and a one-way link (a future
/// elevator-with-a-broken-down-button, say) needs A and B to stay distinct.
/// </summary>
public readonly struct NavLink
{
    public readonly Vector3Int CellA;
    public readonly Vector3Int CellB;
    public readonly float Cost;
    public readonly bool Bidirectional;

    /// <summary>
    /// Optional gate hook for future elevator occupancy/queueing logic
    /// (design doc §5: "an optional gate hook for future elevator
    /// occupancy/queueing logic"). Null means "always passable" - the
    /// common case for stairs, which have no capacity constraint. An
    /// ElevatorController (outside the nav system entirely, same pattern as
    /// DoorController in §6) can assign a delegate here later that returns
    /// false while the car is full or away on another floor, without NavGrid
    /// or AStarPathfinder needing to know elevators exist as a concept.
    /// Deliberately NOT wired into NavGrid's traversal query yet - Phase 2
    /// only needs stairs, where this is always null. Flagged here now so
    /// the field exists and Phase 2 code doesn't need to be revisited when
    /// elevator queueing is eventually built.
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