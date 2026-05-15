using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using GposeCameraSaver.Models;

namespace GposeCameraSaver.Services;

public sealed unsafe class CameraAccessor
{
    private const int GameCameraSlotCount = 4;
    private const float RotationTolerance = 0.0005f;
    private const float DistanceTolerance = 0.01f;
    private const float FovTolerance = 0.0005f;
    private const float TwoPi = MathF.PI * 2f;

    private static readonly string[] CapturedFields =
    [
        "Mode",
        "Distance",
        "HRotation",
        "VRotation",
        "FoV",
        "AddedFoV",
        "Tilt",
        "LastPosition",
        "LastLookAtVector",
        "ScenePosition",
        "RenderOrigin",
    ];

    private static readonly string[] MissingFields =
    [
        "Pan",
        "Roll",
    ];

    private readonly IClientState clientState;
    private readonly Configuration configuration;
    private readonly CameraFieldOffsets fieldOffsets = new();
    private PendingApplyJob? pendingApplyJob;

    public CameraAccessor(IClientState clientState, Configuration configuration)
    {
        this.clientState = clientState;
        this.configuration = configuration;
    }

    public CameraSnapshot? LastApplyBeforeSnapshot { get; private set; }

    public CameraSnapshot? LastApplyTargetSnapshot { get; private set; }

    public CameraSnapshot? LastApplyAfterSnapshot { get; private set; }

    public CameraSnapshot? LastApplyNextFrameSnapshot { get; private set; }

    public string LastApplyBefore { get; private set; } = "n/a";

    public string LastApplyTarget { get; private set; } = "n/a";

    public string LastApplyAfter { get; private set; } = "n/a";

    public string LastApplyNextFrame { get; private set; } = "n/a";

    public string LastApplyResult { get; private set; } = string.Empty;

    public string LastApplyError { get; private set; } = string.Empty;

    public string LastApplyFrame0Result { get; private set; } = string.Empty;

    public string LastApplyFrame1Result { get; private set; } = string.Empty;

    public string LastApplyFrame2Result { get; private set; } = string.Empty;

    public int LastApplyFrameCount { get; private set; }

    public string LastCandidateApplyResults { get; private set; } = string.Empty;

    public string LastResolvedCameraSource { get; private set; } = "not resolved";

    public string LastResolvedCameraPointer { get; private set; } = "0x0";

    public string LastResolvedCameraCandidates { get; private set; } = "not probed";

    public IReadOnlyList<string> GetCapturedFieldNames() => CapturedFields;

    public IReadOnlyList<string> GetMissingFieldNames() => MissingFields;

    public IReadOnlyDictionary<string, string> GetFieldOffsetDiagnostics() => fieldOffsets.Offsets;

    public bool CanAccessCamera() => EnumerateCameraCandidates(false, null, out _, out _);

    public bool ResolveActiveGposeCamera(out IntPtr pointer, out string source, out string error)
    {
        pointer = IntPtr.Zero;
        source = string.Empty;

        if (!EnumerateCameraCandidates(true, null, out var candidates, out error))
            return false;

        var selected = candidates[0];
        pointer = (IntPtr)selected.Pointer;
        source = selected.Source;
        error = string.Empty;
        return true;
    }

    public bool TryCaptureCurrentCamera(out CameraSnapshot snapshot, out string error)
    {
        snapshot = new CameraSnapshot();
        if (!EnumerateCameraCandidates(false, null, out var candidates, out error))
            return false;

        try
        {
            var selected = candidates[0];
            snapshot = UnsafeCameraMemory.Capture(selected.Pointer);
            snapshot.Extra["CameraSource"] = selected.Source;
            snapshot.Extra["CameraPointer"] = UnsafeCameraMemory.PointerString(selected.Pointer);
            snapshot.Extra["CameraSlot"] = selected.Slot;
            snapshot.Extra["CandidateCount"] = candidates.Count;
            snapshot.Extra["IsGPosing"] = clientState.IsGPosing;
            snapshot.Extra["TerritoryType"] = clientState.TerritoryType;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Camera capture failed: {ex.Message}";
            return false;
        }
    }

    public bool TryApplyCamera(CameraSnapshot snapshot, out string error)
    {
        return TryStartApply(snapshot, null, "Load preset", out error);
    }

    public bool TryApplyCameraToSlot(CameraSnapshot snapshot, int slot, out string error)
    {
        return TryStartApply(snapshot, slot, $"Load preset to slot {slot}", out error);
    }

    public bool TestNudgeCamera(out string error)
    {
        return TryNudge(null, out error);
    }

    public bool TestNudgeCameraSlot(int slot, out string error)
    {
        return TryNudge(slot, out error);
    }

    public void FrameworkUpdate()
    {
        if (pendingApplyJob == null)
            return;

        if (!clientState.IsGPosing && pendingApplyJob.RequireGpose)
        {
            LastApplyError = "Pending camera apply was cancelled because GPose ended.";
            LastApplyResult = LastApplyError;
            pendingApplyJob = null;
            return;
        }

        if (pendingApplyJob.Phase == 1)
        {
            LastApplyFrameCount = 1;
            if (!EnumerateCameraCandidates(pendingApplyJob.RequireGpose, pendingApplyJob.ForcedSlot, out var candidates, out var error))
            {
                LastApplyFrame1Result = $"Frame1 failed before write: {error}";
                LastApplyError = LastApplyFrame1Result;
                LastApplyResult = LastApplyError;
                pendingApplyJob = null;
                return;
            }

            var attempt = RunApplyAttempt(candidates, pendingApplyJob.Target, "Frame1", true);
            LastApplyFrame1Result = attempt.Summary;
            pendingApplyJob.Phase = 2;
            return;
        }

        LastApplyFrameCount = 2;
        var target = pendingApplyJob.Target;
        var forcedSlot = pendingApplyJob.ForcedSlot;
        pendingApplyJob = null;

        if (!EnumerateCameraCandidates(true, forcedSlot, out var verifyCandidates, out var verifyError))
        {
            LastApplyNextFrame = "unavailable";
            LastApplyFrame2Result = $"Frame2 verify failed: {verifyError}";
            LastApplyError = LastApplyFrame2Result;
            LastApplyResult = LastApplyError;
            return;
        }

        var verify = CaptureBestMatch(verifyCandidates, target);
        LastApplyNextFrameSnapshot = verify.After;
        LastApplyNextFrame = FormatDebugSnapshot(verify.After);
        LastApplyFrame2Result = verify.Summary;

        if (!verify.MatchesTarget)
        {
            LastApplyError = $"Camera write did not stick after frame2. Probably using a different active camera slot or missing offsets. {verify.Mismatch}";
            LastApplyResult = LastApplyError;
            return;
        }

        LastApplyError = string.Empty;
        LastApplyResult = $"Camera write stuck after frame2 on {verify.Candidate.Source}.";
    }

    public bool ValidateSnapshot(CameraSnapshot? snapshot, out string error)
    {
        if (snapshot == null)
        {
            error = "Preset has no camera payload.";
            return false;
        }

        if (!IsGood(snapshot.Distance, 0f, 500f) ||
            !IsGood(snapshot.HRotation, -1000f, 1000f) ||
            !IsGood(snapshot.VRotation, -1000f, 1000f) ||
            !IsGood(snapshot.FoV, 0.01f, 10f))
        {
            error = "Preset is missing required camera fields or contains out-of-range values.";
            return false;
        }

        if (!IsOptionalGood(snapshot.Tilt, -1000f, 1000f) ||
            !IsOptionalGood(snapshot.AddedFoV, -10f, 10f) ||
            !IsOptionalGood(snapshot.Pan, -1000f, 1000f) ||
            !IsOptionalGood(snapshot.Roll, -1000f, 1000f))
        {
            error = "Preset contains NaN, Infinity, or an out-of-range optional value.";
            return false;
        }

        if (!IsOptionalVectorGood(snapshot.LastPosition, -100000f, 100000f) ||
            !IsOptionalVectorGood(snapshot.LastLookAtVector, -100000f, 100000f) ||
            !IsOptionalVectorGood(snapshot.ScenePosition, -100000f, 100000f) ||
            !IsOptionalVectorGood(snapshot.RenderOrigin, -100000f, 100000f))
        {
            error = "Preset contains an out-of-range camera vector.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public IReadOnlyList<CameraApplyDebugRow> GetApplyDebugRows()
    {
        return
        [
            BuildFloatRow("Distance", static snapshot => snapshot.Distance, DistanceTolerance, false),
            BuildFloatRow("HRotation", static snapshot => snapshot.HRotation, RotationTolerance, true),
            BuildFloatRow("VRotation", static snapshot => snapshot.VRotation, RotationTolerance, true),
            BuildFloatRow("FoV", static snapshot => snapshot.FoV, FovTolerance, false),
            BuildFloatRow("AddedFoV", static snapshot => snapshot.AddedFoV, FovTolerance, false),
            BuildFloatRow("Tilt", static snapshot => snapshot.Tilt, RotationTolerance, false),
            BuildVectorRow("LastLookAtVector", static snapshot => snapshot.LastLookAtVector),
            BuildVectorRow("ScenePosition", static snapshot => snapshot.ScenePosition),
            BuildVectorRow("RenderOrigin", static snapshot => snapshot.RenderOrigin),
        ];
    }

    private bool TryStartApply(CameraSnapshot snapshot, int? forcedSlot, string action, out string error)
    {
        LastApplyError = string.Empty;
        ResetApplyFrameDiagnostics();

        if (!clientState.IsGPosing)
        {
            error = "Please enter GPose before applying a camera preset.";
            SetApplyFailure(error);
            return false;
        }

        if (!ValidateSnapshot(snapshot, out error))
        {
            SetApplyFailure(error);
            return false;
        }

        if (!EnumerateCameraCandidates(true, forcedSlot, out var candidates, out error))
        {
            SetApplyFailure(error);
            return false;
        }

        LastApplyTargetSnapshot = Clone(snapshot);
        LastApplyTarget = FormatDebugSnapshot(snapshot);
        LastApplyNextFrameSnapshot = null;
        LastApplyNextFrame = "pending";

        var attempt = RunApplyAttempt(candidates, snapshot, "Frame0", true);
        LastApplyFrame0Result = attempt.Summary;
        LastApplyFrameCount = 0;

        pendingApplyJob = new PendingApplyJob(Clone(snapshot), forcedSlot, requireGpose: true);
        if (attempt.MatchesTarget)
        {
            LastApplyError = string.Empty;
            LastApplyResult = $"{action}: frame0 matched {attempt.Candidate.Source}; frame1 reinforcement and frame2 verify scheduled.";
        }
        else
        {
            LastApplyError = $"{action}: frame0 did not fully match; frame1 reinforcement and frame2 verify scheduled. {attempt.Mismatch}";
            LastApplyResult = LastApplyError;
        }

        error = string.Empty;
        return true;
    }

    private bool TryNudge(int? forcedSlot, out string error)
    {
        if (!clientState.IsGPosing)
        {
            error = "Please enter GPose before nudging the camera.";
            SetApplyFailure(error);
            return false;
        }

        if (!EnumerateCameraCandidates(true, forcedSlot, out var candidates, out error))
        {
            SetApplyFailure(error);
            return false;
        }

        var before = UnsafeCameraMemory.Capture(candidates[0].Pointer);
        var target = Clone(before);
        if (target.HRotation is { } h)
            target.HRotation = h + 0.25f;
        else if (target.Distance is { } distance)
            target.Distance = Math.Min(distance + 1f, 500f);
        else
            target.Distance = 6f;

        return TryStartApply(target, forcedSlot, forcedSlot == null ? "Test nudge" : $"Test nudge slot {forcedSlot.Value}", out error);
    }

    private bool EnumerateCameraCandidates(bool requireGpose, int? forcedSlot, out List<CameraCandidate> candidates, out string error)
    {
        candidates = new List<CameraCandidate>();
        var descriptions = new List<string>();

        if (requireGpose && !clientState.IsGPosing)
        {
            error = "Not in GPose; refusing to resolve a writable GPose camera.";
            LastResolvedCameraSource = "not in GPose";
            LastResolvedCameraPointer = "0x0";
            LastResolvedCameraCandidates = error;
            return false;
        }

        var manager = CameraManager.Instance();
        if (manager == null)
        {
            error = "CameraManager.Instance() returned null.";
            LastResolvedCameraSource = "CameraManager unavailable";
            LastResolvedCameraPointer = "0x0";
            LastResolvedCameraCandidates = error;
            return false;
        }

        descriptions.Add("Typed ActiveCamera/CurrentCamera member: not exposed by this FFXIVClientStructs build.");
        for (var slot = 0; slot < GameCameraSlotCount; slot++)
        {
            var camera = (Camera*)manager->Cameras[slot];
            if (camera == null)
            {
                descriptions.Add($"slot {slot}: null");
                continue;
            }

            var pointer = UnsafeCameraMemory.PointerString(camera);
            if (!UnsafeCameraMemory.LooksUsable(camera))
            {
                descriptions.Add($"slot {slot}: {pointer}, rejected (distance={F(camera->Distance)}, h={F(camera->DirH)}, v={F(camera->DirV)}, fov={F(camera->FoV)})");
                continue;
            }

            var source = SlotName(slot);
            var hasRenderCamera = camera->SceneCamera.RenderCamera != null;
            descriptions.Add($"slot {slot}: {pointer}, {source}, distance={F(camera->Distance)}, h={F(camera->DirH)}, v={F(camera->DirV)}, fov={F(camera->FoV)}, renderCamera={(hasRenderCamera ? "yes" : "no")}");
            candidates.Add(new CameraCandidate(slot, source, camera, hasRenderCamera));
        }

        LastResolvedCameraCandidates = string.Join("\n", descriptions);

        if (forcedSlot is { } slotFilter)
        {
            candidates = candidates.Where(candidate => candidate.Slot == slotFilter).ToList();
            if (candidates.Count == 0)
            {
                error = $"Preferred/forced camera slot {slotFilter} is not available or not writable.";
                LastResolvedCameraSource = $"slot {slotFilter} unavailable";
                LastResolvedCameraPointer = "0x0";
                return false;
            }
        }
        else
        {
            var selected = candidates.FirstOrDefault(candidate => candidate.Slot == 0);
            if (selected.Pointer != null)
            {
                candidates.RemoveAll(candidate => candidate.Slot == selected.Slot);
                candidates.Insert(0, selected);
            }
        }

        if (candidates.Count == 0)
        {
            error = "No writable typed GameCamera candidates were found in CameraManager.Cameras[0..3].";
            LastResolvedCameraSource = "none";
            LastResolvedCameraPointer = "0x0";
            return false;
        }

        var primary = candidates[0];
        LastResolvedCameraSource = $"{primary.Source} (primary; writes mirror to {candidates.Count} candidate(s))";
        LastResolvedCameraPointer = UnsafeCameraMemory.PointerString(primary.Pointer);
        error = string.Empty;
        return true;
    }

    private ApplyAttemptResult RunApplyAttempt(IReadOnlyList<CameraCandidate> candidates, CameraSnapshot target, string frameLabel, bool updateSnapshots)
    {
        var candidateResults = new List<string>();
        CandidateApplyResult? best = null;

        foreach (var candidate in candidates)
        {
            var before = UnsafeCameraMemory.Capture(candidate.Pointer);
            ApplyToCamera(candidate.Pointer, target);
            var after = UnsafeCameraMemory.Capture(candidate.Pointer);
            var matches = MatchesTarget(target, after, out var mismatch);
            var score = CalculateMismatchScore(target, after);
            candidateResults.Add($"{frameLabel} slot {candidate.Slot}: {(matches ? "match" : "mismatch")} score={score.ToString("0.#####", CultureInfo.InvariantCulture)}; {candidate.Source}; {mismatch}");

            var result = new CandidateApplyResult(candidate, before, after, matches, mismatch, score);
            if (best == null || result.Score < best.Score)
                best = result;
        }

        LastCandidateApplyResults = string.Join("\n", candidateResults);
        best ??= new CandidateApplyResult(candidates[0], new CameraSnapshot(), new CameraSnapshot(), false, "No camera candidates were applied.", float.MaxValue);

        if (updateSnapshots)
        {
            LastApplyBeforeSnapshot = best.Before;
            LastApplyAfterSnapshot = best.After;
            LastApplyBefore = FormatDebugSnapshot(best.Before);
            LastApplyAfter = FormatDebugSnapshot(best.After);
            LastResolvedCameraSource = $"{best.Candidate.Source} (best {frameLabel} match)";
            LastResolvedCameraPointer = UnsafeCameraMemory.PointerString(best.Candidate.Pointer);
        }

        var summary = $"{frameLabel}: best slot {best.Candidate.Slot} score={best.Score.ToString("0.#####", CultureInfo.InvariantCulture)}, match={(best.MatchesTarget ? "yes" : "no")}; {best.Mismatch}";
        return new ApplyAttemptResult(best.Candidate, best.Before, best.After, best.MatchesTarget, best.Mismatch, summary);
    }

    private ApplyAttemptResult CaptureBestMatch(IReadOnlyList<CameraCandidate> candidates, CameraSnapshot target)
    {
        CandidateApplyResult? best = null;
        foreach (var candidate in candidates)
        {
            var snapshot = UnsafeCameraMemory.Capture(candidate.Pointer);
            var matches = MatchesTarget(target, snapshot, out var mismatch);
            var score = CalculateMismatchScore(target, snapshot);
            var result = new CandidateApplyResult(candidate, snapshot, snapshot, matches, mismatch, score);
            if (best == null || result.Score < best.Score)
                best = result;
        }

        best ??= new CandidateApplyResult(candidates[0], new CameraSnapshot(), new CameraSnapshot(), false, "No camera candidates were captured.", float.MaxValue);
        LastResolvedCameraSource = $"{best.Candidate.Source} (best frame2 verify match)";
        LastResolvedCameraPointer = UnsafeCameraMemory.PointerString(best.Candidate.Pointer);
        var summary = $"Frame2: best slot {best.Candidate.Slot} score={best.Score.ToString("0.#####", CultureInfo.InvariantCulture)}, match={(best.MatchesTarget ? "yes" : "no")}; {best.Mismatch}";
        return new ApplyAttemptResult(best.Candidate, best.Before, best.After, best.MatchesTarget, best.Mismatch, summary);
    }

    private static void ApplyToCamera(Camera* camera, CameraSnapshot snapshot)
    {
        if (snapshot.Mode is { } mode)
            camera->ControlMode = (CameraControlMode)mode;

        if (snapshot.Distance is { } distance)
        {
            camera->Distance = distance;
            camera->InterpDistance = distance;
            camera->SavedDistance = distance;
        }

        if (snapshot.HRotation is { } hRotation)
        {
            camera->DirH = hRotation;
            camera->InputDeltaH = 0f;
            camera->InputDeltaHAdjusted = 0f;
        }

        if (snapshot.VRotation is { } vRotation)
        {
            camera->DirV = vRotation;
            camera->InputDeltaV = 0f;
            camera->InputDeltaVAdjusted = 0f;
        }

        camera->ShouldResetAngles = false;

        if (snapshot.FoV is { } fov)
        {
            camera->FoV = fov;
            if (camera->SceneCamera.RenderCamera != null)
                camera->SceneCamera.RenderCamera->FoV = fov;
        }

        if (snapshot.AddedFoV is { } addedFoV && camera->SceneCamera.RenderCamera != null)
            camera->SceneCamera.RenderCamera->FoV_2 = addedFoV;

        if (snapshot.Tilt is { } tilt)
        {
            camera->TiltOffset = tilt;
            camera->SetTiltOffset(tilt);
            camera->UpdateTiltOffset();
        }

        if (snapshot.LastPosition is { } lastPosition)
            camera->LastPosition = ToClientVector(lastPosition);

        if (snapshot.LastLookAtVector is { } lookAt)
            camera->LastLookAtVector = ToClientVector(lookAt);

        if (snapshot.ScenePosition is { } scenePosition)
            camera->SceneCamera.Position = ToNumericsVector(scenePosition);

        if (snapshot.RenderOrigin is { } origin && camera->SceneCamera.RenderCamera != null)
            camera->SceneCamera.RenderCamera->Origin = ToClientVector(origin);

        camera->CalculateSceneCameraPitch();
        camera->CalculateSceneCameraYaw();
        camera->UpdateState();
        camera->Update();

        // Some camera update paths recalculate interpolated/current fields during the call above.
        // Re-assert the captured values once so a preset load snaps immediately instead of drifting
        // back toward the pre-load frame.
        if (snapshot.Distance is { } distanceAfterUpdate)
        {
            camera->Distance = distanceAfterUpdate;
            camera->InterpDistance = distanceAfterUpdate;
            camera->SavedDistance = distanceAfterUpdate;
        }

        if (snapshot.HRotation is { } hRotationAfterUpdate)
            camera->DirH = hRotationAfterUpdate;

        if (snapshot.VRotation is { } vRotationAfterUpdate)
            camera->DirV = vRotationAfterUpdate;

        if (snapshot.FoV is { } fovAfterUpdate)
        {
            camera->FoV = fovAfterUpdate;
            if (camera->SceneCamera.RenderCamera != null)
                camera->SceneCamera.RenderCamera->FoV = fovAfterUpdate;
        }

        if (snapshot.AddedFoV is { } addedFoVAfterUpdate && camera->SceneCamera.RenderCamera != null)
            camera->SceneCamera.RenderCamera->FoV_2 = addedFoVAfterUpdate;

        if (snapshot.Tilt is { } tiltAfterUpdate)
            camera->TiltOffset = tiltAfterUpdate;
    }

    private void ResetApplyFrameDiagnostics()
    {
        LastApplyFrame0Result = string.Empty;
        LastApplyFrame1Result = string.Empty;
        LastApplyFrame2Result = string.Empty;
        LastApplyFrameCount = 0;
        LastCandidateApplyResults = string.Empty;
    }

    private void SetApplyFailure(string error)
    {
        LastApplyError = error;
        LastApplyResult = error;
    }

    private static bool MatchesTarget(CameraSnapshot target, CameraSnapshot actual, out string mismatch)
    {
        var errors = new List<string>();
        AddMismatch(errors, "Distance", target.Distance, actual.Distance, DistanceTolerance, angleAware: false);
        AddMismatch(errors, "HRotation", target.HRotation, actual.HRotation, RotationTolerance, angleAware: true);
        AddMismatch(errors, "VRotation", target.VRotation, actual.VRotation, RotationTolerance, angleAware: true);
        AddMismatch(errors, "FoV", target.FoV, actual.FoV, FovTolerance, angleAware: false);
        AddMismatch(errors, "AddedFoV", target.AddedFoV, actual.AddedFoV, FovTolerance, angleAware: false);
        AddMismatch(errors, "Tilt", target.Tilt, actual.Tilt, RotationTolerance, angleAware: false);
        AddVectorMismatch(errors, "LastLookAtVector", target.LastLookAtVector, actual.LastLookAtVector, DistanceTolerance);
        AddVectorMismatch(errors, "ScenePosition", target.ScenePosition, actual.ScenePosition, DistanceTolerance);
        AddVectorMismatch(errors, "RenderOrigin", target.RenderOrigin, actual.RenderOrigin, DistanceTolerance);

        mismatch = errors.Count == 0 ? string.Empty : string.Join("; ", errors);
        return errors.Count == 0;
    }

    private static float CalculateMismatchScore(CameraSnapshot target, CameraSnapshot actual)
    {
        var score = 0f;
        score += Score(target.Distance, actual.Distance, DistanceTolerance, false);
        score += Score(target.HRotation, actual.HRotation, RotationTolerance, true);
        score += Score(target.VRotation, actual.VRotation, RotationTolerance, true);
        score += Score(target.FoV, actual.FoV, FovTolerance, false);
        score += Score(target.AddedFoV, actual.AddedFoV, FovTolerance, false);
        score += Score(target.Tilt, actual.Tilt, RotationTolerance, false);
        score += Score(target.LastLookAtVector, actual.LastLookAtVector, DistanceTolerance);
        score += Score(target.ScenePosition, actual.ScenePosition, DistanceTolerance);
        score += Score(target.RenderOrigin, actual.RenderOrigin, DistanceTolerance);
        return score;
    }

    private static float Score(float? target, float? actual, float tolerance, bool angleAware)
    {
        if (target == null)
            return 0f;
        if (actual == null)
            return 100000f;
        var delta = angleAware ? AngleDelta(target.Value, actual.Value) : Math.Abs(target.Value - actual.Value);
        return delta <= tolerance ? 0f : delta;
    }

    private static float Score(SerializableVector3? target, SerializableVector3? actual, float tolerance)
    {
        if (target == null)
            return 0f;
        if (actual == null)
            return 100000f;
        var delta = VectorDelta(target, actual);
        return delta <= tolerance ? 0f : delta;
    }

    private static void AddMismatch(List<string> errors, string field, float? target, float? actual, float tolerance, bool angleAware)
    {
        if (target == null)
            return;

        if (actual == null)
        {
            errors.Add($"{field}: actual=null target={F(target)}");
            return;
        }

        var delta = angleAware ? AngleDelta(target.Value, actual.Value) : Math.Abs(actual.Value - target.Value);
        if (delta > tolerance)
            errors.Add($"{field}: actual={F(actual)} target={F(target)} delta={delta.ToString("0.#####", CultureInfo.InvariantCulture)}");
    }

    private static void AddVectorMismatch(List<string> errors, string field, SerializableVector3? target, SerializableVector3? actual, float tolerance)
    {
        if (target == null)
            return;

        if (actual == null)
        {
            errors.Add($"{field}: actual=null target={FormatVector(target)}");
            return;
        }

        var delta = VectorDelta(target, actual);
        if (delta > tolerance)
            errors.Add($"{field}: actual={FormatVector(actual)} target={FormatVector(target)} delta={delta.ToString("0.#####", CultureInfo.InvariantCulture)}");
    }

    private CameraApplyDebugRow BuildFloatRow(string field, Func<CameraSnapshot, float?> selector, float tolerance, bool angleAware)
    {
        var before = LastApplyBeforeSnapshot == null ? null : selector(LastApplyBeforeSnapshot);
        var target = LastApplyTargetSnapshot == null ? null : selector(LastApplyTargetSnapshot);
        var after = LastApplyAfterSnapshot == null ? null : selector(LastApplyAfterSnapshot);
        var next = LastApplyNextFrameSnapshot == null ? null : selector(LastApplyNextFrameSnapshot);
        var check = next ?? after;
        var delta = target != null && check != null
            ? (angleAware ? AngleDelta(target.Value, check.Value) : check.Value - target.Value).ToString("0.#####", CultureInfo.InvariantCulture)
            : "n/a";

        return new CameraApplyDebugRow
        {
            Field = field,
            Before = F(before),
            Target = F(target),
            After = F(after),
            NextFrame = F(next),
            Delta = delta == "n/a" ? delta : $"{delta} (tol {tolerance.ToString("0.#####", CultureInfo.InvariantCulture)})",
        };
    }

    private CameraApplyDebugRow BuildVectorRow(string field, Func<CameraSnapshot, SerializableVector3?> selector)
    {
        var before = LastApplyBeforeSnapshot == null ? null : selector(LastApplyBeforeSnapshot);
        var target = LastApplyTargetSnapshot == null ? null : selector(LastApplyTargetSnapshot);
        var after = LastApplyAfterSnapshot == null ? null : selector(LastApplyAfterSnapshot);
        var next = LastApplyNextFrameSnapshot == null ? null : selector(LastApplyNextFrameSnapshot);
        var check = next ?? after;
        var delta = target != null && check != null
            ? VectorDelta(target, check).ToString("0.#####", CultureInfo.InvariantCulture)
            : "n/a";

        return new CameraApplyDebugRow
        {
            Field = field,
            Before = FormatVector(before),
            Target = FormatVector(target),
            After = FormatVector(after),
            NextFrame = FormatVector(next),
            Delta = delta,
        };
    }

    private static CameraSnapshot Clone(CameraSnapshot snapshot)
    {
        return new CameraSnapshot
        {
            Mode = snapshot.Mode,
            Distance = snapshot.Distance,
            HRotation = snapshot.HRotation,
            VRotation = snapshot.VRotation,
            FoV = snapshot.FoV,
            AddedFoV = snapshot.AddedFoV,
            Pan = snapshot.Pan,
            Tilt = snapshot.Tilt,
            Roll = snapshot.Roll,
            LastPosition = CloneVector(snapshot.LastPosition),
            LastLookAtVector = CloneVector(snapshot.LastLookAtVector),
            ScenePosition = CloneVector(snapshot.ScenePosition),
            RenderOrigin = CloneVector(snapshot.RenderOrigin),
            Extra = new Dictionary<string, object>(snapshot.Extra),
        };
    }

    private static SerializableVector3? CloneVector(SerializableVector3? value)
    {
        return value == null ? null : new SerializableVector3(value.X, value.Y, value.Z);
    }

    private static bool IsGood(float? value, float min, float max)
    {
        return value is { } actual && !float.IsNaN(actual) && !float.IsInfinity(actual) && actual >= min && actual <= max;
    }

    private static bool IsOptionalGood(float? value, float min, float max)
    {
        return value == null || IsGood(value, min, max);
    }

    private static bool IsOptionalVectorGood(SerializableVector3? value, float min, float max)
    {
        return value == null ||
               IsGood(value.X, min, max) &&
               IsGood(value.Y, min, max) &&
               IsGood(value.Z, min, max);
    }

    private static string FormatDebugSnapshot(CameraSnapshot snapshot)
    {
        return $"mode={snapshot.Mode?.ToString(CultureInfo.InvariantCulture) ?? "null"}, dist={F(snapshot.Distance)}, h={F(snapshot.HRotation)}, v={F(snapshot.VRotation)}, fov={F(snapshot.FoV)}, added={F(snapshot.AddedFoV)}, tilt={F(snapshot.Tilt)}, lookAt={FormatVector(snapshot.LastLookAtVector)}, scenePos={FormatVector(snapshot.ScenePosition)}, renderOrigin={FormatVector(snapshot.RenderOrigin)}";
    }

    private static string SlotName(int slot)
    {
        return slot switch
        {
            0 => "CameraManager.Cameras[0] normal GameCamera",
            1 => "CameraManager.Cameras[1] low-cut Camera",
            2 => "CameraManager.Cameras[2] lobby Camera",
            3 => "CameraManager.Cameras[3] spectator Camera",
            _ => $"CameraManager.Cameras[{slot}]",
        };
    }

    private static string F(float? value) => value?.ToString("0.#####", CultureInfo.InvariantCulture) ?? "null";

    private static string FormatVector(SerializableVector3? value)
    {
        return value == null
            ? "null"
            : $"{value.X.ToString("0.###", CultureInfo.InvariantCulture)},{value.Y.ToString("0.###", CultureInfo.InvariantCulture)},{value.Z.ToString("0.###", CultureInfo.InvariantCulture)}";
    }

    private static float AngleDelta(float target, float actual)
    {
        var delta = Math.Abs(actual - target) % TwoPi;
        return delta > MathF.PI ? TwoPi - delta : delta;
    }

    private static float VectorDelta(SerializableVector3 left, SerializableVector3 right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static FFXIVClientStructs.FFXIV.Common.Math.Vector3 ToClientVector(SerializableVector3 value)
    {
        return new FFXIVClientStructs.FFXIV.Common.Math.Vector3(value.X, value.Y, value.Z);
    }

    private static Vector3 ToNumericsVector(SerializableVector3 value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }

    private readonly struct CameraCandidate
    {
        public CameraCandidate(int slot, string source, Camera* pointer, bool hasRenderCamera)
        {
            Slot = slot;
            Source = source;
            Pointer = pointer;
            HasRenderCamera = hasRenderCamera;
        }

        public int Slot { get; }

        public string Source { get; }

        public Camera* Pointer { get; }

        public bool HasRenderCamera { get; }
    }

    private sealed class PendingApplyJob
    {
        public PendingApplyJob(CameraSnapshot target, int? forcedSlot, bool requireGpose)
        {
            Target = target;
            ForcedSlot = forcedSlot;
            RequireGpose = requireGpose;
        }

        public CameraSnapshot Target { get; }

        public int? ForcedSlot { get; }

        public bool RequireGpose { get; }

        public int Phase { get; set; } = 1;
    }

    private sealed class CandidateApplyResult
    {
        public CandidateApplyResult(CameraCandidate candidate, CameraSnapshot before, CameraSnapshot after, bool matchesTarget, string mismatch, float score)
        {
            Candidate = candidate;
            Before = before;
            After = after;
            MatchesTarget = matchesTarget;
            Mismatch = mismatch;
            Score = score;
        }

        public CameraCandidate Candidate { get; }

        public CameraSnapshot Before { get; }

        public CameraSnapshot After { get; }

        public bool MatchesTarget { get; }

        public string Mismatch { get; }

        public float Score { get; }
    }

    private sealed class ApplyAttemptResult
    {
        public ApplyAttemptResult(CameraCandidate candidate, CameraSnapshot before, CameraSnapshot after, bool matchesTarget, string mismatch, string summary)
        {
            Candidate = candidate;
            Before = before;
            After = after;
            MatchesTarget = matchesTarget;
            Mismatch = mismatch;
            Summary = summary;
        }

        public CameraCandidate Candidate { get; }

        public CameraSnapshot Before { get; }

        public CameraSnapshot After { get; }

        public bool MatchesTarget { get; }

        public string Mismatch { get; }

        public string Summary { get; }
    }
}
