using UnityEngine;

public class SimpleDestinationController : MonoBehaviour
{
    [SerializeField] private PathfindingAgent _agent;
    [SerializeField] private Transform _destination;

    private void Start()
    {
        _agent.OnDestinationUnreachable += HandleUnreachable;
        _agent.RequestPathTo(_destination.position);
        Debug.Log("requesting a path");
    }

    private void HandleUnreachable()
    {
        Debug.Log($"{name}: destination unreachable - stopped as close as possible.");
        // Good place to pick a different destination/task rather than
        // leaving the agent idle at a dead end.
    }
}