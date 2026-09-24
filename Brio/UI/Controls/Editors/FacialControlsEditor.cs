// UI behavior is adapted from Ktisis ActorPropertyList (GPL-3.0), commit
// e44fb51873119a05e4943cf08d6c124c6a9dfd04, using Brio's control style.

using Brio.Capabilities.Actor;
using Brio.Capabilities.Posing;
using Brio.Game.Facial;
using Brio.Services;
using Brio.UI.Controls.Stateless;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Brio.UI.Controls.Editors;

public sealed class FacialControlsEditor
{
    private readonly PresetSystem _presetSystem;
    private Preset? _selectedFacialPreset;
    private Preset? _selectedTongueProfile;
    private Preset? _nameTarget;
    private NameOperation _nameOperation;
    private string _presetName = string.Empty;
    private bool _focusPresetName;
    private bool _includeManualBoneAdjustments;
    private Preset? _overwriteTarget;
    private bool _openOverwriteConfirmation;
    private readonly Dictionary<PresetType, OperationStatus> _operationStatuses = [];
    private PosingCapability? _sliderHistoryOwner;
    private PosingCapability.PoseStack? _sliderHistoryBefore;
    private bool _sliderHistoryChanged;

    private const long OperationStatusDurationMs = 1750;

    private enum NameOperation
    {
        None,
        SaveFacial,
        RenameFacial,
        SaveTongue,
        RenameTongue
    }

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["BrowUpL"] = "Brow Up (L)",
        ["BrowUpR"] = "Brow Up (R)",
        ["BrowUp"] = "Brow Up",
        ["BrowFurrowL"] = "Brow Furrow (L)",
        ["BrowFurrowR"] = "Brow Furrow (R)",
        ["BrowFurrow"] = "Brow Furrow",
        ["BlinkL"] = "Blink (L)",
        ["BlinkR"] = "Blink (R)",
        ["Blink"] = "Blink",
        ["EyeWideL"] = "Eye Wide (L)",
        ["EyeWideR"] = "Eye Wide (R)",
        ["EyeWide"] = "Eye Wide",
        ["CheekRaiseL"] = "Cheek Raise (L)",
        ["CheekRaiseR"] = "Cheek Raise (R)",
        ["CheekRaise"] = "Cheek Raise",
        ["SmileL"] = "Smile (L)",
        ["SmileR"] = "Smile (R)",
        ["Smile"] = "Smile",
        ["GrinL"] = "Grin (L)",
        ["GrinR"] = "Grin (R)",
        ["Grin"] = "Grin",
        ["FrownL"] = "Frown (L)",
        ["FrownR"] = "Frown (R)",
        ["Frown"] = "Frown",
        ["JawOpen"] = "Jaw Open",
        ["UpperLipOpen"] = "Upper Lip Open",
        ["LowerLipOpen"] = "Lower Lip Open",
        ["LipPucker"] = "Lip Pucker",
        ["TongueOutPreset"] = "Tongue Out"
    };

    public FacialControlsEditor(PresetSystem presetSystem)
    {
        _presetSystem = presetSystem;
    }

    public void Draw(string id, PosingCapability posing, FacialControlCapability capability, float childHeight = 0f, Action<string>? selectBone = null)
    {
        using(ImRaii.PushId(id))
        {
            DrawOptions(posing, capability);
            ImGui.Spacing();

            if(!capability.IsAvailable)
            {
                ImGui.TextWrapped(capability.UnavailableReason);
                return;
            }

            DrawFacialPresets(posing, capability);
            ImGui.Spacing();

            if(childHeight != 0f)
            {
                using(ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 8f))
                using(var child = ImRaii.Child("###facial_controls_child", new Vector2(0, childHeight), true))
                {
                    if(child.Success)
                        DrawParameters(posing, capability, selectBone);
                }
            }
            else
            {
                DrawParameters(posing, capability, selectBone);
            }

            DrawNamePopup(capability);
            DrawOverwritePopup(capability);
        }
    }

    private static void DrawOptions(PosingCapability posing, FacialControlCapability capability)
    {
        var service = capability.Service;
        if(!ImGui.BeginTable("###facial_options", 3, ImGuiTableFlags.SizingStretchProp))
            return;

        var unlockWidth = ImGui.CalcTextSize("Unlock").X + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
        ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Unlock", ImGuiTableColumnFlags.WidthFixed, unlockWidth);
        ImGui.TableSetupColumn("Reset", ImGuiTableColumnFlags.WidthFixed, 30f * ImGuiHelpers.GlobalScale);
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        var combine = service.CombinePairs;
        if(ImGui.Checkbox("Combine L/R", ref combine))
            service.CombinePairs = combine;

        ImGui.SameLine();
        var link = service.LinkPairs;
        if(ImGui.Checkbox("Link L/R", ref link))
            service.LinkPairs = link;

        ImGui.TableSetColumnIndex(1);
        var unlock = service.UnlockSliders;
        if(ImGui.Checkbox("Unlock", ref unlock))
            service.UnlockSliders = unlock;

        ImGui.TableSetColumnIndex(2);
        var canReset = capability.GetParameters().Any(parameter => capability.GetWeight(parameter.Id) != 0f);
        if(ImBrio.FontIconButton("reset_facial_controls", FontAwesomeIcon.Undo, "Reset all facial controls", canReset))
            CommitPoseAction(posing, capability.ResetAll);

        ImGui.EndTable();
    }

    private void DrawFacialPresets(PosingCapability posing, FacialControlCapability capability)
    {
        if(!ImGui.CollapsingHeader("Facial Presets", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var presets = _presetSystem.GetPresets(PresetType.Facial).OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if(_selectedFacialPreset is not null && !presets.Any(preset => preset.Path == _selectedFacialPreset.Path))
            _selectedFacialPreset = null;

        ImGui.SetNextItemWidth(-1);
        using(var combo = ImRaii.Combo("###facial_preset", _selectedFacialPreset?.Name ?? "Select a facial preset..."))
        {
            if(combo.Success)
            {
                foreach(var preset in presets)
                {
                    if(ImGui.Selectable($"{preset.Name}###{preset.Path}", _selectedFacialPreset?.Path == preset.Path))
                        _selectedFacialPreset = preset;
                }
            }
        }
        DrawOperationStatus(PresetType.Facial);

        var halfWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        using(ImRaii.Disabled(_selectedFacialPreset is null))
        {
            if(ImGui.Button("Apply##facial_preset", new Vector2(halfWidth, 0)) && _selectedFacialPreset is not null)
            {
                var preset = _presetSystem.LoadFacialPreset(_selectedFacialPreset);
                if(preset is not null)
                    CommitPoseAction(posing, () => capability.ApplyFacialPreset(preset));
            }
        }
        ImGui.SameLine();
        if(ImGui.Button("Save Current##facial_preset", new Vector2(halfWidth, 0)))
            BeginNameOperation(NameOperation.SaveFacial);

        if(_selectedFacialPreset is not null)
        {
            if(!ImGui.BeginTable("###facial_preset_actions", 3, ImGuiTableFlags.SizingStretchSame))
                return;

            ImGui.TableNextColumn();
            if(DrawProtectedActionButton("update_facial_preset", "Update", FontAwesomeIcon.Save, "update", "facial preset", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                if(_presetSystem.UpdateFacialPreset(_selectedFacialPreset, capability.CapturePresetControls()))
                    ShowOperationStatus(PresetType.Facial, "Updated ✓");
            }

            ImGui.TableNextColumn();
            if(ImGui.Button("Rename##facial_preset", new Vector2(-1, 0)))
                BeginNameOperation(NameOperation.RenameFacial, _selectedFacialPreset);

            ImGui.TableNextColumn();
            if(DrawProtectedActionButton("delete_facial_preset", "Delete", FontAwesomeIcon.Trash, "delete", "facial preset", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                if(_presetSystem.DeletePreset(_selectedFacialPreset))
                {
                    _selectedFacialPreset = null;
                    ShowOperationStatus(PresetType.Facial, "Deleted");
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawTongueProfileSelector(PosingCapability posing, FacialControlCapability capability)
    {
        capability.EnsureDefaultTongueProfile();

        var profiles = _presetSystem.GetPresets(PresetType.Tongue).OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var activePath = capability.GetActiveTongueProfilePath();
        _selectedTongueProfile = profiles.FirstOrDefault(preset => string.Equals(preset.Path, activePath, StringComparison.OrdinalIgnoreCase));

        ImGui.SetNextItemWidth(-1);
        using(var combo = ImRaii.Combo("Profile###tongue_profile", _selectedTongueProfile?.Name ?? "Custom / built-in defaults"))
        {
            if(combo.Success)
            {
                if(ImGui.Selectable("Custom / built-in defaults###tongue_profile_custom", _selectedTongueProfile is null))
                {
                    CommitPoseAction(posing, capability.ResetTongueSettings);
                    _selectedTongueProfile = null;
                }

                foreach(var profile in profiles)
                {
                    if(ImGui.Selectable($"{profile.Name}###{profile.Path}", _selectedTongueProfile?.Path == profile.Path))
                    {
                        if(CommitPoseAction(posing, () => capability.ApplyTongueProfile(profile)))
                            _selectedTongueProfile = profile;
                    }
                }
            }
        }
        DrawOperationStatus(PresetType.Tongue);

        if(_selectedTongueProfile is not null)
        {
            var isDefault = _presetSystem.IsDefaultTongueProfile(_selectedTongueProfile);
            if(ImGui.Checkbox("Use as global default", ref isDefault))
                _presetSystem.SetDefaultTongueProfile(isDefault ? _selectedTongueProfile : null);
        }
    }

    private void DrawTongueProfileManagement(FacialControlCapability capability, TongueControlDebugSettings settings)
    {
        if(_selectedTongueProfile is null)
            return;

        if(!ImGui.BeginTable("###tongue_profile_actions", 3, ImGuiTableFlags.SizingStretchSame))
            return;

        ImGui.TableNextColumn();
        if(DrawProtectedActionButton("update_tongue_profile", "Update", FontAwesomeIcon.Save, "update", "tongue profile", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
        {
            var adjustments = CaptureOptionalTongueAdjustments(capability);
            if(_presetSystem.UpdateTongueProfile(_selectedTongueProfile, settings.RootWeight, settings.BodyWeight, settings.TipWeight, settings.VisibleExtension, adjustments))
            {
                if(_includeManualBoneAdjustments)
                    capability.ConsumeManualTongueBoneAdjustments();
                capability.ApplyTongueProfile(_selectedTongueProfile);
                ShowOperationStatus(PresetType.Tongue, "Updated ✓");
            }
        }

        ImGui.TableNextColumn();
        if(ImGui.Button("Rename##tongue_profile", new Vector2(-1, 0)))
            BeginNameOperation(NameOperation.RenameTongue, _selectedTongueProfile);

        ImGui.TableNextColumn();
        if(DrawProtectedActionButton("delete_tongue_profile", "Delete", FontAwesomeIcon.Trash, "delete", "tongue profile", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
        {
            if(_presetSystem.DeletePreset(_selectedTongueProfile))
            {
                _selectedTongueProfile = null;
                ShowOperationStatus(PresetType.Tongue, "Deleted");
            }
        }

        ImGui.EndTable();
    }

    private void BeginNameOperation(NameOperation operation, Preset? target = null)
    {
        _nameOperation = operation;
        _nameTarget = target;
        _presetName = target?.Name ?? string.Empty;
        _focusPresetName = true;
        ImGui.OpenPopup("Preset Name###facial_preset_name_popup");
    }

    private void DrawNamePopup(FacialControlCapability capability)
    {
        using var popup = ImRaii.PopupModal("Preset Name###facial_preset_name_popup", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize);
        if(!popup.Success)
            return;

        var title = _nameOperation switch
        {
            NameOperation.SaveFacial => "Save Facial Preset",
            NameOperation.RenameFacial => "Rename Facial Preset",
            NameOperation.SaveTongue => "Save Tongue Profile",
            NameOperation.RenameTongue => "Rename Tongue Profile",
            _ => "Preset Name"
        };
        ImBrio.SeparatorText(title);

        ImGui.SetNextItemWidth(320f * ImGuiHelpers.GlobalScale);
        if(_focusPresetName)
        {
            ImGui.SetKeyboardFocusHere();
            _focusPresetName = false;
        }
        var submit = ImGui.InputTextWithHint("###preset_name_input", "Enter a name...", ref _presetName, 80, ImGuiInputTextFlags.EnterReturnsTrue);

        var valid = !string.IsNullOrWhiteSpace(_presetName);
        var actionLabel = _nameOperation is NameOperation.RenameFacial or NameOperation.RenameTongue ? "Rename" : "Save";
        using(ImRaii.Disabled(!valid))
        {
            if(ImGui.Button(actionLabel, new Vector2(155f * ImGuiHelpers.GlobalScale, 0)) || (submit && valid))
                SubmitNameOperation(capability);
        }
        ImGui.SameLine();
        if(ImGui.Button("Cancel", new Vector2(155f * ImGuiHelpers.GlobalScale, 0)))
        {
            ResetNameOperation();
            ImGui.CloseCurrentPopup();
        }
    }

    private void SubmitNameOperation(FacialControlCapability capability)
    {
        var type = _nameOperation is NameOperation.SaveFacial or NameOperation.RenameFacial
            ? PresetType.Facial
            : PresetType.Tongue;
        var excluded = _nameOperation is NameOperation.RenameFacial or NameOperation.RenameTongue ? _nameTarget : null;
        var collision = _presetSystem.FindPresetByName(type, _presetName, excluded);
        if(collision is not null)
        {
            _overwriteTarget = collision;
            _openOverwriteConfirmation = true;
            ImGui.CloseCurrentPopup();
            return;
        }

        CompleteNameOperation(capability, null);
        ImGui.CloseCurrentPopup();
    }

    private void DrawOverwritePopup(FacialControlCapability capability)
    {
        if(_openOverwriteConfirmation)
        {
            ImGui.OpenPopup("Overwrite Preset###facial_preset_overwrite_popup");
            _openOverwriteConfirmation = false;
        }

        using var popup = ImRaii.PopupModal("Overwrite Preset###facial_preset_overwrite_popup", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize);
        if(!popup.Success)
            return;

        var noun = _nameOperation is NameOperation.SaveTongue or NameOperation.RenameTongue ? "tongue profile" : "facial preset";
        ImGui.TextWrapped($"A {noun} named \"{_presetName.Trim()}\" already exists.\n\nOverwrite it?");
        ImGui.Spacing();

        if(ImGui.Button("Overwrite", new Vector2(155f * ImGuiHelpers.GlobalScale, 0)))
        {
            CompleteNameOperation(capability, _overwriteTarget);
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if(ImGui.Button("Cancel", new Vector2(155f * ImGuiHelpers.GlobalScale, 0)))
        {
            ResetNameOperation();
            ImGui.CloseCurrentPopup();
        }
    }

    private void CompleteNameOperation(FacialControlCapability capability, Preset? overwriteTarget)
    {
        var name = _presetName.Trim();
        switch(_nameOperation)
        {
            case NameOperation.SaveFacial:
                var controls = capability.CapturePresetControls();
                var facialSaved = false;
                if(overwriteTarget is not null)
                {
                    facialSaved = _presetSystem.UpdateFacialPreset(overwriteTarget, controls)
                        && _presetSystem.RenamePreset(overwriteTarget, name);
                    if(facialSaved)
                        _selectedFacialPreset = overwriteTarget;
                }
                else
                {
                    _selectedFacialPreset = _presetSystem.SaveFacialPreset(name, controls);
                    facialSaved = _selectedFacialPreset is not null;
                }
                if(facialSaved)
                    ShowOperationStatus(PresetType.Facial, "Saved ✓");
                break;
            case NameOperation.RenameFacial when _nameTarget is not null:
                var facialRenamed = false;
                if(overwriteTarget is not null)
                {
                    var source = _presetSystem.LoadFacialPreset(_nameTarget);
                    if(source is not null && _presetSystem.UpdateFacialPreset(overwriteTarget, source.Controls))
                    {
                        facialRenamed = _presetSystem.RenamePreset(overwriteTarget, name)
                            && _presetSystem.DeletePreset(_nameTarget);
                        if(facialRenamed)
                            _selectedFacialPreset = overwriteTarget;
                    }
                }
                else
                {
                    facialRenamed = _presetSystem.RenamePreset(_nameTarget, name);
                    if(facialRenamed)
                        _selectedFacialPreset = _nameTarget;
                }
                if(facialRenamed)
                    ShowOperationStatus(PresetType.Facial, "Renamed ✓");
                break;
            case NameOperation.SaveTongue:
                var settings = capability.GetTongueDebugSettings();
                var adjustments = CaptureOptionalTongueAdjustments(capability);
                var tongueProfileSaved = false;
                if(overwriteTarget is not null)
                {
                    tongueProfileSaved = _presetSystem.UpdateTongueProfile(overwriteTarget, settings.RootWeight, settings.BodyWeight, settings.TipWeight, settings.VisibleExtension, adjustments)
                        && _presetSystem.RenamePreset(overwriteTarget, name);
                    if(tongueProfileSaved)
                    {
                        _selectedTongueProfile = overwriteTarget;
                    }
                }
                else
                {
                    _selectedTongueProfile = _presetSystem.SaveTongueProfile(name, settings.RootWeight, settings.BodyWeight, settings.TipWeight, settings.VisibleExtension, adjustments);
                    tongueProfileSaved = _selectedTongueProfile is not null;
                }
                if(tongueProfileSaved && _selectedTongueProfile is not null)
                {
                    if(_includeManualBoneAdjustments)
                        capability.ConsumeManualTongueBoneAdjustments();
                    capability.ApplyTongueProfile(_selectedTongueProfile);
                    ShowOperationStatus(PresetType.Tongue, "Saved ✓");
                }
                break;
            case NameOperation.RenameTongue when _nameTarget is not null:
                var tongueRenamed = false;
                if(overwriteTarget is not null)
                {
                    var source = _presetSystem.LoadTongueProfile(_nameTarget);
                    var keepDefault = _presetSystem.IsDefaultTongueProfile(_nameTarget) || _presetSystem.IsDefaultTongueProfile(overwriteTarget);
                    if(source is not null && _presetSystem.UpdateTongueProfile(overwriteTarget, source.RootWeight, source.BodyWeight, source.TipWeight, source.VisibleExtensionL, source.BoneAdjustments))
                    {
                        tongueRenamed = _presetSystem.RenamePreset(overwriteTarget, name)
                            && _presetSystem.DeletePreset(_nameTarget);
                        if(tongueRenamed)
                        {
                            _selectedTongueProfile = overwriteTarget;
                            if(keepDefault)
                                _presetSystem.SetDefaultTongueProfile(overwriteTarget);
                        }
                    }
                }
                else
                {
                    tongueRenamed = _presetSystem.RenamePreset(_nameTarget, name);
                    if(tongueRenamed)
                        _selectedTongueProfile = _nameTarget;
                }
                if(tongueRenamed)
                    ShowOperationStatus(PresetType.Tongue, "Renamed ✓");
                break;
        }

        ResetNameOperation();
    }

    private void ResetNameOperation()
    {
        _presetName = string.Empty;
        _nameOperation = NameOperation.None;
        _nameTarget = null;
        _overwriteTarget = null;
    }

    private IReadOnlyDictionary<string, TongueBoneAdjustment>? CaptureOptionalTongueAdjustments(FacialControlCapability capability)
        => _includeManualBoneAdjustments ? capability.CaptureTongueBoneAdjustments() : null;

    private void DrawParameters(PosingCapability posing, FacialControlCapability capability, Action<string>? selectBone)
    {
        var parameters = capability.GetParameters().OrderBy(parameter => parameter.Priority).ToArray();
        var drawn = new HashSet<string>();

        if(!ImGui.BeginTable("###facial_controls_table", 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
            return;

        var style = ImGui.GetStyle();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var resetWidth = 30f * ImGuiHelpers.GlobalScale;
        var desiredNameWidth = Labels.Values.Max(label => ImGui.CalcTextSize(label).X);
        var minimumControlWidth = 100f * ImGuiHelpers.GlobalScale;
        var tablePadding = (style.CellPadding.X * 6f) + style.ItemSpacing.X;
        var nameWidth = Math.Clamp(
            availableWidth - resetWidth - minimumControlWidth - tablePadding,
            42f * ImGuiHelpers.GlobalScale,
            desiredNameWidth);

        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, nameWidth);
        ImGui.TableSetupColumn("Reset", ImGuiTableColumnFlags.WidthFixed, resetWidth);

        foreach(var parameter in parameters)
        {
            if(!drawn.Add(parameter.Id))
                continue;

            ImGui.TableNextRow();
            if(capability.Service.CombinePairs && parameter.Pair is not null)
            {
                var pair = parameters.FirstOrDefault(candidate => candidate.Id == parameter.Pair);
                if(pair is not null)
                {
                    drawn.Add(pair.Id);
                    DrawPairedRow(posing, capability, parameter, pair);
                    continue;
                }
            }

            DrawSingleRow(posing, capability, parameter);
        }

        ImGui.EndTable();
        ImGui.Spacing();
        DrawTongueTool(posing, capability, selectBone);
    }

    private void DrawTongueTool(PosingCapability posing, FacialControlCapability capability, Action<string>? selectBone)
    {
        if(!ImGui.CollapsingHeader("Tongue Tool"))
            return;

        DrawTongueProfileSelector(posing, capability);
        ImBrio.SeparatorText("Calibration");

        if(ImGui.BeginTable("###tongue_calibration", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Control", ImGuiTableColumnFlags.WidthFixed, 72f * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 1f);

            var calibrationSettings = capability.GetTongueDebugSettings();
            var root = calibrationSettings.RootWeight;
            var rootChanged = DrawCalibrationRow("root", "A Root", "A / Root\nBone: j_f_bero_01\nControls the tongue root and influences the entire child chain.", ref root, 0f, 1f, "%.3f");
            TrackSliderEdit(posing, rootChanged, () => capability.SetTongueWeight(TongueControlBone.Root, root));

            calibrationSettings = capability.GetTongueDebugSettings();
            var body = calibrationSettings.BodyWeight;
            var bodyChanged = DrawCalibrationRow("body", "B Body", "B / Body\nBone: j_f_bero_02\nPrimarily controls the middle/body of the tongue and the tip.", ref body, 0f, 1f, "%.3f");
            TrackSliderEdit(posing, bodyChanged, () => capability.SetTongueWeight(TongueControlBone.Body, body));

            calibrationSettings = capability.GetTongueDebugSettings();
            var tip = calibrationSettings.TipWeight;
            var tipChanged = DrawCalibrationRow("tip", "C Tip", "C / Tip\nBone: j_f_bero_03\nControls the end/tip of the tongue.", ref tip, 0f, 1f, "%.3f");
            TrackSliderEdit(posing, tipChanged, () => capability.SetTongueWeight(TongueControlBone.Tip, tip));

            calibrationSettings = capability.GetTongueDebugSettings();
            var visibleExtension = calibrationSettings.VisibleExtension;
            var extensionChanged = DrawCalibrationRow("extension", "Extension", "Visible extension relative to the tongue's rest chain length.", ref visibleExtension, 0f, 1.5f, "%.3f L");
            TrackSliderEdit(posing, extensionChanged, () => capability.SetTongueVisibleExtension(visibleExtension));

            ImGui.EndTable();
        }

        ImGui.Checkbox("Include manual bone adjustments", ref _includeManualBoneAdjustments);
        ImBrio.AttachToolTip("Save the current manual Position and Rotation corrections for tongue bones A, B, and C with this tongue profile.");

        var settingsAfterEdit = capability.GetTongueDebugSettings();
        var halfWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        if(ImGui.Button("Save Profile", new Vector2(halfWidth, 0)))
            BeginNameOperation(NameOperation.SaveTongue);
        ImGui.SameLine();
        if(ImGui.Button("Reset", new Vector2(halfWidth, 0)))
        {
            CommitPoseAction(posing, capability.ResetTongueSettings);
            _selectedTongueProfile = null;
        }

        DrawTongueProfileManagement(capability, settingsAfterEdit);

        if(selectBone is not null)
            DrawTongueBoneButtons(selectBone);

        if(ImGui.CollapsingHeader("Advanced Debug"))
        {
            var info = capability.GetTongueDebugInfo();
            var debugSettings = capability.GetTongueDebugSettings();
            ImGui.TextDisabled($"Race/Sex: {capability.RaceSexId} · Face: {capability.FaceId}");
            ImGui.TextDisabled($"Normalized weights: {debugSettings.RootWeight:F3} + {debugSettings.BodyWeight:F3} + {debugSettings.TipWeight:F3} = {debugSettings.RootWeight + debugSettings.BodyWeight + debugSettings.TipWeight:F3}");
            if(info.IsAvailable)
            {
                ImGui.TextDisabled($"L: {info.ChainLength:F6}");
                ImGui.TextDisabled($"Rest gap: {info.RestGap:F6} ({info.RestGapRatio:F3} L)");
                ImGui.TextDisabled($"Desired visible extension: {info.DesiredVisibleExtension:F6}");
                ImGui.TextDisabled($"Total desired tip travel: {info.TotalDesiredTipTravel:F6}");
                ImGui.TextDisabled($"Current tip from mouth plane: {info.CurrentTipFromMouthPlane:F6}");
                ImGui.TextDisabled($"A local delta: ({info.RootLocalDelta.X:F6}, {info.RootLocalDelta.Y:F6}, {info.RootLocalDelta.Z:F6})");
                ImGui.TextDisabled($"B local delta: ({info.BodyLocalDelta.X:F6}, {info.BodyLocalDelta.Y:F6}, {info.BodyLocalDelta.Z:F6})");
                ImGui.TextDisabled($"C local delta: ({info.TipLocalDelta.X:F6}, {info.TipLocalDelta.Y:F6}, {info.TipLocalDelta.Z:F6})");
            }
            else
            {
                ImGui.TextDisabled(info.UnavailableReason);
            }
        }

        ImGui.Separator();
        ImGui.Spacing();
    }

    private static bool DrawCalibrationRow(string id, string label, string tooltip, ref float value, float minimum, float maximum, string format)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImBrio.AttachToolTip(tooltip);

        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(-1);
        var changed = ImGui.SliderFloat($"###{id}_tongue_calibration", ref value, minimum, maximum, format);
        ImBrio.AttachToolTip(tooltip);
        return changed;
    }

    private static void DrawTongueBoneButtons(Action<string> selectBone)
    {
        ImBrio.SeparatorText("Tongue Bones");

        var compact = ImGui.GetContentRegionAvail().X < 250f * ImGuiHelpers.GlobalScale;
        if(!ImGui.BeginTable("###tongue_bone_buttons", 3, ImGuiTableFlags.SizingStretchSame))
            return;

        DrawTongueBoneButton("A", compact ? "A" : "A Root", "j_f_bero_01", "Root", selectBone);
        DrawTongueBoneButton("B", compact ? "B" : "B Body", "j_f_bero_02", "Body", selectBone);
        DrawTongueBoneButton("C", compact ? "C" : "C Tip", "j_f_bero_03", "Tip", selectBone);

        ImGui.EndTable();
    }

    private static void DrawTongueBoneButton(string id, string label, string boneName, string role, Action<string> selectBone)
    {
        ImGui.TableNextColumn();
        if(ImGui.Button($"{label}###{id}_tongue_bone", new Vector2(-1, 0)))
            selectBone(boneName);
        ImBrio.AttachToolTip($"{id} / {role}\nBone: {boneName}\nOpen this bone in the standard Bone Editor for manual fine tuning.");
    }

    private void DrawPairedRow(PosingCapability posing, FacialControlCapability capability, FacialParameter left, FacialParameter right)
    {
        var leftWeight = capability.GetWeight(left.Id);
        var rightWeight = capability.GetWeight(right.Id);

        ImGui.TableSetColumnIndex(0);
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var controlWidth = MathF.Max(1f, (availableWidth - spacing) * 0.5f);
        ImGui.SetNextItemWidth(controlWidth);
        var leftChanged = DrawWeight(capability, left, $"##{left.Id}_left", ref leftWeight, "%.3f L");
        TrackSliderEdit(posing, leftChanged, () =>
        {
            capability.SetWeight(left.Id, leftWeight);
            if(capability.Service.LinkPairs)
            {
                rightWeight = leftWeight;
                capability.SetWeight(right.Id, rightWeight);
            }
        });

        ImGui.SameLine();
        ImGui.SetNextItemWidth(controlWidth);
        var rightChanged = DrawWeight(capability, right, $"##{right.Id}_right", ref rightWeight, "%.3f R");
        TrackSliderEdit(posing, rightChanged, () =>
        {
            capability.SetWeight(right.Id, rightWeight);
            if(capability.Service.LinkPairs)
            {
                leftWeight = rightWeight;
                capability.SetWeight(left.Id, leftWeight);
            }
        });

        ImGui.TableSetColumnIndex(1);
        DrawParameterLabel(GetLabel(left.Id[..^1]));

        ImGui.TableSetColumnIndex(2);
        if(ImBrio.FontIconButton($"reset_{left.Id}_{right.Id}", FontAwesomeIcon.Undo, $"Reset {GetLabel(left.Id[..^1])}", leftWeight != 0f || rightWeight != 0f))
        {
            CommitPoseAction(posing, () =>
            {
                capability.Reset(left.Id);
                capability.Reset(right.Id);
            });
        }
    }

    private void DrawSingleRow(PosingCapability posing, FacialControlCapability capability, FacialParameter parameter)
    {
        var weight = capability.GetWeight(parameter.Id);

        ImGui.TableSetColumnIndex(0);
        ImGui.SetNextItemWidth(-1);
        var changed = DrawWeight(capability, parameter, $"##{parameter.Id}", ref weight, "%.3f");
        TrackSliderEdit(posing, changed, () => capability.SetWeight(parameter.Id, weight));

        ImGui.TableSetColumnIndex(1);
        DrawParameterLabel(GetLabel(parameter.Id));

        ImGui.TableSetColumnIndex(2);
        if(ImBrio.FontIconButton($"reset_{parameter.Id}", FontAwesomeIcon.Undo, $"Reset {GetLabel(parameter.Id)}", weight != 0f))
            CommitPoseAction(posing, () => capability.Reset(parameter.Id));
    }

    private static bool DrawWeight(FacialControlCapability capability, FacialParameter parameter, string id, ref float weight, string format)
    {
        if(capability.Service.UnlockSliders)
            return ImGui.DragFloat(id, ref weight, 0.001f, 0f, 0f, format);

        return ImGui.SliderFloat(id, ref weight, 0f, 1f, format);
    }

    private void TrackSliderEdit(PosingCapability posing, bool changed, Action applyChange)
    {
        if(ImGui.IsItemActivated())
        {
            _sliderHistoryOwner = posing;
            _sliderHistoryBefore = posing.CaptureHistoryState();
            _sliderHistoryChanged = false;
        }

        if(changed)
        {
            applyChange();
            _sliderHistoryChanged = true;
        }

        if(!ImGui.IsItemDeactivated())
            return;

        if(_sliderHistoryChanged && ReferenceEquals(_sliderHistoryOwner, posing) && _sliderHistoryBefore is { } before)
            posing.CommitHistoryState(before);

        _sliderHistoryOwner = null;
        _sliderHistoryBefore = null;
        _sliderHistoryChanged = false;
    }

    private static void CommitPoseAction(PosingCapability posing, Action action)
    {
        var before = posing.CaptureHistoryState();
        action();
        posing.CommitHistoryState(before);
    }

    private static bool CommitPoseAction(PosingCapability posing, Func<bool> action)
    {
        var before = posing.CaptureHistoryState();
        if(!action())
            return false;

        posing.CommitHistoryState(before);
        return true;
    }

    private bool DrawProtectedActionButton(string id, string label, FontAwesomeIcon icon, string verb, string noun, Vector2 size)
    {
        using var actionId = ImRaii.PushId(id);
        var shiftHeld = ImGui.GetIO().KeyShift;
        using var disabledAlpha = ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().DisabledAlpha, !shiftHeld);
        return ImBrio.HoldButton(
            id,
            label,
            icon,
            1.0f,
            size,
            centerTest: true,
            tooltip: shiftHeld ? $"[HOLD]\n{char.ToUpperInvariant(verb[0])}{verb[1..]} {noun}" : $"Hold Shift to enable {label}",
            enabled: shiftHeld,
            allowCtrlShortcut: false);
    }

    private void ShowOperationStatus(PresetType type, string text)
        => _operationStatuses[type] = new OperationStatus(text, Environment.TickCount64 + OperationStatusDurationMs);

    private void DrawOperationStatus(PresetType type)
    {
        if(!_operationStatuses.TryGetValue(type, out var status))
            return;

        if(Environment.TickCount64 >= status.ExpiresAt)
        {
            _operationStatuses.Remove(type);
            return;
        }

        ImGui.TextColored(new Vector4(0.45f, 0.85f, 0.55f, 1f), status.Text);
    }

    private static void DrawParameterLabel(string label)
    {
        ImGui.AlignTextToFramePadding();
        var availableWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        if(ImBrio.TruncatedText(label, availableWidth))
            ImBrio.AttachToolTip(label);
    }

    private static string GetLabel(string id) => Labels.GetValueOrDefault(id, id);

    private readonly record struct OperationStatus(string Text, long ExpiresAt);
}
