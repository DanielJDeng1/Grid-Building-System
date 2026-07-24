using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns a waypoint list into smooth, continuous motion - no cell-to-cell
/// snapping. Deliberately separate from PathfindingAgent (which owns path
/// state/requests): this class only knows about "a list of world points to
/// move through", never about NavGrid, PathRequestManager, or cells at all.
/// 
/// BUG FIX (deadlock): every distance check in this class now uses
/// horizontal-only (XZ) distance, matching how movement force already
/// ignores Y. Previously the reach-check used full 3D distance while
/// movement force zeroed Y - if the agent's Y differed from a waypoint's Y
/// (e.g. a capsule's collider pivot height vs. Grid.CellToWorld's Y=0), the
/// agent could already be exactly at the waypoint horizontally, so movement
/// force was correctly zero, while the reach-check still saw a persistent
/// vertical "distance" that movement was never trying to close - a genuine
/// deadlock, confirmed via logging (distanceToTarget stuck at exactly the
/// agent's height offset, velocity stuck at zero, forever).
/// 
/// BUG FIX: movement target and facing direction are now fully decoupled.
/// The previous version used a single distance-based "look-ahead" check to
/// decide BOTH where to move AND which way to face - once close enough to a
/// waypoint, it would switch the MOVEMENT target to the next waypoint. But
/// moving toward a sharply-angled next waypoint can increase the distance
/// back to the current one past the look-ahead threshold, flipping the
/// target back, which moves toward it again, which flips again - producing
/// exactly the oscillation reported (bobbing at corners), and very likely
/// the "walking through walls" symptom too, since that erratic trajectory
/// near a wall corner can cut across geometry the actual waypoint list never
/// included. Movement now always targets the current waypoint deterministically
/// (advanced only via a simple, one-directional reach check); only the
/// FACING direction blends toward the next waypoint as a corner is
/// approached, which can never cause positional flip-flopping since it
/// doesn't change where the agent is walking.
/// 
/// PHASE 5 EXTENSION POINT: local avoidance steering gets blended into the
/// desired-velocity calculation in Update() below (a separate force,
/// weighted-summed with the path-following force before acceleration is
/// applied) - see the comment at that call site.
/// </summary>
public class AgentMotor : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float _moveSpeed = 3.5f;
    [SerializeField] private float _acceleration = 8f;
    [SerializeField] private float _rotationSpeed = 10f;

    [Header("Waypoint Following")]
    [SerializeField] private float _waypointReachedDistance = 0.15f;
    [Tooltip("Distance from the current waypoint at which FACING starts blending toward the next one, for smoother visual turning through corners. Does not affect movement target.")]
    [SerializeField] private float _lookAheadDistance = 0.75f;

    private List<Vector3> _waypoints;
    private int _currentWaypointIndex;
    private Vector3 _currentVelocity;
    private float _lastDebugLogTime;

    public bool HasPath => _waypoints != null && _currentWaypointIndex < _waypoints.Count;
    public bool IsMoving => _currentVelocity.sqrMagnitude > 0.0001f;

    public void SetPath(List<Vector3> worldWaypoints)
    {
        _waypoints = worldWaypoints;
        _currentWaypointIndex = 0;
        Debug.Log($"[DEBUG][AgentMotor] '{name}' SetPath received {worldWaypoints.Count} waypoints. " +
                  $"First: {(worldWaypoints.Count > 0 ? worldWaypoints[0].ToString() : "n/a")}, " +
                  $"Last: {(worldWaypoints.Count > 0 ? worldWaypoints[worldWaypoints.Count - 1].ToString() : "n/a")}, " +
                  $"current transform position: {transform.position}");
    }

    public void ClearPath()
    {
        _waypoints = null;
        _currentWaypointIndex = 0;
        _currentVelocity = Vector3.zero;
    }

    private void Update()
    {
        if (!HasPath)
            return;

        // TEMPORARY DIAGNOSTIC - throttled to twice a second to avoid
        // flooding the console while still showing what's happening.
        if (Time.time - _lastDebugLogTime > 0.5f)
        {
            _lastDebugLogTime = Time.time;
            Debug.Log($"[DEBUG][AgentMotor] '{name}' Update: waypointIndex={_currentWaypointIndex}/{_waypoints.Count}, " +
                      $"position={transform.position}, target={_waypoints[_currentWaypointIndex]}, " +
                      $"velocity={_currentVelocity}, distanceToTarget={HorizontalDistance(transform.position, _waypoints[_currentWaypointIndex]):F3}");
        }

        // Movement always targets the CURRENT waypoint - deterministic, no
        // re-targeting based on distance, so there's nothing here that can
        // flip-flop.
        Vector3 currentWaypoint = _waypoints[_currentWaypointIndex];
        Vector3 toWaypoint = currentWaypoint - transform.position;
        toWaypoint.y = 0f;

        Vector3 pathFollowingForce = toWaypoint.sqrMagnitude > 0.0001f
            ? toWaypoint.normalized * _moveSpeed
            : Vector3.zero;

        // PHASE 5: local avoidance's steering force gets added here, e.g.
        // `Vector3 desiredVelocity = pathFollowingForce + avoidanceForce;`
        // then clamped to _moveSpeed - not implemented yet (out of scope
        // until Phase 5), but this is deliberately the single place that
        // decision plugs into.
        Vector3 desiredVelocity = pathFollowingForce;

        _currentVelocity = Vector3.MoveTowards(_currentVelocity, desiredVelocity, _acceleration * Time.deltaTime);
        transform.position += _currentVelocity * Time.deltaTime;

        UpdateFacing();
        AdvanceWaypointIfReached();
    }

    /// <summary>
    /// Blends facing direction toward the NEXT waypoint as the agent
    /// approaches the current one, purely for a smoother visual turn through
    /// corners. This ONLY affects rotation, never the movement target above,
    /// so it cannot reintroduce the oscillation the previous version had.
    /// </summary>
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
                float blend = 1f - Mathf.Clamp01(distanceToCurrent / _lookAheadDistance); // 0 far, 1 close
                facingDirection = Vector3.Slerp(_currentVelocity.normalized, toNext.normalized, blend);
            }
        }

        Quaternion targetRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, _rotationSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Simple, one-directional reach check against the CURRENT waypoint
    /// only - deliberately not distance-to-a-different-waypoint or any
    /// look-ahead-based logic, since that ambiguity was the root cause of
    /// the oscillation bug this class previously had.
    /// </summary>
    private void AdvanceWaypointIfReached()
    {
        Vector3 currentWaypoint = _waypoints[_currentWaypointIndex];
        float distance = HorizontalDistance(transform.position, currentWaypoint);

        if (distance > _waypointReachedDistance)
            return;

        if (_currentWaypointIndex < _waypoints.Count - 1)
            _currentWaypointIndex++;
        else
            ClearPath(); // reached the final destination
    }

    /// <summary>
    /// XZ-plane distance only, ignoring Y - matches how movement force is
    /// computed above. Using full 3D distance here was the deadlock bug:
    /// an agent whose collider pivot sits above Grid.CellToWorld's Y=0
    /// waypoints would never satisfy a 3D reach-check even when perfectly
    /// aligned horizontally, since movement never tries to close a purely
    /// vertical gap.
    /// </summary>
    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}