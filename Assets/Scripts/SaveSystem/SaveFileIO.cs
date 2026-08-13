using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Pure file I/O for BuildingSaveData - no reference to PlacementSystem, GridData, or any other
/// gameplay type, only the DTO itself. Deliberately kept this way so a future change to the
/// building system's internals never has a reason to touch this file, and vice versa.
/// 
/// Uses JsonUtility (built into Unity, no extra package dependency) - sufficient here since
/// every DTO in BuildingSaveData.cs is a plain [Serializable] struct/class of primitives, lists,
/// and Vector3Int/enum fields, all of which JsonUtility handles natively.
/// </summary>
public static class SaveFileIO
{
    private const string SaveFileExtension = ".buildsave.json";

    /// <summary>
    /// Default save directory - Application.persistentDataPath is writable on every platform
    /// Unity targets (unlike Application.dataPath, which is read-only on most).
    /// </summary>
    public static string DefaultSaveDirectory => Application.persistentDataPath;

    public static string GetSavePath(string saveName, string directory = null)
    {
        directory ??= DefaultSaveDirectory;
        return Path.Combine(directory, saveName + SaveFileExtension);
    }

    /// <summary>
    /// Serializes and writes a BuildingSaveData to disk. Returns true on success. Never throws -
    /// I/O and serialization failures (disk full, permissions, etc.) are caught and logged,
    /// since a failed save should be reported to the player, not crash the game.
    /// </summary>
    public static bool Save(BuildingSaveData data, string saveName, string directory = null)
    {
        try
        {
            directory ??= DefaultSaveDirectory;
            Directory.CreateDirectory(directory);

            string json = JsonUtility.ToJson(data, prettyPrint: true);
            string path = GetSavePath(saveName, directory);

            File.WriteAllText(path, json);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveFileIO: failed to save '{saveName}' - {e}");
            return false;
        }
    }

    /// <summary>
    /// Reads and deserializes a BuildingSaveData from disk. Returns null (not a throw) if the
    /// file doesn't exist or fails to parse, so callers can distinguish "no save yet" /
    /// "corrupt save" from a successful load without wrapping every call site in a try/catch.
    /// </summary>
    public static BuildingSaveData Load(string saveName, string directory = null)
    {
        string path = GetSavePath(saveName, directory);

        if (!File.Exists(path))
            return null;

        try
        {
            string json = File.ReadAllText(path);
            BuildingSaveData data = JsonUtility.FromJson<BuildingSaveData>(json);

            if (data == null)
            {
                Debug.LogError($"SaveFileIO: '{saveName}' parsed to null - file may be corrupt.");
                return null;
            }

            if (data.saveVersion != BuildingSaveData.CurrentVersion)
            {
                // No migrations exist yet (CurrentVersion has only ever been 1). Logged rather
                // than rejected outright, since a mismatched-but-still-parseable version is
                // more useful to the caller as a warning than a hard failure - add a real
                // migration step here once CurrentVersion is bumped for a breaking change.
                Debug.LogWarning($"SaveFileIO: '{saveName}' has saveVersion {data.saveVersion}, " +
                                  $"expected {BuildingSaveData.CurrentVersion}. Attempting to load anyway.");
            }

            return data;
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveFileIO: failed to load '{saveName}' - {e}");
            return null;
        }
    }

    public static bool SaveExists(string saveName, string directory = null) =>
        File.Exists(GetSavePath(saveName, directory));

    public static bool DeleteSave(string saveName, string directory = null)
    {
        try
        {
            string path = GetSavePath(saveName, directory);
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveFileIO: failed to delete '{saveName}' - {e}");
            return false;
        }
    }
}