using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Throttles pathfinding requests across frames using a fixed execution budget.
/// Flushes dirty grid chunks in LateUpdate prior to query resolution so searches always evaluate against current grid state.
/// </summary>
public class PathRequestManager : MonoBehaviour
{
    [SerializeField] private NavigationService _navigationService;
    [Tooltip("Maximum pathfinding queries processed per frame to prevent frame time spikes.")]
    [SerializeField] private int _maxRequestsPerFrame = 8;

    private IPathfinder _pathfinder;
    private readonly Queue<PathRequest> _pendingRequests = new();

    private struct PathRequest
    {
        public Vector3Int Start;
        public Vector3Int Goal;
        public int AgentSeed;
        public Action<PathResult> OnComplete;
    }

    private void Start()
    {
        if (_navigationService == null)
        {
            Debug.LogError("PathRequestManager: _navigationService must be assigned in the Inspector. Disabling.");
            enabled = false;
            return;
        }

        _pathfinder = new AStarPathfinder(_navigationService.NavGrid, _navigationService.Settings);
    }

    /// <summary>
    /// Non-blocking path query entry point. Returns immediately and executes the callback upon resolution.
    /// </summary>
    public void RequestPath(Vector3Int start, Vector3Int goal, int agentSeed, Action<PathResult> onComplete)
    {
        _pendingRequests.Enqueue(new PathRequest
        {
            Start = start,
            Goal = goal,
            AgentSeed = agentSeed,
            OnComplete = onComplete
        });
        NavDebug.Log($"[PathRequestManager] Request enqueued: {start} -> {goal} (agentSeed={agentSeed}, queue depth={_pendingRequests.Count})");
    }

    private void LateUpdate()
    {
        _navigationService.NavGrid.ProcessDirtyChunks();

        int processed = 0;
        while (processed < _maxRequestsPerFrame && _pendingRequests.Count > 0)
        {
            PathRequest request = _pendingRequests.Dequeue();
            PathResult result = _pathfinder.FindPath(request.Start, request.Goal, request.AgentSeed);
            request.OnComplete?.Invoke(result);
            NavDebug.Log($"[PathRequestManager] Drained request, status={result.Status}, waypoints={result.Waypoints.Count}");
            processed++;
        }
    }
}