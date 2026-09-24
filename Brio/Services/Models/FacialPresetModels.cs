using Brio.Game.Facial;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Brio.Services.Models;

public sealed class FacialPresetFile
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, float> Controls { get; set; } = [];
}

public sealed class TongueProfileFile
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public float RootWeight { get; set; } = 0.05f;
    public float BodyWeight { get; set; } = 0.35f;
    public float TipWeight { get; set; } = 0.60f;
    public float VisibleExtensionL { get; set; } = 0.25f;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, TongueBoneAdjustment>? BoneAdjustments { get; set; }
}
