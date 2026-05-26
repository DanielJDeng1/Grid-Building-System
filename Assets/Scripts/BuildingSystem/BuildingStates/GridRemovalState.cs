using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

public class GridRemovalState : IBuildingState
{

    private int gameObjectIndex = -1;
    Grid grid;
    PreviewSystem previewSystem;
    GridData floorData, furnitureData, ceilingData;
    ObjectPlacer objectPlacer;
    GridData selectedData;

    List<Vector2Int> positionsToBeFilled;

    public GridRemovalState(Grid grid, PreviewSystem previewSystem, ObjectPlacer objectPlacer, GridData floorData, GridData furnitureData, GridData ceilingData)
    {
        this.grid = grid;
        this.previewSystem = previewSystem;
        this.objectPlacer = objectPlacer;
        this.floorData = floorData;
        this.furnitureData = furnitureData;
        this.ceilingData = ceilingData;
        positionsToBeFilled = new(){Vector2Int.zero};

        previewSystem.StartShowingRemovePreview();
    }

    public void EndState()
    {
        previewSystem.StartShowingRemovePreview();
    }

    public void OnAction(Vector3Int gridPosition)
    {
        selectedData = null;
        if (!furnitureData.CanPlaceObjectAt(gridPosition, positionsToBeFilled))
        {
            selectedData = furnitureData;
        }
        else if (!floorData.CanPlaceObjectAt(gridPosition, positionsToBeFilled))
        {
            selectedData = floorData;
        }

        if (selectedData == null)
            return;
        
        gameObjectIndex = selectedData.GetRepresentationIndex(gridPosition);

        if (gameObjectIndex == -1)
            return;

        selectedData.RemoveObjectAt(gridPosition);  
        objectPlacer.RemoveObjectAt(gameObjectIndex);

    }

    private bool CheckIfSelectionIsValid(Vector3Int gridPosition)
    {
        return !(furnitureData.CanPlaceObjectAt(gridPosition, positionsToBeFilled) && floorData.CanPlaceObjectAt(gridPosition, positionsToBeFilled));
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        bool validity = CheckIfSelectionIsValid(gridPosition);
        previewSystem.UpdatePosition(grid.CellToWorld(gridPosition));
    }

    public void Rotate(Vector3Int gridPosition)
    {
        
    }

    public void OnHold(Vector3Int mousePosition)
    {
        
    }
}
