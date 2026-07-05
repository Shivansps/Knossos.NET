using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Knossos.NET.ViewModels
{
    public partial class CustomHomeViewModel : ViewModelBase
    {
        private List<Mod> modVersions = new List<Mod>();
        private Mod? GetActiveInstalledModVersion
        {
            get
            {
                if(ActiveVersionIndex >= 0 && ActiveVersionIndex < modVersions.Count())
                {
                    return modVersions[ActiveVersionIndex];
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "CustomHomeViewModel.GetActiveInstalledModVersion()", "ActiveVersionIndex was " + ActiveVersionIndex + " and modVersions.Count() was " + modVersions.Count());
                    return null;
                }
            }
        }

        private List<Mod> nebulaModVersions = new List<Mod>();
        private CancellationTokenSource? cancellationTokenSource = null;

        [ObservableProperty]
        internal int activeVersionIndex = 0;

        internal ObservableCollection<string> VersionItems { get; set; } = new ObservableCollection<string>();

        [ObservableProperty]
        internal bool installed = false;

        [ObservableProperty]
        internal bool installing = false;

        [ObservableProperty]
        internal bool isUpdateReady = false;

        [ObservableProperty]
        internal bool nebulaVersionsAvailable = false;

        [ObservableProperty]
        internal string? backgroundImage = CustomLauncher.HomeBackgroundImage;

        [ObservableProperty]
        internal int animate = 0;

        [ObservableProperty]
        internal bool nebulaServices = CustomLauncher.UseNebulaServices;

        [ObservableProperty]
        internal string? welcomeHtml = CustomLauncher.HomeWelcomeHtml;

        [ObservableProperty]
        internal Thickness welcomeMargin = new Thickness(50, 50, 50, 0);

        [ObservableProperty]
        internal bool showBasePathSelector = false;

        [ObservableProperty]
        internal string newBasePath = string.Empty;

        [ObservableProperty]
        internal bool changeBasePathButtonVisible = false;

        [ObservableProperty]
        internal string stretchMode = CustomLauncher.HomeBackgroundStretchMode;

        /// <summary>
        /// Handled in mainview, displays a small task viewer in the home screen
        /// </summary>
        [ObservableProperty]
        public ViewModelBase? taskVM;
        [ObservableProperty]
        internal bool isQtFredAvailable = false;
        [ObservableProperty]
        internal bool isQtFredDebugAvailable = false;


        public CustomHomeViewModel()
        {
            if (CustomLauncher.HomeWelcomeMargin != null)
            {
                try
                {
                    WelcomeMargin = Thickness.Parse(CustomLauncher.HomeWelcomeMargin);
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "CustomHomeViewModel.Constructor()", ex);
                }
            }
        }

        /// <summary>
        /// Check if we are in normal mode, but we dont have a saved base path
        /// </summary>
        public void CheckBasePath()
        {
            if (!Knossos.inPortableMode && Knossos.GetKnossosLibraryPath() == null)
            {
                ShowBasePathSelector = true;
            }
        }

        /// <summary>
        /// Checks if the current active mod version is passing a cmdline argument to fso
        /// </summary>
        /// <param name="cmdlineToCheck"></param>
        /// <returns></returns>
        public bool ActiveVersionHasCmdline(string cmdlineToCheck)
        {
            if(GetActiveInstalledModVersion != null)
            {
                var res = GetActiveInstalledModVersion.GetModCmdLine()?.ToLower().Contains(cmdlineToCheck.ToLower());
                if (res.HasValue)
                    return res.Value;
            }
            return false;
        }

        /// <summary>
        /// Handler for the hardcoded UI buttons
        /// </summary>
        /// <param name="cmd"></param>
        internal void HardcodedButtonCommand(object cmd)
        {
            switch ((string)cmd)
            {
                case "play": if(GetActiveInstalledModVersion != null) Knossos.PlayMod(GetActiveInstalledModVersion, FsoExecType.Release); break;
                case "playvr": if (GetActiveInstalledModVersion != null) Knossos.PlayMod(GetActiveInstalledModVersion, FsoExecType.Release, false, 0, true); break;
                case "fred2": if (GetActiveInstalledModVersion != null) Knossos.PlayMod(GetActiveInstalledModVersion, FsoExecType.Fred2); break;
                case "fred2debug": if (GetActiveInstalledModVersion != null) Knossos.PlayMod(GetActiveInstalledModVersion, FsoExecType.Fred2Debug); break;
                case "debug": if (GetActiveInstalledModVersion != null) Knossos.PlayMod(GetActiveInstalledModVersion, FsoExecType.Debug); break;
                case "qtfred": if (GetActiveInstalledModVersion != null) Knossos.PlayMod(GetActiveInstalledModVersion, FsoExecType.QtFred); break;
                case "qtfreddebug": if (GetActiveInstalledModVersion != null) Knossos.PlayMod(GetActiveInstalledModVersion, FsoExecType.QtFredDebug); break;
                case "install": Install(); break;
                case "cancel": Cancel(); break;
                case "update": Update(); break;
                case "modify": Modify(); break;
                case "delete": if (GetActiveInstalledModVersion != null) RemoveInstalledModVersion(GetActiveInstalledModVersion);  break;
                case "details": Details();  break;
                case "settings": Settings(); break;
                case "logfile": OpenFS2Log(); break;
            }
        }

        /// <summary>
        /// Calls to cancel running install tasks
        /// </summary>
        private void Cancel()
        {
            Installing = false;
            try
            {
                cancellationTokenSource?.Cancel();
            }
            catch { }
            cancellationTokenSource = null;
            TaskViewModel.Instance?.CancelAllInstallTaskWithID(CustomLauncher.ModID!, null);
        }

        /// <summary>
        /// Opens mod install window for this mod id
        /// </summary>
        private async void Install()
        {
            if (nebulaModVersions.Any() && CustomLauncher.UseNebulaServices)
            {
                var dialog = new ModInstallView();
                dialog.DataContext = new ModInstallViewModel(nebulaModVersions.First(), dialog);
                await dialog.ShowDialog<ModInstallView?>(MainWindow.instance!);
            }
            else
            {
                Log.Add(Log.LogSeverity.Error, "CustomHomeViewModel.Install()", "Tried to install but no nebula versions were loaded.");
            }
        }

        /// <summary>
        /// Opens mod install window for this mod id in modify active version mode
        /// </summary>
        private async void Modify()
        {
            if (GetActiveInstalledModVersion != null && CustomLauncher.UseNebulaServices)
            {
                var dialog = new ModInstallView();
                dialog.DataContext = new ModInstallViewModel(GetActiveInstalledModVersion, dialog, GetActiveInstalledModVersion.version);
                await dialog.ShowDialog<ModInstallView?>(MainWindow.instance!);
            }
            else
            {
                Log.Add(Log.LogSeverity.Error, "CustomHomeViewModel.Modify()", "GetActiveInstalledModVersion was null. ActiveVersionIndex: " + ActiveVersionIndex + " modVerions.count()" + modVersions.Count());
            }
        }

        /// <summary>
        /// Opens mod install window for this mod id
        /// </summary>
        private async void Update()
        {
            if (GetActiveInstalledModVersion != null && CustomLauncher.UseNebulaServices)
            {
                var dialog = new ModInstallView();
                dialog.DataContext = new ModInstallViewModel(GetActiveInstalledModVersion, dialog);
                await dialog.ShowDialog<ModInstallView?>(MainWindow.instance!);
            }
            else
            {
                Log.Add(Log.LogSeverity.Error, "CustomHomeViewModel.Update()", "GetActiveInstalledModVersion was null. ActiveVersionIndex: " + ActiveVersionIndex + " modVerions.count()" + modVersions.Count());
            }
        }

        /// <summary>
        /// Opens this mod details dialog
        /// </summary>
        private async void Details()
        {
            var dialog = new ModDetailsView();
            var mod = GetActiveInstalledModVersion != null ? GetActiveInstalledModVersion : nebulaModVersions.FirstOrDefault();
            if (mod != null)
            {
                dialog.DataContext = new ModDetailsViewModel(mod, dialog);
                await dialog.ShowDialog<ModDetailsView?>(MainWindow.instance);
            }
            else
            {
                Log.Add(Log.LogSeverity.Error, "CustomHomeViewModel.Details()", "Mod was null, not installed or nebulas versions of this modid were found.");
            }
        }

        /// <summary>
        /// Opens this mod settings dialog
        /// </summary>
        internal async void Settings()
        {
            if (GetActiveInstalledModVersion != null)
            {
                var dialog = new ModSettingsView();
                dialog.DataContext = new ModSettingsViewModel(GetActiveInstalledModVersion);
                await dialog.ShowDialog<ModSettingsView?>(MainWindow.instance);
            }
            else
            {
                Log.Add(Log.LogSeverity.Error, "CustomHomeViewModel.Settings()", "Mod was null, not installed versions of this modid were found.");
            }
        }

        /// <summary>
        /// Opens the fs_open.log file, if it exists.
        /// </summary>
        private void OpenFS2Log()
        {
            if (File.Exists(Path.Combine(KnUtils.GetFSODataFolderPath(), "data", "fs2_open.log")))
            {
                try
                {
                    var cmd = new Process();
                    cmd.StartInfo.FileName = Path.Combine(KnUtils.GetFSODataFolderPath(), "data", "fs2_open.log");
                    cmd.StartInfo.UseShellExecute = true;
                    cmd.Start();
                    cmd.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "CustomHomeViewModel.OpenFS2Log", ex);
                }
            }
            else
            {
                MessageBox.Show(MainWindow.instance, "Log File " + Path.Combine(KnUtils.GetFSODataFolderPath(), "data", "fs2_open.log") + " not found.", "File not found", MessageBox.MessageBoxButtons.OK);
            }
        }

        /// <summary>
        /// Removes ONE installed mod version from the disk
        /// </summary>
        /// <param name="mod"></param>
        public async void RemoveInstalledModVersion(Mod mod)
        {
            try
            {
                if (CustomLauncher.ModID == mod.id)
                {
                    if (TaskViewModel.Instance!.IsSafeState())
                    {
                        if (GetActiveInstalledModVersion != null)
                        {
                            if (modVersions.Count > 1)
                            {
                                var resp = await MessageBox.Show(MainWindow.instance!, "You are about to delete version " + GetActiveInstalledModVersion.version + ", this will remove this version only. Do you want to continue?", "Delete version", MessageBox.MessageBoxButtons.YesNo);
                                if (resp == MessageBox.MessageBoxResult.Yes)
                                {
                                    var delete = modVersions[ActiveVersionIndex];
                                    var verDel = VersionItems[ActiveVersionIndex];
                                    modVersions.Remove(delete);
                                    Knossos.RemoveMod(delete);
                                    VersionItems.Remove(verDel);
                                    ActiveVersionIndex = 0;
                                }
                            }
                            else
                            {
                                var resp = await MessageBox.Show(MainWindow.instance!, "You are about to delete the last installed version. Do you want to continue?", "Delete last version", MessageBox.MessageBoxButtons.YesNo);
                                if (resp == MessageBox.MessageBoxResult.Yes)
                                {
                                    //Last version
                                    modVersions[0].installed = false;
                                    MainViewModel.Instance?.AddNebulaMod(modVersions[0]);
                                    Knossos.RemoveMod(modVersions[0].id);
                                    Installed = false;
                                    Installing = false;
                                    modVersions.Clear();
                                    VersionItems.Clear();
                                    ActiveVersionIndex = 0;
                                }
                            }
                        }
                    }
                    else
                    {
                        await MessageBox.Show(MainWindow.instance!, "You can not delete a mod while other install tasks are running, wait until they finish and try again.", "Tasks are running", MessageBox.MessageBoxButtons.OK);
                    }
                }
            }catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "CustomHomeViewModel.RemoveInstalledModVersion()", ex);
            }
        }

        /// <summary>
        /// This deletes all versions of this mod
        /// Not implemented or needed
        /// </summary>
        /// <param name="id"></param>
        public void RemoveMod(string id)
        {
            if (CustomLauncher.ModID == id)
            {
                //Installed = false;
                //Installing = false;
            }
        }

        /// <summary>
        /// Remove starts cancellation of a install taks with this mod id
        /// </summary>
        /// <param name="id"></param>
        public void CancelModInstall(string id)
        {
            if (CustomLauncher.ModID == id)
            {
                Cancel();
            }
        }

        /// <summary>
        /// Sets the install mode, so the cancel tasks button can be displayed
        /// </summary>
        /// <param name="id"></param>
        /// <param name="cancelToken"></param>
        public void SetInstalling(string id, CancellationTokenSource cancelToken)
        {
            if (CustomLauncher.ModID == id)
            {
                cancellationTokenSource = cancelToken;
                Installing = true;
            }
        }

        /// <summary>
        /// Add a installed mod version of this TC.
        /// It will check if the ID matches the one in CustomLauncher.ModID
        /// </summary>
        /// <param name="modJson"></param>
        public void AddModVersion(Mod modJson)
        {
            if (modJson.id == CustomLauncher.ModID)
            {
                Log.Add(Log.LogSeverity.Information, "CustomHomeViewModel.AddModVersion()", "Adding additional version for mod id: " + CustomLauncher.ModID + " -> " + modJson.folderName);
                if (modVersions.Any())
                {
                    string currentVersion = modVersions[ActiveVersionIndex].version;
                    modVersions.Add(modJson);
                    modVersions.Sort((o1, o2) => -SemanticVersion.Compare(o1.version, o2.version));
                    if (SemanticVersion.Compare(modJson.version, currentVersion) > 0)
                    {
                        Log.Add(Log.LogSeverity.Information, "CustomHomeViewModel.AddModVersion()", "Changing active version for " + modJson.title + " from " + modVersions[ActiveVersionIndex].version + " to " + modJson.version);
                        VersionItems.Clear();
                        modVersions.ForEach(x => VersionItems.Add(x.version));
                        ActiveVersionIndex = -1;
                        ActiveVersionIndex = modVersions.FindIndex((m) => m.version.Equals(modJson.version));
                    }
                    else
                    {
                        VersionItems.Add(modJson.version);
                    }
                }
                else
                {
                    ActiveVersionIndex = -1;
                    modVersions.Add(modJson);
                    VersionItems.Add(modJson.version);
                    ActiveVersionIndex = 0;
                }
                Installed = modVersions.Any();
            }
        }

        /// <summary>
        /// Add a Nebula mod version of this TC.
        /// It will check if the ID matches the one in CustomLauncher.ModID
        /// </summary>
        /// <param name="modJson"></param>
        public void AddNebulaModVersion(Mod modJson)
        {
            if (modJson.id == CustomLauncher.ModID)
            {
                Log.Add(Log.LogSeverity.Information, "CustomHomeViewModel.AddNebulaModVersion()", "Adding additional nebula version for mod id: " + CustomLauncher.ModID + " -> " + modJson.version);
                nebulaModVersions.Add(modJson);
                nebulaModVersions.Sort((o1, o2) => -SemanticVersion.Compare(o1.version, o2.version));
                NebulaVersionsAvailable = true;
            }
        }

        /// <summary>
        /// Tell the TC home screen an update is avalible or not
        /// It will check if the mod id actually matches to the custom CustomLauncher.ModID
        /// </summary>
        /// <param name="id"></param>
        /// <param name="value"></param>
        public void UpdateIsAvailable(string id, bool value, string? newVersion)
        {
            if (id == CustomLauncher.ModID)
            {
                IsUpdateReady = value;
                if (IsUpdateReady && newVersion != null)
                {
                    Dispatcher.UIThread.Invoke(() => TaskViewModel.Instance?.AddMessageTask("An update is available! " + newVersion), DispatcherPriority.Background);
                }
            }
        }

        /// <summary>
        /// Run code when the user clicks the menu item to open this view
        /// </summary>
        public void ViewOpened()
        {
            Animate = 1;
        }

        /// <summary>
        /// Run code when the user exit this view
        /// </summary>
        public void ViewClosed()
        {
            Animate = 0;
        }

        /// <summary>
        /// Changes the knossos library path, reloads settings and nebula repo
        /// </summary>
        internal async void BrowseFolderCommand()
        {
            ChangeBasePathButtonVisible = false;
            NewBasePath = string.Empty;
            FolderPickerOpenOptions options = new FolderPickerOpenOptions();
            options.AllowMultiple = false;
            try
            {
                var result = await KnUtils.GetTopLevel().StorageProvider.OpenFolderPickerAsync(options);
                if (result != null && result.Count > 0)
                {

                    // Test if we can write to the new library directory
                    using (StreamWriter writer = new StreamWriter(result[0].Path.LocalPath.ToString() + Path.DirectorySeparatorChar + "test.txt"))
                    {
                        writer.WriteLine("test");
                    }
                    File.Delete(Path.Combine(result[0].Path.LocalPath.ToString() + Path.DirectorySeparatorChar + "test.txt"));
                    NewBasePath = result[0].Path.LocalPath.ToString();
                    ChangeBasePathButtonVisible = true;
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "CustomHomeViewModel.BrowseFolderCommand() - test read/write was not successful: ", ex);
                await Dispatcher.UIThread.Invoke(async () => {
                    await MessageBox.Show(null, "We were not able to write to this folder.  Please select another library folder.", "Cannot Select Folder", MessageBox.MessageBoxButtons.OK);
                }).ConfigureAwait(false);
            }
        }

        internal void ChangeBasePath()
        {
            if (NewBasePath != string.Empty)
            {
                Knossos.globalSettings.basePath = NewBasePath;
                Knossos.globalSettings.Save();
                Knossos.ResetBasePath();
                ShowBasePathSelector = false;
            }
        }

        public void RefreshQtFredAvailability()
        {
            var bools = modVersions[ActiveVersionIndex].IsQtFredAvailable();

            if (bools == null || bools.Length < 2){
                IsQtFredAvailable = false;
                IsQtFredDebugAvailable = false;
                return;
            }

            IsQtFredAvailable = bools[0];
            IsQtFredDebugAvailable = bools[1];
        }
    }
}
