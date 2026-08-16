#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX || UNITY_EDITOR
#define HAS_FILE_BROWSER
#endif

using System;
using UnityEngine;
using SFB;

/// <summary>
/// UI facing system for save/load operations.
/// Bridges UI events with PlacementSystem state using persistent local slots and native file dialogs.
/// </summary>
public class SaveLoadManager : MonoBehaviour
{
    [SerializeField] private PlacementSystem _placementSystem;

    [Tooltip("Export extension without leading dot (e.g. json).")]
    [SerializeField] private string _exportExtension = "json";

    [Tooltip("Suggested default filename for native save dialogs.")]
    [SerializeField] private string _exportDefaultName = "MyBuilding";

    public event Action<string> OnSlotSaved;
    public event Action<string> OnSlotLoaded;
    public event Action<string> OnSlotDeleted;
    public event Action<string> OnExported;
    public event Action<string> OnImported;
    public event Action<string> OnOperationFailed;

    private void Awake()
    {
        if (_placementSystem == null)
        {
            Debug.LogError("SaveLoadManager: _placementSystem must be assigned in the Inspector. Disabling.");
            enabled = false;
        }
    }

    #region Slots

    /// <summary>Reads available slot names directly from disk. Uncached to reflect external file changes instantly.</summary>
    public string[] GetAvailableSlots() => SaveFileIO.ListSaveNames();

    public bool SlotExists(string slotName) => SaveFileIO.SaveExists(slotName);

    /// <summary>Captures current placement state and overwrites target slot. Unsanitized input.</summary>
    public void SaveToSlot(string slotName)
    {
        if (string.IsNullOrWhiteSpace(slotName))
        {
            OnOperationFailed?.Invoke("Slot name cannot be empty.");
            return;
        }

        BuildingSaveData data = _placementSystem.CaptureSaveData();

        if (SaveFileIO.Save(data, slotName))
            OnSlotSaved?.Invoke(slotName);
        else
            OnOperationFailed?.Invoke($"Failed to save slot '{slotName}' - see console for details.");
    }

    /// <summary>Loads slot payload and overwrites active placement layout.</summary>
    public void LoadFromSlot(string slotName)
    {
        BuildingSaveData data = SaveFileIO.Load(slotName);

        if (data == null)
        {
            OnOperationFailed?.Invoke($"Failed to load slot '{slotName}' - see console for details.");
            return;
        }

        _placementSystem.LoadSaveData(data);
        OnSlotLoaded?.Invoke(slotName);
    }

    public void DeleteSlot(string slotName)
    {
        if (SaveFileIO.DeleteSave(slotName))
            OnSlotDeleted?.Invoke(slotName);
        else
            OnOperationFailed?.Invoke($"Failed to delete slot '{slotName}' - see console for details.");
    }

    #endregion

    #region Import / Export (native file dialog)

    /// <summary>Prompts native save dialog and exports active layout to specified path.</summary>
    public void ExportToFile()
    {
#if HAS_FILE_BROWSER
        string path = StandaloneFileBrowser.SaveFilePanel("Export Building", "", _exportDefaultName, _exportExtension);

        if (string.IsNullOrEmpty(path))
            return; // Dialog cancelled

        BuildingSaveData data = _placementSystem.CaptureSaveData();

        if (SaveFileIO.SaveToPath(data, path))
            OnExported?.Invoke(path);
        else
            OnOperationFailed?.Invoke($"Failed to export to '{path}' - see console for details.");
#else
        OnOperationFailed?.Invoke("Export requires Standalone File Browser, which isn't available on this platform/build.");
        Debug.LogWarning("SaveLoadManager.ExportToFile: Standalone File Browser not available (desktop-only plugin). " +
                          "Install it via Package Manager -> Add package from git URL -> " +
                          "https://github.com/gkngkc/UnityStandaloneFileBrowser.git#upm");
#endif
    }

    /// <summary>Prompts native file dialog and overwrites current layout with selected file payload.</summary>
    public void ImportFromFile()
    {
#if HAS_FILE_BROWSER
        string[] paths = StandaloneFileBrowser.OpenFilePanel("Import Building", "", _exportExtension, false);

        if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0]))
            return; // Dialog cancelled

        string path = paths[0];
        BuildingSaveData data = SaveFileIO.LoadFromPath(path);

        if (data == null)
        {
            OnOperationFailed?.Invoke($"Failed to import '{path}' - see console for details.");
            return;
        }

        _placementSystem.LoadSaveData(data);
        OnImported?.Invoke(path);
#else
        OnOperationFailed?.Invoke("Import requires Standalone File Browser, which isn't available on this platform/build.");
        Debug.LogWarning("SaveLoadManager.ImportFromFile: Standalone File Browser not available (desktop-only plugin). " +
                          "Install it via Package Manager -> Add package from git URL -> " +
                          "https://github.com/gkngkc/UnityStandaloneFileBrowser.git#upm");
#endif
    }

    #endregion
}