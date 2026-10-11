using NKNOVA.Installer.Core;

if (args.Length < 3 || args[0] != "plan" || args[1] != "--manifest")
{
    Console.Error.WriteLine("Usage: plan --manifest PATH [--archives DIRECTORY]");
    return 2;
}
try
{
    var manifest = Manifest.Load(args[2]);
    var archiveFolder = Environment.CurrentDirectory;
    for (int i = 3; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length || args[i] != "--archives")
            throw new ArgumentException("Only --archives DIRECTORY is supported.");
        archiveFolder = args[i + 1];
    }
    var plan = InstallerPlanner.Build(manifest, LinuxProbe.Scan(manifest), archiveFolder);
    Console.WriteLine("NKNOVA installer preflight — " + manifest.DisplayName);
    foreach (var step in plan.Steps)
    {
        Console.WriteLine("[" + (step.Ready ? "READY" : "BLOCKED") + "] " + step.Id +
            " — " + step.Operation);
        if (step.Blocker is not null) Console.WriteLine("  " + step.Blocker);
    }
    Console.WriteLine("DRY RUN: no changes were made to Steam, Proton, Fluorine or Skyrim.");
    return plan.Ready ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine("NKNOVA preflight error: " + ex.Message);
    return 2;
}
