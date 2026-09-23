// Facial controls are based on Ktisis (GPL-3.0), commit
// e44fb51873119a05e4943cf08d6c124c6a9dfd04.

using Brio.Entities.Actor;
using Brio.Game.Facial;
using Brio.Game.Posing.Skeletons;
using System.Collections.Generic;

namespace Brio.Capabilities.Actor;

public sealed class FacialControlCapability : ActorCharacterCapability
{
    private readonly FacialControlService _service;

    public FacialControlState State { get; } = new();
    public FacialControlService Service => _service;

    public bool IsAvailable => State.IsAvailable;
    public string UnavailableReason => State.UnavailableReason;
    public ushort RaceSexId => State.RaceSexId;
    public byte FaceId => State.FaceId;

    public FacialControlCapability(ActorEntity parent, FacialControlService service) : base(parent)
    {
        _service = service;
    }

    public IReadOnlyList<FacialParameter> GetParameters() => State.GetParameters();
    public float GetWeight(string id) => State.GetWeight(id);
    public bool SetWeight(string id, float weight) => State.SetWeight(id, weight);
    public void Reset(string id) => State.Reset(id);
    public void ResetAll() => State.ResetAll();

    internal void UpdateAndApply(Skeleton skeleton) => _service.UpdateAndApply(State, skeleton);

    public override void Dispose()
    {
        State.SetUnavailable("The actor is no longer attached.");
        base.Dispose();
    }
}
