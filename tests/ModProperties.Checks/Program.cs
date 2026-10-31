using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.ViewModels;
using Knossos.NET.Views;
using Knossos.NET;

sealed class CheckApplication : Application
{
    public override void Initialize()
    {
        var origin = new Uri("avares://Knossos.NET/App.axaml");
        Styles.Add(new StyleInclude(origin) { Source = new Uri("avares://Knossos.NET/AppStyles.axaml") });
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(origin) { Source = new Uri("avares://Knossos.NET/Styles/Futuristic.axaml") });
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Resources["escapeUnderscoresConverter"] = new Knossos.NET.Converters.EscapeUnderscoresConverter();
    }
}

sealed class Catalog : ModPackageCatalog
{
    public bool CanDevelop { get; set; } = true;
    public bool UseActualInstalled { get; set; }
    public int InstalledReads, DetailReads, VersionReads, BuildReads;
    public (int, int, int, int) Reads => (InstalledReads, DetailReads, VersionReads, BuildReads);
    public List<Mod> Available { get; } = new();
    public TaskCompletionSource<List<Mod>>? PendingVersions { get; set; }
    public List<Mod> Installed { get; } = new();
    public Dictionary<string, TaskCompletionSource<Mod>> Pending { get; } = new();
    public override Task<List<Mod>> GetVersionsAsync(string id) { VersionReads++; return PendingVersions?.Task ?? Task.FromResult(Available.Where(m => m.id == id).Select(Copy).ToList()); }
    public override Task<Mod> GetDetailsAsync(Mod mod) { DetailReads++; return Pending.TryGetValue(Key(mod), out var pending) ? pending.Task : Task.FromResult(Copy(mod.installed ? mod : Available.FirstOrDefault(m => Key(m) == Key(mod)) ?? mod)); }
    public override List<Mod> GetInstalled(string id) { InstalledReads++; return UseActualInstalled ? base.GetInstalled(id) : Installed.Where(m => m.id == id).Select(Copy).ToList(); }
    public override Mod? GetInstalledBuild(ModDependency dependency) { BuildReads++; return null; }
    public override Task<bool> CanDevelopAsync(string id) => Task.FromResult(CanDevelop);
}

static class Program
{
    static int assertions;
    static void Check(bool value, string description)
    {
        if (!value) throw new Exception(description);
        assertions++;
        Console.WriteLine("PASS " + description);
    }
    static void Pump(Task? task = null)
    {
        var limit = DateTime.UtcNow.AddSeconds(20);
        do
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        } while (task != null && !task.IsCompleted && DateTime.UtcNow < limit);
        if (task != null) { Check(task.IsCompleted, "async operation completes"); task.GetAwaiter().GetResult(); }
        for (int i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    }
    static Mod Make(string id, string version, params ModPackage[] packages) => new()
    { id = id, title = id == "blue-planet" ? "Blue Planet Complete" : id, version = version, type = ModType.tc,
      fullDataLoaded = true, firstRelease = "2026-10-01", description = "A test mod.", packages = packages.ToList() };
    static ModPackage Pkg(string name, string status, params ModDependency[] dependencies) => new()
    { name = name, status = status, notes = "Description of " + name + ".", dependencies = dependencies,
      files = new[] { new ModFile { filesize = 1024 } } };
    static ModDependency Dep(string id, params string[] packages) => new() { id = id, version = ">=1.0.0", packages = packages.ToList() };
    static ModPackagePlanNode Resolve(Catalog catalog, Mod mod, Dictionary<string, bool> preferences, bool dev, out ModPackagePlanner planner)
    {
        planner = new ModPackagePlanner(catalog, preferences, dev, _ => { });
        var task = planner.BuildAsync(mod, CancellationToken.None);
        Pump(task);
        return task.Result;
    }
    static void PlannerChecks()
    {
        var catalog = new Catalog();
        var main = Make("blue-planet", "2.0.0", Pkg("core", "required", Dep("assets", "HD")),
            Pkg("voices", "optional", Dep("voices")), Pkg("music", "recommended"));
        catalog.Available.AddRange(new[] { main,
            Make("assets", "1.0.0", Pkg("core", "required"), Pkg("HD", "optional", Dep("textures")), Pkg("bonus", "optional", Dep("bonus"))),
            Make("voices", "1.0.0", Pkg("voice files", "required", Dep("radio"))),
            Make("textures", "1.0.0", Pkg("core", "required")), Make("bonus", "1.0.0", Pkg("core", "required")),
            Make("radio", "1.0.0", Pkg("core", "required")) });
        var prefs = new Dictionary<string, bool>();
        var root = Resolve(catalog, main, prefs, false, out var plan);
        Check(root.Packages.Single(p => p.Name == "core").IsRequired, "required package is locked");
        Check(root.Packages.Single(p => p.Name == "music").IsSelected, "recommended package is selected by default");
        Check(!root.Packages.Single(p => p.Name == "voices").IsSelected, "optional package is initially unchecked");
        Check(plan.Nodes.All(n => n.Mod.id != "voices" && n.Mod.id != "radio" && n.Mod.id != "bonus"), "unchecked packages do not add dependencies at any depth");
        var hd = plan.Nodes.Single(n => n.Mod.id == "assets").Packages.Single(p => p.Name == "HD");
        Check(hd.IsSelected && !hd.CanToggle && hd.RequiredBy.Contains("core"), "an optional dependency package is forced and explains its parent");
        hd.IsSelected = false;
        Check(hd.IsSelected, "forced package rejects deselection");
        Check(plan.Nodes.Any(n => n.Mod.id == "textures"), "a forced optional package contributes its own dependency");
        prefs[Key(main, "voices")] = true;
        root = Resolve(catalog, main, prefs, false, out plan);
        Check(plan.Nodes.Any(n => n.Mod.id == "voices") && plan.Nodes.Any(n => n.Mod.id == "radio"), "checking a package adds its complete dependency branch");
        prefs[Key(main, "voices")] = false;
        Resolve(catalog, main, prefs, false, out plan);
        Check(plan.Nodes.All(n => n.Mod.id != "voices" && n.Mod.id != "radio"), "unchecking removes its exclusive dependency branch");
        prefs["assets@1.0.0/bonus"] = true;
        Resolve(catalog, main, prefs, false, out plan);
        Check(plan.Nodes.Any(n => n.Mod.id == "bonus"), "dependency package selections also expand the tree");
        var optionalParent = Make("optional-parent", "1.0.0", Pkg("core", "required", Dep("assets")), Pkg("HD support", "optional", Dep("support")));
        catalog.Available.AddRange(new[] { optionalParent, Make("support", "1.0.0", Pkg("core", "required", Dep("assets", "HD"))) });
        prefs.Clear(); prefs["optional-parent@1.0.0/HD support"] = true;
        Resolve(catalog, optionalParent, prefs, false, out plan);
        Check(plan.Nodes.Single(n => n.Mod.id == "assets").Packages.Single(p => p.Name == "HD").IsRequired, "shared dependency remains forced while its requiring branch is selected");
        prefs["optional-parent@1.0.0/HD support"] = false;
        Resolve(catalog, optionalParent, prefs, false, out plan);
        Check(!plan.Nodes.Single(n => n.Mod.id == "assets").Packages.Single(p => p.Name == "HD").IsRequired, "removing the requiring branch unlocks a still-shared dependency");
        Check(plan.Nodes.All(n => n.Mod.id != "textures"), "removing a forced package prunes its dependencies");
        Resolve(catalog, main, new(), true, out plan);
        Check(plan.Nodes.Single(n => n.Mod.id == main.id).Packages.All(p => p.IsRequired && p.IsSelected), "new developer installs force every own package");
        Check(plan.Nodes.Single(n => n.Mod.id == "assets").Packages.Single(p => p.Name == "bonus").CanToggle, "developer mode does not force optional dependency packages");
        var installed = Copy(main); installed.installed = true; installed.devMode = true;
        installed.packages.Single(p => p.name == "voices").isEnabled = false;
        catalog.Installed.Add(installed);
        root = Resolve(catalog, main, new(), true, out plan);
        var voice = root.Packages.Single(p => p.Name == "voices");
        Check(voice.Installed && !voice.IsSelected && voice.CanToggle && voice.Action.Contains("files kept"), "installed developer package uses persisted activation");
        Check(root.ToInstallMod(true).packages.All(p => p.isSelected), "developer task snapshot retains all files even for inactive packages");
        Check(catalog.Installed[0].packages.Count == 3 && !catalog.Installed[0].packages.Single(p => p.name == "voices").isEnabled, "planning never mutates installed metadata");
        catalog.Installed.Clear();
        var old = Make("blue-planet", "1.0.0", Pkg("core", "required", Dep("assets", "HD")), Pkg("voices", "optional"), Pkg("music", "recommended"));
        catalog.Available.Add(old);
        var oldInstalled = Copy(old); oldInstalled.installed = true; oldInstalled.packages.RemoveAll(p => p.name != "core"); catalog.Installed.Add(oldInstalled);
        root = Resolve(catalog, main, new(), false, out plan);
        Check(root.Packages.Where(p => p.Name != "core").All(p => !p.IsSelected && p.History.Contains("1.0.0")), "new version remembers skipped optional and recommended packages");
        root = Resolve(catalog, old, new(), false, out plan);
        Check(root.Packages.Any(p => p.Name == "voices" && !p.Installed && p.CanToggle), "installed versions expose published optional packages that have not been downloaded");
        var broken = Make("broken", "1.0.0", Pkg("core", "required", Dep("missing")));
        catalog.Available.Add(broken);
        Resolve(catalog, broken, new(), false, out plan);
        Check(plan.Errors.Count == 1 && plan.Errors[0].Contains("missing"), "unavailable dependency produces a visible plan error");
        var duplicates = Make("duplicates", "1.0.0", Pkg("core", "required", Dep("assets", "HD"), Dep("assets", "bonus")));
        catalog.Available.Add(duplicates);
        Resolve(catalog, duplicates, new(), false, out plan);
        Check(!plan.Nodes.Single(n => n.Mod.id == "assets").Packages.Single(p => p.Name == "bonus").IsRequired, "legacy duplicate filtering is preserved without merging package lists");
        var a = Make("a", "1.0.0", Pkg("core", "required", Dep("b")));
        var b = Make("b", "1.0.0", Pkg("core", "required", Dep("a")));
        catalog.Available.AddRange(new[] { a, b });
        root = Resolve(catalog, a, new(), false, out plan);
        Check(plan.Nodes.Count == 2 && new ModPackageTreeItem(root, "root").Children.Count > 0, "cyclic dependencies terminate in planner and display tree");
    }
    static string Key(Mod mod, string package) => ModPackageCatalog.Key(mod) + "/" + package;
    static Mod Copy(Mod mod) => ModPackageCatalog.Copy(mod);
    static void IncrementalChecks(string output)
    {
        var catalog = new Catalog { CanDevelop = false };
        var main = Make("incremental", "1.0.0", Pkg("missions", "required", Dep("assets")),
            Pkg("music", "recommended"), Pkg("voices", "optional", Dep("voices")),
            Pkg("HD support", "optional", Dep("support")), Pkg("streaming", "optional", Dep("stream")));
        main.title = "FS1 COOP UP";
        var slow = Make("stream", "1.0.0", Pkg("core", "required"));
        catalog.Available.AddRange(new[] { main,
            Make("assets", "1.0.0", Pkg("core", "required"), Pkg("HD", "optional", Dep("textures")), Pkg("extras", "optional")),
            Make("voices", "1.0.0", Pkg("core", "required", Dep("radio"))),
            Make("radio", "1.0.0", Pkg("core", "required")), Make("textures", "1.0.0", Pkg("core", "required")),
            Make("support", "1.0.0", Pkg("core", "required", Dep("assets", "HD"))), slow });
        catalog.Pending[ModPackageCatalog.Key(slow)] = new TaskCompletionSource<Mod>();
        using var owner = new ModPropertiesViewModel(main, menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        Pump(owner.Versions.CurrentLoad);
        var model = owner.Versions;
        var root = model.Root!;
        var music = root.Packages.Single(p => p.Name == "music");
        var assets = model.Dependencies.Single();
        Check(!assets.IsExpanded && assets.Children.All(c => !c.IsExpanded), "all dependency levels start collapsed");
        assets.IsExpanded = true;
        var hd = assets.Children.Single(c => c.Selection?.Name == "HD").Selection!;
        hd.IsDetailsExpanded = true;
        var rootEvents = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        var childEvents = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        model.Dependencies.CollectionChanged += (_, e) => rootEvents.Add(e.Action);
        assets.Children.CollectionChanged += (_, e) => childEvents.Add(e.Action);
        var reads = catalog.Reads;
        var load = model.CurrentLoad;
        var size = model.DownloadSize;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 50; i++) music.IsSelected = !music.IsSelected;
        music.IsSelected = false;
        stopwatch.Stop();
        Console.WriteLine("51 local package toggles: " + stopwatch.Elapsed.TotalMilliseconds.ToString("F2") + " ms");
        Check(catalog.Reads == reads && ReferenceEquals(load, model.CurrentLoad), "plain package toggles perform no catalog reads or dependency rebuild");
        Check(ReferenceEquals(root, model.Root) && ReferenceEquals(music, model.Root!.Packages.Single(p => p.Name == "music")), "checkbox updates preserve root and package row identity");
        Check(rootEvents.Count == 0 && childEvents.Count == 0 && assets.IsExpanded && hd.IsDetailsExpanded, "local toggles leave dependency collections and expansion state untouched");
        Check(model.DownloadSize != size && !model.IsLoading, "local toggles update byte estimates immediately without a loading phase");
        var extras = assets.Children.Single(c => c.Selection?.Name == "extras").Selection!;
        extras.IsSelected = true;
        Check(catalog.Reads == reads && childEvents.Count == 0, "plain dependency package toggles also avoid metadata reads and tree rebuilds");
        var voices = root.Packages.Single(p => p.Name == "voices");
        voices.IsSelected = true; Pump(model.CurrentLoad);
        var voiceTree = model.Dependencies.Single(c => c.Key == "mod:voices@1.0.0");
        Check(!voiceTree.IsExpanded && voiceTree.Children.Where(c => c.IsGroup).All(c => !c.IsExpanded), "new dependencies and their nested dependencies remain collapsed");
        Check(ReferenceEquals(assets, model.Dependencies[0]) && assets.IsExpanded && hd.IsDetailsExpanded, "adding a branch preserves existing dependency rows and details");
        Check(catalog.InstalledReads == reads.Item1 + 2 && catalog.DetailReads == reads.Item2 + 2, "adding a branch loads only its two new mods");
        var afterAdding = catalog.Reads;
        voices.IsSelected = false; Pump(model.CurrentLoad);
        Check(catalog.Reads == afterAdding && model.Dependencies.Count == 1, "removing a branch uses existing snapshots without rereading installed JSON");
        Check(!rootEvents.Contains(System.Collections.Specialized.NotifyCollectionChangedAction.Reset), "dependency updates use precise collection changes instead of resetting the tree");
        root.Packages.Single(p => p.Name == "HD support").IsSelected = true; Pump(model.CurrentLoad);
        Check(hd.IsRequired && hd.IsSelected && !hd.CanToggle && hd.IsDetailsExpanded, "incremental updates enforce shared requirements without replacing package rows");
        root.Packages.Single(p => p.Name == "HD support").IsSelected = false; Pump(model.CurrentLoad);
        Check(!hd.IsRequired && !hd.IsSelected && assets.Children.All(c => c.Key != "mod:textures@1.0.0"), "removing a requirement restores the old choice and prunes its nested branch");
        var streaming = root.Packages.Single(p => p.Name == "streaming");
        streaming.IsSelected = true;
        var pending = model.CurrentLoad;
        Pump();
        Check(model.IsUpdatingDependencies && model.CanEdit && !model.IsLoading, "new metadata loads leave checkboxes responsive");
        music.IsSelected = true;
        streaming.IsSelected = false; Pump(model.CurrentLoad);
        catalog.Pending[ModPackageCatalog.Key(slow)].SetResult(slow); Pump(pending);
        Check(!streaming.IsSelected && music.IsSelected && model.Dependencies.All(c => c.Key != "mod:stream@1.0.0"), "rapid changes cannot publish a cancelled branch or revert another checkbox");
        var view = new ModPropertiesView { DataContext = owner };
        var window = new Window { Content = view, Width = 1600, Height = 960 };
        assets.IsExpanded = false;
        window.Show(); Pump();
        var page = view.GetVisualDescendants().OfType<ModVersionsView>().Single();
        var tree = page.GetVisualDescendants().OfType<TreeView>().Single();
        var container = tree.ContainerFromItem(assets) as TreeViewItem;
        Check(container != null && !container.IsExpanded, "rendered dependency containers are collapsed by default");
        var row = page.GetVisualDescendants().OfType<ModPackageRowView>().First(v => ReferenceEquals(v.DataContext, music));
        Check(row.Bounds.Height <= 40 && !music.IsDetailsExpanded, "collapsed package rows use only a checkbox and name line");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "compact-desktop.png"));
        music.IsDetailsExpanded = true; Pump();
        Check(row.Bounds.Height > 40, "package descriptions and status can be expanded on demand");
        music.IsDetailsExpanded = false;
        container!.IsExpanded = true; Pump();
        Check(assets.IsExpanded, "expansion changes in the control persist in its view model");
        voices.IsSelected = true; Pump(model.CurrentLoad);
        Check(ReferenceEquals(tree.ContainerFromItem(assets), container) && container.IsExpanded, "adding another dependency preserves the rendered expanded container");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "compact-expanded.png"));
        window.Width = 390; window.Height = 844; Pump();
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "compact-mobile.png"));
        Check(page.FindControl<Grid>("PageLayout")!.Bounds.Width <= page.Bounds.Width, "compact package rows fit the phone viewport");
        window.Close();
    }
    static void EnginePackageChecks()
    {
        ModPackage EnginePackage(string name, string environment)
        {
            var package = Pkg(name, "required");
            package.environment = environment;
            package.executables = new List<ModExecutable> { new ModExecutable() };
            return package;
        }
        var packages = new[] { EnginePackage("Win32-AVX", "windows && avx"), EnginePackage("Win32-SSE2", "windows"),
            EnginePackage("Win64-AVX", "windows && X86_64 && avx"), EnginePackage("Win64-SSE2", "windows && X86_64") };
        var previousForce = Knossos.NET.Knossos.globalSettings.forceSSE2;
        try
        {
            Knossos.NET.Knossos.globalSettings.forceSSE2 = false;
            var best = FsoBuild.SelectBestPackage(packages);
            if (KnUtils.IsWindows && KnUtils.CpuArch == "X64" && KnUtils.CpuAVX)
                Check(best?.name == "Win64-AVX", "Windows x64 with AVX chooses only Win64-AVX");
            var catalog = new Catalog { CanDevelop = false };
            var engine = Make("scored-engine", "1.0.0", packages); engine.type = ModType.engine;
            var main = Make("engine-parent", "1.0.0", Pkg("core", "required", Dep(engine.id)));
            catalog.Available.AddRange(new[] { main, engine });
            Resolve(catalog, main, new(), false, out var plan);
            if (best != null)
            {
                var node = plan.Nodes.Single(n => n.Engine);
                Check(node.Packages.Count == 1 && node.Packages[0].Name == best.name, "engine dependency displays only its highest scoring package");
                Check(node.ToInstallMod(false).packages.Count == 1 && node.ToInstallMod(false).packages[0].name == best.name, "engine install snapshot and byte estimate use the same single package");
                Knossos.NET.Knossos.globalSettings.forceSSE2 = true;
                Check(!FsoBuild.SelectBestPackage(packages)!.name.Contains("AVX"), "package selection still honors the force-SSE2 setting");
            }
            var incompatible = EnginePackage("unsupported", KnUtils.IsWindows ? "macosx && ARM64" : "windows && ARM64");
            engine.packages = new List<ModPackage> { incompatible };
            Resolve(catalog, main, new(), false, out plan);
            Check(plan.Nodes.All(n => !n.Engine) && plan.Errors.Count == 0, "an incompatible engine is omitted without blocking mod installation");
            using var owner = new ModPropertiesViewModel(main, menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
            Pump(owner.Versions.CurrentLoad);
            Check(owner.Versions.CanApply && owner.Versions.Dependencies.Count == 0, "Install remains available when no engine has a compatible package");
        }
        finally { Knossos.NET.Knossos.globalSettings.forceSSE2 = previousForce; }
    }
    static void BackupChecks(string output)
    {
        var library = Path.Combine(output, "library");
        var version = Path.Combine(library, "tc", "mod-1.0.0");
        Directory.CreateDirectory(Path.Combine(version, "kn_images"));
        File.WriteAllText(Path.Combine(version, "mod.json"), "original metadata");
        File.WriteAllText(Path.Combine(version, "mod_settings.json"), "settings");
        File.WriteAllText(Path.Combine(version, "edited.tbl"), "original files");
        File.WriteAllText(Path.Combine(version, "kn_images", "banner.png"), "banner");
        using (var backup = new ModReinstallBackup(library, version, version))
        {
            Check(!File.Exists(Path.Combine(version, "edited.tbl")), "clean reinstall starts without the original game files");
            Check(File.Exists(Path.Combine(version, "kn_images", "banner.png")) && File.Exists(Path.Combine(version, "mod_settings.json")), "clean reinstall preserves settings and cached media");
            File.WriteAllText(Path.Combine(version, "new.tbl"), "partial download");
        }
        Check(File.ReadAllText(Path.Combine(version, "edited.tbl")) == "original files" && !File.Exists(Path.Combine(version, "new.tbl")), "failed or cancelled replacement restores the original directory");
        using (var backup = new ModReinstallBackup(library, version, version))
        {
            File.WriteAllText(Path.Combine(version, "new.tbl"), "completed");
            backup.Commit();
            Check(!Directory.Exists(backup.BackupPath), "successful replacement removes its backup");
        }
        Check(File.Exists(Path.Combine(version, "new.tbl")) && !File.Exists(Path.Combine(version, "edited.tbl")), "committed reinstall keeps only replacement files");
        bool rejected = false;
        try { using var backup = new ModReinstallBackup(library, output, output); } catch (IOException) { rejected = true; }
        Check(rejected, "reinstall rejects a target outside the configured library");
    }
    static void TaskChecks(string output)
    {
        var library = Path.Combine(output, "task-library");
        Knossos.NET.Knossos.globalSettings.basePath = library;
        Knossos.NET.Knossos.globalSettings.modCompression = CompressionSettings.Disabled;
        var tasks = new TaskViewModel();
        var mod = Make("task-fixture", "1.0.0", Pkg("core", "required"), Pkg("extra", "optional"));
        mod.installed = true;
        mod.fullPath = Path.Combine(library, mod.id, mod.id + "-" + mod.version);
        Directory.CreateDirectory(mod.fullPath);
        foreach (var pkg in mod.packages) pkg.files = Array.Empty<ModFile>();
        mod.packages[1].filelist = new[] { new ModFilelist { filename = "extra.tbl" } };
        File.WriteAllText(Path.Combine(mod.fullPath, "extra.tbl"), "optional data");
        mod.SaveJson();
        Knossos.NET.Knossos.AddMod(mod);
        var snapshot = Copy(mod);
        snapshot.packages[0].isSelected = true;
        snapshot.packages[1].isSelected = false;
        var remove = tasks.InstallModAsync(snapshot);
        Pump(remove);
        Check(remove.Result && !File.Exists(Path.Combine(mod.fullPath, "extra.tbl")) && mod.packages.All(p => p.name != "extra"), "normal modification removes unchecked files and package metadata");
        mod.devMode = true;
        mod.packages.Add(Pkg("extra", "optional"));
        mod.packages[1].files = Array.Empty<ModFile>();
        mod.packages[1].filelist = new[] { new ModFilelist { filename = "extra.tbl" } };
        File.WriteAllText(Path.Combine(mod.fullPath, "extra.tbl"), "developer edits");
        mod.SaveJson();
        var catalog = new Catalog(); catalog.Available.Add(Copy(mod)); catalog.Installed.Add(mod);
        using (var owner = new ModPropertiesViewModel(mod, menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog))
        {
            Pump(owner.Versions.CurrentLoad);
            owner.Versions.Root!.Packages.Single(p => p.Name == "extra").IsSelected = false;
            Pump(owner.Versions.CurrentLoad);
            var apply = owner.Versions.ApplyCommand.ExecuteAsync(null);
            Pump(apply);
            mod.ReLoadJson();
            Check(File.ReadAllText(Path.Combine(mod.fullPath, "extra.tbl")) == "developer edits" && !mod.packages.Single(p => p.name == "extra").isEnabled,
                "developer Apply saves inactive state and preserves edited files");
        }
        snapshot = Copy(mod);
        snapshot.description = "Updated metadata";
        snapshot.packages.ForEach(p => p.isSelected = true);
        var repair = tasks.InstallModAsync(snapshot, new List<ModPackage> { snapshot.packages.Single(p => p.name == "extra") });
        Pump(repair);
        mod.ReLoadJson();
        Check(repair.Result && !mod.packages.Single(p => p.name == "extra").isEnabled, "repairing developer packages preserves their saved activation");
        mod.devMode = false;
        mod.packages.RemoveAll(p => p.name == "extra");
        mod.SaveJson();
        snapshot = Copy(mod); snapshot.packages.ForEach(p => p.isSelected = true);
        var reinstall = tasks.InstallModAsync(snapshot, reinstallCurrentVersion: true);
        Pump(reinstall);
        mod.ReLoadJson();
        Check(reinstall.Result && !File.Exists(Path.Combine(mod.fullPath, "extra.tbl")) && mod.packages.Any(p => p.name == "core"), "clean task reinstalls the selected package set and removes the old version files");
        var verifyBytes = new byte[4 * 1024 * 1024];
        File.WriteAllBytes(Path.Combine(mod.fullPath, "verify.tbl"), verifyBytes);
        mod.packages[0].filelist = new[] { new ModFilelist { filename = "verify.tbl", checksum = new[] { "sha256", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(verifyBytes)) } } };
        mod.SaveJson();
        catalog = new Catalog(); catalog.Available.Add(Copy(mod)); catalog.Installed.Add(mod);
        using (var owner = new ModPropertiesViewModel(mod, menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog))
        {
            Pump(owner.Versions.CurrentLoad);
            var verify = owner.Versions.VerifyCommand.ExecuteAsync(null);
            Check(owner.Versions.IsApplying && owner.Versions.ActiveTask?.Name.StartsWith("Verifying") == true, "verification exposes live progress inside Properties");
            owner.Versions.CancelOperationCommand.Execute(null);
            Pump(verify);
            Check(!owner.Versions.IsApplying && owner.Versions.Status.Contains("stopped"), "verification can be cancelled from Properties without opening another window");
        }
        File.WriteAllText(Path.Combine(mod.fullPath, "original.tbl"), "restore me");
        snapshot = Copy(mod); snapshot.packages.ForEach(p => p.isSelected = true);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var canceled = new TaskItemViewModel().InstallMod(snapshot, cancel, reinstallCurrentVersion: true);
        Pump(canceled);
        Check(!canceled.Result && File.ReadAllText(Path.Combine(mod.fullPath, "original.tbl")) == "restore me", "cancelled clean task rolls back the complete version directory");
    }

    static bool IsDisposed(ModPropertiesViewModel owner) => (bool)typeof(ModPropertiesViewModel)
        .GetField("disposed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner)!;

    static void SessionChecks(string output)
    {
        Knossos.NET.Knossos.globalSettings.basePath = Path.Combine(output, "session-library");
        Knossos.NET.Knossos.globalSettings.modCompression = CompressionSettings.Disabled;
        var tasks = new TaskViewModel();
        var queue = (Queue<TaskItemViewModel>)typeof(TaskViewModel).GetProperty("taskQueue",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(tasks)!;
        var catalog = new Catalog { CanDevelop = false, UseActualInstalled = true };
        Mod Fixture(string id)
        {
            var mod = Make(id, "1.0.0", Pkg("core", "required"));
            mod.packages[0].files = Array.Empty<ModFile>();
            catalog.Available.Add(mod);
            return mod;
        }
        var host = new Window { Width = 1600, Height = 960 }; host.Show();
        var mod = Fixture("resumable-install");
        queue.Enqueue(new TaskItemViewModel());
        var firstView = new ModPropertiesView();
        var owner = ModPropertiesViewModel.GetOrCreate(mod, firstView, ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        firstView.DataContext = owner;
        firstView.Show(host); Pump(owner.Versions.CurrentLoad);
        var root = owner.Versions.Root;
        var apply = owner.Versions.ApplyCommand.ExecuteAsync(null); Pump();
        Check(owner.Versions.IsApplying && owner.Versions.ActiveTask != null, "queued installation exposes its live task");
        var active = owner.Versions.ActiveTask!;
        active.ProgressBarMax = 100; active.ProgressCurrent = 37;
        firstView.Close(); Pump();
        Check(firstView.DataContext == null && !IsDisposed(owner) && owner.Versions.IsApplying &&
            typeof(ModPropertiesViewModel).GetField("view", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner) == null,
            "closing Properties detaches the view while installation continues");
        var secondView = new ModPropertiesView();
        var resumed = ModPropertiesViewModel.GetOrCreate(mod, secondView, ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        secondView.DataContext = resumed;
        secondView.Show(host); Pump();
        Check(ReferenceEquals(owner, resumed) && ReferenceEquals(root, resumed.Versions.Root) && ReferenceEquals(active, resumed.Versions.ActiveTask),
            "reopening the same mod resumes its exact plan and task without restarting installation");
        var page = secondView.GetVisualDescendants().OfType<ModVersionsView>().Single();
        Check(page.GetVisualDescendants().OfType<ProgressBar>().Any(p => p.Value == 37), "reopened Versions displays current progress");
        active.ProgressCurrent = 42; Pump();
        Check(page.GetVisualDescendants().OfType<ProgressBar>().Any(p => p.Value == 42), "progress remains live after reopening");
        using (var other = ModPropertiesViewModel.GetOrCreate(Fixture("other-mod"), catalog: catalog))
            Check(!ReferenceEquals(other, owner), "another mod cannot pick up the running session");
        queue.Dequeue(); Pump(apply);
        Check(!owner.Versions.IsApplying && !IsDisposed(owner), "completion keeps a reopened window usable");
        secondView.Close(); Pump();
        Check(IsDisposed(owner) && secondView.DataContext == null, "closing after completion releases the session");

        var hiddenMod = Fixture("closed-install");
        queue.Enqueue(new TaskItemViewModel());
        var hiddenView = new ModPropertiesView();
        var hidden = ModPropertiesViewModel.GetOrCreate(hiddenMod, hiddenView, ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        hiddenView.DataContext = hidden; hiddenView.Show(host); Pump(hidden.Versions.CurrentLoad);
        var hiddenApply = hidden.Versions.ApplyCommand.ExecuteAsync(null); Pump();
        hiddenView.Close(); Pump();
        Check(hidden.Versions.IsApplying && !IsDisposed(hidden), "closed active session is retained until its operation finishes");
        queue.Dequeue(); Pump(hiddenApply);
        Check(IsDisposed(hidden) && !hidden.Versions.IsApplying && hidden.Versions.ActiveTask == null,
            "completion while closed disposes the view models and releases task subscriptions");
        using (var fresh = ModPropertiesViewModel.GetOrCreate(hiddenMod, catalog: catalog))
            Check(!ReferenceEquals(hidden, fresh), "completed closed session is removed from the running-session registry");

        queue.Enqueue(new TaskItemViewModel());
        var verifyMod = Fixture("resumable-verify");
        verifyMod.installed = true;
        verifyMod.fullPath = Path.Combine(Knossos.NET.Knossos.globalSettings.basePath!, verifyMod.id, verifyMod.id + "-" + verifyMod.version);
        Directory.CreateDirectory(verifyMod.fullPath); verifyMod.SaveJson(); Knossos.NET.Knossos.AddMod(verifyMod);
        var verifyView = new ModPropertiesView();
        var verifyOwner = ModPropertiesViewModel.GetOrCreate(verifyMod, verifyView,
            ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        verifyView.DataContext = verifyOwner; verifyView.Show(host); Pump(verifyOwner.Versions.CurrentLoad);
        var verify = verifyOwner.Versions.VerifyCommand.ExecuteAsync(null); Pump();
        Check(verifyOwner.Versions.IsApplying && verifyOwner.Versions.ActiveTask?.Name.StartsWith("Verifying") == true,
            "verification also registers a resumable operation");
        verifyView.Close(); Pump();
        var cancelView = new ModPropertiesView();
        var cancelOwner = ModPropertiesViewModel.GetOrCreate(verifyMod, cancelView, ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        cancelView.DataContext = cancelOwner; cancelView.Show(host); Pump();
        Check(ReferenceEquals(cancelOwner, verifyOwner), "reopening during verification reconnects to its progress");
        cancelOwner.Versions.CancelOperationCommand.Execute(null);
        cancelView.Close(); Pump();
        queue.Dequeue(); Pump(verify);
        Check(IsDisposed(verifyOwner) && !verifyOwner.Versions.IsApplying, "cancellation while closed also releases the session after file operations finish");
        var failureMod = Fixture("closed-failure");
        failureMod.packages[0].folder = "../unsafe";
        queue.Enqueue(new TaskItemViewModel());
        var failureView = new ModPropertiesView();
        var failureOwner = ModPropertiesViewModel.GetOrCreate(failureMod, failureView, ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        failureView.DataContext = failureOwner; failureView.Show(host); Pump(failureOwner.Versions.CurrentLoad);
        var failure = failureOwner.Versions.ApplyCommand.ExecuteAsync(null); Pump();
        failureView.Close(); Pump(); queue.Dequeue(); Pump(failure);
        Check(IsDisposed(failureOwner) && failureOwner.Versions.Status.Contains("stopped"), "failed installation while closed also releases the session");
        host.Close();
    }

    static void DeletionChecks(string output)
    {
        Knossos.NET.Knossos.globalSettings.basePath = Path.Combine(output, "deletion-library");
        _ = new TaskViewModel();
        var catalog = new Catalog { CanDevelop = false, UseActualInstalled = true };
        Mod Installed(string version)
        {
            var mod = Make("delete-fixture", version, Pkg("core", "required"));
            mod.packages[0].files = Array.Empty<ModFile>();
            mod.fullPath = Path.Combine(Knossos.NET.Knossos.globalSettings.basePath!, mod.id, mod.id + "-" + version);
            Directory.CreateDirectory(mod.fullPath);
            File.WriteAllText(Path.Combine(mod.fullPath, "keep.tbl"), version);
            mod.installed = true; mod.SaveJson(); Knossos.NET.Knossos.AddMod(mod);
            catalog.Available.Add(Copy(mod));
            return mod;
        }
        var first = Installed("1.0.0"); var second = Installed("2.0.0");
        using var owner = new ModPropertiesViewModel(new List<Mod> { second, first }, 0, null,
            menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        Pump(owner.Versions.CurrentLoad);
        owner.Versions.SelectedVersion = owner.Versions.Versions.Single(v => v.Mod.version == first.version); Pump(owner.Versions.CurrentLoad);
        Check(owner.Versions.CanDelete, "installed normal version offers Delete");
        var cancelled = owner.Versions.DeleteCommand.ExecuteAsync(null); Pump();
        Check(owner.IsConfirmationModal && owner.ModalText.Contains("1.0.0"), "Delete confirms the selected package version rather than the active information version");
        owner.CloseModalCommand.Execute(null); Pump(cancelled);
        Check(Directory.Exists(first.fullPath) && Knossos.NET.Knossos.GetInstalledMod(first.id, first.version) != null,
            "cancelling deletion leaves installed files and metadata intact");
        var deletion = owner.Versions.DeleteCommand.ExecuteAsync(null); Pump();
        owner.ConfirmModalCommand.Execute(null); Pump(deletion);
        Check(!Directory.Exists(first.fullPath) && Knossos.NET.Knossos.GetInstalledMod(first.id, first.version) == null &&
            File.ReadAllText(Path.Combine(second.fullPath, "keep.tbl")) == "2.0.0", "deletion removes only the selected installed version");
        Check(!owner.Versions.IsInstalled && !owner.Versions.CanDelete && owner.IsInstalled && owner.ActiveVersion.version == second.version,
            "deletion refreshes package state while retaining another active installed version");
        var last = owner.DeleteCommand.ExecuteAsync(null); Pump();
        owner.ConfirmModalCommand.Execute(null); Pump(last);
        Check(!owner.IsInstalled && !owner.CanConfigure && !owner.Versions.IsInstalled && owner.CanDownload,
            "deleting the last version returns Properties to a downloadable state");
        Check(owner.Versions.Versions.All(v => !v.Label.Contains("Installed")), "deleted versions lose their installed marker");

        var dev = Installed("3.0.0"); dev.devMode = true; dev.SaveJson();
        using var devOwner = new ModPropertiesViewModel(dev, menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        Pump(devOwner.Versions.CurrentLoad);
        Check(!devOwner.Versions.CanDelete && !devOwner.Versions.DeleteCommand.CanExecute(null), "developer versions cannot delete files from Versions");
        var retail = Make("FS2", "1.0.0", Pkg("core", "required")); retail.installed = true;
        catalog.Available.Add(retail); catalog.UseActualInstalled = false; catalog.Installed.Add(retail);
        using var retailOwner = new ModPropertiesViewModel(retail, menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        Pump(retailOwner.Versions.CurrentLoad);
        Check(!retailOwner.Versions.CanDelete, "retail FS2 stays protected from version deletion");
    }

    static void QueueChecks(string output)
    {
        var library = Path.Combine(output, "queue-library");
        Knossos.NET.Knossos.globalSettings.basePath = library;
        Knossos.NET.Knossos.globalSettings.modCompression = CompressionSettings.Disabled;
        var tasks = new TaskViewModel();
        var queue = (Queue<TaskItemViewModel>)typeof(TaskViewModel).GetProperty("taskQueue",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(tasks)!;
        var catalog = new Catalog { CanDevelop = false, UseActualInstalled = true };
        Mod Fixture(string id, params ModPackage[] packages)
        {
            var mod = Make(id, "1.0.0", packages);
            foreach (var pkg in mod.packages) { pkg.files = Array.Empty<ModFile>(); pkg.isSelected = pkg.status != "optional"; }
            catalog.Available.Add(Copy(mod));
            return mod;
        }
        void Register(Mod mod)
        {
            mod.installed = true;
            mod.fullPath = Path.Combine(library, mod.id, mod.id + "-" + mod.version);
            Directory.CreateDirectory(mod.fullPath); mod.SaveJson(); Knossos.NET.Knossos.AddMod(mod);
        }
        var shared = Fixture("queued-shared", Pkg("core", "required"), Pkg("keep", "optional"),
            Pkg("voices", "optional"), Pkg("music", "optional"));
        var installed = Copy(shared); installed.packages.RemoveAll(p => p.name == "voices" || p.name == "music"); Register(installed);
        installed.packages.Single(p => p.name == "keep").filelist = new[] { new ModFilelist { filename = "keep.tbl" } };
        File.WriteAllText(Path.Combine(installed.fullPath, "keep.tbl"), "existing user files"); installed.SaveJson();
        var first = Fixture("queued-first", Pkg("core", "required", Dep(shared.id, "voices")));
        var second = Fixture("queued-second", Pkg("core", "required", Dep(shared.id, "music")));
        using var firstOwner = new ModPropertiesViewModel(first, menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        using var secondOwner = new ModPropertiesViewModel(second, menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        Pump(firstOwner.Versions.CurrentLoad); Pump(secondOwner.Versions.CurrentLoad);
        var dependencyKeep = firstOwner.Versions.Dependencies.SelectMany(d => d.Children).Single(p => p.Selection?.Name == "keep").Selection!;
        dependencyKeep.IsSelected = false;
        Check(dependencyKeep.IsSelected && !dependencyKeep.CanToggle, "installed dependency packages stay selected and cannot be removed by another mod");
        var blocker = new TaskItemViewModel(); queue.Enqueue(blocker);
        var firstApply = firstOwner.Versions.ApplyCommand.ExecuteAsync(null); Pump();
        var firstTask = firstOwner.Versions.ActiveTask;
        var secondApply = secondOwner.Versions.ApplyCommand.ExecuteAsync(null); Pump();
        var secondTask = secondOwner.Versions.ActiveTask;
        Check(firstOwner.Versions.IsApplying && secondOwner.Versions.IsApplying && !firstOwner.IsModalOpen && !secondOwner.IsModalOpen,
            "another mod can enqueue installation while an earlier request is pending without a removal question");
        Check(firstTask != null && secondTask != null && !ReferenceEquals(firstTask, secondTask) &&
            ReferenceEquals(firstOwner.Versions.ActiveTask, firstTask), "each Properties operation tracks only its own queued task");
        tasks.AddMessageTask("Unrelated task"); Pump();
        Check(ReferenceEquals(firstOwner.Versions.ActiveTask, firstTask) && ReferenceEquals(secondOwner.Versions.ActiveTask, secondTask),
            "unrelated tasks cannot replace either operation's progress");
        queue.Dequeue(); Pump(Task.WhenAll(firstApply, secondApply));
        installed.ReLoadJson();
        Check(installed.packages.Select(p => p.name).OrderBy(n => n).SequenceEqual(new[] { "core", "keep", "music", "voices" }),
            "stale shared-dependency requests add the union of requested packages without removing earlier additions");
        Check(File.ReadAllText(Path.Combine(installed.fullPath, "keep.tbl")) == "existing user files" &&
            Knossos.NET.Knossos.GetInstalledMod(first.id, first.version) != null && Knossos.NET.Knossos.GetInstalledMod(second.id, second.version) != null,
            "both queued mods finish and existing dependency files survive");
        var duplicate = Copy(shared); duplicate.packages.ForEach(p => p.isSelected = p.name == "core");
        var duplicateInstall = tasks.InstallModAsync(duplicate, addOnly: true); Pump(duplicateInstall); installed.ReLoadJson();
        Check(duplicateInstall.Result && installed.packages.Count == 4 && queue.Count == 0, "repeated additive installation becomes a successful no-op without duplicate packages");

        var cancellationA = Fixture("cancel-one", Pkg("core", "required", Dep("queued-shared")));
        var cancellationB = Fixture("cancel-two", Pkg("core", "required", Dep("queued-shared")));
        using var cancelOwner = new ModPropertiesViewModel(cancellationA, menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        using var continueOwner = new ModPropertiesViewModel(cancellationB, menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        Pump(cancelOwner.Versions.CurrentLoad); Pump(continueOwner.Versions.CurrentLoad);
        queue.Enqueue(blocker);
        var cancelled = cancelOwner.Versions.ApplyCommand.ExecuteAsync(null); Pump();
        var continued = continueOwner.Versions.ApplyCommand.ExecuteAsync(null); Pump();
        var continuedTask = continueOwner.Versions.ActiveTask;
        cancelOwner.Versions.CancelOperationCommand.Execute(null); Pump(cancelled);
        Check(continueOwner.Versions.IsApplying && continuedTask?.IsCancelled == false && queue.Peek() == blocker && queue.Count == 2,
            "cancelling a pending operation removes only its own task and preserves the other request's turn");
        queue.Dequeue(); Pump(continued);
        Check(Knossos.NET.Knossos.GetInstalledMod(cancellationA.id, cancellationA.version) == null &&
            Knossos.NET.Knossos.GetInstalledMod(cancellationB.id, cancellationB.version) != null, "cancellation leaves another queued installation able to finish");

        var late = Fixture("installed-while-waiting", Pkg("core", "required"), Pkg("extra", "optional"));
        using var lateOwner = new ModPropertiesViewModel(late, menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        Pump(lateOwner.Versions.CurrentLoad); queue.Enqueue(blocker);
        var lateApply = lateOwner.Versions.ApplyCommand.ExecuteAsync(null); Pump();
        var lateInstalled = Copy(late); Register(lateInstalled);
        lateInstalled.packages[1].filelist = new[] { new ModFilelist { filename = "extra.tbl" } };
        File.WriteAllText(Path.Combine(lateInstalled.fullPath, "extra.tbl"), "installed by an earlier request"); lateInstalled.SaveJson();
        queue.Dequeue(); Pump(lateApply); lateInstalled.ReLoadJson();
        Check(lateInstalled.packages.Any(p => p.name == "extra") && File.Exists(Path.Combine(lateInstalled.fullPath, "extra.tbl")),
            "a version installed during the wait preserves packages unchecked in the stale new-install plan");

        var explicitMod = Fixture("explicit-modification", Pkg("core", "required"), Pkg("remove", "optional"), Pkg("later", "optional"));
        var explicitInstalled = Copy(explicitMod); explicitInstalled.packages.RemoveAll(p => p.name == "later"); Register(explicitInstalled);
        explicitInstalled.packages[1].filelist = new[] { new ModFilelist { filename = "remove.tbl" } };
        File.WriteAllText(Path.Combine(explicitInstalled.fullPath, "remove.tbl"), "explicit removal"); explicitInstalled.SaveJson();
        using var explicitOwner = new ModPropertiesViewModel(explicitInstalled, menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        Pump(explicitOwner.Versions.CurrentLoad);
        explicitOwner.Versions.Root!.Packages.Single(p => p.Name == "remove").IsSelected = false;
        queue.Enqueue(blocker); var modification = explicitOwner.Versions.ApplyCommand.ExecuteAsync(null); Pump();
        Check(!explicitOwner.IsModalOpen && explicitOwner.Versions.IsApplying, "an explicit own-package change queues without a removal confirmation");
        var later = Copy(explicitMod).packages.Single(p => p.name == "later"); later.filelist = new[] { new ModFilelist { filename = "later.tbl" } };
        explicitInstalled.packages.Add(later); File.WriteAllText(Path.Combine(explicitInstalled.fullPath, "later.tbl"), "concurrent addition"); explicitInstalled.SaveJson();
        queue.Dequeue(); Pump(modification); explicitInstalled.ReLoadJson();
        Check(!File.Exists(Path.Combine(explicitInstalled.fullPath, "remove.tbl")) && File.Exists(Path.Combine(explicitInstalled.fullPath, "later.tbl")) &&
            explicitInstalled.packages.Any(p => p.name == "later"), "explicit modification removes only packages selected for removal in the original plan and preserves later additions");

        installed.devMode = true;
        installed.packages.Single(p => p.name == "music").isEnabled = false;
        installed.SaveJson();
        duplicate = Copy(shared); duplicate.packages.ForEach(p => p.isSelected = p.name == "voices");
        var devAdd = tasks.InstallModAsync(duplicate, addOnly: true); Pump(devAdd); installed.ReLoadJson();
        Check(installed.packages.Single(p => p.name == "keep").isEnabled && !installed.packages.Single(p => p.name == "music").isEnabled &&
            File.Exists(Path.Combine(installed.fullPath, "keep.tbl")), "additive requests preserve developer files and existing unrelated activation states");
        duplicate = Copy(shared); duplicate.packages.ForEach(p => p.isSelected = p.name == "music");
        devAdd = tasks.InstallModAsync(duplicate, addOnly: true); Pump(devAdd); installed.ReLoadJson();
        Check(installed.packages.Single(p => p.name == "music").isEnabled, "an additive dependency request can activate a required developer package without deactivating others");
        TaskItemViewModel? failedTask = null;
        using var failedOperation = new ModInstallOperation(t => failedTask = t);
        var bad = Fixture("cleanup-order", Pkg("core", "required"));
        bad.packages[0].files = new[] { new ModFile { filename = "payload.7z", dest = "../outside", filesize = 1 } };
        bool failureReported = false;
        var failed = tasks.InstallModAsync(bad, reportError: _ => failureReported = true, operation: failedOperation);
        var replacement = Fixture("cleanup-order", Pkg("core", "required"));
        var next = tasks.InstallModAsync(replacement, addOnly: true); Pump();
        Check(failureReported && failedTask?.IsCancelled == true && !next.IsCompleted && queue.Peek() == failedTask && !tasks.IsSafeState(),
            "a failed task keeps its queue slot and blocks deletion until partial-file cleanup finishes");
        Pump(Task.WhenAll(failed, next));
        var replacementInstalled = Knossos.NET.Knossos.GetInstalledMod(replacement.id, replacement.version);
        Check(!failed.Result && next.Result && replacementInstalled != null && File.Exists(Path.Combine(replacementInstalled.fullPath, "mod.json")),
            "failure cleanup cannot delete files written by the next installation of the same version");

        var old = Fixture("reserved-cleanup", Pkg("core", "required")); Register(old);
        var upgrade = Copy(old); upgrade.installed = false; upgrade.fullPath = ""; upgrade.version = "2.0.0";
        using (var reservation = new ModInstallOperation(_ => { }, new[] { old }))
        {
            var upgradeTask = tasks.InstallModAsync(upgrade, cleanupOldVersions: true, addOnly: true); Pump(upgradeTask);
            Check(upgradeTask.Result && Knossos.NET.Knossos.GetInstalledMod(old.id, old.version) != null && Directory.Exists(old.fullPath),
                "old-version cleanup preserves a dependency reserved by another pending operation");
        }
        upgrade = Copy(old); upgrade.installed = false; upgrade.fullPath = ""; upgrade.version = "3.0.0";
        var cleanup = tasks.InstallModAsync(upgrade, cleanupOldVersions: true, addOnly: true); Pump(cleanup);
        Check(cleanup.Result && Knossos.NET.Knossos.GetInstalledMod(old.id, old.version) == null,
            "finished operations release reservations so later explicit cleanup can remove unused old versions");

        var compressed = Fixture("compressed-addition", Pkg("core", "required"), Pkg("extra", "optional"));
        var compressedInstalled = Copy(compressed); compressedInstalled.packages.RemoveAll(p => p.name == "extra"); Register(compressedInstalled);
        compressedInstalled.modSettings.SetInitialFilePath(compressedInstalled.fullPath);
        compressedInstalled.modSettings.customCmdLine = "-nomusic";
        compressedInstalled.modSettings.isCompressed = true; compressedInstalled.modSettings.Save();
        var compressedRequest = Copy(compressed); compressedRequest.modSettings = new ModSettings();
        compressedRequest.packages.ForEach(p => p.isSelected = true);
        var compressedAdd = tasks.InstallModAsync(compressedRequest, addOnly: true); Pump(compressedAdd);
        var savedSettings = new ModSettings(); savedSettings.Load(compressedInstalled.fullPath);
        Check(compressedAdd.Result && savedSettings.isCompressed && savedSettings.customCmdLine == "-nomusic",
            "adding packages reuses the current installation settings and preserves custom launch options during compression");

        queue.Enqueue(blocker);
        var compressedVerify = tasks.VerifyModAsync(Copy(explicitInstalled)); Pump();
        explicitInstalled.modSettings.SetInitialFilePath(explicitInstalled.fullPath);
        explicitInstalled.modSettings.isCompressed = true; explicitInstalled.modSettings.Save();
        queue.Dequeue(); Pump(compressedVerify);
        Check(!compressedVerify.Result && tasks.IsSafeState() && queue.Count == 0,
            "verification rechecks compression after waiting and leaves no unfinished task blocking the queue");

    }

    static void SharedBuildChecks(string output)
    {
        var library = Path.Combine(output, "shared-build-library");
        Knossos.NET.Knossos.globalSettings.basePath = library;
        var tasks = new TaskViewModel(); var builds = new FsoBuildsViewModel();
        var queue = (Queue<TaskItemViewModel>)typeof(TaskViewModel).GetProperty("taskQueue",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(tasks)!;
        var environment = (KnUtils.IsWindows ? "windows" : KnUtils.IsLinux ? "linux" : "macosx") + " && " +
            (KnUtils.CpuArch == "X64" ? "X86_64" : KnUtils.CpuArch == "Arm64" ? "ARM64" : "X86");
        var package = Pkg("platform", "required"); package.files = Array.Empty<ModFile>(); package.environment = environment;
        package.executables = new List<ModExecutable> { new() { file = "fixture.exe", properties = FsoBuild.FillProperties(environment) } };
        var mod = Make("FSO", "99.0.0", package); mod.type = ModType.engine; mod.stability = "stable";
        var item = new FsoBuildItemViewModel(new FsoBuild(mod)); builds.StableItems.Add(item);
        TaskItemViewModel? firstTask = null, secondTask = null;
        using var first = new ModInstallOperation(t => firstTask = t); using var second = new ModInstallOperation(t => secondTask = t);
        var blocker = new TaskItemViewModel(); queue.Enqueue(blocker);
        var firstInstall = builds.InstallDependencyAsync(Copy(mod), operation: first); Pump();
        var secondInstall = builds.InstallDependencyAsync(Copy(mod), operation: second); Pump();
        Check(!firstInstall.IsCompleted && !secondInstall.IsCompleted && firstTask != null && secondTask != null && !ReferenceEquals(firstTask, secondTask),
            "two mods may wait for the same FSO build without the second being rejected as already downloading");
        var installed = Copy(mod); installed.installed = true; installed.fullPath = Path.Combine(library, "bin", "FSO-99.0.0") + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(installed.fullPath); File.WriteAllText(Path.Combine(installed.fullPath, "fixture.exe"), "fixture only; never executed");
        installed.SaveJson(); var build = new FsoBuild(installed); Knossos.NET.Knossos.AddBuild(build);
        Check(build.IsValidBuild(), "shared-build fixture is compatible with this platform");
        queue.Dequeue(); Pump(Task.WhenAll(firstInstall, secondInstall));
        Check(firstInstall.Result && secondInstall.Result && firstTask!.Info == "Already installed" && secondTask!.Info == "Already installed" &&
            Knossos.NET.Knossos.GetInstalledBuildsList("FSO").Count(b => b.version == "99.0.0") == 1,
            "each queued FSO request rechecks the installed build and reuses it without another download or duplicate registration");
        using var cancel = new ModInstallOperation(_ => { }); using var retain = new ModInstallOperation(_ => { });
        queue.Enqueue(blocker);
        var cancelBuild = builds.InstallDependencyAsync(Copy(mod), operation: cancel); Pump();
        var retainBuild = builds.InstallDependencyAsync(Copy(mod), operation: retain); Pump();
        cancel.Cancel(); Pump(cancelBuild);
        Check(!cancelBuild.Result && !retainBuild.IsCompleted && queue.Peek() == blocker,
            "cancelling one shared-build request leaves another mod's request queued");
        queue.Dequeue(); Pump(retainBuild);
        Check(retainBuild.Result && File.Exists(Path.Combine(installed.fullPath, "fixture.exe")), "remaining FSO request completes and keeps the installed executable");
    }

    static void ViewChecks(string output)
    {
        Knossos.NET.Knossos.globalSettings.modCompression = CompressionSettings.Always;
        var catalog = new Catalog();
        var first = Make("blue-planet", "1.0.0", Pkg("core", "required"), Pkg("music", "recommended"));
        var latest = Make("blue-planet", "2.0.0", Pkg("core", "required", Dep("assets", "HD")), Pkg("voices", "optional"), Pkg("music", "recommended"));
        latest.isPrivate = true;
        catalog.Available.AddRange(new[] { first, latest, Make("assets", "1.0.0", Pkg("core", "required"), Pkg("HD", "optional")) });
        var installed = Copy(first); installed.installed = true; catalog.Installed.Add(installed);
        using var model = new ModPropertiesViewModel(installed, menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: catalog);
        Pump(model.Versions.CurrentLoad);
        Check(model.IsVersionsSelected && model.Versions.Root?.Mod.version == "1.0.0", "initial Versions entry respects the active installed version");
        Check(model.Versions.ShowDeveloper && !model.Versions.ShowCompression, "developer option is independent of manual compression visibility");
        Check(model.Versions.CanVerify && model.Versions.CanCleanReinstall, "normal installed version permits verify and clean reinstall");
        var view = new ModPropertiesView { DataContext = model };
        var window = new Window { Content = view, Width = 1600, Height = 960 };
        window.Show(); Pump();
        model.Versions.SelectedVersion = model.Versions.Versions.First(v => v.Mod.version == "2.0.0"); Pump(model.Versions.CurrentLoad);
        model.Versions.Developer = false; Pump(model.Versions.CurrentLoad);
        Check(model.Versions.IsPrivate && model.Versions.VisibilityLabel.Contains("Private"), "private version is identified explicitly");
        var page = view.GetVisualDescendants().OfType<ModVersionsView>().Single();
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "versions-desktop.png"));
        Check(Grid.GetColumn(page.FindControl<Border>("DependencyPanel")!) == 1, "desktop layout places dependency panel beside mod packages");
        Check(page.FindControl<Grid>("PackagePanels")!.Bounds.Height > 400, "package lists use the available window height");
        window.Width = 390; window.Height = 844; Pump();
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "versions-mobile.png"));
        Check(Grid.GetRow(page.FindControl<Border>("DependencyPanel")!) == 1, "phone layout stacks dependency panel below mod packages");
        Check(page.FindControl<Grid>("PageLayout")!.Bounds.Width <= page.Bounds.Width, "phone content stays inside available width");
        window.Width = 340; Pump();
        Check(page.FindControl<Grid>("PageLayout")!.Bounds.Width <= page.Bounds.Width, "small phone content fits its viewport");
        var confirm = model.ConfirmAsync("Test", "Confirm inside Properties", "Continue");
        Check(model.IsModalOpen && model.IsConfirmationModal, "confirmation uses the Properties overlay");
        model.CloseModalCommand.Execute(null); Pump(confirm);
        Check(!confirm.Result, "closing an internal confirmation cancels the operation");
        var confirm2 = model.ConfirmAsync("Test", "Confirm inside Properties", "Continue");
        model.ConfirmModalCommand.Execute(null); Pump(confirm2);
        Check(confirm2.Result && !model.IsModalOpen, "accepting the internal confirmation completes the pending operation");
        window.Close();
        using var remote = new ModPropertiesViewModel(first, catalog: catalog);
        remote.NavigateCommand.Execute(ModPropertiesViewModel.MenuPageEntry.Options);
        Check(!remote.CanConfigure && !remote.CanChangeInformationVersion && remote.IsInformationSelected, "uninstalled information cannot change versions or open settings");
        remote.NavigateCommand.Execute(ModPropertiesViewModel.MenuPageEntry.Versions); Pump(remote.Versions.CurrentLoad);
        Check(remote.Versions.Root?.Mod.version == "2.0.0", "uninstalled mod initially chooses the newest available version");
        var engineCatalog = new Catalog();
        var compatible = Pkg("compatible", "required"); compatible.environment = (KnUtils.IsWindows ? "windows" : KnUtils.IsLinux ? "linux" : "macosx") + " && " + (KnUtils.CpuArch == "X64" ? "X86_64" : KnUtils.CpuArch == "Arm64" ? "ARM64" : "X86");
        var incompatible = Pkg("incompatible", "required"); incompatible.environment = KnUtils.IsWindows ? "macosx && ARM64" : "windows && ARM64";
        var engine = Make("private-engine", "1.0.0", compatible, incompatible); engine.type = ModType.engine;
        engineCatalog.Available.Add(engine);
        using (var engineOwner = new ModPropertiesViewModel(engine, menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: engineCatalog))
        {
            Pump(engineOwner.Versions.CurrentLoad);
            Check(engineOwner.IsVersionsSelected && engineOwner.Versions.IsEngine, "private engine install entry can open Versions instead of an unavailable page");
            Check(engineOwner.Versions.Root!.Packages.Count == 1 && engineOwner.Versions.Root.Packages[0].Name == "compatible", "root engine installation offers only packages compatible with the current platform");
            Check(!engineOwner.Versions.Developer && !engineOwner.Versions.ShowDeveloper && !engineOwner.Versions.ShowCompression, "engine installs keep the specialized build installer policies");
        }
        var racingCatalog = new Catalog { PendingVersions = new TaskCompletionSource<List<Mod>>() };
        var activeOne = Make("race", "1.0.0", Pkg("core", "required")); activeOne.installed = true;
        var activeTwo = Make("race", "2.0.0", Pkg("core", "required")); activeTwo.installed = true;
        racingCatalog.Available.AddRange(new[] { activeOne, activeTwo });
        racingCatalog.Installed.AddRange(new[] { activeOne, activeTwo });
        using (var racingOwner = new ModPropertiesViewModel(new List<Mod> { activeOne, activeTwo }, 0, null,
            menuPageEntry: ModPropertiesViewModel.MenuPageEntry.Versions, catalog: racingCatalog))
        {
            racingOwner.SelectedVersionIndex = 1;
            racingCatalog.PendingVersions.SetResult(racingCatalog.Available);
            Pump(racingOwner.Versions.CurrentLoad);
            Check(racingOwner.Versions.Root?.Mod.version == "2.0.0", "initial catalog load respects an active-version change made while it was pending");
        }
        catalog.Installed.Clear();
        var deferred = new TaskCompletionSource<Mod>(); catalog.Pending[ModPackageCatalog.Key(first)] = deferred;
        remote.Versions.SelectedVersion = remote.Versions.Versions.First(v => v.Mod.version == "1.0.0");
        var oldLoad = remote.Versions.CurrentLoad;
        remote.Versions.SelectedVersion = remote.Versions.Versions.First(v => v.Mod.version == "2.0.0"); Pump(remote.Versions.CurrentLoad);
        deferred.SetResult(first); Pump(oldLoad);
        Check(remote.Versions.Root?.Mod.version == "2.0.0", "late response from an older version cannot replace the current plan");
        installed.modSettings.isCompressed = true;
        catalog.Installed.Add(installed);
        remote.Versions.SelectedVersion = remote.Versions.Versions.First(v => v.Mod.version == "1.0.0"); Pump(remote.Versions.CurrentLoad);
        Check(!remote.Versions.CanVerify && remote.Versions.VerifyHint.Contains("compressed"), "compressed installation disables verification and explains why");
    }
    [STAThread]
    static void Main()
    {
        AppBuilder.Configure<CheckApplication>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont().SetupWithoutStarting();
        var output = Path.GetFullPath(Path.Combine("artifacts", "mod-versions-checks", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(output);
        PlannerChecks(); EnginePackageChecks(); IncrementalChecks(output); BackupChecks(output); TaskChecks(output); SessionChecks(output); DeletionChecks(output); QueueChecks(output); SharedBuildChecks(output); ViewChecks(output);
        Console.WriteLine("All " + assertions + " checks passed. Preview images: " + output);
    }
}
