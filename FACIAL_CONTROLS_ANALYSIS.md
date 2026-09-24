# Facial Controls Analysis

## Scope and exact source revisions

This analysis was performed before the Brio fork implementation was changed.

- Brio source: `Etheirys/Brio`, commit `8d45c2950f2f0c212c821a5194beb8d5a7a4a18c`, tag `v0.8.0.11`.
- Installed Brio: `0.8.0.11`, `ProductVersion` `0.8.0.11+8d45c2950f2f0c212c821a5194beb8d5a7a4a18c`, Dalamud API 15.
- Ktisis reference source: `ktisis-tools/Ktisis`, commit `e44fb51873119a05e4943cf08d6c124c6a9dfd04`, with GLib submodule `e17c9609af15a24e9c9ff03929175a416834aac5`.
- Installed Ktisis: `0.4.1.2`, `ProductVersion` `0.4.1.2+e44fb51873119a05e4943cf08d6c124c6a9dfd04`, Dalamud API 15.
- Important: installed Ktisis is **not** the public `v0.4.1.2` tag. That tag points to `6f0fcf04fef28119d00645e253e6fbe3eb65d337`; the installed DLL identifies the later `e44fb518...` commit. The reference checkout uses the DLL-matching commit.

The working directory initially contained no source code. The installed DLLs/manifests were used to establish the exact revisions, then matching public GPL-3.0 source was checked out. Ktisis at the matching revision builds successfully in the supplied environment. Unmodified Brio restores but currently fails with six environment-compatibility compile errors: four uses of preview collection-capacity syntax (`[with(...)]`) and two `DrawData` ambiguity/type errors caused by the current FFXIVClientStructs surface. The upstream checkout is kept unchanged; only the fork will receive compatibility fixes.

## What the Ktisis sliders actually control

The sliders are not direct FFXIV morph, blend-shape, animation-parameter, or fixed-offset memory fields. They are a data-driven facial action-unit layer over the Dawntrail facial Havok skeleton.

The complete data flow is:

1. `ActorPropertyList.DrawExpressionsTab` renders a slider and reads `ExpressionState.Weight`.
2. `ActorPropertyList.Blend` calls `IExpressionController.ApplyBlend(id, weight)`.
3. `ExpressionController` selects the actor's render skeleton, obtains `PartialSkeletons[1].GetHavokPose(0)`, and finds each facial bone by name in `pose->Skeleton->Bones`.
4. A target local transform is read from an embedded per-race/sex expression table. Blink data is additionally selected by face ID.
5. Position, rotation, and scale are interpolated from identity to the target using the slider weight.
6. The local delta and the expression's previously applied delta are projected through the current parent model transform. The old contribution is removed and the new contribution is composed with the current model-space bone transform.
7. `HavokPosing.SetModelTransform` writes the `hkaPose.ModelPose` entry. `HavokPosing.Propagate` updates descendant facial bones.

Ktisis posing hooks freeze/suppress normal model-space recalculation while pose mode is active, so a UI-time write persists. Brio has a different posing architecture: it reapplies its own bone override stacks during the skeleton update hook. The port must therefore preserve Ktisis's data and transform math but execute it in Brio's skeleton update interval, after ordinary Brio pose transforms, rather than copy Ktisis's entire hook set.

No facial-specific signature or hardcoded structure offset is used by the Ktisis expression controller. It uses typed FFXIVClientStructs pointers (`Human`, render `Skeleton`, `hkaPose`) and Ktisis's existing posing hooks. The data tables contain bone names and transform values, not memory addresses.

## Ktisis implementation map

### UI

- `Ktisis/Interface/Editor/Properties/ActorPropertyList.cs`
  - Adds the Expressions tab only when the controller has entries.
  - Provides Combine, Link, Unlock, and Reset controls.
  - Uses `SliderFloat` with `0.0..1.0` when locked and unbounded `DragFloat` with speed `0.001` when unlocked.
  - Combines paired left/right units into a two-column row and optionally writes the same weight to both sides.
  - Disables controls unless pose mode is enabled and a Dawntrail face bone (`j_f_face`) exists.
  - Creates undo mementos while the slider is dragged.

### State and orchestration

- `Ktisis/Editor/Expressions/ExpressionManager.cs`
  - Loads schemas once, creates/removes per-pose controllers, and resets controller blend state when posing is disabled.
- `Ktisis/Editor/Expressions/Handlers/ExpressionController.cs`
  - Owns per-actor slider/blend state, detects race/sex and face changes, selects expression tables, performs interpolation and Havok writes, and implements reset.
- `Ktisis/Editor/Expressions/State/ExpressionState.cs`
  - Stores the current UI weight and the last per-bone contribution used to replace a blend without accumulating it.
- `Ktisis/Editor/Expressions/Types/IExpressionManager.cs`
- `Ktisis/Editor/Expressions/Types/IExpressionController.cs`
- `Ktisis/Editor/Expressions/Types/ExpressionMemento.cs`
  - Public backend contracts and undo/redo state.

### Data loading and schemas

- `Ktisis/Data/Expressions/ExpressionData.cs`
- `Ktisis/Data/Expressions/ExpressionsSchema.cs`
- `Ktisis/Data/Expressions/ExpressionsSchemaFile.cs`
- `Ktisis/Data/Serialization/ExpressionReader.cs`
- `Ktisis/Data/Serialization/SchemaReader.cs`
  - Load embedded JSON tables keyed by `Human.RaceSexId`.
- `Ktisis/Data/Library/Expressions/*.json`
  - Eighteen race/sex tables, one for every playable race/sex combination.
  - Each table defines 20 action units and approximately 55 distinct Dawntrail facial bones.
  - Blink L/R contains face-specific transforms; other units use a common transform set for that race/sex.
- `Ktisis/Common/Utility/Transform.cs` and Ktisis JSON numeric converters
  - Transform representation and JSON conversion.

### Posing/native layer and lifecycle

- `Ktisis/Editor/Posing/HavokPosing.cs`
  - Model/local transform access, writes, parent projection, and descendant propagation.
- `Ktisis/Editor/Posing/PosingModule.cs`
  - Pose-freeze hooks and skeleton restoration. The facial port does not copy these hooks because Brio already owns equivalent posing hooks.
- `Ktisis/Scene/Entities/Skeleton/EntityPose.cs`
  - Calls expression-controller update, exposes `HasDTFace`, and destroys the controller with the pose entity.
- `Ktisis/Scene/Factory/Builders/PoseBuilder.cs`
  - Creates one expression controller per actor pose and binds it to that actor's skeleton abstraction.
- `Ktisis/Editor/Posing/PosingManager.cs`
  - Initializes expression data; clears expression state when posing stops or a face pose/reference pose is loaded.

`Ktisis/Data/Generation/FaceLibraryGenerator.cs` generated the shipped tables from known facial animation timelines. It is developer tooling and is not required at runtime.

## Parameters and behavior

All 18 tables expose the same ordered units:

| Priority | Unit | Pair |
|---:|---|---|
| 0-1 | BrowUpL / BrowUpR | paired |
| 2-3 | BrowFurrowL / BrowFurrowR | paired |
| 4-5 | BlinkL / BlinkR | paired and face-specific |
| 6-7 | EyeWideL / EyeWideR | paired |
| 8-9 | CheekRaiseL / CheekRaiseR | paired |
| 10-11 | SmileL / SmileR | paired |
| 12-13 | GrinL / GrinR | paired |
| 14-15 | FrownL / FrownR | paired |
| 16 | LipPucker | none |
| 17 | UpperLipOpen | none |
| 18 | LowerLipOpen | none |
| 19 | JawOpen | none |

- Default/reset value: `0.0`.
- Normal range: `0.0..1.0`.
- Unlock mode: unbounded `DragFloat`; negative and greater-than-one extrapolation are intentional Ktisis behavior.
- Grouping: Ktisis has a single Expressions tab. Its only functional grouping is consolidation of paired left/right units into one row. There are no separate brow/eye/mouth backend groups.
- Current-value retrieval: Ktisis does not infer weights from the current bone pose. Weights start at zero when the controller is loaded and then reflect only changes made through the expression controller.
- Reset: replaces every active contribution with zero; state-only reset is also used when another face pose operation has already replaced the skeleton.
- Actor change: state belongs to each actor's `EntityPose`; selecting another actor displays that actor's controller.
- Race/face change: `ExpressionController.Update` reads `Human.RaceSexId` and `Human.Customize.Face` and reloads state when either changes.
- Race/model handling: schemas exist only for the 18 playable human race/sex IDs. Non-human models have no `Human` owner/schema. The UI also requires the Dawntrail `j_f_face` bone.
- Face-specific handling: blink tables use face IDs 1-4 normally, 101-104 for Highlander, and 5-8 for Hrothgar feminine. If an exact face key is absent, Ktisis falls back to the first available entry.
- Freeze: facial controls depend on Ktisis pose mode but do not own a separate facial freeze. Disabling pose mode resets the controller's weight/last-contribution state.

## Brio architecture and integration points

- Selected actor: `EntityManager.SelectedEntity`; actor entities are `Brio.Entities.Actor.ActorEntity` and retain their Dalamud `IGameObject`.
- Actor lifetime: `EntityActorManager` attaches/detaches `ActorEntity`; capabilities are created in `ActorEntity.OnAttached` and disposed by `Entity.ClearCapabilities` on detach.
- Native access: `GameObjectExtensions.Native`, `CharacterExtensions.GetCharacterBase/GetHuman`, `BrioCharacterBase`, and FFXIVClientStructs.
- Skeleton lifecycle: `SkeletonService` caches render skeletons, invalidates them from `ObjectMonitorService` destruction/material-update events, and maps them to `SkeletonPosingCapability` every update interval.
- Brio skeleton abstraction: `Skeleton`, `PartialSkeleton`, and `Bone`; `PartialSkeleton.GetBestPose()` gives the typed `hkaPose` already used by Brio.
- Posing: `SkeletonService.ApplyBrioTransforms` applies persistent `PoseInfo` stacks during the hooked skeleton update. Facial blending should be an additional layer after ordinary Brio transforms and before transform caching/reparenting.
- UI: actor-specific panels are capability widgets. A `FacialControlCapability` with a `FacialControlsWidget` integrates naturally and automatically follows selected actor state.
- Update/render loop: native mutation occurs in `SkeletonService`'s render/skeleton hook; ImGui only changes managed weights.

## Proposed port architecture

```text
ActorEntity
  -> FacialControlCapability (per-actor weights, race/face/skeleton identity)
      -> FacialControlService (embedded Ktisis schemas + blend application)
          -> Brio Skeleton / PartialSkeleton / Bone
              -> FFXIVClientStructs hkaPose.ModelPose

FacialControlsWidget
  -> FacialControlCapability.SetWeight / Reset
```

The service will load an immutable schema once. Each actor capability owns only managed state and never caches a raw actor or skeleton pointer. `SkeletonService` will ask the capability/service to apply the current weights to the skeleton object that Brio has validated for that frame. A skeleton/race/face change reloads state, so redraw/despawn cannot leave a stale pointer or an old blend contribution.

The Ktisis JSON tables can be transferred directly (with attribution). Ktisis UI code, dependency injection, entity system, undo framework, GLib widgets, and posing hook module must be rewritten for Brio. The transform interpolation/projection/propagation logic is adapted closely, using Brio's types and lifecycle.

## Version-sensitive dependencies and crash points

- FFXIVClientStructs layouts for `Human`, render `Skeleton`, `PartialSkeleton`, and `hkaPose` are game-version-sensitive. No new numeric offsets are introduced; the port uses the same typed fields as matching Ktisis and existing Brio.
- Existing Brio skeleton hook signatures remain version-sensitive. Facial controls add no new signature scan or hook.
- The face partial assumption (`partial 1`) is proved by Ktisis and Dawntrail tables, but must still be checked against `PartialSkeletonCount`, null pose/skeleton/data pointers, and bone-array bounds each frame.
- Actor `IGameObject.Address`, draw object, model type, render skeleton, and Havok arrays may become invalid during despawn/redraw. Native work must be performed only with the currently validated Brio `Skeleton` supplied during its update interval.
- Bone names in modded/non-Dawntrail/custom skeletons may be absent. Missing bones must be skipped, not indexed blindly.
- A face ID may not exist in blink data. Match Ktisis by falling back to the first available face variant.
- Zero-scale Customize+ bones can make propagation unstable. Match Ktisis's small absolute scale clamp during descendant propagation.
- Invalid quaternion data can produce NaN. Normalize only valid quaternions and skip/log malformed resource entries at schema load.
- Applying both Ktisis and Brio simultaneously would install conflicting posing hooks. Test with original Brio and Ktisis disabled.

## Fork identity plan

The fork keeps the `Brio` namespaces to minimize the diff but changes its assembly/plugin identity to `Briosis`, uses a distinct manifest name/internal name and command, and emits a separate DLL/config identity. It is intended to replace Brio while active; simultaneous hook operation is not supported.
