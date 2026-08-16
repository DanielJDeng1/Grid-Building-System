using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

// Core grid state mapping 3D tile and edge coordinates to placed objects or walls
public class GridData
{
    private Dictionary<Vector3Int, PlacedObject> _placedObjects = new();
    private Dictionary<Edge, PlacedEdge> _placedEdges = new();

    // Set tracking to avoid iterating multi-cell footprints multiple times
    private HashSet<PlacedObject> _allObjectInstances = new();
    private HashSet<PlacedEdge> _allEdgeInstances = new();

    // Reused lists for frame updates during drag placement previews
    private List<Vector3Int> _cachedPositionsList = new(16);
    private List<Edge> _cachedEdgesList = new(16);

    // Fires whenever cell or edge occupancy updates for navmesh or pathfinding rebuilds
    public event Action<Vector3Int, bool> OnCellOccupancyChanged;
    public event Action<Edge, bool> OnEdgeOccupancyChanged;

    #region Grid Object Placement

    public void AddObjectAt(Vector3Int gridPosition, List<Vector2Int> positionsFilled, int ID, int placedObjectIndex, GridRotation rotation)
    {
        List<Vector3Int> positionsToOccupy = CalculatePositions(gridPosition, positionsFilled, rotation);
        PlacedObject data = new PlacedObject(positionsToOccupy, gridPosition, rotation, ID, placedObjectIndex);

        foreach(var position in positionsToOccupy)
        {
            if (_placedObjects.ContainsKey(position))
                throw new Exception($"Dictionary already contains this position {position}");
            _placedObjects[position] = data;
            OnCellOccupancyChanged?.Invoke(position, true);
        }

        _allObjectInstances.Add(data);
    }

    // Projects 2D offsets onto 3D grid while maintaining level height on the Y axis
    private List<Vector3Int> CalculatePositions(Vector3Int gridPosition, List<Vector2Int> positionsFilled, GridRotation rotation)
    {
        _cachedPositionsList.Clear();

        switch (rotation)
        {
            case GridRotation.Deg0:
                foreach (var offset in positionsFilled)
                {
                    _cachedPositionsList.Add(gridPosition + new Vector3Int(offset.x, 0, offset.y));
                }
                break;
            case GridRotation.Deg90:
                foreach (var offset in positionsFilled)
                {
                    _cachedPositionsList.Add(gridPosition + new Vector3Int(offset.y, 0, -offset.x));
                }
                break;
            case GridRotation.Deg180:
                foreach (var offset in positionsFilled)
                {
                    _cachedPositionsList.Add(gridPosition + new Vector3Int(-offset.x, 0, -offset.y));
                }
                break;
            case GridRotation.Deg270:
                foreach (var offset in positionsFilled)
                {
                    _cachedPositionsList.Add(gridPosition + new Vector3Int(-offset.y, 0, offset.x));
                }
                break;            
        }

        return new List<Vector3Int>(_cachedPositionsList);
    }

    // Zero-allocation check that rejects overlap with existing objects or internal walls
    public bool CanPlaceObjectAt(Vector3Int gridPosition, List<Vector2Int> positionsFilled, GridRotation rotation)
    {
        _cachedPositionsList.Clear();

        switch (rotation)
        {
            case GridRotation.Deg0:
                foreach (var offset in positionsFilled)
                {
                    _cachedPositionsList.Add(gridPosition + new Vector3Int(offset.x, 0, offset.y));
                }
                break;
            case GridRotation.Deg90:
                foreach (var offset in positionsFilled)
                {
                    _cachedPositionsList.Add(gridPosition + new Vector3Int(offset.y, 0, -offset.x));
                }
                break;
            case GridRotation.Deg180:
                foreach (var offset in positionsFilled)
                {
                    _cachedPositionsList.Add(gridPosition + new Vector3Int(-offset.x, 0, -offset.y));
                }
                break;
            case GridRotation.Deg270:
                foreach (var offset in positionsFilled)
                {
                    _cachedPositionsList.Add(gridPosition + new Vector3Int(-offset.y, 0, offset.x));
                }
                break;            
        }

        foreach (var pos in _cachedPositionsList)
        {
            if (_placedObjects.ContainsKey(pos) && _placedObjects[pos] != null)
                return false;
        }

        if (FootprintContainsInteriorEdge(_cachedPositionsList))
            return false;

        return true;
    }

    // Ensures placed edges sit on object boundaries and do not cut across interior seams
    private bool FootprintContainsInteriorEdge(List<Vector3Int> footprintPositions)
    {
        foreach (var pos in footprintPositions)
        {
            Vector3Int xNeighbor = pos + new Vector3Int(1, 0, 0);
            if (footprintPositions.Contains(xNeighbor))
            {
                Edge candidate = new Edge(xNeighbor, xNeighbor + new Vector3Int(0, 0, 1));
                if (_placedEdges.ContainsKey(candidate))
                    return true;
            }

            Vector3Int zNeighbor = pos + new Vector3Int(0, 0, 1);
            if (footprintPositions.Contains(zNeighbor))
            {
                Edge candidate = new Edge(zNeighbor, zNeighbor + new Vector3Int(1, 0, 0));
                if (_placedEdges.ContainsKey(candidate))
                    return true;
            }
        }
        return false;
    }
    
    public int GetRepresentationIndex(Vector3Int gridPosition)
    {
        if (!_placedObjects.ContainsKey(gridPosition))
            return -1;

        return _placedObjects[gridPosition].placedObjectIndex;
    }

    // Single-pass lookup for wall validation and prefab index retrieval
    public bool TryGetEdgeInfo(Edge edge, out int ID, out int placedObjectIndex)
    {
        if (_placedEdges.TryGetValue(edge, out PlacedEdge data))
        {
            ID = data.ID;
            placedObjectIndex = data.placedObjectIndex;
            return true;
        }

        ID = -1;
        placedObjectIndex = -1;
        return false;
    }

    public void RemoveObjectAt(Vector3Int gridPosition)
    {
        if (!_placedObjects.ContainsKey(gridPosition))
            return;

        PlacedObject data = _placedObjects[gridPosition];

        foreach (var pos in data.occupiedPositions)
        {
            _placedObjects.Remove(pos);
            OnCellOccupancyChanged?.Invoke(pos, false);
        }

        _allObjectInstances.Remove(data);
    }

    #endregion

    #region Edge Placement

    public void AddEdgeAt(Edge baseEdge, List<int> positionsFilled, int ID, int placedObjectIndex, EdgeRotation rotation)
    {
        List<Edge> edgesToOccupy = CalculateEdges(baseEdge, positionsFilled, rotation);
        PlacedEdge data = new PlacedEdge(edgesToOccupy, baseEdge, rotation, ID, placedObjectIndex);

        foreach(var edge in edgesToOccupy)
        {
            if (_placedEdges.ContainsKey(edge))
                throw new Exception($"Dictionary already contains this edge {edge}");
            _placedEdges[edge] = data;
            OnEdgeOccupancyChanged?.Invoke(edge, true);
        }

        _allEdgeInstances.Add(data);
    }

    // Maps multi-segment wall offsets along cardinal axes
    private List<Edge> CalculateEdges(Edge baseEdge, List<int> positionsFilled, EdgeRotation rotation)
    {
        _cachedEdgesList.Clear();
        Vector3Int baseTile = baseEdge.end1;

        switch (rotation)
        {
            case EdgeRotation.Deg0:
                foreach (int offset in positionsFilled)
                {
                    Vector3Int tilePos = baseTile + new Vector3Int(offset, 0, 0);
                    Edge edge = new Edge(
                        new Vector3Int(tilePos.x, tilePos.y, tilePos.z),
                        new Vector3Int(tilePos.x + 1, tilePos.y, tilePos.z)
                    );
                    _cachedEdgesList.Add(edge);
                }
                break;
                
            case EdgeRotation.Deg90:
                foreach (int offset in positionsFilled)
                {
                    Vector3Int tilePos = baseTile + new Vector3Int(0, 0, offset);
                    Edge edge = new Edge(
                        new Vector3Int(tilePos.x, tilePos.y, tilePos.z),
                        new Vector3Int(tilePos.x, tilePos.y, tilePos.z + 1)
                    );
                    _cachedEdgesList.Add(edge);
                }
                break;         
        }

        return new List<Edge>(_cachedEdgesList);
    }

    // Strict validation check requiring complete edge clarity and no object intersections
    public bool CanPlaceEdgeAt(Edge baseEdge, List<int> positionsFilled, EdgeRotation rotation)
    {
        _cachedEdgesList.Clear();
        Vector3Int baseTile = baseEdge.end1;

        switch (rotation)
        {
            case EdgeRotation.Deg0:
                foreach (int offset in positionsFilled)
                {
                    Vector3Int tilePos = baseTile + new Vector3Int(offset, 0, 0);
                    Edge edge = new Edge(
                        new Vector3Int(tilePos.x, tilePos.y, tilePos.z),
                        new Vector3Int(tilePos.x + 1, tilePos.y, tilePos.z)
                    );
                    _cachedEdgesList.Add(edge);
                }
                break;
                
            case EdgeRotation.Deg90:
                foreach (int offset in positionsFilled)
                {
                    Vector3Int tilePos = baseTile + new Vector3Int(0, 0, offset);
                    Edge edge = new Edge(
                        new Vector3Int(tilePos.x, tilePos.y, tilePos.z),
                        new Vector3Int(tilePos.x, tilePos.y, tilePos.z + 1)
                    );
                    _cachedEdgesList.Add(edge);
                }
                break;         
        }

        foreach (var edge in _cachedEdgesList)
        {
            if (_placedEdges.ContainsKey(edge) && _placedEdges[edge] != null)
                return false;

            if (EdgeCutsThroughObjectBody(edge))
                return false;
        }
        return true;
    }

    // Ignores edge-on-edge collisions for replacement or override workflows
    public bool WouldEdgeIntersectObject(Edge baseEdge, List<int> positionsFilled, EdgeRotation rotation)
    {
        List<Edge> candidateEdges = CalculateEdges(baseEdge, positionsFilled, rotation);

        foreach (var edge in candidateEdges)
        {
            if (EdgeCutsThroughObjectBody(edge))
                return true;
        }
        return false;
    }

    // Checks if an edge runs through two cells that belong to the same object
    private bool EdgeCutsThroughObjectBody(Edge edge)
    {
        Vector3Int cellA;
        Vector3Int cellB;

        if (edge.end1.x != edge.end2.x)
        {
            int ex = Mathf.Min(edge.end1.x, edge.end2.x);
            int ez = edge.end1.z;
            cellA = new Vector3Int(ex, edge.end1.y, ez - 1);
            cellB = new Vector3Int(ex, edge.end1.y, ez);
        }
        else
        {
            int ez = Mathf.Min(edge.end1.z, edge.end2.z);
            int ex = edge.end1.x;
            cellA = new Vector3Int(ex - 1, edge.end1.y, ex);
            cellB = new Vector3Int(ex, edge.end1.y, ez);
        }

        if (_placedObjects.TryGetValue(cellA, out PlacedObject objA) &&
            _placedObjects.TryGetValue(cellB, out PlacedObject objB))
        {
            return ReferenceEquals(objA, objB);
        }
        return false;
    }

    public int GetEdgeRepresentationIndex(Edge edge)
    {
        if (!_placedEdges.ContainsKey(edge))
            return -1;

        return _placedEdges[edge].placedObjectIndex;
    }

    public void RemoveEdgeAt(Edge edge)
    {
        if (!_placedEdges.ContainsKey(edge))
            return;

        PlacedEdge data = _placedEdges[edge];

        foreach (var e in data.occupiedEdges)
        {
            _placedEdges.Remove(e);
            OnEdgeOccupancyChanged?.Invoke(e, false);
        }

        _allEdgeInstances.Remove(data);
    }

    // Clears all overlapping edge instances across a proposed footprint for batch replacement
    public List<int> ClearEdgesInFootprint(Edge baseEdge, List<int> positionsFilled, EdgeRotation rotation)
    {
        List<Edge> targetEdges = CalculateEdges(baseEdge, positionsFilled, rotation);
        var removedIndices = new List<int>();

        foreach (Edge edge in targetEdges)
        {
            int index = GetEdgeRepresentationIndex(edge);

            if (index == -1 || removedIndices.Contains(index))
                continue;

            removedIndices.Add(index);
            RemoveEdgeAt(edge);
        }

        return removedIndices;
    }

    #endregion

    #region Save System Support

    public IReadOnlyCollection<PlacedObject> GetAllPlacedObjects() => _allObjectInstances;

    public IReadOnlyCollection<PlacedEdge> GetAllPlacedEdges() => _allEdgeInstances;

    // Fast state wipe bypassing cell events for level loading or resets
    public void Clear()
    {
        _placedObjects.Clear();
        _placedEdges.Clear();
        _allObjectInstances.Clear();
        _allEdgeInstances.Clear();
    }

    #endregion
}

#region Edge Definition with Bidirectional Equality

// Direction-independent edge descriptor between two grid points
public record Edge
{
    public Vector3Int end1 { get; init; }
    public Vector3Int end2 { get; init; }

    public Edge(Vector3Int end1, Vector3Int end2)
    {
        this.end1 = end1;
        this.end2 = end2;
    }

    public virtual bool Equals(Edge other)
    {
        if (other is null) return false;
        
        return (end1 == other.end1 && end2 == other.end2) || 
               (end1 == other.end2 && end2 == other.end1);
    }

    public override int GetHashCode()
    {
        int hash1 = end1.GetHashCode();
        int hash2 = end2.GetHashCode();
        
        if (hash1 < hash2)
            return HashCode.Combine(hash1, hash2);
        else
            return HashCode.Combine(hash2, hash1);
    }
}

#endregion

#region C# 9.0 Record Support for Unity 2020.x/2021.x
namespace System.Runtime.CompilerServices
{
    internal class IsExternalInit { }
}
#endregion

#region Data Structures

public class PlacedObject
{
    public List<Vector3Int> occupiedPositions;

    // Saved for serialization because asymmetric footprints can be tricky to infer later
    public Vector3Int basePosition { get; }
    public GridRotation rotation { get; }

    public int ID { get; set; }
    public int placedObjectIndex { get; set; }

    public PlacedObject(List<Vector3Int> occupiedPositions, Vector3Int basePosition, GridRotation rotation, int id, int index)
    {
        this.occupiedPositions = occupiedPositions;
        this.basePosition = basePosition;
        this.rotation = rotation;
        this.ID = id;
        this.placedObjectIndex = index;
    }
}

public class PlacedEdge
{
    public List<Edge> occupiedEdges;

    // Saved for serialization and multi-edge segment rebuilds
    public Edge baseEdge { get; }
    public EdgeRotation rotation { get; }

    public int ID { get; set; }
    public int placedObjectIndex { get; set; }

    public PlacedEdge(List<Edge> occupiedEdges, Edge baseEdge, EdgeRotation rotation, int id, int index)
    {
        this.occupiedEdges = occupiedEdges;
        this.baseEdge = baseEdge;
        this.rotation = rotation;
        this.ID = id;
        this.placedObjectIndex = index;
    }
}

#endregion