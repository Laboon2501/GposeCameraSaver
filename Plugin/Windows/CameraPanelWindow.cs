using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using GposeCameraSaver.Models;
using GposeCameraSaver.Services;

namespace GposeCameraSaver.Windows;

public sealed class CameraPanelWindow : Window, IDisposable
{
    private const string LoadPopupId = "ConfirmLoadPreset##GCS";
    private const string DeletePopupId = "ConfirmDeletePreset##GCS";

    private readonly Configuration configuration;
    private readonly CameraPresetService presetService;
    private readonly PresetStorageService storageService;
    private readonly GposeStateService gposeStateService;
    private readonly TerritoryNameService territoryNameService;
    private readonly LocalizationService loc;

    private string note = string.Empty;
    private bool openSettingsTab;
    private bool openLoadPopupRequested;
    private bool openDeletePopupRequested;
    private Vector2 loadPopupPosition;
    private Vector2 deletePopupPosition;
    private string externalFolderInput;

    public CameraPanelWindow(
        Configuration configuration,
        CameraPresetService presetService,
        PresetStorageService storageService,
        GposeStateService gposeStateService,
        TerritoryNameService territoryNameService,
        LocalizationService loc)
        : base("Gpose Camera Saver###GCSCameraPanel")
    {
        this.configuration = configuration;
        this.presetService = presetService;
        this.storageService = storageService;
        this.gposeStateService = gposeStateService;
        this.territoryNameService = territoryNameService;
        this.loc = loc;
        externalFolderInput = configuration.ExternalPresetFolder;

        Size = new Vector2(configuration.PanelWidth, configuration.PanelHeight);
        SizeCondition = ImGuiCond.FirstUseEver;
        Position = new Vector2(configuration.PanelPositionX, configuration.PanelPositionY);
        PositionCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose()
    {
    }

    public void OpenSettingsTab()
    {
        IsOpen = true;
        openSettingsTab = true;
    }

    public override void Draw()
    {
        SaveLayout();
        DrawHeader();
        ImGui.Separator();

        var requestSettingsTab = openSettingsTab;
        openSettingsTab = false;

        if (ImGui.BeginTabBar("##GCSTabs"))
        {
            if (ImGui.BeginTabItem($"{loc.T("CameraPresets")}##GCSCameraPresetsTab"))
            {
                DrawCameraPresetsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem($"{loc.T("Settings")}##GCSSettingsTab", requestSettingsTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        DrawLoadModal();
        DrawDeleteModal();
    }

    private void DrawHeader()
    {
        ImGui.TextUnformatted(loc.T("PluginName"));
        ImGui.SameLine();
        ImGui.TextDisabled(gposeStateService.IsInGpose ? loc.T("GposeActive") : loc.T("GposeInactive"));
        ImGui.TextWrapped($"{loc.T("ActualTerritory")}: {territoryNameService.TerritoryType} / {territoryNameService.MapId} / {territoryNameService.TerritoryName}");

        var localCount = storageService.Presets.Count(item => item.SourceKind == PresetSourceKind.Local);
        var externalCount = storageService.Presets.Count(item => item.SourceKind == PresetSourceKind.External);
        ImGui.TextDisabled($"{loc.T("Presets")}: {loc.T("Local")} {localCount} / {loc.T("External")} {externalCount}");
    }

    private void DrawCameraPresetsTab()
    {
        DrawStateAndSave();
        ImGui.Separator();
        DrawPresetTable();
    }

    private void DrawStateAndSave()
    {
        ImGui.TextUnformatted(loc.T("Note"));
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##PresetNote", ref note, 256);

        ImGui.SameLine();
        ImGui.BeginDisabled(!gposeStateService.IsInGpose);
        if (ImGui.Button($"{loc.T("SaveCurrentCamera")}##SavePreset"))
        {
            if (presetService.SaveCurrentCamera(note))
                note = string.Empty;
        }
        ImGui.EndDisabled();

        ImGui.TextWrapped(BuildStatusMessage());
    }

    private void DrawPresetTable()
    {
        DrawAreaFilter();

        if (!ImGui.BeginTable("##PresetTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY, new Vector2(0, 260)))
            return;

        ImGui.TableSetupColumn(loc.T("CreatedTime"), ImGuiTableColumnFlags.WidthFixed, 132);
        ImGui.TableSetupColumn(loc.T("Note"), ImGuiTableColumnFlags.WidthFixed, 280);
        ImGui.TableSetupColumn(loc.T("Area"), ImGuiTableColumnFlags.WidthFixed, 160);
        ImGui.TableSetupColumn(loc.T("Source"), ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn(loc.T("File"), ImGuiTableColumnFlags.WidthFixed, 190);
        ImGui.TableSetupColumn(loc.T("Actions"), ImGuiTableColumnFlags.WidthFixed, 92);
        ImGui.TableHeadersRow();

        foreach (var item in GetFilteredPresetItems())
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(item.Preset.CreatedAtLocal);

            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(GetPresetNote(item));

            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(GetPresetArea(item));

            ImGui.TableSetColumnIndex(3);
            ImGui.TextUnformatted(item.SourceKind == PresetSourceKind.External ? loc.T("External") : loc.T("Local"));

            ImGui.TableSetColumnIndex(4);
            ImGui.TextUnformatted(Path.GetFileName(item.FilePath));

            ImGui.TableSetColumnIndex(5);
            var disabled = !gposeStateService.IsInGpose || !item.IsValid;
            ImGui.BeginDisabled(disabled);
            if (ImGui.Button($"{loc.T("Load")}##LoadPreset::{Path.GetFileName(item.FilePath)}"))
                OpenLoadPopup(item);
            ImGui.EndDisabled();

            if (ImGui.BeginPopupContextItem($"LoadPresetContext::{item.FilePath}"))
            {
                if (ImGui.MenuItem(loc.T("Delete")))
                    OpenDeletePopup(item);

                ImGui.EndPopup();
            }
        }

        ImGui.EndTable();
    }

    private void DrawAreaFilter()
    {
        var options = storageService.Presets
            .Select(GetPresetArea)
            .Where(area => !string.IsNullOrWhiteSpace(area))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(area => area, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selectedArea = configuration.SelectedAreaFilter;
        if (!string.IsNullOrWhiteSpace(selectedArea) && !options.Contains(selectedArea, StringComparer.Ordinal))
        {
            selectedArea = string.Empty;
            configuration.SelectedAreaFilter = string.Empty;
            configuration.Save();
        }

        var selectedText = string.IsNullOrWhiteSpace(selectedArea) ? loc.T("AllAreas") : selectedArea;
        if (ImGui.BeginCombo(loc.T("AreaFilter"), selectedText))
        {
            if (ImGui.Selectable(loc.T("AllAreas"), string.IsNullOrWhiteSpace(selectedArea)))
            {
                if (configuration.SelectedAreaFilter != string.Empty)
                {
                    configuration.SelectedAreaFilter = string.Empty;
                    configuration.Save();
                }
            }

            foreach (var option in options)
            {
                var isSelected = string.Equals(selectedArea, option, StringComparison.Ordinal);
                if (ImGui.Selectable(option, isSelected))
                {
                    configuration.SelectedAreaFilter = option;
                    configuration.Save();
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawSettingsTab()
    {
        DrawLanguage();
        ImGui.Separator();
        DrawPresetFolders();
    }

    private void DrawLanguage()
    {
        ImGui.TextUnformatted(loc.T("Language"));
        ImGui.SameLine();
        var current = loc.Language == "zh" ? loc.T("Chinese") : loc.T("English");
        if (!ImGui.BeginCombo($"{loc.T("Language")}##Language", current))
            return;

        if (ImGui.Selectable(loc.T("English"), loc.Language == "en"))
            loc.Language = "en";

        if (ImGui.Selectable(loc.T("Chinese"), loc.Language == "zh"))
            loc.Language = "zh";

        ImGui.EndCombo();
    }

    private void DrawPresetFolders()
    {
        ImGui.TextUnformatted($"{loc.T("PresetFolder")}:");
        ImGui.TextDisabled(storageService.LocalPresetFolder);
        ImGui.SameLine();
        if (ImGui.Button($"{loc.T("OpenPresetFolder")}##OpenLocalPresetFolder"))
            TryOpenFolder(storageService.LocalPresetFolder);

        ImGui.Separator();
        ImGui.TextUnformatted($"{loc.T("ExternalFolder")}:");
        ImGui.SetNextItemWidth(-180);
        if (ImGui.InputText("##ExternalFolder", ref externalFolderInput, 512))
            configuration.ExternalPresetFolder = externalFolderInput;

        if (ImGui.Button(loc.T("Scan")))
        {
            configuration.ExternalPresetFolder = externalFolderInput;
            configuration.Save();
            storageService.Reload(externalFolderInput);
        }

        ImGui.SameLine();
        if (ImGui.Button(loc.T("ClearExternal")))
        {
            externalFolderInput = string.Empty;
            configuration.ExternalPresetFolder = string.Empty;
            configuration.Save();
            storageService.ClearExternal();
        }
    }

    private void DrawLoadModal()
    {
        if (openLoadPopupRequested)
        {
            ImGui.OpenPopup(LoadPopupId);
            openLoadPopupRequested = false;
        }

        if (presetService.PendingLoadPreset == null)
            return;

        ImGui.SetNextWindowPos(loadPopupPosition, ImGuiCond.Always);
        if (!ImGui.BeginPopupModal(LoadPopupId, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        var item = presetService.PendingLoadPreset;
        ImGui.TextUnformatted(loc.T("ConfirmLoad"));
        ImGui.TextDisabled(GetPresetNote(item));
        ImGui.TextDisabled($"{loc.T("Area")}: {GetPresetArea(item)}");
        ImGui.Separator();

        if (ImGui.Button($"{loc.T("Confirm")}##ConfirmLoadPreset"))
        {
            if (presetService.ConfirmLoadPreset())
                ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button($"{loc.T("Cancel")}##CancelLoadPreset"))
            ImGui.CloseCurrentPopup();

        if (!string.IsNullOrWhiteSpace(presetService.LastError))
            ImGui.TextWrapped(presetService.LastError);

        ImGui.EndPopup();
    }

    private void DrawDeleteModal()
    {
        if (openDeletePopupRequested)
        {
            ImGui.OpenPopup(DeletePopupId);
            openDeletePopupRequested = false;
        }

        if (presetService.PendingDeletePreset == null)
            return;

        ImGui.SetNextWindowPos(deletePopupPosition, ImGuiCond.Always);
        if (!ImGui.BeginPopupModal(DeletePopupId, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        var item = presetService.PendingDeletePreset;
        ImGui.TextUnformatted(loc.T("ConfirmDelete"));
        ImGui.TextDisabled(GetPresetNote(item));
        ImGui.TextDisabled(GetPresetArea(item));
        ImGui.Separator();

        if (ImGui.Button($"{loc.T("Confirm")}##ConfirmDeletePreset"))
        {
            if (presetService.ConfirmDeletePreset())
                ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button($"{loc.T("Cancel")}##CancelDeletePreset"))
            ImGui.CloseCurrentPopup();

        if (!string.IsNullOrWhiteSpace(presetService.LastError))
            ImGui.TextWrapped(presetService.LastError);

        ImGui.EndPopup();
    }

    private void OpenLoadPopup(CameraPresetListItem item)
    {
        presetService.RequestLoadPreset(item);
        loadPopupPosition = ImGui.GetMousePos() + new Vector2(12, 12);
        openLoadPopupRequested = true;
    }

    private void OpenDeletePopup(CameraPresetListItem item)
    {
        presetService.RequestDeletePreset(item);
        deletePopupPosition = ImGui.GetMousePos() + new Vector2(12, 12);
        openDeletePopupRequested = true;
    }

    private void SaveLayout()
    {
        if (ImGui.IsWindowAppearing())
            return;

        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        configuration.PanelPositionX = pos.X;
        configuration.PanelPositionY = pos.Y;
        configuration.PanelWidth = size.X;
        configuration.PanelHeight = size.Y;
    }

    public static bool TryOpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return false;

            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string BuildStatusMessage()
    {
        if (!string.IsNullOrWhiteSpace(presetService.LastError))
            return presetService.LastError;

        if (string.IsNullOrWhiteSpace(presetService.LastMessageKey))
            return loc.T("NoRecentResult");

        var text = loc.T(presetService.LastMessageKey);
        return string.IsNullOrWhiteSpace(presetService.LastMessageDetail) ? text : $"{text}: {presetService.LastMessageDetail}";
    }

    private System.Collections.Generic.IReadOnlyList<CameraPresetListItem> GetFilteredPresetItems()
    {
        var items = storageService.Presets
            .OrderByDescending(item => item.Preset.CreatedAtUtc)
            .AsEnumerable();

        var selectedArea = configuration.SelectedAreaFilter;
        if (!string.IsNullOrWhiteSpace(selectedArea))
            items = items.Where(item => string.Equals(GetPresetArea(item), selectedArea, StringComparison.Ordinal));

        return items.ToList();
    }

    private static string GetPresetNote(CameraPresetListItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Preset.Note))
            return item.Preset.Note;

        if (!string.IsNullOrWhiteSpace(item.Preset.Name))
            return item.Preset.Name;

        return string.Empty;
    }

    private static string GetPresetArea(CameraPresetListItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Preset.TerritoryName))
            return item.Preset.TerritoryName;

        if (item.Preset.ActualTerritoryType != 0 && !string.IsNullOrWhiteSpace(item.Preset.ActualTerritoryName))
            return item.Preset.ActualTerritoryName;

        return item.Preset.TerritoryType == 0 ? "Unknown" : $"TerritoryType: {item.Preset.TerritoryType}";
    }
}
