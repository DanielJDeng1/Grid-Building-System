using UnityEngine;

public class GridState : MonoBehaviour, IBuildingState
{

    private int selectedObjectIndex = -1;
    int ID;
    Grid grid;
    PreviewSystem previewSystem;
    ObjectDatabase database;
    GridData floorData, furnitureData, ceilingData;
    ObjectPlacer objectPlacer;

    public GridState(int ID, Grid grid, PreviewSystem previewSystem, ObjectDatabase database, GridData gridData, ObjectPlacer objectPlacer)
    {
        selectedObjectIndex = database.objectsData.FindIndex(data => data.ID == ID);
        if (selectedObjectIndex < 0)
        {
            throw new System.Exception($"No object with ID {ID}");
        }

        previewSystem.StartShowingPlacementPreview(database.objectsData[selectedObjectIndex].prefab);
    }

    public void EndState()
    {
        
    }

    public void OnAction(Vector3 mousePosition)
    {
        bool placementValidity = CheckPlacementValidity(gridPosition, selectedObjectIndex);
        if (!placementValidity)
            return;
        
        int index = objectPlacer.PlaceObject(objectDatabase.objectsData[selectedObjectIndex].prefab, grid.CellToWorld(gridPosition));

        GridData selectedData = GetSelectedData(selectedObjectIndex);

        selectedData.AddObjectAt(gridPosition, objectDatabase.objectsData[selectedObjectIndex].positionsFilled, objectDatabase.objectsData[selectedObjectIndex].ID, index);
    }

    public void UpdateState(Vector3 mousePosition)
    {
        preview.UpdatePosition(grid.CellToWorld(gridPosition));
    }

    public void Rotate(Vector3Int gridPosition)
    {
        
    }

    public void OnHold(Vector3 mousePosition)
    {
        
    }

    private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedObjectIndex)
    {
        GridData selectedData = GetSelectedData(selectedObjectIndex);
        
        return selectedData.CanPlaceObjectAt(gridPosition, objectDatabase.objectsData[selectedObjectIndex].positionsFilled);
        
    }
}
