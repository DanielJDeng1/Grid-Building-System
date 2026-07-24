using UnityEngine;

/// <summary>
/// A single placed instance (a Floor grid object or any Edge/wall object) that lives
/// inside a mesh chunk instead of being instantiated as its own GameObject.
///
/// Only the prefab reference and a baked world transform matrix are kept - there is no
/// live Transform to update, since chunked entries are never instantiated individually.
/// The prefab's actual mesh/material data is looked up lazily via PrefabMeshCache when
/// a chunk rebuilds.
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
