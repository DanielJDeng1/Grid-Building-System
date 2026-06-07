using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

/// <summary>
/// Abstraction of placed objects
/// Maps locations or edge locations to build objects
/// </summary>
public class GridData
{
    Dictionary<Vector3Int, PlacedObject> placedObjects = new();

    Dictionary<Edge, PlacedEdge> placedEdges = new();

    public void AddObjectAt(Vector3Int gridPosition, List<Vector2Int> positionsFilled, int ID, int placedObjectIndex, GridRotation rotation)
    {
        List<Vector3Int> positionsToOccupy = CalculatePositions(gridPosition, positionsFilled, rotation);
        PlacedObject data = new PlacedObject(positionsToOccupy, ID, placedObjectIndex);
        foreach(var position in positionsToOccupy)
        {
            if (placedObjects.ContainsKey(position))
                throw new Exception($"Dictionary already contains this position {position}");
            placedObjects[position] = data;
        }   

    }

    private List<Vector3Int> CalculatePositions(Vector3Int gridPosition, List<Vector2Int> positionsFilled, GridRotation rotation)
    {
        List<Vector3Int> returnValues = new();

        switch (rotation)
        {
            case GridRotation.Deg0:
                for (int i = 0; i < positionsFilled.Count; i++)
                {
                    returnValues.Add(gridPosition + new Vector3Int(positionsFilled[i].x, 0, positionsFilled[i].y));
                }
                break;
            case GridRotation.Deg90:
                for (int i = 0; i < positionsFilled.Count; i++)
                {
                    returnValues.Add(gridPosition + new Vector3Int(positionsFilled[i].y, 0, -positionsFilled[i].x));
                }
                break;
            case GridRotation.Deg180:
                for (int i = 0; i < positionsFilled.Count; i++)
                {
                    returnValues.Add(gridPosition + new Vector3Int(-positionsFilled[i].x, 0, -positionsFilled[i].y));
                }
                break;
            default:
                for (int i = 0; i < positionsFilled.Count; i++)
                {
                    returnValues.Add(gridPosition + new Vector3Int(-positionsFilled[i].y, 0, positionsFilled[i].x));
                }
                break;            
        }

        return returnValues;
    }

    public bool CanPlaceObjectAt(Vector3Int gridPosition, List<Vector2Int> positionsFilled, GridRotation rotation)
    {
        List<Vector3Int> positionsToOccupy = CalculatePositions(gridPosition, positionsFilled, rotation);

        foreach (var pos in positionsToOccupy)
        {
            if (placedObjects.ContainsKey(pos) && placedObjects[pos] != null)
                return false;
        }
        return true;
    }
    
    public int GetRepresentationIndex(Vector3Int gridPosition)
    {
        if (!placedObjects.ContainsKey(gridPosition))
            return -1;

        return placedObjects[gridPosition].placedObjectIndex;
    }

    public void RemoveObjectAt(Vector3Int gridPosition)
    {
        foreach (var pos in placedObjects[gridPosition].occupiedPositions)
        {
            placedObjects.Remove(pos);
        }
    }


    //edge placement and removal

    public void AddEdgeAt(Edge edge, int ID, int placedObjectIndex, EdgeRotation rotation)
    {
        List<Edge> edgesToOccupy = CalculateEdges(edge, rotation);
        PlacedEdge data = new PlacedEdge(edgesToOccupy, ID, placedObjectIndex);
        foreach(var e in edgesToOccupy)
        {
            if (placedEdges.ContainsKey(e))
                throw new Exception($"Dictionary already contains this edge {e}");
            placedEdges[e] = data;
        }   

    }

    private List<Edge> CalculateEdges(Edge edge, EdgeRotation rotation)
    {
        List<Edge> returnValues = new();
        switch (rotation)
        {
            case EdgeRotation.Deg0:
                returnValues.Add(edge);
                break;
            case EdgeRotation.Deg90:
                returnValues.Add(new Edge(edge.end1, edge.end2));
                break;         
        }

        return returnValues;
    }

    public bool CanPlaceEdgeAt(Edge edge, EdgeRotation rotation)
    {
        List<Edge> edgesToOccupy = CalculateEdges(edge, rotation);

        foreach (var e in edgesToOccupy)
        {
            if (placedEdges.ContainsKey(e) && placedEdges[e] != null)
                return false;
        }
        return true;
    }

    public int GetEdgeRepresentationIndex(Edge edge)
    {
        if (!placedEdges.ContainsKey(edge))
            return -1;

        return placedEdges[edge].placedObjectIndex;
    }

    public void RemoveEdgeAt(Edge edge)
    {
        foreach (var e in placedEdges[edge].occupiedEdges)
        {
            placedEdges.Remove(e);
        }
    }

}

public record Edge(Vector3Int end1, Vector3Int end2);

namespace System.Runtime.CompilerServices{
    public class IsExternalInit{

    }
}

public class PlacedObject
{
    public List<Vector3Int> occupiedPositions;

    public int ID { get; set; }

    public int placedObjectIndex { get; set; }

    public PlacedObject(List<Vector3Int> occ, int id, int index)
    {
        occupiedPositions = occ;
        ID = id;
        placedObjectIndex = index;
    }
    
}

public class PlacedEdge{

    public List<Edge> occupiedEdges;
    public int ID {get; set;}

    public int placedObjectIndex {get; set;}

    public PlacedEdge(List<Edge> edgeList, int id, int index)
    {
        occupiedEdges = edgeList;
        ID = id;
        placedObjectIndex = index;
    }
}
