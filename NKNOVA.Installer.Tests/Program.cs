using NKNOVA.Installer.Core;

var file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "../../../../install-profiles/nknova-linux.example.json"));
if (!File.Exists(file))
    file = Path.Combine(Environment.CurrentDirectory, "install-profiles/nknova-linux.example.json");
var manifest = Manifest.Load(file);
Check(manifest.Components.Count == 4, "example component count");
var order = ManifestValidation.Ordered(manifest).Select(x => x.Id).ToList();
Check(order.IndexOf("nolvus-awakening") < order.IndexOf("community-shaders"), "Nolvus before CS");
Check(order.IndexOf("community-shaders") < order.IndexOf("cs-upscaling"), "CS before upscaling");
var plan = InstallerPlanner.Build(manifest, new RuntimeState(null, null, null, false), Path.GetTempPath());
Check(!plan.Ready && plan.Steps.Any(s => s.Id == "nolvus-awakening" && !s.Ready),
    "planner must refuse unimplemented upstream adapter");
var duplicate = Manifest.Load(file);
duplicate.Components.Add(new ComponentSpec { Id = "nolvus-awakening",
    Version = "test", SourceKind = "upstream-dashboard" });
MustFail(() => ManifestValidation.Check(duplicate), "duplicate");
var traversal = Manifest.Load(file);
traversal.Components[1].ArchiveFile = "../unsafe.zip";
MustFail(() => ManifestValidation.Check(traversal), "traversal");
var cyclic = Manifest.Load(file);
cyclic.Components[0].Dependencies.Add("cs-upscaling");
MustFail(() => ManifestValidation.Check(cyclic), "cycle");
var missing = Manifest.Load(file);
missing.Components[1].Dependencies.Add("missing-dependency");
MustFail(() => ManifestValidation.Check(missing), "unknown dependency");
var digest = Manifest.Load(file);
digest.Components[1].Sha256 = "not-a-sha";
MustFail(() => ManifestValidation.Check(digest), "hash");
var upstreamDigest = Manifest.Load(file);
upstreamDigest.Components.First(x => x.SourceKind == "upstream-dashboard").Sha256 =
    new string('a', 64);
MustFail(() => ManifestValidation.Check(upstreamDigest), "upstream digest without archive");
// An archive with a declared SHA256 must not be accepted merely because it exists.
var archiveSandbox = Directory.CreateTempSubdirectory("nknova-archive-digest-");
try
{
    var hashManifest = Manifest.Load(file);
    var component = hashManifest.Components.First(x => x.SourceKind == "user-archive");
    // Isolate archive integrity from upstream adapter readiness.
    component.Dependencies.Clear();
    var archivePath = Path.Combine(archiveSandbox.FullName, component.ArchiveFile!);
    File.WriteAllText(archivePath, "synthetic authorized archive fixture");
    component.Sha256 = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archivePath)));

    var verifiedPlan = InstallerPlanner.Build(hashManifest,
        new RuntimeState(null, null, null, false), archiveSandbox.FullName);
    Check(verifiedPlan.Steps.Single(x => x.Id == component.Id).Ready,
        "matching archive SHA256 should pass");

    // The same valid archive must be blocked when its prerequisite is unavailable.
    component.Dependencies.Add("nolvus-awakening");
    var dependentPlan = InstallerPlanner.Build(hashManifest,
        new RuntimeState(null, null, null, false), archiveSandbox.FullName);
    Check(!dependentPlan.Steps.Single(x => x.Id == component.Id).Ready,
        "ready archive must be blocked by an unready prerequisite");
    component.Dependencies.Clear();

    File.AppendAllText(archivePath, "tampered");
    var rejectedPlan = InstallerPlanner.Build(hashManifest,
        new RuntimeState(null, null, null, false), archiveSandbox.FullName);
    Check(!rejectedPlan.Steps.Single(x => x.Id == component.Id).Ready,
        "modified archive must fail SHA256 preflight");
}
finally
{
    archiveSandbox.Delete(recursive: true);
}

// Read-only Steam discovery must not accept a manifest that escapes common/.
var syntheticHome = Directory.CreateTempSubdirectory("nknova-steam-probe-");
try
{
    var steamRoot = Path.Combine(syntheticHome.FullName, ".local", "share", "Steam");
    var steamapps = Path.Combine(steamRoot, "steamapps");
    var common = Path.Combine(steamapps, "common");
    Directory.CreateDirectory(common);

    var outsideGame = Path.Combine(steamRoot, "outside-game");
    Directory.CreateDirectory(outsideGame);
    File.WriteAllText(Path.Combine(outsideGame, manifest.Game.Executable), "synthetic fixture");
    var appManifest = Path.Combine(steamapps, $"appmanifest_{manifest.Game.SteamAppId}.acf");
    File.WriteAllText(appManifest, "\"installdir\" \"../../outside-game\"");

    var rejected = LinuxProbe.Scan(manifest, syntheticHome.FullName);
    Check(rejected.GamePath is null, "Steam discovery must reject escaping installdir");

    var installedGame = Path.Combine(common, "OwnedSkyrim");
    Directory.CreateDirectory(installedGame);
    File.WriteAllText(Path.Combine(installedGame, manifest.Game.Executable), "synthetic fixture");
    File.WriteAllText(appManifest, "\"installdir\" \"OwnedSkyrim\"");

    var accepted = LinuxProbe.Scan(manifest, syntheticHome.FullName);
    Check(string.Equals(accepted.GamePath, installedGame, StringComparison.Ordinal),
        "Steam discovery must accept a safe direct child of steamapps/common");
}
finally
{
    syntheticHome.Delete(recursive: true);
}

Console.WriteLine("PASS: NKNOVA manifest validation, dependency sorting, read-only plan.");
return;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
}
static void MustFail(Action fn, string message)
{
    try { fn(); }
    catch (InvalidDataException) { return; }
    throw new Exception("Expected failure: " + message);
}
