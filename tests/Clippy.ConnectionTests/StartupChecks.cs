using Microsoft.Win32;
using Clippy.Services;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
internal static class StartupChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var root = Path.Combine(Path.GetTempPath(), "Clippy-startup-" + Guid.NewGuid());
        var binary = Path.Combine(root, "Folder with spaces", "Clippy.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
        File.WriteAllBytes(binary, Array.Empty<byte>());
        var valueName = "Clippy test " + Guid.NewGuid();
        var service = new StartupService(binary, valueName);
        try
        {
            assert(!service.IsEnabled, "startup defaults off without a registry entry");
            service.SetEnabled(true);
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                assert(service.IsEnabled && (string?)key!.GetValue(valueName) == '"' + binary + '"', "startup uses current-user Run with a quoted executable path");
            service.SetEnabled(false);
            assert(!service.IsEnabled, "disabling startup removes only the owned entry");
            var scoopPath = Path.Combine(root, "apps", "clippy", "0.7.0", "Clippy.exe");
            var current = Path.Combine(root, "apps", "clippy", "current");
            var stable = Path.Combine(current, "Clippy.exe");
            var metadata = Path.Combine(current, "scoop-install.json");
            assert(StartupService.ResolveExecutable(scoopPath, p => p == stable || p == metadata) == stable, "Scoop startup targets stable current alias instead of a version directory");
            assert(StartupService.ResolveExecutable(scoopPath, _ => false) == scoopPath, "portable path retained when no Scoop metadata exists");
        }
        finally
        {
            service.SetEnabled(false);
            Directory.Delete(root, true);
        }
        Console.WriteLine("All startup checks passed.");
    }
}
