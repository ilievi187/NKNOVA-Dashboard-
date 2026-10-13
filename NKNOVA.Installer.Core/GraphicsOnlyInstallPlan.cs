namespace NKNOVA.Installer.Core;

/// <summary>
/// Platform-neutral, read-only preparation for upstream Nolvus v6 Graphics Only.
/// Downloads, authentication, ENB and resume remain in the existing dashboard.
/// </summary>
public sealed record GraphicsOnlyInstallPlan(
    string GameDirectory,
    string InstallDirectory,
    string ArchiveDirectory,
    string EnbCode,
    string ResolutionWidth,
    string ResolutionHeight,
    string AspectRatio)
{
    public const string Variant = "Graphics Only";
    public static readonly IReadOnlyList<string> SupportedEnbs =
        Array.AsReadOnly(new[] { "CABBAGE", "KAUZ", "PICHO", "AMON" });

    public static GraphicsOnlyInstallPlan Create(
        string gameDirectory,
        string installDirectory,
        string archiveDirectory,
        string enbCode,
        int width,
        int height,
        bool instanceRegistered,
        bool variantPublished)
    {
        if (instanceRegistered)
            throw new InvalidOperationException(
                "An Awakening instance already exists. Quick install will not overwrite it.");
        if (!variantPublished)
            throw new InvalidOperationException(
                "Graphics Only is unavailable from the Nolvus API. No changes made.");
        if (!SupportedEnbs.Contains(enbCode, StringComparer.Ordinal))
            throw new ArgumentException("Unknown Nolvus v6 ENB preset.", nameof(enbCode));
        if (width < 1024 || height < 720 || width > 16384 || height > 8640)
            throw new ArgumentOutOfRangeException(nameof(width), "Unsupported screen resolution.");

        var game = Absolute(gameDirectory, nameof(gameDirectory));
        var install = Absolute(installDirectory, nameof(installDirectory));
        var archive = Absolute(archiveDirectory, nameof(archiveDirectory));
        if (!File.Exists(Path.Combine(game, "SkyrimSE.exe")))
            throw new InvalidOperationException(
                "Steam Skyrim SE/AE is not configured. Choose its folder in Dashboard settings.");
        if (File.Exists(install) || (Directory.Exists(install) &&
            Directory.EnumerateFileSystemEntries(install).Any()))
            throw new InvalidOperationException(
                "Install folder is not empty. Quick install will never overwrite it.");
        if (Overlaps(game, install) || Overlaps(game, archive) || Overlaps(install, archive))
            throw new InvalidOperationException(
                "Game, installation and download archive must have independent directories.");
        RejectLinks(install);
        RejectLinks(archive);

        var ratio = (double)width / height;
        var aspect = ratio >= 2.9 ? "32:9" : ratio >= 2.15 ? "21:9" : "16:9";
        return new(game, install, archive, enbCode,
            width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            height.ToString(System.Globalization.CultureInfo.InvariantCulture), aspect);
    }

    private static string Absolute(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Absolute directory required.", name);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool Overlaps(string left, string right)
        => Contained(left, right) || Contained(right, left);

    private static bool Contained(string candidate, string ancestor)
    {
        var relative = Path.GetRelativePath(ancestor, candidate);
        return relative == "." ||
            (!Path.IsPathRooted(relative) && relative != ".." &&
             !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
             !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static void RejectLinks(string path)
    {
        for (DirectoryInfo? dir = new(path); dir is not null; dir = dir.Parent)
            if (dir.Exists && dir.LinkTarget is not null)
                throw new InvalidOperationException(
                    "Quick install refuses symlink/junction directory: " + dir.FullName);
    }
}
