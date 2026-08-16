using UnityEngine;

/// <summary>
/// Interface for grid-based placement and removal states
/// </summary>
public interface IBuildingState
{
    void EndState();

    /// <summary>
    /// Captures initial drag origin and initializes selection preview on mouse down
    /// </summary>
    void OnActionStart(Vector3Int gridPosition);

    /// <summary>
    /// Commits single-cell action or finalized drag selection
    /// </summary>
    void OnAction(Vector3Int mousePosition);

    /// <summary>
    /// Updates hover preview position and validity during pointer movement
    /// </summary>
    void UpdateState(Vector3Int mousePosition);

    void Rotate(Vector3Int mousePosition);

    /// <summary>
    /// Updates active drag bounds and visual feedback while input is held
    /// </summary>
    void OnHold(Vector3Int mousePosition);
}