using UnityEngine;

public class EdgeState : MonoBehaviour
{

    private int selectedObjectIndex = -1;
    int ID;
    Grid grid;
    PreviewSystem previewSystem;
    EdgeDatabase database;
    GridData floorData, furnitureData, ceilingData;
    ObjectPlacer objectPlacer;
    GridData selectedData;

    EdgeRotation currentRotation = EdgeRotation.Deg0;

    public EdgeState(int ID, Grid grid, PreviewSystem previewSystem, EdgeDatabase database, ObjectPlacer objectPlacer, GridData floorData, GridData furnitureData, GridData ceilingData)
    {
        selectedObjectIndex = database.edgeData.FindIndex(data => data.ID == ID);

        if (selectedObjectIndex < 0)
        {
            throw new System.Exception($"No object with ID {ID}");
        }
        this.ID = ID;
        this.grid = grid;
        this.previewSystem = previewSystem;
        this.database = database;
        this.floorData = floorData;
        this.furnitureData = furnitureData;
        this.ceilingData = ceilingData;
        this.objectPlacer = objectPlacer;

        selectedData = GetSelectedData(selectedObjectIndex);

        
    }

    public void EndState()
    {
        previewSystem.StopShowingPreview();
    }

    public void OnAction(Vector3Int gridPosition)
    {
        bool placementValidity = CheckPlacementValidity(gridPosition, selectedObjectIndex);

        if (!placementValidity)
            return;
        
        int index = objectPlacer.PlaceEdge(database.edgeData[selectedObjectIndex].prefab, grid.CellToWorld(gridPosition), currentRotation);

        selectedData.AddEdgeAt(new Edge(gridPosition, gridPosition), database.edgeData[selectedObjectIndex].ID, index, currentRotation);
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        previewSystem.UpdatePosition(grid.CellToWorld(gridPosition));
    }

    public void Rotate(Vector3Int gridPosition)
    {
        currentRotation = (EdgeRotation)(((int)currentRotation + 1) % 2);
    }   

    public void OnHold(Vector3Int gridPosition)
    {

    }

    private GridData GetSelectedData(int selectedObjectIndex)
    {
        GridData selectedData = floorData;
        if (database.edgeData[selectedObjectIndex].buildType == ObjectBuildType.Furniture)
            selectedData = furnitureData;
        else if (database.edgeData[selectedObjectIndex].buildType == ObjectBuildType.Ceiling)
            selectedData = ceilingData;
        return selectedData;
    }

    private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedObjectIndex)
    {
        return selectedData.CanPlaceEdgeAt(new Edge(gridPosition, gridPosition), currentRotation);
    }

}

public enum EdgeRotation
{
    Deg0,
    Deg90
}
