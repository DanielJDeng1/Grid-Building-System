using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns path state and requests - does NOT move the agent itself (see
/// AgentMotor). Converts a raw PathResult (cell-space) into a smoothed,
/// world-space waypoint list: line-of-sight simplification, then Grid
/// conversion (the one place this class touches Unity's shared Grid
/// component, matching the building system's own CellToWorld usage), then
/// lane offset.
/// 
/// agentSeed is derived once at spawn (GetInstanceID()) and reused for every
/// request this agent makes - stable across replans, which is what keeps
/// this agent's routing/lane bias consistent rather than flickering.
/// 
/// INSPECTOR SETUP:
/// - Assign the scene's PathRequestManager
/// - Assign the scene's NavigationService
/// - Assign the shared Grid component (same one PlacementSystem uses)
/// - Assign an AgentMotor on the same GameObject (or a child)
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

    /// <summary>
    /// Requests a new path to the given world-space destination. Never
    /// blocks - the agent keeps its current path (or stays idle) until the
    /// result arrives, potentially several frames later if the request
    /// queue is backed up.
    /// </summary>
    public void RequestPathTo(Vector3 worldDestination)
    {
        if (_hasPendingRequest)
            return; // avoid piling up duplicate requests while one is already in flight

        Vector3Int start = _grid.WorldToCell(transform.position);
        Vector3Int goal = _grid.WorldToCell(worldDestination);

        _hasPendingRequest = true;
        _pathRequestManager.RequestPath(start, goal, _agentSeed, HandlePathResult);
    }

    private void HandlePathResult(PathResult result)
    {
        _hasPendingRequest = false;

        Debug.Log($"[DEBUG][PathfindingAgent] '{name}' HandlePathResult: status={result.Status}, raw waypoint count={result.Waypoints.Count}");

        if (result.Waypoints.Count == 0)
        {
            _motor.ClearPath();
            return;
        }

        List<Vector3Int> simplifiedCells = _postProcessor.SimplifyLineOfSight(result.Waypoints);
        Debug.Log($"[DEBUG][PathfindingAgent] '{name}' after LOS simplify: {simplifiedCells.Count} cells: {string.Join(" -> ", simplifiedCells)}");

        var worldWaypoints = new List<Vector3>(simplifiedCells.Count);
        foreach (var cell in simplifiedCells)
            worldWaypoints.Add(_grid.CellToWorld(cell));

        List<Vector3> lanedWaypoints = _postProcessor.ApplyLaneOffset(worldWaypoints, _agentSeed, _laneOffsetRange);
        Debug.Log($"[DEBUG][PathfindingAgent] '{name}' final world waypoints ({lanedWaypoints.Count}): {string.Join(" -> ", lanedWaypoints)}");

        _motor.SetPath(lanedWaypoints);

        if (result.Status == PathStatus.Unreachable)
        {
            // Walked as close as possible per the design's fallback - worth
            // exposing to higher-level AI logic so it can pick a different
            // task rather than the agent just silently stopping.
            OnDestinationUnreachable?.Invoke();
        }
    }

    /// <summary>Fired when a requested destination is confirmed unreachable (not just slow to find).</summary>
    public event System.Action OnDestinationUnreachable;
}