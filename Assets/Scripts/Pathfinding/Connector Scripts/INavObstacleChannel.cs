using System;
using UnityEngine;

/// <summary>
/// Interface serving as the decoupling boundary between the building system and the navigation system.
/// Defines write operations for cell/edge obstacles, query methods for navigation rebuilds, and state events.
/// </summary>
public interface INavObstacleChannel
{
    #region Write side - cell/edge obstacles

    void RegisterFloorPresence(Vector3Int cell, bool present);

    void RegisterCellObstacle(Vector3Int cell);
    void UnregisterCellObstacle(Vector3Int cell);

    void RegisterEdgeObstacle(Vector3Int cellA, Vector3Int cellB);
    void UnregisterEdgeObstacle(Vector3Int cellA, Vector3Int cellB);

    #endregion

    #region Write side - stateful registrations

    NavObstacleId AllocateId();

    void RegisterEdgeObstacle(NavObstacleId id, Vector3Int cellA, Vector3Int cellB);
    void UnregisterEdgeObstacle(NavObstacleId id);

    void SetObstaclePassable(NavObstacleId id, bool passable);

    void RegisterNavLink(NavObstacleId id, Vector3Int cellA, Vector3Int cellB, float cost, bool bidirectional);
    void UnregisterNavLink(NavObstacleId id);

    #endregion

    #region Query side

    bool IsFloorPresent(Vector3Int cell);
    bool IsCellBlocked(Vector3Int cell);
    bool IsEdgeBlocked(Vector3Int cellA, Vector3Int cellB);

    #endregion

    #region Read side (events)

    event Action<Vector3Int> OnCellDirty;
    event Action<Vector3Int, Vector3Int> OnEdgeDirty;
    event Action<NavObstacleId, Vector3Int, Vector3Int, float, bool> OnNavLinkRegistered;
    event Action<NavObstacleId> OnNavLinkUnregistered;

    #endregion

    #region Save System Support

    void Clear();

    #endregion
}