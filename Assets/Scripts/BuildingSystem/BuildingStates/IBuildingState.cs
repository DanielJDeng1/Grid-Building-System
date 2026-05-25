using UnityEngine;

public interface IBuildingState
{
    void EndState();

    void OnAction(Vector3Int mousePosition);

    void UpdateState(Vector3Int mousePosition);

    void Rotate(Vector3Int gridPosition);

    void OnHold(Vector3Int mousePosition);

}
