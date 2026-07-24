using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One build-height level's worth of chunked storage. Reuses Vector2Int as
/// the chunk coordinate rather than a dedicated struct - the floor is
/// already keyed separately by height in NavGrid, so a 2D coordinate is all
/// a chunk needs, and introducing a new type here would just be extra
/// ceremony for no real type-safety gain.
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
        // Proper modulo (not C#'s remainder operator) so negative cell
        // coordinates wrap correctly instead of producing a negative index.
        localX = ((cellX % _chunkSize) + _chunkSize) % _chunkSize;
        localZ = ((cellZ % _chunkSize) + _chunkSize) % _chunkSize;
    }

    /// <summary>Returns null if the chunk has never been allocated - "unbuilt", not "unknown".</summary>
    public NavChunk GetChunkOrNull(Vector2Int chunkCoord)
    {
        return _chunks.TryGetValue(chunkCoord, out NavChunk chunk) ? chunk : null;
    }

    /// <summary>Lazily allocates the chunk on first use.</summary>
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
