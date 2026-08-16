using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Generates and caches synthetic wall segment prefabs with rectangular cutouts for wall openings.
/// Uses constructive box geometry per mesh part instead of runtime CSG.
/// </summary>
public static class WallSegmentCutCache
{
    private static readonly Dictionary<CutKey, GameObject> _cache = new();
    private static GameObject _emptyPrefab;

    /// <summary>Sentinel empty prefab representing a fully removed wall segment</summary>
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
    /// Retrieves or generates a cached synthetic cut wall prefab given local cutout ranges
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

    /// <summary>Invalidates cached cut prefabs associated with a specific wall prefab asset</summary>
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
            Debug.LogWarning($"WallSegmentCutCache: '{wallPrefab.name}' has no renderable mesh data - leaving wall tile uncut.");
            return wallPrefab;
        }

        float openMinX = xRange.x;
        float openMaxX = xRange.y;
        float openMinY = yRange.x;
        float openMaxY = yRange.y;

        GameObject root = new GameObject($"WallCut_{wallPrefab.name}");
        root.hideFlags = HideFlags.HideAndDontSave;

        foreach (PrefabMeshPart part in meshData.parts)
        {
            if (!TryGetPartLocalBounds(part, out Vector3 partMin, out Vector3 partMax))
                continue;

            bool openingReachesPart = openMaxX > partMin.x && openMinX < partMax.x &&
                                      openMaxY > partMin.y && openMinY < partMax.y;

            if (!openingReachesPart)
            {
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
                continue;

            // Generate frame boxes surrounding the cutout for this specific mesh part
            AddBox(root, part.material, partMin.x, partOpenMinX, partMin.y, partMax.y, partMin.z, partMax.z);     // Left pillar
            AddBox(root, part.material, partOpenMaxX, partMax.x, partMin.y, partMax.y, partMin.z, partMax.z);     // Right pillar
            AddBox(root, part.material, partOpenMinX, partOpenMaxX, partMin.y, partOpenMinY, partMin.z, partMax.z); // Sill
            AddBox(root, part.material, partOpenMinX, partOpenMaxX, partOpenMaxY, partMax.y, partMin.z, partMax.z); // Lintel
        }

        if (root.transform.childCount == 0)
        {
            UnityEngine.Object.DestroyImmediate(root);
            return EmptyPrefab;
        }

        root.SetActive(false);
        return root;
    }

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
            return;

        GameObject box = new GameObject("Part_Cut");
        box.transform.SetParent(parent.transform, worldPositionStays: false);

        MeshFilter filter = box.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildBoxMesh(minX, maxX, minY, maxY, minZ, maxZ);

        MeshRenderer renderer = box.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
    }

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

        SetFace(vertices, normals, uvs, 0, p0, p1, p2, p3, Vector3.back, sizeX, sizeY);
        SetFace(vertices, normals, uvs, 4, p4, p5, p6, p7, Vector3.forward, sizeX, sizeY);
        SetFace(vertices, normals, uvs, 8, p0, p1, p5, p4, Vector3.down, sizeX, sizeZ);
        SetFace(vertices, normals, uvs, 12, p2, p3, p7, p6, Vector3.up, sizeX, sizeZ);
        SetFace(vertices, normals, uvs, 16, p0, p4, p7, p3, Vector3.left, sizeZ, sizeY);
        SetFace(vertices, normals, uvs, 20, p1, p2, p6, p5, Vector3.right, sizeZ, sizeY);

        int[] triangles =
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            8, 9, 10, 8, 10, 11,
            12, 13, 14, 12, 14, 15,
            16, 17, 18, 16, 18, 19,
            20, 21, 22, 20, 22, 23,
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