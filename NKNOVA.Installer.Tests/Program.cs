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
