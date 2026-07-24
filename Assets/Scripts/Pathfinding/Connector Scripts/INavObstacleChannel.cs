using System;
using UnityEngine;

/// <summary>
/// The decoupling boundary between the building system and the navigation
/// system. Neither system references the other's concrete types - the
/// building system (via BuildingNavBridge) only calls the write-side methods
/// here, and the navigation system (NavGrid, Phase 1) only subscribes to the
/// read-side events and calls the query methods. This file has no dependency
/// on GridData, PlacementSystem, NavGrid, or anything Nav-internal - it
/// belongs to neither side.
/// 
/// WRITE SIDE - called by BuildingNavBridge (translating GridData occupancy),
/// and directly by future building-system code that has no GridData
/// equivalent to translate from (TraversalState for stairs/elevators,
/// DoorController for lock state).
/// 
/// QUERY SIDE - called by NavGrid when rebuilding a dirty chunk. The dirty
/// events below deliberately carry no state payload (just "this cell/edge
/// changed"), since more than one registration can affect the same
/// cell/edge - NavGrid re-reads current truth from these queries rather than
/// trying to reconstruct it from a stream of deltas.
/// 
/// READ (EVENT) SIDE - subscribed to by NavGrid to know which chunks to
/// mark dirty and rebuild via the query methods above.
/// </summary>
public interface INavObstacleChannel
{
    #region Write side - cell/edge obstacles (refcounted by key, no id needed)

    /// <summary>
    /// Floor presence has the OPPOSITE polarity from an obstacle: a cell
    /// needs a floor present to be walkable at all. Called by
    /// BuildingNavBridge whenever it observes a change on the FLOOR layer
    /// specifically (see BuildingNavBridge for how it tells layers apart).
    /// </summary>
    void RegisterFloorPresence(Vector3Int cell, bool present);

    /// <summary>
    /// Marks a cell as blocked by one more obstacle. Reference-counted, not
    /// boolean: once wall/ceiling furniture exist, more than one obstacle
    /// could plausibly overlap the same cell, and removing one shouldn't
    /// erroneously mark the cell walkable while another remains. No id
    /// parameter needed here - GridData's occupancy events are always
    /// naturally paired (occupied fires once, unoccupied fires once, for a
    /// given source), so a simple increment/decrement keyed by cell is
    /// sufficient and avoids inventing an id for every placed object.
    /// </summary>
    void RegisterCellObstacle(Vector3Int cell);
    void UnregisterCellObstacle(Vector3Int cell);

    /// <summary>
    /// Anonymous edge obstacle registration for anything that will never
    /// need to be referenced again after placement (ordinary walls). Same
    /// refcounting approach as cell obstacles. cellA/cellB order doesn't
    /// matter - implementations must treat (A,B) and (B,A) as the same edge.
    /// </summary>
    void RegisterEdgeObstacle(Vector3Int cellA, Vector3Int cellB);
    void UnregisterEdgeObstacle(Vector3Int cellA, Vector3Int cellB);

    #endregion

    #region Write side - stateful registrations (need a stable id)

    /// <summary>
    /// Allocates a new, unique id for a caller that needs to reference its
    /// own registration later - currently TraversalState (nav links) and
    /// DoorController (edge obstacles that toggle passability).
    /// </summary>
    NavObstacleId AllocateId();

    /// <summary>
    /// Id-aware edge obstacle registration, for anything that needs to
    /// toggle its own passability later via SetObstaclePassable (doors).
    /// Unlike the anonymous overload, this does NOT immediately count
    /// toward the edge's blocked state - the caller must call
    /// SetObstaclePassable right after registering to establish the
    /// starting state (e.g. a newly placed door defaults to whatever lock
    /// state DoorController says it should).
    /// </summary>
    void RegisterEdgeObstacle(NavObstacleId id, Vector3Int cellA, Vector3Int cellB);
    void UnregisterEdgeObstacle(NavObstacleId id);

    /// <summary>
    /// Toggles an id-registered obstacle's passability - this is the whole
    /// point of doors being lockable: the door stays placed, only its
    /// passability changes, potentially many times over the course of play.
    /// Only valid for ids registered via the id-aware RegisterEdgeObstacle
    /// overload above.
    /// </summary>
    void SetObstaclePassable(NavObstacleId id, bool passable);

    /// <summary>
    /// Registers a traversal link (stairs/elevators, Phase 2) between two
    /// cells, which may be on different floors. cost lets stairs (near-zero)
    /// and elevators (higher, representing wait+ride time) share the same
    /// graph primitive without any special-casing in the pathfinder.
    /// </summary>
    void RegisterNavLink(NavObstacleId id, Vector3Int cellA, Vector3Int cellB, float cost, bool bidirectional);
    void UnregisterNavLink(NavObstacleId id);

    #endregion

    #region Query side - called by NavGrid when rebuilding a dirty chunk

    bool IsFloorPresent(Vector3Int cell);
    bool IsCellBlocked(Vector3Int cell);
    bool IsEdgeBlocked(Vector3Int cellA, Vector3Int cellB);

    #endregion

    #region Read side (events) - subscribed to by NavGrid (Phase 1)

    /// <summary>Fired whenever a cell's occupancy or floor-presence state changes.</summary>
    event Action<Vector3Int> OnCellDirty;

    /// <summary>Fired whenever an edge's occupancy or passability state changes.</summary>
    event Action<Vector3Int, Vector3Int> OnEdgeDirty;

    /// <summary>Fired when a nav link is registered.</summary>
    event Action<NavObstacleId, Vector3Int, Vector3Int, float, bool> OnNavLinkRegistered;

    /// <summary>Fired when a nav link is unregistered.</summary>
    event Action<NavObstacleId> OnNavLinkUnregistered;

    #endregion
}
