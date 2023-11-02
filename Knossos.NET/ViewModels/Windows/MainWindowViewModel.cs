using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Knossos.NET.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase 
    {
        public static MainWindowViewModel? Instance { get; set; }

        /* UI Bindings, use the uppercase version, otherwise changes will not register */
        [ObservableProperty]
        internal string appTitle = "Knossos.NET v" + Knossos.AppVersion;
        [ObservableProperty]
        internal ModListViewModel installedModsView = new ModListViewModel();
        [ObservableProperty]
        internal NebulaModListViewModel nebulaModsView = new NebulaModListViewModel();
        [ObservableProperty]
        internal FsoBuildsViewModel fsoBuildsView = new FsoBuildsViewModel();
        [ObservableProperty]
        internal DeveloperModsViewModel developerModView = new DeveloperModsViewModel();
        [ObservableProperty]
        internal PxoViewModel pxoView = new PxoViewModel();
        [ObservableProperty]
        internal GlobalSettingsViewModel globalSettingsView = new GlobalSettingsViewModel();
        [ObservableProperty]
        internal TaskViewModel taskView = new TaskViewModel();
        [ObservableProperty]
        internal string uiConsoleOutput = string.Empty;

        internal int tabIndex = 0;
        internal int TabIndex
        {
            get => tabIndex;
            set
            {
                if (value != tabIndex)
                {
                    this.SetProperty(ref tabIndex, value);
                    if (tabIndex == 1) //Nebula Mods
                    {
                        NebulaModsView.OpenTab();
                    }
                    if (tabIndex == 4) //PXO
                    {
                        PxoViewModel.Instance!.InitialLoad();
                    }
                    if (tabIndex == 5) //Settings
                    {
                        Knossos.globalSettings.Load();
                        GlobalSettingsView.LoadData();
                        Knossos.globalSettings.EnableIniWatch();
                        GlobalSettingsView.UpdateImgCacheSize();
                    }
                    else
                    {
                        Knossos.globalSettings.DisableIniWatch();
                    }
                }
            }
        }

        public MainWindowViewModel()
        {
            Instance = this;
            string[] args = Environment.GetCommandLineArgs();
            bool isQuickLaunch = false;
            foreach (var arg in args)
            {
                if (arg.ToLower() == "-playmod")
                {
                    isQuickLaunch = true;
                }
            }
            Knossos.StartUp(isQuickLaunch);
        }

        /* External Commands */
        public void AddDevMod(Mod devmod)
        {
            DeveloperModView.AddMod(devmod);
        }

        public void RunModStatusChecks()
        {
            InstalledModsView.RunModStatusChecks();
        }

        public void ClearViews()
        {
            InstalledModsView?.ClearView();
            FsoBuildsView?.ClearView();
            NebulaModsView?.ClearView();
        }

        public void MarkAsUpdateAvalible(string id, bool value = true)
        {
            InstalledModsView.UpdateIsAvalible(id, value);
        }

        public void AddInstalledMod(Mod modJson)
        {
            InstalledModsView.AddMod(modJson);
        }

        public void AddNebulaMod(Mod modJson)
        {
            NebulaModsView.AddMod(modJson);
        }

        public void BulkLoadNebulaMods(List<Mod> mods, bool clear)
        {
            if(clear)
                NebulaModsView.ClearView();
            NebulaModsView.AddMods(mods);
        }

        public void CancelModInstall(string id)
        {
            NebulaModsView.CancelModInstall(id);
        }

        public void RemoveInstalledMod(string id)
        {
            InstalledModsView.RemoveMod(id);
        }

        public void GlobalSettingsLoadData()
        {
            GlobalSettingsView.LoadData();
        }

        public void WriteToUIConsole(string message)
        {
            UiConsoleOutput += "\n"+ message;
        }

        /* Debug Section */
        internal void OpenLog()
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
                    Log.Add(Log.LogSeverity.Error, "MainWindowViewModel.ReloadLog",ex);
                }
            }
            else
            {
                if(MainWindow.instance != null)
                    MessageBox.Show(MainWindow.instance, "Log File " + KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "Knossos.log not found.","File not found",MessageBox.MessageBoxButtons.OK);
            }
        }

        internal void OpenSettings()
        {
            if (File.Exists(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "settings.json"))
            {
                try
                {
                    var cmd = new Process();
                    cmd.StartInfo.FileName = KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar +"settings.json";
                    cmd.StartInfo.UseShellExecute = true;
                    cmd.Start();
                    cmd.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "MainWindowViewModel.ReloadLog", ex);
                }
            }
            else
            {
                if (MainWindow.instance != null)
                    MessageBox.Show(MainWindow.instance, "Log File " + KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "settings.json not found.", "File not found", MessageBox.MessageBoxButtons.OK);
            }
        }

        internal void OpenFS2Log()
        {
            if (File.Exists(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "fs2_open.log"))
            {
                try
                {
                    var cmd = new Process();
                    cmd.StartInfo.FileName = KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "data"+ Path.DirectorySeparatorChar + "fs2_open.log";
                    cmd.StartInfo.UseShellExecute = true;
                    cmd.Start();
                    cmd.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "MainWindowViewModel.ReloadFS2Log", ex);
                }
            }
            else
            {
                if (MainWindow.instance != null)
                    MessageBox.Show(MainWindow.instance, "Log File " + KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar+ "data"+ Path.DirectorySeparatorChar+"fs2_open.log not found.", "File not found", MessageBox.MessageBoxButtons.OK);
            }
        }

        internal void OpenFS2Ini()
        {
            if (File.Exists(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar+ "fs2_open.ini"))
            {
                try
                {
                    var cmd = new Process();
                    cmd.StartInfo.FileName = KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "fs2_open.ini";
                    cmd.StartInfo.UseShellExecute = true;
                    cmd.Start();
                    cmd.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "MainWindowViewModel.ReloadFS2ini", ex);
                }
            }
            else
            {
                if (MainWindow.instance != null)
                    MessageBox.Show(MainWindow.instance, "Log File " + KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "fs2_open.ini not found.", "File not found", MessageBox.MessageBoxButtons.OK);
            }
        }

        internal async void UploadFS2Log()
        {
            if (File.Exists(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "fs2_open.log"))
            {
                try
                {
                    var logString = File.ReadAllText(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "fs2_open.log",System.Text.Encoding.UTF8);
                    if(logString.Trim() != string.Empty)
                    {
                        var status = await Nebula.UploadLog(logString);
                        if(!status)
                        {
                            if (MainWindow.instance != null)
                                await MessageBox.Show(MainWindow.instance, "An error has ocurred while uploading the log file, check the log below.", "Upload log error", MessageBox.MessageBoxButtons.OK);
                        }
                    }
                    else
                    {
                        if (MainWindow.instance != null)
                            await MessageBox.Show(MainWindow.instance, "The log file is empty.", "Error", MessageBox.MessageBoxButtons.OK);
                    }
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "MainWindowViewModel.UploadFS2Log", ex);
                }
            }
            else
            {
                if (MainWindow.instance != null)
                    await MessageBox.Show(MainWindow.instance, "Log File " + KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "fs2_open.log not found.", "File not found", MessageBox.MessageBoxButtons.OK);
            }
        }
        internal async void UploadKnossosConsole()
        {
            try
            {
                var status = await Nebula.UploadLog(UiConsoleOutput);
                if (!status)
                {
                    if (MainWindow.instance != null)
                        await MessageBox.Show(MainWindow.instance, "An error has ocurred while uploading the console output, check the log below.", "Upload error", MessageBox.MessageBoxButtons.OK);
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "MainWindowViewModel.UploadKnossosConsole", ex);
            }
        }
    }
}
