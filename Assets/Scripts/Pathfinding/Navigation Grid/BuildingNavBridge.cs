using UnityEngine;

/// <summary>
/// Bridges PlacementSystem grid occupancy events to INavObstacleChannel updates.
/// </summary>
public class BuildingNavBridge : MonoBehaviour
{
    [SerializeField] private PlacementSystem _placementSystem;
    [SerializeField] private NavigationService _navigationService;

    private INavObstacleChannel _channel;

    private void Start()
    {
        if (_placementSystem == null || _navigationService == null)
        {
            Debug.LogError("BuildingNavBridge: _placementSystem and _navigationService must both be assigned in the Inspector. Disabling.");
            enabled = false;
            return;
        }

        _channel = _navigationService.ObstacleChannel;

        // Floor presence dictates walkability; furniture and ceiling act as cell blockings.
        SubscribeLayer(_placementSystem.FloorData, isFloorLayer: true);
        SubscribeLayer(_placementSystem.FurnitureData, isFloorLayer: false);
        SubscribeLayer(_placementSystem.CeilingData, isFloorLayer: false);
    }

    private void OnDestroy()
    {
        if (_placementSystem == null)
            return;

        UnsubscribeLayer(_placementSystem.FloorData, isFloorLayer: true);
        UnsubscribeLayer(_placementSystem.FurnitureData, isFloorLayer: false);
        UnsubscribeLayer(_placementSystem.CeilingData, isFloorLayer: false);
    }

    #region Subscription management

    private void SubscribeLayer(GridData layer, bool isFloorLayer)
    {
        if (layer == null)
            return;

        if (isFloorLayer)
            layer.OnCellOccupancyChanged += HandleFloorCellOccupancyChanged;
        else
            layer.OnCellOccupancyChanged += HandleBlockingCellOccupancyChanged;

        layer.OnEdgeOccupancyChanged += HandleEdgeOccupancyChanged;
    }

    private void UnsubscribeLayer(GridData layer, bool isFloorLayer)
    {
        if (layer == null)
            return;

        if (isFloorLayer)
            layer.OnCellOccupancyChanged -= HandleFloorCellOccupancyChanged;
        else
            layer.OnCellOccupancyChanged -= HandleBlockingCellOccupancyChanged;

        layer.OnEdgeOccupancyChanged -= HandleEdgeOccupancyChanged;
    }

    #endregion

    #region Translation

    private void HandleFloorCellOccupancyChanged(Vector3Int cell, bool isNowOccupied)
    {
        _channel.RegisterFloorPresence(cell, isNowOccupied);
    }

    private void HandleBlockingCellOccupancyChanged(Vector3Int cell, bool isNowOccupied)
    {
        if (isNowOccupied)
            _channel.RegisterCellObstacle(cell);
        else
            _channel.UnregisterCellObstacle(cell);
    }

    private void HandleEdgeOccupancyChanged(Edge edge, bool isNowOccupied)
    {
        NavDebug.Log($"[BuildingNavBridge] Edge occupancy changed: {edge.end1} <-> {edge.end2}, isNowOccupied={isNowOccupied}");

        Vector3Int diff = edge.end2 - edge.end1;
        
        // Derive perpendicular vector across the edge boundary
        Vector3Int perp = (diff.x != 0) 
            ? new Vector3Int(0, 0, 1) 
            : new Vector3Int(1, 0, 0);

        // Block adjacent cell transitions across both endpoints
        ApplyOrClearEdgeObstacle(edge.end1, edge.end1 + perp, isNowOccupied);
        ApplyOrClearEdgeObstacle(edge.end1, edge.end1 - perp, isNowOccupied);
        ApplyOrClearEdgeObstacle(edge.end2, edge.end2 + perp, isNowOccupied);
        ApplyOrClearEdgeObstacle(edge.end2, edge.end2 - perp, isNowOccupied);
    }

    private void ApplyOrClearEdgeObstacle(Vector3Int tileA, Vector3Int tileB, bool isNowOccupied)
    {
        if (isNowOccupied)
            _channel.RegisterEdgeObstacle(tileA, tileB);
        else
            _channel.UnregisterEdgeObstacle(tileA, tileB);
    }

    #endregion
}