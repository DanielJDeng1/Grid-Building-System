using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles smooth horizontal and vertical movement along a waypoint path.
/// Decouples movement targeting from facing direction to prevent corner oscillation.
/// </summary>
public class AgentMotor : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float _moveSpeed = 3.5f;
    [SerializeField] private float _acceleration = 8f;
    [SerializeField] private float _rotationSpeed = 10f;

    [Header("Waypoint Following")]
    [SerializeField] private float _waypointReachedDistance = 0.15f;
    [Tooltip("Distance from current waypoint at which facing starts blending toward the next waypoint.")]
    [SerializeField] private float _lookAheadDistance = 0.75f;

    [Header("Vertical Traversal")]
    [Tooltip("Speed used to resolve vertical height deltas between waypoints (units/sec).")]
    [SerializeField] private float _verticalSpeed = 2f;

    private List<Vector3> _waypoints;
    private int _currentWaypointIndex;
    private Vector3 _currentVelocity;
    private float _pendingVerticalDelta;
    private float _lastDebugLogTime;

    public bool HasPath => _waypoints != null && _currentWaypointIndex < _waypoints.Count;
    public bool IsMoving => _currentVelocity.sqrMagnitude > 0.0001f || !Mathf.Approximately(_pendingVerticalDelta, 0f);

    public void SetPath(List<Vector3> worldWaypoints)
    {
        _waypoints = worldWaypoints;
        _currentWaypointIndex = 0;

        NavDebug.Log($"[AgentMotor] '{name}' SetPath received {worldWaypoints.Count} waypoints. " +
                     $"First: {(worldWaypoints.Count > 0 ? worldWaypoints[0].ToString() : "n/a")}, " +
                     $"Last: {(worldWaypoints.Count > 0 ? worldWaypoints[worldWaypoints.Count - 1].ToString() : "n/a")}");
    }

    public void ClearPath()
    {
        _waypoints = null;
        _currentWaypointIndex = 0;
        _currentVelocity = Vector3.zero;
    }

    private void Update()
    {
        ApplyPendingVerticalMovement();

        if (!HasPath)
            return;

        if (Time.time - _lastDebugLogTime > 0.5f)
        {
            _lastDebugLogTime = Time.time;
            NavDebug.Log($"[AgentMotor] '{name}' Update: waypointIndex={_currentWaypointIndex}/{_waypoints.Count}, " +
                         $"position={transform.position}, target={_waypoints[_currentWaypointIndex]}, " +
                         $"velocity={_currentVelocity}, distanceToTarget={HorizontalDistance(transform.position, _waypoints[_currentWaypointIndex]):F3}");
        }

        Vector3 currentWaypoint = _waypoints[_currentWaypointIndex];
        Vector3 toWaypoint = currentWaypoint - transform.position;
        toWaypoint.y = 0f;

        Vector3 pathFollowingForce = toWaypoint.sqrMagnitude > 0.0001f
            ? toWaypoint.normalized * _moveSpeed
            : Vector3.zero;

        Vector3 desiredVelocity = pathFollowingForce;

        _currentVelocity = Vector3.MoveTowards(_currentVelocity, desiredVelocity, _acceleration * Time.deltaTime);
        transform.position += _currentVelocity * Time.deltaTime;

        UpdateFacing();
        AdvanceWaypointIfReached();
    }

    private void UpdateFacing()
    {
        if (!IsMoving)
            return;

        Vector3 facingDirection = _currentVelocity.normalized;
        float distanceToCurrent = HorizontalDistance(transform.position, _waypoints[_currentWaypointIndex]);

        if (distanceToCurrent < _lookAheadDistance && _currentWaypointIndex + 1 < _waypoints.Count)
        {
            Vector3 toNext = _waypoints[_currentWaypointIndex + 1] - transform.position;
            toNext.y = 0f;

            if (toNext.sqrMagnitude > 0.0001f)
            {
                float blend = 1f - Mathf.Clamp01(distanceToCurrent / _lookAheadDistance);
                facingDirection = Vector3.Slerp(_currentVelocity.normalized, toNext.normalized, blend);
            }
        }

        Quaternion targetRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, _rotationSpeed * Time.deltaTime);
    }

    private void AdvanceWaypointIfReached()
    {
        Vector3 currentWaypoint = _waypoints[_currentWaypointIndex];
        float distance = HorizontalDistance(transform.position, currentWaypoint);

        if (distance > _waypointReachedDistance)
            return;

        if (_currentWaypointIndex < _waypoints.Count - 1)
        {
            float verticalDelta = _waypoints[_currentWaypointIndex + 1].y - _waypoints[_currentWaypointIndex].y;
            _pendingVerticalDelta += verticalDelta;
            _currentWaypointIndex++;
        }
        else
        {
            ClearPath();
        }
    }

    private void ApplyPendingVerticalMovement()
    {
        if (Mathf.Approximately(_pendingVerticalDelta, 0f))
            return;

        float step = Mathf.Sign(_pendingVerticalDelta) * _verticalSpeed * Time.deltaTime;
        if (Mathf.Abs(step) >= Mathf.Abs(_pendingVerticalDelta))
            step = _pendingVerticalDelta;

        transform.position += new Vector3(0f, step, 0f);
        _pendingVerticalDelta -= step;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}