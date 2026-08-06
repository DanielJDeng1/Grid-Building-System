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
/// - Deg90 (Vertical): Edge from (x, y, z) to (x, y, z+1) along positive Z-axis - 90° rotation
/// 
/// Multi-Edge Example:
/// If positionsFilled = {0, 1} for a 2-unit wall at tile (0, 0, 0):
/// - Deg0: Edges [(0,0,0)-(1,0,0)] and [(1,0,0)-(2,0,0)] - extends horizontally along positive X-axis
/// - Deg90: Edges [(0,0,0)-(0,0,1)] and [(0,0,1)-(0,0,2)] - extends vertically along positive Z-axis
/// 
/// OBJECT/EDGE INTERSECTION RULES:
/// The wall prefab's pivot sits at a grid position and its mesh extends +1 unit along
/// X at that same Z - so an edge's own endpoint coordinates are NOT the two cells it
/// runs between. An X-oriented edge (x,z)-(x+1,z) is a wall on the boundary between
/// cell (x, z-1) [south] and cell (x, z) [north]; a Z-oriented edge (x,z)-(x,z+1) is a
/// wall between cell (x-1, z) [west] and cell (x, z) [east]. A wall is "around" an
/// object (allowed) when those two bordered cells belong to different objects, or one/
/// both are empty. A wall "cuts through" an object (disallowed) only when BOTH bordered
/// cells belong to the SAME object - meaning the wall runs across that object's own
/// interior seam. Symmetrically, an object can't be placed if the wall sitting on the
/// seam between two of its own would-be footprint cells already exists.
/// </summary>
public class GridData
{
    private Dictionary<Vector3Int, PlacedObject> _placedObjects = new();
    private Dictionary<Edge, PlacedEdge> _placedEdges = new();

    // PERFORMANCE FIX: Cached lists to eliminate per-frame allocations
    private List<Vector3Int> _cachedPositionsList = new(16);
    private List<Edge> _cachedEdgesList = new(16);

    /// <summary>
    /// NAV BRIDGE INTEGRATION: fired once per individual cell/edge whenever
    /// this GridData's occupancy changes, regardless of how many cells a
    /// single object's footprint spans. GridData doesn't know or care that
    /// these are consumed by pathfinding - it's just reporting on its own
    /// state, the way any data model should. Any future consumer (minimap,
    /// AI perception, save system) can subscribe to the same events without
    /// GridData needing to change at all.
    /// </summary>
    public event Action<Vector3Int, bool> OnCellOccupancyChanged;
    public event Action<Edge, bool> OnEdgeOccupancyChanged;

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
            OnCellOccupancyChanged?.Invoke(position, true);
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
    /// 
    /// INTERSECTION FIX: Also rejects placement if any already-placed edge has both of
    /// its endpoints within the proposed footprint - such an edge would end up running
    /// through the interior of the new object rather than around it.
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

        if (FootprintContainsInteriorEdge(_cachedPositionsList))
            return false;

        return true;
    }

    /// <summary>
    /// Checks whether any already-placed edge sits on the shared boundary between two
    /// cells that are BOTH part of the given footprint.
    /// 
    /// GEOMETRY: the wall prefab's pivot sits at a grid position and the mesh extends
    /// +1 unit along X at that same Z - so an X-oriented edge (x,z)-(x+1,z) is a wall
    /// along the boundary between row z and row z-1, NOT a wall running between the
    /// X-adjacent cells (x,z) and (x+1,z). The actual seam between two X-adjacent cells
    /// (x,z) and (x+1,z) is the Z-oriented edge one column over: (x+1,z)-(x+1,z+1).
    /// Symmetrically, the seam between two Z-adjacent cells (x,z) and (x,z+1) is the
    /// X-oriented edge one row over: (x,z+1)-(x+1,z+1).
    /// </summary>
    private bool FootprintContainsInteriorEdge(List<Vector3Int> footprintPositions)
    {
        foreach (var pos in footprintPositions)
        {
            // X-adjacent neighbor: the seam between pos and xNeighbor is the
            // Z-oriented edge at X = pos.x + 1.
            Vector3Int xNeighbor = pos + new Vector3Int(1, 0, 0);
            if (footprintPositions.Contains(xNeighbor))
            {
                Edge candidate = new Edge(xNeighbor, xNeighbor + new Vector3Int(0, 0, 1));
                if (_placedEdges.ContainsKey(candidate))
                    return true;
            }

            // Z-adjacent neighbor: the seam between pos and zNeighbor is the
            // X-oriented edge at Z = pos.z + 1.
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

    public void RemoveObjectAt(Vector3Int gridPosition)
    {
        if (!_placedObjects.ContainsKey(gridPosition))
            return;

        foreach (var pos in _placedObjects[gridPosition].occupiedPositions)
        {
            _placedObjects.Remove(pos);
            OnCellOccupancyChanged?.Invoke(pos, false);
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
            OnEdgeOccupancyChanged?.Invoke(edge, true);
        } 
        
    }

    /// <summary>
    /// Calculates all edges occupied by a multi-edge structure based on rotation.
    /// 
    /// MULTI-LEVEL FIX: Now preserves baseEdge.end1.y throughout all calculations.
    /// PERFORMANCE FIX: Uses cached list to eliminate GC allocation.
    /// 
    /// Algorithm:
    /// - Deg0: Extends along positive X-axis (wall runs parallel to X)
    /// - Deg90: Extends along positive Z-axis (wall runs parallel to Z)
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
                // Wall runs along positive X-axis.
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
                // Wall runs along positive Z-axis.
                // Base edge: (x, y, z) to (x, y, z+1)
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

        // Return a new list for storage (caller needs to keep ownership)
        return new List<Edge>(_cachedEdgesList);
    }

    /// <summary>
    /// STRICT validity check: true only if every candidate edge is both (a) not already
    /// occupied by another edge structure, AND (b) doesn't cut through an object's body.
    /// This is the "no override" version - useful for callers that want a hard yes/no with
    /// no side effects. EdgeState does NOT use this for its actual placement gating, since
    /// edge-vs-edge placement is designed to override (see ClearEdgesInFootprint) rather
    /// than reject - it uses WouldEdgeIntersectObject instead, which ignores existing edge
    /// occupancy entirely and only cares about objects.
    /// 
    /// PERFORMANCE FIX: Uses cached list for zero-allocation validation checks.
    /// MULTI-LEVEL FIX: Preserves Y-coordinate for correct collision detection at different heights.
    /// 
    /// AXIS: matches CalculateEdges - Deg0 extends along X, Deg90 extends along Z.
    /// </summary>
    public bool CanPlaceEdgeAt(Edge baseEdge, List<int> positionsFilled, EdgeRotation rotation)
    {
        _cachedEdgesList.Clear();
        Vector3Int baseTile = baseEdge.end1;

        // Inline calculation to avoid method call overhead
        switch (rotation)
        {
            case EdgeRotation.Deg0:
                // Wall runs along positive X-axis.
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
                // Wall runs along positive Z-axis.
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

    /// <summary>
    /// OVERRIDE-AWARE validity check: true if the proposed edge placement would cut
    /// through an object's body. Deliberately ignores whether the edges are already
    /// occupied by another edge structure - edge-vs-edge overlap is expected to be
    /// resolved by clearing/overriding (ClearEdgesInFootprint), not by rejecting the
    /// placement. This is what EdgeState should call before placing/overriding an edge.
    /// </summary>
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

    /// <summary>
    /// True if the two cells this wall physically separates are BOTH occupied by the
    /// SAME placed object, meaning the wall would cut through that object's interior.
    /// 
    /// GEOMETRY: the wall prefab's pivot sits at a grid position and its mesh extends
    /// +1 unit along X at that same Z, so the edge's own endpoint coordinates are NOT
    /// the two cells it runs between - an X-oriented edge (x,z)-(x+1,z) is a wall on
    /// the boundary between cell (x, z-1) [south] and cell (x, z) [north]. Symmetrically
    /// a Z-oriented edge (x,z)-(x,z+1) borders cell (x-1, z) [west] and cell (x, z) [east].
    /// Two DIFFERENT objects on either side (or empty space on one/both sides) is a
    /// normal perimeter/boundary wall and returns false.
    /// </summary>
    private bool EdgeCutsThroughObjectBody(Edge edge)
    {
        Vector3Int cellA;
        Vector3Int cellB;

        if (edge.end1.x != edge.end2.x)
        {
            // X-oriented wall: separates the row south of it from the row north of it.
            int ex = Mathf.Min(edge.end1.x, edge.end2.x);
            int ez = edge.end1.z; // both endpoints share Z on an X-oriented edge
            cellA = new Vector3Int(ex, edge.end1.y, ez - 1); // south
            cellB = new Vector3Int(ex, edge.end1.y, ez);     // north
        }
        else
        {
            // Z-oriented wall: separates the column west of it from the column east of it.
            int ez = Mathf.Min(edge.end1.z, edge.end2.z);
            int ex = edge.end1.x; // both endpoints share X on a Z-oriented edge
            cellA = new Vector3Int(ex - 1, edge.end1.y, ez); // west
            cellB = new Vector3Int(ex, edge.end1.y, ez);     // east
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

        foreach (var e in _placedEdges[edge].occupiedEdges)
        {
            _placedEdges.Remove(e);
            OnEdgeOccupancyChanged?.Invoke(e, false);
        }
    }

    /// <summary>
    /// Removes every existing edge structure whose footprint overlaps ANY edge the given
    /// baseEdge/positionsFilled/rotation would occupy - not just an existing occupant at
    /// baseEdge itself.
    /// 
    /// WHY THIS EXISTS: a multi-segment placement (positionsFilled.Count > 1) can span several
    /// edges, and each of those edges can belong to a DIFFERENT existing placed structure, not
    /// just whatever (if anything) currently occupies baseEdge. Removing only baseEdge's
    /// occupant before calling AddEdgeAt leaves those other structures in place, and AddEdgeAt
    /// then throws when it reaches an edge that's still occupied. This clears the entire
    /// footprint up front so the subsequent AddEdgeAt call is guaranteed to succeed.
    /// 
    /// Returns the distinct placedObjectIndex values that were removed, so the caller can also
    /// clean up the corresponding visual entries (e.g. via ObjectPlacer.RemoveEdgeAt) - GridData
    /// has no knowledge of ObjectPlacer, so it can't do that part itself.
    /// </summary>
    public List<int> ClearEdgesInFootprint(Edge baseEdge, List<int> positionsFilled, EdgeRotation rotation)
    {
        List<Edge> targetEdges = CalculateEdges(baseEdge, positionsFilled, rotation);
        var removedIndices = new List<int>();

        foreach (Edge edge in targetEdges)
        {
            int index = GetEdgeRepresentationIndex(edge);

            // Already removed as part of a previously-found structure this pass (a single
            // existing structure can occupy more than one of the new placement's target
            // edges) - skip to avoid recording/removing the same index twice.
            if (index == -1 || removedIndices.Contains(index))
                continue;

            removedIndices.Add(index);
            RemoveEdgeAt(edge);
        }

        return removedIndices;
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