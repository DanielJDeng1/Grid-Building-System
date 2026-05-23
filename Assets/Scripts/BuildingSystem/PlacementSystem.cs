using UnityEngine;

public class PlacementSystem : MonoBehaviour
{
    [SerializeField] private InputManager inputManager;

    [SerializeField] private Grid grid;

    [SerializeField] private ObjectDatabase objectDatabase;

    [SerializeField] private EdgeDatabase edgeDatabase;

    private int selectedObjectIndex = -1;

    [SerializeField] private GameObject gridVisualization;

    private void Start()
    {
        StopPlacement();
    }

    public void StartPlacement(int ID)
    {
        Debug.Log("active");
        StopPlacement();
        selectedObjectIndex = objectDatabase.objectsData.FindIndex(data => data.ID == ID);
        
        if (selectedObjectIndex < 0){
            Debug.LogError($"No ID found {ID}");
            return;
        }

        gridVisualization.SetActive(true);

        inputManager.OnMouseRelease += PlaceStructure;
        inputManager.OnExit += StopPlacement;
    }

    public void StartEdgePlacement(int ID)
    {
        StopPlacement();
        selectedObjectIndex = edgeDatabase.edgeData.FindIndex(data => data.ID == ID);
        
        if (selectedObjectIndex < 0){
            Debug.LogError($"No ID found {ID}");
            return;
        }

        gridVisualization.SetActive(true);

        inputManager.OnMouseRelease += PlaceEdge;
        inputManager.OnExit += StopPlacement;
    }

    private void PlaceStructure()
    {
        if (inputManager.IsPointerOverUI())
        {
            return;
        }

        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);
        GameObject newObject = Instantiate(objectDatabase.objectsData[selectedObjectIndex].prefab);
        newObject.transform.position = grid.CellToWorld(gridPosition);
    }

    private void PlaceEdge()
    {
        
    }

    private void StopPlacement()
    {
        selectedObjectIndex = -1;
        gridVisualization.SetActive(false);
        inputManager.OnMouseRelease -= PlaceEdge;
        inputManager.OnMouseRelease -= PlaceStructure;
        inputManager.OnExit -= StopPlacement;
    }

    private void Update()
    {
        if (selectedObjectIndex < 0)
            return;

        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);

    }

}
