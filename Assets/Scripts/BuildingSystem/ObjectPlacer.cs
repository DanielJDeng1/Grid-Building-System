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
/// 
/// MESH CHUNKING:
/// Floor grid objects are always chunked. Edge objects are chunked PER-TYPE, controlled by
/// EdgeData.shouldChunk:
///   - Floor objects go to FloorChunkManager, which buckets them spatially (no contiguity
///     requirement - floors never need to be independently toggled).
///   - Edge objects with shouldChunk = true (walls, fences, railings) go to WallChunkManager,
///     which groups them by CONTIGUOUS STRAIGHT RUN instead of spatial bucket - this matters
///     for future dynamic wall hiding, where a whole side of a room needs to be one
///     toggleable visual unit. A square room's 4 walls are always 4 separate chunks, since
///     contiguity never crosses an orientation change.
///   - Edge objects with shouldChunk = false (doors, edge-mounted furniture, anything needing
///     its own GameObject identity) are instantiated individually via the same free-list path
///     as Furniture/Ceiling.
/// Furniture and Ceiling grid objects are always instantiated individually, since they need
/// individual GameObject identity (scripts, physics, etc).
/// 
/// Handles returned from chunked placements are negative ints allocated via
/// ChunkHandleRegistry, which also remembers which manager owns each handle - so
/// RemoveObjectAt/RemoveEdgeAt can route correctly without knowing which chunking system
/// (or how many) are involved.
/// </summary>
public class ObjectPlacer : MonoBehaviour
{
    [SerializeField] private List<GameObject> _placedGameObjects = new List<GameObject>();

    [Tooltip("Handles chunked Floor placements. Required for PlaceObject to function for Floor build types.")]
    [SerializeField] private FloorChunkManager _floorChunkManager;

    [Tooltip("Handles chunked Wall/edge placements. Required for PlaceEdge to function.")]
    [SerializeField] private WallChunkManager _wallChunkManager;
    
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
    /// <param name="buildType">
    /// Build type from the object's ObjectData. Floor objects are routed to
    /// FloorChunkManager and never instantiated; Furniture/Ceiling fall through to the
    /// normal instantiate path.
    /// </param>
    /// <returns>
    /// Index/handle for later removal via RemoveObjectAt. Non-negative for instantiated
    /// (Furniture/Ceiling) objects, negative for chunked (Floor) objects.
    /// </returns>
    public int PlaceObject(GameObject prefab, Vector3 position, GridRotation rotation, ObjectBuildType buildType)
    {
        if (buildType == ObjectBuildType.Floor)
        {
            Matrix4x4 worldMatrix = ChunkRotationMath.GetGridObjectMatrix(position, rotation);
            return _floorChunkManager.AddEntry(prefab, position, worldMatrix);
        }

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
    /// EDGE ROTATION BEHAVIOR:
    /// - Deg0: 0° rotation (horizontal alignment along positive X-axis)
    /// - Deg90: 90° rotation (vertical alignment along positive Z-axis)
    /// 
    /// Edge GameObjects are positioned at the grid integer coordinate (the pivot).
    /// 
    /// ROTATION FIX: each child is rotated around a pivot derived from that child's
    /// OWN local X offset (position.x + child.localPosition.x, applied to both the
    /// pivot's X and Z), not a fixed tile-center constant. Edge prefabs commonly have
    /// their visual mesh offset from the pivot along local X by half the object's
    /// total length - e.g. a 1-tile wall sits at local x = 0.5, a 2-tile wall at
    /// local x = 1.0, matching how GridData.CalculateEdges lays out multi-segment
    /// edges (positionsFilled.Count segments spanning that many tiles from the base
    /// position). A fixed 0.5 pivot only happens to be correct for a 1-tile wall;
    /// for any other length it rotates around the wrong point and the mesh lands a
    /// tile (or more) away from where the logical edge coordinates say it should.
    /// Deriving the pivot from the child's own offset generalizes correctly to any
    /// wall length without ObjectPlacer needing to know positionsFilled.Count at all.
    /// This assumes the standard authoring convention: the mesh lies flat at local
    /// Z = 0 (only its local X offset is meaningful for this calculation) - a child
    /// authored with its own Z offset would need different handling.
    /// </summary>
    /// <param name="prefab">Edge GameObject prefab. Instantiated only if shouldChunk is false - see MESH CHUNKING note above.</param>
    /// <param name="position">World position of the edge (grid integer coordinate)</param>
    /// <param name="rotation">Edge rotation determining orientation</param>
    /// <param name="shouldChunk">
    /// From the edge's EdgeData. True (walls, fences, railings): routed to WallChunkManager,
    /// no GameObject instantiated. False (doors, edge-mounted furniture, anything needing its
    /// own GameObject identity): instantiated individually, same free-list path as Furniture.
    /// </param>
    /// <returns>Handle for later removal via RemoveEdgeAt - negative if chunked, non-negative if instantiated.</returns>
    public int PlaceEdge(GameObject prefab, Vector3 position, EdgeRotation rotation, bool shouldChunk)
    {
        if (shouldChunk)
        {
            // Chunked: walls, fences, railings - grouped by contiguous run, no GameObject
            // instantiated. WallChunkManager computes the world matrix internally since it
            // needs the rotation to determine which axis the run extends along.
            return _wallChunkManager.AddEntry(prefab, position, rotation);
        }

        // Non-chunked: doors, edge-mounted furniture, or anything else needing individual
        // GameObject identity (scripts, physics, animation).
        GameObject newObject = Instantiate(prefab);
        newObject.transform.position = position;

        if (rotation == EdgeRotation.Deg90)
        {
            // Rotate each child around a pivot derived from ITS OWN local X offset -
            // see ROTATION FIX note above. Skipped entirely for Deg0 since that's
            // the prefab's authored (unrotated) orientation already.
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
    /// Removes an object at the specified index/handle. Works for grid objects, edge objects,
    /// AND chunked entries (Floor grid objects, all edges) - chunked handles are negative and
    /// are routed through ChunkHandleRegistry to whichever manager (Floor or Wall) owns them.
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
    /// Removes an edge object at the specified handle. Since all edges are chunked, this will
    /// always route through ChunkHandleRegistry to WallChunkManager in practice - kept as its
    /// own method for API clarity and in case non-chunked edge types are ever reintroduced.
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