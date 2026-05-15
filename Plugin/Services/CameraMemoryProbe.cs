using System.Collections.Generic;

namespace GposeCameraSaver.Services;

public sealed class CameraMemoryProbe
{
    public List<string> CandidateDescriptions { get; } = new();

    public string SelectedSource { get; set; } = string.Empty;

    public string SelectedPointer { get; set; } = string.Empty;

    public string FailureReason { get; set; } = string.Empty;
}
