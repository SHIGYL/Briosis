// Facial action-unit state is adapted from Ktisis (GPL-3.0), commit
// e44fb51873119a05e4943cf08d6c124c6a9dfd04, and bound to Brio's actor lifecycle.

using Brio.Game.Posing.Skeletons;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Brio.Game.Facial;

public sealed class FacialControlState
{
    private readonly object _sync = new();
    private readonly Dictionary<string, float> _weights = [];
    private IReadOnlyList<FacialParameter> _parameters = [];
    private Skeleton? _skeleton;
    private bool _tongueRigDiagnosticsRequested;
    private TongueControlDebugSettings _tongueDebugSettings = new(0.05f, 0.35f, 0.60f, 0.25f);
    private TongueControlDebugInfo _tongueDebugInfo = TongueControlDebugInfo.Unavailable("Waiting for a valid tongue rig.");
    private string? _activeTongueProfilePath;
    private bool _tongueProfileInitialized;
    private readonly Dictionary<string, TongueBoneAdjustment> _tongueBoneAdjustments = new(StringComparer.Ordinal);

    public ushort RaceSexId { get; private set; }
    public byte FaceId { get; private set; }
    public bool IsAvailable { get; private set; }
    public string UnavailableReason { get; private set; } = "Waiting for a valid actor skeleton.";

    internal void Bind(Skeleton skeleton, ushort raceSexId, byte faceId, IReadOnlyList<FacialParameter> parameters)
    {
        lock(_sync)
        {
            if(ReferenceEquals(_skeleton, skeleton) && RaceSexId == raceSexId && FaceId == faceId && IsAvailable)
                return;

            _skeleton = skeleton;
            RaceSexId = raceSexId;
            FaceId = faceId;
            _parameters = parameters;
            _weights.Clear();
            foreach(var parameter in parameters)
                _weights[parameter.Id] = 0f;

            IsAvailable = parameters.Count > 0;
            UnavailableReason = IsAvailable ? string.Empty : $"No expression table exists for race/sex ID {raceSexId}.";
        }
    }

    internal void SetUnavailable(string reason, Skeleton? skeleton = null)
    {
        lock(_sync)
        {
            if(!ReferenceEquals(_skeleton, skeleton) || IsAvailable)
            {
                _weights.Clear();
                _parameters = [];
            }

            _skeleton = skeleton;
            RaceSexId = 0;
            FaceId = 0;
            IsAvailable = false;
            UnavailableReason = reason;
        }
    }

    public IReadOnlyList<FacialParameter> GetParameters()
    {
        lock(_sync)
            return _parameters;
    }

    public float GetWeight(string id)
    {
        lock(_sync)
            return _weights.GetValueOrDefault(id);
    }

    public bool SetWeight(string id, float weight)
    {
        lock(_sync)
        {
            if(!IsAvailable || !_weights.ContainsKey(id))
                return false;

            _weights[id] = weight;
            return true;
        }
    }

    public void Reset(string id)
    {
        lock(_sync)
        {
            if(_weights.ContainsKey(id))
                _weights[id] = 0f;
        }
    }

    public void ResetAll()
    {
        lock(_sync)
        {
            foreach(var id in _weights.Keys.ToArray())
                _weights[id] = 0f;
        }
    }

    public IReadOnlyDictionary<string, float> CapturePresetControls()
    {
        lock(_sync)
        {
            return _parameters
                .Select(parameter => (parameter.Id, Weight: _weights.GetValueOrDefault(parameter.Id)))
                .Where(entry => entry.Weight != 0f)
                .ToDictionary(entry => entry.Id, entry => entry.Weight, System.StringComparer.Ordinal);
        }
    }

    public void ApplyPresetControls(IReadOnlyDictionary<string, float> controls)
    {
        lock(_sync)
        {
            if(!IsAvailable)
                return;

            foreach(var id in _weights.Keys.ToArray())
                _weights[id] = 0f;

            foreach(var (id, weight) in controls)
            {
                if(_weights.ContainsKey(id) && float.IsFinite(weight))
                    _weights[id] = weight;
            }
        }
    }

    public IReadOnlyDictionary<string, TongueBoneAdjustment> GetTongueBoneAdjustments()
    {
        lock(_sync)
            return new Dictionary<string, TongueBoneAdjustment>(_tongueBoneAdjustments, StringComparer.Ordinal);
    }

    public void SetTongueBoneAdjustments(IReadOnlyDictionary<string, TongueBoneAdjustment>? adjustments)
    {
        lock(_sync)
        {
            _tongueBoneAdjustments.Clear();
            if(adjustments is null)
                return;

            foreach(var (boneName, adjustment) in adjustments)
            {
                if(boneName is not ("j_f_bero_01" or "j_f_bero_02" or "j_f_bero_03"))
                    continue;

                var position = adjustment.PositionDelta;
                var rotation = adjustment.RotationDelta;
                if(!IsFinite(position) || !IsFinite(rotation))
                    continue;

                if(rotation.LengthSquared() < 1e-12f)
                    rotation = Quaternion.Identity;
                else
                    rotation = Quaternion.Normalize(rotation);

                _tongueBoneAdjustments[boneName] = new TongueBoneAdjustment(position, rotation);
            }
        }
    }

    public void RequestTongueRigDiagnostics()
    {
        lock(_sync)
            _tongueRigDiagnosticsRequested = true;
    }

    public TongueControlDebugSettings GetTongueDebugSettings()
    {
        lock(_sync)
            return _tongueDebugSettings;
    }

    public TongueControlDebugInfo GetTongueDebugInfo()
    {
        lock(_sync)
            return _tongueDebugInfo;
    }

    public void SetTongueWeight(TongueControlBone bone, float value)
    {
        lock(_sync)
        {
            value = System.Math.Clamp(value, 0f, 1f);
            var root = _tongueDebugSettings.RootWeight;
            var body = _tongueDebugSettings.BodyWeight;
            var tip = _tongueDebugSettings.TipWeight;

            switch(bone)
            {
                case TongueControlBone.Root:
                    NormalizeOthers(value, body, tip, out body, out tip);
                    root = value;
                    break;
                case TongueControlBone.Body:
                    NormalizeOthers(value, root, tip, out root, out tip);
                    body = value;
                    break;
                case TongueControlBone.Tip:
                    NormalizeOthers(value, root, body, out root, out body);
                    tip = value;
                    break;
            }

            _tongueDebugSettings = _tongueDebugSettings with { RootWeight = root, BodyWeight = body, TipWeight = tip };
        }
    }

    public void SetTongueVisibleExtension(float value)
    {
        lock(_sync)
            _tongueDebugSettings = _tongueDebugSettings with { VisibleExtension = System.Math.Clamp(value, 0f, 1.5f) };
    }

    public void ApplyTongueSettings(TongueControlDebugSettings settings, string? profilePath)
    {
        lock(_sync)
        {
            var root = float.IsFinite(settings.RootWeight) ? System.Math.Max(0f, settings.RootWeight) : 0f;
            var body = float.IsFinite(settings.BodyWeight) ? System.Math.Max(0f, settings.BodyWeight) : 0f;
            var tip = float.IsFinite(settings.TipWeight) ? System.Math.Max(0f, settings.TipWeight) : 0f;
            var sum = root + body + tip;
            if(sum <= 1e-6f)
            {
                root = 0.05f;
                body = 0.35f;
                tip = 0.60f;
            }
            else
            {
                root /= sum;
                body /= sum;
                tip /= sum;
            }

            _tongueDebugSettings = new TongueControlDebugSettings(
                root,
                body,
                tip,
                float.IsFinite(settings.VisibleExtension) ? System.Math.Clamp(settings.VisibleExtension, 0f, 1.5f) : 0.25f);
            _activeTongueProfilePath = profilePath;
            _tongueProfileInitialized = true;
        }
    }

    public void ResetTongueSettings()
    {
        ApplyTongueSettings(new TongueControlDebugSettings(0.05f, 0.35f, 0.60f, 0.25f), null);
        SetTongueBoneAdjustments(null);
    }

    public string? GetActiveTongueProfilePath()
    {
        lock(_sync)
            return _activeTongueProfilePath;
    }

    public bool IsTongueProfileInitialized()
    {
        lock(_sync)
            return _tongueProfileInitialized;
    }

    internal void SetTongueDebugInfo(TongueControlDebugInfo info)
    {
        lock(_sync)
            _tongueDebugInfo = info;
    }

    private static void NormalizeOthers(float selected, float first, float second, out float normalizedFirst, out float normalizedSecond)
    {
        var remainder = 1f - selected;
        var sum = first + second;
        if(sum <= 1e-6f)
        {
            normalizedFirst = remainder * 0.5f;
            normalizedSecond = remainder * 0.5f;
            return;
        }

        normalizedFirst = remainder * first / sum;
        normalizedSecond = remainder * second / sum;
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

    internal bool ConsumeTongueRigDiagnosticsRequest(Skeleton skeleton)
    {
        lock(_sync)
        {
            if(!_tongueRigDiagnosticsRequested || !IsAvailable || !ReferenceEquals(_skeleton, skeleton))
                return false;

            _tongueRigDiagnosticsRequested = false;
            return true;
        }
    }

    internal IReadOnlyList<(FacialParameter Parameter, float Weight)> CaptureActiveParameters(Skeleton skeleton)
    {
        lock(_sync)
        {
            if(!IsAvailable || !ReferenceEquals(_skeleton, skeleton))
                return [];

            return [.. _parameters
                .Select(parameter => (Parameter: parameter, Weight: _weights.GetValueOrDefault(parameter.Id)))
                .Where(entry => entry.Weight != 0f)
                .OrderBy(entry => entry.Parameter.Priority)];
        }
    }
}
