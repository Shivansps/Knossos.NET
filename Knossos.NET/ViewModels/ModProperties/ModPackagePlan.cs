using CommunityToolkit.Mvvm.ComponentModel;
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
    /// <summary>Retrieves detached metadata so a pending plan never changes installed mods.</summary>
    public class ModPackageCatalog
    {
        private readonly Dictionary<string, Task<List<Mod>>> versions = new();
        private readonly Dictionary<string, Task<Mod>> details = new();
        public static string Key(Mod mod) => mod.id + "@" + mod.version;
        public static Mod Copy(Mod mod)
        {
            var copy = JsonSerializer.Deserialize<Mod>(JsonSerializer.Serialize(mod))!;
            copy.fullPath = mod.fullPath;
            copy.folderName = mod.folderName;
            copy.modSettings = mod.modSettings;
            copy.fullDataLoaded = mod.fullDataLoaded;
            return copy;
        }
        public virtual void Refresh() { versions.Clear(); details.Clear(); }
        public virtual async Task<List<Mod>> GetVersionsAsync(string id)
        {
            if (!versions.TryGetValue(id, out var task)) versions[id] = task = Nebula.GetAllModsWithID(id);
            try { return await task; }
            catch { versions.Remove(id); throw; }
        }
        public virtual async Task<Mod> GetDetailsAsync(Mod mod)
        {
            var key = Key(mod);
            if (!details.TryGetValue(key, out var task)) details[key] = task = LoadAsync(mod);
            try { return Copy(await task); }
            catch { details.Remove(key); throw; }
        }
        private static async Task<Mod> LoadAsync(Mod source)
        {
            var mod = Copy(source);
            if (!await mod.LoadFulLNebulaData()) throw new InvalidOperationException("Could not load " + mod.title + " " + mod.version + ". Retry when Nebula is available.");
            return mod;
        }
        public virtual List<Mod> GetInstalled(string id)
        {
            var result = Knossos.GetInstalledModList(id);
            foreach (var mod in result) mod.ReLoadJson();
            foreach (var build in Knossos.GetInstalledBuildsList(id).Where(b => b.IsValidBuild() && b.modData != null))
            {
                var mod = Copy(build.modData!);
                mod.installed = true;
                mod.fullPath = build.folderPath;
                result.Add(mod);
            }
            return result.Select(Copy).ToList();
        }
        public virtual Mod? GetInstalledBuild(ModDependency dependency)
        {
            var build = dependency.SelectBuild(true);
            if (build?.modData == null) return null;
            var mod = Copy(build.modData);
            mod.installed = true;
            mod.fullPath = build.folderPath;
            return mod;
        }
        public virtual async Task<bool> CanDevelopAsync(string id) => Nebula.userIsLoggedIn &&
            (!Knossos.inSingleTCMode || CustomLauncher.MenuDisplayNebulaLoginEntry) &&
            (await Nebula.GetEditableModIDs())?.Contains(id) == true;
    }

    public sealed class ModVersionChoice
    {
        public Mod Mod { get; }
        public string Label { get; }
        public string Color => Mod.isPrivate ? "#E9B66F" : "#89C8FF";
        public ModVersionChoice(Mod mod, bool latest, bool installed)
        {
            Mod = mod;
            Label = mod.version + (latest ? " · Latest" : "") + (installed ? " · Installed" : "") + (mod.isPrivate ? " · Private" : " · Public");
        }
    }

    public partial class ModPackageSelectionViewModel : ViewModelBase
    {
        private readonly Action<ModPackageSelectionViewModel> changed;
        private bool forcing;
        private readonly bool keepSelected;
        public ModPackage Package { get; }
        public string Key { get; }
        public string Name => Package.name;
        public string Notes => Package.notes ?? string.Empty;
        public string Size => KnUtils.FormatBytes(Package.files?.Sum(f => f.filesize) ?? 0);
        public bool Installed { get; }
        public bool Developer { get; }
        public string History { get; }
        public string HistoryColor { get; }
        [ObservableProperty] private bool isDetailsExpanded;
        [ObservableProperty] private bool isSelected;
        [ObservableProperty] private bool isRequired;
        [ObservableProperty] private string requiredBy = string.Empty;
        public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
        public bool HasHistory => History.Length > 0;
        public bool HasRequiredBy => RequiredBy.Length > 0;
        public string DetailsLabel => "Details for " + Name;
        public bool CanToggle => !IsRequired && !keepSelected;
        public string Kind => IsRequired ? "Required" : Package.status == "optional" ? "Optional" : "Recommended";
        public string Color => RequiredBy.Length > 0 ? "#FFB65B" : IsRequired ? "#CFDCE8" : HistoryColor;
        public string Action => Developer && Installed ? (IsSelected ? "Active · files kept" : "Inactive · files kept") :
            Developer ? (IsSelected ? "Will be installed · active" : "Will be installed · inactive") :
            Installed ? (IsSelected ? "Installed" : "Will be removed") : IsSelected ? "Will be installed" : "Not selected";
        public ModPackageSelectionViewModel(Mod mod, ModPackage package, Mod? installed, bool developer,
            bool selected, string history, string color, Action<ModPackageSelectionViewModel> changed, bool addOnly = false)
        {
            Package = package;
            Key = ModPackageCatalog.Key(mod) + "/" + package.name;
            Installed = installed?.packages.Any(p => p.name == package.name) == true;
            Developer = developer;
            keepSelected = addOnly && Installed && (!developer || installed!.packages.First(p => p.name == package.name).isEnabled);
            isSelected = selected || keepSelected;
            History = history;
            HistoryColor = color;
            this.changed = changed;
        }
        partial void OnIsSelectedChanged(bool value)
        {
            if ((IsRequired || keepSelected) && !value) { IsSelected = true; return; }
            OnPropertyChanged(nameof(Action));
            if (!forcing) changed(this);
        }
        public void UpdateFrom(ModPackageSelectionViewModel source)
        {
            var requiredChanged = IsRequired != source.IsRequired;
            var reasonChanged = RequiredBy != source.RequiredBy;
            forcing = true;
            IsRequired = source.IsRequired;
            RequiredBy = source.RequiredBy;
            IsSelected = source.IsSelected;
            forcing = false;
            if (requiredChanged) { OnPropertyChanged(nameof(CanToggle)); OnPropertyChanged(nameof(Kind)); }
            if (requiredChanged || reasonChanged) OnPropertyChanged(nameof(Color));
            if (reasonChanged) OnPropertyChanged(nameof(HasRequiredBy));
        }
        public void Require(string reason)
        {
            // Forced selection must not overwrite the user's preference for a later, independent plan.
            forcing = true;
            IsSelected = true;
            forcing = false;
            IsRequired = true;
            if (!string.IsNullOrEmpty(reason) && !RequiredBy.Contains(reason)) RequiredBy += (RequiredBy.Length == 0 ? "" : "\n") + reason;
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(CanToggle));
            OnPropertyChanged(nameof(HasRequiredBy));
            OnPropertyChanged(nameof(Kind));
            OnPropertyChanged(nameof(Color));
            OnPropertyChanged(nameof(Action));
        }
    }

    public sealed class ModPackagePlanNode
    {
        public Mod Mod { get; }
        public Mod? Installed { get; }
        public List<ModPackageSelectionViewModel> Packages { get; } = new();
        public List<(ModPackagePlanNode Node, string Origin)> Dependencies { get; } = new();
        public bool Engine => Mod.type == ModType.engine;
        public ModPackagePlanNode(Mod mod, Mod? installed) { Mod = mod; Installed = installed; }
        public bool HasChanges => Installed == null || !Engine && (Models.Mod.IsMetaUpdate(Mod, Installed) ||
            Installed.devMode && Packages.Any(p => !p.Installed) || Packages.Any(p =>
                !Installed.devMode && p.Installed != p.IsSelected || Installed.devMode && p.IsSelected !=
                (Installed.packages.FirstOrDefault(x => x.name == p.Name)?.isEnabled ?? false)));
        public Mod ToInstallMod(bool developer)
        {
            var copy = ModPackageCatalog.Copy(Mod);
            copy.devMode = developer;
            for (var i = 0; i < copy.packages.Count; i++)
            {
                var selection = Packages.FirstOrDefault(p => p.Name == copy.packages[i].name);
                // Developer packages are always retained on disk, independently of activation.
                copy.packages[i].isSelected = developer || selection?.IsSelected == true;
            }
            if (Engine) copy.packages.RemoveAll(p => !p.isSelected);
            return copy;
        }
    }

    /// <summary>A display occurrence; shared selections may appear below more than one parent.</summary>
    public partial class ModPackageTreeItem : ViewModelBase
    {
        public string Key { get; }
        [ObservableProperty] private string title = string.Empty;
        [ObservableProperty] private string subtitle = string.Empty;
        [ObservableProperty] private bool isExpanded;
        public ModPackageSelectionViewModel? Selection { get; }
        public bool IsPackage => Selection != null;
        public bool IsGroup => !IsPackage;
        public ObservableCollection<ModPackageTreeItem> Children { get; } = new();
        public ModPackageTreeItem(ModPackagePlanNode node, string origin, HashSet<string>? ancestors = null)
        {
            Key = "mod:" + ModPackageCatalog.Key(node.Mod);
            Update(node, origin, ancestors ?? new());
        }
        private ModPackageTreeItem(ModPackageSelectionViewModel package)
        { Key = "pkg:" + package.Key; Selection = package; Title = package.Name; Subtitle = package.Notes; }
        private void Update(ModPackagePlanNode node, string origin, HashSet<string> ancestors)
        {
            Title = node.Mod.title + "  " + node.Mod.version;
            Subtitle = (node.Installed != null ? "Installed · " : "") + origin;
            ancestors = new(ancestors);
            if (!ancestors.Add(ModPackageCatalog.Key(node.Mod)))
            { Subtitle += " · See earlier occurrence"; Children.Clear(); return; }
            var desired = new List<ModPackageTreeItem>();
            foreach (var package in node.Packages)
                desired.Add(Children.FirstOrDefault(c => c.Key == "pkg:" + package.Key) ?? new ModPackageTreeItem(package));
            foreach (var edge in node.Dependencies)
            {
                var existing = Children.FirstOrDefault(c => c.Key == "mod:" + ModPackageCatalog.Key(edge.Node.Mod));
                if (existing != null) existing.Update(edge.Node, edge.Origin, ancestors);
                desired.Add(existing ?? new ModPackageTreeItem(edge.Node, edge.Origin, ancestors));
            }
            Synchronize(Children, desired);
        }
        public static void UpdateDependencies(ObservableCollection<ModPackageTreeItem> items, ModPackagePlanNode root)
        {
            var ancestors = new HashSet<string> { ModPackageCatalog.Key(root.Mod) };
            var desired = new List<ModPackageTreeItem>();
            foreach (var edge in root.Dependencies)
            {
                var existing = items.FirstOrDefault(c => c.Key == "mod:" + ModPackageCatalog.Key(edge.Node.Mod));
                if (existing != null) existing.Update(edge.Node, edge.Origin, ancestors);
                desired.Add(existing ?? new ModPackageTreeItem(edge.Node, edge.Origin, ancestors));
            }
            Synchronize(items, desired);
        }
        private static void Synchronize(ObservableCollection<ModPackageTreeItem> items, List<ModPackageTreeItem> desired)
        {
            for (var i = items.Count - 1; i >= 0; i--) if (!desired.Contains(items[i])) items.RemoveAt(i);
            for (var i = 0; i < desired.Count; i++)
            {
                var previous = items.IndexOf(desired[i]);
                if (previous < 0) items.Insert(i, desired[i]);
                else if (previous != i) items.Move(previous, i);
            }
        }
    }

    /// <summary>Read-only metadata snapshots reused for package changes within one version.</summary>
    public sealed class ModPackagePlanCache
    {
        private readonly Dictionary<string, List<Mod>> installed = new();
        private readonly Dictionary<string, Task<Mod>> details = new();
        private readonly Dictionary<string, Mod> localDetails = new();
        private readonly Dictionary<string, Task<List<Mod>>> versions = new();
        private readonly Dictionary<string, Mod?> builds = new();
        public List<Mod> GetInstalled(ModPackageCatalog catalog, string id)
        {
            if (!installed.TryGetValue(id, out var mods)) installed[id] = mods = catalog.GetInstalled(id);
            return mods;
        }
        public Mod? GetInstalledBuild(ModPackageCatalog catalog, ModDependency dependency)
        {
            var key = dependency.id + "@" + dependency.version;
            if (!builds.TryGetValue(key, out var build)) builds[key] = build = catalog.GetInstalledBuild(dependency);
            return build;
        }
        public Task<List<Mod>> GetVersionsAsync(ModPackageCatalog catalog, string id, CancellationToken token)
        {
            if (!versions.TryGetValue(id, out var task)) versions[id] = task = catalog.GetVersionsAsync(id);
            return task.WaitAsync(token);
        }
        public Mod GetLocalDetails(Mod source)
        {
            var key = ModPackageCatalog.Key(source);
            if (!localDetails.TryGetValue(key, out var mod)) localDetails[key] = mod = ModPackageCatalog.Copy(source);
            return mod;
        }
        public async Task<Mod> GetDetailsAsync(ModPackageCatalog catalog, Mod source, CancellationToken token)
        {
            var key = ModPackageCatalog.Key(source);
            if (!details.TryGetValue(key, out var task)) details[key] = task = catalog.GetDetailsAsync(source);
            try { return await task.WaitAsync(token); }
            catch when (task.IsFaulted || task.IsCanceled) { details.Remove(key); throw; }
        }
    }

    /// <summary>Preserves the legacy candidate/filter rules while following selected packages only.</summary>
    public sealed class ModPackagePlanner
    {
        private readonly ModPackageCatalog catalog;
        private readonly IReadOnlyDictionary<string, bool> preferences;
        private readonly Action<ModPackageSelectionViewModel> changed;
        private readonly bool developer;
        private readonly ModPackagePlanCache cache;
        private readonly Dictionary<string, ModPackagePlanNode> nodes = new();
        private readonly Dictionary<string, string> processed = new();
        public List<string> Errors { get; } = new();
        public IReadOnlyCollection<ModPackagePlanNode> Nodes => nodes.Values;
        public ModPackagePlanner(ModPackageCatalog catalog, IReadOnlyDictionary<string, bool> preferences,
            bool developer, Action<ModPackageSelectionViewModel> changed, ModPackagePlanCache? cache = null)
        { this.catalog = catalog; this.preferences = preferences; this.developer = developer; this.changed = changed; this.cache = cache ?? new(); }

        public async Task<ModPackagePlanNode> BuildAsync(Mod source, CancellationToken token)
        {
            var root = await CreateNodeAsync(source, true, token);
            if (root.Engine)
            {
                if (root.Packages.Count == 0) Errors.Add("This build has no packages compatible with your operating system and processor.");
                return root;
            }
            // A shared node may gain a forced package from a later branch. Revisit it until stable.
            for (var pass = 0; pass < 256; pass++)
            {
                token.ThrowIfCancellationRequested();
                var pending = nodes.Values.FirstOrDefault(n => !processed.TryGetValue(ModPackageCatalog.Key(n.Mod), out var previous) || previous != Signature(n));
                if (pending == null) return root;
                processed[ModPackageCatalog.Key(pending.Mod)] = Signature(pending);
                await ExpandAsync(pending, token);
            }
            throw new InvalidOperationException("The dependency tree is too large to resolve safely.");
        }
        private static string Signature(ModPackagePlanNode node) => string.Join("|", node.Packages.Where(p => p.IsSelected).Select(p => p.Name));
        private async Task<ModPackagePlanNode> CreateNodeAsync(Mod source, bool root, CancellationToken token)
        {
            var key = ModPackageCatalog.Key(source);
            if (nodes.TryGetValue(key, out var existing)) return existing;
            var installedVersions = cache.GetInstalled(catalog, source.id);
            var installed = installedVersions.FirstOrDefault(m => m.version == source.version);
            var metadata = await GetMetadataAsync(source, token);
            token.ThrowIfCancellationRequested();
            // Keep installed-only packages, including unpublished developer content.
            if (installed != null)
                foreach (var pkg in installed.packages.Where(p => metadata.packages.All(x => x.name != p.name))) metadata.packages.Add(pkg);
            var node = new ModPackagePlanNode(metadata, installed);
            nodes.Add(key, node);
            var previous = installedVersions.OrderByDescending(m => new SemanticVersion(m.version)).FirstOrDefault();
            Mod? previousMetadata = null;
            if (installed == null && previous != null)
            {
                try { previousMetadata = await GetMetadataAsync(previous, token); }
                catch (OperationCanceledException) { throw; }
                catch { previousMetadata = previous; }
            }
            var bestPackage = node.Engine ? FsoBuild.SelectBestPackage(metadata.packages) : null;
            foreach (var pkg in metadata.packages.Where(p => !node.Engine || p == bestPackage))
            {
                var current = installed?.packages.FirstOrDefault(p => p.name == pkg.name);
                var selected = installed != null ? current != null && (!installed.devMode || current.isEnabled) : pkg.status != "optional";
                var history = "";
                var color = "#A9B8C8";
                if (installed == null && previous != null && pkg.status != "required")
                {
                    var wasInstalled = previous.packages.Any(p => p.name == pkg.name);
                    var existed = previousMetadata?.packages.Any(p => p.name == pkg.name) == true;
                    selected = wasInstalled || (!existed && pkg.status != "optional");
                    history = wasInstalled ? "Selected in " + previous.version : existed ? "Not installed in " + previous.version : "New package";
                    color = wasInstalled ? "#86D98A" : existed ? "#E0AC72" : "#89C8FF";
                }
                if (preferences.TryGetValue(key + "/" + pkg.name, out var preference)) selected = preference;
                var item = new ModPackageSelectionViewModel(metadata, pkg, installed, installed?.devMode ?? (root && developer), selected, history, color, changed, addOnly: !root);
                if (pkg.status == "required" || (root && developer && installed == null)) item.Require("");
                if (node.Engine) item.Require("Selected automatically for this platform");
                node.Packages.Add(item);
            }
            return node;
        }
        private async Task<Mod> GetMetadataAsync(Mod source, CancellationToken token)
        {
            if (source.modSource == ModSource.local || source.installed && source.type == ModType.engine) return cache.GetLocalDetails(source);
            if (source.installed)
            {
                // Installed JSON contains only downloaded packages. Use published metadata to expose
                // missing options and distinguish a previously skipped package from a newly added one.
                var versions = await cache.GetVersionsAsync(catalog, source.id, token);
                var published = versions.FirstOrDefault(m => m.version == source.version);
                if (published == null) return ModPackageCatalog.Copy(source);
                source = ModPackageCatalog.Copy(published);
                source.installed = false;
            }
            return await cache.GetDetailsAsync(catalog, source, token);
        }
        private async Task ExpandAsync(ModPackagePlanNode node, CancellationToken token)
        {
            if (node.Engine) return;
            var selected = new Mod { modSettings = node.Mod.modSettings,
                packages = node.Packages.Where(p => p.IsSelected).Select(p => p.Package).ToList() };
            // Use the existing per-mod filter: global version reconciliation and duplicate package merging are separate work.
            var dependencies = selected.GetModDependencyList(false, true) ?? new();
            foreach (var dep in dependencies)
            {
                token.ThrowIfCancellationRequested();
                var originPackages = selected.packages.Where(p => p.dependencies?.Any(d => d.id == dep.id) == true).Select(p => p.name);
                var origin = "Required by " + node.Mod.title + " / " + string.Join(", ", originPackages);
                var installed = cache.GetInstalled(catalog, dep.id).Where(m => SemanticVersion.SastifiesDependency(dep.version, m.version))
                    .OrderByDescending(m => new SemanticVersion(m.version)).FirstOrDefault();
                Mod? candidate = null;
                if (!Knossos.inSingleTCMode && installed != null && (dep.packages == null || dep.packages.All(p => installed.packages.Any(x => x.name == p)))) candidate = installed;
                if (candidate?.type == ModType.engine) candidate = null;
                if (!Knossos.inSingleTCMode) candidate ??= cache.GetInstalledBuild(catalog, dep);
                var candidates = candidate == null || Knossos.inSingleTCMode ? await cache.GetVersionsAsync(catalog, dep.id, token) : new List<Mod>();
                candidate ??= await dep.SelectModNebula(candidates);
                var engineDependency = candidate?.type == ModType.engine || dep.id == "FSO" || candidates.Any(m => m.type == ModType.engine);
                if (candidate?.type == ModType.engine && !candidate.installed)
                {
                    var remaining = candidates.ToList();
                    for (var attempt = 0; attempt < 10 && candidate != null; attempt++)
                    {
                        var full = await cache.GetDetailsAsync(catalog, candidate, token);
                        if (FsoBuild.SelectBestPackage(full.packages) != null) { candidate = full; break; }
                        remaining.RemoveAll(m => m.version == candidate.version);
                        candidate = await dep.SelectModNebula(remaining);
                    }
                    if (candidate != null && FsoBuild.SelectBestPackage(candidate.packages) == null) candidate = null;
                }
                if (candidate == null && engineDependency) continue;
                if (candidate == null) { Errors.Add("No compatible version is available for " + dep + ". " + origin + "."); continue; }
                var child = await CreateNodeAsync(candidate, false, token);
                if (child.Engine && child.Packages.Count == 0) continue;
                foreach (var name in child.Engine ? new List<string>() : dep.packages ?? new())
                {
                    var required = child.Packages.FirstOrDefault(p => p.Name == name);
                    if (required == null) Errors.Add(child.Mod.title + " does not contain required package " + name + ". " + origin + ".");
                    else required.Require(origin);
                }
                if (!node.Dependencies.Any(e => e.Node == child && e.Origin == origin)) node.Dependencies.Add((child, origin));
            }
        }
    }
}
