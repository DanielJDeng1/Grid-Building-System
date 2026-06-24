using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages instantiation, destruction, and lifecycle of placed GameObjects.
/// 
/// PERFORMANCE OPTIMIZATION:
/// Uses a free list to track available indices rather than iterating through
/// the entire list searching for null entries. This reduces placement from O(n)
/// to O(1) in the average case.
/// 
/// When objects are removed, their indices are added to the free list for reuse.
/// This prevents unbounded memory growth while maintaining constant-time operations.
/// 
/// ARCHITECTURE NOTE:
/// Both grid objects and edge objects share the same placement list since they're
/// both GameObjects with identical lifecycle management. This reduces code duplication
/// and simplifies the object tracking system.
/// </summary>
public class ObjectPlacer : MonoBehaviour
{
    [SerializeField] private List<GameObject> _placedGameObjects = new List<GameObject>();
    
    // Free list: tracks indices where GameObjects have been destroyed and can be reused
    // Stack provides O(1) push/pop operations for index recycling
    private Stack<int> _freeIndices = new Stack<int>();

    private const int INITIAL_CAPACITY = 64;

    private void Awake()
    {
        // Pre-allocate to reduce early reallocations
        _placedGameObjects.Capacity = INITIAL_CAPACITY;
    }

    #region Grid Object Placement

    /// <summary>
    /// Places a grid object (floor, furniture, ceiling) at the specified position with rotation.
    /// </summary>
    /// <param name="prefab">GameObject prefab to instantiate</param>
    /// <param name="position">World position (typically from Grid.CellToWorld)</param>
    /// <param name="rotation">Grid rotation enum determining Y-axis rotation</param>
    /// <returns>Index in the placement list for later removal reference</returns>
    public int PlaceObject(GameObject prefab, Vector3 position, GridRotation rotation)
    {
        GameObject newObject = Instantiate(prefab);
        newObject.transform.position = position;

        // Calculate pivot at tile center for proper rotation
        Vector3 pivot = new Vector3(position.x + 0.5f, position.y, position.z + 0.5f);

        // Rotate all child transforms around the tile center
        // This maintains proper visual alignment regardless of prefab structure
        foreach (Transform child in newObject.transform)
        {
            child.transform.RotateAround(pivot, Vector3.up, (int)rotation * 90f);
        }

        return AddToPlacementList(newObject);
    }

    #endregion

    #region Edge Object Placement

    /// <summary>
    /// Places an edge object (wall, fence, railing) at the specified position with rotation.
    /// 
    /// EDGE ROTATION BEHAVIOR (CORRECTED):
    /// - Deg0: 0° rotation (horizontal alignment along positive X-axis)
    /// - Deg90: -90° rotation (vertical alignment along negative Z-axis)
    /// 
    /// Edge GameObjects are positioned at the grid integer coordinate (the pivot).
    /// Rotation is applied to the PARENT GameObject's transform, not individual children.
    /// </summary>
    /// <param name="prefab">Edge GameObject prefab to instantiate</param>
    /// <param name="position">World position of the edge (grid integer coordinate)</param>
    /// <param name="rotation">Edge rotation determining orientation</param>
    /// <returns>Index in the placement list for later removal reference</returns>
    public int PlaceEdge(GameObject prefab, Vector3 position, EdgeRotation rotation)
    {
        GameObject newObject = Instantiate(prefab);
        newObject.transform.position = position;

        // Apply rotation to parent GameObject transform
        // Deg0: 0° (horizontal - along positive X-axis)
        // Deg90: -90° (vertical - along negative Z-axis)
        float rotationAngle = rotation == EdgeRotation.Deg0 ? 0f : -90f;
        newObject.transform.Rotate(Vector3.up, rotationAngle);

        return AddToPlacementList(newObject);
    }

    #endregion

    #region Object Removal

    /// <summary>
    /// Removes an object at the specified index and returns the index to the free list.
    /// Works for both grid objects and edge objects.
    /// </summary>
    public void RemoveObjectAt(int gameObjectIndex)
    {
        if (!IsValidIndex(gameObjectIndex))
            return;

        Destroy(_placedGameObjects[gameObjectIndex]);
        _placedGameObjects[gameObjectIndex] = null;
        _freeIndices.Push(gameObjectIndex);
    }

    /// <summary>
    /// Removes an edge object at the specified index and returns the index to the free list.
    /// Functionally identical to RemoveObjectAt - kept for API clarity.
    /// </summary>
    public void RemoveEdgeAt(int gameObjectIndex)
    {
        RemoveObjectAt(gameObjectIndex);
    }

    #endregion

    #region Internal Placement Management

    /// <summary>
    /// Adds a GameObject to the placement list, reusing a free index if available.
    /// This is the core optimization that prevents O(n) iteration on every placement.
    /// </summary>
    private int AddToPlacementList(GameObject gameObject)
    {
        // Reuse free index if available (O(1) operation)
        if (_freeIndices.Count > 0)
        {
            int index = _freeIndices.Pop();
            _placedGameObjects[index] = gameObject;
            return index;
        }

        // Otherwise append to list
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
    /// Optional method to compact the placement list by removing trailing null entries.
    /// This can be called periodically during low-activity periods (e.g., between levels)
    /// to reduce memory overhead if many objects at the end of the list have been removed.
    /// 
    /// WARNING: Only call this when no placements are occurring to avoid index invalidation.
    /// </summary>
    public void CompactPlacementList()
    {
        // Remove trailing nulls
        while (_placedGameObjects.Count > 0 && _placedGameObjects[_placedGameObjects.Count - 1] == null)
        {
            _placedGameObjects.RemoveAt(_placedGameObjects.Count - 1);
        }

        // Rebuild free list to only include valid indices
        _freeIndices.Clear();
        for (int i = 0; i < _placedGameObjects.Count; i++)
        {
            if (_placedGameObjects[i] == null)
            {
                _freeIndices.Push(i);
            }
        }

        // Trim excess capacity if list has shrunk significantly
        if (_placedGameObjects.Capacity > _placedGameObjects.Count * 2)
        {
            _placedGameObjects.TrimExcess();
        }
    }

    #endregion

    #region Debug Information

#if UNITY_EDITOR
    /// <summary>
    /// Provides debug information about memory usage and fragmentation.
    /// Visible in Inspector during Play mode.
    /// </summary>
    [System.Serializable]
    public struct PlacementStats
    {
        public int totalSlots;
        public int activeObjects;
        public int freeSlots;
        public float fragmentation; // Percentage of null entries
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
