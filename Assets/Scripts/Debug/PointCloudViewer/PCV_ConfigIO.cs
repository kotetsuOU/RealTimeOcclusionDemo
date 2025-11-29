using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public static class PCV_ConfigIO
{
    private const string DEFAULT_SAVE_FOLDER = "Assets/Config/PCV_Profiles";

    [Serializable]
    public class PCV_ProfileData
    {
        // File Settings
        public List<FileSettingDTO> fileSettings = new List<FileSettingDTO>();

        // Neighbor Search
        public float voxelSize;
        public float searchRadius;
        public Color neighborColor;
        public int neighborThreshold;
        public int voxelDensityThreshold;

        // Morphology
        public int erosionIterations;
        public int dilationIterations;

        // Density Complementation
        public int complementationDensityThreshold;
        public uint complementationPointsPerAxis;
        public Color complementationPointColor;
        public bool complementationRandomPlacement;

        // Rendering
        public float pointSize;
        public Color outlineColor;

        // GPU Settings
        public bool useGpuNoiseFilter;
        public bool useGpuDensityFilter;
        public bool useGpuDensityComplementation;
    }

    [Serializable]
    public class FileSettingDTO
    {
        public string filePath;
        public bool useFile;
    }

    public static void SaveConfig(PCV_Settings settings, string fileName)
    {
        if (settings == null)
        {
            UnityEngine.Debug.LogError("[PCV_ConfigIO] Settings component is null.");
            return;
        }

        PCV_ProfileData data = new PCV_ProfileData();

        if (settings.fileSettings != null)
        {
            foreach (var fs in settings.fileSettings)
            {
                data.fileSettings.Add(new FileSettingDTO
                {
                    filePath = fs.filePath,
                    useFile = fs.useFile
                });
            }
        }

        data.voxelSize = settings.voxelSize;
        data.searchRadius = settings.searchRadius;
        data.neighborColor = settings.neighborColor;
        data.neighborThreshold = settings.neighborThreshold;
        data.voxelDensityThreshold = settings.voxelDensityThreshold;

        data.erosionIterations = settings.erosionIterations;
        data.dilationIterations = settings.dilationIterations;

        data.complementationDensityThreshold = settings.complementationDensityThreshold;
        data.complementationPointsPerAxis = settings.complementationPointsPerAxis;
        data.complementationPointColor = settings.complementationPointColor;
        data.complementationRandomPlacement = settings.complementationRandomPlacement;

        data.pointSize = settings.pointSize;
        data.outlineColor = settings.outlineColor;

        data.useGpuNoiseFilter = settings.useGpuNoiseFilter;
        data.useGpuDensityFilter = settings.useGpuDensityFilter;
        data.useGpuDensityComplementation = settings.useGpuDensityComplementation;

        if (!Directory.Exists(DEFAULT_SAVE_FOLDER))
        {
            Directory.CreateDirectory(DEFAULT_SAVE_FOLDER);
        }

        string safeFileName = fileName.EndsWith(".json") ? fileName : fileName + ".json";
        string fullPath = Path.Combine(DEFAULT_SAVE_FOLDER, safeFileName);

        try
        {
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(fullPath, json);
            UnityEngine.Debug.Log($"[PCV_ConfigIO] Profile saved to: {fullPath}");
#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[PCV_ConfigIO] Failed to save profile: {e.Message}");
        }
    }

    public static void LoadConfig(PCV_Settings settings, string fileName)
    {
        if (settings == null)
        {
            UnityEngine.Debug.LogError("[PCV_ConfigIO] Settings component is null.");
            return;
        }

        string safeFileName = fileName.EndsWith(".json") ? fileName : fileName + ".json";
        string fullPath = Path.Combine(DEFAULT_SAVE_FOLDER, safeFileName);

        if (!File.Exists(fullPath))
        {
            UnityEngine.Debug.LogError($"[PCV_ConfigIO] File not found: {fullPath}");
            return;
        }

        try
        {
            string json = File.ReadAllText(fullPath);
            PCV_ProfileData data = JsonUtility.FromJson<PCV_ProfileData>(json);

            if (data == null)
            {
                UnityEngine.Debug.LogError("[PCV_ConfigIO] Failed to parse JSON.");
                return;
            }

#if UNITY_EDITOR
            Undo.RecordObject(settings, "Load PCV Profile");
#endif
            if (settings.fileSettings != null)
            {
                for (int i = 0; i < settings.fileSettings.Length; i++)
                {
                    FileSettingDTO matchedDto = null;
                    foreach (var dto in data.fileSettings)
                    {
                        if (dto.filePath == settings.fileSettings[i].filePath)
                        {
                            matchedDto = dto;
                            break;
                        }
                    }

                    if (matchedDto != null)
                    {
                        settings.fileSettings[i].useFile = matchedDto.useFile;
                    }
                }
            }

            settings.voxelSize = data.voxelSize;
            settings.searchRadius = data.searchRadius;
            settings.neighborColor = data.neighborColor;
            settings.neighborThreshold = data.neighborThreshold;
            settings.voxelDensityThreshold = data.voxelDensityThreshold;

            settings.erosionIterations = data.erosionIterations;
            settings.dilationIterations = data.dilationIterations;

            settings.complementationDensityThreshold = data.complementationDensityThreshold;
            settings.complementationPointsPerAxis = data.complementationPointsPerAxis; // uint“¯Žm
            settings.complementationPointColor = data.complementationPointColor;
            settings.complementationRandomPlacement = data.complementationRandomPlacement;

            settings.pointSize = data.pointSize;
            settings.outlineColor = data.outlineColor;

            settings.useGpuNoiseFilter = data.useGpuNoiseFilter;
            settings.useGpuDensityFilter = data.useGpuDensityFilter;
            settings.useGpuDensityComplementation = data.useGpuDensityComplementation;

#if UNITY_EDITOR
            EditorUtility.SetDirty(settings);
#endif
            UnityEngine.Debug.Log($"[PCV_ConfigIO] Profile loaded from: {fullPath}");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[PCV_ConfigIO] Failed to load profile: {e.Message}");
        }
    }
}