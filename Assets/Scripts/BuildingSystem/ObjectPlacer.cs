using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Tracks instantiated and chunked object lifecycles on the grid.
/// Handles routing between standalone GameObject instantiation and batch-rendered chunk managers.
/// </summary>
public class ObjectPlacer : MonoBehaviour
{
    [SerializeField] private List<GameObject> _placedGameObjects = new List<GameObject>();

    [Tooltip("Target manager for batch-rendered floor geometry")]
    [SerializeField] private FloorChunkManager _floorChunkManager;

    [Tooltip("Target manager for batch-rendered wall and edge geometry")]
    [SerializeField] private WallChunkManager _wallChunkManager;
    
    // Recycled array indices from destroyed objects
    private Stack<int> _freeIndices = new Stack<int>();

    private const int INITIAL_CAPACITY = 64;

    private void Awake()
    {
        _placedGameObjects.Capacity = INITIAL_CAPACITY;
    }

    #region Grid Object Placement

    /// <summary>
    /// Instantiates grid entities (furniture, ceiling) or routes floor geometry to the floor chunker.
    /// </summary>
    /// <returns>Handle ID. Positive values index into _placedGameObjects; negative values represent chunk handles.</returns>
    public int PlaceObject(GameObject prefab, Vector3 position, GridRotation rotation, ObjectBuildType buildType)
    {
        if (buildType == ObjectBuildType.Floor)
        {
            Matrix4x4 worldMatrix = ChunkRotationMath.GetGridObjectMatrix(position, rotation);
            return _floorChunkManager.AddEntry(prefab, position, worldMatrix);
        }

        GameObject newObject = Instantiate(prefab);
        newObject.transform.position = position;

        Vector3 pivot = new Vector3(position.x + 0.5f, position.y, position.z + 0.5f);

        // Center-tile rotation for uniform visual orientation
        foreach (Transform child in newObject.transform)
        {
            child.transform.RotateAround(pivot, Vector3.up, (int)rotation * 90f);
        }

        return AddToPlacementList(newObject);
    }

    #endregion

    #region Edge Object Placement

    /// <summary>
    /// Instantiates standalone edge objects (doors, props) or routes contiguous wall runs to the wall chunker.
    /// </summary>
    /// <returns>Handle ID. Positive values index into _placedGameObjects; negative values represent chunk handles.</returns>
    public int PlaceEdge(GameObject prefab, Vector3 position, EdgeRotation rotation, bool shouldChunk)
    {
        if (shouldChunk)
        {
            return _wallChunkManager.AddEntry(prefab, position, rotation);
        }

        GameObject newObject = Instantiate(prefab);
        newObject.transform.position = position;

        if (rotation == EdgeRotation.Deg90)
        {
            // Offset rotation pivot per child using its local X coordinate to support multi-tile edges
            foreach (Transform child in newObject.transform)
            {
                float pivotOffset = child.localPosition.x;
                Vector3 pivot = new Vector3(position.x + pivotOffset, position.y, position.z + pivotOffset);
                child.transform.RotateAround(pivot, Vector3.up, 90f);
            }
        }

        return AddToPlacementList(newObject);
    }

    #endregion

    #region Object Removal

    /// <summary>
    /// Destroys the object or unregisters the chunk entry corresponding to the handle.
    /// </summary>
    public void RemoveObjectAt(int gameObjectIndex)
    {
        if (ChunkHandleRegistry.IsChunkedHandle(gameObjectIndex))
        {
            ChunkHandleRegistry.Remove(gameObjectIndex);
            return;
        }

        if (!IsValidIndex(gameObjectIndex))
            return;

        Destroy(_placedGameObjects[gameObjectIndex]);
        _placedGameObjects[gameObjectIndex] = null;
        _freeIndices.Push(gameObjectIndex);
    }

    /// <summary>
    /// Alias for RemoveObjectAt provided for API symmetry.
    /// </summary>
    public void RemoveEdgeAt(int gameObjectIndex)
    {
        RemoveObjectAt(gameObjectIndex);
    }

    #endregion

    #region Internal Placement Management

    private int AddToPlacementList(GameObject gameObject)
    {
        if (_freeIndices.Count > 0)
        {
            int index = _freeIndices.Pop();
            _placedGameObjects[index] = gameObject;
            return index;
        }

        _placedGameObjects.Add(gameObject);
        return _placedGameObjects.Count - 1;
    }

    private bool IsValidIndex(int index)
    {
        return index >= 0 && 
               index < _placedGameObjects.Count && 
               _placedGameObjects[index] != null;
    }

    #endregion

    #region Optional: Memory Cleanup

    /// <summary>
    /// Trims trailing nulls and rebuilds the free index stack
    /// </summary>
    public void CompactPlacementList()
    {
        while (_placedGameObjects.Count > 0 && _placedGameObjects[_placedGameObjects.Count - 1] == null)
        {
            _placedGameObjects.RemoveAt(_placedGameObjects.Count - 1);
        }

        _freeIndices.Clear();
        for (int i = 0; i < _placedGameObjects.Count; i++)
        {
            if (_placedGameObjects[i] == null)
            {
                _freeIndices.Push(i);
            }
        }

        if (_placedGameObjects.Capacity > _placedGameObjects.Count * 2)
        {
            _placedGameObjects.TrimExcess();
        }
    }

    #endregion

    #region Save System Support

    /// <summary>
    /// Destroys all standalone GameObjects and clears placement references during level unload or save reload.
    /// </summary>
    public void ClearAll()
    {
        for (int i = 0; i < _placedGameObjects.Count; i++)
        {
            if (_placedGameObjects[i] != null)
                Destroy(_placedGameObjects[i]);
        }

        _placedGameObjects.Clear();
        _freeIndices.Clear();
    }

    #endregion

    #region Debug Information

#if UNITY_EDITOR
    public struct PlacementStats
    {
        public int totalSlots;
        public int activeObjects;
        public int freeSlots;
        public float fragmentation;
    }

    public PlacementStats GetStats()
    {
        int nullCount = 0;
        int activeCount = 0;

        foreach (var obj in _placedGameObjects)
        {
            if (obj == null)
                nullCount++;
            else
                activeCount++;
        }

        return new PlacementStats
        {
            totalSlots = _placedGameObjects.Count,
            activeObjects = activeCount,
            freeSlots = _freeIndices.Count,
            fragmentation = _placedGameObjects.Count > 0 ? (float)nullCount / _placedGameObjects.Count * 100f : 0f
        };
    }
#endif

    #endregion
}