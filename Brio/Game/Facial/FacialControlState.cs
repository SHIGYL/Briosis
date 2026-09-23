// Facial action-unit state is adapted from Ktisis (GPL-3.0), commit
// e44fb51873119a05e4943cf08d6c124c6a9dfd04, and bound to Brio's actor lifecycle.

using Brio.Game.Posing.Skeletons;
using System.Collections.Generic;
using System.Linq;

namespace Brio.Game.Facial;

public sealed class FacialControlState
{
    private readonly object _sync = new();
    private readonly Dictionary<string, float> _weights = [];
    private IReadOnlyList<FacialParameter> _parameters = [];
    private Skeleton? _skeleton;

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

