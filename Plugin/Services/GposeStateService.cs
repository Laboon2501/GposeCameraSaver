using Dalamud.Plugin.Services;
using GposeCameraSaver.Models;

namespace GposeCameraSaver.Services;

public sealed class GposeStateService
{
    private readonly IClientState clientState;
    private readonly CameraAccessor cameraAccessor;

    public GposeStateService(IClientState clientState, CameraAccessor cameraAccessor)
    {
        this.clientState = clientState;
        this.cameraAccessor = cameraAccessor;
        IsInGpose = clientState.IsGPosing;
    }

    public bool IsInGpose { get; private set; }

    public CameraSnapshot? EntrySnapshot { get; private set; }

    public bool CanRestoreEntrySnapshot => IsInGpose && EntrySnapshot != null;

    public string LastStateError { get; private set; } = string.Empty;

    public bool Update(out bool enteredGpose, out bool exitedGpose)
    {
        enteredGpose = false;
        exitedGpose = false;

        var current = clientState.IsGPosing;
        if (current == IsInGpose)
            return false;

        IsInGpose = current;
        if (current)
        {
            enteredGpose = true;
            if (cameraAccessor.TryCaptureCurrentCamera(out var snapshot, out var error))
            {
                EntrySnapshot = snapshot;
                LastStateError = string.Empty;
            }
            else
            {
                EntrySnapshot = null;
                LastStateError = error;
            }
        }
        else
        {
            exitedGpose = true;
            EntrySnapshot = null;
        }

        return true;
    }
}
