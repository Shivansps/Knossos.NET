using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.ViewModels;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Knossos.NET
{
    public partial class App : Application
    {
        TrayIcon? trayIcon = null;
        bool trayMode = false;
        private MainWindowViewModel? mainVM = null;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                trayMode = Environment.GetCommandLineArgs().FirstOrDefault(x => x.ToLower() == "-traymode") != null;
                if (trayMode)
                {
                    desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    Knossos.StartUp(false, false);
                    StartTrayIcon();
                }
                else
                {
                    CreateMainWindow(desktop);
                }
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new MainView
                {
                    DataContext = new MainViewModel()
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void CreateMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (mainVM == null)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel()
                };

                mainVM = desktop.MainWindow.DataContext as MainWindowViewModel;
            }
            else
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainVM
                };
            }

            desktop.MainWindow.Closing += (_, __) =>
            {
                if (Knossos.globalSettings.closeToTray || trayMode)
                {
                    desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    StartTrayIcon();
                }
            };

            desktop.MainWindow.Show();
        }


        private async void StartTrayIcon()
        {
            trayIcon?.Dispose();
            trayIcon = new TrayIcon
            {
                IsVisible = true,
                ToolTipText = "Knossos.NET v" + Knossos.AppVersion,
                Icon = new WindowIcon(new Bitmap(AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/knossos-icon.ico")))),
                Menu = new NativeMenu() { new NativeMenuItem("Loading...") }
            };

            while (!Knossos.initIsComplete) { await Task.Delay(10); }

            trayIcon.Menu?.Items.Clear();
            
            /*****************************OPEN***********************************/

            var open = new NativeMenuItem("Open");
            open.Click += (s, _) => {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    CreateMainWindow(desktop);
                    desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
                    if (CustomLauncher.IsCustomMode && CustomLauncher.MenuTaskButtonAtTheEnd)
                    {
                        MainView.instance?.FixMarginButtomTasks();
                    }
                    MainWindow.instance?.SetSize(mainVM?.WindowWidth, mainVM?.WindowHeight);
                    trayIcon?.Dispose();
                    GC.Collect();
                }
            };
            trayIcon.Menu?.Add(open);
            trayIcon.Menu?.Add(new NativeMenuItemSeparator());

            try
            {
                /*****************************PLAY***********************************/
                var play = new NativeMenuItem("Play") { Menu = new NativeMenu(), Icon = new Bitmap(AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/general/menu_play.png"))) };
                var mods = Knossos.GetInstalledModList(null);
                mods.Sort(Mod.CompareTitles);
                var filters = ModTags.GetListAllFilters();
                foreach (var filter in filters)
                {
                    if( string.Compare(filter, ModTags.Filters.Dependency.ToString(),true) == 0 ||
                        string.Compare(filter, ModTags.Filters.Utility.ToString(), true) == 0 )
                    {
                        continue; //skip dependency and utility mods
                    }
                    TextInfo myTI = new CultureInfo("en-US", false).TextInfo;
                    var displayName = myTI.ToTitleCase(filter.Replace("_", " "));
                    var filterItem = new NativeMenuItem(displayName) { Menu = new NativeMenu() };
                    var modsInFilter = mods.Where(x => ModTags.IsFilterPresentInModID(x.id, filter));
                    if (modsInFilter.Any())
                    {
                        var addedIds = new List<string>();
                        foreach (var mod in modsInFilter)
                        {
                            if (!addedIds.Contains(mod.id))
                            {
                                var m = mods.Where(x => x.id == mod.id)?.MaxBy(x => new SemanticVersion(x.version));
                                if (m != null)
                                {
                                    filterItem.Menu.Add(CreateModMenuItem(m));
                                    addedIds.Add(mod.id);
                                }
                            }
                        }
                        play.Menu.Add(filterItem);
                    }
                }
                if(play.Menu.Any())
                    trayIcon.Menu?.Add(play);
                /*****************************DEVELOP***********************************/
                var dev = new NativeMenuItem("Develop") { Menu = new NativeMenu(), Icon = new Bitmap(AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/general/menu_develop.png"))) };
                var devMods = mods.Where(x => x.devMode);
                if(devMods != null && devMods.Any())
                {
                    var addedIds = new List<string>();
                    foreach (var devMod in devMods)
                    {
                        if (!addedIds.Contains(devMod.id))
                        {
                            var m = mods.Where(x => x.id == devMod.id)?.MaxBy(x => new SemanticVersion(x.version));
                            if(m != null)
                            {
                                dev.Menu.Add(CreateModMenuItem(m));
                                addedIds.Add(devMod.id);
                            }
                        }
                    }
                }    
                if(dev.Menu.Any())
                    trayIcon.Menu?.Add(dev);
                /*****************************TOOLS*************************************/
                var toolsItem = new NativeMenuItem("Tools") { Menu = new NativeMenu(), Icon = new Bitmap(AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/general/custom-config-icon.png"))) };
                var tools = Knossos.GetTools();
                if (tools != null && tools.Any())
                {
                    foreach (var tool in tools)
                    {
                        var tItem = new NativeMenuItem(tool.name);
                        tItem.Click += (s, e) => {
                            if(tool.isInstalled)
                                tool?.Open();
                        };
                        toolsItem.Menu.Add(tItem);
                    }
                }
                if (toolsItem.Menu.Any())
                    trayIcon.Menu?.Add(toolsItem);
                /*****************************DEBUG*************************************/
                var debug = new NativeMenuItem("Debug") { Menu = new NativeMenu(), Icon = new Bitmap(AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/general/menu_debug.png"))) };
                var openFs2Log = new NativeMenuItem("Open fs2_open.log");
                openFs2Log.Click += (s, e) => {
                    OpenFS2Log();
                };
                debug.Menu.Add(openFs2Log);
                var openLog = new NativeMenuItem("Open knossos.log");
                openLog.Click += (s, e) => {
                    OpenLog();
                };
                debug.Menu.Add(openLog);
                trayIcon.Menu?.Add(debug);
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "App.StartTrayIconApp()", ex);
            }

            /*****************************CLOSE***********************************/
            trayIcon.Menu?.Add(new NativeMenuItemSeparator());
            var close = new NativeMenuItem("Exit Knossos.NET");
            close.Click += (s, e) => { 
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    trayIcon?.Dispose();
                    desktop.Shutdown();
                }
            };
            trayIcon.Menu?.Add(close);
            GC.Collect();
        }

        private NativeMenuItem CreateModMenuItem(Mod mod)
        {
            var modItem = new NativeMenuItem(mod.ToString()) { Menu = new NativeMenu() };
            /*************************************MOD SUB BUTTONS*************************************/
            modItem.Menu.Add(CreateLaunchFSOButton(mod, FsoExecType.Release));

            var advItem = new NativeMenuItem("Advanced") { Menu = new NativeMenu() };
            if (mod.devMode)
            {
                modItem.Menu.Add(CreateLaunchFSOButton(mod, FsoExecType.Fred2));
            }
            else
            {
                advItem.Menu.Add(CreateLaunchFSOButton(mod, FsoExecType.Fred2));
            }

            advItem.Menu.Add(CreateLaunchFSOButton(mod, FsoExecType.Debug));
            advItem.Menu.Add(CreateLaunchFSOButton(mod, FsoExecType.Fred2Debug));
            advItem.Menu.Add(CreateLaunchFSOButton(mod, FsoExecType.QtFred));
            advItem.Menu.Add(CreateLaunchFSOButton(mod, FsoExecType.QtFredDebug));

            modItem.Menu.Add(advItem);

            var settings = new NativeMenuItem("Settings");
            settings.Click += (s, e) => {
                var dialog = new ModSettingsView();
                dialog.DataContext = new ModSettingsViewModel(mod);
                dialog.Show();
            };
            modItem.Menu.Add(settings);
            /*****************************************************************************************/
            return modItem;
        }

        private NativeMenuItem CreateLaunchFSOButton(Mod mod, FsoExecType fsoExecType)
        {
            var name = fsoExecType != FsoExecType.Release ? fsoExecType.ToString() : "Play";
            var item = new NativeMenuItem(name);
            item.Click += (s, e) => {
                Knossos.PlayMod(mod, fsoExecType);
            };
            return item;
        }

        private void OpenLog()
        {
            if (File.Exists(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "Knossos.log"))
            {
                try
                {
                    var cmd = new Process();
                    cmd.StartInfo.FileName = KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "Knossos.log";
                    cmd.StartInfo.UseShellExecute = true;
                    cmd.Start();
                    cmd.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "App.OpenLog", ex);
                }
            }
            else
            {
                if (MainWindow.instance != null)
                    MessageBox.Show(MainWindow.instance, "Log File " + KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "Knossos.log not found.", "File not found", MessageBox.MessageBoxButtons.OK);
            }
        }

        private void OpenFS2Log()
        {
            if (File.Exists(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "fs2_open.log"))
            {
                try
                {
                    var cmd = new Process();
                    cmd.StartInfo.FileName = KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "fs2_open.log";
                    cmd.StartInfo.UseShellExecute = true;
                    cmd.Start();
                    cmd.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "App.OpenFS2Log", ex);
                }
            }
            else
            {
                if (MainWindow.instance != null)
                    MessageBox.Show(MainWindow.instance, "Log File " + KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "fs2_open.log not found.", "File not found", MessageBox.MessageBoxButtons.OK);
            }
        }
    }
}
