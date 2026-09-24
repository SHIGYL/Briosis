# Tongue Out Control Analysis

## Stable baseline

The validated facial-controls implementation was committed as `88d2987` and
tagged `v0.8.0.11-facial.1` before this experimental control was added. That
historical development artifact is intentionally not part of the public source
or release package.

## Reference behavior

Current Anamnesis source was checked at commit
`bb79bb71bc3458be5d381193c2a6336f16135ac2`.

Anamnesis does not implement an automatic tongue-out slider. Its current
Dawntrail face UI exposes the three native tongue bones individually:

- `j_f_bero_01` — Tongue A / root
- `j_f_bero_02` — Tongue B / middle
- `j_f_bero_03` — Tongue C / tip

Relevant Anamnesis files:

- `Anamnesis/Actor/Posing/Views/PoseFaceGUIView.xaml`
- `Anamnesis/Actor/Posing/Views/PoseMatrixView.xaml`
- `Anamnesis/Core/Skeleton.cs`

The older Anamnesis documentation stating that playable characters have no
rigged tongue predates Dawntrail. Current Anamnesis explicitly uses
`j_f_bero_01` to distinguish a Dawntrail face skeleton from a legacy face.

## Axis and distance choice

The chain was checked against 60 Brio pose snapshots containing all three
bones. After transforming each child-to-parent position difference into the
parent bone's local space:

- `j_f_bero_01 -> j_f_bero_02`: local X `0.013399..0.016500`, maximum absolute
  Y/Z residual below `6.8e-7`.
- `j_f_bero_02 -> j_f_bero_03`: local X `0.013999..0.016500`, maximum absolute
  Y/Z residual below `7.8e-7`.

This proves that local `+X` is the natural forward axis of the Dawntrail tongue
chain in the inspected models. The retired `TongueForward` experiment used this
axis for a hardcoded root-only translation. It is no longer registered as a
facial parameter or applied at runtime; the normalized procedural controller
described below is the only user-facing tongue-out control.

## UI and current controller

One `Tongue Out` control is appended after `Jaw Open` in `Advanced Posing ->
Face -> Expressions. Its internal ID remains `TongueOutPreset` for preset-file
compatibility. It has a normal range of `0..1`, retains the existing Jaw
  Open, Lower Lip Open, and Upper Lip Open action units, but tongue motion is
  calculated from the live Dawntrail reference rig. The target travel is the
  rest distance to the teeth-derived mouth plane plus a configurable visible
  extension expressed in tongue-chain lengths. The face-local displacement is
  distributed over A/B/C and converted into each bone parent's local space.

The default distribution is Root `0.05`, Body `0.35`, Tip `0.60`, with visible
extension `0.25 L`. The Tongue Tool exposes these four relative tuning values,
normalizes the three weights to one, and can persist them as portable Tongue
Profiles. It never stores absolute bone positions, chain length, mouth gap, or
derived transforms. A global default profile can be selected; an actor can use
a different profile manually. Facial presets store only the high-level
`TongueOutPreset` value. Tongue Profile version 2 can optionally store
Position/Rotation delta corrections for A/B/C; those corrections blend in with
Tongue Out and are applied after the procedural controller.

Legacy facial preset files may still contain a `TongueForward` entry. Preset
deserialization keeps accepting it as an ordinary dictionary entry, while the
facial state applies only controls present in the current schema. The legacy
entry is therefore ignored without being converted to `TongueOutPreset`.
