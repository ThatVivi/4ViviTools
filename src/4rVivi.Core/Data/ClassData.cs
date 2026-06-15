using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FourRVivi.Core.Data;

public sealed class ClassSkill
{
    [JsonPropertyName("aegis")] public string Aegis { get; set; } = "";
    [JsonPropertyName("id")] public int Id { get; set; }
}

public sealed class ClassDataModel
{
    [JsonPropertyName("jobs")] public List<string> Jobs { get; set; } = new();
    [JsonPropertyName("classSkills")] public Dictionary<string, List<ClassSkill>> ClassSkills { get; set; } = new();
}

/// <summary>Per-class skill lists from rAthena's skill tree. Skill icons = &lt;aegis&gt;.bmp in the GRF.</summary>
public sealed class ClassData
{
    private readonly ClassDataModel _d;
    public IReadOnlyList<string> Jobs => _d.Jobs;

    public ClassData()
    {
        _d = Load() ?? new ClassDataModel();
    }

    public IReadOnlyList<ClassSkill> SkillsFor(string job)
        => _d.ClassSkills.TryGetValue(job, out var s) ? s : new List<ClassSkill>();

    private static ClassDataModel? Load()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("classdata.json", StringComparison.OrdinalIgnoreCase));
            if (name == null) return null;
            using var s = asm.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s);
            return JsonSerializer.Deserialize<ClassDataModel>(r.ReadToEnd());
        }
        catch { return null; }
    }
}
