using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders available save slots in a scrollable UI container and binds slot action listeners.
/// Automatically updates in response to SaveLoadManager lifecycle events.
/// </summary>
public class SaveSlotListUI : MonoBehaviour
{
    [SerializeField] private SaveLoadManager _saveLoadManager;
    [SerializeField] private Transform _rowContainer;
    [SerializeField] private SaveSlotRow _rowPrefab;

    private readonly List<SaveSlotRow> _spawnedRows = new();

    private void OnEnable()
    {
        if (_saveLoadManager == null || _rowContainer == null || _rowPrefab == null)
        {
            Debug.LogError("SaveSlotListUI: _saveLoadManager, _rowContainer, and _rowPrefab must all be assigned in the Inspector.");
            enabled = false;
            return;
        }

        _saveLoadManager.OnSlotSaved += HandleSlotsChanged;
        _saveLoadManager.OnSlotDeleted += HandleSlotsChanged;

        Refresh();
    }

    private void OnDisable()
    {
        if (_saveLoadManager == null)
            return;

        _saveLoadManager.OnSlotSaved -= HandleSlotsChanged;
        _saveLoadManager.OnSlotDeleted -= HandleSlotsChanged;
    }

    private void HandleSlotsChanged(string _) => Refresh();

    /// <summary>Clears active rows and instantiates new entries sorted alphabetically.</summary>
    public void Refresh()
    {
        foreach (SaveSlotRow row in _spawnedRows)
        {
            if (row != null)
                Destroy(row.gameObject);
        }
        _spawnedRows.Clear();

        string[] slotNames = _saveLoadManager.GetAvailableSlots();
        System.Array.Sort(slotNames); // Enforce deterministic UI ordering

        foreach (string slotName in slotNames)
        {
            SaveSlotRow row = Instantiate(_rowPrefab, _rowContainer);
            row.Bind(slotName, _saveLoadManager);
            _spawnedRows.Add(row);
        }
    }

    /// <summary>Triggers slot persistence through SaveLoadManager.</summary>
    public void SaveNewSlot(string slotName)
    {
        _saveLoadManager.SaveToSlot(slotName);
        // Refresh triggers via OnSlotSaved event.
    }
}

/// <summary>
/// Row view controller for save slot entries. Binds load and delete actions to a specific slot name.
/// </summary>
public class SaveSlotRow : MonoBehaviour
{
    [SerializeField] private Text _label; // Use TMP_Text if using TextMeshPro
    [SerializeField] private Button _loadButton;
    [SerializeField] private Button _deleteButton;

    private string _slotName;
    private SaveLoadManager _saveLoadManager;

    public void Bind(string slotName, SaveLoadManager saveLoadManager)
    {
        _slotName = slotName;
        _saveLoadManager = saveLoadManager;

        if (_label != null)
            _label.text = slotName;

        _loadButton.onClick.RemoveAllListeners();
        _loadButton.onClick.AddListener(() => _saveLoadManager.LoadFromSlot(_slotName));

        _deleteButton.onClick.RemoveAllListeners();
        _deleteButton.onClick.AddListener(() => _saveLoadManager.DeleteSlot(_slotName));
    }
}