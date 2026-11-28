using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Knossos.NET.Classes;

internal sealed class FsoOptionsJson
{
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("mod")] public string Mod { get; init; } = string.Empty;
    [JsonPropertyName("in_game_options")] public bool IngameOptions { get; init; }
    [JsonPropertyName("settings_file")] public string? SettingsFile { get; init; }
    [JsonPropertyName("settings_inherited")] public bool? SettingsInherited { get; init; }
    [JsonPropertyName("options")] public List<FsoOption> Options { get; init; } = null!;
}

internal sealed class FsoOption
{
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
    [JsonPropertyName("category")] public string Category { get; init; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; init; } = string.Empty;
    [JsonPropertyName("values")] public List<FsoOptionChoice>? Choices { get; init; }
    [JsonPropertyName("range")] public FsoOptionRange? Range { get; init; }
    [JsonPropertyName("current")] public JsonElement Current { get; init; }
    [JsonPropertyName("saved")] public JsonElement Saved { get; init; }
    [JsonPropertyName("effective")] public JsonElement Effective { get; init; }
    [JsonPropertyName("default")] public JsonElement Default { get; init; }
    [JsonPropertyName("enforced")] public bool Enforced { get; init; }
    [JsonPropertyName("overridden_by")] public string? OverriddenBy { get; init; }
    [JsonPropertyName("override_reason")] public string? OverrideReason { get; init; }
    [JsonPropertyName("requires")] public string? Requires { get; init; }
    [JsonPropertyName("requires_text")] public string? RequiresText { get; init; }
    [JsonPropertyName("requires_support")] public string? RequiresSupport { get; init; }
    [JsonPropertyName("restart_needed")] public bool RestartNeeded { get; init; }
}

public sealed class FsoOptionChoice
{
    [JsonPropertyName("value")] public JsonElement Value { get; init; }
    [JsonPropertyName("display")] public string Display { get; init; } = string.Empty;
}

internal sealed class FsoOptionRange
{
    [JsonPropertyName("min")] public decimal Min { get; init; }
    [JsonPropertyName("max")] public decimal Max { get; init; }
    [JsonPropertyName("integer")] public bool Integer { get; init; }
}

internal static class FsoOptionsFile
{
    internal static string ResolvePath(string preferencesPath, string modList)
    {
        const string globalFile = "fs2_open.ini";
        if (string.IsNullOrEmpty(modList)) return Path.Combine(preferencesPath, "data", "retail_fs2_settings.ini");

        // Match FSO's os_init_registry_stuff: use the first mod's root folder and strip
        // its version and up to five dash-delimited suffixes, including development suffixes.
        var folder = modList.Split(new[] { ',', Path.DirectorySeparatorChar }, 2)[0];
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var dash = folder.LastIndexOf('-');
            var suffix = folder[(dash + 1)..];
            if (suffix.Count(c => c == '.') >= 2 && suffix.All(c => c is '.' or >= '0' and <= '9')) break;
            if (dash >= 0) folder = folder[..dash];
        }
        var versionSeparator = folder.LastIndexOf('-');
        if (versionSeparator >= 0) folder = folder[..versionSeparator];
        if (folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException("FSO returned an invalid mod folder name for the settings file.");
        return folder.Length == 0
            ? Path.Combine(preferencesPath, globalFile)
            : Path.Combine(preferencesPath, "data", folder + "_settings.ini");
    }

    internal static bool HasValidKey(string key)
    {
        var dot = key.IndexOf('.');
        return dot > 0 && dot < key.Length - 1 && key.IndexOfAny(new[] { '\r', '\n', '[', ']', '=', ';', '#' }) < 0;
    }

    internal static void Save(string path, IReadOnlyDictionary<string, string> changes, bool? settingsInherited)
    {
        var directory = Path.GetDirectoryName(path)!;
        var source = path;
        var exists = File.Exists(path);
        if (!exists && settingsInherited != false && Path.GetFileName(path) != "fs2_open.ini")
            source = Path.Combine(Path.GetDirectoryName(directory) ?? directory, "fs2_open.ini");
        if (exists && settingsInherited == true)
        {
            // The game may have materialized the profile since the query. Check that the
            // file is writable before replacing it, since FSO also inherits from a read-only profile.
            using var writable = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        }
        var lines = File.Exists(source) ? File.ReadAllLines(source).ToList() : new List<string>();

        foreach (var (key, json) in changes)
        {
            var dot = key.IndexOf('.');
            var section = key[..dot];
            var entry = key[(dot + 1)..];
            var insertAt = -1;
            var inSection = false;
            var replaced = false;
            // FSO strips comment characters before decoding the JSON, even inside quotes.
            var value = entry + "=" + json.Replace(";", "\\u003b").Replace("#", "\\u0023");
            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                var comment = line.IndexOfAny(new[] { ';', '#' });
                var content = (comment >= 0 ? line[..comment] : line).Trim();
                if (content.StartsWith('[') && content.IndexOf(']') is var closing && closing > 1)
                {
                    inSection = content[1..closing].Equals(section, StringComparison.OrdinalIgnoreCase);
                    if (inSection && insertAt < 0) insertAt = index + 1;
                }
                else if (inSection && content.IndexOf('=') is var equals && equals > 0 && content[..equals] == entry)
                {
                    lines[index] = comment >= 0 ? value + " " + line[comment..] : value;
                    replaced = true;
                    break;
                }
            }

            if (replaced) continue;
            if (insertAt < 0)
            {
                if (lines.Count > 0 && lines[^1].Length > 0) lines.Add(string.Empty);
                lines.Add("[" + section + "]");
                insertAt = lines.Count;
            }
            lines.Insert(insertAt, value);
        }

        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllLines(temporary, lines);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
