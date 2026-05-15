using System;
using System.Collections.Generic;

namespace GposeCameraSaver.Models;

public sealed class CameraPresetFile
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedAtLocal { get; set; } = string.Empty;
    public uint TerritoryType { get; set; }
    public string TerritoryName { get; set; } = string.Empty;
    public uint MapId { get; set; }
    public uint ActualTerritoryType { get; set; }
    public uint ActualMapId { get; set; }
    public string ActualTerritoryName { get; set; } = string.Empty;
    public List<string> CapturedFields { get; set; } = new();
    public CameraSnapshot Camera { get; set; } = new();
}
