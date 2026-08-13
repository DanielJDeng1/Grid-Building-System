using UnityEngine;

/// <summary>
/// Building state for removing wall openings (doors, windows) without touching the host wall.
/// Single-click only. Delegates entirely to WallOpeningLinkService, which restores the host
/// wall tile(s)' original (uncut) prefab as part of removal - see
/// WallOpeningLinkService.RemoveOpening.
/// </summary>
public class WallOpeningRemovalState : IBuildingState
{
    private Grid _grid;
    private PreviewSystem _previewSystem;
    private WallOpeningLinkService _linkService;
    private EdgeRotation _currentRotation = EdgeRotation.Deg0;

    public WallOpeningRemovalState(Grid grid, PreviewSystem previewSystem, WallOpeningLinkService linkService)
    {
        _grid = grid;
        _previewSystem = previewSystem;
        _linkService = linkService;

        _previewSystem.StartShowingEdgeRemovalPreview(null, Vector3.zero);
    }

    public void EndState() => _previewSystem.StopShowingPreview();

    public void OnActionStart(Vector3Int gridPosition) { }
    public void OnHold(Vector3Int gridPosition) { }

    public void OnAction(Vector3Int gridPosition)
    {
        Edge edge = CalculateBaseEdge(gridPosition, _currentRotation);
        _linkService.RemoveOpening(edge);
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        Edge edge = CalculateBaseEdge(gridPosition, _currentRotation);
        bool isValid = _linkService.HasOpeningAt(edge);
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdatePosition(worldPosition, isValid);
    }

    public void Rotate(Vector3Int gridPosition)
    {
        _currentRotation = (EdgeRotation)(((int)_currentRotation + 1) % 2);
        Vector3 worldPosition = _grid.CellToWorld(gridPosition);
        _previewSystem.UpdateRotation(worldPosition);
        UpdateState(gridPosition);
    }

    private Edge CalculateBaseEdge(Vector3Int tilePosition, EdgeRotation rotation)
    {
        return rotation == EdgeRotation.Deg0
            ? new Edge(tilePosition, tilePosition + new Vector3Int(1, 0, 0))
            : new Edge(tilePosition, tilePosition + new Vector3Int(0, 0, 1));
    }
}
