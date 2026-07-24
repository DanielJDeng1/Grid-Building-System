using UnityEngine;

/// <summary>
/// The single point of contact between the building system and the
/// navigation system. Subscribes to all three GridData instances'
/// occupancy events (via PlacementSystem's read-only accessors) and
/// translates them into INavObstacleChannel calls.
/// 
/// This is the only class in the project that references both PlacementSystem
/// (or GridData) and INavObstacleChannel. Neither of those two systems knows
/// this class exists - remove it, and both continue to compile and function
/// independently; that's the whole point of the contract in §3 of the design
/// doc.
/// 
/// LAYER SEMANTICS:
/// Floor has the OPPOSITE polarity from an obstacle - a cell needs floor
/// PRESENT to be walkable, so floor occupancy maps to RegisterFloorPresence,
/// not RegisterCellObstacle. Furniture and Ceiling both block uniformly
/// (current simplifying assumption - see design doc for the ceiling caveat).
/// Edges (walls/fences/railings) block regardless of which of the three
/// GridData instances they're registered on.
/// 
/// TIMING:
/// Relies on PlacementSystem constructing its three GridData instances in
/// Awake() (see PlacementSystem.Awake() comments) and this bridge reading
/// them in its own Start() - Unity guarantees every Awake() in the scene
/// completes before any Start() runs, so this is safe regardless of script
/// execution order between the two components.
/// 
/// INSPECTOR SETUP:
/// - Assign the scene's PlacementSystem
/// - Assign the scene's NavigationService
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
        Debug.Log($"[DEBUG][BuildingNavBridge] Edge occupancy changed: {edge.end1} <-> {edge.end2}, isNowOccupied={isNowOccupied}");

        if (isNowOccupied)
            _channel.RegisterEdgeObstacle(edge.end1, edge.end2);
        else
            _channel.UnregisterEdgeObstacle(edge.end1, edge.end2);
    }

    #endregion
}