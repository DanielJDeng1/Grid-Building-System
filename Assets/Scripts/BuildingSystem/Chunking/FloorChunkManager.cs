using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages mesh-chunked Floor grid objects using simple spatial bucketing - a Floor object's
/// chunk is determined purely by which (chunkX, buildHeightY, chunkZ) bucket its position
/// falls into. There is no contiguity requirement for floors (unlike walls - see
/// WallChunkManager), since floors never need to be independently toggled/hidden.
///
/// Furniture and Ceiling grid objects are NOT chunked and continue to be handled by
/// ObjectPlacer's existing per-object instantiation path.
///
/// ARCHITECTURE:
/// Chunked entries are never instantiated as GameObjects. ObjectPlacer bakes a world
/// transform matrix for each placement and hands it here along with the prefab reference;
/// this class stores it, buckets it into the correct chunk by world position, and rebuilds
/// that chunk's single combined mesh on a batched end-of-frame pass (see LateUpdate).
///
/// CHUNK KEY:
/// Chunks are keyed by (chunkX, buildHeightY, chunkZ) - the Y-level is NOT divided into the
/// chunk grid, so each build floor level gets its own independent set of chunks. This matches
/// how floors are already placed at discrete, exact Y heights.
/// </summary>
public class FloorChunkManager : MonoBehaviour, IChunkOwner
{
    [Header("Chunking")]
    [Tooltip("Chunk width/depth in tiles (X/Z). Does not affect Y - each build height level is chunked independently.")]
    [SerializeField] private int _chunkSize = 16;

    [Tooltip("Optional parent transform for generated chunk mesh GameObjects. Defaults to this GameObject's transform.")]
    [SerializeField] private Transform _chunkParent;

    [Tooltip("Generates one BoxCollider per placed floor tile (not one per chunk - floor chunks aren't guaranteed contiguous, so a single bounding box could create false collision over gaps/holes in irregular room shapes).")]
    [SerializeField] private bool _generateColliders = true;

    private readonly Dictionary<Vector3Int, Chunk> _chunks = new();
    private readonly HashSet<Vector3Int> _dirtyChunks = new();
    private readonly Dictionary<int, Vector3Int> _handleToChunk = new();

    #region Public API

    /// <summary>
    /// Registers a chunked Floor placement.
    /// </summary>
    /// <param name="prefab">Prefab asset reference (NOT instantiated).</param>
    /// <param name="anchorPosition">World placement position - determines which chunk this entry belongs to.</param>
    /// <param name="worldMatrix">Baked world transform, see ChunkRotationMath.</param>
    /// <returns>A negative handle (from ChunkHandleRegistry) for later removal.</returns>
    public int AddEntry(GameObject prefab, Vector3 anchorPosition, Matrix4x4 worldMatrix)
    {
        Vector3Int chunkCoord = GetChunkCoord(anchorPosition);
        Chunk chunk = GetOrCreateChunk(chunkCoord);

        int handle = ChunkHandleRegistry.Register(this);
        chunk.AddEntry(handle, new ChunkEntry(prefab, worldMatrix));
        _handleToChunk[handle] = chunkCoord;

        MarkDirty(chunkCoord);
        return handle;
    }

    /// <summary>
    /// Removes a previously-added entry and marks its chunk dirty for rebuild.
    /// Called by ChunkHandleRegistry - do not call directly from ObjectPlacer.
    /// </summary>
    public void RemoveEntry(int handle)
    {
        if (!_handleToChunk.TryGetValue(handle, out Vector3Int chunkCoord))
            return;

        if (_chunks.TryGetValue(chunkCoord, out Chunk chunk))
        {
            chunk.RemoveEntry(handle);
            MarkDirty(chunkCoord);
            TryRemoveEmptyChunk(chunkCoord, chunk);
        }

        _handleToChunk.Remove(handle);
    }

    #endregion

    #region Chunk Lookup / Coordinates

    private Vector3Int GetChunkCoord(Vector3 worldPosition)
    {
        Vector3Int gridPosition = Vector3Int.RoundToInt(worldPosition);

        return new Vector3Int(
            FloorDiv(gridPosition.x, _chunkSize),
            gridPosition.y,
            FloorDiv(gridPosition.z, _chunkSize)
        );
    }

    /// <summary>
    /// Integer division that rounds toward negative infinity instead of toward zero, so
    /// negative tile coordinates fall into the correct (negative) chunk.
    /// </summary>
    private static int FloorDiv(int a, int b)
    {
        int q = a / b;
        if (a % b != 0 && (a < 0) != (b < 0))
            q--;
        return q;
    }

    private Chunk GetOrCreateChunk(Vector3Int chunkCoord)
    {
        if (!_chunks.TryGetValue(chunkCoord, out Chunk chunk))
        {
            Transform parent = _chunkParent != null ? _chunkParent : transform;
            string debugName = $"FloorChunk_{chunkCoord.x}_{chunkCoord.y}_{chunkCoord.z}";
            ColliderMode colliderMode = _generateColliders ? ColliderMode.PerEntryBox : ColliderMode.None;
            chunk = new Chunk(debugName, parent, colliderMode);
            _chunks[chunkCoord] = chunk;
        }

        return chunk;
    }

    private void TryRemoveEmptyChunk(Vector3Int chunkCoord, Chunk chunk)
    {
        if (!chunk.IsEmpty)
            return;

        _chunks.Remove(chunkCoord);
        _dirtyChunks.Remove(chunkCoord);
        chunk.DestroySelf();
    }

    private void MarkDirty(Vector3Int chunkCoord)
    {
        _dirtyChunks.Add(chunkCoord);
    }

    #endregion

    #region Batched Rebuild

    /// <summary>
    /// Rebuilds every dirty chunk once per frame, regardless of how many placements/removals
    /// touched it this frame.
    /// </summary>
    private void LateUpdate()
    {
        if (_dirtyChunks.Count == 0)
            return;

        foreach (Vector3Int coord in _dirtyChunks)
        {
            if (_chunks.TryGetValue(coord, out Chunk chunk))
                chunk.Rebuild();
        }

        _dirtyChunks.Clear();
    }

    #endregion
}