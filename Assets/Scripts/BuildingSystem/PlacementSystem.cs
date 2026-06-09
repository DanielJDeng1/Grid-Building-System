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
/// ARCHITECTURE:
/// Uses State pattern to handle different building modes.
/// Each mode (GridState, EdgeState, GridRemovalState, EdgeRemovalState)
/// encapsulates its own logic for placement, validation, and preview.
/// 
/// INPUT BINDING:
/// PlacementSystem subscribes to InputManager events and delegates
/// to the active building state. Events are unsubscribed when state changes.
/// 
/// INSPECTOR SETUP:
/// - Assign InputManager reference
/// - Assign Grid component (Unity's Grid)
/// - Assign ObjectDatabase ScriptableObject
/// - Assign EdgeDatabase ScriptableObject
/// - Assign GridVisualization GameObject
/// - Assign ObjectPlacer component
/// - Assign PreviewSystem component
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

    private GridData _floorData;
    private GridData _furnitureData;
    private GridData _ceilingData;

    private Vector3Int _lastDetectedPosition = new Vector3Int(0, -999, 0);

    private IBuildingState _buildingState;

    private void Start()
    {
        StopPlacement();
        
        // Initialize the three independent build layers
        _floorData = new();
        _furnitureData = new();
        _ceilingData = new();
    }

    #region State Activation

    /// <summary>
    /// Activates grid object placement mode.
    /// </summary>
    /// <param name="ID">Object ID from ObjectDatabase</param>
    public void StartPlacement(int ID)
    {
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
    /// </summary>
    /// <param name="ID">Edge object ID from EdgeDatabase</param>
    public void StartEdgePlacement(int ID)
    {
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

        _lastDetectedPosition = Vector3Int.zero;
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
    }

    /// <summary>
    /// Unbinds InputManager events to prevent memory leaks.
    /// </summary>
    private void UnbindInputEvents()
    {
        _inputManager.OnMouseRelease -= PlaceStructure;
        _inputManager.OnExit -= StopPlacement;
        _inputManager.OnPressR -= Rotate;
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

        _buildingState.OnAction(gridPosition);
    }

    /// <summary>
    /// Handles rotation action (R key press).
    /// Delegates to active building state.
    /// </summary>
    private void Rotate()
    {
        _buildingState.Rotate(_lastDetectedPosition);
    }

    #endregion

    #region Update Loop

    /// <summary>
    /// Updates building state with current mouse position every frame.
    /// Only calls UpdateState when grid position changes to minimize overhead.
    /// </summary>
    private void Update()
    {
        if (_buildingState == null)
            return;

        Vector3 mousePosition = _inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = _grid.WorldToCell(mousePosition);

        // Only update state when grid position changes
        if (_lastDetectedPosition != gridPosition)
        {
            _buildingState.UpdateState(gridPosition);
            _lastDetectedPosition = gridPosition;
        }
    }

    #endregion
}
