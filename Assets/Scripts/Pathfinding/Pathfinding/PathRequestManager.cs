using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Queues path requests and drains them with a per-frame budget. Per the
/// Phase 1 scope decision (design doc §13, decision 4): the external API
/// shape is built correctly now (RequestPath returns immediately, completion
/// arrives via callback, never blocks the caller) even though the drain
/// itself is synchronous underneath. Phase 3 replaces only what happens
/// INSIDE the drain (Burst-scheduled job batches instead of a foreach loop)
/// - nothing about how callers use this class needs to change then.
/// 
/// Also the drive point for NavGrid.ProcessDirtyChunks() - dirty chunks and
/// queued path requests are naturally processed together each frame, since
/// a request drained before its relevant chunks are rebuilt could compute
/// against stale walkability data.
/// 
/// INSPECTOR SETUP:
/// - Assign the scene's NavigationService.
/// - Tune _maxRequestsPerFrame - this is a request-count budget (separate
///   from PathfindingSettings' per-request expansion budgets), controlling
///   how many agents can get a new path in a single frame regardless of how
///   cheap or expensive each individual search turns out to be.
/// </summary>
public class PathRequestManager : MonoBehaviour
{
    [SerializeField] private NavigationService _navigationService;
    [Tooltip("Max number of queued requests drained per frame, independent of each request's own expansion budget.")]
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
    /// Never blocks - the result arrives later via onComplete, potentially
    /// several frames later if the queue is backed up. Callers (typically
    /// PathfindingAgent) should keep moving along their last known path (or
    /// idle) while waiting rather than stalling for a result.
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
        Debug.Log("request enqueued");

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
            Debug.Log(result.Waypoints.Count);
            processed++;
        }
    }
}
