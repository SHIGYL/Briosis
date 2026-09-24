# Facial Controls Implementation

## Result

`Briosis` is a separate community fork based on Brio `v0.8.0.11` with the Ktisis facial action-unit controls integrated into Brio's actor capability and skeleton-update architecture.

- Plugin identity: `Briosis` / `Briosis`.
- Assembly: `Briosis.dll`, version `0.1.0.0`.
- Main command: `/briosis`.
- Reference behavior and data: Ktisis commit `e44fb51873119a05e4943cf08d6c124c6a9dfd04` (installed version `0.4.1.2`).
- Upstream Brio source revision: `8d45c2950f2f0c212c821a5194beb8d5a7a4a18c` (tag `v0.8.0.11`).

The original Brio and Ktisis upstream repositories were not modified. All implementation changes are in the Briosis fork.

For local testing, point Dalamud's development plugin loader at `Briosis.dll` in the Release build output. The adjacent manifest and dependencies are staged by the build.

## What was transferred and what was rewritten

Transferred directly, with attribution:

- All 18 files from Ktisis `Data/Library/Expressions`.
- Their parameter IDs, priorities, left/right pair mappings, per-bone position/rotation/scale targets, and face-specific blink variants.
- The expression interpolation, parent-space projection, contribution composition, descendant propagation, and near-zero scale clamp semantics.
- UI behavior for Combine L/R, Link L/R, locked `0..1` sliders, unlocked unbounded dragging at `0.001` speed, and zero reset.

Rewritten for Brio:

- Schema loading uses Brio embedded resources and `System.Text.Json`.
- Per-actor expression state is owned by a Brio actor capability.
- ImGui rendering is a native Brio widget rather than Ktisis/GLib property-list code.
- Native writes occur during Brio's existing skeleton update after ordinary Brio pose stacks are applied. No Ktisis pose hooks were copied.
- Pointer lifetime is tied to the currently validated Brio skeleton for the current update. Managed state does not retain raw game pointers.
- Ktisis's persistent "last contribution" state is not needed in this update path: Brio starts from the game/current pose and reapplies active facial weights each skeleton update, in priority order.

## Runtime data flow

1. `ActorEntity` creates one `FacialControlCapability` for the actor.
2. `Advanced Posing -> Face -> Expressions` displays that capability's managed weights through the reusable `FacialControlsEditor`.
3. UI interaction updates weights only; it does not write game memory.
4. During Brio's skeleton update, `SkeletonService` first applies normal Brio pose transforms.
5. `FacialControlService` validates the current human model, facial partial, Havok pose, `j_f_face`, race/sex schema, array pointers, and bounds.
6. Active parameters are applied to partial skeleton 1 in schema priority order and descendant transforms are propagated.
7. Brio then refreshes its transform cache and reparents partials as usual.

Changing skeleton, race/sex, or face ID rebinds the capability and clears its weights. Actor detach disposes the capability and clears its state. Missing or unsupported skeleton data disables the controls with an explanatory message rather than indexing invalid memory.

## UI behavior

The selected actor exposes a `Facial Controls` widget with the same 20 Ktisis action units:

- Brow Up L/R
- Brow Furrow L/R
- Blink L/R
- Eye Wide L/R
- Cheek Raise L/R
- Smile L/R
- Grin L/R
- Frown L/R
- Lip Pucker
- Upper Lip Open
- Lower Lip Open
- Jaw Open

`Combine L/R` puts a pair on one row, while `Link L/R` makes either side update both weights. `Unlock` switches from a bounded slider to an unbounded drag control. Each row has a reset action and the header has Reset All. State belongs to the actor, so selection changes show the newly selected actor's values rather than a global value set. The controls are exposed only in `Advanced Posing -> Face -> Expressions`; the duplicate actor-menu widget was removed after in-game validation.

An additional procedural `Tongue Out` control is documented in
`TONGUE_CONTROL_ANALYSIS.md`. It operates on the native Dawntrail tongue chain
and was added after the `v0.8.0.11-facial.1` stable checkpoint.

### Persistent facial presets and tongue profiles

The expression pane now extends Brio's existing `PresetSystem` with two new
preset types:

- `Facial`: versioned JSON containing only non-neutral high-level facial
  parameter IDs and weights.
- `Tongue`: versioned JSON containing normalized Root/Body/Tip weights and
  `VisibleExtensionL`. Version 2 can additionally contain optional Position and
  quaternion Rotation deltas for `j_f_bero_01/02/03`. The corrections blend in
  with Tongue Out and are applied after its procedural transform. Chain length
  and mouth gap are never serialized and are recalculated from the selected
  actor's `ReferencePose`.

The payloads are stored under the plugin config directory in
`Data/Presets/Facial` and `Data/Presets/Tongue`. Brio's normal MessagePack
`brio.data` files remain the index/metadata store. The global default tongue
profile path is stored in the normal Dalamud plugin configuration.

## Changed and new files

New implementation files:

- `Brio/Game/Facial/FacialControlModels.cs`
- `Brio/Game/Facial/FacialControlState.cs`
- `Brio/Game/Facial/FacialControlService.cs`
- `Brio/Capabilities/Actor/FacialControlCapability.cs`
- `Brio/UI/Controls/Editors/FacialControlsEditor.cs` (Advanced Posing expression-control renderer)
- `Brio/Services/Models/FacialPresetModels.cs` (versioned facial/tongue JSON payloads)
- `Brio/Config/FacialConfiguration.cs` (global default tongue-profile selection)
- `Brio/Resources/Embedded/FacialControls/*.json` (18 exact Ktisis data files)
- `Brio/Resources/Embedded/FacialControls/SOURCE.md` (data provenance and license attribution)
- `Brio/Briosis.json`
- `FACIAL_CONTROLS_ANALYSIS.md`
- `FACIAL_CONTROLS_IMPLEMENTATION.md`

Integration and identity changes:

- `Brio/Entities/Actor/ActorEntity.cs`: attaches the facial capability.
- `Brio/Game/Posing/SkeletonService.cs`: applies facial weights in the existing safe update interval.
- `Brio/UI/Windows/Specialized/PosingGraphicalWindow.cs`: adds `Bones` / `Expressions` modes to the Face page's control pane while retaining the graphical face and bone points.
- `Brio/Brio.cs`: registers the service and changes the displayed fork name.
- `Brio/Briosis.csproj`: distinct assembly/version/manifest identity.
- `Brio/Game/Chat/CommandHandlerService.cs`: changes the main command to `/briosis`.
- `Brio/Brio.json`: replaced by `Brio/Briosis.json`.
- `repo.json`, `README.md`, `Acknowledgements.md`: fork metadata and Ktisis attribution.

Build-compatibility-only changes required by the current API/toolchain:

- `Brio/UI/Windows/Specialized/PosingOverlayWindow.cs`: replaces unsupported collection-capacity preview syntax with equivalent constructors.
- `Brio/Game/Actor/ActorAppearanceService.cs`: fully qualifies the now-ambiguous FFXIVClientStructs `Human.DrawData` type.
- `Brio/Services/PathMetadataService.cs`: treats a missing user path-metadata file as a normal empty store on the first launch of the new plugin identity; non-empty corrupt files still report an error.

## Verification completed

- Exact Ktisis reference revision builds: 0 errors (existing warnings only).
- Briosis Release build: 0 errors; inherited deprecation warnings are documented in the release audit.
- All 18 copied expression files have the same SHA-256 hashes as the reference checkout.
- Every schema parses as JSON and contains 20 controls.
- The final assembly contains all 18 expression resources.
- Final assembly name/version and generated manifest are `Briosis` / `0.1.0.0` with `InternalName` `Briosis`.
- No new signature scan, hook, or numeric native-structure offset was added.

## Known limitations

- An actual FFXIV/GPose runtime was not available to this build process, so native facial movement and redraw/despawn behavior still require in-game validation.
- Like Ktisis, the UI does not reverse-engineer slider weights from an arbitrary current facial pose. It reports weights managed by this controller.
- Facial weights remain in-memory actor state and are cleared when the actor's skeleton, race/sex, or face changes. User facial presets persist those high-level weights independently of Brio pose files.
- The feature assumes the Dawntrail facial partial at index 1 and requires `j_f_face`, matching the reference Ktisis implementation.
- Brio's existing native hooks and FFXIVClientStructs layouts remain game-version-sensitive. The port adds defensive checks but cannot make an outdated base plugin compatible with a future game patch.
- Run this fork with the original Brio and Ktisis disabled. The fork has a separate Dalamud identity, but simultaneous posing hooks are not supported or tested.

## In-game test checklist

1. Disable the original Brio and Ktisis, then load `Briosis.dll` as a Dalamud development plugin.
2. Enter GPose, select a normal playable character, and verify that the facial controls become available. Race/sex and face IDs are available under `Tongue Tool -> Advanced Debug`.
3. Move every control independently and confirm that only the intended facial region/side changes.
4. Test `Combine L/R` both on and off; test `Link L/R` from both the left and right controls.
5. Confirm bounded mode stops at `0` and `1`; enable `Unlock` and test negative and greater-than-one values cautiously.
6. Test per-row reset and Reset All. Confirm the face returns on the next skeleton update without drift or accumulation.
7. Switch between two actors after setting different values and verify that each actor retains only its own state.
8. Redraw the actor, change face/race/sex where possible, despawn it, and leave/re-enter GPose. Confirm state resets safely and no stale controls write to the old actor.
9. Test all 18 playable race/sex combinations, especially Highlander faces 101-104 and Hrothgar feminine faces 5-8 for blink behavior.
10. Test an unsupported/non-human model and a model without `j_f_face`; the widget should show an unavailable reason and the game should remain stable.
11. Regress ordinary Brio behavior: body posing, freeze/unfreeze, overlay selection, actor spawning, animation playback, appearance changes, pose import/export, and plugin unload/reload.
12. Watch Dalamud logs during rapid actor switching and redraws for schema, pointer, or hook errors.

### Runtime fixes from initial GPose testing

- Facial application is explicitly restricted to `SkeletonPosingCapability.CharacterSkeleton`. Brio also registers weapon, prop, and ornament skeletons against the same actor capability; allowing those auxiliary skeletons through facial binding reset the actor's weights every frame.
- Unlock and Reset All use fixed right-side table columns, while Combine/Link use the responsive remaining width.
