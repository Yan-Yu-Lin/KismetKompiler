using System.Text.Json;
using System.Text.Json.Serialization;

namespace KismetKompiler.Library.Compiler;

public sealed class AutoImportManifest
{
    public int Version { get; set; } = 1;
    public string Package { get; set; } = "/Script/FSD";
    public List<AutoImportManifestClass> Classes { get; set; } = new();

    public static AutoImportManifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<AutoImportManifest>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to load auto-import manifest: {path}");
    }

    public void Save(string path)
    {
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, this, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}

public sealed class AutoImportManifestClass
{
    public required string Package { get; set; }
    public required string Name { get; set; }
    public string ImportClassName { get; set; } = "Class";
    public bool IsStatic { get; set; }
    public List<AutoImportManifestFunction> Functions { get; set; } = new();
}

public sealed class AutoImportManifestFunction
{
    public required string Name { get; set; }
    public bool IsStatic { get; set; }
    public string CustomFlags { get; set; } = "FinalFunction";
}
