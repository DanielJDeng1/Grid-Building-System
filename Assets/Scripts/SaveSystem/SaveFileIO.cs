using System;
using System.IO;
using UnityEngine;

/// <summary>
/// File I/O operations for BuildingSaveData DTOs. Keeps persistence decoupled from gameplay logic.
/// </summary>
public static class SaveFileIO
{
    private const string SaveFileExtension = ".buildsave.json";

    /// <summary>
    /// Target persistent data path writable across all target platforms.
    /// </summary>
    public static string DefaultSaveDirectory => Application.persistentDataPath;

    public static string GetSavePath(string saveName, string directory = null)
    {
        directory ??= DefaultSaveDirectory;
        return Path.Combine(directory, saveName + SaveFileExtension);
    }

    /// <summary>
    /// Serializes BuildingSaveData to JSON. Catches I/O exceptions internally to prevent game crashes.
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
    /// Reads and deserializes a BuildingSaveData file. Returns null if missing or invalid.
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
                // Warn on version mismatch; schema migration required when CurrentVersion increments.
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

    /// <summary>
    /// Returns available save slot names by stripping extensions from files in the target directory.
    /// </summary>
    public static string[] ListSaveNames(string directory = null)
    {
        directory ??= DefaultSaveDirectory;

        if (!Directory.Exists(directory))
            return Array.Empty<string>();

        string[] files = Directory.GetFiles(directory, "*" + SaveFileExtension);
        string[] names = new string[files.Length];

        for (int i = 0; i < files.Length; i++)
        {
            string fileName = Path.GetFileName(files[i]);
            names[i] = fileName.Substring(0, fileName.Length - SaveFileExtension.Length);
        }

        return names;
    }

    /// <summary>
    /// Writes BuildingSaveData directly to an absolute file path for custom exports or system file dialogs.
    /// </summary>
    public static bool SaveToPath(BuildingSaveData data, string fullPath)
    {
        try
        {
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string json = JsonUtility.ToJson(data, prettyPrint: true);
            File.WriteAllText(fullPath, json);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveFileIO: failed to save to '{fullPath}' - {e}");
            return false;
        }
    }

    /// <summary>
    /// Reads BuildingSaveData directly from an absolute file path.
    /// </summary>
    public static BuildingSaveData LoadFromPath(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"SaveFileIO: '{fullPath}' does not exist.");
            return null;
        }

        try
        {
            string json = File.ReadAllText(fullPath);
            BuildingSaveData data = JsonUtility.FromJson<BuildingSaveData>(json);

            if (data == null)
            {
                Debug.LogError($"SaveFileIO: '{fullPath}' parsed to null - file may be corrupt or not a building save.");
                return null;
            }

            if (data.saveVersion != BuildingSaveData.CurrentVersion)
            {
                Debug.LogWarning($"SaveFileIO: '{fullPath}' has saveVersion {data.saveVersion}, " +
                                  $"expected {BuildingSaveData.CurrentVersion}. Attempting to load anyway.");
            }

            return data;
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveFileIO: failed to load '{fullPath}' - {e}");
            return null;
        }
    }

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