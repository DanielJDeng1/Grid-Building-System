using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages mesh-chunked floor grid objects using spatial bucketing.
/// Chunks are determined by 2D tile position and Y build height level.
/// </summary>
public class FloorChunkManager : MonoBehaviour, IChunkOwner
{
    [Header("Chunking")]
    [Tooltip("Chunk width and depth in tiles (X/Z). Y levels are chunked independently.")]
    [SerializeField] private int _chunkSize = 16;

    [Tooltip("Transform parent for generated chunk mesh GameObjects")]
    [SerializeField] private Transform _chunkParent;

    [Tooltip("Generates individual BoxColliders per floor tile to prevent false collisions over irregular gaps")]
    [SerializeField] private bool _generateColliders = true;

    private readonly Dictionary<Vector3Int, Chunk> _chunks = new();
    private readonly HashSet<Vector3Int> _dirtyChunks = new();
    private readonly Dictionary<int, Vector3Int> _handleToChunk = new();

    #region Public API

    /// <summary>
    /// Registers a floor placement into the corresponding spatial chunk
    /// </summary>
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
    /// Removes a floor entry and cleans up empty chunks if necessary
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
    /// Integer division rounding toward negative infinity for correct negative coordinate bucketing
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
    /// Rebuilds all dirty chunks once per frame in a batched pass
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