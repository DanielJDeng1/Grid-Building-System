using UnityEngine;

/// <summary>
/// Scene entry point for the navigation system. Manages the lifetime of the
/// obstacle channel and navigation grid instance using configured settings.
/// </summary>
public class NavigationService : MonoBehaviour
{
    [SerializeField] private PathfindingSettings _settings;

    private NavObstacleChannel _obstacleChannel;
    private NavGrid _navGrid;

    public INavObstacleChannel ObstacleChannel => _obstacleChannel;
    public NavGrid NavGrid => _navGrid;
    public PathfindingSettings Settings => _settings;

    private void Awake()
    {
        if (_settings == null)
        {
            Debug.LogError("NavigationService: PathfindingSettings missing. Disabling service.", this);
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