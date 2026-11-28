using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels;

public partial class ModIngameSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly Dictionary<string, PendingProfile> pending = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Dictionary<string, bool> expansionChanges = new(StringComparer.Ordinal);
    private CancellationTokenSource? query;

    private sealed record PendingProfile(Dictionary<string, string> Changes, bool? Inherited);

    [ObservableProperty] private string buildLabel = string.Empty;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string? message;
    [ObservableProperty] private bool hasMessage;
    [ObservableProperty] private IReadOnlyList<IngameSettingsGroup> groups = Array.Empty<IngameSettingsGroup>();

    partial void OnMessageChanged(string? value) => HasMessage = !string.IsNullOrEmpty(value);

    internal async Task OpenAsync(Mod mod, FsoBuild? build)
    {
        query?.Cancel();
        using var request = new CancellationTokenSource();
        query = request;
        Message = null;
        Groups = Array.Empty<IngameSettingsGroup>();
        BuildLabel = build == null ? string.Empty : "FSO build · " + (build.directExec == null ? build.ToString() : Path.GetFileName(build.directExec));
        IsLoading = true;

        try
        {
            var executable = build?.GetGameQueryExecutable();
            if (build == null || executable == null)
            {
                Message = "Select an installed FSO build in Launch Options to configure in-game settings.";
                return;
            }

            var workingFolder = Knossos.GetModLaunchFolder(mod);
            if (!Directory.Exists(workingFolder))
            {
                Message = "Knossos could not find this mod’s game folder.";
                return;
            }

            var cmdline = Knossos.ApplyModLaunchFlags(mod.modSettings.noIngameOptions
                ? "-parse_cmdline_only -no_ingame_options" : "-parse_cmdline_only", mod, build);
            cmdline = LaunchCommandLine.SetFlag(LaunchCommandLine.SetFlag(cmdline, "-get_flags", false), "-get_options", false);
            var args = LaunchCommandLine.SplitArguments(cmdline).ToList();
            var portableMode = args.Contains("-portable_mode", StringComparer.OrdinalIgnoreCase);
            var flags = await build.GetFlagsV2Async(executable, workingFolder, portableMode, request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (flags?.caps?.Contains("GetOptions", StringComparer.Ordinal) != true)
            {
                Message = $"The selected FSO build ({build}) does not support configuring in-game settings from Knossos.";
                return;
            }

            var dependencies = mod.GetModDependencyList(false, true)?.Select(dep => dep.SelectMod()).OfType<Mod>().ToArray()
                ?? Array.Empty<Mod>();
            var modList = Knossos.GetModLaunchFlag(mod, workingFolder, dependencies);
            if (!string.IsNullOrEmpty(modList))
            {
                args.Add("-mod");
                args.Add(modList);
            }

            var result = await build.GetOptionsAsync(executable, workingFolder, args, request.Token);
            request.Token.ThrowIfCancellationRequested();
            string settingsPath;
            if (!string.IsNullOrWhiteSpace(result.SettingsFile))
            {
                settingsPath = Path.GetFullPath(result.SettingsFile, workingFolder);
            }
            else
            {
                var preferences = Path.GetFullPath(string.IsNullOrWhiteSpace(flags.pref_path)
                    ? KnUtils.GetFSODataFolderPath() : flags.pref_path, workingFolder);
                settingsPath = FsoOptionsFile.ResolvePath(preferences, result.Mod);
            }
            var changes = pending.TryGetValue(settingsPath, out var profile)
                ? profile.Changes : new Dictionary<string, string>(StringComparer.Ordinal);
            pending[settingsPath] = new PendingProfile(changes, result.SettingsInherited);

            Groups = result.Options.Where(option => FsoOptionsFile.HasValidKey(option.Key) &&
                    (option.Type == "selection" || (option.Type == "range" && option.Range is { } range && range.Min <= range.Max)))
                .GroupBy(option => string.IsNullOrWhiteSpace(option.Category) ? "General" : option.Category)
                .Select(group => new IngameSettingsGroup(group.Key,
                    group.Select(option => new IngameSetting(option, result.IngameOptions, changes)).ToArray(),
                    expansionChanges.TryGetValue(group.Key, out var expanded) ? expanded :
                        Knossos.globalSettings.expandedIngameSettingsCategories.Contains(group.Key),
                    value => expansionChanges[group.Key] = value))
                .ToArray();
            if (!result.IngameOptions) Message = "In-game options are disabled for this launch. Check Launch Options to enable them.";
            else if (Groups.Count == 0) Message = "This FSO build returned no in-game settings for the mod.";
        }
        catch (Exception ex)
        {
            if (!request.IsCancellationRequested)
            {
                Log.Add(Log.LogSeverity.Error, "ModIngameSettings.Open", ex);
                Message = $"Knossos could not load in-game settings from {build}. {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(query, request))
            {
                query = null;
                IsLoading = false;
            }
        }
    }

    internal void SaveChanges()
    {
        foreach (var (file, profile) in pending)
        {
            if (profile.Changes.Count == 0) continue;
            FsoOptionsFile.Save(file, profile.Changes, profile.Inherited);
            profile.Changes.Clear();
        }

        var categories = Knossos.globalSettings.expandedIngameSettingsCategories;
        var changed = false;
        foreach (var (category, expanded) in expansionChanges)
            changed |= expanded ? categories.Add(category) : categories.Remove(category);
        if (changed) Knossos.globalSettings.Save(false);
        expansionChanges.Clear();
    }

    internal void CancelLoad() => query?.Cancel();

    public void Dispose() => CancelLoad();
}

public sealed partial class IngameSettingsGroup : ObservableObject
{
    private readonly Action<bool> expansionChanged;
    [ObservableProperty] private bool isExpanded;

    public string Name { get; }
    public IReadOnlyList<IngameSetting> Options { get; }

    internal IngameSettingsGroup(string name, IReadOnlyList<IngameSetting> options, bool expanded, Action<bool> expansionChanged)
    {
        Name = name;
        Options = options;
        isExpanded = expanded;
        this.expansionChanged = expansionChanged;
    }

    partial void OnIsExpandedChanged(bool value) => expansionChanged(value);
}

public partial class IngameSetting : ObservableObject
{
    private readonly FsoOption option;
    private readonly Dictionary<string, string> pending;
    private readonly JsonElement original;

    [ObservableProperty] private FsoOptionChoice? selectedChoice;
    [ObservableProperty] private decimal? number;

    public string Title => option.Title;
    public string Description => option.Description;
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public bool IsSelection => option.Type == "selection";
    public bool IsRange => option.Type == "range";
    public IReadOnlyList<FsoOptionChoice> Choices { get; }
    public int ChoiceCount { get; }
    public decimal Min => option.Range?.Min ?? 0;
    public decimal Max => option.Range?.Max ?? 0;
    public decimal Increment => option.Range?.Integer == false && Max > Min ? (Max - Min) / 100 : 1;
    public NumberStyles NumberStyle => option.Range?.Integer == true ? NumberStyles.Integer : NumberStyles.Number;
    public string Format => option.Range?.Integer == true ? "0" : "0.############################";
    public string RangeLabel => option.Range?.Integer == true ? $"{Min:G}–{Max:G} · Whole numbers" : $"{Min:G}–{Max:G}";
    public bool CanEdit { get; }
    public bool CanRestoreDefault { get; }
    public string? Status { get; }
    public bool HasStatus => !string.IsNullOrEmpty(Status);
    public string? SavedValueStatus { get; }
    public bool HasSavedValueStatus => SavedValueStatus != null;

    internal IngameSetting(FsoOption option, bool enabled, Dictionary<string, string> pending)
    {
        this.option = option;
        this.pending = pending;
        var choices = option.Choices?.Where(choice => choice.Value.ValueKind != JsonValueKind.Undefined).ToList() ?? new();
        ChoiceCount = choices.Count;
        var requirementEditable = option.Requires == null || option.RequiresSupport == "supported" ||
            (option.RequiresSupport == "unknown" && (!IsSelection || ChoiceCount > 1));
        CanEdit = enabled && !option.Enforced && option.OverriddenBy == null && requirementEditable && (!IsSelection || ChoiceCount > 0);

        if (option.Effective.ValueKind != JsonValueKind.Undefined)
            original = enabled ? option.Effective : default;
        else
            original = option.Current.ValueKind == JsonValueKind.Undefined ? option.Default : option.Current;

        if (enabled && option.Saved.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) && !SameValue(option.Saved, original))
        {
            var savedChoice = choices.FirstOrDefault(choice => SameValue(choice.Value, option.Saved));
            SavedValueStatus = "Saved preference · " + (savedChoice?.Display ?? option.Saved.ToString());
        }

        var shown = original;
        if (CanEdit && pending.TryGetValue(option.Key, out var draft))
        {
            using var document = JsonDocument.Parse(draft);
            shown = document.RootElement.Clone();
        }

        if (IsSelection)
        {
            selectedChoice = choices.FirstOrDefault(choice => SameValue(choice.Value, shown));
            if (selectedChoice == null && shown.ValueKind != JsonValueKind.Undefined)
            {
                var display = shown.ValueKind == JsonValueKind.Null ? "None" : shown.ToString();
                selectedChoice = new FsoOptionChoice { Value = shown, Display = display + " (unavailable)" };
                choices.Insert(0, selectedChoice);
            }
        }
        else if (shown.ValueKind == JsonValueKind.Number && shown.TryGetDecimal(out var value) && ValidNumber(value))
        {
            number = value;
        }

        Choices = choices;
        CanRestoreDefault = CanEdit && (IsSelection
            ? choices.Skip(choices.Count - ChoiceCount).Any(choice => SameValue(choice.Value, option.Default))
            : option.Default.ValueKind == JsonValueKind.Number && option.Default.TryGetDecimal(out var defaultValue) && ValidNumber(defaultValue));
        Status = option.Enforced ? "Enforced by the mod" : option.OverriddenBy switch
        {
            "cmdline" => "Set by " + (option.OverrideReason ?? "the command line"),
            "mod" => "Set by an active mod",
            not null => option.OverrideReason ?? "Set by FSO",
            _ => option.Requires != null ? (option.RequiresText ?? option.Requires) + (option.RequiresSupport switch
                {
                    "unsupported" => " · Unsupported",
                    "unknown" => " · Support could not be verified",
                    _ => string.Empty
                })
                : IsSelection && ChoiceCount == 0 ? "No choices available"
                : option.RestartNeeded ? "Applies on the next launch" : null
        };
    }

    partial void OnSelectedChoiceChanged(FsoOptionChoice? value)
    {
        if (!CanEdit || value == null) return;
        if (SameValue(value.Value, original)) pending.Remove(option.Key);
        else if (Choices.Skip(Choices.Count - ChoiceCount).Contains(value))
            pending[option.Key] = JsonSerializer.Serialize(value.Value);
    }

    partial void OnNumberChanged(decimal? value)
    {
        if (!CanEdit || !IsRange) return;
        if (value == null)
        {
            pending.Remove(option.Key);
            return;
        }
        var edited = value.Value;
        if (!ValidNumber(edited)) return;

        if (original.ValueKind == JsonValueKind.Number && original.TryGetDecimal(out var saved) && edited == saved)
            pending.Remove(option.Key);
        else
            pending[option.Key] = edited.ToString(option.Range?.Integer == true ? "0" : "0.0###########################", CultureInfo.InvariantCulture);
    }

    private bool ValidNumber(decimal value) => value >= Min && value <= Max &&
        (option.Range?.Integer != true || decimal.Truncate(value) == value);

    [RelayCommand]
    private void RestoreDefault()
    {
        if (!CanRestoreDefault) return;
        if (IsRange && option.Default.ValueKind == JsonValueKind.Number && option.Default.TryGetDecimal(out var value)) Number = value;
        else if (IsSelection)
        {
            var choice = Choices.Skip(Choices.Count - ChoiceCount).FirstOrDefault(item => SameValue(item.Value, option.Default));
            if (choice != null) SelectedChoice = choice;
        }
    }

    private static bool SameValue(JsonElement left, JsonElement right) => left.ValueKind == right.ValueKind &&
        (left.ValueKind == JsonValueKind.Undefined || JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right));
}
