using Brio.API;
using Brio.API.Enums;
using Brio.API.Helpers;
using Brio.Services;
using Brio.Services.MediatorMessages;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Brio.IPC.API;

public class IPCProviders : MediatorSubscriberBase
{
    private static class Labels
    {
        public const string ApiVersion = "Briosis.ApiVersion";
        public const string IsAvailable = "Briosis.IsAvailable";
        public const string IsValidGPoseSession = "Briosis.IsValidGPoseSession";
        public const string Initialized = "Briosis.Initialized";
        public const string Deinitialized = "Briosis.Deinitialized";
        public const string SpawnActor = "Briosis.SpawnActor.V3";
        public const string DespawnActor = "Briosis.DespawnActor.V3";
        public const string ActorExists = "Briosis.ActorExists.V3";
        public const string GetAllActors = "Briosis.GetAllActors.V3";
        public const string LoadMCDF = "Briosis.LoadMCDF.V3";
        public const string SaveMCDF = "Briosis.SaveMCDF.V3";
        public const string ActorSpawned = "Briosis.ActorSpawned";
        public const string ActorDestroyed = "Briosis.ActorDestroyed";
        public const string SetActorSpeed = "Briosis.SetActorSpeed.V3";
        public const string GetActorSpeed = "Briosis.GetActorSpeed.V3";
        public const string FreezeActor = "Briosis.FreezeActor.V3";
        public const string UnFreezeActor = "Briosis.UnFreezeActor.V3";
        public const string FreezePhysics = "Briosis.FreezePhysics.V3";
        public const string UnFreezePhysics = "Briosis.UnFreezePhysics.V3";
        public const string SetModelTransform = "Briosis.SetModelTransform.V3";
        public const string GetModelTransform = "Briosis.GetModelTransform.V3";
        public const string ResetModelTransform = "Briosis.ResetModelTransform.V3";
        public const string LoadPoseFromFile = "Briosis.LoadPoseFromFile.V3";
        public const string LoadPoseFromJson = "Briosis.LoadPoseFromJson.V3";
        public const string GetPoseAsJson = "Briosis.GetPoseAsJson.V3";
        public const string ResetPose = "Briosis.ResetPose.V3";
    }

    private readonly List<IDisposable> _providers;

    private readonly BrioEventProvider _deinitializedProvider;
    private readonly BrioEventProvider _initializedProvider;

    public readonly BrioEventProvider<IGameObject> ActorDespawned;
    public readonly BrioEventProvider<IGameObject> ActorSpawned;

    public IPCProviders(Mediator mediator, IDalamudPluginInterface pi, BrioAPIService brioAPI) : base(mediator)
    {
        _deinitializedProvider = new BrioEventProvider(pi, Labels.Deinitialized);
        _initializedProvider = new BrioEventProvider(pi, Labels.Initialized);

        ActorDespawned = new BrioEventProvider<IGameObject>(pi, Labels.ActorDestroyed);
        ActorSpawned = new BrioEventProvider<IGameObject>(pi, Labels.ActorSpawned);

        Mediator.Subscribe<ActorSpawnedMessage>(this, (IGameObject) => ActorSpawned.Invoke(IGameObject.GameObject));
        Mediator.Subscribe<ActorDespawnedMessage>(this, (IGameObject) => ActorDespawned.Invoke(IGameObject.GameObject));

        _providers = [
            new FuncProvider<(int Breaking, int Feature)>(pi, Labels.ApiVersion, () => brioAPI.State.ApiVersion),
            new FuncProvider<bool>(pi, Labels.IsAvailable, () => brioAPI.State.IsAvailable),
            new FuncProvider<bool>(pi, Labels.IsValidGPoseSession, () => brioAPI.State.IsValidGPoseSession),

            new FuncProvider<SpawnFlags, bool, bool, IGameObject?>(pi, Labels.SpawnActor, brioAPI.Actor.Spawn),
            new FuncProvider<IGameObject, bool>(pi, Labels.DespawnActor, brioAPI.Actor.Despawn),
            new FuncProvider<IGameObject, bool>(pi, Labels.ActorExists, brioAPI.Actor.Exists),
            new FuncProvider<IGameObject[]?>(pi, Labels.GetAllActors, brioAPI.Actor.GetAllActors),
            new FuncProvider<IGameObject, string, BrioApiResult>(pi, Labels.LoadMCDF, brioAPI.Actor.LoadMCDF),
            new FuncProvider<IGameObject, string, string, BrioApiResult>(pi, Labels.SaveMCDF, brioAPI.Actor.SaveMCDF),

            new FuncProvider<IGameObject, float, bool>(pi, Labels.SetActorSpeed, brioAPI.Animation.SetActorSpeed),
            new FuncProvider<IGameObject, float>(pi, Labels.GetActorSpeed, brioAPI.Animation.GetActorSpeed),
            new FuncProvider<IGameObject, bool>(pi, Labels.FreezeActor, brioAPI.Animation.FreezeActor),
            new FuncProvider<IGameObject, bool>(pi, Labels.UnFreezeActor, brioAPI.Animation.UnFreezeActor),

            new FuncProvider<bool>(pi, Labels.FreezePhysics, brioAPI.Environment.FreezePhysics),
            new FuncProvider<bool>(pi, Labels.UnFreezePhysics, brioAPI.Environment.UnFreezePhysics),

            new FuncProvider<IGameObject, Vector3?, Quaternion?, Vector3?, bool, bool>(pi, Labels.SetModelTransform, brioAPI.Posing.SetModelTransform),
            new FuncProvider<IGameObject, (Vector3?, Quaternion?, Vector3?)>(pi, Labels.GetModelTransform, brioAPI.Posing.GetModelTransform),
            new FuncProvider<IGameObject, bool>(pi, Labels.ResetModelTransform, brioAPI.Posing.ResetModelTransform),
            new FuncProvider<IGameObject, string, bool>(pi, Labels.LoadPoseFromFile, brioAPI.Posing.LoadPoseFromFile),
            new FuncProvider<IGameObject, bool, string, bool>(pi, Labels.LoadPoseFromJson, brioAPI.Posing.LoadPoseFromJson),
            new FuncProvider<IGameObject, string?>(pi, Labels.GetPoseAsJson, brioAPI.Posing.GetPoseAsJson),
            new FuncProvider<IGameObject, bool, bool>(pi, Labels.ResetPose, brioAPI.Posing.ResetPose)
        ];

        _initializedProvider.Invoke();
    }

    public override void Dispose()
    {
        foreach(var provider in _providers)
        {
            provider.Dispose();
        }

        _initializedProvider.Dispose();
        _deinitializedProvider.Invoke();
        _deinitializedProvider.Dispose();

        ActorDespawned.Dispose();
        ActorSpawned.Dispose();

        base.Dispose();
    }
}
