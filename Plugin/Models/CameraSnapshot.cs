using System.Collections.Generic;

namespace GposeCameraSaver.Models;

public sealed class CameraSnapshot
{
    public int? Mode { get; set; }
    public float? Distance { get; set; }
    public float? HRotation { get; set; }
    public float? VRotation { get; set; }
    public float? FoV { get; set; }
    public float? AddedFoV { get; set; }
    public float? Pan { get; set; }
    public float? Tilt { get; set; }
    public float? Roll { get; set; }
    public SerializableVector3? LastPosition { get; set; }
    public SerializableVector3? LastLookAtVector { get; set; }
    public SerializableVector3? ScenePosition { get; set; }
    public SerializableVector3? RenderOrigin { get; set; }
    public Dictionary<string, object> Extra { get; set; } = new();
}
