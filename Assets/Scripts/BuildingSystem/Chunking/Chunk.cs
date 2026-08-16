using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Defines physics collider generation strategy for chunk entries using dynamic mesh bounds.
/// Assumes 90-degree rotation increments, making AABBs exact fits without OBB overhead.
/// </summary>
public enum ColliderMode
{
    /// <summary>No colliders generated.</summary>
    None,

    /// <summary>Single box covering all entries. Valid only for contiguous straight runs.</summary>
    AggregateBox,

    /// <summary>One box per entry for non-contiguous or complex layouts.</summary>
    PerEntryBox
}

/// <summary>
/// Manages a combined mesh renderer and physics colliders for a collection of chunk entries.
/// Rebuilds are deferred to end-of-frame passes to batch structural modifications.
/// Uses a two-step combine to group geometry by material and align submeshes 1:1 with materials.
/// </summary>
public class Chunk
{
    private readonly string _debugName;
    private readonly ColliderMode _colliderMode;
    private readonly Dictionary<int, ChunkEntry> _entries = new();

    private readonly GameObject _meshHolder;
    private readonly MeshFilter _meshFilter;
    private readonly MeshRenderer _meshRenderer;
    private Mesh _combinedMesh;

    private BoxCollider _aggregateCollider;
    private readonly List<BoxCollider> _perEntryColliders = new();

    public bool IsEmpty => _entries.Count == 0;
    public int EntryCount => _entries.Count;

    public Chunk(string debugName, Transform parent, ColliderMode colliderMode = ColliderMode.None)
    {
        _debugName = debugName;
        _colliderMode = colliderMode;

        _meshHolder = new GameObject(debugName);
        _meshHolder.transform.SetParent(parent, worldPositionStays: false);

        _meshFilter = _meshHolder.AddComponent<MeshFilter>();
        _meshRenderer = _meshHolder.AddComponent<MeshRenderer>();
    }

    public void AddEntry(int handle, ChunkEntry entry)
    {
        _entries[handle] = entry;
    }

    public void RemoveEntry(int handle)
    {
        _entries.Remove(handle);
    }

    /// <summary>
    /// Looks up an entry without removal, supporting efficient cross-chunk migrations.
    /// </summary>
    public bool TryGetEntry(int handle, out ChunkEntry entry)
    {
        return _entries.TryGetValue(handle, out entry);
    }

    /// <summary>
    /// Rebuilds combined mesh and colliders from current entries. Called once per frame when dirty.
    /// </summary>
    public void Rebuild()
    {
        var byMaterial = new Dictionary<Material, List<CombineInstance>>();

        // Collider bounds, accumulated alongside the mesh combine data in the same pass.
        bool aggregateHasBounds = false;
        Vector3 aggregateMin = Vector3.zero, aggregateMax = Vector3.zero;
        var perEntryBounds = _colliderMode == ColliderMode.PerEntryBox
            ? new Dictionary<int, (Vector3 min, Vector3 max)>()
            : null;

        foreach (KeyValuePair<int, ChunkEntry> kvp in _entries)
        {
            int handle = kvp.Key;
            ChunkEntry entry = kvp.Value;
            PrefabMeshData meshData = PrefabMeshCache.Get(entry.prefab);

            bool entryHasBounds = false;
            Vector3 entryMin = Vector3.zero, entryMax = Vector3.zero;

            foreach (PrefabMeshPart part in meshData.parts)
            {
                Matrix4x4 finalMatrix = entry.worldMatrix * part.localMatrix;

                if (!byMaterial.TryGetValue(part.material, out List<CombineInstance> list))
                {
                    list = new List<CombineInstance>();
                    byMaterial[part.material] = list;
                }

                list.Add(new CombineInstance
                {
                    mesh = part.mesh,
                    subMeshIndex = part.subMeshIndex,
                    transform = finalMatrix
                });

                if (_colliderMode == ColliderMode.AggregateBox)
                    EncapsulateBounds(ref aggregateHasBounds, ref aggregateMin, ref aggregateMax, part.mesh.bounds, finalMatrix);
                else if (_colliderMode == ColliderMode.PerEntryBox)
                    EncapsulateBounds(ref entryHasBounds, ref entryMin, ref entryMax, part.mesh.bounds, finalMatrix);
            }

            if (_colliderMode == ColliderMode.PerEntryBox && entryHasBounds)
                perEntryBounds[handle] = (entryMin, entryMax);
        }

        if (byMaterial.Count == 0)
        {
            _meshFilter.sharedMesh = null;
            _meshRenderer.sharedMaterials = System.Array.Empty<Material>();
            ClearColliders();
            return;
        }

        // Step 1: Combine meshes grouped by material.
        var perMaterialMeshes = new List<Mesh>(byMaterial.Count);
        var materialsInOrder = new List<Material>(byMaterial.Count);

        foreach (KeyValuePair<Material, List<CombineInstance>> kvp in byMaterial)
        {
            Mesh materialMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            materialMesh.CombineMeshes(kvp.Value.ToArray(), mergeSubMeshes: true, useMatrices: true);

            perMaterialMeshes.Add(materialMesh);
            materialsInOrder.Add(kvp.Key);
        }

        // Step 2: Combine per-material meshes into a single final mesh with multiple submeshes.
        var finalCombine = new CombineInstance[perMaterialMeshes.Count];
        for (int i = 0; i < perMaterialMeshes.Count; i++)
        {
            finalCombine[i] = new CombineInstance
            {
                mesh = perMaterialMeshes[i],
                subMeshIndex = 0,
                transform = Matrix4x4.identity
            };
        }

        if (_combinedMesh == null)
            _combinedMesh = new Mesh { name = _debugName, indexFormat = IndexFormat.UInt32 };
        else
            _combinedMesh.Clear();

        _combinedMesh.CombineMeshes(finalCombine, mergeSubMeshes: false, useMatrices: true);
        _combinedMesh.RecalculateBounds();

        _meshFilter.sharedMesh = _combinedMesh;
        _meshRenderer.sharedMaterials = materialsInOrder.ToArray();

        // Intermediate per-material meshes are only needed for the second combining step above.
        foreach (Mesh m in perMaterialMeshes)
            Object.Destroy(m);

        switch (_colliderMode)
        {
            case ColliderMode.AggregateBox:
                RebuildAggregateCollider(aggregateHasBounds, aggregateMin, aggregateMax);
                break;
            case ColliderMode.PerEntryBox:
                RebuildPerEntryColliders(perEntryBounds);
                break;
        }
    }

    public void DestroySelf()
    {
        if (_meshHolder != null)
            Object.Destroy(_meshHolder);

        if (_combinedMesh != null)
            Object.Destroy(_combinedMesh);
    }

    #region Colliders

    private void RebuildAggregateCollider(bool hasBounds, Vector3 worldMin, Vector3 worldMax)
    {
        if (!hasBounds)
        {
            if (_aggregateCollider != null)
                _aggregateCollider.enabled = false;
            return;
        }

        if (_aggregateCollider == null)
            _aggregateCollider = _meshHolder.AddComponent<BoxCollider>();

        _aggregateCollider.enabled = true;
        ApplyWorldBounds(_aggregateCollider, worldMin, worldMax);
    }

    private void RebuildPerEntryColliders(Dictionary<int, (Vector3 min, Vector3 max)> perEntryBounds)
    {
        // Full teardown + recreate each rebuild, consistent with how the render mesh itself
        // is fully rebuilt from scratch rather than incrementally patched. BoxColliders are
        // cheap enough (no mesh cooking) that this isn't a meaningful cost even for chunks
        // with many tiles.
        foreach (BoxCollider collider in _perEntryColliders)
            Object.Destroy(collider);
        _perEntryColliders.Clear();

        if (perEntryBounds == null)
            return;

        foreach (KeyValuePair<int, (Vector3 min, Vector3 max)> kvp in perEntryBounds)
        {
            BoxCollider collider = _meshHolder.AddComponent<BoxCollider>();
            ApplyWorldBounds(collider, kvp.Value.min, kvp.Value.max);
            _perEntryColliders.Add(collider);
        }
    }

    private void ClearColliders()
    {
        if (_aggregateCollider != null)
            _aggregateCollider.enabled = false;

        foreach (BoxCollider collider in _perEntryColliders)
            Object.Destroy(collider);
        _perEntryColliders.Clear();
    }

    /// <summary>
    /// Transforms world-space AABB bounds into local space for the collider.
    /// </summary>
    private void ApplyWorldBounds(BoxCollider collider, Vector3 worldMin, Vector3 worldMax)
    {
        Vector3 worldCenter = (worldMin + worldMax) * 0.5f;
        Vector3 worldSize = worldMax - worldMin;

        Transform holderTransform = _meshHolder.transform;
        Vector3 localCenter = holderTransform.InverseTransformPoint(worldCenter);
        Vector3 localSize = holderTransform.InverseTransformVector(worldSize);

        collider.center = localCenter;
        collider.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
    }

    /// <summary>
    /// Expands world-space AABB bounds by transforming all 8 local corners of a mesh.
    /// </summary>
    private static void EncapsulateBounds(ref bool hasBounds, ref Vector3 min, ref Vector3 max, Bounds localBounds, Matrix4x4 matrix)
    {
        Vector3 c = localBounds.center;
        Vector3 e = localBounds.extents;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                c.x + ((i & 1) == 0 ? -e.x : e.x),
                c.y + ((i & 2) == 0 ? -e.y : e.y),
                c.z + ((i & 4) == 0 ? -e.z : e.z)
            );

            Vector3 worldCorner = matrix.MultiplyPoint3x4(corner);

            if (!hasBounds)
            {
                min = worldCorner;
                max = worldCorner;
                hasBounds = true;
            }
            else
            {
                min = Vector3.Min(min, worldCorner);
                max = Vector3.Max(max, worldCorner);
            }
        }
    }

    #endregion
}