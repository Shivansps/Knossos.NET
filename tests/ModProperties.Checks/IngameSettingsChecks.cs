using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Knossos.NET.Models;
using Knossos.NET.ViewModels;
using Knossos.NET.Views;
using System.Text.Json;

static partial class Program
{
    static bool AnswerFsoQuery(string[] args)
    {
        if (args.Length < 2 || (args[0] != "-get_flags" && args[0] != "-get_options")) return false;
        File.AppendAllLines("fso-queries.log", new[] { JsonSerializer.Serialize(args) });
        if (args[0] == "-get_flags")
        {
            if (File.Exists("flags-failure")) Console.WriteLine("Simulated engine diagnostics, not a flags response.");
            else
            {
                var caps = File.Exists("supports-options") ? new[] { "GetOptions" } : Array.Empty<string>();
                Console.WriteLine(JsonSerializer.Serialize(new { caps, pref_path = Environment.CurrentDirectory },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            Environment.ExitCode = 1;
        }
        else if (!args.Contains("-get_options_no_gpu"))
        {
            Console.Error.WriteLine("The simulated driver failed.");
            Environment.ExitCode = 1;
        }
        else
        {
            Console.WriteLine("Driver message {this line is not engine JSON}");
            Console.WriteLine(File.ReadAllText("fso-options.json"));
        }
        return true;
    }

    static void IngameSettingsChecks(string output)
    {
        var root = Path.Combine(output, "ingame");
        var mod = Make("test-mod", "1.2.3");
        mod.installed = true;
        mod.folderName = "test-mod-1.2.3-DevEnv";
        mod.fullPath = Path.Combine(root, mod.folderName) + Path.DirectorySeparatorChar;
        mod.modFlag = new() { mod.id };
        mod.cmdline = "-test_flag \"with spaces\"";
        mod.modSettings.customBuildId = "DirectExec";
        mod.modSettings.customBuildVersion = "99.99.99";
        mod.modSettings.customBuildExec = Path.Combine(AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "ModProperties.Checks.exe" : "ModProperties.Checks");
        Directory.CreateDirectory(mod.fullPath);
        var inherited = Path.Combine(root, "fs2_open.ini");
        File.WriteAllText(inherited, "# Preserve this comment\n[Graphics]\nUntouched=\"existing\"\n[Input]\nDeadZone=5\n");
        var reply = new
        {
            version = 1,
            mod = mod.folderName,
            in_game_options = true,
            options = new object[]
            {
                new { key = "Input.DeadZone", title = "Joystick Dead Zone", category = "Input", type = "range",
                    range = new { min = 0, max = 45, integer = true }, current = 5, @default = 10 },
                new { key = "Input.Device.Primary", title = "Input Device", category = "Input", type = "selection",
                    values = new[] { new { value = "default", display = "Default" }, new { value = "Stick#1;Pro", display = "Flight stick" } },
                    current = "default", @default = "default" },
                new { key = "Input.Locked", title = "Locked by Flag", category = "Input", type = "range",
                    range = new { min = 0, max = 10, integer = true }, current = 4, @default = 2,
                    overridden_by = "cmdline", override_reason = "-test_flag" },
                new { key = "Input.Sensitivity", title = "Sensitivity", category = "Input", type = "range",
                    range = new { min = 0, max = 3, integer = false }, current = 0.5, @default = 0.5 },
            }
        };
        File.WriteAllText(Path.Combine(root, "fso-options.json"), JsonSerializer.Serialize(reply));
        var trace = Path.Combine(root, "fso-queries.log");
        var settings = Path.Combine(root, "data", "test-mod_settings.ini");

        using var model = new ModPropertiesViewModel(mod);
        var view = new ModPropertiesView { DataContext = model };
        var window = new Window { Width = 920, Height = 760, Content = view };
        window.Show();
        Pump();
        var navigation = view.GetVisualDescendants().OfType<Button>().Single(button =>
            button.CommandParameter is ModPropertiesViewModel.MenuPageEntry.IngameSettings);
        navigation.Command!.Execute(navigation.CommandParameter);
        var page = (ModIngameSettingsViewModel)model.CurrentPage!;
        WaitForIngameSettings(page);
        Check(page.Message?.Contains("does not support configuring") == true && page.Message.Contains(mod.modSettings.customBuildExec),
            "Ingame Settings names the selected engine when the capability is missing");
        Check(JsonSerializer.Deserialize<string[]>(File.ReadAllLines(trace).Single())!.SequenceEqual(new[] { "-get_flags", "json_v2" }),
            "an unsupported engine only receives the flags v2 query");

        var failure = Path.Combine(root, "flags-failure");
        File.WriteAllText(failure, "");
        navigation.Command.Execute(navigation.CommandParameter);
        WaitForIngameSettings(page);
        Check(page.Message?.Contains("exit code 1") == true && !page.Message.Contains("Simulated engine diagnostics"),
            "a failed flags query shows a concise error and leaves engine output in the log");
        File.Delete(failure);

        File.WriteAllText(Path.Combine(root, "supports-options"), "");
        navigation.Command.Execute(navigation.CommandParameter);
        WaitForIngameSettings(page);
        Check(page.Groups.Single().Name == "Input" && !page.HasMessage, "valid flags v2 JSON with exit code 1 still loads the options");
        var group = page.Groups.Single();
        var expander = view.GetVisualDescendants().OfType<Expander>().Single(item => ReferenceEquals(item.DataContext, group));
        var wasExpanded = Knossos.NET.Knossos.globalSettings.expandedIngameSettingsCategories.Contains(group.Name);
        Check(expander.IsExpanded == wasExpanded, "the category starts collapsed unless an expanded preference was saved");
        expander.IsExpanded = true;
        Pump();
        Check(group.IsExpanded, "expanding a category updates its preference draft");
        var options = group.Options;
        var range = options.Single(option => option.Title == "Joystick Dead Zone");
        var device = options.Single(option => option.Title == "Input Device");
        var locked = options.Single(option => option.Title == "Locked by Flag");
        var sensitivity = options.Single(option => option.Title == "Sensitivity");
        Check(range.Min == 0 && range.Max == 45 && range.Number == 5 && !locked.CanEdit && locked.Status == "Set by -test_flag",
            "the editor honors exported bounds, current values and command-line locks");
        var queries = File.ReadAllLines(trace).Select(json => JsonSerializer.Deserialize<string[]>(json)!).ToArray();
        Check(queries.Length == 5 && queries[3].Take(2).SequenceEqual(new[] { "-get_options", "json_v1" }) &&
            queries[3].Contains(mod.folderName) && queries[3].Contains("with spaces") && queries[4].Contains("-get_options_no_gpu"),
            "engine queries retain the mod folder and launch argument boundaries");

        var control = view.GetVisualDescendants().OfType<NumericUpDown>().Single(input => input.Value == 5);
        control.GetVisualDescendants().OfType<TextBox>().Single().Text = "24";
        Pump();
        Check(range.Number == 24, "the futuristic settings page binds its range control to the draft");
        device.SelectedChoice = device.Choices.Single(choice => choice.Display == "Flight stick");
        sensitivity.Number = 1;
        Check(!File.Exists(settings), "editing creates no INI before Mod Properties closes");
        var preview = window.CaptureRenderedFrame();
        preview?.Save(Path.Combine(output, "ingame-settings.png"));
        preview?.Dispose();
        window.Width = 430;
        Pump();
        Check(view.GetVisualDescendants().OfType<StackPanel>().Where(panel => panel.Classes.Contains("settingControls"))
            .All(panel => panel.Orientation == Avalonia.Layout.Orientation.Vertical), "option controls stack in the compact layout");
        var compactPreview = window.CaptureRenderedFrame();
        compactPreview?.Save(Path.Combine(output, "ingame-settings-compact.png"));
        compactPreview?.Dispose();
        locked.Number = 9;

        model.NavigateCommand.Execute(ModPropertiesViewModel.MenuPageEntry.Information);
        model.NavigateCommand.Execute(ModPropertiesViewModel.MenuPageEntry.IngameSettings);
        WaitForIngameSettings(page);
        Check(page.Groups.Single().IsExpanded, "returning to Ingame Settings restores the draft expansion state");
        page.Groups.Single().IsExpanded = wasExpanded;
        model.NavigateCommand.Execute(ModPropertiesViewModel.MenuPageEntry.Information);
        view.Close();
        Pump();
        var saved = File.ReadAllText(settings);
        Check(saved.Contains("DeadZone=24") && saved.Contains("Device.Primary=\"Stick\\u00231\\u003bPro\"") &&
            saved.Contains("Sensitivity=1.0") && !saved.Contains("Locked=") && saved.Contains("Untouched=\"existing\"") &&
            saved.Contains("# Preserve this comment") && File.ReadAllText(inherited).Contains("DeadZone=5"),
            "close writes only edited values as typed JSON into the mod profile and preserves inherited settings");
        window.Close();
        CheckReportedIngameProfile(root, mod, inherited: true);
        CheckReportedIngameProfile(root, mod, inherited: false);
    }

    static void CheckReportedIngameProfile(string root, Mod mod, bool inherited)
    {
        var folder = inherited ? "inherited-profile" : "fresh-profile";
        var preferences = Path.Combine(root, folder);
        Directory.CreateDirectory(preferences);
        File.WriteAllText(Path.Combine(preferences, "fs2_open.ini"), "[Graphics]\nPreserved=true\n");
        var advertisedPath = Path.Combine(folder, "data", "engine-picked.ini");
        var reply = new
        {
            version = 1, mod = mod.folderName, in_game_options = true,
            settings_file = advertisedPath, settings_inherited = inherited,
            options = new object[]
            {
                new { key = "Input.DeadZone", title = "Dead Zone", category = "Input", type = "range",
                    range = new { min = 0, max = 45, integer = true }, saved = 7, effective = 7, @default = 10 },
                new { key = "Input.Locked", title = "Locked", category = "Input", type = "range",
                    range = new { min = 0, max = 10, integer = true }, saved = 4, effective = 8, @default = 2,
                    overridden_by = "cmdline", override_reason = "-test_flag" },
                new { key = "Graphics.Hdr", title = "HDR", category = "Graphics", type = "range",
                    range = new { min = 0, max = 10, integer = true }, effective = 2, @default = 2,
                    requires = "vulkan", requires_support = "supported", requires_text = "Needs Vulkan" },
                new { key = "Graphics.Rtao", title = "RTAO", category = "Graphics", type = "range",
                    range = new { min = 0, max = 10, integer = true }, effective = 0, @default = 0,
                    requires = "raytracing", requires_support = "unsupported", requires_text = "Needs ray tracing" },
                new { key = "Graphics.Fsr", title = "FSR", category = "Graphics", type = "selection",
                    values = new[] { new { value = 0, display = "Off" } }, saved = 2, effective = 0, @default = 0,
                    requires = "fsr", requires_support = "unknown" },
                new { key = "Graphics.HdrPeak", title = "HDR Peak", category = "Graphics", type = "range",
                    range = new { min = 1, max = 10, integer = true }, effective = 5, @default = 5,
                    requires = "vulkan", requires_support = "unknown", requires_text = "Needs Vulkan" },
            }
        };
        File.WriteAllText(Path.Combine(root, "fso-options.json"), JsonSerializer.Serialize(reply));
        using var model = new ModPropertiesViewModel(mod, menuPageEntry: ModPropertiesViewModel.MenuPageEntry.IngameSettings);
        var view = new ModPropertiesView { DataContext = model };
        var page = (ModIngameSettingsViewModel)model.CurrentPage!;
        WaitForIngameSettings(page);
        var options = page.Groups.SelectMany(group => group.Options).ToDictionary(option => option.Title);
        Check(options["Dead Zone"].Number == 7 && options["Locked"].Number == 8 && !options["Locked"].CanEdit &&
            options["Locked"].SavedValueStatus == "Saved preference · 4", "the editor displays effective values and preserves the saved preference under an override");
        Check(options["HDR"].CanEdit && !options["RTAO"].CanEdit && !options["FSR"].CanEdit && options["HDR Peak"].CanEdit &&
            options["FSR"].Status?.Contains("could not be verified") == true, "requirement support controls editing and explains an unverified feature");

        options["Dead Zone"].Number = 15;
        view.Close();
        Pump();
        var profile = File.ReadAllText(Path.Combine(root, advertisedPath));
        Check(profile.Contains("DeadZone=15") && profile.Contains("Preserved=true") == inherited,
            inherited ? "FSO's reported profile is seeded from inherited settings" : "FSO's fresh profile contains only edited values");
    }

    static void WaitForIngameSettings(ModIngameSettingsViewModel page)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (page.IsLoading && DateTime.UtcNow < deadline) Pump();
        Check(!page.IsLoading, "the in-game settings query completes");
        Pump();
    }
}
