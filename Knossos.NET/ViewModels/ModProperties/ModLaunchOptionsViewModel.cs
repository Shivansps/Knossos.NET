using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    /// <summary>Owns per-mod launch options; engine metadata is queried only when flags are expanded.</summary>
    public partial class ModLaunchOptionsViewModel : ViewModelBase, IDisposable
    {
        private readonly Mod mod;
        private readonly ModCardViewModel? card;
        private readonly ModPropertiesViewModel? owner;
        private readonly ModLaunchOptionsServices services;
        private readonly List<FsoBuild> installedBuilds;
        private readonly List<LaunchFlagViewModel> allFlags = new();
        private bool initializing = true, disposed, expansionPreferencesDirty;
        private CancellationTokenSource? flagsSource;
        private string? loadedBuildKey, loadingBuildKey;
        [ObservableProperty] private LaunchBuildChoice? selectedBuild;
        [ObservableProperty] private bool hideRC, hideNightly, hideCustom;
        [ObservableProperty] private bool noIngameOptions, ingameOptionsFileExist, ignoreGlobalCmd, customDependencies;
        [ObservableProperty] private bool isFlagsExpanded, isLoadingFlags;
        [ObservableProperty] private bool isQuickLaunchExpanded, isEngineOptionsExpanded, isExecutableExpanded, isCommandLineExpanded, isDependenciesExpanded, isQuickHelpExpanded;
        [ObservableProperty] private string cmdLine = "", flagSearch = "", flagsError = "", error = "", status = "", directPath = "";
        [ObservableProperty] private string selectedQuickVersion = "", selectedQuickExecutable = "Default", quickExtraArguments = "", quickLaunch = "";
        public ObservableCollection<LaunchBuildChoice> BuildChoices { get; } = new();
        public ObservableCollection<string> QuickVersions { get; } = new();
        public IReadOnlyList<string> QuickExecutables { get; } = new[] { "Default", "Release", "Debug", "Fred2", "Fred2Debug", "QtFred", "QtFredDebug" };
        public ObservableCollection<LaunchFlagGroupViewModel> FlagGroups { get; } = new();
        public ObservableCollection<LaunchDependencyViewModel> Dependencies { get; } = new();
        public string Name => mod.title;
        public string Version => mod.version;
        public bool ShowQuickLaunch => !services.IsAndroid;
        public bool ShowDirectExecutable => !services.IsAndroid;
        public bool CanCreateShortcut => ShowQuickLaunch && services.IsWindows;
        public bool HasError => Error.Length > 0;
        public bool HasFlagsError => FlagsError.Length > 0;
        public bool HasFlags => FlagGroups.Count > 0;
        public string GlobalCommandLine => IgnoreGlobalCmd ? "Global settings are disabled for this mod." :
            (Knossos.globalSettings.GetSystemCMD() + " " + Knossos.globalSettings.globalCmdLine).Trim();
        public string ResolvedBuildLabel => ResolveBuild() is FsoBuild build ? "Resolved engine: " + build : "No installed engine matches these dependencies. Select a build or install the required engine.";
        public string OptionsFilePath => services.OptionsPath(mod);
        public IEnumerable<string> DependencyIds => services.GetMods().Select(m => m.id).Concat(installedBuilds.Select(b => b.id)).Distinct();
        public Task CurrentFlagsLoad { get; private set; } = Task.CompletedTask;
        public ModLaunchOptionsViewModel() : this(new Mod { id = "preview", title = "Blue Planet Complete", version = "3.3.1" }) { }
        public ModLaunchOptionsViewModel(Mod mod, ModCardViewModel? card = null, ModPropertiesViewModel? owner = null, ModLaunchOptionsServices? services = null)
        {
            this.mod = mod; this.card = card; this.owner = owner; this.services = services ?? new ModLaunchOptionsServices();
            installedBuilds = this.services.GetBuilds();
            noIngameOptions = mod.modSettings.noIngameOptions;
            ignoreGlobalCmd = mod.modSettings.ignoreGlobalCmd;
            cmdLine = mod.GetModCmdLine() ?? "";
            customDependencies = mod.modSettings.customDependencies != null;
            hideRC = Knossos.globalSettings.hideBuildRC; hideNightly = Knossos.globalSettings.hideBuildNightly; hideCustom = Knossos.globalSettings.hideBuildCustom;
            FsoBuild? selected = null;
            if (mod.modSettings.customBuildExec != null) selected = new FsoBuild(mod.modSettings.customBuildExec);
            else if (mod.modSettings.customBuildId != null)
            {
                selected = installedBuilds.FirstOrDefault(b => b.id == mod.modSettings.customBuildId && b.version == mod.modSettings.customBuildVersion);
                if (selected == null) Error = "The saved engine " + mod.modSettings.customBuildId + " " + mod.modSettings.customBuildVersion + " is missing. Select another installed build.";
            }
            RebuildChoices(selected);
            directPath = selected?.directExec ?? "";
            QuickVersions.Add("Latest installed");
            foreach (var version in this.services.GetMods().Where(m => m.id == mod.id).Select(m => m.version).Prepend(mod.version).Distinct()) QuickVersions.Add(version);
            selectedQuickVersion = mod.version;
            CreateDependencies(); CheckOptionsFile();
            isQuickLaunchExpanded = this.services.IsSectionExpanded(LaunchOptionsSection.QuickLaunch);
            isEngineOptionsExpanded = this.services.IsSectionExpanded(LaunchOptionsSection.EngineOptions);
            isExecutableExpanded = this.services.IsSectionExpanded(LaunchOptionsSection.Executable);
            isCommandLineExpanded = this.services.IsSectionExpanded(LaunchOptionsSection.CommandLine);
            isDependenciesExpanded = this.services.IsSectionExpanded(LaunchOptionsSection.Dependencies);
            isQuickHelpExpanded = this.services.IsSectionExpanded(LaunchOptionsSection.QuickHelp);
            isFlagsExpanded = this.services.IsSectionExpanded(LaunchOptionsSection.Flags);
            initializing = false; UpdateQuickLaunch();
            if (IsFlagsExpanded) CurrentFlagsLoad = LoadFlagsAsync();
        }
        private void RebuildChoices(FsoBuild? selected)
        {
            var previous = initializing; initializing = true;
            BuildChoices.Clear(); BuildChoices.Add(new LaunchBuildChoice(null));
            foreach (var build in installedBuilds.OrderBy(b => b.stability).ThenByDescending(b => b.date))
                if (ReferenceEquals(build, selected) || SameBuild(build, selected) ||
                    !(build.stability == FsoStability.RC && HideRC || build.stability == FsoStability.Nightly && HideNightly || build.stability == FsoStability.Custom && HideCustom))
                    BuildChoices.Add(new LaunchBuildChoice(build));
            if (selected?.directExec != null) BuildChoices.Add(new LaunchBuildChoice(selected));
            SelectedBuild = BuildChoices.FirstOrDefault(c => SameBuild(c.Build, selected)) ?? BuildChoices[0];
            initializing = previous;
        }
        private static bool SameBuild(FsoBuild? a, FsoBuild? b) => a == null || b == null ? a == b :
            a.directExec != null || b.directExec != null ? a.directExec == b.directExec : a.id == b.id && a.version == b.version;
        private void FiltersChanged()
        {
            if (initializing) return;
            var selected = SelectedBuild?.Build;
            services.SaveBuildFilters(HideRC, HideNightly, HideCustom);
            RebuildChoices(selected);
        }
        partial void OnHideRCChanged(bool value) => FiltersChanged();
        partial void OnHideNightlyChanged(bool value) => FiltersChanged();
        partial void OnHideCustomChanged(bool value) => FiltersChanged();
        partial void OnSelectedBuildChanged(LaunchBuildChoice? value)
        {
            if (initializing) return;
            DirectPath = value?.Build?.directExec ?? "";
            Error = ""; InvalidateFlags();
        }
        partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
        partial void OnFlagsErrorChanged(string value) => OnPropertyChanged(nameof(HasFlagsError));
        partial void OnSelectedQuickVersionChanged(string value) => UpdateQuickLaunch();
        partial void OnSelectedQuickExecutableChanged(string value) => UpdateQuickLaunch();
        partial void OnQuickExtraArgumentsChanged(string value) => UpdateQuickLaunch();
        private void UpdateQuickLaunch()
        {
            if (initializing || !ShowQuickLaunch) return;
            QuickLaunch = LaunchCommandLine.Quote(services.LauncherPath, services.IsWindows) + " " +
                LaunchCommandLine.QuickArguments(mod.id, SelectedQuickVersion == "Latest installed" ? null : SelectedQuickVersion, SelectedQuickExecutable, QuickExtraArguments, services.IsWindows);
        }
        [RelayCommand] private async Task CopyQuickLaunchAsync()
        {
            try
            {
                var clipboard = KnUtils.GetTopLevel().Clipboard;
                if (clipboard == null) throw new InvalidOperationException("Clipboard is unavailable.");
                await clipboard.SetTextAsync(QuickLaunch); Status = "Quick Launch command copied.";
            }
            catch (Exception ex) { Error = ex.Message; }
        }
        [RelayCommand] private void CreateShortcut()
        {
            if (!CanCreateShortcut) return;
            try
            {
                // The editable preview is the source of truth for the created shortcut.
                var (executable, arguments) = LaunchCommandLine.SplitCommand(QuickLaunch);
                Status = "Created " + services.CreateShortcut(mod.title + " " + SelectedQuickVersion + " " + SelectedQuickExecutable, executable, arguments);
                Error = "";
            }
            catch (Exception ex) { Error = ex.Message; }
        }
        [RelayCommand] private void UseDirectPath()
        {
            if (!ShowDirectExecutable) return;
            var value = DirectPath.Trim().Trim('"');
            if (!File.Exists(value)) { Error = "Select an executable file that exists on this device."; return; }
            var build = new FsoBuild(value); RebuildChoices(build); DirectPath = value; Error = ""; InvalidateFlags();
        }
        [RelayCommand] private async Task BrowseExecutableAsync()
        {
            if (!ShowDirectExecutable) return;
            var files = await KnUtils.GetTopLevel().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select an FSO executable", AllowMultiple = false });
            var file = files.FirstOrDefault()?.TryGetLocalPath();
            if (file != null) { DirectPath = file; UseDirectPath(); }
        }
        public FsoBuild? ResolveBuild()
        {
            if (SelectedBuild?.Build != null) return SelectedBuild.Build;
            foreach (var row in Dependencies)
            {
                var dep = row.ToDependency();
                if (dep != null && installedBuilds.Any(b => b.id == dep.id))
                {
                    var build = services.Resolve(dep);
                    if (build != null) return build;
                }
            }
            return null;
        }
        private void CheckOptionsFile() => IngameOptionsFileExist = mod.installed && File.Exists(OptionsFilePath);
        [RelayCommand] private async Task EditEngineOptionsAsync()
        {
            try
            {
                if (!File.Exists(OptionsFilePath)) { CheckOptionsFile(); return; }
                var view = new TextEditorView();
                var editor = new TextEditorViewModel(OptionsFilePath, view.Close);
                view.DataContext = editor;
                await editor.LoadAsync();
                await view.ShowDialog<TextEditorView?>(owner?.GetDialogOwner() ?? MainWindow.instance);
            }
            catch (Exception ex) { Error = ex.Message; }
            finally { CheckOptionsFile(); }
        }
        [RelayCommand] private async Task DeleteEngineOptionsAsync()
        {
            if (owner != null && !await owner.ConfirmAsync("Reset engine options?", "Delete the engine options file? FSO will recreate it with default values on the next launch.", "Delete file")) return;
            try { File.Delete(OptionsFilePath); Status = "Engine options file deleted."; CheckOptionsFile(); }
            catch (Exception ex) { Error = ex.Message; }
        }
        partial void OnIsQuickLaunchExpandedChanged(bool value) => SetSectionExpanded(LaunchOptionsSection.QuickLaunch, value);
        partial void OnIsEngineOptionsExpandedChanged(bool value) => SetSectionExpanded(LaunchOptionsSection.EngineOptions, value);
        partial void OnIsExecutableExpandedChanged(bool value) => SetSectionExpanded(LaunchOptionsSection.Executable, value);
        partial void OnIsCommandLineExpandedChanged(bool value) => SetSectionExpanded(LaunchOptionsSection.CommandLine, value);
        partial void OnIsDependenciesExpandedChanged(bool value) => SetSectionExpanded(LaunchOptionsSection.Dependencies, value);
        partial void OnIsQuickHelpExpandedChanged(bool value) => SetSectionExpanded(LaunchOptionsSection.QuickHelp, value);
        partial void OnIsFlagsExpandedChanged(bool value)
        {
            SetSectionExpanded(LaunchOptionsSection.Flags, value);
            if (value && !IsLoadingFlags) CurrentFlagsLoad = LoadFlagsAsync();
        }
        partial void OnCmdLineChanged(string value) => RefreshFlagValues();
        partial void OnIgnoreGlobalCmdChanged(bool value) { OnPropertyChanged(nameof(GlobalCommandLine)); RefreshFlagValues(); }
        partial void OnFlagSearchChanged(string value) => FilterFlags();
        private void InvalidateFlags()
        {
            flagsSource?.Cancel(); loadedBuildKey = loadingBuildKey = null; IsLoadingFlags = false;
            allFlags.Clear(); FilterFlags(); FlagsError = "";
            OnPropertyChanged(nameof(ResolvedBuildLabel));
            if (IsFlagsExpanded) CurrentFlagsLoad = LoadFlagsAsync();
        }
        private async Task LoadFlagsAsync()
        {
            if (disposed) return;
            var build = ResolveBuild();
            var key = build == null ? "" : build.id + "@" + build.version + "@" + build.directExec;
            if (key == loadedBuildKey || key == loadingBuildKey && IsLoadingFlags) return;
            flagsSource?.Cancel(); flagsSource?.Dispose(); flagsSource = new CancellationTokenSource();
            var request = flagsSource;
            var token = request.Token;
            loadingBuildKey = key;
            IsLoadingFlags = true; FlagsError = "";
            try
            {
                if (build == null) throw new InvalidOperationException("No installed FSO executable could be resolved. Select an installed build above.");
                var flags = await services.GetFlagsAsync(build, token).WaitAsync(token);
                token.ThrowIfCancellationRequested();
                if (disposed) return;
                if (flags?.flags == null) throw new InvalidOperationException("This executable did not return valid flags. It may be older than FSO 3.8.1 or unavailable on this device.");
                allFlags.Clear();
                foreach (var flag in flags.flags.GroupBy(f => f.name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                    allFlags.Add(new LaunchFlagViewModel(flag, (item, value) => CmdLine = LaunchCommandLine.SetFlag(CmdLine, item.Name, value)));
                loadedBuildKey = key; RefreshFlagValues(); FilterFlags();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!token.IsCancellationRequested) FlagsError = ex.Message; }
            finally { if (ReferenceEquals(flagsSource, request)) { IsLoadingFlags = false; loadingBuildKey = null; } }
        }
        private void SetSectionExpanded(LaunchOptionsSection section, bool expanded)
        {
            if (initializing || disposed) return;
            services.SetSectionExpanded(section, expanded);
            expansionPreferencesDirty = true;
        }
        private void SetFlagCategoryExpanded(string category, bool expanded)
        {
            if (disposed) return;
            services.SetFlagCategoryExpanded(category, expanded);
            expansionPreferencesDirty = true;
        }
        private void SaveExpansionPreferences()
        {
            if (!expansionPreferencesDirty) return;
            services.SaveExpansionPreferences();
            expansionPreferencesDirty = false;
        }
        private void RefreshFlagValues()
        {
            foreach (var flag in allFlags) flag.Update(LaunchCommandLine.HasFlag(CmdLine, flag.Name), !IgnoreGlobalCmd && LaunchCommandLine.HasFlag(GlobalCommandLine, flag.Name));
        }
        private void FilterFlags()
        {
            FlagGroups.Clear();
            foreach (var group in allFlags.Where(f => string.IsNullOrWhiteSpace(FlagSearch) ||
                f.Name.Contains(FlagSearch, StringComparison.OrdinalIgnoreCase) || f.Description.Contains(FlagSearch, StringComparison.OrdinalIgnoreCase) ||
                f.Flag.type.Contains(FlagSearch, StringComparison.OrdinalIgnoreCase)).GroupBy(f => string.IsNullOrWhiteSpace(f.Flag.type) ? "Other" : f.Flag.type))
                FlagGroups.Add(new LaunchFlagGroupViewModel(group.Key, group.ToList(), services.IsFlagCategoryExpanded(group.Key),
                    expanded => SetFlagCategoryExpanded(group.Key, expanded)));
            OnPropertyChanged(nameof(HasFlags));
        }
        [RelayCommand] private void RetryFlags() { loadedBuildKey = null; CurrentFlagsLoad = LoadFlagsAsync(); }
        [RelayCommand] private void ResetFlags() { CmdLine = mod.GetModCmdLine(true) ?? ""; IgnoreGlobalCmd = false; }
        public IEnumerable<string> DependencyVersions(string id) => services.GetMods().Where(m => m.id == id).Select(m => m.version)
            .Concat(installedBuilds.Where(b => b.id == id).Select(b => b.version)).Distinct();
        private void CreateDependencies(bool defaults = false)
        {
            Dependencies.Clear();
            var deps = mod.GetModDependencyList(defaults || !CustomDependencies)?.ToList() ?? new();
            var remaining = deps.ToList();
            foreach (var id in mod.GetModFlagList(defaults || !CustomDependencies))
            {
                if (id == mod.id) Dependencies.Add(new LaunchDependencyViewModel(this, id, null, true));
                else if (remaining.FirstOrDefault(d => d.id == id) is ModDependency dep) { Dependencies.Add(new LaunchDependencyViewModel(this, id, dep)); remaining.Remove(dep); }
            }
            foreach (var dep in remaining) Dependencies.Add(new LaunchDependencyViewModel(this, dep.id, dep));
            RefreshDependencies();
        }
        partial void OnCustomDependenciesChanged(bool value)
        {
            if (initializing) return;
            if (!value) CreateDependencies(true);
            RefreshDependencies(); InvalidateFlags();
        }
        private void RefreshDependencies() { foreach (var row in Dependencies) row.RefreshState(); }
        public void DependencyChanged() { if (!initializing) InvalidateFlags(); }
        public void MoveDependency(LaunchDependencyViewModel row, int offset) { var index = Dependencies.IndexOf(row); Dependencies.Move(index, index + offset); RefreshDependencies(); InvalidateFlags(); }
        public void RemoveDependency(LaunchDependencyViewModel row) { Dependencies.Remove(row); RefreshDependencies(); InvalidateFlags(); }
        [RelayCommand] private void AddDependency()
        {
            CustomDependencies = true;
            var id = DependencyIds.FirstOrDefault(i => i != mod.id) ?? "";
            Dependencies.Add(new LaunchDependencyViewModel(this, id, new ModDependency { id = id })); RefreshDependencies();
        }
        public void SaveSettings()
        {
            if (disposed) return;
            SaveExpansionPreferences();
            if (!mod.installed) return;
            var build = SelectedBuild?.Build;
            mod.modSettings.customBuildId = build?.id;
            mod.modSettings.customBuildVersion = build?.version;
            mod.modSettings.customBuildExec = build?.directExec;
            mod.modSettings.customCmdLine = CmdLine == (mod.GetModCmdLine(true) ?? "") ? null : CmdLine;
            mod.modSettings.ignoreGlobalCmd = IgnoreGlobalCmd;
            mod.modSettings.noIngameOptions = NoIngameOptions;
            mod.modSettings.customDependencies = CustomDependencies ? Dependencies.Select(d => d.ToDependency()).Where(d => d != null).Cast<ModDependency>().ToList() : null;
            mod.modSettings.customModFlags = CustomDependencies ? Dependencies.Select(d => d.SelectedId).Where(id => !string.IsNullOrWhiteSpace(id) && !installedBuilds.Any(b => b.id == id)).Distinct().ToList() : null;
            mod.modSettings.SetInitialFilePath(mod.fullPath); mod.modSettings.Save();
            card?.CheckDependencyActiveVersion(); card?.RefreshSpecialIcons();
        }
        public void Dispose()
        {
            if (disposed) return;
            SaveExpansionPreferences();
            disposed = true; flagsSource?.Cancel(); flagsSource?.Dispose();
        }
    }
}
