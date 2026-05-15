namespace GposeCameraSaver.Models;

public sealed class CameraPresetListItem
{
    public CameraPresetFile Preset { get; set; } = new();
    public string FilePath { get; set; } = string.Empty;
    public PresetSourceKind SourceKind { get; set; }
    public bool IsValid { get; set; }
    public string ValidationMessage { get; set; } = string.Empty;
}
