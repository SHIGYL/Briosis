// Facial controls are based on Ktisis (GPL-3.0), commit
// e44fb51873119a05e4943cf08d6c124c6a9dfd04.

using Brio.Capabilities.Posing;
using Brio.Core;
using Brio.Entities.Actor;
using Brio.Game.Facial;
using Brio.Game.Posing;
using Brio.Game.Posing.Skeletons;
using Brio.Services;
using Brio.Services.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Brio.Capabilities.Actor;

public sealed class FacialControlCapability : ActorCharacterCapability
{
    private static readonly string[] TongueBoneNames = ["j_f_bero_01", "j_f_bero_02", "j_f_bero_03"];
    private readonly FacialControlService _service;
    private readonly PresetSystem _presetSystem;

    public FacialControlState State { get; } = new();
    public FacialControlService Service => _service;

    public bool IsAvailable => State.IsAvailable;
    public string UnavailableReason => State.UnavailableReason;
    public ushort RaceSexId => State.RaceSexId;
    public byte FaceId => State.FaceId;

    public FacialControlCapability(ActorEntity parent, FacialControlService service, PresetSystem presetSystem) : base(parent)
    {
        _service = service;
        _presetSystem = presetSystem;

        var defaultProfile = _presetSystem.GetDefaultTongueProfile();
        if(defaultProfile is not null)
            ApplyTongueProfile(defaultProfile);
    }

    public IReadOnlyList<FacialParameter> GetParameters() => State.GetParameters();
    public float GetWeight(string id) => State.GetWeight(id);
    public bool SetWeight(string id, float weight) => State.SetWeight(id, weight);
    public void Reset(string id) => State.Reset(id);
    public void ResetAll() => State.ResetAll();
    public void LogTongueRigDiagnostics() => State.RequestTongueRigDiagnostics();
    public TongueControlDebugSettings GetTongueDebugSettings() => State.GetTongueDebugSettings();
    public TongueControlDebugInfo GetTongueDebugInfo() => State.GetTongueDebugInfo();
    public void SetTongueWeight(TongueControlBone bone, float value) => State.SetTongueWeight(bone, value);
    public void SetTongueVisibleExtension(float value) => State.SetTongueVisibleExtension(value);
    public IReadOnlyDictionary<string, float> CapturePresetControls() => State.CapturePresetControls();
    public void ApplyPresetControls(IReadOnlyDictionary<string, float> controls) => State.ApplyPresetControls(controls);
    public string? GetActiveTongueProfilePath() => State.GetActiveTongueProfilePath();
    public void ResetTongueSettings() => State.ResetTongueSettings();

    public FacialControlHistoryState CaptureHistoryState()
        => new(
            State.CapturePresetControls(),
            State.GetTongueDebugSettings(),
            State.GetActiveTongueProfilePath(),
            State.GetTongueBoneAdjustments());

    public void ApplyHistoryState(FacialControlHistoryState state)
    {
        State.ApplyPresetControls(state.Controls);
        State.ApplyTongueSettings(state.TongueSettings, state.ActiveTongueProfilePath);
        State.SetTongueBoneAdjustments(state.TongueBoneAdjustments);
    }

    public void ApplyFacialPreset(FacialPresetFile preset)
    {
        State.ApplyPresetControls(preset.Controls);
    }

    public IReadOnlyDictionary<string, TongueBoneAdjustment> CaptureTongueBoneAdjustments()
    {
        var result = State.GetTongueBoneAdjustments().ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        if(!Entity.TryGetCapability<SkeletonPosingCapability>(out var skeletonPosing))
            return result;

        foreach(var boneName in TongueBoneNames)
        {
            var bone = skeletonPosing.GetBone(boneName, PoseInfoSlot.Character);
            if(bone is null)
                continue;

            var manualPosition = Vector3.Zero;
            var manualRotation = Quaternion.Identity;
            foreach(var stack in skeletonPosing.GetBonePose(bone).Stacks)
            {
                manualPosition += stack.Transform.Position;
                manualRotation = NormalizeSafe(manualRotation * stack.Transform.Rotation);
            }

            var hasManualDelta = manualPosition.LengthSquared() > 1e-12f
                || MathF.Abs(Quaternion.Dot(manualRotation, Quaternion.Identity)) < 0.999999f;
            if(!hasManualDelta)
                continue;

            if(result.TryGetValue(boneName, out var appliedPresetAdjustment))
            {
                manualPosition += appliedPresetAdjustment.PositionDelta;
                manualRotation = NormalizeSafe(manualRotation * appliedPresetAdjustment.RotationDelta);
            }

            result[boneName] = new TongueBoneAdjustment(manualPosition, manualRotation);
        }

        return result;
    }

    public void ConsumeManualTongueBoneAdjustments()
    {
        if(!Entity.TryGetCapability<SkeletonPosingCapability>(out var skeletonPosing))
            return;

        foreach(var boneName in TongueBoneNames)
        {
            var bone = skeletonPosing.GetBone(boneName, PoseInfoSlot.Character);
            if(bone is not null)
                skeletonPosing.GetBonePose(bone).ClearComponents(TransformComponents.Position | TransformComponents.Rotation);
        }
    }

    private static Quaternion NormalizeSafe(Quaternion value)
        => !float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W) || value.LengthSquared() < 1e-12f
            ? Quaternion.Identity
            : Quaternion.Normalize(value);

    public void EnsureDefaultTongueProfile()
    {
        if(State.IsTongueProfileInitialized())
            return;

        var defaultProfile = _presetSystem.GetDefaultTongueProfile();
        if(defaultProfile is not null)
            ApplyTongueProfile(defaultProfile);
    }

    public bool ApplyTongueProfile(Preset preset)
    {
        var profile = _presetSystem.LoadTongueProfile(preset);
        if(profile is null)
            return false;

        State.ApplyTongueSettings(
            new TongueControlDebugSettings(profile.RootWeight, profile.BodyWeight, profile.TipWeight, profile.VisibleExtensionL),
            preset.Path);
        State.SetTongueBoneAdjustments(profile.BoneAdjustments);
        return true;
    }

    internal void UpdateAndApply(Skeleton skeleton) => _service.UpdateAndApply(State, skeleton, Actor.FriendlyName);

    public override void Dispose()
    {
        State.SetUnavailable("The actor is no longer attached.");
        base.Dispose();
    }
}
