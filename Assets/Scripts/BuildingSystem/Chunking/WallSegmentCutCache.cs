using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Produces (and caches) synthetic runtime "prefabs" representing a wall segment with a
/// rectangular hole cut out of it, sized from a wall opening's collider bounds (see
/// WallOpeningCutPlanner for how that rectangle is derived).
///
/// WHY NOT REAL CSG: every cut this system ever needs is an axis-aligned rectangle removed from
/// an axis-aligned box - rotations are always 90-degree multiples (the same assumption Chunk's
/// own collider generation relies on). That means the result is exactly representable as up to
/// 4 boxes (left/right pillars, sill, lintel) per affected mesh part, no boolean mesh operations
/// required. A runtime CSG library would trade a page of box math for a much heavier, slower,
/// and more fragile dependency - not proportionate to what this actually needs to do.
///
/// WHY A SYNTHETIC PREFAB, NOT A NEW CHUNK CODE PATH: the result is packaged as an ordinary
/// (hidden, never-instantiated-in-scene) GameObject with MeshFilter/MeshRenderer children, so it
/// can be handed to PrefabMeshCache/Chunk exactly like any authored prefab. Chunk.Rebuild() and
/// PrefabMeshCache need ZERO changes to support cut wall segments - WallChunkManager just points
/// a tile's existing ChunkEntry at this synthetic prefab instead of the original one.
///
/// PER-PART, NOT PER-AGGREGATED-MATERIAL:
/// The cut is computed independently for each of the wall prefab's individual mesh parts (as
/// decomposed by PrefabMeshCache), NOT for each material with all its parts merged into one
/// combined bounding box. This matters for any wall built from more than one physical piece of
/// geometry - most commonly several shorter wall meshes stacked/tiled together to form one
/// tile (bricks, planks, log courses, a body mesh plus a separate trim strip), whether or not
/// they share a material.
///
/// An earlier version of this cache grouped all parts sharing a material into one combined AABB
/// before building the cut boxes. That's wrong the moment a material appears in more than one
/// physically-separate piece: merging them produces one artificial solid box spanning a region
/// the original mesh never actually filled continuously, which then overlaps in world space
/// with whatever other parts/materials occupy that same span - visible as z-fighting across
/// large areas of the cut, not just at the opening's edges. Operating per PART instead means a
/// piece the opening doesn't reach is copied through completely unmodified (bit-identical to
/// how it renders in the uncut wall), and only pieces the opening actually overlaps get their
/// own independent box reconstruction from THEIR OWN bounds - never merged with any other
/// part's geometry, so two genuinely-adjacent pieces stay exactly as adjacent as they were
/// originally, with no new coincident geometry introduced by the cut.
/// </summary>
public static class WallSegmentCutCache
{
    private static readonly Dictionary<CutKey, GameObject> _cache = new();
    private static GameObject _emptyPrefab;

    /// <summary>Shared sentinel for "no wall geometry at all" - a tile the opening fully covers.</summary>
    public static GameObject EmptyPrefab
    {
        get
        {
            if (_emptyPrefab == null)
            {
                _emptyPrefab = new GameObject("WallSegment_Empty");
                _emptyPrefab.hideFlags = HideFlags.HideAndDontSave;
                _emptyPrefab.SetActive(false);
            }
            return _emptyPrefab;
        }
    }

    /// <summary>
    /// Returns a cached synthetic prefab for wallPrefab cut to (xRange, yRange), both in the
    /// wall's own local space (same convention as PrefabColliderCache/WallOpeningCutPlanner).
    /// Returns EmptyPrefab if the cut fully covers the wall's bounds, or wallPrefab itself
    /// unchanged if the wall has no BoxCollider to derive bounds from (logged as a warning -
    /// this is an authoring gap, not something to fail silently on).
    /// </summary>
    public static GameObject GetOrCreateCut(GameObject wallPrefab, Vector2 xRange, Vector2 yRange)
    {
        CutKey key = new CutKey(wallPrefab, xRange, yRange);

        if (_cache.TryGetValue(key, out GameObject cached) && cached != null)
            return cached;

        GameObject cut = BuildCutPrefab(wallPrefab, xRange, yRange);
        _cache[key] = cut;
        return cut;
    }

    /// <summary>Clears every cached cut derived from a specific wall prefab - call if that prefab's mesh/collider/material ever changes at runtime.</summary>
    public static void InvalidateWallPrefab(GameObject wallPrefab)
    {
        var toRemove = new List<CutKey>();
        foreach (CutKey key in _cache.Keys)
        {
            if (key.Matches(wallPrefab))
                toRemove.Add(key);
        }
        foreach (CutKey key in toRemove)
            _cache.Remove(key);
    }

    private static GameObject BuildCutPrefab(GameObject wallPrefab, Vector2 xRange, Vector2 yRange)
    {
        PrefabMeshData meshData = PrefabMeshCache.Get(wallPrefab);
        if (meshData.parts.Count == 0)
        {
            Debug.LogWarning($"WallSegmentCutCache: '{wallPrefab.name}' has no renderable mesh data - leaving this wall tile uncut.");
            return wallPrefab;
        }

        // The opening rectangle arrives already expressed in the wall prefab's own local space,
        // pre-clamped to this one tile's [0,1] domain by WallOpeningCutPlanner (see that class
        // and WallOpeningState.CutHostWallTiles). Deliberately NOT re-clamped here against the
        // wall prefab's own BoxCollider bounds, unlike an earlier version of this method: a
        // wall's authored collider is often a single simplified box that doesn't perfectly match
        // the combined footprint of its actual render geometry - a multi-mesh wall commonly has
        // an end cap, bevel, or overhang on one of its stacked parts extending slightly past the
        // nominal collider extent. Clamping the opening to the COLLIDER's bounds before comparing
        // against each PART's own (mesh-derived) bounds silently capped the hole short of where a
        // wide opening should have fully reached, leaving a thin sliver of that part's mesh
        // behind as a visible artifact - only noticeable once an opening got wide enough to
        // actually reach that mismatched edge. Per-part clamping below (against each part's own
        // true bounds, from TryGetPartLocalBounds) is sufficient on its own to keep every
        // generated box within valid geometry - no wall-level guard is needed on top of it, and
        // the wall prefab no longer needs a BoxCollider at all for this to work correctly.
        float openMinX = xRange.x;
        float openMaxX = xRange.y;
        float openMinY = yRange.x;
        float openMaxY = yRange.y;

        GameObject root = new GameObject($"WallCut_{wallPrefab.name}");
        root.hideFlags = HideFlags.HideAndDontSave;

        foreach (PrefabMeshPart part in meshData.parts)
        {
            if (!TryGetPartLocalBounds(part, out Vector3 partMin, out Vector3 partMax))
                continue; // empty submesh - nothing to bound or render.

            bool openingReachesPart = openMaxX > partMin.x && openMinX < partMax.x &&
                                       openMaxY > partMin.y && openMinY < partMax.y;

            if (!openingReachesPart)
            {
                // The opening doesn't touch this piece at all - carry it through completely
                // unmodified. Correct by construction (identical to how it renders uncut) and
                // avoids ever needing to reason about this part's relationship to any other.
                AddUnmodifiedPart(root, part);
                continue;
            }

            float partOpenMinX = Mathf.Clamp(openMinX, partMin.x, partMax.x);
            float partOpenMaxX = Mathf.Clamp(openMaxX, partMin.x, partMax.x);
            float partOpenMinY = Mathf.Clamp(openMinY, partMin.y, partMax.y);
            float partOpenMaxY = Mathf.Clamp(openMaxY, partMin.y, partMax.y);

            bool partFullyOpen = partOpenMinX <= partMin.x && partOpenMaxX >= partMax.x &&
                                  partOpenMinY <= partMin.y && partOpenMaxY >= partMax.y;
            if (partFullyOpen)
                continue; // this piece sits entirely inside the hole - nothing of it survives the cut.

            // Partial overlap - reconstruct as a box frame from THIS PART'S OWN bounds only,
            // never merged with any other part's geometry (see class doc).
            AddBox(root, part.material, partMin.x, partOpenMinX, partMin.y, partMax.y, partMin.z, partMax.z);           // left pillar
            AddBox(root, part.material, partOpenMaxX, partMax.x, partMin.y, partMax.y, partMin.z, partMax.z);           // right pillar
            AddBox(root, part.material, partOpenMinX, partOpenMaxX, partMin.y, partOpenMinY, partMin.z, partMax.z);     // sill
            AddBox(root, part.material, partOpenMinX, partOpenMaxX, partOpenMaxY, partMax.y, partMin.z, partMax.z);     // lintel
        }

        if (root.transform.childCount == 0)
        {
            UnityEngine.Object.DestroyImmediate(root);
            return EmptyPrefab;
        }

        root.SetActive(false);
        return root;
    }

    /// <summary>
    /// This single part's own local-space AABB (relative to the prefab root), computed from its
    /// submesh's actual triangle data and its own localMatrix - NOT merged with any other part.
    /// </summary>
    private static bool TryGetPartLocalBounds(PrefabMeshPart part, out Vector3 min, out Vector3 max)
    {
        if (!TryGetSubMeshLocalBounds(part.mesh, part.subMeshIndex, out Vector3 subMin, out Vector3 subMax))
        {
            min = Vector3.zero;
            max = Vector3.zero;
            return false;
        }

        min = Vector3.zero;
        max = Vector3.zero;
        bool hasBounds = false;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                (i & 1) == 0 ? subMin.x : subMax.x,
                (i & 2) == 0 ? subMin.y : subMax.y,
                (i & 4) == 0 ? subMin.z : subMax.z
            );

            Vector3 rootCorner = part.localMatrix.MultiplyPoint3x4(corner);

            if (!hasBounds)
            {
                min = rootCorner;
                max = rootCorner;
                hasBounds = true;
            }
            else
            {
                min = Vector3.Min(min, rootCorner);
                max = Vector3.Max(max, rootCorner);
            }
        }

        return true;
    }

    /// <summary>
    /// Computes a SUBMESH's own local-space AABB directly from the vertices its triangles
    /// actually reference - NOT Mesh.bounds, which covers the WHOLE mesh regardless of submesh.
    /// This matters whenever a wall prefab's parts come from multiple submeshes on a single
    /// SHARED mesh (common for modular wall art - one mesh, split into a body submesh and a
    /// trim submesh) rather than from separate MeshFilters, since every submesh would otherwise
    /// report the exact same Mesh.bounds.
    /// </summary>
    private static bool TryGetSubMeshLocalBounds(Mesh mesh, int subMeshIndex, out Vector3 min, out Vector3 max)
    {
        int[] triangleIndices = mesh.GetTriangles(subMeshIndex);
        Vector3[] vertices = mesh.vertices;

        min = Vector3.zero;
        max = Vector3.zero;
        bool hasBounds = false;

        for (int i = 0; i < triangleIndices.Length; i++)
        {
            Vector3 v = vertices[triangleIndices[i]];

            if (!hasBounds)
            {
                min = v;
                max = v;
                hasBounds = true;
            }
            else
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
        }

        return hasBounds;
    }

    /// <summary>
    /// Copies a mesh part through to the cut prefab completely unmodified - used when the
    /// opening's rectangle doesn't reach this part's bounds at all. Bakes this part's own
    /// local-to-root transform directly into a standalone mesh via CombineMeshes, the same way
    /// Chunk.Rebuild's own phase-1 per-material combine does, so the result is pixel-identical
    /// to how this part renders in the UNCUT chunk.
    /// </summary>
    private static void AddUnmodifiedPart(GameObject root, PrefabMeshPart part)
    {
        var combine = new CombineInstance
        {
            mesh = part.mesh,
            subMeshIndex = part.subMeshIndex,
            transform = part.localMatrix
        };

        Mesh partMesh = new Mesh { name = "WallCut_Unmodified", indexFormat = IndexFormat.UInt32 };
        partMesh.CombineMeshes(new[] { combine }, mergeSubMeshes: true, useMatrices: true);

        GameObject child = new GameObject("Part_Unmodified");
        child.transform.SetParent(root.transform, worldPositionStays: false);

        MeshFilter filter = child.AddComponent<MeshFilter>();
        filter.sharedMesh = partMesh;

        MeshRenderer renderer = child.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = part.material;
    }

    private static void AddBox(GameObject parent, Material material, float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        const float EPSILON = 0.001f;
        if (maxX - minX <= EPSILON || maxY - minY <= EPSILON || maxZ - minZ <= EPSILON)
            return; // degenerate box (hole flush with this part's own edge) - nothing to render here.

        GameObject box = new GameObject("Part_Cut");
        box.transform.SetParent(parent.transform, worldPositionStays: false);
        // Left at the identity local transform deliberately - BuildBoxMesh bakes absolute
        // corner positions (already in the wall's own local space) directly into the mesh, so
        // no position/scale is needed on the GameObject itself.

        MeshFilter filter = box.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildBoxMesh(minX, maxX, minY, maxY, minZ, maxZ);

        MeshRenderer renderer = box.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
    }

    /// <summary>
    /// Builds a single box mesh directly from world-unit corner coordinates in the wall's own
    /// local space - NOT a shared unit cube scaled via the transform. Two things this fixes
    /// relative to that naive approach:
    ///
    /// 1. FLAT NORMALS: each face gets its own 4 vertices (24 total, none shared across faces),
    ///    so normals stay flat per face instead of averaged/smeared across the 3 faces meeting
    ///    at each corner.
    ///
    /// 2. CORRECT UVS: each face gets a standard 4-corner UV rect scaled to that face's ACTUAL
    ///    size (1 UV unit per 1 world unit), matching the wall's own neighboring, uncut tiles
    ///    instead of stretching one tile's worth of texture across an arbitrary pillar width.
    ///
    /// ASSUMPTION: this only blends in if the wall prefab's own UVs are laid out at the same
    /// 1-unit-per-world-unit density, which is the standard convention for tileable modular
    /// building sets.
    /// </summary>
    private static Mesh BuildBoxMesh(float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        Vector3 p0 = new(minX, minY, minZ);
        Vector3 p1 = new(maxX, minY, minZ);
        Vector3 p2 = new(maxX, maxY, minZ);
        Vector3 p3 = new(minX, maxY, minZ);
        Vector3 p4 = new(minX, minY, maxZ);
        Vector3 p5 = new(maxX, minY, maxZ);
        Vector3 p6 = new(maxX, maxY, maxZ);
        Vector3 p7 = new(minX, maxY, maxZ);

        float sizeX = maxX - minX, sizeY = maxY - minY, sizeZ = maxZ - minZ;

        var vertices = new Vector3[24];
        var normals = new Vector3[24];
        var uvs = new Vector2[24];

        SetFace(vertices, normals, uvs, 0, p0, p1, p2, p3, Vector3.back, sizeX, sizeY);       // back
        SetFace(vertices, normals, uvs, 4, p4, p5, p6, p7, Vector3.forward, sizeX, sizeY);    // front
        SetFace(vertices, normals, uvs, 8, p0, p1, p5, p4, Vector3.down, sizeX, sizeZ);       // bottom
        SetFace(vertices, normals, uvs, 12, p2, p3, p7, p6, Vector3.up, sizeX, sizeZ);        // top
        SetFace(vertices, normals, uvs, 16, p0, p4, p7, p3, Vector3.left, sizeZ, sizeY);      // left
        SetFace(vertices, normals, uvs, 20, p1, p2, p6, p5, Vector3.right, sizeZ, sizeY);     // right

        int[] triangles =
        {
            0, 2, 1, 0, 3, 2,     // back - winding intentionally reversed relative to the other
                                  // 5 faces - this exact corner layout needs the opposite
                                  // winding to stay front-facing for -Z-facing geometry.
            4, 5, 6, 4, 6, 7,     // front
            8, 9, 10, 8, 10, 11,  // bottom
            12, 13, 14, 12, 14, 15, // top
            16, 17, 18, 16, 18, 19, // left
            20, 21, 22, 20, 22, 23, // right
        };

        var mesh = new Mesh { name = "WallCut_Part" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void SetFace(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int offset,
                                 Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
                                 float uWorldSize, float vWorldSize)
    {
        vertices[offset] = a;
        vertices[offset + 1] = b;
        vertices[offset + 2] = c;
        vertices[offset + 3] = d;

        normals[offset] = normals[offset + 1] = normals[offset + 2] = normals[offset + 3] = normal;

        uvs[offset] = new Vector2(0f, 0f);
        uvs[offset + 1] = new Vector2(uWorldSize, 0f);
        uvs[offset + 2] = new Vector2(uWorldSize, vWorldSize);
        uvs[offset + 3] = new Vector2(0f, vWorldSize);
    }

    private readonly struct CutKey : IEquatable<CutKey>
    {
        private readonly GameObject prefab;
        // Quantized so float noise between near-identical cuts doesn't defeat the cache.
        private readonly int minXQ, maxXQ, minYQ, maxYQ;

        private const float QUANTIZE = 1000f;

        public CutKey(GameObject prefab, Vector2 xRange, Vector2 yRange)
        {
            this.prefab = prefab;
            minXQ = Mathf.RoundToInt(xRange.x * QUANTIZE);
            maxXQ = Mathf.RoundToInt(xRange.y * QUANTIZE);
            minYQ = Mathf.RoundToInt(yRange.x * QUANTIZE);
            maxYQ = Mathf.RoundToInt(yRange.y * QUANTIZE);
        }

        public bool Matches(GameObject candidatePrefab) => prefab == candidatePrefab;

        public bool Equals(CutKey other) =>
            prefab == other.prefab && minXQ == other.minXQ && maxXQ == other.maxXQ && minYQ == other.minYQ && maxYQ == other.maxYQ;

        public override bool Equals(object obj) => obj is CutKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(prefab, minXQ, maxXQ, minYQ, maxYQ);
    }
}