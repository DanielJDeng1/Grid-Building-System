using UnityEngine;

/// <summary>
/// Scene entry point for the navigation system. Owns the obstacle channel
/// (plain C#, performance reasons documented on NavObstacleChannel) and the
/// NavGrid built on top of it, giving every other Nav component
/// (BuildingNavBridge, PathRequestManager, and later PathfindingAgents) the
/// same Inspector-assignable integration point the rest of the project
/// already uses (PlacementSystem, PreviewSystem, ObjectPlacer).
/// 
/// INSPECTOR SETUP:
/// - Assign a PathfindingSettings asset (chunk size, heuristic weights,
///   budgets, jitter range).
/// - Place exactly one of these in the scene.
/// </summary>
public class NavigationService : MonoBehaviour
{
    [SerializeField] private PathfindingSettings _settings;

    private NavObstacleChannel _obstacleChannel;
    private NavGrid _navGrid;

    /// <summary>Exposed as the interface, not the concrete type - consumers should only ever depend on INavObstacleChannel.</summary>
    public INavObstacleChannel ObstacleChannel => _obstacleChannel;

    public NavGrid NavGrid => _navGrid;

    public PathfindingSettings Settings => _settings;

    private void Awake()
    {
        if (_settings == null)
        {
            Debug.LogError("NavigationService: no PathfindingSettings assigned - assign one in the Inspector. Disabling.");
            enabled = false;
            return;
        }

        _obstacleChannel = new NavObstacleChannel();
        _navGrid = new NavGrid(_obstacleChannel, _settings.ChunkSize);
    }

    private void OnDestroy()
    {
        _navGrid?.Dispose();
    }
}
