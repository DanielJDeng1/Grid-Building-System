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

    public void AddObjectAt(Vector3Int gridPosition, List<Vector2Int> positionsFilled, int ID, int placedObjectIndex)
    {
        List<Vector3Int> positionsToOccupy = CalculatePositions(gridPosition, positionsFilled);
        PlacedObject data = new PlacedObject(positionsToOccupy, ID, placedObjectIndex);
        foreach(var position in positionsToOccupy)
        {
            if (placedObjects.ContainsKey(position))
                throw new Exception($"Dictionary already contains this position {position}");
            placedObjects[position] = data;
        }   

    }

    private List<Vector3Int> CalculatePositions(Vector3Int gridPosition, List<Vector2Int> positionsFilled)
    {
        List<Vector3Int> returnValues = new();
        for (int i = 0; i < positionsFilled.Count; i++)
        {
            returnValues.Add(gridPosition + new Vector3Int(positionsFilled[i].x, gridPosition.y, positionsFilled[i].y));
        }

        return returnValues;
    }

    public bool CanPlaceObjectAt(Vector3Int gridPosition, List<Vector2Int> positionsFilled)
    {
        List<Vector3Int> positionsToOccupy = CalculatePositions(gridPosition, positionsFilled);
        foreach (var pos in positionsToOccupy)
        {
            if (placedObjects.ContainsKey(pos) && placedObjects[pos] != null)
                return false;
        }
        return true;
    }
    
    internal int GetRepresentationIndex(Vector3Int gridPosition)
    {
        if (!placedObjects.ContainsKey(gridPosition))
            return -1;

        return placedObjects[gridPosition].placedObjectIndex;
    }

    internal void RemoveObjectAt(Vector3Int gridPosition)
    {
        foreach (var pos in placedObjects[gridPosition].occupiedPositions)
        {
            placedObjects.Remove(pos);
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
