using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bridges Unity UI Button OnClick events with dynamic InputField text values for slot creation.
/// </summary>
public class SaveNewSlotButtonBridge : MonoBehaviour
{
    [SerializeField] private InputField _nameInput; // Use TMP_InputField for TextMeshPro
    [SerializeField] private SaveSlotListUI _saveSlotListUI;

    /// <summary>Reads current input field text, triggers slot save, and clears field state.</summary>
    public void SaveNewSlot()
    {
        _saveSlotListUI.SaveNewSlot(_nameInput.text);
        _nameInput.text = "";
    }
}