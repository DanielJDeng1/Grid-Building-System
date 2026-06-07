using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class ObjectPlacer : MonoBehaviour
{
    //System does not remove deleted objects and keeps their null value. Please fix so it is more optimized

    [SerializeField] private List<GameObject> placedGameObjects = new List<GameObject>();

    public int PlaceObject(GameObject prefab, Vector3 position, GridRotation rotation)
    {
        GameObject newObject = Instantiate(prefab);
        newObject.transform.position = position;

        Vector3 pivot = new Vector3(position.x + 0.5f, position.y, position.z + 0.5f);

        foreach (Transform child in newObject.transform)
        {
            child.transform.RotateAround(pivot, Vector3.up, (int)rotation * 90f);
        }

        for (int i = 0; i < placedGameObjects.Count; i++)
        {
            if (placedGameObjects[i] == null)
            {
                placedGameObjects[i] = newObject;
                return i;   
            }
        }

        placedGameObjects.Add(newObject);
        return placedGameObjects.Count - 1;
    }

    public void RemoveObjectAt(int gameObjectIndex)
    {
        if (placedGameObjects.Count <= gameObjectIndex || placedGameObjects[gameObjectIndex] == null)
            return;
        Destroy(placedGameObjects[gameObjectIndex]);
        placedGameObjects[gameObjectIndex] = null;
    }

    public int PlaceEdge(GameObject prefab, Vector3 position, EdgeRotation rotation)
    {
        GameObject newObject = Instantiate(prefab);
        newObject.transform.position = position;

        foreach (Transform child in newObject.transform)
        {
            child.transform.RotateAround(child.transform.position, Vector3.up, (int)rotation * 90f);
        }

        for (int i = 0; i < placedGameObjects.Count; i++)
        {
            if (placedGameObjects[i] == null)
            {
                placedGameObjects[i] = newObject;
                return i;
            }
        }

        placedGameObjects.Add(newObject);
        return placedGameObjects.Count - 1;
    }

    public void RemoveEdgeAt(int gameObjectIndex)
    {
        if (placedGameObjects.Count <= gameObjectIndex || placedGameObjects[gameObjectIndex] == null)
            return;
        Destroy(placedGameObjects[gameObjectIndex]);
        placedGameObjects[gameObjectIndex] = null;
    }


}
