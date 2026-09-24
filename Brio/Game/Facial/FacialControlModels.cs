// Facial action-unit data is adapted from Ktisis (GPL-3.0), commit
// e44fb51873119a05e4943cf08d6c124c6a9dfd04.

using Brio.Core;
using System.Collections.Generic;
using System.Numerics;

namespace Brio.Game.Facial;

public sealed record FacialParameter(
    string Id,
    int Priority,
    string? Pair,
    IReadOnlyDictionary<string, Transform>? Transforms,
    IReadOnlyDictionary<byte, IReadOnlyDictionary<string, Transform>>? FaceTransforms)
{
    public IReadOnlyDictionary<string, Transform>? GetTransforms(byte faceId)
    {
        if(Transforms is not null)
            return Transforms;

        if(FaceTransforms is null || FaceTransforms.Count == 0)
            return null;

        if(FaceTransforms.TryGetValue(faceId, out var transforms))
            return transforms;

        foreach(var fallback in FaceTransforms.Values)
            return fallback;

        return null;
    }
}

public enum TongueControlBone
{
    Root,
    Body,
    Tip
}

public readonly record struct TongueControlDebugSettings(
    float RootWeight,
    float BodyWeight,
    float TipWeight,
    float VisibleExtension);

public readonly record struct TongueBoneAdjustment(
    Vector3 PositionDelta,
    Quaternion RotationDelta);

public sealed record FacialControlHistoryState(
    IReadOnlyDictionary<string, float> Controls,
    TongueControlDebugSettings TongueSettings,
    string? ActiveTongueProfilePath,
    IReadOnlyDictionary<string, TongueBoneAdjustment> TongueBoneAdjustments);

public readonly record struct TongueControlDebugInfo(
    bool IsAvailable,
    string UnavailableReason,
    float ChainLength,
    float RestGap,
    float RestGapRatio,
    float DesiredVisibleExtension,
    float TotalDesiredTipTravel,
    float CurrentTipFromMouthPlane,
    Vector3 RootLocalDelta,
    Vector3 BodyLocalDelta,
    Vector3 TipLocalDelta)
{
    public static TongueControlDebugInfo Unavailable(string reason)
        => new(false, reason, 0f, 0f, 0f, 0f, 0f, 0f, Vector3.Zero, Vector3.Zero, Vector3.Zero);
}

internal sealed class FacialSchemaFileDto
{
    public FacialParameterDto[] Data { get; set; } = [];
}

internal sealed class FacialParameterDto
{
    public string Id { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string? Pair { get; set; }
    public Dictionary<string, FacialTransformDto>? Transforms { get; set; }
    public Dictionary<byte, Dictionary<string, FacialTransformDto>>? Skeletons { get; set; }
}

internal sealed class FacialTransformDto
{
    public string Position { get; set; } = "0, 0, 0";
    public string Rotation { get; set; } = "0, 0, 0, 1";
    public string Scale { get; set; } = "1, 1, 1";
}
