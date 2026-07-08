using UnityEngine;

public interface IBuildingState
{
    void EndState();

    /// <summary>
    /// Called once on mouse-down, before any OnHold calls. Used by drag-capable
    /// states (currently GridState / GridRemovalState) to record the drag origin
    /// cell and activate the rectangle-bounds preview. States that don't support
    /// dragging (EdgeState, EdgeRemovalState) can no-op this.
    /// </summary>
    void OnActionStart(Vector3Int gridPosition);

    /// <summary>
    /// Commits the action. For non-dragging states this is a single-cell/edge
    /// placement or removal. For drag-capable states, if a drag is active this
    /// commits the full rectangle; otherwise it falls back to single-cell behavior.
    /// </summary>
    void OnAction(Vector3Int mousePosition);

    /// <summary>
    /// Called every frame the mouse position changes while the button is NOT held.
    /// Drives the normal hover preview.
    /// </summary>
    void UpdateState(Vector3Int mousePosition);

    void Rotate(Vector3Int mousePosition);

    /// <summary>
    /// Called every frame while the mouse button IS held (after OnActionStart,
    /// before OnAction). Drag-capable states use this to update the rectangle
    /// bounds preview.
    /// </summary>
    void OnHold(Vector3Int mousePosition);

}