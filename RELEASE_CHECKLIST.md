# Briosis Release Checklist

## Automated/local checks

- [x] Release build succeeds for `Briosis.slnx`.
- [x] Assembly name and version are `Briosis` / `0.1.0.0`.
- [x] Generated manifest uses `Name` and `InternalName` `Briosis`.
- [x] Manifest and custom repository target Dalamud API level 15.
- [x] `repo.json` metadata and release URLs match version `0.1.0.0`.
- [x] Installable ZIP contains `Briosis.dll`, `Briosis.json`, and runtime dependencies.
- [x] Installable ZIP contains no PDBs, source files, local configs, presets, or legacy `BrioFacial` binaries.
- [x] Source scan found no known token/password patterns in the publication tree.
- [x] Remove the unused Affinity source assets containing local metadata from the current tree.

## Required in-game clean-install test

1. Disable the original Brio and Ktisis plugins.
2. Back up and remove any existing **Briosis** plugin configuration directory only.
3. Confirm there is no Briosis `PathStore.user.bpath`, Facial Preset, or Tongue Profile data.
4. Install or load the freshly built `Briosis.dll` with its adjacent package files.
5. Enable Briosis and confirm the log shows `[Briosis]` with no `ERR` on first start.
6. Enter GPose and open Advanced Posing → Face → Expressions.
7. Exercise facial sliders, Tongue Out, Tongue Tool, and manual tongue-bone shortcuts.
8. Create and update a Facial Preset; confirm Update requires its confirmation interaction.
9. Create and update a Tongue Profile; confirm Update requires its confirmation interaction.
10. Exercise Undo/Redo after expression edits.
11. Restart the plugin or game and confirm configuration, presets, and profiles persist.
12. Confirm the missing/empty user PathStore initializes silently.
13. If testing corruption handling, provide a non-empty invalid PathStore and confirm a clear error is logged without crashing the plugin.
14. Confirm `/briosis` opens the plugin and the Plugin Installer identifies it separately from Brio.

## Post-publication verification

1. Fetch `https://raw.githubusercontent.com/SHIGYL/Briosis/main/repo.json` without GitHub authentication.
2. Fetch the `Briosis.zip` release asset without GitHub authentication.
3. Add the raw URL in Dalamud Settings → Experimental → Custom Plugin Repositories.
4. Verify Briosis appears in `/xlplugins`, installs, enables, and updates independently of Brio.
5. Compare the downloaded asset SHA-256 with the release report.
