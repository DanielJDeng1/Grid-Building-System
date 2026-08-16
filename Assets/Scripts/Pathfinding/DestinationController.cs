using UnityEngine;

/// <summary>
/// Directs a pathfinding agent toward a target destination and responds when the destination cannot be reached.
/// </summary>
public class SimpleDestinationController : MonoBehaviour
{
    [SerializeField] private PathfindingAgent _agent;
    [SerializeField] private Transform _destination;

    private void Start()
    {
        if (_agent == null || _destination == null)
            return;

        _agent.OnDestinationUnreachable += HandleUnreachable;
        _agent.RequestPathTo(_destination.position);
    }

    private void OnDestroy()
    {
        if (_agent != null)
        {
            _agent.OnDestinationUnreachable -= HandleUnreachable;
        }
    }

    private void HandleUnreachable()
    {
        Debug.Log($"{name}: Destination unreachable.");
    }
}