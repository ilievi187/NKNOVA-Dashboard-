using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NKNOVA.Installer.Core;

/// <summary>A declarative installation manifest. Parsing never performs installation.</summary>
public sealed class Manifest
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("game")] public GameSpec Game { get; set; } = new();
    [JsonPropertyName("runtime")] public RuntimeSpec Runtime { get; set; } = new();
    [JsonPropertyName("components")] public List<ComponentSpec> Components { get; set; } = new();

    public static Manifest Load(string file)
    {
        using var input = File.OpenRead(file);
        var value = JsonSerializer.Deserialize<Manifest>(input, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow
        }) ?? throw new InvalidDataException("Empty manifest.");
        ManifestValidation.Check(value);
        return value;
    }
}
public sealed class GameSpec
{
    [JsonPropertyName("steamAppId")] public int SteamAppId { get; set; }
    [JsonPropertyName("executable")] public string Executable { get; set; } = "";
}
public sealed class RuntimeSpec
{
    [JsonPropertyName("protonVersion")] public string ProtonVersion { get; set; } = "";
    [JsonPropertyName("displayBackend")] public string DisplayBackend { get; set; } = "";
    [JsonPropertyName("modManager")] public string ModManager { get; set; } = "";
}
public sealed class ComponentSpec
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("sourceKind")] public string SourceKind { get; set; } = "";
    [JsonPropertyName("archiveFile")] public string? ArchiveFile { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("dependencies")] public List<string> Dependencies { get; set; } = new();
}
public static class ManifestValidation
{
    // \\z is a strict end-of-input anchor; $ can match before a final newline.
    private static readonly Regex IdPattern = new(@"\A[A-Za-z0-9][A-Za-z0-9_.-]*\z", RegexOptions.CultureInvariant);
    private static readonly Regex HashPattern = new(@"\A[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant);
    public static void Check(Manifest manifest)
    {
        ValidateId(manifest.Id);
        if (manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.DisplayName))
            throw new InvalidDataException("Unsupported schemaVersion or missing displayName.");
        if (manifest.Game is null || manifest.Game.SteamAppId <= 0 ||
            !SafeName(manifest.Game.Executable))
            throw new InvalidDataException("Invalid Steam game.");
        if (manifest.Runtime is null || !SafeName(manifest.Runtime.ProtonVersion) ||
            !string.Equals(manifest.Runtime.ModManager, "fluorine", StringComparison.OrdinalIgnoreCase) ||
            !new[] { "wayland", "xwayland" }.Contains(manifest.Runtime.DisplayBackend?.ToLowerInvariant()))
            throw new InvalidDataException("Invalid runtime specification.");
        if (manifest.Components is null || manifest.Components.Count == 0)
            throw new InvalidDataException("Missing components.");
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.Components)
        {
            if (item is null) throw new InvalidDataException("Null component.");
            ValidateId(item.Id);
            if (!found.Add(item.Id)) throw new InvalidDataException("Duplicate component " + item.Id);
            if (string.IsNullOrWhiteSpace(item.Version)) throw new InvalidDataException("Missing component version.");
            if (item.SourceKind != "upstream-dashboard" && item.SourceKind != "user-archive")
                throw new InvalidDataException("Unsupported component source.");
            if (item.SourceKind == "user-archive" && !SafeName(item.ArchiveFile))
                throw new InvalidDataException("Invalid user archive filename.");
            if (item.SourceKind == "upstream-dashboard" && item.ArchiveFile is not null)
                throw new InvalidDataException("Upstream component cannot specify an archive.");
            if (item.SourceKind == "upstream-dashboard" && item.Sha256 is not null)
                throw new InvalidDataException("Upstream component cannot specify an archive digest.");
            if (item.Sha256 is not null && !HashPattern.IsMatch(item.Sha256))
                throw new InvalidDataException("Invalid archive SHA256.");
            if (item.Dependencies is null) throw new InvalidDataException("Null dependency list.");
        }
        Ordered(manifest);
    }
    public static IReadOnlyList<ComponentSpec> Ordered(Manifest manifest)
    {
        var indexed = manifest.Components.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ComponentSpec>();
        void Visit(ComponentSpec item)
        {
            if (visited.Contains(item.Id)) return;
            if (!active.Add(item.Id)) throw new InvalidDataException("Dependency cycle at " + item.Id);
            foreach (var name in item.Dependencies)
            {
                if (name is null || !indexed.TryGetValue(name, out var required))
                    throw new InvalidDataException("Unknown dependency for " + item.Id + ": " + name);
                Visit(required);
            }
            active.Remove(item.Id);
            visited.Add(item.Id);
            ordered.Add(item);
        }
        foreach (var item in manifest.Components) Visit(item);
        return ordered;
    }
    private static void ValidateId(string? value)
    {
        if (value is null || !IdPattern.IsMatch(value))
            throw new InvalidDataException("Invalid ID.");
    }
    private static bool SafeName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name is not "." and not ".." &&
        name == Path.GetFileName(name) &&
        !name.Contains('/') && !name.Contains('\\') && !name.Contains(':') &&
        !name.Any(char.IsControl);
}
