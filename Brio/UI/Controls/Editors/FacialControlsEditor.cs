// UI behavior is adapted from Ktisis ActorPropertyList (GPL-3.0), commit
// e44fb51873119a05e4943cf08d6c124c6a9dfd04, using Brio's control style.

using Brio.Capabilities.Actor;
using Brio.Game.Facial;
using Brio.UI.Controls.Stateless;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Brio.UI.Controls.Editors;

public sealed class FacialControlsEditor
{
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
        ["LipPucker"] = "Lip Pucker"
    };

    public void Draw(string id, FacialControlCapability capability, float childHeight = 0f, bool showIdentity = true)
    {
        using(ImRaii.PushId(id))
        {
            DrawOptions(capability);
            ImGui.Spacing();

            if(!capability.IsAvailable)
            {
                ImGui.TextWrapped(capability.UnavailableReason);
                return;
            }

            if(showIdentity)
            {
                ImGui.TextDisabled($"Race/Sex {capability.RaceSexId} · Face {capability.FaceId}");
                ImGui.Spacing();
            }

            if(childHeight != 0f)
            {
                using(ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 8f))
                using(var child = ImRaii.Child("###facial_controls_child", new Vector2(0, childHeight), true))
                {
                    if(child.Success)
                        DrawParameters(capability);
                }
            }
            else
            {
                DrawParameters(capability);
            }
        }
    }

    private static void DrawOptions(FacialControlCapability capability)
    {
        var service = capability.Service;
        var combine = service.CombinePairs;
        if(ImGui.Checkbox("Combine L/R", ref combine))
            service.CombinePairs = combine;

        ImGui.SameLine();
        var link = service.LinkPairs;
        if(ImGui.Checkbox("Link L/R", ref link))
            service.LinkPairs = link;

        // A second row keeps Reset All visible in narrow actor/control panes.
        var unlock = service.UnlockSliders;
        if(ImGui.Checkbox("Unlock", ref unlock))
            service.UnlockSliders = unlock;

        ImGui.SameLine();
        var canReset = capability.GetParameters().Any(parameter => capability.GetWeight(parameter.Id) != 0f);
        if(ImBrio.FontIconButton("reset_facial_controls", FontAwesomeIcon.Undo, "Reset all facial controls", canReset))
            capability.ResetAll();
    }

    private static void DrawParameters(FacialControlCapability capability)
    {
        var parameters = capability.GetParameters().OrderBy(parameter => parameter.Priority).ToArray();
        var drawn = new HashSet<string>();

        if(!ImGui.BeginTable("###facial_controls_table", 4, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
            return;

        ImGui.TableSetupColumn("Left/Value", ImGuiTableColumnFlags.WidthStretch, 1.3f);
        ImGui.TableSetupColumn("Right", ImGuiTableColumnFlags.WidthStretch, 1.3f);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Reset", ImGuiTableColumnFlags.WidthFixed, 30f * ImGuiHelpers.GlobalScale);

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
                    DrawPairedRow(capability, parameter, pair);
                    continue;
                }
            }

            DrawSingleRow(capability, parameter);
        }

        ImGui.EndTable();
    }

    private static void DrawPairedRow(FacialControlCapability capability, FacialParameter left, FacialParameter right)
    {
        var leftWeight = capability.GetWeight(left.Id);
        var rightWeight = capability.GetWeight(right.Id);

        ImGui.TableSetColumnIndex(0);
        ImGui.SetNextItemWidth(-1);
        if(DrawWeight(capability, $"##{left.Id}_left", ref leftWeight, "%.3f L"))
        {
            capability.SetWeight(left.Id, leftWeight);
            if(capability.Service.LinkPairs)
            {
                rightWeight = leftWeight;
                capability.SetWeight(right.Id, rightWeight);
            }
        }

        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(-1);
        if(DrawWeight(capability, $"##{right.Id}_right", ref rightWeight, "%.3f R"))
        {
            capability.SetWeight(right.Id, rightWeight);
            if(capability.Service.LinkPairs)
            {
                leftWeight = rightWeight;
                capability.SetWeight(left.Id, leftWeight);
            }
        }

        ImGui.TableSetColumnIndex(2);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(GetLabel(left.Id[..^1]));

        ImGui.TableSetColumnIndex(3);
        if(ImBrio.FontIconButton($"reset_{left.Id}_{right.Id}", FontAwesomeIcon.Undo, $"Reset {GetLabel(left.Id[..^1])}", leftWeight != 0f || rightWeight != 0f))
        {
            capability.Reset(left.Id);
            capability.Reset(right.Id);
        }
    }

    private static void DrawSingleRow(FacialControlCapability capability, FacialParameter parameter)
    {
        var weight = capability.GetWeight(parameter.Id);

        ImGui.TableSetColumnIndex(0);
        ImGui.SetNextItemWidth(-1);
        if(DrawWeight(capability, $"##{parameter.Id}", ref weight, "%.3f"))
            capability.SetWeight(parameter.Id, weight);

        ImGui.TableSetColumnIndex(2);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(GetLabel(parameter.Id));

        ImGui.TableSetColumnIndex(3);
        if(ImBrio.FontIconButton($"reset_{parameter.Id}", FontAwesomeIcon.Undo, $"Reset {GetLabel(parameter.Id)}", weight != 0f))
            capability.Reset(parameter.Id);
    }

    private static bool DrawWeight(FacialControlCapability capability, string id, ref float weight, string format)
    {
        if(capability.Service.UnlockSliders)
            return ImGui.DragFloat(id, ref weight, 0.001f, 0f, 0f, format);

        return ImGui.SliderFloat(id, ref weight, 0f, 1f, format);
    }

    private static string GetLabel(string id) => Labels.GetValueOrDefault(id, id);
}
