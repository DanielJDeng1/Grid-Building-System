using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages 2D spatial chunking for a single floor height level.
/// Handles coordinate mapping between world cell locations and local chunk bounds, managing lazy allocation.
/// </summary>
public class NavFloor
{
    private readonly int _chunkSize;
    private readonly Dictionary<Vector2Int, NavChunk> _chunks = new();

    public NavFloor(int chunkSize)
    {
        _chunkSize = chunkSize;
    }

    public int ChunkSize => _chunkSize;

    public Vector2Int GetChunkCoord(int cellX, int cellZ)
    {
        return new Vector2Int(
            Mathf.FloorToInt(cellX / (float)_chunkSize),
            Mathf.FloorToInt(cellZ / (float)_chunkSize)
        );
    }

    public void GetLocalCoord(int cellX, int cellZ, out int localX, out int localZ)
    {
        // Non-negative modulo mapping for world-to-chunk coordinate wrapping.
        localX = ((cellX % _chunkSize) + _chunkSize) % _chunkSize;
        localZ = ((cellZ % _chunkSize) + _chunkSize) % _chunkSize;
    }

    /// <summary>Returns the chunk at the given coordinate, or null if unallocated.</summary>
    public NavChunk GetChunkOrNull(Vector2Int chunkCoord)
    {
        return _chunks.TryGetValue(chunkCoord, out NavChunk chunk) ? chunk : null;
    }

    /// <summary>Lazily allocates a new NavChunk if not previously instantiated.</summary>
    public NavChunk GetOrCreateChunk(Vector2Int chunkCoord)
    {
        if (!_chunks.TryGetValue(chunkCoord, out NavChunk chunk))
        {
            chunk = new NavChunk(_chunkSize);
            _chunks[chunkCoord] = chunk;
        }
        return chunk;
    }

    public IEnumerable<KeyValuePair<Vector2Int, NavChunk>> AllChunks => _chunks;
}