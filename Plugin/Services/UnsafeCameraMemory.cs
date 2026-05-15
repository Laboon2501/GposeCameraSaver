using FFXIVClientStructs.FFXIV.Client.Game;
using GposeCameraSaver.Models;

namespace GposeCameraSaver.Services;

public static unsafe class UnsafeCameraMemory
{
    public static bool LooksUsable(Camera* camera)
    {
        return camera != null &&
               camera->Distance is >= 0f and <= 500f &&
               camera->FoV is > 0.01f and < 10f &&
               !float.IsNaN(camera->DirH) &&
               !float.IsNaN(camera->DirV);
    }

    public static string PointerString(Camera* camera)
    {
        return camera == null ? "0x0" : $"0x{((nint)camera).ToString("X")}";
    }

    public static CameraSnapshot Capture(Camera* camera)
    {
        var snapshot = new CameraSnapshot
        {
            Mode = (int)camera->ControlMode,
            Distance = camera->Distance,
            HRotation = camera->DirH,
            VRotation = camera->DirV,
            FoV = camera->FoV,
            AddedFoV = camera->SceneCamera.RenderCamera != null ? camera->SceneCamera.RenderCamera->FoV_2 : null,
            Tilt = camera->TiltOffset,
            LastPosition = new SerializableVector3(camera->LastPosition.X, camera->LastPosition.Y, camera->LastPosition.Z),
            LastLookAtVector = new SerializableVector3(camera->LastLookAtVector.X, camera->LastLookAtVector.Y, camera->LastLookAtVector.Z),
            ScenePosition = new SerializableVector3(camera->SceneCamera.Position.X, camera->SceneCamera.Position.Y, camera->SceneCamera.Position.Z),
        };

        if (camera->SceneCamera.RenderCamera != null)
            snapshot.RenderOrigin = new SerializableVector3(camera->SceneCamera.RenderCamera->Origin.X, camera->SceneCamera.RenderCamera->Origin.Y, camera->SceneCamera.RenderCamera->Origin.Z);

        return snapshot;
    }
}
