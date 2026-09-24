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
    private const string TongueOutPresetId = "TongueOutPreset";
    private static readonly string[] TongueRigDiagnosticBones =
    [
        "j_f_ago",
        "j_f_dago",
        "j_f_hagukiup",
        "j_f_hagukidn",
        "j_f_bero_01",
        "j_f_bero_02",
        "j_f_bero_03"
    ];
    private static readonly string[] TongueBoneNames = ["j_f_bero_01", "j_f_bero_02", "j_f_bero_03"];
    private readonly Dictionary<ushort, IReadOnlyList<FacialParameter>> _schemas = [];

    public bool CombinePairs { get; set; } = true;
    public bool LinkPairs { get; set; }
    public bool UnlockSliders { get; set; }

    public FacialControlService()
    {
        LoadSchemas();
    }

    public unsafe void UpdateAndApply(FacialControlState state, Skeleton skeleton, string actorName)
    {
        if(!TryBind(state, skeleton))
            return;

        if(state.ConsumeTongueRigDiagnosticsRequest(skeleton))
            LogTongueRigDiagnostics(state, skeleton, actorName);

        if(skeleton.Partials.Count <= 1)
            return;

        var pose = skeleton.Partials[1].GetBestPose();
        if(pose == null || pose->Skeleton == null || pose->Skeleton->ParentIndices.Data == null || pose->ModelPose.Data == null)
            return;

        var active = ResolveCompositeParameters(state, skeleton, out var tongueOutWeight);

        foreach(var (parameter, weight) in active)
            ApplyParameter(pose, parameter, state.FaceId, weight);

        ApplyTongueOut(pose, state, tongueOutWeight);
        ApplyTongueBoneAdjustments(pose, state.GetTongueBoneAdjustments(), SmoothStep(0f, 1f, tongueOutWeight));
    }

    private static unsafe void ApplyTongueBoneAdjustments(hkaPose* pose, IReadOnlyDictionary<string, TongueBoneAdjustment> adjustments, float blend)
    {
        if(adjustments.Count == 0 || blend <= 0f || pose->Skeleton == null)
            return;

        var count = Math.Min(pose->Skeleton->Bones.Length, pose->ModelPose.Length);
        foreach(var boneName in TongueBoneNames)
        {
            if(!adjustments.TryGetValue(boneName, out var adjustment))
                continue;

            var boneIndex = FindBoneIndex(pose->Skeleton, count, boneName);
            if(boneIndex < 0)
                continue;

            var initial = GetModelTransform(pose, boneIndex);
            if(initial is null)
                continue;

            var target = initial.Value;
            target.Position += adjustment.PositionDelta * blend;
            var rotationDelta = Quaternion.Slerp(Quaternion.Identity, adjustment.RotationDelta, blend);
            target.Rotation = NormalizeSafe(target.Rotation * rotationDelta);
            SetModelTransform(pose, boneIndex, target);
            Propagate(pose, boneIndex, target, initial.Value);
        }
    }

    private static unsafe void LogTongueRigDiagnostics(FacialControlState state, Skeleton skeleton, string actorName)
    {
        if(skeleton.Partials.Count <= 1)
            return;

        var pose = skeleton.Partials[1].GetBestPose();
        if(pose == null || pose->Skeleton == null)
            return;

        var rig = pose->Skeleton;
        var count = Math.Min(rig->Bones.Length, rig->ParentIndices.Length);
        var skeletonName = rig->Name.String ?? "<unnamed>";
        Brio.Log.Info($"[TongueRig] BEGIN actor=\"{actorName}\" raceSex={state.RaceSexId} face={state.FaceId} skeleton=\"{skeletonName}\" bones={rig->Bones.Length} referencePose={rig->ReferencePose.Length} localPose={pose->LocalPose.Length} modelPose={pose->ModelPose.Length}");

        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        for(var index = 0; index < count; index++)
        {
            var name = rig->Bones[index].Name.String;
            if(name is not null && TongueRigDiagnosticBones.Contains(name, StringComparer.Ordinal))
                indices[name] = index;
        }

        foreach(var boneName in TongueRigDiagnosticBones)
        {
            if(!indices.TryGetValue(boneName, out var index))
            {
                Brio.Log.Info($"[TongueRig] bone={boneName} present=false");
                continue;
            }

            var parentIndex = rig->ParentIndices[index];
            var parentName = parentIndex >= 0 && parentIndex < count
                ? rig->Bones[parentIndex].Name.String ?? "<unnamed>"
                : "<root>";
            var hierarchy = FormatHierarchy(rig, index, count);
            var reference = index < rig->ReferencePose.Length && rig->ReferencePose.Data != null
                ? FormatTransform(rig->ReferencePose.Data + index)
                : "<unavailable>";
            var local = index < pose->LocalPose.Length && pose->LocalPose.Data != null
                ? FormatTransform(pose->LocalPose.Data + index)
                : "<unavailable>";
            var model = index < pose->ModelPose.Length && pose->ModelPose.Data != null
                ? FormatTransform(pose->ModelPose.Data + index)
                : "<unavailable>";

            Brio.Log.Info($"[TongueRig] bone={boneName} present=true index={index} parent={parentName}[{parentIndex}] hierarchy={hierarchy}");
            Brio.Log.Info($"[TongueRig] bone={boneName} restLocal={reference}");
            Brio.Log.Info($"[TongueRig] bone={boneName} currentLocal={local} currentModel={model}");
        }

        if(indices.TryGetValue("j_f_bero_01", out var tongueRoot)
            && indices.TryGetValue("j_f_bero_02", out var tongueBody)
            && indices.TryGetValue("j_f_bero_03", out var tongueTip)
            && rig->ReferencePose.Data != null
            && tongueRoot < rig->ReferencePose.Length
            && tongueBody < rig->ReferencePose.Length
            && tongueTip < rig->ReferencePose.Length)
        {
            var rootToBody = ReadTranslation(rig->ReferencePose.Data + tongueBody).Length();
            var bodyToTip = ReadTranslation(rig->ReferencePose.Data + tongueTip).Length();
            Brio.Log.Info($"[TongueRig] restChain rootToBody={FormatFloat(rootToBody)} bodyToTip={FormatFloat(bodyToTip)} total={FormatFloat(rootToBody + bodyToTip)} rootParentMatchesJaw={rig->ParentIndices[tongueRoot] == indices.GetValueOrDefault("j_f_ago", -2)} bodyParentMatchesRoot={rig->ParentIndices[tongueBody] == tongueRoot} tipParentMatchesBody={rig->ParentIndices[tongueTip] == tongueBody}");
        }

        Brio.Log.Info("[TongueRig] END");
    }

    private static unsafe string FormatHierarchy(hkaSkeleton* rig, int boneIndex, int count)
    {
        var names = new List<string>();
        var current = boneIndex;
        var guard = count;
        while(current >= 0 && current < count && guard-- > 0)
        {
            names.Add($"{rig->Bones[current].Name.String ?? "<unnamed>"}[{current}]");
            current = rig->ParentIndices[current];
        }
        return string.Join(" <- ", names);
    }

    private static unsafe string FormatTransform(hkQsTransformf* transform)
        => $"P({FormatFloat(transform->Translation.X)},{FormatFloat(transform->Translation.Y)},{FormatFloat(transform->Translation.Z)}) R({FormatFloat(transform->Rotation.X)},{FormatFloat(transform->Rotation.Y)},{FormatFloat(transform->Rotation.Z)},{FormatFloat(transform->Rotation.W)}) S({FormatFloat(transform->Scale.X)},{FormatFloat(transform->Scale.Y)},{FormatFloat(transform->Scale.Z)})";

    private static unsafe Vector3 ReadTranslation(hkQsTransformf* transform)
        => new(transform->Translation.X, transform->Translation.Y, transform->Translation.Z);

    private static string FormatFloat(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static IReadOnlyList<(FacialParameter Parameter, float Weight)> ResolveCompositeParameters(FacialControlState state, Skeleton skeleton, out float tongueOutWeight)
    {
        var active = state.CaptureActiveParameters(skeleton).ToList();
        var presetIndex = active.FindIndex(entry => entry.Parameter.Id == TongueOutPresetId);
        if(presetIndex < 0)
        {
            tongueOutWeight = 0f;
            return active;
        }

        tongueOutWeight = Math.Clamp(active[presetIndex].Weight, 0f, 1f);
        active.RemoveAt(presetIndex);
        if(tongueOutWeight <= 0f)
            return active;

        var parameters = state.GetParameters();
        var mouthWeight = SmoothStep(0f, 0.35f, tongueOutWeight);
        RaiseMinimum("JawOpen", mouthWeight * 0.55f);
        RaiseMinimum("LowerLipOpen", mouthWeight * 0.35f);
        RaiseMinimum("UpperLipOpen", mouthWeight * 0.10f);

        return [.. active.OrderBy(entry => entry.Parameter.Priority)];

        void RaiseMinimum(string id, float weight)
        {
            if(weight == 0f)
                return;

            var index = active.FindIndex(entry => entry.Parameter.Id == id);
            if(index >= 0)
            {
                var current = active[index];
                active[index] = (current.Parameter, MathF.Max(current.Weight, weight));
                return;
            }

            var parameter = parameters.FirstOrDefault(candidate => candidate.Id == id);
            if(parameter is not null)
                active.Add((parameter, weight));
        }
    }

    private static unsafe void ApplyTongueOut(hkaPose* pose, FacialControlState state, float weight)
    {
        if(!TryBuildTongueGeometry(pose, out var geometry, out var reason))
        {
            state.SetTongueDebugInfo(TongueControlDebugInfo.Unavailable(reason));
            return;
        }

        var settings = state.GetTongueDebugSettings();
        var visibleExtension = settings.VisibleExtension * geometry.ChainLength;
        var totalTravel = geometry.RestGap + visibleExtension;
        var desiredDisplacement = geometry.MouthForward * totalTravel;

        var rootFactor = settings.RootWeight * SmoothStep(0.20f, 0.85f, weight);
        var bodyFactor = settings.BodyWeight * SmoothStep(0.22f, 0.95f, weight);
        var tipFactor = settings.TipWeight * SmoothStep(0.30f, 1.00f, weight);

        var rootDelta = Vector3.Zero;
        var bodyDelta = Vector3.Zero;
        var tipDelta = Vector3.Zero;

        if(weight > 0f)
        {
            rootDelta = ApplyReferenceRelativeTranslation(pose, geometry.FaceIndex, geometry.RootIndex, desiredDisplacement * rootFactor);
            bodyDelta = ApplyReferenceRelativeTranslation(pose, geometry.FaceIndex, geometry.BodyIndex, desiredDisplacement * bodyFactor);
            tipDelta = ApplyReferenceRelativeTranslation(pose, geometry.FaceIndex, geometry.TipIndex, desiredDisplacement * tipFactor);
        }

        var currentTipFromPlane = GetCurrentTipFromMouthPlane(pose, geometry);
        state.SetTongueDebugInfo(new TongueControlDebugInfo(
            true,
            string.Empty,
            geometry.ChainLength,
            geometry.RestGap,
            geometry.ChainLength > 1e-6f ? geometry.RestGap / geometry.ChainLength : 0f,
            visibleExtension,
            totalTravel,
            currentTipFromPlane,
            rootDelta,
            bodyDelta,
            tipDelta));
    }

    private static unsafe bool TryBuildTongueGeometry(hkaPose* pose, out TongueGeometry geometry, out string reason)
    {
        geometry = default;
        reason = string.Empty;

        if(pose == null || pose->Skeleton == null || pose->Skeleton->ReferencePose.Data == null || pose->Skeleton->ParentIndices.Data == null)
        {
            reason = "Reference pose is unavailable.";
            return false;
        }

        var rig = pose->Skeleton;
        var count = Math.Min(rig->Bones.Length, Math.Min(rig->ParentIndices.Length, rig->ReferencePose.Length));
        var faceIndex = FindBoneIndex(rig, count, "j_f_face");
        var upperTeethIndex = FindBoneIndex(rig, count, "j_f_hagukiup");
        var lowerTeethIndex = FindBoneIndex(rig, count, "j_f_hagukidn");
        var rootIndex = FindBoneIndex(rig, count, "j_f_bero_01");
        var bodyIndex = FindBoneIndex(rig, count, "j_f_bero_02");
        var tipIndex = FindBoneIndex(rig, count, "j_f_bero_03");

        if(faceIndex < 0 || upperTeethIndex < 0 || lowerTeethIndex < 0 || rootIndex < 0 || bodyIndex < 0 || tipIndex < 0)
        {
            reason = "Required face, teeth, or tongue bones are missing.";
            return false;
        }

        if(rig->ParentIndices[bodyIndex] != rootIndex || rig->ParentIndices[tipIndex] != bodyIndex)
        {
            reason = "The tongue bones do not form the expected 01 -> 02 -> 03 chain.";
            return false;
        }

        var referenceModels = BuildReferenceModelTransforms(rig, count);
        var faceReference = referenceModels[faceIndex];
        var rootFace = ModelPointToLocal(referenceModels[rootIndex].Position, faceReference);
        var bodyFace = ModelPointToLocal(referenceModels[bodyIndex].Position, faceReference);
        var tipFace = ModelPointToLocal(referenceModels[tipIndex].Position, faceReference);
        var upperTeethFace = ModelPointToLocal(referenceModels[upperTeethIndex].Position, faceReference);
        var lowerTeethFace = ModelPointToLocal(referenceModels[lowerTeethIndex].Position, faceReference);

        var rootToBody = Vector3.Distance(rootFace, bodyFace);
        var bodyToTip = Vector3.Distance(bodyFace, tipFace);
        var chainLength = rootToBody + bodyToTip;
        if(chainLength <= 1e-6f)
        {
            reason = "The tongue chain has zero length.";
            return false;
        }

        var mouthCenter = (upperTeethFace + lowerTeethFace) * 0.5f;
        var mouthForward = Vector3.Normalize(tipFace - rootFace);
        if(Vector3.Dot(mouthCenter - tipFace, mouthForward) < 0f)
            mouthForward = -mouthForward;

        var restGap = MathF.Max(0f, Vector3.Dot(mouthCenter - tipFace, mouthForward));
        geometry = new TongueGeometry(faceIndex, rootIndex, bodyIndex, tipIndex, chainLength, restGap, mouthCenter, mouthForward);
        return true;
    }

    private static unsafe Vector3 ApplyReferenceRelativeTranslation(hkaPose* pose, int faceIndex, int boneIndex, Vector3 faceLocalContribution)
    {
        var rig = pose->Skeleton;
        if(boneIndex < 0 || boneIndex >= rig->ParentIndices.Length || boneIndex >= rig->ReferencePose.Length)
            return Vector3.Zero;

        var parentIndex = rig->ParentIndices[boneIndex];
        var face = GetModelTransform(pose, faceIndex);
        var parent = GetModelTransform(pose, parentIndex);
        var initial = GetModelTransform(pose, boneIndex);
        if(face is null || parent is null || initial is null)
            return Vector3.Zero;

        var modelContribution = Vector3.Transform(Multiply(faceLocalContribution, face.Value.Scale), face.Value.Rotation);
        var parentUnrotated = Vector3.Transform(modelContribution, Quaternion.Inverse(parent.Value.Rotation));
        var parentLocalContribution = Divide(parentUnrotated, parent.Value.Scale);
        var referenceLocalPosition = ReadTranslation(rig->ReferencePose.Data + boneIndex);
        var targetLocalPosition = referenceLocalPosition + parentLocalContribution;
        var targetModelPosition = parent.Value.Position
            + Vector3.Transform(Multiply(targetLocalPosition, parent.Value.Scale), parent.Value.Rotation);

        var target = initial.Value;
        target.Position = targetModelPosition;
        SetModelTransform(pose, boneIndex, target);
        Propagate(pose, boneIndex, target, initial.Value);
        return parentLocalContribution;
    }

    private static unsafe float GetCurrentTipFromMouthPlane(hkaPose* pose, TongueGeometry geometry)
    {
        var face = GetModelTransform(pose, geometry.FaceIndex);
        var tip = GetModelTransform(pose, geometry.TipIndex);
        if(face is null || tip is null)
            return float.NaN;

        var tipFace = ModelPointToLocal(tip.Value.Position, face.Value);
        return Vector3.Dot(tipFace - geometry.MouthCenter, geometry.MouthForward);
    }

    private static unsafe Transform[] BuildReferenceModelTransforms(hkaSkeleton* rig, int count)
    {
        var models = new Transform[count];
        for(var index = 0; index < count; index++)
        {
            var local = (Transform)(rig->ReferencePose.Data + index);
            var parentIndex = rig->ParentIndices[index];
            models[index] = parentIndex >= 0 && parentIndex < index
                ? Compose(models[parentIndex], local)
                : local;
        }
        return models;
    }

    private static Transform Compose(Transform parent, Transform local) => new()
    {
        Position = parent.Position + Vector3.Transform(Multiply(local.Position, parent.Scale), parent.Rotation),
        Rotation = NormalizeSafe(parent.Rotation * local.Rotation),
        Scale = Multiply(parent.Scale, local.Scale)
    };

    private static Vector3 ModelPointToLocal(Vector3 point, Transform parent)
    {
        var unrotated = Vector3.Transform(point - parent.Position, Quaternion.Inverse(parent.Rotation));
        return Divide(unrotated, parent.Scale);
    }

    private static unsafe int FindBoneIndex(hkaSkeleton* rig, int count, string boneName)
    {
        for(var index = 0; index < count; index++)
        {
            if(string.Equals(rig->Bones[index].Name.String, boneName, StringComparison.Ordinal))
                return index;
        }
        return -1;
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        if(edge1 <= edge0)
            return value >= edge1 ? 1f : 0f;

        var normalized = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return normalized * normalized * (3f - 2f * normalized);
    }

    private readonly record struct TongueGeometry(
        int FaceIndex,
        int RootIndex,
        int BodyIndex,
        int TipIndex,
        float ChainLength,
        float RestGap,
        Vector3 MouthCenter,
        Vector3 MouthForward);

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

                var parameters = dto.Data
                    .Select(ConvertParameter)
                    .Append(CreateTongueOutPresetParameter())
                    .OrderBy(parameter => parameter.Priority)
                    .ToArray();
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

    private static FacialParameter CreateTongueOutPresetParameter()
        => new(TongueOutPresetId, 21, null, new Dictionary<string, Transform>(), null);

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

    private static Vector3 Divide(Vector3 value, Vector3 divisor) => new(
        MathF.Abs(divisor.X) > 1e-6f ? value.X / divisor.X : value.X,
        MathF.Abs(divisor.Y) > 1e-6f ? value.Y / divisor.Y : value.Y,
        MathF.Abs(divisor.Z) > 1e-6f ? value.Z / divisor.Z : value.Z);

    private static Quaternion NormalizeSafe(Quaternion value)
    {
        if(!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W) || value.LengthSquared() < 1e-12f)
            return Quaternion.Identity;
        return Quaternion.Normalize(value);
    }
}
