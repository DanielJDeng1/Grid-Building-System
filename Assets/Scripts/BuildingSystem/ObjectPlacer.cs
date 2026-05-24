using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class ObjectPlacer : MonoBehaviour
{
    //System does not remove deleted objects and keeps their null value. Please fix so it is more optimized

    [SerializeField] private List<GameObject> placedGameObjects = new List<GameObject>();

    internal int PlaceObject(GameObject prefab, Vector3 position)
    {
        GameObject newObject = Instantiate(prefab);
        newObject.transform.position = position;

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

}
