using System;
using System.IO;
using System.Linq;
using GposeCameraSaver.Models;

namespace GposeCameraSaver.Services;

public sealed class CameraPresetService
{
    private readonly CameraAccessor cameraAccessor;
    private readonly GposeStateService gposeStateService;
    private readonly PresetStorageService storageService;
    private readonly TerritoryNameService territoryNameService;

    public CameraPresetService(
        CameraAccessor cameraAccessor,
        GposeStateService gposeStateService,
        PresetStorageService storageService,
        TerritoryNameService territoryNameService)
    {
        this.cameraAccessor = cameraAccessor;
        this.gposeStateService = gposeStateService;
        this.storageService = storageService;
        this.territoryNameService = territoryNameService;
    }

    public CameraPresetListItem? PendingLoadPreset { get; private set; }

    public CameraPresetListItem? PendingDeletePreset { get; private set; }

    public string LastMessage { get; private set; } = string.Empty;

    public string LastMessageKey { get; private set; } = string.Empty;

    public string LastMessageDetail { get; private set; } = string.Empty;

    public string LastError { get; private set; } = string.Empty;

    public bool SaveCurrentCamera(string note)
    {
        LastError = string.Empty;
        LastMessageKey = string.Empty;
        LastMessageDetail = string.Empty;
        if (!gposeStateService.IsInGpose)
        {
            LastError = "Please enter GPose before saving a camera preset.";
            LastMessageKey = "SaveFailed";
            LastMessageDetail = LastError;
            return false;
        }

        if (!cameraAccessor.TryCaptureCurrentCamera(out var snapshot, out var error))
        {
            LastError = error;
            LastMessageKey = "SaveFailed";
            LastMessageDetail = error;
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        var local = DateTime.Now;
        var preset = new CameraPresetFile
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"GPose Camera {local:yyyy-MM-dd HH:mm:ss}",
            Note = note.Trim(),
            CreatedAtUtc = nowUtc,
            CreatedAtLocal = local.ToString("yyyy-MM-dd HH:mm:ss"),
            TerritoryType = territoryNameService.TerritoryType,
            TerritoryName = territoryNameService.TerritoryName,
            MapId = territoryNameService.MapId,
            ActualTerritoryType = territoryNameService.TerritoryType,
            ActualTerritoryName = territoryNameService.TerritoryName,
            ActualMapId = territoryNameService.MapId,
            CapturedFields = [.. cameraAccessor.GetCapturedFieldNames()],
            Camera = snapshot,
        };

        try
        {
            var path = storageService.SavePreset(preset);
            LastMessage = $"Saved preset: {path}";
            LastMessageKey = "Saved";
            LastMessageDetail = Path.GetFileName(path);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastMessageKey = "SaveFailed";
            LastMessageDetail = ex.Message;
            return false;
        }
    }

    public void RequestLoadPreset(CameraPresetListItem item)
    {
        PendingLoadPreset = CloneItem(item);
    }

    public bool ConfirmLoadPreset()
    {
        LastError = string.Empty;
        LastMessageKey = string.Empty;
        LastMessageDetail = string.Empty;
        if (PendingLoadPreset == null)
        {
            LastError = "No preset selected.";
            LastMessageKey = "LoadFailed";
            LastMessageDetail = LastError;
            return false;
        }

        if (!gposeStateService.IsInGpose)
        {
            LastError = "Please enter GPose before loading a camera preset.";
            LastMessageKey = "LoadFailed";
            LastMessageDetail = LastError;
            return false;
        }

        if (!PendingLoadPreset.IsValid)
        {
            LastError = PendingLoadPreset.ValidationMessage;
            LastMessageKey = "LoadFailed";
            LastMessageDetail = LastError;
            return false;
        }

        if (IsCameraPayloadEmpty(PendingLoadPreset.Preset.Camera))
        {
            LastError = "Preset camera data is empty or invalid.";
            LastMessageKey = "LoadFailed";
            LastMessageDetail = LastError;
            return false;
        }

        if (!cameraAccessor.TryApplyCamera(PendingLoadPreset.Preset.Camera, out var error))
        {
            LastError = error;
            LastMessageKey = "LoadFailed";
            LastMessageDetail = error;
            return false;
        }

        LastMessage = $"Started camera load from: {Path.GetFileName(PendingLoadPreset.FilePath)}";
        LastMessageKey = "Loaded";
        LastMessageDetail = Path.GetFileName(PendingLoadPreset.FilePath);
        PendingLoadPreset = null;
        return true;
    }

    public void CancelLoadPreset() => PendingLoadPreset = null;

    public void RequestDeletePreset(CameraPresetListItem item)
    {
        PendingDeletePreset = CloneItem(item);
    }

    public bool ConfirmDeletePreset()
    {
        LastError = string.Empty;
        LastMessageKey = string.Empty;
        LastMessageDetail = string.Empty;
        if (PendingDeletePreset == null)
        {
            LastError = "No preset selected.";
            LastMessageKey = "DeleteFailed";
            LastMessageDetail = LastError;
            return false;
        }

        var path = PendingDeletePreset.FilePath;
        if (!storageService.DeletePresetFile(PendingDeletePreset, out var error))
        {
            LastError = error;
            LastMessageKey = "DeleteFailed";
            LastMessageDetail = error;
            return false;
        }

        LastMessage = string.IsNullOrWhiteSpace(error)
            ? $"Deleted preset: {path}"
            : $"{error}: {path}";
        LastMessageKey = "Deleted";
        LastMessageDetail = string.IsNullOrWhiteSpace(error) ? Path.GetFileName(path) : error;
        PendingDeletePreset = null;
        return true;
    }

    public void CancelDeletePreset() => PendingDeletePreset = null;

    private static bool IsCameraPayloadEmpty(CameraSnapshot? camera)
    {
        if (camera == null)
            return true;

        var scalarValues = new[]
        {
            camera.Distance,
            camera.HRotation,
            camera.VRotation,
            camera.FoV,
            camera.AddedFoV,
            camera.Pan,
            camera.Tilt,
            camera.Roll,
        };

        return scalarValues.All(value => value == null) &&
               camera.LastPosition == null &&
               camera.LastLookAtVector == null &&
               camera.ScenePosition == null &&
               camera.RenderOrigin == null;
    }

    private static CameraPresetListItem CloneItem(CameraPresetListItem item)
    {
        return new CameraPresetListItem
        {
            Preset = item.Preset,
            FilePath = item.FilePath,
            SourceKind = item.SourceKind,
            IsValid = item.IsValid,
            ValidationMessage = item.ValidationMessage,
        };
    }
}
