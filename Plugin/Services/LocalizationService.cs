using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace GposeCameraSaver.Services;

public sealed class LocalizationService
{
    private readonly Configuration configuration;

    private static readonly Dictionary<string, (string En, string Zh)> Strings = new(StringComparer.Ordinal)
    {
        ["PluginName"] = ("Gpose Camera Saver", "\u76f8\u673a\u9884\u8bbe"),
        ["CameraPresets"] = ("Camera Presets", "\u76f8\u673a\u9884\u8bbe"),
        ["Settings"] = ("Settings", "\u8bbe\u7f6e"),
        ["Language"] = ("Language", "\u8bed\u8a00"),
        ["English"] = ("English", "English"),
        ["Chinese"] = ("Chinese", "\u4e2d\u6587"),
        ["PresetFolder"] = ("Preset Folder", "\u9884\u8bbe\u76ee\u5f55"),
        ["OpenPresetFolder"] = ("Open Preset Folder", "\u6253\u5f00\u9884\u8bbe\u76ee\u5f55"),
        ["ExternalFolder"] = ("External Preset Folder", "\u5916\u90e8\u9884\u8bbe\u76ee\u5f55"),
        ["Scan"] = ("Scan", "\u626b\u63cf"),
        ["ClearExternal"] = ("Clear", "\u6e05\u7a7a"),
        ["AreaFilter"] = ("Area Filter", "\u533a\u57df\u7b5b\u9009"),
        ["AllAreas"] = ("All Areas", "\u5168\u90e8\u533a\u57df"),
        ["CreatedTime"] = ("Created Time", "\u4fdd\u5b58\u65f6\u95f4"),
        ["Note"] = ("Note", "\u5907\u6ce8"),
        ["Area"] = ("Area", "\u533a\u57df"),
        ["Source"] = ("Source", "\u6765\u6e90"),
        ["File"] = ("File", "\u6587\u4ef6\u540d"),
        ["Actions"] = ("Actions", "\u64cd\u4f5c"),
        ["Load"] = ("Load", "\u8bfb\u53d6"),
        ["Delete"] = ("Delete", "\u5220\u9664"),
        ["Cancel"] = ("Cancel", "\u53d6\u6d88"),
        ["Confirm"] = ("Confirm", "\u786e\u8ba4"),
        ["ConfirmLoad"] = ("Confirm load?", "\u786e\u8ba4\u8bfb\u53d6\uff1f"),
        ["ConfirmDelete"] = ("Confirm delete?", "\u786e\u8ba4\u5220\u9664\uff1f"),
        ["SaveCurrentCamera"] = ("Save Current Camera", "\u4fdd\u5b58\u5f53\u524d\u955c\u5934"),
        ["Presets"] = ("Presets", "\u9884\u8bbe"),
        ["Local"] = ("Local", "\u672c\u5730"),
        ["External"] = ("External", "\u5916\u90e8"),
        ["ActualTerritory"] = ("Current Territory", "\u5f53\u524d\u5730\u56fe/\u533a\u57df"),
        ["GposeActive"] = ("GPose: Active", "GPose: \u6b63\u5728\u4f7f\u7528"),
        ["GposeInactive"] = ("GPose: Inactive", "GPose: \u672a\u4f7f\u7528"),
        ["Loaded"] = ("Loaded", "\u5df2\u52a0\u8f7d"),
        ["Saved"] = ("Saved", "\u5df2\u4fdd\u5b58"),
        ["Deleted"] = ("Deleted", "\u5df2\u5220\u9664"),
        ["SaveFailed"] = ("Save failed", "\u4fdd\u5b58\u5931\u8d25"),
        ["LoadFailed"] = ("Load failed", "\u8bfb\u53d6\u5931\u8d25"),
        ["DeleteFailed"] = ("Delete failed", "\u5220\u9664\u5931\u8d25"),
        ["NoRecentResult"] = ("No recent result.", "\u6682\u65e0\u7ed3\u679c"),
        ["PleaseEnterGpose"] = (
            "Please enter GPose before saving or loading camera presets.",
            "\u8bf7\u5148\u8fdb\u5165 GPose \u540e\u518d\u4fdd\u5b58\u6216\u8005\u8bfb\u53d6\u955c\u5934\u9884\u8bbe\u3002"
        ),
    };

    public LocalizationService(Configuration configuration, IClientState clientState)
    {
        this.configuration = configuration;
        if (!IsSupported(configuration.UiLanguage))
        {
            configuration.UiLanguage = InferLanguage(clientState);
            configuration.Save();
        }
    }

    public string Language
    {
        get => IsSupported(configuration.UiLanguage) ? configuration.UiLanguage : "en";
        set
        {
            var normalized = IsSupported(value) ? value : "en";
            if (configuration.UiLanguage == normalized)
                return;

            configuration.UiLanguage = normalized;
            configuration.Save();
        }
    }

    public string T(string key) =>
        Strings.TryGetValue(key, out var value) && Language == "zh" ? value.Zh : Strings.TryGetValue(key, out value) ? value.En : key;

    private static bool IsSupported(string language) => string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);

    private static string InferLanguage(IClientState clientState)
    {
        try
        {
            var value = clientState.GetType().GetProperty("ClientLanguage")?.GetValue(clientState)?.ToString() ?? string.Empty;
            return value.Contains("Chinese", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("chs", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("cht", StringComparison.OrdinalIgnoreCase)
                ? "zh"
                : "en";
        }
        catch
        {
            return "en";
        }
    }
}
