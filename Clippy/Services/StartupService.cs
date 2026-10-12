#nullable enable
using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.Versioning;

namespace Clippy.Services
{
    [SupportedOSPlatform("windows")]
    internal sealed class StartupService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private readonly string executable;
        private readonly string valueName;

        public StartupService(string? executable = null, string valueName = "Clippy LAN")
        {
            this.executable = executable ?? Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Clippy.exe");
            this.valueName = valueName;
        }

        public bool IsEnabled
        {
            get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(valueName) != null; }
        }

        public void SetEnabled(bool enabled)
        {
            if (!enabled)
            {
                using var existing = Registry.CurrentUser.OpenSubKey(RunKey, true);
                existing?.DeleteValue(valueName, false);
                return;
            }
            var path = ResolveExecutable(executable, File.Exists);
            if (!File.Exists(path)) throw new FileNotFoundException("Clippy executable not found.", path);
            var command = '"' + path + '"';
            if (command.Length > 260) throw new InvalidOperationException("The startup path is too long for Windows Run.");
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(valueName, command, RegistryValueKind.String);
        }

        public static string ResolveExecutable(string executable, Func<string, bool> exists)
        {
            // A Scoop executable can report the resolved version directory. Prefer
            // the stable current alias only when it has Scoop's installation metadata.
            var directory = Path.GetDirectoryName(Path.GetFullPath(executable))!;
            var parent = Directory.GetParent(directory);
            if (parent != null && parent.Name.Equals("clippy", StringComparison.OrdinalIgnoreCase))
            {
                var current = Path.Combine(parent.FullName, "current");
                var binary = Path.Combine(current, "Clippy.exe");
                if (exists(binary) && (exists(Path.Combine(current, "scoop-install.json")) || exists(Path.Combine(current, "install.json"))))
                    return binary;
            }
            return Path.GetFullPath(executable);
        }
    }
}
