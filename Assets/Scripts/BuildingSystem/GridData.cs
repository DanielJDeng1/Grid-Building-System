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

    Dictionary<Edge, PlacedEdge> placedWalls = new();

    public GridData()
    {
        placedObjects = new();
        placedWalls = new();
    }

    //methods for object placement

    /// <summary>
    /// Adds tile object to map
    /// </summary>
    /// <param name="gridPosition"> location to build the object </param>
    /// <param name="objectSize"> size of the object </param>
    /// <param name="ID"> i don't know what ID is for </param>
    /// <param name="objectIndex"> the index of the object in the objectDatabase of objects </param>
    /// <exception cref="Exception"> the position we want to add the object is occupied </exception>
    public void AddObjectAt(Vector3Int gridPosition, Vector2Int objectSize, int ID, int objectIndex)
    {
        List<Vector3Int> positionsToOccupy = CalculatePositions(gridPosition, objectSize);
        PlacedObject data = new PlacedObject(positionsToOccupy, ID, objectIndex);

        foreach (var pos in positionsToOccupy)
        {
            if (placedObjects.ContainsKey(pos))
            {
                throw new Exception($"Dictionary already has cell{pos}");
            }

            placedObjects[pos] = data;

        }
    }

    public void AddObjectAt(Vector3Int gridPosition, Vector2Int objectSize, int ID, int objectIndex, int movementPenalty)
    {
        List<Vector3Int> positionsToOccupy = CalculatePositions(gridPosition, objectSize);
        PlacedObject data = new PlacedObject(positionsToOccupy, ID, objectIndex);

        foreach (var pos in positionsToOccupy)
        {
            if (placedObjects.ContainsKey(pos))
            {
                throw new Exception($"Dictionary already has cell{pos}");
            }

            placedObjects[pos] = data;

        }

    }

    /// <summary>
    /// Calculates the positions we want to fill up with the object
    /// </summary>
    /// <param name="gridPosition"> position we want to build in </param>
    /// <param name="objectSize"> size of the object </param>
    /// <returns> returns a list of tiles we want to fill up </returns>
    private List<Vector3Int> CalculatePositions(Vector3Int gridPosition, Vector2Int objectSize)
    {

        List<Vector3Int> returnVal = new();

        for (int x = 0; x < objectSize.x; x++)
        {
            for (int y = 0; y < objectSize.y; y++)
            {
                returnVal.Add(gridPosition + new Vector3Int(x, 0, y));
            }
        }
        return returnVal;
    }

    /// <summary>
    /// sees if an object can be placed at location
    /// </summary>
    /// <param name="gridPosition"> position we want to build at </param>
    /// <param name="objectSize"> size of object </param>
    /// <returns> if an object can be placed there </returns>
    public bool CanPlaceObjectAt(Vector3Int gridPosition, Vector2Int objectSize)
    {

        /*
        loops through the object's positions, bottom to top, left to right
        */
        for (int x = 0; x < objectSize.x; x++)
        {
            for (int y = 0; y < objectSize.y; y++)
            {
                Vector3Int pos = gridPosition + new Vector3Int(x, 0, y);
                if (placedObjects.ContainsKey(pos))
                    return false;

                //if not at the final row, see if there is an edge above
                if (y < objectSize.y - 1)
                {
                    if (placedWalls.ContainsKey(GetEdge(pos + new Vector3Int(x, 0, y + 1), new Vector3Int(x + 1, 0, y + 1))))
                        return false;
                }

                //if not at final column, check if an edge to the right
                if (x < objectSize.x - 1)
                {
                    if (placedWalls.ContainsKey(GetEdge(pos + new Vector3Int(x + 1, 0, y), new Vector3Int(x + 1, 0, y + 1))))
                        return false;
                }

            }
        }
        return true;
    }


    /// <summary>
    /// returns the object index of a location
    /// </summary>
    /// <param name="gridPosition"> position of object we want to check </param>
    /// <returns> index of the object at location, or -1 if no object is there </returns>
    public int GetRepresentationIndex(Vector3Int gridPosition)
    {
        if (placedObjects.ContainsKey(gridPosition) == false)
        {
            return -1;
        }
        return placedObjects[gridPosition].placedObjectIndex;
    }

    public void RemoveObjectAt(Vector3Int gridPosition)
    {
        foreach (var pos in placedObjects[gridPosition].occupiedPositions)
        {
            placedObjects.Remove(pos);

        }
    }

    //methods for edge placement

    public void AddObjectAt(Vector3Int pos1, Vector3Int pos2, int ID, int objectIndex)
    {

        Edge edge = GetEdge(pos1, pos2);

        if (placedWalls.ContainsKey(edge))
            return;

        PlacedEdge placedEdge = new PlacedEdge(edge, ID, objectIndex);

        placedWalls.Add(edge, placedEdge);

    }

    public bool CanPlaceObjectAt(Vector3Int pos1, Vector3Int pos2)
    {
        Edge edge = GetEdge(pos1, pos2);

        if (placedWalls.ContainsKey(edge))
            return false;

        //compare the two tiles next to the edge
        /*if (pos1.x == pos2.x){
            Vector3Int check = new Vector3Int(pos1.x, pos1.y, Mathf.Min(pos1.z, pos2.z));
            if (placedObjects.ContainsKey(check) == placedObjects.ContainsKey(check - new Vector3Int(-1, 0, 0)))
                return false;
        }
        else if (pos1.z == pos2.z){
            Vector3Int check = new Vector3Int(Math.Min(pos1.x, pos2.x), pos1.y, pos1.z);
            if (placedObjects.ContainsKey(check) == placedObjects.ContainsKey(check - new Vector3Int(0, 0, -1)))
                return false;
        }*/
        return true;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    private int compareVectorInts(Vector3Int a, Vector3Int b)
    {
        if (a.x != b.x)
        {
            return a.x.CompareTo(b.x);
        }
        if (a.y != b.y)
        {
            return a.y.CompareTo(b.y);
        }
        return a.z.CompareTo(b.z);
    }

    public void RemoveObjectAt(Vector3Int pos1, Vector3Int pos2)
    {
        Edge edge = GetEdge(pos1, pos2);

        placedWalls.Remove(edge);

    }

    private Edge GetEdge(Vector3Int pos1, Vector3Int pos2)
    {
        Edge edge;

        if (compareVectorInts(pos1, pos2) > 0)
            edge = new Edge(pos1, pos2);
        else
            edge = new Edge(pos2, pos1);

        return edge;
    }

    public List<PlacedObject> GetPlacedObjects()
    {
        return new List<PlacedObject>(placedObjects.Values);
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

    Edge edge;
    public int ID {get; set;}

    public int placedObjectIndex {get; set;}

    public PlacedEdge(Edge e, int id, int index)
    {
        edge = e;
        ID = id;
        placedObjectIndex = index;
    }
}
