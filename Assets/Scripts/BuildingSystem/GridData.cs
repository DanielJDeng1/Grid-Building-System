using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

/// <summary>
/// Abstraction of placed objects and edges.
/// Maps tile locations to grid objects and edge locations to edge objects (walls, fences, etc).
/// Maintains separate dictionaries for three build layers: floor, furniture, ceiling.
/// 
/// MULTI-LEVEL SUPPORT (FIXED):
/// All coordinate calculations now preserve Y-coordinates for proper multi-level building.
/// Objects at different heights (Y-levels) are stored with their correct Y-coordinate as dictionary keys.
/// 
/// PERFORMANCE OPTIMIZATION:
/// Uses cached lists for temporary calculations to eliminate per-frame GC allocations.
/// Previously allocated new List<Vector3Int> and List<Edge> on every UpdateState() call.
/// Now reuses pre-allocated lists, reducing GC pressure to zero during placement operations.
/// 
/// EDGE COORDINATE SYSTEM:
/// Edges are defined by two adjacent tile positions representing the edge BETWEEN them.
/// 
/// Edge Rotation Mapping (relative to mouse-hovered tile at position (x, y, z)):
/// - Deg0 (Horizontal): Edge from (x, y, z) to (x+1, y, z) along positive X-axis - 0° rotation
/// - Deg90 (Vertical): Edge from (x, y, z) to (x, y, z-1) along negative Z-axis - 90° rotation
/// 
/// Multi-Edge Example:
/// If positionsFilled = {0, 1} for a 2-unit wall at tile (0, 0, 0):
/// - Deg0: Edges [(0,0,0)-(1,0,0)] and [(1,0,0)-(2,0,0)] - extends horizontally along positive X-axis
/// - Deg90: Edges [(0,0,0)-(0,0,-1)] and [(0,0,-1)-(0,0,-2)] - extends vertically along negative Z-axis
/// </summary>
public class GridData
{
    private Dictionary<Vector3Int, PlacedObject> _placedObjects = new();
    private Dictionary<Edge, PlacedEdge> _placedEdges = new();

    // PERFORMANCE FIX: Cached lists to eliminate per-frame allocations
    private List<Vector3Int> _cachedPositionsList = new(16);
    private List<Edge> _cachedEdgesList = new(16);

    #region Grid Object Placement

    public void AddObjectAt(Vector3Int gridPosition, List<Vector2Int> positionsFilled, int ID, int placedObjectIndex, GridRotation rotation)
    {
        List<Vector3Int> positionsToOccupy = CalculatePositions(gridPosition, positionsFilled, rotation);
        PlacedObject data = new PlacedObject(positionsToOccupy, ID, placedObjectIndex);
        
        foreach(var position in positionsToOccupy)
        {
            if (_placedObjects.ContainsKey(position))
                throw new Exception($"Dictionary already contains this position {position}");
            _placedObjects[position] = data;
        }   
    }

    /// <summary>
    /// MULTI-LEVEL FIX: Now preserves gridPosition.y throughout all rotations.
    /// Returns a NEW list with copied values for storage, but uses cached list for calculations.
    /// </summary>
    private List<Vector3Int> CalculatePositions(Vector3Int gridPosition, List<Vector2Int> positionsFilled, GridRotation rotation)
    {
        _cachedPositionsList.Clear();

        // Apply 2D rotation matrix to each position offset
        // Rotation is around Y-axis (vertical), affecting X and Z coordinates
        // CRITICAL: gridPosition.y is preserved for multi-level building support
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
                    // 90° rotation: (x, z) -> (z, -x)
                    _cachedPositionsList.Add(gridPosition + new Vector3Int(offset.y, 0, -offset.x));
                }
                break;
            case GridRotation.Deg180:
                foreach (var offset in positionsFilled)
                {
                    // 180° rotation: (x, z) -> (-x, -z)
                    _cachedPositionsList.Add(gridPosition + new Vector3Int(-offset.x, 0, -offset.y));
                }
                break;
            case GridRotation.Deg270:
                foreach (var offset in positionsFilled)
                {
                    // 270° rotation: (x, z) -> (-z, x)
                    _cachedPositionsList.Add(gridPosition + new Vector3Int(-offset.y, 0, offset.x));
                }
                break;            
        }

        // Return a new list for storage (caller needs to keep ownership)
        return new List<Vector3Int>(_cachedPositionsList);
    }

    /// <summary>
    /// PERFORMANCE FIX: Uses cached list for zero-allocation validation checks.
    /// MULTI-LEVEL FIX: Preserves Y-coordinate for correct collision detection at different heights.
    /// Called every frame during UpdateState(), so must be allocation-free.
    /// </summary>
    public bool CanPlaceObjectAt(Vector3Int gridPosition, List<Vector2Int> positionsFilled, GridRotation rotation)
    {
        _cachedPositionsList.Clear();

        // Inline calculation to avoid method call overhead and additional allocations
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
        return true;
    }
    
    public int GetRepresentationIndex(Vector3Int gridPosition)
    {
        if (!_placedObjects.ContainsKey(gridPosition))
            return -1;

        return _placedObjects[gridPosition].placedObjectIndex;
    }

    public void RemoveObjectAt(Vector3Int gridPosition)
    {
        if (!_placedObjects.ContainsKey(gridPosition))
            return;

        foreach (var pos in _placedObjects[gridPosition].occupiedPositions)
        {
            _placedObjects.Remove(pos);
        }
    }

    #endregion

    #region Edge Placement

    /// <summary>
    /// Adds an edge at the specified base position with rotation.
    /// The edge parameter should be the base edge for the current rotation,
    /// and positionsFilled from EdgeData determines how many edges are occupied.
    /// </summary>
    public void AddEdgeAt(Edge baseEdge, List<int> positionsFilled, int ID, int placedObjectIndex, EdgeRotation rotation)
    {
        List<Edge> edgesToOccupy = CalculateEdges(baseEdge, positionsFilled, rotation);
        PlacedEdge data = new PlacedEdge(edgesToOccupy, ID, placedObjectIndex);
        
        foreach(var edge in edgesToOccupy)
        {
            if (_placedEdges.ContainsKey(edge))
                throw new Exception($"Dictionary already contains this edge {edge}");
            _placedEdges[edge] = data;
        } 
        
    }

    /// <summary>
    /// Calculates all edges occupied by a multi-edge structure based on rotation.
    /// 
    /// MULTI-LEVEL FIX: Now preserves baseEdge.end1.y throughout all calculations.
    /// PERFORMANCE FIX: Uses cached list to eliminate GC allocation.
    /// 
    /// Algorithm:
    /// - Deg0: Extends horizontally along positive X-axis
    /// - Deg90: Extends vertically along negative Z-axis
    /// 
    /// Each integer in positionsFilled represents an edge segment offset from base position.
    /// The base edge's end1 is used as the reference tile position (the pivot).
    /// </summary>
    private List<Edge> CalculateEdges(Edge baseEdge, List<int> positionsFilled, EdgeRotation rotation)
    {
        _cachedEdgesList.Clear();
        Vector3Int baseTile = baseEdge.end1; // Use end1 as the reference tile position (the pivot)

        // CRITICAL: baseTile.y is preserved throughout calculations for multi-level support
        switch (rotation)
        {
            case EdgeRotation.Deg0:
                // Horizontal edges along positive X-axis
                // Base edge: (x, y, z) to (x+1, y, z)
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
                // Vertical edges along negative Z-axis
                // Base edge: (x, y, z) to (x, y, z-1)
                foreach (int offset in positionsFilled)
                {
                    Vector3Int tilePos = baseTile + new Vector3Int(0, 0, -offset);
                    Edge edge = new Edge(
                        new Vector3Int(tilePos.x, tilePos.y, tilePos.z),
                        new Vector3Int(tilePos.x, tilePos.y, tilePos.z - 1)
                    );
                    _cachedEdgesList.Add(edge);
                }
                break;         
        }

        // Return a new list for storage (caller needs to keep ownership)
        return new List<Edge>(_cachedEdgesList);
    }

    /// <summary>
    /// PERFORMANCE FIX: Uses cached list for zero-allocation validation checks.
    /// MULTI-LEVEL FIX: Preserves Y-coordinate for correct collision detection at different heights.
    /// </summary>
    public bool CanPlaceEdgeAt(Edge baseEdge, List<int> positionsFilled, EdgeRotation rotation)
    {
        _cachedEdgesList.Clear();
        Vector3Int baseTile = baseEdge.end1;

        // Inline calculation to avoid method call overhead
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
                    Vector3Int tilePos = baseTile + new Vector3Int(0, 0, -offset);
                    Edge edge = new Edge(
                        new Vector3Int(tilePos.x, tilePos.y, tilePos.z),
                        new Vector3Int(tilePos.x, tilePos.y, tilePos.z - 1)
                    );
                    _cachedEdgesList.Add(edge);
                }
                break;         
        }

        foreach (var edge in _cachedEdgesList)
        {
            if (_placedEdges.ContainsKey(edge) && _placedEdges[edge] != null)
                return false;
        }
        return true;
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

        foreach (var e in _placedEdges[edge].occupiedEdges)
        {
            _placedEdges.Remove(e);
        }
    }

    #endregion
}

#region Edge Definition with Bidirectional Equality

/// <summary>
/// Represents an edge between two tile positions in 3D grid space.
/// 
/// MULTI-LEVEL SUPPORT:
/// Edges now properly maintain Y-coordinates for multi-level building.
/// Edge at (x, y1, z) is distinct from edge at (x, y2, z).
/// 
/// BIDIRECTIONAL EQUALITY:
/// Edge(A, B) equals Edge(B, A) for dictionary lookups and comparisons.
/// This is essential because edge direction is arbitrary - the physical edge
/// between tile A and tile B is the same regardless of endpoint order.
/// 
/// HASH FUNCTION FIX:
/// Uses HashCode.Combine() instead of XOR to prevent hash collisions.
/// Maintains bidirectional equality while ensuring unique hashes per edge pair.
/// </summary>
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
        
        // Bidirectional equality: (A, B) == (B, A)
        return (end1 == other.end1 && end2 == other.end2) || 
               (end1 == other.end2 && end2 == other.end1);
    }

    public override int GetHashCode()
    {
        // HASH FIX: Use proper hash combining instead of XOR to prevent collisions
        // Sort hashes to ensure (A, B) and (B, A) produce identical hashes
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
    public int ID { get; set; }
    public int placedObjectIndex { get; set; }

    public PlacedObject(List<Vector3Int> occupiedPositions, int id, int index)
    {
        this.occupiedPositions = occupiedPositions;
        this.ID = id;
        this.placedObjectIndex = index;
    }
}

public class PlacedEdge
{
    public List<Edge> occupiedEdges;
    public int ID { get; set; }
    public int placedObjectIndex { get; set; }

    public PlacedEdge(List<Edge> occupiedEdges, int id, int index)
    {
        this.occupiedEdges = occupiedEdges;
        this.ID = id;
        this.placedObjectIndex = index;
    }
}

#endregion