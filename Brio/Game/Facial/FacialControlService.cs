// Facial blend data and the core interpolation/propagation algorithm are adapted
// from Ktisis (GPL-3.0), commit e44fb51873119a05e4943cf08d6c124c6a9dfd04.
// The lifecycle and skeleton binding are rewritten for Brio.

using Brio.Core;
using Brio.Game.Posing.Skeletons;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok.Animation.Rig;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;
using FFXIVClientStructs.Havok.Common.Base.Math.Quaternion;
using FFXIVClientStructs.Havok.Common.Base.Math.Vector;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;

namespace Brio.Game.Facial;

public sealed class FacialControlService
{
    private const string ResourceMarker = ".Resources.Embedded.FacialControls.";
    private readonly Dictionary<ushort, IReadOnlyList<FacialParameter>> _schemas = [];

    public bool CombinePairs { get; set; } = true;
    public bool LinkPairs { get; set; }
    public bool UnlockSliders { get; set; }

    public FacialControlService()
    {
        LoadSchemas();
    }

    public unsafe void UpdateAndApply(FacialControlState state, Skeleton skeleton)
    {
        if(!TryBind(state, skeleton))
            return;

        var active = state.CaptureActiveParameters(skeleton);
        if(active.Count == 0)
            return;

        if(skeleton.Partials.Count <= 1)
            return;

        var pose = skeleton.Partials[1].GetBestPose();
        if(pose == null || pose->Skeleton == null || pose->Skeleton->ParentIndices.Data == null || pose->ModelPose.Data == null)
            return;

        foreach(var (parameter, weight) in active)
            ApplyParameter(pose, parameter, state.FaceId, weight);
    }

    private unsafe bool TryBind(FacialControlState state, Skeleton skeleton)
    {
        if(!skeleton.IsValid || skeleton.GameSkeleton == null || skeleton.CharacterBase == null)
        {
            state.SetUnavailable("The actor skeleton is no longer valid.");
            return false;
        }

        if(skeleton.CharacterBase->CharacterBase.GetModelType() != CharacterBase.ModelType.Human)
        {
            state.SetUnavailable("Facial controls require a human character model.", skeleton);
            return false;
        }

        if(skeleton.Partials.Count <= 1)
        {
            state.SetUnavailable("The actor has no Dawntrail facial partial skeleton.", skeleton);
            return false;
        }

        var facePartial = skeleton.Partials[1];
        var pose = facePartial.GetBestPose();
        if(pose == null || pose->Skeleton == null || pose->Skeleton->Bones.Data == null || pose->Skeleton->ParentIndices.Data == null || pose->ModelPose.Data == null)
        {
            state.SetUnavailable("The actor's facial Havok pose is not available.", skeleton);
            return false;
        }

        if(facePartial.GetBone("j_f_face") is null)
        {
            state.SetUnavailable("Facial controls require a Dawntrail face skeleton (j_f_face).", skeleton);
            return false;
        }

        var human = (Human*)skeleton.CharacterBase;
        var raceSexId = human->RaceSexId;
        var faceId = human->Customize.Face;

        if(!_schemas.TryGetValue(raceSexId, out var parameters))
        {
            state.SetUnavailable($"No expression table exists for race/sex ID {raceSexId}.", skeleton);
            return false;
        }

        state.Bind(skeleton, raceSexId, faceId, parameters);
        return state.IsAvailable;
    }

    private static unsafe void ApplyParameter(hkaPose* pose, FacialParameter parameter, byte faceId, float weight)
    {
        var transforms = parameter.GetTransforms(faceId);
        if(transforms is null)
            return;

        var bones = pose->Skeleton->Bones;
        var modelLength = pose->ModelPose.Length;
        var count = Math.Min(Math.Min(bones.Length, modelLength), pose->Skeleton->ParentIndices.Length);

        // Ktisis begins at one because a facial partial's root is not a slider target.
        for(var boneIndex = 1; boneIndex < count; boneIndex++)
        {
            var name = bones[boneIndex].Name.String;
            if(name is null || !transforms.TryGetValue(name, out var targetLocal))
                continue;

            var parentIndex = pose->Skeleton->ParentIndices[boneIndex];
            if(parentIndex < 0 || parentIndex >= count)
                continue;

            var parent = GetModelTransform(pose, parentIndex);
            var initial = GetModelTransform(pose, boneIndex);
            if(parent is null || initial is null)
                continue;

            var local = new Transform
            {
                Position = Vector3.Lerp(Vector3.Zero, targetLocal.Position, weight),
                Rotation = Quaternion.Slerp(Quaternion.Identity, targetLocal.Rotation, weight),
                // Brio's Transform identity uses a zero scale delta; expression tables use
                // an absolute multiplicative scale whose identity is Vector3.One.
                Scale = Vector3.Lerp(Vector3.One, targetLocal.Scale, weight)
            };

            var projected = new Transform
            {
                Position = parent.Value.Position + Vector3.Transform(local.Position, parent.Value.Rotation),
                Rotation = NormalizeSafe(parent.Value.Rotation * local.Rotation),
                Scale = local.Scale
            };

            var identityProjected = new Transform
            {
                Position = parent.Value.Position,
                Rotation = parent.Value.Rotation,
                Scale = Vector3.One
            };

            var next = new Transform
            {
                Position = (initial.Value.Position - identityProjected.Position) + projected.Position,
                Rotation = NormalizeSafe(initial.Value.Rotation * Quaternion.Inverse(identityProjected.Rotation) * projected.Rotation),
                Scale = Multiply(initial.Value.Scale, projected.Scale)
            };

            SetModelTransform(pose, boneIndex, next);
            Propagate(pose, boneIndex, next, initial.Value);
        }
    }

    private void LoadSchemas()
    {
        var assembly = typeof(FacialControlService).Assembly;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach(var resourceName in assembly.GetManifestResourceNames().Where(name => name.Contains(ResourceMarker, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var fileName = resourceName[(resourceName.IndexOf(ResourceMarker, StringComparison.Ordinal) + ResourceMarker.Length)..];
                var idText = fileName.Split('_', 2)[0];
                if(!ushort.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var raceSexId))
                    continue;

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if(stream is null)
                    continue;

                var dto = System.Text.Json.JsonSerializer.Deserialize<FacialSchemaFileDto>(stream, options);
                if(dto is null)
                    continue;

                var parameters = dto.Data.Select(ConvertParameter).OrderBy(parameter => parameter.Priority).ToArray();
                ValidateParameters(raceSexId, parameters);
                _schemas.Add(raceSexId, parameters);
            }
            catch(Exception ex)
            {
                Brio.Log.Error(ex, $"Failed to load facial-control resource {resourceName}");
            }
        }

        Brio.Log.Info($"Loaded {_schemas.Count} Ktisis facial-control schemas.");
    }

    private static FacialParameter ConvertParameter(FacialParameterDto dto)
    {
        IReadOnlyDictionary<string, Transform>? transforms = dto.Transforms?.ToDictionary(entry => entry.Key, entry => ConvertTransform(entry.Value));
        IReadOnlyDictionary<byte, IReadOnlyDictionary<string, Transform>>? faceTransforms = dto.Skeletons?.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyDictionary<string, Transform>)entry.Value.ToDictionary(transform => transform.Key, transform => ConvertTransform(transform.Value)));

        return new FacialParameter(dto.Id, dto.Priority, dto.Pair, transforms, faceTransforms);
    }

    private static Transform ConvertTransform(FacialTransformDto dto) => new()
    {
        Position = ParseVector3(dto.Position),
        Rotation = ParseQuaternion(dto.Rotation),
        Scale = ParseVector3(dto.Scale)
    };

    private static Vector3 ParseVector3(string value)
    {
        var components = ParseComponents(value, 3);
        return new Vector3(components[0], components[1], components[2]);
    }

    private static Quaternion ParseQuaternion(string value)
    {
        var components = ParseComponents(value, 4);
        return new Quaternion(components[0], components[1], components[2], components[3]);
    }

    private static float[] ParseComponents(string value, int expected)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length != expected)
            throw new InvalidDataException($"Expected {expected} transform components, found {parts.Length}: {value}");

        var result = new float[expected];
        for(var index = 0; index < expected; index++)
        {
            result[index] = float.Parse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture);
            if(!float.IsFinite(result[index]))
                throw new InvalidDataException($"Non-finite transform component: {value}");
        }
        return result;
    }

    private static void ValidateParameters(ushort raceSexId, IReadOnlyList<FacialParameter> parameters)
    {
        if(parameters.Count == 0)
            throw new InvalidDataException($"Facial schema {raceSexId} is empty.");

        var ids = parameters.Select(parameter => parameter.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var parameter in parameters)
        {
            if(string.IsNullOrWhiteSpace(parameter.Id))
                throw new InvalidDataException($"Facial schema {raceSexId} contains an empty parameter ID.");
            if(parameter.Pair is not null && !ids.Contains(parameter.Pair))
                throw new InvalidDataException($"Facial schema {raceSexId} parameter {parameter.Id} references missing pair {parameter.Pair}.");
            if(parameter.Transforms is null && parameter.FaceTransforms is null)
                throw new InvalidDataException($"Facial schema {raceSexId} parameter {parameter.Id} contains no transforms.");
        }
    }

    private static unsafe Transform? GetModelTransform(hkaPose* pose, int boneIndex)
    {
        if(pose == null || pose->ModelPose.Data == null || boneIndex < 0 || boneIndex >= pose->ModelPose.Length)
            return null;

        var transform = pose->ModelPose.Data + boneIndex;
        return new Transform
        {
            Position = new Vector3(transform->Translation.X, transform->Translation.Y, transform->Translation.Z),
            Rotation = new Quaternion(transform->Rotation.X, transform->Rotation.Y, transform->Rotation.Z, transform->Rotation.W),
            Scale = new Vector3(transform->Scale.X, transform->Scale.Y, transform->Scale.Z)
        };
    }

    private static unsafe void SetModelTransform(hkaPose* pose, int boneIndex, Transform transform)
    {
        if(pose == null || pose->ModelPose.Data == null || boneIndex < 0 || boneIndex >= pose->ModelPose.Length)
            return;

        var target = pose->ModelPose.Data + boneIndex;
        target->Translation = new hkVector4f { X = transform.Position.X, Y = transform.Position.Y, Z = transform.Position.Z, W = 0f };
        target->Rotation = new hkQuaternionf { X = transform.Rotation.X, Y = transform.Rotation.Y, Z = transform.Rotation.Z, W = transform.Rotation.W };
        target->Scale = new hkVector4f { X = transform.Scale.X, Y = transform.Scale.Y, Z = transform.Scale.Z, W = 0f };
    }

    private static unsafe void Propagate(hkaPose* pose, int boneIndex, Transform target, Transform initial)
    {
        var sourcePosition = target.Position;
        var deltaPosition = sourcePosition - initial.Position;
        var deltaRotation = NormalizeSafe(target.Rotation * Quaternion.Inverse(initial.Rotation));

        for(var index = boneIndex + 1; index < pose->Skeleton->Bones.Length && index < pose->ModelPose.Length; index++)
        {
            if(!IsDescendantOf(pose, index, boneIndex))
                continue;

            var transform = GetModelTransform(pose, index);
            if(transform is null)
                continue;

            var nextPosition = sourcePosition + Vector3.Transform(transform.Value.Position - (sourcePosition - deltaPosition), deltaRotation);
            var nextRotation = NormalizeSafe(deltaRotation * transform.Value.Rotation);
            var matrix = Matrix4x4.CreateScale(ClampScale(transform.Value.Scale))
                * Matrix4x4.CreateFromQuaternion(nextRotation)
                * Matrix4x4.CreateTranslation(nextPosition);

            SetModelTransform(pose, index, DecomposePrecise(matrix, transform.Value));
        }
    }

    private static unsafe bool IsDescendantOf(hkaPose* pose, int boneIndex, int parentIndex)
    {
        if(pose->Skeleton->ParentIndices.Data == null || boneIndex < 0 || boneIndex >= pose->Skeleton->ParentIndices.Length)
            return false;

        var current = pose->Skeleton->ParentIndices[boneIndex];
        var guard = pose->Skeleton->ParentIndices.Length;
        while(current >= 0 && current < pose->Skeleton->ParentIndices.Length && guard-- > 0)
        {
            if(current == parentIndex)
                return true;
            current = pose->Skeleton->ParentIndices[current];
        }
        return false;
    }

    private static Transform DecomposePrecise(Matrix4x4 matrix, Transform initial)
    {
        const float epsilon = 1e-6f;
        if(!Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var position))
            return initial;

        if((position - initial.Position).LengthSquared() < epsilon * epsilon)
            position = initial.Position;

        if(Quaternion.Dot(rotation, initial.Rotation) < 0f)
            rotation = new Quaternion(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);

        var dot = Math.Clamp(Quaternion.Dot(rotation, initial.Rotation), -1f, 1f);
        if(2f * MathF.Acos(dot) < epsilon)
            rotation = initial.Rotation;

        scale.X = IsScaleJitter(initial.Scale.X, scale.X, epsilon) ? initial.Scale.X : scale.X;
        scale.Y = IsScaleJitter(initial.Scale.Y, scale.Y, epsilon) ? initial.Scale.Y : scale.Y;
        scale.Z = IsScaleJitter(initial.Scale.Z, scale.Z, epsilon) ? initial.Scale.Z : scale.Z;

        return new Transform { Position = position, Rotation = rotation, Scale = scale };
    }

    private static bool IsScaleJitter(float initial, float current, float epsilon)
    {
        var magnitude = MathF.Max(MathF.Abs(initial), 1e-6f);
        return MathF.Abs(current - initial) / magnitude < epsilon;
    }

    private static Vector3 ClampScale(Vector3 scale) => new(
        scale.X is < 0.001f and > -0.001f ? 0.001f : scale.X,
        scale.Y is < 0.001f and > -0.001f ? 0.001f : scale.Y,
        scale.Z is < 0.001f and > -0.001f ? 0.001f : scale.Z);

    private static Vector3 Multiply(Vector3 left, Vector3 right) => new(left.X * right.X, left.Y * right.Y, left.Z * right.Z);

    private static Quaternion NormalizeSafe(Quaternion value)
    {
        if(!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W) || value.LengthSquared() < 1e-12f)
            return Quaternion.Identity;
        return Quaternion.Normalize(value);
    }
}
