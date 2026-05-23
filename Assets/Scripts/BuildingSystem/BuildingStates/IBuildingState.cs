using UnityEngine;

public interface IBuildingState
{
    void EndState();

    void OnAction(Vector3 mousePosition);

    void UpdateState(Vector3 mousePosition);

    void Rotate(Vector3Int gridPosition);

    void OnHold(Vector3 mousePosition);

}
