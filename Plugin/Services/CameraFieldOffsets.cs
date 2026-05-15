using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace GposeCameraSaver.Services;

public sealed class CameraFieldOffsets
{
    private readonly Dictionary<string, string> offsets = new();

    public CameraFieldOffsets()
    {
        ResolveTypedOffset<Camera>("Distance");
        ResolveTypedOffset<Camera>("InterpDistance");
        ResolveTypedOffset<Camera>("DirH");
        ResolveTypedOffset<Camera>("DirV");
        ResolveTypedOffset<Camera>("FoV");
        ResolveTypedOffset<Camera>("TiltOffset");
        ResolveTypedOffset<Camera>("LastPosition");
        ResolveTypedOffset<Camera>("LastLookAtVector");
        offsets["Pan"] = "offset unresolved in current typed Camera struct";
        offsets["Roll"] = "offset unresolved in current typed Camera struct";
    }

    public IReadOnlyDictionary<string, string> Offsets => offsets;

    private void ResolveTypedOffset<T>(string fieldName)
    {
        try
        {
            offsets[fieldName] = $"0x{Marshal.OffsetOf<T>(fieldName).ToInt64():X}";
        }
        catch (Exception ex)
        {
            offsets[fieldName] = $"offset unresolved: {ex.Message}";
        }
    }
}
