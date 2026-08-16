using UnityEngine;

/// <summary>
/// Represents a single placed instance (Floor or Edge/wall object) living inside a mesh chunk
/// Stores prefab reference and baked world transform matrix without individual GameObject instantiation
/// </summary>
public readonly struct ChunkEntry
{
    public readonly GameObject prefab;
    public readonly Matrix4x4 worldMatrix;

    public ChunkEntry(GameObject prefab, Matrix4x4 worldMatrix)
    {
        this.prefab = prefab;
        this.worldMatrix = worldMatrix;
    }
}