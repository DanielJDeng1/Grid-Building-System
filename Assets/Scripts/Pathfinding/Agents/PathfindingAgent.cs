using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages path requests, coordinates with pathfinding services, post-processes cell paths into world-space laned waypoints, and passes them to the AgentMotor.
/// </summary>
[RequireComponent(typeof(AgentMotor))]
public class PathfindingAgent : MonoBehaviour
{
    [SerializeField] private PathRequestManager _pathRequestManager;
    [SerializeField] private NavigationService _navigationService;
    [SerializeField] private Grid _grid;
    [Tooltip("Max world-space lane offset applied to smoothed paths, in world units.")]
    [SerializeField] private float _laneOffsetRange = 0.35f;

    private AgentMotor _motor;
    private PathPostProcessor _postProcessor;
    private int _agentSeed;
    private bool _hasPendingRequest;

    public event System.Action OnDestinationUnreachable;

    private void Awake()
    {
        _motor = GetComponent<AgentMotor>();
        _agentSeed = GetInstanceID();
    }

    private void Start()
    {
        if (_pathRequestManager == null || _navigationService == null || _grid == null)
        {
            Debug.LogError($"PathfindingAgent on '{name}': _pathRequestManager, _navigationService, and _grid must all be assigned. Disabling.", this);
            enabled = false;
            return;
        }

        _postProcessor = new PathPostProcessor(_navigationService.NavGrid);
    }

    public void RequestPathTo(Vector3 worldDestination)
    {
        if (_hasPendingRequest)
            return;

        Vector3Int start = _grid.WorldToCell(transform.position);
        Vector3Int goal = _grid.WorldToCell(worldDestination);

        _hasPendingRequest = true;
        _pathRequestManager.RequestPath(start, goal, _agentSeed, HandlePathResult);
    }

    private void HandlePathResult(PathResult result)
    {
        _hasPendingRequest = false;

        NavDebug.Log($"[PathfindingAgent] '{name}' HandlePathResult: status={result.Status}, raw waypoint count={result.Waypoints.Count}");

        if (result.Waypoints.Count == 0)
        {
            _motor.ClearPath();
            return;
        }

        List<Vector3Int> simplifiedCells = _postProcessor.SimplifyLineOfSight(result.Waypoints);
        NavDebug.Log($"[PathfindingAgent] '{name}' after LOS simplify: {simplifiedCells.Count} cells");

        var worldWaypoints = new List<Vector3>(simplifiedCells.Count);
        foreach (var cell in simplifiedCells)
            worldWaypoints.Add(_grid.CellToWorld(cell));

        List<Vector3> lanedWaypoints = _postProcessor.ApplyLaneOffset(worldWaypoints, _agentSeed, _laneOffsetRange);
        NavDebug.Log($"[PathfindingAgent] '{name}' final world waypoints ({lanedWaypoints.Count})");

        _motor.SetPath(lanedWaypoints);

        if (result.Status == PathStatus.Unreachable)
        {
            OnDestinationUnreachable?.Invoke();
        }
    }
}