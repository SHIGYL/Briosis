// Facial action-unit data is adapted from Ktisis (GPL-3.0), commit
// e44fb51873119a05e4943cf08d6c124c6a9dfd04.

using Brio.Core;
using System.Collections.Generic;

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

