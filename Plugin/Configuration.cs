using Dalamud.Configuration;
using System;

namespace GposeCameraSaver;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool AutoOpenOnEnterGpose { get; set; } = true;
    public bool AutoCloseOnExitGpose { get; set; } = false;
    public string UiLanguage { get; set; } = string.Empty;
    public string PresetFolder { get; set; } = string.Empty;
    public string ExternalPresetFolder { get; set; } = string.Empty;
    public string SelectedAreaFilter { get; set; } = string.Empty;

    public float PanelPositionX { get; set; } = 100f;
    public float PanelPositionY { get; set; } = 100f;
    public float PanelWidth { get; set; } = 760f;
    public float PanelHeight { get; set; } = 520f;

    // Kept for older compatibility with older presets/configurations.
    [Obsolete("Legacy debug flag retained for compatibility with older serialized configs. Not used by the plugin anymore.")]
    public bool ShowDebug { get; set; } = false;

    [Obsolete("Legacy debug flag retained for compatibility with older serialized configs. Not used by the plugin anymore.")]
    public bool HideDebugPanel { get; set; } = false;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
