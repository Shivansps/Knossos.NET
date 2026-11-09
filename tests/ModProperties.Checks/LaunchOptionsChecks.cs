using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.ViewModels;
using Knossos.NET.Views;
using System.Runtime.InteropServices;
using System.Text;

sealed class LaunchServices : ModLaunchOptionsServices
{
    public bool Android;
    public override bool IsAndroid => Android;
    public override bool IsWindows => true;
    public override string LauncherPath => @"C:\Program Files\Knossos.NET\Knossos.NET.exe";
    public string OptionsFile = "";
    public int FlagReads, FilterWrites, CategoryChanges, ExpansionSaves;
    public HashSet<string> ExpandedCategories { get; } = new();
    public HashSet<string> ExpandedSections { get; } = new();
    public List<FsoBuild> Builds { get; } = new();
    public List<Mod> Mods { get; } = new();
    public Func<FsoBuild, CancellationToken, Task<FlagsJsonV1?>>? Query;
    public (string Executable, string Arguments)? Shortcut;
    public override List<FsoBuild> GetBuilds() => Builds.ToList();
    public override List<Mod> GetMods() => Mods.ToList();
    public override FsoBuild? Resolve(ModDependency dependency) => Builds.FirstOrDefault(b => b.id == dependency.id);
    public override string OptionsPath(Mod mod) => OptionsFile;
    public override Task<FlagsJsonV1?> GetFlagsAsync(FsoBuild build, CancellationToken token)
    {
        FlagReads++;
        return Query?.Invoke(build, token) ?? Task.FromResult<FlagsJsonV1?>(Metadata());
    }
    public override void SaveBuildFilters(bool rc, bool nightly, bool custom)
    {
        FilterWrites++;
        Knossos.NET.Knossos.globalSettings.hideBuildRC = rc;
        Knossos.NET.Knossos.globalSettings.hideBuildNightly = nightly;
        Knossos.NET.Knossos.globalSettings.hideBuildCustom = custom;
    }
    public override bool IsSectionExpanded(LaunchOptionsSection section) => ExpandedSections.Contains(section.ToString());
    public override void SetSectionExpanded(LaunchOptionsSection section, bool expanded)
    {
        if (expanded) ExpandedSections.Add(section.ToString()); else ExpandedSections.Remove(section.ToString());
    }
    public override bool IsFlagCategoryExpanded(string category) => ExpandedCategories.Contains(category);
    public override void SetFlagCategoryExpanded(string category, bool expanded)
    {
        CategoryChanges++;
        if (expanded) ExpandedCategories.Add(category); else ExpandedCategories.Remove(category);
    }
    public override void SaveExpansionPreferences() => ExpansionSaves++;
    public override string CreateShortcut(string title, string executable, string arguments)
    {
        Shortcut = (executable, arguments);
        return title + ".lnk";
    }
    public static FlagsJsonV1 Metadata() => new() { flags = new()
    {
        new Flag { name = "-nebula_detail", description = "Render distant nebula clouds with this build's experimental lighting.", type = "Build-specific rendering" },
        new Flag { name = "-test_quality", description = "Enable high quality rendering", type = "Build-specific rendering" },
        new Flag { name = "-flight_test", description = "Enable this engine's flight mode", type = "Flight controls" }
    } };
}

static partial class Program
{
    [DllImport("shell32.dll", SetLastError = true)]
    static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string command, out int count);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr pointer);
    static string[] WindowsArguments(string command)
    {
        var pointer = CommandLineToArgvW(command, out var count);
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("Could not parse Windows arguments");
        try { return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size))!).ToArray(); }
        finally { LocalFree(pointer); }
    }
    static (Mod Mod, LaunchServices Services) LaunchFixture(string output, string id)
    {
        var mod = Make(id, "3.3.1", Pkg("core", "required", Dep("FSO"), Dep("assets", "HD")));
        mod.installed = true; mod.fullPath = Path.Combine(output, id);
        mod.modFlag = new() { id, "assets" };
        mod.cmdline = "-nebula_detail -unknown value";
        Directory.CreateDirectory(mod.fullPath);
        var services = new LaunchServices { OptionsFile = Path.Combine(mod.fullPath, "engine_settings.ini") };
        services.Mods.AddRange(new[] { mod, Make(id, "3.4.0"), Make("assets", "1.2.0") });
        foreach (var stability in Enum.GetValues<FsoStability>())
        {
            var engine = Make("FSO", "26.0." + (int)stability); engine.type = ModType.engine;
            var build = new FsoBuild(engine) { stability = stability };
            services.Builds.Add(build);
        }
        return (mod, services);
    }
    static void LaunchOptionsChecks(string output)
    {
        var oldFilters = (Knossos.NET.Knossos.globalSettings.hideBuildRC, Knossos.NET.Knossos.globalSettings.hideBuildNightly, Knossos.NET.Knossos.globalSettings.hideBuildCustom);
        try
        {
            Knossos.NET.Knossos.globalSettings.hideBuildRC = false;
            Knossos.NET.Knossos.globalSettings.hideBuildNightly = false;
            Knossos.NET.Knossos.globalSettings.hideBuildCustom = false;
            var (mod, services) = LaunchFixture(output, "launch-options");
            using var model = new ModLaunchOptionsViewModel(mod, services: services);
            Check(!model.IsFlagsExpanded && services.FlagReads == 0 && model.FlagGroups.Count == 0, "launch flags are collapsed and never queried during page creation");
            Check(!model.IsQuickLaunchExpanded && !model.IsEngineOptionsExpanded && !model.IsExecutableExpanded &&
                !model.IsCommandLineExpanded && !model.IsDependenciesExpanded && !model.IsQuickHelpExpanded, "all Launch Options sections start collapsed");
            model.IsQuickLaunchExpanded = true; model.IsDependenciesExpanded = true;
            Check(services.ExpandedSections.SetEquals(new[] { "QuickLaunch", "Dependencies" }) && services.FlagReads == 0 && services.ExpansionSaves == 0,
                "opening sections updates memory without saving or querying FSO");
            Check(model.BuildChoices.Count == 5, "all installed build types and the recommended default are available");
            model.SelectedBuild = model.BuildChoices.Single(c => c.Build?.stability == FsoStability.Nightly);
            model.HideRC = true; model.HideNightly = true; model.HideCustom = true;
            Check(services.FilterWrites == 3 && model.BuildChoices.Count == 3 && model.SelectedBuild?.Build?.stability == FsoStability.Nightly,
                "build filters persist immediately while preserving the selected hidden type");
            model.CmdLine = "-unknown custom -nebula_detail"; model.SaveSettings();
            Check(mod.modSettings.customCmdLine == model.CmdLine && services.FlagReads == 0, "command line saves even when the flags panel was never opened");
            Check(services.ExpansionSaves == 1, "saving launch settings writes all pending expansion changes together");
            model.SaveSettings();
            Check(services.ExpansionSaves == 1, "saving again without expansion changes does not rewrite UI preferences");
            using (var reopened = new ModLaunchOptionsViewModel(mod, services: services))
                Check(reopened.HideRC && reopened.HideNightly && reopened.HideCustom && reopened.SelectedBuild?.Build?.stability == FsoStability.Nightly,
                    "reopening restores all build filters and the selected engine");
            model.IsFlagsExpanded = true; Pump(model.CurrentFlagsLoad);
            Check(services.FlagReads == 1 && model.FlagGroups.Select(g => g.Name).SequenceEqual(new[] { "Build-specific rendering", "Flight controls" }),
                "flags and arbitrary category names come from the engine response");
            Check(model.FlagGroups.All(g => !g.IsExpanded) && services.CategoryChanges == 0, "new flag categories start collapsed without writing preferences");
            model.FlagGroups[0].IsExpanded = true;
            Check(services.CategoryChanges == 1 && services.ExpandedCategories.Contains("Build-specific rendering") && services.ExpansionSaves == 1, "expanding a flag category only marks its state pending");
            var flag = model.FlagGroups[0].Items[0];
            Check(flag.IsChecked, "runtime flags reflect the command line entered before expanding");
            model.IgnoreGlobalCmd = true; flag.IsChecked = false;
            Check(model.CmdLine == "-unknown custom", "unchecking a runtime flag preserves unknown arguments");
            flag.IsChecked = true;
            Check(LaunchCommandLine.HasFlag(model.CmdLine, flag.Name), "checking a runtime flag updates the mod command line");
            model.IsFlagsExpanded = false; model.IsFlagsExpanded = true; Pump(model.CurrentFlagsLoad);
            Check(services.FlagReads == 1, "reexpanding flags reuses metadata for the same engine");
            model.FlagSearch = "flight";
            Check(model.FlagGroups.Count == 1 && model.FlagGroups[0].Items[0].Name == "-flight_test", "runtime flags can be searched by engine metadata");
            model.FlagSearch = "";
            Check(model.FlagGroups[0].IsExpanded && !model.FlagGroups[1].IsExpanded && services.CategoryChanges == 1,
                "filtering flags preserves each category's saved state");
            Check(services.ExpansionSaves == 1, "multiple expansion changes and filtering cause no additional disk writes");
            model.SaveSettings();
            Check(services.ExpansionSaves == 2, "pending section and flag-category states flush as one batch");
            var savedUi = new GlobalSettings { expandedLaunchFlagCategories = new(services.ExpandedCategories), expandedLaunchSections = new(services.ExpandedSections) };
            var uiPath = Path.Combine(output, "launch-category-preferences.json");
            File.WriteAllText(uiPath, System.Text.Json.JsonSerializer.Serialize(savedUi));
            var restoredUi = System.Text.Json.JsonSerializer.Deserialize<GlobalSettings>(File.ReadAllText(uiPath))!;
            var restoredServices = new LaunchServices(); restoredServices.Builds.AddRange(services.Builds); restoredServices.Mods.AddRange(services.Mods);
            restoredServices.ExpandedCategories.UnionWith(restoredUi.expandedLaunchFlagCategories);
            restoredServices.ExpandedSections.UnionWith(restoredUi.expandedLaunchSections);
            using (var reopenedFlags = new ModLaunchOptionsViewModel(mod, services: restoredServices))
            {
                Check(reopenedFlags.IsQuickLaunchExpanded && reopenedFlags.IsDependenciesExpanded && !reopenedFlags.IsEngineOptionsExpanded &&
                    !reopenedFlags.IsExecutableExpanded && !reopenedFlags.IsCommandLineExpanded, "reopening restores each section's expanded or collapsed state");
                Pump(reopenedFlags.CurrentFlagsLoad);
                Check(reopenedFlags.IsFlagsExpanded && restoredServices.FlagReads == 1, "a saved open flags section loads engine metadata once on reopening");
                Check(reopenedFlags.FlagGroups[0].IsExpanded && !reopenedFlags.FlagGroups[1].IsExpanded,
                    "reopening from persisted preferences restores expanded and collapsed categories");
                reopenedFlags.FlagGroups[0].IsExpanded = false;
                reopenedFlags.FlagSearch = "flight"; reopenedFlags.FlagSearch = "";
                Check(reopenedFlags.FlagGroups.All(g => !g.IsExpanded) && restoredServices.ExpandedCategories.Count == 0,
                    "collapsing a category retains the closed state in memory");
                Check(restoredServices.ExpansionSaves == 0, "restoring and changing category state does not write preferences before closing");
            }
            Check(restoredServices.ExpansionSaves == 1, "disposing the closed launch page flushes its pending category changes once");
            var pending = new TaskCompletionSource<FlagsJsonV1?>();
            services.Query = (_, _) => pending.Task;
            model.SelectedBuild = model.BuildChoices.Single(c => c.Build?.stability == FsoStability.Stable);
            var obsolete = model.CurrentFlagsLoad;
            model.IsFlagsExpanded = false; model.IsFlagsExpanded = true;
            Check(services.FlagReads == 2 && ReferenceEquals(obsolete, model.CurrentFlagsLoad), "reexpanding during a query keeps the existing request");
            services.Query = (_, _) => Task.FromResult<FlagsJsonV1?>(new() { flags = new() { new Flag { name = "-new_engine", type = "New engine only" } } });
            model.SelectedBuild = model.BuildChoices[0]; Pump(model.CurrentFlagsLoad);
            pending.SetResult(LaunchServices.Metadata()); Pump(obsolete);
            Check(model.FlagGroups.Single().Name == "New engine only" && !model.IsLoadingFlags, "a late response cannot replace flags from the newly selected engine");
            model.IsFlagsExpanded = false;
            model.SelectedBuild = model.BuildChoices.Single(c => c.Build?.stability == FsoStability.Stable);
            Check(!model.IsLoadingFlags && model.FlagGroups.Count == 0, "changing engines while collapsed clears stale flags without querying");

            model.CustomDependencies = true;
            var assets = model.Dependencies.Single(d => d.SelectedId == "assets");
            assets.SelectedVersion = "~1.2.0";
            assets.MoveUpCommand.Execute(null);
            model.SaveSettings();
            Check(mod.modSettings.customDependencies!.Single(d => d.id == "assets").packages!.SequenceEqual(new[] { "HD" }) &&
                mod.modSettings.customDependencies!.Single(d => d.id == "assets").version == "~1.2.0" &&
                mod.modSettings.customModFlags!.SequenceEqual(new[] { "assets", mod.id }), "dependency overrides preserve required packages, constraints and launch order");
            model.CustomDependencies = false; model.SaveSettings();
            Check(mod.modSettings.customDependencies == null && mod.modSettings.customModFlags == null && model.Dependencies[0].IsRoot,
                "disabling custom dependencies restores defaults and removes saved overrides");

            var duplicated = Make("duplicate-dependency", "1.0.0", Pkg("core", "required",
                new ModDependency { id = "assets", version = ">=1.0.0", packages = new() { "HD" } },
                new ModDependency { id = "assets", version = "<=2.0.0", packages = new() { "music" } }));
            duplicated.modFlag = new() { duplicated.id, "assets" };
            using (var overrides = new ModLaunchOptionsViewModel(duplicated, services: services))
                Check(overrides.Dependencies.Count(d => d.SelectedId == "assets") == 2 &&
                    overrides.Dependencies.Where(d => d.SelectedId == "assets").SelectMany(d => d.ToDependency()!.packages!).SequenceEqual(new[] { "HD", "music" }),
                    "launch dependency editing retains repeated requirements and their package lists");

            model.SelectedQuickExecutable = "QtFredDebug"; model.QuickExtraArguments = "-nomovies -start_mission \"A mission.fs2\" -path \"C:\\mods\\\"";
            if (OperatingSystem.IsWindows())
            {
                var argv = WindowsArguments(model.QuickLaunch);
                Check(argv[0] == services.LauncherPath && argv[2] == mod.id && argv[4] == mod.version && argv[6] == "QtFredDebug" &&
                    argv[8] == model.QuickExtraArguments, "Quick Launch quoting roundtrips spaces, embedded quotes and backslashes through Windows");
                foreach (var value in new[] { "", "plain", @"C:\Folder with spaces\", "test\"quote", @"C:\O'Brien\Knossos.exe" })
                {
                    var quoted = LaunchCommandLine.Quote(value, true, true);
                    Check(WindowsArguments("launcher.exe " + quoted)[1] == value, "Windows argument roundtrip: " + value);
                }
            }
            model.SelectedQuickVersion = "Latest installed";
            Check(!model.QuickLaunch.Contains("-version") && model.QuickLaunch.Contains("-exec QtFredDebug"), "latest installed Quick Launch omits the exact version constraint");
            model.QuickLaunch = "\"C:\\My launcher\\Knossos.exe\" -playmod custom -exec Debug";
            model.CreateShortcutCommand.Execute(null);
            Check(services.Shortcut == (@"C:\My launcher\Knossos.exe", "-playmod custom -exec Debug"), "desktop shortcut uses the user's editable command preview");
            var split = LaunchCommandLine.SplitCommand(LaunchCommandLine.Quote(@"C:\O'Brien\Knossos.exe", true) + " -playmod custom");
            Check(split.Executable == @"C:\O'Brien\Knossos.exe", "shortcut parser preserves apostrophes in Windows paths");
            Check(!LaunchCommandLine.HasFlag("-test_quality_more \"-test_quality\" -arg '-test_quality'", "-test_quality"),
                "flag matching ignores prefixes and quoted argument values");
            Check(LaunchCommandLine.SetFlag("-a x -test_quality 3 -b y -test_quality 4 -c z", "-test_quality", false) == "-a x -b y -c z",
                "removing repeated flags also removes their parameters without losing other switches");

            var executable = Path.Combine(mod.fullPath, "custom-fso.exe"); File.WriteAllText(executable, "");
            mod.modSettings.customBuildId = null; mod.modSettings.customBuildExec = executable;
            using (var direct = new ModLaunchOptionsViewModel(mod, services: services))
                Check(direct.SelectedBuild?.Build?.directExec == executable, "direct executable restores independently of a saved build ID");
            services.Android = true;
            using (var android = new ModLaunchOptionsViewModel(mod, services: services))
                Check(!android.ShowQuickLaunch && !android.ShowDirectExecutable && !android.CanCreateShortcut, "Android hides Quick Launch, desktop shortcuts and direct executable selection");

            services.Android = false; mod.modSettings.customBuildExec = null;
            var waiting = new TaskCompletionSource<FlagsJsonV1?>(); CancellationToken requestToken = default;
            services.Query = (_, token) => { requestToken = token; return waiting.Task; };
            var closing = new ModLaunchOptionsViewModel(mod, services: services); closing.IsFlagsExpanded = true;
            closing.Dispose(); Pump(closing.CurrentFlagsLoad);
            Check(requestToken.IsCancellationRequested && closing.FlagGroups.Count == 0, "closing Launch Options cancels its pending flag query");
            waiting.SetResult(LaunchServices.Metadata()); Pump();
        }
        finally
        {
            Knossos.NET.Knossos.globalSettings.hideBuildRC = oldFilters.Item1;
            Knossos.NET.Knossos.globalSettings.hideBuildNightly = oldFilters.Item2;
            Knossos.NET.Knossos.globalSettings.hideBuildCustom = oldFilters.Item3;
        }
    }
    static void TextEditorChecks(string output)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encodings = new Encoding[] { new UTF8Encoding(false, true), new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true),
            new UnicodeEncoding(true, true, true), new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true),
            new UnicodeEncoding(false, false, true), new UnicodeEncoding(true, false, true), Encoding.GetEncoding(1252) };
        var index = 0;
        foreach (var encoding in encodings)
        {
            var file = Path.Combine(output, "encoding-" + index++ + ".ini");
            var original = encoding.GetPreamble().Concat(encoding.GetBytes("[Settings]\r\nName=café\r\n")).ToArray();
            File.WriteAllBytes(file, original);
            var open = TextFileDocument.OpenAsync(file); Pump(open); var doc = open.Result;
            Check(doc.Text == "[Settings]\r\nName=café\r\n", "editor decodes " + encoding.EncodingName + " BOM=" + encoding.GetPreamble().Length);
            var save = doc.SaveAsync("[Settings]\nName=niño\n"); Pump(save);
            Check(File.ReadAllBytes(file).SequenceEqual(encoding.GetPreamble().Concat(encoding.GetBytes("[Settings]\r\nName=niño\r\n"))),
                "editor preserves original encoding, BOM and CRLF: " + index);
        }
        var editFile = Path.Combine(output, "editor.ini"); File.WriteAllText(editFile, "[Settings]\nValue=1\n", new UTF8Encoding(false));
        var closed = false;
        var model = new TextEditorViewModel(editFile, () => closed = true); Pump(model.LoadAsync());
        model.Text = "[Settings]\nValue=2\n";
        Check(model.IsDirty && model.CanSave && !model.RequestClose() && model.IsDiscardPromptOpen, "text editor protects unsaved edits when closed");
        model.KeepEditingCommand.Execute(null);
        Check(!model.IsDiscardPromptOpen && model.IsDirty, "keep editing retains unsaved text");
        Pump(model.SaveCommand.ExecuteAsync(null));
        Check(!model.IsDirty && model.RequestClose() && File.ReadAllText(editFile).Contains("Value=2"), "save writes changes and clears the dirty state");
        model.Text = "discard this"; model.DiscardAndCloseCommand.Execute(null);
        Check(closed && File.ReadAllText(editFile).Contains("Value=2"), "discard closes without writing the edited buffer");

        var conflicting = TextFileDocument.OpenAsync(editFile); Pump(conflicting);
        File.WriteAllText(editFile, "external change");
        var conflict = conflicting.Result.SaveAsync("overwrite");
        try { Pump(conflict); } catch (IOException) { }
        Check(conflict.IsFaulted && File.ReadAllText(editFile) == "external change", "external file changes are not overwritten by the editor");
        var ansi = Path.Combine(output, "ansi.ini"); var ansiBytes = Encoding.GetEncoding(1252).GetBytes("café");
        File.WriteAllBytes(ansi, ansiBytes); var ansiOpen = TextFileDocument.OpenAsync(ansi); Pump(ansiOpen);
        var invalid = ansiOpen.Result.SaveAsync("café 中文");
        try { Pump(invalid); } catch (EncoderFallbackException) { }
        Check(invalid.IsFaulted && File.ReadAllBytes(ansi).SequenceEqual(ansiBytes), "unrepresentable characters report an error without changing the original encoding");
    }
    static void LaunchViewChecks(string output)
    {
        var (mod, services) = LaunchFixture(output, "launch-preview");
        File.WriteAllText(services.OptionsFile, "[Video]\nResolution=1920x1080\n\n[Audio]\nMusicVolume=0.75\n");
        var catalog = new Catalog(); catalog.Available.Add(mod); catalog.Installed.Add(mod);
        using var owner = new ModPropertiesViewModel(mod, catalog: catalog);
        using var model = new ModLaunchOptionsViewModel(mod, owner: owner, services: services);
        owner.CurrentPage = model; owner.SelectedPage = ModPropertiesViewModel.MenuPageEntry.Options;
        var view = new ModPropertiesView { DataContext = owner };
        var window = new Window { Content = view, Width = 1600, Height = 1000 };
        window.Show(); Pump();
        var launchView = view.GetVisualDescendants().OfType<ModLaunchOptionsView>().Single();
        Check(services.FlagReads == 0, "rendering the Launch Options page leaves the engine untouched");
        var sectionHeaders = launchView.GetVisualDescendants().OfType<Expander>().Where(e => e.Classes.Contains("launchSection")).ToList();
        Check(sectionHeaders.Count == 6 && sectionHeaders.All(e => !e.IsExpanded), "desktop initially shows only the six collapsed section headers");
        Check(sectionHeaders.All(e => e.Header is Grid header && header.GetVisualDescendants().OfType<TextBlock>().Count() >= 2),
            "every section header retains its title and short description");
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "launch-options-desktop.png"));
        var flagsExpander = launchView.FindControl<Expander>("FlagsSection")!;
        flagsExpander.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First().IsChecked = true;
        Pump(model.CurrentFlagsLoad);
        Check(model.IsFlagsExpanded && services.FlagReads == 1, "expander header opens flags and triggers the deferred engine query");
        var category = launchView.GetVisualDescendants().OfType<Expander>().First(e => e.Classes.Contains("flagCategory"));
        Check(!category.IsExpanded && model.FlagGroups.All(g => !g.IsExpanded), "rendered category headers start collapsed");
        category.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First().IsChecked = true; Pump();
        Check(model.FlagGroups[0].IsExpanded && services.CategoryChanges == 1 && services.FlagReads == 1,
            "category header expands its flags and keeps the state pending without querying the engine again");
        var scroll = launchView.GetVisualDescendants().OfType<ScrollViewer>().First();
        scroll.ScrollToEnd(); Pump();
        window.CaptureRenderedFrame()!.Save(Path.Combine(output, "launch-flags-desktop.png"));
        Check(launchView.FindControl<StackPanel>("LaunchLayout")!.Bounds.Width <= launchView.Bounds.Width,
            "desktop Launch Options fits its viewport");
        window.Close();

        services.Android = true;
        using var mobileOwner = new ModPropertiesViewModel(mod, catalog: catalog);
        using var mobile = new ModLaunchOptionsViewModel(mod, owner: mobileOwner, services: services);
        mobileOwner.CurrentPage = mobile; mobileOwner.SelectedPage = ModPropertiesViewModel.MenuPageEntry.Options;
        var mobileView = new ModPropertiesView { DataContext = mobileOwner };
        var mobileWindow = new Window { Content = mobileView, Width = 390, Height = 844 };
        mobileWindow.Show(); Pump();
        var mobileLaunch = mobileView.GetVisualDescendants().OfType<ModLaunchOptionsView>().Single();
        Check(mobile.IsFlagsExpanded && mobile.FlagGroups[0].IsExpanded && !mobile.IsDependenciesExpanded,
            "reopening on phone restores both section and nested category states");
        mobile.IsFlagsExpanded = false; Pump();
        mobileWindow.CaptureRenderedFrame()!.Save(Path.Combine(output, "launch-options-mobile.png"));
        mobile.IsFlagsExpanded = true; Pump(mobile.CurrentFlagsLoad);
        Check(mobile.FlagGroups[0].IsExpanded && !mobile.FlagGroups[1].IsExpanded, "phone view restores the saved category expansion states");
        var mobileScroll = mobileLaunch.GetVisualDescendants().OfType<ScrollViewer>().First(); mobileScroll.ScrollToEnd(); Pump();
        mobileWindow.CaptureRenderedFrame()!.Save(Path.Combine(output, "launch-flags-mobile.png"));
        Check(mobileLaunch.FindControl<StackPanel>("LaunchLayout")!.Bounds.Width <= mobileLaunch.Bounds.Width,
            "phone Launch Options stays within the available width");
        foreach (var group in mobileLaunch.GetVisualDescendants().OfType<Border>().Where(b => b.DataContext is LaunchFlagGroupViewModel))
            Check(group.Bounds.Width <= mobileLaunch.Bounds.Width, "phone runtime flag group stays within the page");
        mobileWindow.Close();

        var editorModel = new TextEditorViewModel(services.OptionsFile); Pump(editorModel.LoadAsync());
        var editor = new TextEditorView { DataContext = editorModel };
        var editorWindow = new Window { Content = editor, Width = 390, Height = 844 };
        editorWindow.Show(); Pump();
        editorWindow.CaptureRenderedFrame()!.Save(Path.Combine(output, "text-editor-mobile.png"));
        Check(editor.GetVisualDescendants().OfType<TextBox>().Single().Bounds.Width <= 390, "reusable text editor fits the phone viewport");
        editorWindow.Close();
    }
}
