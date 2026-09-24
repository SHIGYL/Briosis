using Brio.Entities.Camera;
using Brio.Entities.World;
using Brio.Config;
using Brio.Game.Facial;
using Brio.Game.World;
using Brio.Services.Models;
using Dalamud.Plugin;
using MessagePack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using BrioJsonSerializer = global::Brio.Core.JsonSerializer;

namespace Brio.Services;

public enum PresetType : int
{
    Light = 1,
    Camera = 2,
    Facial = 3,
    Tongue = 4,
}

[MessagePackObject]
public class BrioPresets
{
    [Key(0)] public List<Preset> Presets { get; set; } = [];
}

[MessagePackObject]
public record class Preset
{
    [Key(0)] public int Version { get; set; } = 1;

    [Key(1)] public required string Name { get; set; }
    [Key(2)] public string? Description { get; set; }

    [Key(3)] public required string Path { get; set; }
    [Key(4)] public required PresetType Type { get; set; }
    [Key(5)] public int EntryCount { get; set; }

    [Key(6)] public DateTime? Created { get; set; }
}

public class PresetSystem
{
    private const int FormatVersion = 1;
    private static readonly byte[] Magic = "BRIOPST"u8.ToArray();

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly LightingService _lightingService;
    private readonly ConfigurationService _configurationService;

    private readonly Dictionary<PresetType, BrioPresets> _presets = [];

    public PresetSystem(IDalamudPluginInterface pluginInterface, LightingService lightingService, ConfigurationService configurationService)
    {
        _pluginInterface = pluginInterface;
        _lightingService = lightingService;
        _configurationService = configurationService;

        foreach(PresetType type in Enum.GetValues<PresetType>())
        {
            Directory.CreateDirectory(PresetSaveFolder(type));

            LoadPresetData(type);
        }
    }

    private string PresetSaveFolder(PresetType type)
        => Path.Combine(_pluginInterface.GetPluginConfigDirectory(), "Data", "Presets", type.ToString());
    private string BrioDataPath(PresetType type)
        => Path.Combine(PresetSaveFolder(type), "brio.data");

    public IReadOnlyList<Preset> GetPresets(PresetType type)
        => _presets[type].Presets;

    public Preset? FindPresetByName(PresetType type, string name, Preset? exclude = null)
    {
        var normalizedName = name.Trim();
        return _presets[type].Presets.FirstOrDefault(preset =>
            !ReferenceEquals(preset, exclude)
            && !string.Equals(preset.Path, exclude?.Path, StringComparison.OrdinalIgnoreCase)
            && string.Equals(preset.Name.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    //

    public void SaveLightPreset(string name, string? description, IReadOnlyList<LightEntity> entities)
    {
        var dtos = entities
            .Select(entity => LightDTO.ToDTO(entity, _lightingService, Vector3.Zero))
            .Where(dto => dto is not null)
            .Cast<LightDTO>()
            .ToList();

        SavePreset(PresetType.Light, name, description, dtos);
    }
    public void SaveCameraPreset(string name, string? description, IReadOnlyList<CameraEntity> entities)
    {
        var dtos = entities
            .Select(entity => new CameraDTO { CameraType = entity.CameraType, Camera = entity.VirtualCamera })
            .ToList();

        SavePreset(PresetType.Camera, name, description, dtos);
    }

    public List<LightDTO> LoadLightPreset(Preset preset)
        => LoadPreset<LightDTO>(preset);
    public List<CameraDTO> LoadCameraPreset(Preset preset)
        => LoadPreset<CameraDTO>(preset);

    public Preset? SaveFacialPreset(string name, IReadOnlyDictionary<string, float> controls)
    {
        var file = new FacialPresetFile
        {
            Version = 1,
            Name = name.Trim(),
            Controls = controls.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
        };
        return SaveJsonPreset(PresetType.Facial, file.Name, file, file.Controls.Count);
    }

    public FacialPresetFile? LoadFacialPreset(Preset preset)
        => LoadJsonPreset<FacialPresetFile>(preset, PresetType.Facial);

    public bool UpdateFacialPreset(Preset preset, IReadOnlyDictionary<string, float> controls)
    {
        if(preset.Type != PresetType.Facial)
            return false;

        var file = new FacialPresetFile
        {
            Version = 1,
            Name = preset.Name,
            Controls = controls.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
        };
        return UpdateJsonPreset(preset, file, file.Controls.Count);
    }

    public Preset? SaveTongueProfile(string name, float rootWeight, float bodyWeight, float tipWeight, float visibleExtensionL, IReadOnlyDictionary<string, TongueBoneAdjustment>? boneAdjustments = null)
    {
        var file = new TongueProfileFile
        {
            Version = 2,
            Name = name.Trim(),
            RootWeight = rootWeight,
            BodyWeight = bodyWeight,
            TipWeight = tipWeight,
            VisibleExtensionL = visibleExtensionL,
            BoneAdjustments = boneAdjustments?.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
        };
        return SaveJsonPreset(PresetType.Tongue, file.Name, file, 4 + (file.BoneAdjustments?.Count ?? 0));
    }

    public TongueProfileFile? LoadTongueProfile(Preset preset)
        => LoadJsonPreset<TongueProfileFile>(preset, PresetType.Tongue);

    public bool UpdateTongueProfile(Preset preset, float rootWeight, float bodyWeight, float tipWeight, float visibleExtensionL, IReadOnlyDictionary<string, TongueBoneAdjustment>? boneAdjustments = null)
    {
        if(preset.Type != PresetType.Tongue)
            return false;

        var file = new TongueProfileFile
        {
            Version = 2,
            Name = preset.Name,
            RootWeight = rootWeight,
            BodyWeight = bodyWeight,
            TipWeight = tipWeight,
            VisibleExtensionL = visibleExtensionL,
            BoneAdjustments = boneAdjustments?.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
        };
        return UpdateJsonPreset(preset, file, 4 + (file.BoneAdjustments?.Count ?? 0));
    }

    public bool RenamePreset(Preset preset, string name)
    {
        name = name.Trim();
        if(string.IsNullOrWhiteSpace(name))
            return false;
        if(FindPresetByName(preset.Type, name, preset) is not null)
            return false;

        try
        {
            if(preset.Type == PresetType.Facial)
            {
                var file = LoadFacialPreset(preset);
                if(file is null)
                    return false;
                file.Name = name;
                File.WriteAllText(preset.Path, BrioJsonSerializer.Serialize(file));
            }
            else if(preset.Type == PresetType.Tongue)
            {
                var file = LoadTongueProfile(preset);
                if(file is null)
                    return false;
                file.Name = name;
                File.WriteAllText(preset.Path, BrioJsonSerializer.Serialize(file));
            }

            preset.Name = name;
            SavePresetData(preset.Type);
            return true;
        }
        catch(Exception ex)
        {
            Brio.Log.Error(ex, $"Exception while renaming preset: {preset.Name}");
            return false;
        }
    }

    public Preset? GetDefaultTongueProfile()
    {
        var path = _configurationService.Configuration.Facial.DefaultTongueProfilePath;
        if(string.IsNullOrEmpty(path))
            return null;

        return GetPresets(PresetType.Tongue).FirstOrDefault(preset => string.Equals(preset.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsDefaultTongueProfile(Preset preset)
        => preset.Type == PresetType.Tongue
            && string.Equals(_configurationService.Configuration.Facial.DefaultTongueProfilePath, preset.Path, StringComparison.OrdinalIgnoreCase);

    public void SetDefaultTongueProfile(Preset? preset)
    {
        _configurationService.Configuration.Facial.DefaultTongueProfilePath = preset?.Path ?? string.Empty;
        _configurationService.Save();
    }

    //

    private void SavePreset<T>(PresetType type, string name, string? description, List<T> dtos)
    {
        var path = Path.Combine(PresetSaveFolder(type), $"{name}-{DateTime.Now:yyyy-MM-dd-hh-mm-ss}.brioprst");

        try
        {
            Brio.Log.Verbose($"saving new preset: {path}");

            byte[] bytes = Serialize(dtos);
            File.WriteAllBytes(path, bytes);

            _presets[type].Presets.Add(new Preset
            {
                Name = name,
                Path = path,
                Description = description,
                Type = type,
                EntryCount = dtos.Count,
                Created = DateTime.UtcNow
            });

            SavePresetData(type);
        }
        catch(Exception ex)
        {
            Brio.Log.Error(ex, $"Exception while saving new preset: {name}");
        }
    }
    private List<T> LoadPreset<T>(Preset preset)
        => Deserialize<T>(File.ReadAllBytes(preset.Path));
    public bool DeletePreset(Preset preset)
    {
        try
        {
            if(File.Exists(preset.Path))
                File.Delete(preset.Path);

            _presets[preset.Type].Presets.Remove(preset);

            if(preset.Type == PresetType.Tongue && IsDefaultTongueProfile(preset))
                SetDefaultTongueProfile(null);

            SavePresetData(preset.Type);
            return true;
        }
        catch(Exception ex)
        {
            Brio.Log.Error(ex, $"Exception while deleting preset: {preset.Name}");
            return false;
        }
    }

    private Preset? SaveJsonPreset<T>(PresetType type, string name, T data, int entryCount)
    {
        name = name.Trim();
        if(string.IsNullOrWhiteSpace(name) || FindPresetByName(type, name) is not null)
            return null;

        var path = Path.Combine(PresetSaveFolder(type), $"{type.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, BrioJsonSerializer.Serialize(data!));
            var preset = new Preset
            {
                Name = name,
                Path = path,
                Description = null,
                Type = type,
                EntryCount = entryCount,
                Created = DateTime.UtcNow
            };
            _presets[type].Presets.Add(preset);
            SavePresetData(type);
            return preset;
        }
        catch(Exception ex)
        {
            Brio.Log.Error(ex, $"Exception while saving new {type} preset: {name}");
            return null;
        }
    }

    private T? LoadJsonPreset<T>(Preset preset, PresetType expectedType) where T : class
    {
        if(preset.Type != expectedType)
            return null;

        try
        {
            return BrioJsonSerializer.Deserialize<T>(File.ReadAllText(preset.Path));
        }
        catch(Exception ex)
        {
            Brio.Log.Error(ex, $"Exception while loading preset: {preset.Name}");
            return null;
        }
    }

    private bool UpdateJsonPreset<T>(Preset preset, T data, int entryCount)
    {
        try
        {
            File.WriteAllText(preset.Path, BrioJsonSerializer.Serialize(data!));
            preset.EntryCount = entryCount;
            SavePresetData(preset.Type);
            return true;
        }
        catch(Exception ex)
        {
            Brio.Log.Error(ex, $"Exception while updating preset: {preset.Name}");
            return false;
        }
    }

    //

    private void LoadPresetData(PresetType type)
    {
        var path = BrioDataPath(type);

        if(File.Exists(path))
            _presets[type] = MessagePackSerializer.Deserialize<BrioPresets>(File.ReadAllBytes(path));
        else
            _presets[type] = new BrioPresets();
    }
    private void SavePresetData(PresetType type)
    {
        byte[] bytes = MessagePackSerializer.Serialize(_presets[type]);

        File.WriteAllBytes(BrioDataPath(type), bytes);
    }

    private static byte[] Serialize<T>(List<T> entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        var payload = MessagePackSerializer.Serialize(entries);

        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(payload.Length);
        writer.Write(payload);

        writer.Flush();
        return stream.ToArray();
    }
    private static List<T> Deserialize<T>(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);

        reader.ReadBytes(Magic.Length);
        _ = reader.ReadInt32();
        var length = reader.ReadInt32();
        var payload = reader.ReadBytes(length);

        return MessagePackSerializer.Deserialize<List<T>>(payload);
    }
}
