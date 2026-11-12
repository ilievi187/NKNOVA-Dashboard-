using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace NKNOVA.Installer.Core
{
    public sealed record RuntimeState(
        string? GamePath,
        string? ProtonPath,
        string? FluorinePath,
        bool XWaylandFound,
        long AvailableDiskBytes);

    public sealed record PlanStep(string Id, string Operation, bool Ready, string? Blocker);

    public sealed record PreflightPlan(IReadOnlyList<PlanStep> Steps)
    {
        public bool Ready => Steps.All(s => s.Ready);
    }

    /// <summary>Read-only Linux probe: no prefixes, symlinks, or game files are modified.</summary>
    public static class LinuxProbe
    {
        public static RuntimeState Scan(Manifest manifest, string? homeDirectory = null, string? targetDrivePath = null)
        {
            var home = homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home)) throw new InvalidOperationException("Home directory is unknown.");

            var roots = new[] {
                Path.Combine(home, ".local/share/Steam"),
                Path.Combine(home, ".steam/steam"),
                Path.Combine(home, ".steam/root"),
                Path.Combine(home, ".var/app/com.valvesoftware.Steam/data/Steam")
            }.Distinct(StringComparer.Ordinal).Where(Directory.Exists).ToArray();

            string? game = null;
            foreach (var root in roots)
            {
                foreach (var library in EnumerateLibraries(root))
                {
                    var path = Path.Combine(library, "steamapps", "appmanifest_" + manifest.Game.SteamAppId + ".acf");
                    if (!File.Exists(path)) continue;

                    try
                    {
                        var text = File.ReadAllText(path);
                        var match = Regex.Match(text, "\"installdir\"\\s*\"([^\"]+)\"");
                        if (!match.Success) continue;

                        string installDirectory = match.Groups[1].Value;
                        if (!SafeSteamInstallDirectoryName(installDirectory)) continue;

                        var candidate = Path.Combine(library, "steamapps/common", installDirectory);
                        if (!File.Exists(Path.Combine(candidate, manifest.Game.Executable))) continue;

                        game = candidate;
                        break;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                if (game is not null) break;
            }

            var proton = roots.Select(r => Path.Combine(r, "compatibilitytools.d",
                manifest.Runtime.ProtonVersion, "proton")).FirstOrDefault(File.Exists);

            var fluorine = new[] {
                Path.Combine(home, ".local/share/fluorine/bin/fluorine-manager"),
                Path.Combine(home, ".local/bin/fluorine-manager")
            }.FirstOrDefault(File.Exists);

            long availableBytes = 0;
            try
            {
                var checkPath = targetDrivePath ?? home;
                if (Directory.Exists(checkPath))
                {
                    var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(checkPath)) ?? checkPath);
                    availableBytes = drive.AvailableFreeSpace;
                }
            }
            catch (Exception)
            {
                availableBytes = 0;
            }

            return new RuntimeState(game, proton, fluorine, OnPath("Xwayland"), availableBytes);
        }

        private static bool SafeSteamInstallDirectoryName(string? name) =>
            !string.IsNullOrWhiteSpace(name) &&
            name is not "." and not ".." &&
            string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal) &&
            !name.Any(c => char.IsControl(c) || c == '/' || c == '\\' || c == ':');

        private static IEnumerable<string> EnumerateLibraries(string root)
        {
            yield return root;
            var file = Path.Combine(root, "steamapps/libraryfolders.vdf");
            if (!File.Exists(file)) yield break;

            string data;
            try { data = File.ReadAllText(file); }
            catch (IOException) { yield break; }
            catch (UnauthorizedAccessException) { yield break; }

            foreach (Match match in Regex.Matches(data, "\"path\"\\s*\"([^\"]+)\""))
            {
                var path = match.Groups[1].Value.Replace("\\\\", "\\");
                if (Path.IsPathFullyQualified(path) && Directory.Exists(path)) yield return path;
            }
        }

        private static bool OnPath(string executable) =>
            (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(folder => File.Exists(Path.Combine(folder, executable)));
    }

    /// <summary>Deterministic preflight only. Evaluates execution readiness without side effects.</summary>
    public static class InstallerPlanner
    {
        public static PreflightPlan Build(Manifest manifest, RuntimeState found, string archives, long requiredDiskBytes = 0)
        {
            ManifestValidation.Check(manifest);
            if (string.IsNullOrWhiteSpace(archives))
                throw new ArgumentException("Archive directory is required.", nameof(archives));

            var steps = new List<PlanStep> {
                new("steam", "Locate owned Skyrim in Steam libraries",
                    found.GamePath is not null, found.GamePath is null ? "Skyrim not detected" : null),
                new("proton", "Locate pinned " + manifest.Runtime.ProtonVersion,
                    found.ProtonPath is not null, found.ProtonPath is null ? "Proton version missing" : null),
                new("fluorine", "Locate Fluorine Manager",
                    found.FluorinePath is not null, found.FluorinePath is null ? "Fluorine missing" : null)
            };

            if (manifest.Runtime.DisplayBackend.Equals("xwayland", StringComparison.OrdinalIgnoreCase))
            {
                steps.Add(new("xwayland", "Locate XWayland", found.XWaylandFound,
                    found.XWaylandFound ? null : "Xwayland not on PATH"));
            }

            if (requiredDiskBytes > 0)
            {
                bool spaceOk = found.AvailableDiskBytes >= requiredDiskBytes;
                string? spaceBlocker = spaceOk
                    ? null
                    : $"Insufficient disk space: {found.AvailableDiskBytes / (1024 * 1024 * 1024)} GiB available, {requiredDiskBytes / (1024 * 1024 * 1024)} GiB required";
                steps.Add(new("diskspace", "Verify destination disk space", spaceOk, spaceBlocker));
            }

            foreach (var component in ManifestValidation.Ordered(manifest))
            {
                if (component.SourceKind == "upstream-dashboard")
                {
                    steps.Add(new(component.Id, "Delegate upstream Nolvus installation", false,
                        "Upstream adapter not implemented yet"));
                    continue;
                }

                var source = Path.Combine(archives, component.ArchiveFile!);
                var exists = File.Exists(source);

                bool verified = false;
                string? blocker = !exists
                    ? "Missing user archive: " + source
                    : component.Sha256 is null
                        ? "Missing SHA256 pin for user archive: " + component.Id
                        : null;

                if (exists && component.Sha256 is not null)
                {
                    try
                    {
                        using var input = File.OpenRead(source);
                        var actual = Convert.ToHexString(SHA256.HashData(input));
                        verified = string.Equals(actual, component.Sha256, StringComparison.OrdinalIgnoreCase);
                        if (!verified) blocker = "Archive SHA256 mismatch: " + component.Id;
                    }
                    catch (IOException)
                    {
                        verified = false;
                        blocker = "Archive could not be read: " + component.Id;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        verified = false;
                        blocker = "Archive access denied: " + component.Id;
                    }
                }

                var blockedDependency = component.Dependencies.FirstOrDefault(id =>
                    steps.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase) && !s.Ready));

                if (blockedDependency is not null)
                {
                    verified = false;
                    blocker = blocker is null
                        ? "Dependency not ready: " + blockedDependency
                        : blocker + "; dependency not ready: " + blockedDependency;
                }

                steps.Add(new(component.Id, "Check authorized archive for " + component.Version, verified, blocker));
            }

            return new PreflightPlan(steps);
        }
    }
}
