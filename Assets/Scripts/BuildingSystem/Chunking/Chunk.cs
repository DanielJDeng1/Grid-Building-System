using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// How a Chunk generates physics colliders for its entries. Computed from each part's mesh
/// bounds (not fixed dimensions), so colliders automatically match whatever height/thickness
/// each prefab actually has. Since every rotation in this system is a 90-degree multiple,
/// plain axis-aligned bounding boxes are always an exact fit for wall/floor geometry - no
/// oriented-box math needed.
/// </summary>
public enum ColliderMode
{
    /// <summary>No colliders generated for this chunk.</summary>
    None,

    /// <summary>
    /// One BoxCollider covering the bounds of ALL entries combined. Only correct when the
    /// chunk's entries are guaranteed contiguous/convex - i.e. wall runs, which are always a
    /// straight line by construction. Using this for a spatially-bucketed (non-contiguous)
    /// chunk would create false collision over any gaps within the bounding box.
    /// </summary>
    AggregateBox,

    /// <summary>
    /// One BoxCollider per entry, all attached to the same chunk GameObject. Stays exact
    /// regardless of the chunk's shape (L-shaped rooms, holes, disconnected regions) at the
    /// cost of more collider components - used for spatially-bucketed chunks (floors).
    /// </summary>
    PerEntryBox
}

/// <summary>
/// A single mesh chunk: a bag of chunked entries (Floor grid objects, or Wall segments)
/// combined into one renderer, plus physics colliders (see ColliderMode). What determines
/// WHICH entries end up in the same Chunk is entirely up to the owning manager
/// (FloorChunkManager buckets spatially, WallChunkManager groups by contiguous straight run)
/// - this class only knows how to store entries by handle and rebuild combined visuals/
/// colliders from whatever it's holding.
///
/// REBUILD STRATEGY:
/// Rebuild() is expensive (mesh combining, collider updates) and is only called by the owning
/// manager's batched end-of-frame pass, never directly from AddEntry/RemoveEntry. This is what
/// turns "40 placements in one chunk during a drag" into a single rebuild instead of 40.
///
/// MULTI-MATERIAL HANDLING:
/// Placed prefabs can have multiple child meshes with different materials. Combining happens
/// in two phases:
///   1. Group every mesh part across the whole chunk by material, combine each group into one
///      mesh (mergeSubMeshes: true) - this collapses all geometry sharing a material into a
///      single submesh-worth of geometry, regardless of which prefab it came from.
///   2. Combine those per-material meshes into one final mesh (mergeSubMeshes: false) - this
///      produces one mesh with N submeshes, matched 1:1 with N materials on the renderer.
/// Collider bounds are accumulated in the same pass as step 1, so this costs no extra full
/// iteration over entries/parts.
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
    /// Looks up a previously-added entry without removing it. Used when moving an entry from
    /// one Chunk to another (wall-run merge/split) without needing to recompute its data.
    /// </summary>
    public bool TryGetEntry(int handle, out ChunkEntry entry)
    {
        return _entries.TryGetValue(handle, out entry);
    }

    /// <summary>
    /// Rebuilds the combined mesh AND colliders from scratch based on current entries.
    /// Called at most once per frame per dirty chunk by the owning manager.
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

        // Phase 1: one mesh per material.
        var perMaterialMeshes = new List<Mesh>(byMaterial.Count);
        var materialsInOrder = new List<Material>(byMaterial.Count);

        foreach (KeyValuePair<Material, List<CombineInstance>> kvp in byMaterial)
        {
            Mesh materialMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            materialMesh.CombineMeshes(kvp.Value.ToArray(), mergeSubMeshes: true, useMatrices: true);

            perMaterialMeshes.Add(materialMesh);
            materialsInOrder.Add(kvp.Key);
        }

        // Phase 2: combine per-material meshes into one mesh with N submeshes.
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

        // Intermediate per-material meshes are only needed for the phase-2 combine above.
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
    /// Sets a BoxCollider's center/size from a world-space AABB, converting into the mesh
    /// holder's LOCAL space so the collider stays correct even if this chunk's GameObject
    /// ends up parented under a non-identity transform.
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
    /// Expands a running world-space AABB (min/max, tracked via ref params) to include a
    /// mesh's local-space bounds transformed by the given matrix. Transforms all 8 corners
    /// rather than just center/extents, since an arbitrary matrix can rotate the box - for
    /// this system rotations are always 90-degree multiples, so the result is an exact fit,
    /// not an over-conservative approximation.
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