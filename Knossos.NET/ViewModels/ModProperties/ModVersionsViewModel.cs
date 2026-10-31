using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    public partial class ModVersionsViewModel : ViewModelBase, IDisposable
    {
        private readonly ModPropertiesViewModel owner;
        private readonly ModPackageCatalog catalog;
        private readonly bool forceDeveloper;
        private readonly Dictionary<string, bool> preferences = new();
        private CancellationTokenSource? loadSource;
        private bool disposed, initialized, changing, hasOtherVersions, operationCancelled;
        private ModInstallOperation? operation;
        [ObservableProperty] private TaskItemViewModel? activeTask;
        private List<ModPackagePlanNode> plan = new();
        private ModPackagePlanCache planningCache = new();
        private readonly Dictionary<string, ModPackagePlanNode> displayedNodes = new();
        private readonly Dictionary<string, string> dependencySignatures = new();
        [ObservableProperty] private bool isUpdatingDependencies;
        [ObservableProperty] private ModVersionChoice? selectedVersion;
        [ObservableProperty] private ModPackagePlanNode? root;
        [ObservableProperty] private bool isLoading;
        [ObservableProperty] private bool isApplying;
        [ObservableProperty] private bool developer;
        [ObservableProperty] private bool hasWriteAccess;
        [ObservableProperty] private bool compress;
        [ObservableProperty] private bool cleanReinstall;
        [ObservableProperty] private bool removeOldVersions = true;
        [ObservableProperty] private string error = string.Empty;
        [ObservableProperty] private string status = string.Empty;
        [ObservableProperty] private string downloadSize = "—";
        [ObservableProperty] private string freeSpace = "—";
        public ObservableCollection<ModVersionChoice> Versions { get; } = new();
        public ObservableCollection<ModPackageTreeItem> Dependencies { get; } = new();
        public bool HasError => Error.Length > 0;
        partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
        public bool HasDependencies => Dependencies.Count > 0;
        public string Name => owner.ActiveVersion.title;
        public string ReleaseDate => Root?.Mod.firstRelease ?? "—";
        public bool IsEngine => Root?.Engine ?? owner.ActiveVersion.type == ModType.engine;
        public bool ShowVerifyHint => IsInstalled && !IsEngine;
        public bool IsInstalled => Root?.Installed != null;
        public bool IsPrivate => Root?.Mod.isPrivate == true;
        public string VisibilityLabel => IsPrivate ? "Private version" : "Public version";
        public string VisibilityColor => IsPrivate ? "#E9B66F" : "#89C8FF";
        public bool IsCompressed => Root?.Installed?.modSettings.isCompressed == true;
        public bool CanDelete => IsInstalled && Root?.Installed?.devMode != true && Root?.Mod.id != "FS2" && CanEdit && !IsUpdatingDependencies;
        public bool CanVerify => !IsEngine && IsInstalled && !IsCompressed && CanEdit && !IsUpdatingDependencies;
        public string VerifyHint => IsCompressed ? "Verification is unavailable because this installation is compressed." : "Verify checks installed files and automatically repairs damaged packages.";
        public bool CanChooseDeveloper => !IsEngine && !IsInstalled && HasWriteAccess && !forceDeveloper && CanEdit;
        public bool ShowDeveloper => !IsEngine && (HasWriteAccess || Developer || forceDeveloper);
        public bool ShowCompression => !IsEngine && Knossos.globalSettings.modCompression == CompressionSettings.Manual;
        public bool CanCompress => !IsEngine && !Developer && !IsInstalled && !IsApplying;
        public bool CanCleanReinstall => !IsEngine && IsInstalled && !Developer;
        public bool ShowRemoveOldVersions => !IsEngine && !IsInstalled && hasOtherVersions;
        public bool CanEdit => !IsLoading && !IsApplying && !owner.IsSubmitting;
        public bool CanApply => CanEdit && !IsUpdatingDependencies && Root != null && Error.Length == 0 && (CleanReinstall || plan.Any(n => n.HasChanges));
        public string ApplyLabel => !IsInstalled ? "Download & Install" : CleanReinstall ? "Reinstall version" : "Apply Changes";
        public string PackageSummary => Root == null ? "" : Root.Packages.Count(p => p.IsSelected) + " of " + Root.Packages.Count + " packages selected";
        public string PackageHint => IsEngine ? "FSO packages are selected automatically for your operating system and processor. Development builds are managed in Develop." : Developer && IsInstalled ? "Uncheck optional packages to deactivate them. Their files are kept." :
            Developer ? "Developer mode installs all packages of this mod." : IsInstalled ? "Uncheck installed packages to remove their files." : "Required packages are locked; recommended packages are selected by default.";
        public Task CurrentLoad { get; private set; } = Task.CompletedTask;

        public ModVersionsViewModel(ModPropertiesViewModel owner, bool forceDeveloper = false, ModPackageCatalog? catalog = null)
        { this.owner = owner; this.forceDeveloper = forceDeveloper; this.catalog = catalog ?? new ModPackageCatalog(); }

        public void SelectInstalledVersion(string version)
        {
            if (initialized && !IsApplying) SelectedVersion = Versions.FirstOrDefault(v => v.Mod.version == version) ?? SelectedVersion;
        }

        public Task EnsureLoadedAsync()
        {
            if (initialized) return CurrentLoad;
            initialized = true;
            return CurrentLoad = InitializeAsync();
        }
        private CancellationToken BeginLoad()
        {
            loadSource?.Cancel();
            loadSource?.Dispose();
            loadSource = new CancellationTokenSource();
            IsUpdatingDependencies = false;
            IsLoading = true;
            Error = string.Empty;
            NotifyState();
            return loadSource.Token;
        }
        private async Task InitializeAsync()
        {
            var token = BeginLoad();
            try
            {
                var source = owner.ActiveVersion;
                HasWriteAccess = forceDeveloper || await catalog.CanDevelopAsync(source.id).WaitAsync(token);
                var available = source.modSource == ModSource.local ? new List<Mod> { source } : (await catalog.GetVersionsAsync(source.id).WaitAsync(token)).ToList();
                var installed = catalog.GetInstalled(source.id);
                foreach (var mod in installed)
                    if (available.All(m => m.version != mod.version)) { mod.fullDataLoaded = true; available.Add(mod); }
                if (available.Count == 0) throw new InvalidOperationException("This mod is no longer available on Nebula. Please retry later.");
                available.Sort((a, b) => -Mod.CompareVersion(a, b));
                token.ThrowIfCancellationRequested();
                source = owner.ActiveVersion;
                changing = true;
                Versions.Clear();
                foreach (var mod in available) Versions.Add(new ModVersionChoice(mod, mod == available[0], installed.Any(i => i.version == mod.version)));
                SelectedVersion = source.installed ? Versions.FirstOrDefault(v => v.Mod.version == source.version) ?? Versions[0] : Versions[0];
                changing = false;
                await LoadPlanAsync(token, true);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!token.IsCancellationRequested) Error = ex.Message; }
            finally { if (!token.IsCancellationRequested) { changing = false; IsLoading = false; NotifyState(); } }
        }
        partial void OnSelectedVersionChanged(ModVersionChoice? value)
        {
            if (changing || value == null || disposed) return;
            CleanReinstall = false;
            CurrentLoad = ReloadAsync(true);
        }
        private async Task ReloadAsync(bool versionChanged = false)
        {
            if (disposed) return;
            var token = BeginLoad();
            try { await LoadPlanAsync(token, versionChanged); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!token.IsCancellationRequested) Error = ex.Message; }
            finally { if (!token.IsCancellationRequested) { IsLoading = false; NotifyState(); } }
        }
        private async Task LoadPlanAsync(CancellationToken token, bool versionChanged)
        {
            if (SelectedVersion == null) return;
            if (versionChanged)
            {
                changing = true;
                var installed = catalog.GetInstalled(SelectedVersion.Mod.id).FirstOrDefault(m => m.version == SelectedVersion.Mod.version);
                Developer = SelectedVersion.Mod.type != ModType.engine && (installed?.devMode ?? (forceDeveloper || HasWriteAccess));
                Compress = installed?.modSettings.isCompressed ?? false;
                changing = false;
            }
            planningCache = new ModPackagePlanCache();
            var planner = new ModPackagePlanner(catalog, new Dictionary<string, bool>(preferences), Developer, SelectionChanged, planningCache);
            var root = await planner.BuildAsync(SelectedVersion.Mod, token);
            token.ThrowIfCancellationRequested();
            if (disposed) return;
            hasOtherVersions = planningCache.GetInstalled(catalog, root.Mod.id).Count > 0;
            Root = root;
            // Publish only a complete, current plan. Stale requests never update the displayed tree.
            plan = planner.Nodes.ToList();
            displayedNodes.Clear();
            foreach (var node in plan) displayedNodes[ModPackageCatalog.Key(node.Mod)] = node;
            RememberDependencySignatures();
            Dependencies.Clear();
            ModPackageTreeItem.UpdateDependencies(Dependencies, root);
            Error = string.Join("\n", planner.Errors.Distinct());
            Status = root.Installed == null ? "Ready to install this version" : Developer ? "Developer installation · package activation preserves files" : "This version is installed";
            UpdateSize(true);
        }
        private static string DependencySignature(ModPackagePlanNode node)
            => JsonSerializer.Serialize(new
            {
                Custom = node.Mod.modSettings.customDependencies,
                Packages = node.Mod.modSettings.customDependencies != null ? null : node.Packages
                    .Where(p => p.IsSelected && p.Package.dependencies?.Length > 0)
                    .Select(p => new { p.Name, Dependencies = p.Package.dependencies })
            });
        private void RememberDependencySignatures()
        {
            dependencySignatures.Clear();
            foreach (var node in plan) dependencySignatures[ModPackageCatalog.Key(node.Mod)] = DependencySignature(node);
        }
        private void SelectionChanged(ModPackageSelectionViewModel item)
        {
            if (disposed || Root == null) return;
            preferences[item.Key] = item.IsSelected;
            UpdateSize();
            var node = plan.FirstOrDefault(n => n.Packages.Contains(item));
            var graphChanged = node != null && (!dependencySignatures.TryGetValue(ModPackageCatalog.Key(node.Mod), out var before) || before != DependencySignature(node));
            if (graphChanged || IsUpdatingDependencies) CurrentLoad = UpdateDependenciesAsync();
            else
            {
                // A package without dependencies only changes selection and byte counts.
                OnPropertyChanged(nameof(PackageSummary));
                OnPropertyChanged(nameof(CanApply));
                ApplyCommand.NotifyCanExecuteChanged();
            }
        }
        private async Task UpdateDependenciesAsync()
        {
            loadSource?.Cancel();
            loadSource?.Dispose();
            loadSource = new CancellationTokenSource();
            var token = loadSource.Token;
            IsUpdatingDependencies = true;
            NotifyState();
            try
            {
                // Let the checkbox publish its value before resolving any changed dependency branches.
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                var planner = new ModPackagePlanner(catalog, new Dictionary<string, bool>(preferences), Developer, SelectionChanged, planningCache);
                var result = await planner.BuildAsync(SelectedVersion!.Mod, token);
                token.ThrowIfCancellationRequested();
                if (disposed) return;
                foreach (var candidate in planner.Nodes)
                {
                    var key = ModPackageCatalog.Key(candidate.Mod);
                    if (displayedNodes.TryGetValue(key, out var existing))
                    {
                        foreach (var package in candidate.Packages)
                            existing.Packages.First(p => p.Key == package.Key).UpdateFrom(package);
                    }
                    else displayedNodes[key] = candidate;
                }
                // Keep the existing rows and change only the edges that were added or removed.
                foreach (var candidate in planner.Nodes)
                {
                    var existing = displayedNodes[ModPackageCatalog.Key(candidate.Mod)];
                    var edges = candidate.Dependencies.Select(e => (displayedNodes[ModPackageCatalog.Key(e.Node.Mod)], e.Origin)).ToList();
                    existing.Dependencies.Clear();
                    existing.Dependencies.AddRange(edges);
                }
                plan = planner.Nodes.Select(n => displayedNodes[ModPackageCatalog.Key(n.Mod)]).ToList();
                ModPackageTreeItem.UpdateDependencies(Dependencies, displayedNodes[ModPackageCatalog.Key(result.Mod)]);
                RememberDependencySignatures();
                Error = string.Join("\n", planner.Errors.Distinct());
                UpdateSize();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!token.IsCancellationRequested) Error = ex.Message; }
            finally { if (!token.IsCancellationRequested) { IsUpdatingDependencies = false; NotifyState(); } }
        }
        partial void OnDeveloperChanged(bool value)
        {
            if (value) Compress = false;
            if (!changing && SelectedVersion != null) CurrentLoad = ReloadAsync();
            NotifyState();
        }
        partial void OnCleanReinstallChanged(bool value) { UpdateSize(); NotifyState(); }
        private void UpdateSize(bool refreshFreeSpace = false)
        {
            long bytes = 0;
            foreach (var node in plan)
                foreach (var pkg in node.Packages.Where(p => p.IsSelected && (!p.Installed || node == Root && CleanReinstall)))
                    bytes += pkg.Package.files?.Sum(f => f.filesize) ?? 0;
            DownloadSize = KnUtils.FormatBytes(bytes);
            if (refreshFreeSpace)
            {
                var library = Knossos.GetKnossosLibraryPath();
                FreeSpace = string.IsNullOrEmpty(library) ? "Library not configured" : KnUtils.FormatBytes(KnUtils.CheckDiskSpace(library));
            }
        }
        internal void RefreshCommands() => NotifyState();

        private void NotifyState()
        {
            foreach (var property in new[] { nameof(ReleaseDate), nameof(IsEngine), nameof(ShowVerifyHint), nameof(ShowCompression), nameof(IsInstalled), nameof(IsPrivate), nameof(VisibilityLabel), nameof(VisibilityColor), nameof(IsCompressed),
                nameof(CanDelete), nameof(CanVerify), nameof(VerifyHint), nameof(CanChooseDeveloper), nameof(ShowDeveloper), nameof(CanCompress), nameof(CanCleanReinstall), nameof(ShowRemoveOldVersions),
                nameof(CanEdit), nameof(CanApply), nameof(ApplyLabel), nameof(PackageSummary), nameof(PackageHint), nameof(HasDependencies) }) OnPropertyChanged(property);
            ApplyCommand.NotifyCanExecuteChanged();
            VerifyCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
        }
        [RelayCommand]
        private void Retry() { catalog.Refresh(); initialized = false; CurrentLoad = EnsureLoadedAsync(); }
        [RelayCommand]
        private void PackageInfo() => owner.ShowMessage("Mod packages", (Root?.Mod.notes ?? "") + "\n\n" + PackageHint +
            "\n\nGreen: selected in the previous installation. Amber: previously skipped. Blue: a new package. Orange: required by a selected dependency.\n\n" +
            string.Join("\n\n", Root?.Packages.Select(p => p.Name + " · " + p.Kind + "\n" + p.Notes + (p.History.Length > 0 ? "\n" + p.History : "")) ?? Array.Empty<string>()));
        [RelayCommand]
        private void DependencyInfo() => owner.ShowMessage("Dependencies", "Only dependencies of selected packages are included. Expand each mod to see its packages and nested dependencies.\n\nOrange packages are required by a selected parent and cannot be unchecked until that parent is no longer selected.\n\nFSO builds are selected automatically for this operating system and processor. Version choices follow the existing installer rules.");
        [RelayCommand(CanExecute = nameof(CanDelete))]
        private async Task DeleteAsync()
        {
            if (CanDelete && Root?.Installed != null) await owner.DeleteInstalledVersionAsync(Root.Installed);
        }

        internal async Task InstalledVersionRemovedAsync(Mod deleted)
        {
            preferences.Clear();
            var remaining = catalog.GetInstalled(deleted.id);
            changing = true;
            var selected = SelectedVersion?.Mod.version;
            var choices = Versions.Select(v => v.Mod).ToList();
            Versions.Clear();
            foreach (var mod in choices)
                Versions.Add(new ModVersionChoice(mod, mod == choices[0], remaining.Any(m => m.version == mod.version)));
            SelectedVersion = Versions.FirstOrDefault(v => v.Mod.version == selected);
            changing = false;
            owner.InstalledVersionRemoved(deleted, remaining);
            if (initialized) await ReloadAsync(true);
        }

        private void BeginOperation()
        {
            operationCancelled = false;
            IsApplying = true;
            owner.OperationStarted();
            operation = new ModInstallOperation(task => ActiveTask = task, plan.Select(n => n.Mod));
            NotifyState();
        }
        private void EndOperation()
        {
            operation?.Dispose();
            operation = null;
            ActiveTask = null;
            IsApplying = false;
            NotifyState();
            owner.OperationEnded();
        }
        [RelayCommand]
        private void CancelOperation()
        {
            if (!IsApplying) return;
            operationCancelled = true;
            operation?.Cancel();
            Status = "Cancelling… Waiting for file operations to finish safely.";
        }
        [RelayCommand(CanExecute = nameof(CanApply))]
        private async Task ApplyAsync()
        {
            if (!CanApply || Root == null) return;
            if (TaskViewModel.Instance == null) return;
            if (string.IsNullOrEmpty(Knossos.GetKnossosLibraryPath())) { owner.ShowMessage("Library not configured", "Select a library folder in Settings before installing mods."); return; }
            if (CleanReinstall && !await owner.ConfirmAsync("Reinstall this version?",
                "The current version's files will be replaced. Your selected packages will be reinstalled; other versions can still supply verified files. A failed installation restores the current version.", "Reinstall version")) return;
            BeginOperation();
            try
            {
                if (Knossos.inSingleTCMode) TaskViewModel.Instance.CleanCommand();
                var visited = new HashSet<ModPackagePlanNode>();
                var ordered = new List<ModPackagePlanNode>();
                void Visit(ModPackagePlanNode node) { if (!visited.Add(node)) return; foreach (var edge in node.Dependencies) Visit(edge.Node); ordered.Add(node); }
                Visit(Root);
                foreach (var node in ordered)
                {
                    if (operationCancelled) { Status = "Installation cancelled"; return; }
                    Status = "Applying changes to " + node.Mod.title + " " + node.Mod.version + "…";
                    var dev = node == Root ? Developer : node.Installed?.devMode == true;
                    var needsFiles = node.Installed == null || node.Packages.Any(p => (dev || p.IsSelected) && !p.Installed) || node == Root && CleanReinstall;
                    if (needsFiles || node.HasChanges || node.Installed != null && Mod.IsMetaUpdate(node.Mod, node.Installed))
                    {
                        var addOnly = node != Root || node.Installed == null;
                        var snapshot = node.ToInstallMod(dev);
                        if (addOnly) snapshot.packages.RemoveAll(p => node.Packages.All(s => s.Name != p.name || !s.IsSelected));
                        var activation = !addOnly && dev ? node.Packages.Where(p => !p.Installed || p.IsSelected !=
                            (node.Installed!.packages.FirstOrDefault(i => i.name == p.Name)?.isEnabled ?? false))
                            .ToDictionary(p => p.Name, p => p.IsSelected) : null;
                        var success = await TaskViewModel.Instance.InstallModAsync(snapshot, manualCompress: node == Root && Compress,
                            cleanupOldVersions: node == Root && ShowRemoveOldVersions && RemoveOldVersions, allowHardlinks: true,
                            reinstallCurrentVersion: node == Root && CleanReinstall, reportError: message => owner.ShowMessage("Installation failed", message),
                            addOnly: addOnly, operation: operation, packageActivation: activation,
                            removePackages: addOnly ? Array.Empty<string>() : node.Packages.Where(p => p.Installed && !p.IsSelected).Select(p => p.Name).ToArray());
                        if (!success || operationCancelled) { Status = "Installation stopped. Review Tasks for details."; return; }
                    }

                }
                preferences.Clear();
                changing = true;
                var selected = SelectedVersion?.Mod.version;
                var choices = Versions.Select(v => v.Mod).ToList();
                var installedVersions = catalog.GetInstalled(owner.ActiveVersion.id);
                Versions.Clear();
                foreach (var mod in choices) Versions.Add(new ModVersionChoice(mod, mod == choices[0], installedVersions.Any(m => m.version == mod.version)));
                SelectedVersion = Versions.FirstOrDefault(v => v.Mod.version == selected);
                changing = false;
                var active = Knossos.GetInstalledMod(Root.Mod.id, Root.Mod.version);
                if (Root.Engine)
                {
                    var build = Knossos.GetInstalledBuild(Root.Mod.id, Root.Mod.version);
                    if (build?.modData != null)
                    {
                        active = ModPackageCatalog.Copy(build.modData);
                        active.installed = true;
                        active.fullPath = build.folderPath;
                    }
                }
                if (active != null) owner.InstalledVersionChanged(active);
                MainViewModel.Instance?.RunModStatusChecks();
                CleanReinstall = false;
                await ReloadAsync(true);
                Status = "Changes applied";
            }
            catch (OperationCanceledException) { Status = "Installation cancelled"; }
            catch (Exception ex) { Log.Add(Log.LogSeverity.Error, "ModProperties.Apply", ex); owner.ShowMessage("Installation failed", ex.Message); }
            finally { EndOperation(); }
        }
        [RelayCommand(CanExecute = nameof(CanVerify))]
        private async Task VerifyAsync()
        {
            if (!CanVerify || Root?.Installed == null || TaskViewModel.Instance == null) return;
            BeginOperation();
            try
            {
                var installed = Knossos.GetInstalledMod(Root.Mod.id, Root.Mod.version);
                if (installed == null) return;
                installed.modSettings.Load(installed.fullPath);
                if (installed.modSettings.isCompressed) { NotifyState(); return; }
                Status = "Verifying and repairing files…";
                var success = await TaskViewModel.Instance.VerifyModAsync(ModPackageCatalog.Copy(installed), message => owner.ShowMessage("Repair failed", message), operation);
                await ReloadAsync(true);
                Status = success ? "Verification and any required repairs completed" : "Verification stopped. Review Tasks for details.";
            }
            catch (Exception ex) { owner.ShowMessage("Verification failed", ex.Message); }
            finally { EndOperation(); }
        }
        public void Dispose() { disposed = true; loadSource?.Cancel(); loadSource?.Dispose(); operation?.Dispose(); }
    }
}
