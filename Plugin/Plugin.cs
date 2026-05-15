using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.IO;
using GposeCameraSaver.Services;
using GposeCameraSaver.Windows;

namespace GposeCameraSaver;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/gcs";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;

    public Configuration Configuration { get; }
    public WindowSystem WindowSystem { get; } = new("GposeCameraSaver");

    private readonly CameraAccessor cameraAccessor;
    private readonly GposeStateService gposeStateService;
    private readonly PresetStorageService presetStorageService;
    private readonly TerritoryNameService territoryNameService;
    private readonly CameraPresetService cameraPresetService;
    private readonly LocalizationService localizationService;
    private readonly CameraPanelWindow cameraPanelWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        cameraAccessor = new CameraAccessor(ClientState, Configuration);
        gposeStateService = new GposeStateService(ClientState, cameraAccessor);
        if (string.IsNullOrWhiteSpace(Configuration.PresetFolder))
        {
            Configuration.PresetFolder = Path.Combine(PluginInterface.GetPluginConfigDirectory(), "Presets");
            Configuration.Save();
        }

        presetStorageService = new PresetStorageService(PluginLog, cameraAccessor, Configuration.PresetFolder);
        territoryNameService = new TerritoryNameService(ClientState, DataManager);
        localizationService = new LocalizationService(Configuration, ClientState);
        cameraPresetService = new CameraPresetService(cameraAccessor, gposeStateService, presetStorageService, territoryNameService);

        presetStorageService.Reload(Configuration.ExternalPresetFolder);

        cameraPanelWindow = new CameraPanelWindow(Configuration, cameraPresetService, presetStorageService, gposeStateService, territoryNameService, localizationService);
        WindowSystem.AddWindow(cameraPanelWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Gpose Camera Saver window. /gcs settings to open settings tab.",
        });

        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.DisableGposeUiHide = true;
    }

    public void Dispose()
    {
        Configuration.Save();
        CommandManager.RemoveHandler(CommandName);
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        WindowSystem.RemoveAllWindows();
        cameraPanelWindow.Dispose();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        cameraAccessor.FrameworkUpdate();

        if (!gposeStateService.Update(out var enteredGpose, out var exitedGpose))
            return;

        if (enteredGpose && Configuration.AutoOpenOnEnterGpose)
            cameraPanelWindow.IsOpen = true;
        else if (exitedGpose && Configuration.AutoCloseOnExitGpose)
            cameraPanelWindow.IsOpen = false;
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        var parts = trimmed.Split(' ', 2, System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        var subcommand = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;

        switch (subcommand)
        {
            case "":
            case "open":
                cameraPanelWindow.Toggle();
                break;
            case "config":
            case "settings":
                cameraPanelWindow.OpenSettingsTab();
                break;
            default:
                cameraPanelWindow.Toggle();
                break;
        }
    }

    public void ToggleMainUi() => cameraPanelWindow.Toggle();

    public void ToggleConfigUi() => cameraPanelWindow.OpenSettingsTab();
}
