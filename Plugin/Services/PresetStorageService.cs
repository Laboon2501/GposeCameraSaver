using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Dalamud.Plugin.Services;
using GposeCameraSaver.Models;

namespace GposeCameraSaver.Services;

public sealed class PresetStorageService
{
    private readonly IPluginLog log;
    private readonly CameraAccessor cameraAccessor;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
    private string externalFolder = string.Empty;

    public PresetStorageService(IPluginLog log, CameraAccessor cameraAccessor, string presetFolder)
    {
        this.log = log;
        this.cameraAccessor = cameraAccessor;
        LocalPresetFolder = string.IsNullOrWhiteSpace(presetFolder) ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Presets") : presetFolder;
        Directory.CreateDirectory(LocalPresetFolder);
    }

    public string LocalPresetFolder { get; }

    public List<CameraPresetListItem> Presets { get; private set; } = new();

    public int LoadErrorCount { get; private set; }

    public string LastError { get; private set; } = string.Empty;

    public string ExternalFolder => externalFolder;

    public string SavePreset(CameraPresetFile preset)
    {
        Directory.CreateDirectory(LocalPresetFolder);
        var fileName = BuildFileName(preset);
        var path = Path.Combine(LocalPresetFolder, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(preset, jsonOptions));
        Reload(externalFolder);
        return path;
    }

    public CameraPresetListItem? LoadPresetFile(string path, PresetSourceKind sourceKind)
    {
        try
        {
            var json = File.ReadAllText(path);
            var preset = JsonSerializer.Deserialize<CameraPresetFile>(json, jsonOptions);
            if (preset == null)
                throw new InvalidDataException("JSON did not contain a preset.");

            var item = new CameraPresetListItem
            {
                Preset = preset,
                FilePath = path,
                SourceKind = sourceKind,
            };
            item.IsValid = cameraAccessor.ValidateSnapshot(preset.Camera, out var validation);
            item.ValidationMessage = item.IsValid ? "OK" : validation;
            return item;
        }
        catch (Exception ex)
        {
            LoadErrorCount++;
            LastError = $"{Path.GetFileName(path)}: {ex.Message}";
            log.Warning(ex, "Failed to load camera preset {Path}", path);
            return null;
        }
    }

    public List<CameraPresetListItem> ScanLocalPresets()
    {
        Directory.CreateDirectory(LocalPresetFolder);
        return ScanFolder(LocalPresetFolder, PresetSourceKind.Local);
    }

    public List<CameraPresetListItem> ScanExternalPresets(string folder)
    {
        externalFolder = folder.Trim();
        if (string.IsNullOrWhiteSpace(externalFolder))
            return new List<CameraPresetListItem>();

        if (!Directory.Exists(externalFolder))
        {
            LastError = $"External folder does not exist: {externalFolder}";
            return new List<CameraPresetListItem>();
        }

        return ScanFolder(externalFolder, PresetSourceKind.External);
    }

    public bool DeletePresetFile(CameraPresetListItem item, out string error)
    {
        try
        {
            if (!File.Exists(item.FilePath))
            {
                Reload(externalFolder);
                error = "File already missing";
                LastError = error;
                return true;
            }

            File.Delete(item.FilePath);
            Reload(externalFolder);
            error = string.Empty;
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = $"UnauthorizedAccessException: {ex.Message}";
            LastError = error;
            log.Warning(ex, "Failed to delete camera preset {Path}", item.FilePath);
            return false;
        }
        catch (FileNotFoundException ex)
        {
            Reload(externalFolder);
            error = $"File already missing: {ex.Message}";
            LastError = error;
            return true;
        }
        catch (IOException ex)
        {
            error = $"IOException: {ex.Message}";
            LastError = error;
            log.Warning(ex, "Failed to delete camera preset {Path}", item.FilePath);
            return false;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            LastError = error;
            log.Warning(ex, "Failed to delete camera preset {Path}", item.FilePath);
            return false;
        }
    }

    public void Reload(string? externalFolderPath = null)
    {
        LoadErrorCount = 0;
        LastError = string.Empty;
        var items = ScanLocalPresets();
        if (!string.IsNullOrWhiteSpace(externalFolderPath))
            items.AddRange(ScanExternalPresets(externalFolderPath));

        Presets = items
            .GroupBy(item => Path.GetFullPath(item.FilePath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => item.Preset.CreatedAtUtc)
            .ToList();
    }

    public void ClearExternal()
    {
        externalFolder = string.Empty;
        Presets = Presets.Where(item => item.SourceKind == PresetSourceKind.Local).ToList();
    }

    private List<CameraPresetListItem> ScanFolder(string folder, PresetSourceKind sourceKind)
    {
        return Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => LoadPresetFile(path, sourceKind))
            .Where(item => item != null)
            .Cast<CameraPresetListItem>()
            .ToList();
    }

    private static string BuildFileName(CameraPresetFile preset)
    {
        var safeName = string.IsNullOrWhiteSpace(preset.Note) ? preset.Name : preset.Note;
        safeName = SanitizeFileName(safeName);
        if (safeName.Length > 32)
            safeName = safeName[..32];

        var shortId = preset.Id.Length >= 8 ? preset.Id[..8] : preset.Id;
        return $"{preset.CreatedAtUtc.ToLocalTime():yyyyMMdd_HHmmss}_{safeName}_{shortId}.json";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "CameraPreset" : sanitized;
    }
}
