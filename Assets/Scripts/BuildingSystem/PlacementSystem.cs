using System;
using UnityEngine;

/// <summary>
/// Main controller for the grid-based building system.
/// Manages building state transitions and input event binding.
/// 
/// SUPPORTED MODES:
/// - Grid object placement (floors, furniture, ceilings) - Floor/Ceiling support rectangle drag-fill
/// - Grid object removal - supports rectangle drag-removal
/// - Edge object placement (walls, fences, railings) - single-click only
/// - Edge object removal - single-click only
/// 
/// DRAG INPUT FLOW:
/// - Mouse down  -> OnActionStart(gridPosition): records drag origin (if the active state supports it)
/// - Mouse held  -> OnHold(gridPosition): every frame, drives the drag/bounds preview
/// - Mouse up    -> OnAction(gridPosition): commits the action (single cell/edge, or full rectangle)
/// This mirrors IBuildingState exactly, so the input flow adapts per-state without
/// PlacementSystem needing to know which states support dragging.
/// 
/// MULTI-LEVEL BUILDING:
/// Uses an offset-based system where placement Y-coordinate is determined by _currentBuildHeight
/// (in grid cell units). Build height can be changed via public methods
/// (IncreaseBuildHeight/DecreaseBuildHeight). Y-level increments are configurable via
/// _buildHeightIncrement (default: 3 units).
/// 
/// MULTI-LEVEL PREVIEW/CURSOR FIX:
/// RefreshPreviewAtCurrentHeight() recomputes the cursor's grid position FROM SCRATCH via
/// GetCurrentGridPosition() every time build height changes - it does not just patch the Y
/// of a previously cached position. This matters because the camera is pitched (not a
/// top-down orthographic view): the X/Z the cursor is aiming at on the OLD height's plane is
/// generally different from the X/Z it aims at on the NEW height's plane, even though the
/// mouse hasn't moved on screen. It also routes to OnHold (not UpdateState) while a drag is
/// active, so the multi-place/multi-removal rectangle preview updates through the same path
/// it normally would.
/// 
/// CAMERA / GRID VISUAL INTEGRATION:
/// OnBuildHeightChanged fires (with the new WORLD-SPACE height, via Grid.CellToWorld)
/// whenever build height changes, so BuilderCameraController and GridSnapToView can react
/// (camera fly-to, grid visualization repositioning) without PlacementSystem needing any
/// reference to either of them.
/// 
/// ARCHITECTURE:
/// Uses State pattern to handle different building modes.
/// Each mode (GridState, EdgeState, GridRemovalState, EdgeRemovalState)
/// encapsulates its own logic for placement, validation, and preview.
/// 
/// INPUT BINDING:
/// PlacementSystem subscribes to InputManager events and delegates
/// to the active building state. Events are unsubscribed when state changes.
/// 
/// SAFETY FIX:
/// Now validates object/edge IDs before creating states to prevent
/// constructor exceptions from leaving the system in a broken state.
/// 
/// INSPECTOR SETUP:
/// - Assign InputManager reference
/// - Assign Grid component (Unity's Grid)
/// - Assign ObjectDatabase ScriptableObject
/// - Assign EdgeDatabase ScriptableObject
/// - Assign GridVisualization GameObject
/// - Assign ObjectPlacer component
/// - Assign PreviewSystem component
/// - Configure buildHeightIncrement (e.g., 3 for floors every 3 units)
/// - To enable camera fly-to and grid-visual height tracking, assign THIS
///   PlacementSystem in BuilderCameraController's and GridSnapToView's
///   Inspector reference fields. Both log a warning on Awake/OnEnable if
///   left unassigned, since that failure mode is otherwise silent.
/// </summary>
public class PlacementSystem : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private InputManager _inputManager;
    [SerializeField] private Grid _grid;
    [SerializeField] private ObjectDatabase _objectDatabase;
    [SerializeField] private EdgeDatabase _edgeDatabase;
    [SerializeField] private GameObject _gridVisualization;
    [SerializeField] private ObjectPlacer _objectPlacer;
    [SerializeField] private PreviewSystem _previewSystem;

    [Header("Multi-Level Building")]
    [SerializeField] private int _buildHeightIncrement = 3; // Y-units per floor level
    [SerializeField] private int _minBuildHeight = 0; // Minimum Y-level (ground)
    [SerializeField] private int _maxBuildHeight = 30; // Maximum Y-level (10 floors at increment of 3)

    /// <summary>
    /// Fired whenever the active build height changes, carrying the new
    /// height already converted to WORLD SPACE (via Grid.CellToWorld) so
    /// listeners like the camera or the grid visualization never need to
    /// know about grid cell sizing.
    /// </summary>
    public event Action<float> OnBuildHeightChanged;

    private GridData _floorData;
    private GridData _furnitureData;
    private GridData _ceilingData;

    // NULLABLE FIX: Use nullable instead of sentinel value
    private Vector3Int? _lastDetectedPosition = null;

    private IBuildingState _buildingState;

    // MULTI-LEVEL: Current build height offset (in grid cell units)
    private int _currentBuildHeight = 0;

    // DRAG INPUT: True from mouse-down until mouse-up. Drives whether Update()
    // (and RefreshPreviewAtCurrentHeight) calls OnHold (dragging) or
    // UpdateState (plain hover) each frame.
    private bool _isHolding = false;

    private void Start()
    {
        StopPlacement();
        
        // Initialize the three independent build layers
        _floorData = new();
        _furnitureData = new();
        _ceilingData = new();
    }

    #region Multi-Level Build Height Control

    /// <summary>
    /// Increases the build height by one floor level.
    /// Call this method from UI buttons, input events, or other systems.
    /// </summary>
    public void IncreaseBuildHeight()
    {
        int newHeight = _currentBuildHeight + _buildHeightIncrement;
        
        if (newHeight <= _maxBuildHeight)
        {
            _currentBuildHeight = newHeight;
            Debug.Log($"Build height increased to Y={_currentBuildHeight}");
            
            RefreshPreviewAtCurrentHeight();
            NotifyBuildHeightChanged();
        }
        else
        {
            Debug.LogWarning($"Cannot increase build height beyond maximum ({_maxBuildHeight})");
        }
    }

    /// <summary>
    /// Decreases the build height by one floor level.
    /// Call this method from UI buttons, input events, or other systems.
    /// </summary>
    public void DecreaseBuildHeight()
    {
        int newHeight = _currentBuildHeight - _buildHeightIncrement;
        
        if (newHeight >= _minBuildHeight)
        {
            _currentBuildHeight = newHeight;
            Debug.Log($"Build height decreased to Y={_currentBuildHeight}");
            
            RefreshPreviewAtCurrentHeight();
            NotifyBuildHeightChanged();
        }
        else
        {
            Debug.LogWarning($"Cannot decrease build height below minimum ({_minBuildHeight})");
        }
    }

    /// <summary>
    /// Sets the build height to a specific Y-level.
    /// Useful for UI sliders or direct level selection.
    /// </summary>
    /// <param name="height">Target Y-level (will be clamped to min/max)</param>
    public void SetBuildHeight(int height)
    {
        _currentBuildHeight = Mathf.Clamp(height, _minBuildHeight, _maxBuildHeight);
        Debug.Log($"Build height set to Y={_currentBuildHeight}");
        
        RefreshPreviewAtCurrentHeight();
        NotifyBuildHeightChanged();
    }

    /// <summary>
    /// Returns the current build height (Y-level) in grid units.
    /// Use this for UI display (e.g., "Floor: 2" when height is 6 and increment is 3).
    /// </summary>
    public int GetCurrentBuildHeight()
    {
        return _currentBuildHeight;
    }

    /// <summary>
    /// Returns the current floor number (0-indexed).
    /// Example: If height is 6 and increment is 3, this returns 2 (third floor).
    /// </summary>
    public int GetCurrentFloorNumber()
    {
        return _currentBuildHeight / _buildHeightIncrement;
    }

    /// <summary>
    /// Refreshes the preview/cursor position when build height changes.
    /// 
    /// FIX: Recomputes the grid position FROM SCRATCH via GetCurrentGridPosition()
    /// (a fresh raycast against the NEW height's plane) rather than patching the Y
    /// of the previously cached _lastDetectedPosition. Also routes to OnHold instead
    /// of UpdateState while a drag is active, so the multi-place/removal rectangle
    /// preview (which only updates via OnHold) is refreshed correctly too.
    /// </summary>
    private void RefreshPreviewAtCurrentHeight()
    {
        if (_buildingState == null)
            return;

        Vector3Int updatedPosition = GetCurrentGridPosition();

        if (_isHolding)
        {
            _buildingState.OnHold(updatedPosition);
        }
        else
        {
            _buildingState.UpdateState(updatedPosition);
        }

        _lastDetectedPosition = updatedPosition;
    }

    /// <summary>
    /// Fires OnBuildHeightChanged with the current build height converted to
    /// world space. Kept as one method so every height-change entry point
    /// notifies listeners identically.
    /// </summary>
    private void NotifyBuildHeightChanged()
    {
        OnBuildHeightChanged?.Invoke(GetCurrentBuildWorldHeight());
    }

    /// <summary>
    /// Converts _currentBuildHeight (grid cell units) to world-space Y via
    /// Grid.CellToWorld - the same conversion already used everywhere else in
    /// this system to position placed objects, so height-following code stays
    /// consistent with where objects actually end up in the scene. Public
    /// because the camera and GridSnapToView both need this to stay in sync.
    /// </summary>
    public float GetCurrentBuildWorldHeight()
    {
        return _grid.CellToWorld(new Vector3Int(0, _currentBuildHeight, 0)).y;
    }

    #endregion

    #region State Activation

    /// <summary>
    /// Activates grid object placement mode.
    /// SAFETY FIX: Validates ID before state creation to prevent broken state.
    /// </summary>
    /// <param name="ID">Object ID from ObjectDatabase</param>
    public void StartPlacement(int ID)
    {
        // Validate ID before state transition
        int objectIndex = _objectDatabase.objectsData.FindIndex(data => data.ID == ID);
        if (objectIndex < 0)
        {
            Debug.LogError($"PlacementSystem: Cannot start placement - no object with ID {ID} found in ObjectDatabase");
            return;
        }

        StopPlacement();
        _gridVisualization.SetActive(true);

        _buildingState = new GridState(
            ID, 
            _grid, 
            _previewSystem, 
            _objectDatabase, 
            _objectPlacer, 
            _floorData, 
            _furnitureData, 
            _ceilingData
        );
        
        BindInputEvents();
    }

    /// <summary>
    /// Activates grid object removal mode.
    /// </summary>
    public void StartRemoving()
    {
        StopPlacement();
        _gridVisualization.SetActive(true);
        
        _buildingState = new GridRemovalState(
            _grid, 
            _previewSystem, 
            _objectPlacer, 
            _floorData, 
            _furnitureData, 
            _ceilingData
        );

        BindInputEvents();
    }

    /// <summary>
    /// Activates edge object placement mode.
    /// SAFETY FIX: Validates ID before state creation to prevent broken state.
    /// </summary>
    /// <param name="ID">Edge object ID from EdgeDatabase</param>
    public void StartEdgePlacement(int ID)
    {
        // Validate ID before state transition
        int edgeIndex = _edgeDatabase.edgeData.FindIndex(data => data.ID == ID);
        if (edgeIndex < 0)
        {
            Debug.LogError($"PlacementSystem: Cannot start edge placement - no edge with ID {ID} found in EdgeDatabase");
            return;
        }

        StopPlacement();
        _gridVisualization.SetActive(true);

        _buildingState = new EdgeState(
            ID, 
            _grid, 
            _previewSystem, 
            _edgeDatabase, 
            _objectPlacer, 
            _floorData, 
            _furnitureData, 
            _ceilingData
        );

        BindInputEvents();
    }

    /// <summary>
    /// Activates edge object removal mode.
    /// </summary>
    public void StartEdgeRemoving()
    {
        StopPlacement();
        _gridVisualization.SetActive(true);

        _buildingState = new EdgeRemovalState(
            _grid, 
            _previewSystem, 
            _edgeDatabase,
            _objectPlacer, 
            _floorData, 
            _furnitureData, 
            _ceilingData
        );

        BindInputEvents();
    }

    /// <summary>
    /// Exits building mode and cleans up state.
    /// </summary>
    private void StopPlacement()
    {
        if (_buildingState == null)
            return;

        _gridVisualization.SetActive(false);
        _buildingState.EndState();
        
        UnbindInputEvents();

        _lastDetectedPosition = null;
        _isHolding = false;
        _buildingState = null;
    }

    #endregion

    #region Input Event Handling

    /// <summary>
    /// Binds InputManager events to building state methods.
    /// </summary>
    private void BindInputEvents()
    {
        _inputManager.OnMouseDown += BeginAction;
        _inputManager.OnMouseRelease += CommitAction;
        _inputManager.OnExit += StopPlacement;
        _inputManager.OnPressR += Rotate;
        _inputManager.OnPageUp += IncreaseBuildHeight;
        _inputManager.OnPageDown += DecreaseBuildHeight;
    }

    /// <summary>
    /// Unbinds InputManager events to prevent memory leaks.
    /// </summary>
    private void UnbindInputEvents()
    {
        _inputManager.OnMouseDown -= BeginAction;
        _inputManager.OnMouseRelease -= CommitAction;
        _inputManager.OnExit -= StopPlacement;
        _inputManager.OnPressR -= Rotate;
        _inputManager.OnPageUp -= IncreaseBuildHeight;
        _inputManager.OnPageDown -= DecreaseBuildHeight;
    }

    /// <summary>
    /// Handles the start of a click/drag (mouse down).
    /// Delegates to active building state's OnActionStart, which records a drag
    /// origin for states that support it and is a no-op for the rest.
    /// </summary>
    private void BeginAction()
    {
        if (_inputManager.IsPointerOverUI() || _buildingState == null)
            return;

        Vector3Int gridPosition = GetCurrentGridPosition();
        _buildingState.OnActionStart(gridPosition);
        _isHolding = true;
    }

    /// <summary>
    /// Handles the commit of a click/drag (mouse release).
    /// Delegates to active building state's OnAction.
    /// </summary>
    private void CommitAction()
    {
        _isHolding = false;

        if (_inputManager.IsPointerOverUI() || _buildingState == null)
            return;

        Vector3Int gridPosition = GetCurrentGridPosition();
        _buildingState.OnAction(gridPosition);
    }

    /// <summary>
    /// Handles rotation action (R key press).
    /// Delegates to active building state.
    /// </summary>
    private void Rotate()
    {
        if (!_lastDetectedPosition.HasValue)
            return;

        _buildingState.Rotate(_lastDetectedPosition.Value);
    }

    #endregion

    #region Update Loop

    /// <summary>
    /// Updates building state with current mouse position every frame.
    /// While the mouse button is held, routes to OnHold (drag preview);
    /// otherwise routes to UpdateState (hover preview) only when the grid
    /// position has changed, to minimize overhead.
    /// MULTI-LEVEL: Applies current build height to Y-coordinate.
    /// </summary>
    private void Update()
    {
        if (_buildingState == null)
            return;

        Vector3Int gridPosition = GetCurrentGridPosition();

        if (_isHolding)
        {
            _buildingState.OnHold(gridPosition);
            _lastDetectedPosition = gridPosition;
            return;
        }

        // Only update state when grid position changes
        if (!_lastDetectedPosition.HasValue || _lastDetectedPosition.Value != gridPosition)
        {
            _buildingState.UpdateState(gridPosition);
            _lastDetectedPosition = gridPosition;
        }
    }

    /// <summary>
    /// Converts the current mouse position to a grid cell, overriding the
    /// Y-coordinate with the current build height.
    /// 
    /// MULTI-LEVEL CURSOR FIX: raycasts against a math plane at the build
    /// height's WORLD-SPACE Y (see InputManager.GetSelectedMapPositionAtHeight),
    /// instead of always raycasting the ground plane and overwriting Y after
    /// the fact - which previously caused the cursor/preview to drift off the
    /// visual cursor position on any floor above ground level.
    /// </summary>
    private Vector3Int GetCurrentGridPosition()
    {
        float worldHeight = GetCurrentBuildWorldHeight();
        Vector3 mousePosition = _inputManager.GetSelectedMapPositionAtHeight(worldHeight);
        Vector3Int gridPosition = _grid.WorldToCell(mousePosition);
        gridPosition.y = _currentBuildHeight;
        return gridPosition;
    }

    #endregion
}