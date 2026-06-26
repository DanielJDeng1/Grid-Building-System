using UnityEngine;

/// <summary>
/// Main controller for the grid-based building system.
/// Manages building state transitions and input event binding.
/// 
/// SUPPORTED MODES:
/// - Grid object placement (floors, furniture, ceilings)
/// - Grid object removal
/// - Edge object placement (walls, fences, railings)
/// - Edge object removal
/// 
/// MULTI-LEVEL BUILDING:
/// Uses an offset-based system where placement Y-coordinate is determined by _currentBuildHeight.
/// Build height can be changed via public methods (IncreaseBuildHeight/DecreaseBuildHeight).
/// Y-level increments are configurable via _buildHeightIncrement (default: 3 units).
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

    private GridData _floorData;
    private GridData _furnitureData;
    private GridData _ceilingData;

    // NULLABLE FIX: Use nullable instead of sentinel value
    private Vector3Int? _lastDetectedPosition = null;

    private IBuildingState _buildingState;

    // MULTI-LEVEL: Current build height offset (in grid units)
    private int _currentBuildHeight = 0;

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
            
            // Refresh preview at new height if building mode is active
            RefreshPreviewAtCurrentHeight();
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
            
            // Refresh preview at new height if building mode is active
            RefreshPreviewAtCurrentHeight();
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
    /// Refreshes the preview position when build height changes.
    /// Forces an immediate UpdateState call to reposition the preview.
    /// </summary>
    private void RefreshPreviewAtCurrentHeight()
    {
        if (_buildingState != null && _lastDetectedPosition.HasValue)
        {
            // Create new grid position with updated Y-level
            Vector3Int updatedPosition = new Vector3Int(
                _lastDetectedPosition.Value.x,
                _currentBuildHeight,
                _lastDetectedPosition.Value.z
            );
            
            _buildingState.UpdateState(updatedPosition);
            _lastDetectedPosition = updatedPosition;
        }
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
        _buildingState = null;
    }

    #endregion

    #region Input Event Handling

    /// <summary>
    /// Binds InputManager events to building state methods.
    /// </summary>
    private void BindInputEvents()
    {
        _inputManager.OnMouseRelease += PlaceStructure;
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
        _inputManager.OnMouseRelease -= PlaceStructure;
        _inputManager.OnExit -= StopPlacement;
        _inputManager.OnPressR -= Rotate;
        _inputManager.OnPageUp -= IncreaseBuildHeight;
        _inputManager.OnPageDown -= DecreaseBuildHeight;
    }

    /// <summary>
    /// Handles placement action (mouse release).
    /// Delegates to active building state.
    /// </summary>
    private void PlaceStructure()
    {
        if (_inputManager.IsPointerOverUI())
            return;

        Vector3 mousePosition = _inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = _grid.WorldToCell(mousePosition);
        
        // MULTI-LEVEL: Override Y-coordinate with current build height
        gridPosition.y = _currentBuildHeight;
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
    /// Only calls UpdateState when grid position changes to minimize overhead.
    /// MULTI-LEVEL: Applies current build height to Y-coordinate.
    /// </summary>
    private void Update()
    {
        if (_buildingState == null)
            return;

        Vector3 mousePosition = _inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = _grid.WorldToCell(mousePosition);
        
        // MULTI-LEVEL: Override Y-coordinate with current build height
        gridPosition.y = _currentBuildHeight;

        // Only update state when grid position changes
        if (!_lastDetectedPosition.HasValue || _lastDetectedPosition.Value != gridPosition)
        {
            _buildingState.UpdateState(gridPosition);
            _lastDetectedPosition = gridPosition;
        }
    }

    #endregion
}