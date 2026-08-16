using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Main controller for the grid building system.
/// Manages active placement states, multi-floor elevation, input routing, and save/load replay.
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
    [Tooltip("Required for stair/elevator placement to register obstacle channels and nav links.")]
    [SerializeField] private NavigationService _navigationService;

    [Header("Wall Openings")]
    [SerializeField] private WallOpeningDatabase _wallOpeningDatabase;
    [Tooltip("Must match ObjectPlacer's instance so wall openings modify the active host meshes.")]
    [SerializeField] private WallChunkManager _wallChunkManager;

    [Header("Multi-Level Building")]
    [SerializeField] private int _buildHeightIncrement = 3; // Cell Y offset per floor level
    [SerializeField] private int _minBuildHeight = 0;
    [SerializeField] private int _maxBuildHeight = 30; // Max Y offset (10 floors at 3u increment)

    /// <summary>
    /// Emitted when floor elevation changes. Supplies target Y position in world space.
    /// </summary>
    public event Action<float> OnBuildHeightChanged;

    private GridData _floorData;
    private GridData _furnitureData;
    private GridData _ceilingData;
    private GridData _ceilingFurnitureData;
    private GridData _traversalData;

    private WallOpeningLinkService _wallOpeningLink;

    private Vector3Int? _lastDetectedPosition = null;

    private IBuildingState _buildingState;

    private int _currentBuildHeight = 0;

    private bool _isHolding = false;

    private void Awake()
    {
        _floorData = new();
        _furnitureData = new();
        _ceilingData = new();
        _ceilingFurnitureData = new();
        _traversalData = new();

        _wallOpeningLink = new WallOpeningLinkService(_wallChunkManager, _objectPlacer, _floorData, _furnitureData, _ceilingData);
    }

    private void OnDestroy()
    {
        _wallOpeningLink?.Dispose();
    }

    public GridData FloorData => _floorData;
    public GridData FurnitureData => _furnitureData;
    public GridData CeilingData => _ceilingData;
    public GridData CeilingFurnitureData => _ceilingFurnitureData;
    public GridData TraversalData => _traversalData;

    private void Start()
    {
        StopPlacement();
    }

    #region Multi-Level Build Height Control

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

    public void SetBuildHeight(int height)
    {
        _currentBuildHeight = Mathf.Clamp(height, _minBuildHeight, _maxBuildHeight);
        Debug.Log($"Build height set to Y={_currentBuildHeight}");
        
        RefreshPreviewAtCurrentHeight();
        NotifyBuildHeightChanged();
    }

    public int GetCurrentBuildHeight()
    {
        return _currentBuildHeight;
    }

    public int GetCurrentFloorNumber()
    {
        return _currentBuildHeight / _buildHeightIncrement;
    }

    // Re-raycasts target cell on height changes to account for angled camera perspective
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

    private void NotifyBuildHeightChanged()
    {
        OnBuildHeightChanged?.Invoke(GetCurrentBuildWorldHeight());
    }

    public float GetCurrentBuildWorldHeight()
    {
        return _grid.CellToWorld(new Vector3Int(0, _currentBuildHeight, 0)).y;
    }

    #endregion

    #region State Activation

    public void StartPlacement(int ID)
    {
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
            _ceilingData,
            _ceilingFurnitureData
        );
        
        BindInputEvents();
    }

    public void StartTraversalPlacement(int ID)
    {
        int objectIndex = _objectDatabase.objectsData.FindIndex(data => data.ID == ID);
        if (objectIndex < 0)
        {
            Debug.LogError($"PlacementSystem: Cannot start traversal placement - no object with ID {ID} found in ObjectDatabase");
            return;
        }

        if (_navigationService == null)
        {
            Debug.LogError("PlacementSystem: _navigationService must be assigned in the Inspector to place stairs/elevators.");
            return;
        }

        StopPlacement();
        _gridVisualization.SetActive(true);

        _buildingState = new TraversalState(
            ID,
            _grid,
            _previewSystem,
            _objectDatabase,
            _objectPlacer,
            _traversalData,
            _navigationService.ObstacleChannel,
            _buildHeightIncrement
        );

        BindInputEvents();
    }

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
            _ceilingData,
            _ceilingFurnitureData
        );

        BindInputEvents();
    }

    public void StartEdgePlacement(int ID)
    {
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

    public void StartWallOpeningPlacement(int ID)
    {
        int openingIndex = _wallOpeningDatabase.openingData.FindIndex(data => data.ID == ID);
        if (openingIndex < 0)
        {
            Debug.LogError($"PlacementSystem: Cannot start wall opening placement - no opening with ID {ID} found in WallOpeningDatabase");
            return;
        }

        if (_wallChunkManager == null)
        {
            Debug.LogError("PlacementSystem: _wallChunkManager must be assigned in the Inspector to place wall openings.");
            return;
        }

        StopPlacement();
        _gridVisualization.SetActive(true);

        _buildingState = new WallOpeningState(
            ID,
            _grid,
            _previewSystem,
            _wallOpeningDatabase,
            _edgeDatabase,
            _objectPlacer,
            _wallChunkManager,
            _wallOpeningLink,
            _floorData,
            _furnitureData,
            _ceilingData
        );

        BindInputEvents();
    }

    public void StartWallOpeningRemoving()
    {
        StopPlacement();
        _gridVisualization.SetActive(true);

        _buildingState = new WallOpeningRemovalState(
            _grid,
            _previewSystem,
            _wallOpeningLink
        );

        BindInputEvents();
    }

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

    private void BindInputEvents()
    {
        _inputManager.OnMouseDown += BeginAction;
        _inputManager.OnMouseRelease += CommitAction;
        _inputManager.OnExit += StopPlacement;
        _inputManager.OnPressR += Rotate;
        _inputManager.OnPageUp += IncreaseBuildHeight;
        _inputManager.OnPageDown += DecreaseBuildHeight;
    }

    private void UnbindInputEvents()
    {
        _inputManager.OnMouseDown -= BeginAction;
        _inputManager.OnMouseRelease -= CommitAction;
        _inputManager.OnExit -= StopPlacement;
        _inputManager.OnPressR -= Rotate;
        _inputManager.OnPageUp -= IncreaseBuildHeight;
        _inputManager.OnPageDown -= DecreaseBuildHeight;
    }

    private void BeginAction()
    {
        if (_inputManager.IsPointerOverUI() || _buildingState == null)
            return;

        Vector3Int gridPosition = GetCurrentGridPosition();
        _buildingState.OnActionStart(gridPosition);
        _isHolding = true;
    }

    private void CommitAction()
    {
        _isHolding = false;

        if (_inputManager.IsPointerOverUI() || _buildingState == null)
            return;

        Vector3Int gridPosition = GetCurrentGridPosition();
        _buildingState.OnAction(gridPosition);
    }

    private void Rotate()
    {
        if (!_lastDetectedPosition.HasValue)
            return;

        _buildingState.Rotate(_lastDetectedPosition.Value);
    }

    #endregion

    #region Update Loop

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

        if (!_lastDetectedPosition.HasValue || _lastDetectedPosition.Value != gridPosition)
        {
            _buildingState.UpdateState(gridPosition);
            _lastDetectedPosition = gridPosition;
        }
    }

    private Vector3Int GetCurrentGridPosition()
    {
        float worldHeight = GetCurrentBuildWorldHeight();
        Vector3 mousePosition = _inputManager.GetSelectedMapPositionAtHeight(worldHeight);
        Vector3Int gridPosition = _grid.WorldToCell(mousePosition);
        gridPosition.y = _currentBuildHeight;
        return gridPosition;
    }

    #endregion

    #region Save System

    /// <summary>
    /// Serializes active grid layers, edges, traversal paths, and wall openings into a save snapshot.
    /// </summary>
    public BuildingSaveData CaptureSaveData()
    {
        var data = new BuildingSaveData();

        CaptureGridLayer(_floorData, data.gridObjects);
        CaptureGridLayer(_furnitureData, data.gridObjects);
        CaptureGridLayer(_ceilingData, data.gridObjects);
        CaptureGridLayer(_ceilingFurnitureData, data.gridObjects);

        CaptureGridLayer(_traversalData, data.traversalObjects);

        CaptureEdgeLayer(_floorData, data.edges);
        CaptureEdgeLayer(_furnitureData, data.edges);
        CaptureEdgeLayer(_ceilingData, data.edges);

        foreach (var (openingID, basePosition, rotation) in _wallOpeningLink.GetAllOpenings())
        {
            data.openings.Add(new WallOpeningSaveEntry
            {
                id = openingID,
                basePosition = basePosition,
                rotation = rotation
            });
        }

        return data;
    }

    private static void CaptureGridLayer(GridData layer, List<PlacedObjectSaveEntry> into)
    {
        foreach (PlacedObject obj in layer.GetAllPlacedObjects())
        {
            into.Add(new PlacedObjectSaveEntry
            {
                id = obj.ID,
                basePosition = obj.basePosition,
                rotation = obj.rotation
            });
        }
    }

    private static void CaptureEdgeLayer(GridData layer, List<PlacedEdgeSaveEntry> into)
    {
        foreach (PlacedEdge edge in layer.GetAllPlacedEdges())
        {
            into.Add(new PlacedEdgeSaveEntry
            {
                id = edge.ID,
                baseEdgeEnd1 = edge.baseEdge.end1,
                baseEdgeEnd2 = edge.baseEdge.end2,
                rotation = edge.rotation
            });
        }
    }

    /// <summary>
    /// Clears current scene state and replays a saved snapshot by re-executing state placement calls.
    /// </summary>
    public void LoadSaveData(BuildingSaveData data)
    {
        if (data == null)
        {
            Debug.LogError("PlacementSystem.LoadSaveData: data is null - aborting load.");
            return;
        }

        StopPlacement();
        ResetAllBuildingState();

        ReplayGridObjects(data.gridObjects, _objectDatabase, isTraversal: false);
        ReplayEdges(data.edges);
        ReplayGridObjects(data.traversalObjects, _objectDatabase, isTraversal: true);
        ReplayOpenings(data.openings);

        if (_navigationService != null)
        {
            _navigationService.NavGrid.ProcessDirtyChunks();
        }
    }

    private void ResetAllBuildingState()
    {
        _floorData.Clear();
        _furnitureData.Clear();
        _ceilingData.Clear();
        _ceilingFurnitureData.Clear();
        _traversalData.Clear();

        _objectPlacer.ClearAll();
        _wallOpeningLink.Clear();

        if (_navigationService != null)
        {
            _navigationService.ObstacleChannel.Clear();
            _navigationService.NavGrid.Clear();
        }
    }

    private void ReplayGridObjects(List<PlacedObjectSaveEntry> entries, ObjectDatabase database, bool isTraversal)
    {
        if (entries == null)
            return;

        foreach (var group in entries.GroupBy(e => e.id))
        {
            if (isTraversal)
            {
                if (_navigationService == null)
                {
                    Debug.LogWarning($"PlacementSystem.LoadSaveData: skipping {group.Count()} traversal entr" +
                                     $"{(group.Count() == 1 ? "y" : "ies")} for ID {group.Key} - _navigationService is not assigned.");
                    continue;
                }

                TraversalState state = new TraversalState(
                    group.Key, _grid, _previewSystem, database, _objectPlacer,
                    _traversalData, _navigationService.ObstacleChannel, _buildHeightIncrement
                );

                foreach (var entry in group)
                    state.PlaceDirect(entry.basePosition);

                state.EndState();
            }
            else
            {
                GridState state = new GridState(
                    group.Key, _grid, _previewSystem, database, _objectPlacer,
                    _floorData, _furnitureData, _ceilingData, _ceilingFurnitureData
                );

                foreach (var entry in group)
                    state.PlaceDirect(entry.basePosition, entry.rotation);

                state.EndState();
            }
        }
    }

    private void ReplayEdges(List<PlacedEdgeSaveEntry> entries)
    {
        if (entries == null)
            return;

        foreach (var group in entries.GroupBy(e => e.id))
        {
            EdgeState state = new EdgeState(
                group.Key, _grid, _previewSystem, _edgeDatabase, _objectPlacer,
                _floorData, _furnitureData, _ceilingData
            );

            foreach (var entry in group)
                state.PlaceDirect(entry.baseEdgeEnd1, entry.rotation);

            state.EndState();
        }
    }

    private void ReplayOpenings(List<WallOpeningSaveEntry> entries)
    {
        if (entries == null)
            return;

        foreach (var group in entries.GroupBy(e => e.id))
        {
            WallOpeningState state = new WallOpeningState(
                group.Key, _grid, _previewSystem, _wallOpeningDatabase, _edgeDatabase,
                _objectPlacer, _wallChunkManager, _wallOpeningLink,
                _floorData, _furnitureData, _ceilingData
            );

            foreach (var entry in group)
                state.PlaceDirect(entry.basePosition, entry.rotation);

            state.EndState();
        }
    }

    #endregion
}