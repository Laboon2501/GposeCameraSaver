namespace GposeCameraSaver.Models;

public sealed class CameraApplyDebugRow
{
    public string Field { get; set; } = string.Empty;
    public string Before { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
    public string NextFrame { get; set; } = string.Empty;
    public string Delta { get; set; } = string.Empty;
}
