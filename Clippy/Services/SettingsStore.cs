#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Clippy.Services
{
    // Portable EXE settings do not require MSIX package identity.
    internal sealed class SettingsStore
    {
        private readonly string path;
        public Dictionary<string, object?> Values { get; } = new();

        public SettingsStore(string? filePath = null)
        {
            path = filePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clippy", "settings.json");
            try
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));
                if (values == null) return;
                foreach (var item in values)
                    Values[item.Key] = item.Value.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Number => item.Value.GetInt32(),
                        JsonValueKind.String => item.Value.GetString(),
                        _ => null
                    };
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (JsonException) { Values.Clear(); }
            catch (FormatException) { Values.Clear(); }
        }

        public object Get(string name, object fallback) =>
            Values.TryGetValue(name, out var value) && value?.GetType() == fallback.GetType() ? value : fallback;

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Values));
            File.Move(temporary, path, true);
        }
    }
}
