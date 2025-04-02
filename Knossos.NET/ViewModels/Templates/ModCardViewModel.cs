using Avalonia.Platform;
using Avalonia.Media.Imaging;
using System;
using Knossos.NET.Models;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using Knossos.NET.Classes;
using Knossos.NET.Views;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Avalonia.Threading;
using System.Diagnostics;

namespace Knossos.NET.ViewModels
{
    public partial class ModCardViewModel : ViewModelBase, IComparable<ModCardViewModel>
    {
        /* General Mod variables */
        private ModDetailsView? detailsView = null;
        private ModSettingsView? settingsView = null;
        private List<Mod> modVersions = new List<Mod>();
        private int activeVersionIndex = 0;
        private bool devMode { get; set; } = false;
        public string ID { get; set; }

        public Mod? ActiveVersion 
        {
            get
            {
                if (modVersions.Count() > activeVersionIndex)
                    return modVersions[activeVersionIndex];
                else
                    return null;
            }
        }

        /* UI Bindings */
        [ObservableProperty]
        internal string? name;
        [ObservableProperty]
        internal string? modVersion;
        [ObservableProperty]
        internal Bitmap? image;
        private bool visible = true;
        /// <summary>
        /// External property to enable/disable mod card visibility
        /// </summary>
        public bool Visible
        {
            get
            {
                return visible;
            }
            set
            {
                if (visible != value)
                {
                    visible = value;
                    CardVisible = value;
                    if (CardVisible)
                    {
                        _ = LazyReLoadTileImageAsync();
                    }
                    else
                    {
                        Image = MainWindowViewModel.Instance?.placeholderTileImage;
                    }
                }
            }
        }
        [ObservableProperty]
        internal bool updateAvailable = false;
        [ObservableProperty]
        internal IBrush borderColor = KnUtils.GetResourceColor("ModCardBorderNormal");
        [ObservableProperty]
        internal bool isLocalMod = false;
        [ObservableProperty]
        internal bool isCustomConfig = false;
        [ObservableProperty]
        internal bool cardVisible = true;
        [ObservableProperty]
        internal bool isQtFredAvailable = false;
        [ObservableProperty]
        internal bool isQtFredDebugAvailable = false;
        private Bitmap? tileModBitmap;

        /* Should only be used by the editor preview */
        public ModCardViewModel()
        {
            Name = "default test string very long";
            ModVersion = "1.0.0";
            ID = "test";
            Image = new Bitmap(AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/general/NebulaDefault.png")));
        }

        public ModCardViewModel(Mod modJson)
        {
            modJson.ClearUnusedData();
            modVersions.Add(modJson);
            Name = modJson.title;
            ModVersion = modJson.version;
            if(modJson.modSource == ModSource.local)
            {
                isLocalMod = true;
            }
            ID = modJson.id;
            if (ID == "FS2")
            {
                //This is to disable mod delete and mod modify for retail fs2
                devMode = true;
            }
            else
            {
                devMode = modJson.devMode;
            }

            if (devMode && ID != "FS2")
            {
                BorderColor = KnUtils.GetResourceColor("ModCardBorderDevMode");
            }
            LoadImage();
        }

        /// <summary>
        /// Reloads the tile image to the view at a random time
        /// after the mod card itseft becomes visible
        /// </summary>
        private async Task LazyReLoadTileImageAsync()
        {
            await Task.Delay(new Random().Next(200, 700));
            Image = tileModBitmap != null ? tileModBitmap : MainWindowViewModel.Instance?.placeholderTileImage;
        }

        public void AddModVersion(Mod modJson)
        {
            modJson.ClearUnusedData();
            Log.Add(Log.LogSeverity.Information, "ModCardViewModel.AddModVersion()", "Adding additional version for mod id: " + ID + " -> " + modJson.folderName);
            string currentVersion = modVersions[activeVersionIndex].version;
            modVersions.Add(modJson);
            modVersions.Sort((o1, o2) => -SemanticVersion.Compare(o1.version, o2.version));
            if (SemanticVersion.Compare(modJson.version, currentVersion) > 0)
            {
                Log.Add(Log.LogSeverity.Information, "ModCardViewModel.AddModVersion()", "Changing active version for " + modJson.title + " from " + currentVersion + " to " + modJson.version);
                activeVersionIndex = modVersions.FindIndex((m) => m.version.Equals(modJson.version));
                Name = modJson.title;
                ModVersion = modJson.version + " (+" + (modVersions.Count - 1) + ")";
                LoadImage();
            }
        }

        public async void CheckDependencyActiveVersion()
        {
            await Task.Run(() =>
            {
                if (modVersions[activeVersionIndex].installed && !modVersions[activeVersionIndex].devMode && !UpdateAvailable)
                {
                    var missingDeps = modVersions[activeVersionIndex].GetMissingDependenciesList();
                    foreach (var dependency in missingDeps.ToList())
                    {
                        //If we are missing the FSO dep and the user selected a custom build, ignore it
                        if (modVersions[activeVersionIndex].modSettings.customBuildId != null && dependency.id == "FSO")
                        {
                            missingDeps.Remove(dependency);
                        }
                    }
                    if (missingDeps.Any())
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            BorderColor = KnUtils.GetResourceColor("ModCardBorderError");
                        });
                    }
                    else
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            BorderColor = KnUtils.GetResourceColor("ModCardBorderNormal");
                        });
                    }
                }

                RefreshQtFredAvailability();
            });
        }

        public void SwitchModVersion(int newIndex)
        {
            if (newIndex != activeVersionIndex)
            {
                Log.Add(Log.LogSeverity.Information, "ModCardViewModel.SwitchModVersion()", "Changing active version for mod id " + ID + " to " + modVersions[newIndex].version);
                activeVersionIndex = newIndex;
                Name = modVersions[newIndex].title;
                if(modVersions.Count() > 1)
                    ModVersion = modVersions[newIndex].version + " (+" + (modVersions.Count - 1) + ")";
                else
                    ModVersion = modVersions[newIndex].version;
                if (modVersions[newIndex].modSource == ModSource.local)
                {
                    IsLocalMod = true;
                }
                else
                {
                    IsLocalMod = false;
                }
                LoadImage();
                CheckDependencyActiveVersion();
                RefreshSpecialIcons();
                RefreshQtFredAvailability();
            }
        }

        /// <summary>
        /// Deletes a mod from the mod versions list and switches the active version to the newerest
        /// </summary>
        /// <param name="mod"></param>
        public void DeleteModVersion(Mod mod)
        {
            try
            {
                //if the details or settings view is open close it
                Dispatcher.UIThread.Invoke(() => { 
                    detailsView?.Close();
                    settingsView?.Close();
                });
                modVersions.Remove(mod);
                SwitchModVersion(modVersions.Count() - 1);
            }
            catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "ModCardViewModel.DeleteModVersion()", ex);
            }
        }

        /// <summary>
        /// Returns the number of mods in the modVersions list
        /// </summary>
        /// <returns>int</returns>
        public int GetNumberOfModVersions()
        {
            return modVersions.Count();
        }

        public void UpdateIsAvailable(bool value)
        {
            UpdateAvailable = value;
            if(value)
            {
                BorderColor = KnUtils.GetResourceColor("ModCardBorderUpdate");
            }
            else
            {
                BorderColor = KnUtils.GetResourceColor("ModCardBorderNormal");
            }
        }

        public void RefreshSpecialIcons()
        {
            IsCustomConfig = !modVersions[activeVersionIndex].modSettings.IsDefaultConfig();
        }

        public void RefreshQtFredAvailability()
        {
            var bools = modVersions[activeVersionIndex].IsQtFredAvailable();

            if (bools == null || bools.Length < 2){
                IsQtFredAvailable = false;
                IsQtFredDebugAvailable = false;
                return;
            }

            IsQtFredAvailable = bools[0];
            IsQtFredDebugAvailable = bools[1];
        }

        /* Button Commands */
        internal void ButtonCommand(object command)
        {
            switch((string)command)
            {
                case "play" : Knossos.PlayMod(modVersions[activeVersionIndex], FsoExecType.Release); break;
                case "playvr": Knossos.PlayMod(modVersions[activeVersionIndex], FsoExecType.Release, false, 0, true); break;
                case "fred2": Knossos.PlayMod(modVersions[activeVersionIndex], FsoExecType.Fred2); break;
                case "fred2debug": Knossos.PlayMod(modVersions[activeVersionIndex], FsoExecType.Fred2Debug); break;
                case "debug": Knossos.PlayMod(modVersions[activeVersionIndex], FsoExecType.Debug); break;
                case "qtfred": Knossos.PlayMod(modVersions[activeVersionIndex], FsoExecType.QtFred); break;
                case "qtfreddebug": Knossos.PlayMod(modVersions[activeVersionIndex], FsoExecType.QtFredDebug); break;
                case "update": ButtonCommandUpdate(); break;
                case "modify": ButtonCommandModify(); break;
                case "delete": ButtonCommandDelete(); break;
                case "details": ButtonCommandDetails(); break;
                case "settings": ButtonCommandSettings(); break;
                case "logfile": OpenFS2Log(); break;
            }
        }

        private void OpenFS2Log()
        {
            if (File.Exists(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "fs2_open.log"))
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
                    Log.Add(Log.LogSeverity.Error, "ModCardViewModel.OpenFS2Log", ex);
                }
            }
            else
            {
                if (MainWindow.instance != null)
                    MessageBox.Show(MainWindow.instance, "Log File " + Path.Combine(KnUtils.GetFSODataFolderPath(), "data", "fs2_open.log") + " not found.", "File not found", MessageBox.MessageBoxButtons.OK);
            }
        }

        internal async void ButtonCommandUpdate()
        {
            var dialog = new ModInstallView();
            dialog.DataContext = new ModInstallViewModel(modVersions[activeVersionIndex], dialog);
            await dialog.ShowDialog<ModInstallView?>(MainWindow.instance!);
        }

        internal async void ButtonCommandModify()
        {
            var dialog = new ModInstallView();
            dialog.DataContext = new ModInstallViewModel(modVersions[activeVersionIndex], dialog, modVersions[activeVersionIndex].version);
            await dialog.ShowDialog<ModInstallView?>(MainWindow.instance!);
        }

        internal async void ButtonCommandDelete()
        {
            if (!modVersions[activeVersionIndex].devMode)
            {
                if (TaskViewModel.Instance!.IsSafeState())
                {
                    var result = await MessageBox.Show(MainWindow.instance!, "You are going to remove mod " + Name + " .This will delete ALL versions of the mod. If you only want to delete a specific version you can do it from mod details.\n Do you really want to delete the mod?", "Delete mod", MessageBox.MessageBoxButtons.OKCancel);
                    if (result == MessageBox.MessageBoxResult.OK)
                    {
                        modVersions[modVersions.Count - 1].installed = false;
                        if (!IsLocalMod)
                        {
                            MainWindowViewModel.Instance?.AddNebulaMod(modVersions[modVersions.Count - 1]);
                        }
                        Knossos.RemoveMod(modVersions[activeVersionIndex].id);
                        MainWindowViewModel.Instance?.RunModStatusChecks();
                    }
                }
                else
                {
                    await MessageBox.Show(MainWindow.instance!, "You can not delete a mod while other install tasks are running, wait until they finish and try again.", "Tasks are running", MessageBox.MessageBoxButtons.OK);
                }
            }
            else
            {
                await MessageBox.Show(MainWindow.instance!, "Dev mode mods can not be delated from the main view, go to the Development section.", "Mod is dev mode", MessageBox.MessageBoxButtons.OK);
            }
        }

        internal async void ButtonCommandDetails()
        {
            if (MainWindow.instance != null)
            {
                var dialog = new ModDetailsView();
                dialog.DataContext = new ModDetailsViewModel(modVersions, activeVersionIndex, this, dialog);
                detailsView = dialog;
                await dialog.ShowDialog<ModDetailsView?>(MainWindow.instance);
                detailsView = null;
            }
        }

        internal async void ButtonCommandSettings()
        {
            if (MainWindow.instance != null)
            {
                var dialog = new ModSettingsView();
                dialog.DataContext = new ModSettingsViewModel(modVersions[activeVersionIndex],this);
                settingsView = dialog;
                await dialog.ShowDialog<ModSettingsView?>(MainWindow.instance);
            }
        }

        private void LoadImage()
        {
            Image = MainWindowViewModel.Instance?.placeholderTileImage;

            try
            {
                var tile = modVersions[activeVersionIndex].tile;
                if (!string.IsNullOrEmpty(tile))
                {
                    if (!tile.ToLower().Contains("http"))
                    {
                        tileModBitmap = new Bitmap(modVersions[activeVersionIndex].fullPath + Path.DirectorySeparatorChar + tile);
                    }
                    else
                    {
                        Task.Run(async () =>
                        {
                            using (var fs = await KnUtils.GetRemoteResourceStream(tile))
                            {
                                if(fs != null)
                                    tileModBitmap = new Bitmap(fs);
                            }
                        }); 
                    }
                    if (tileModBitmap != null)
                    {
                        Image = tileModBitmap;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Warning, "ModCardViewModel.LoadImage", ex);
            }
        }

        public int CompareTo(ModCardViewModel? other)
        {
            if (other == null)
                return -1;
            return Mod.SortMods(ActiveVersion, other.ActiveVersion);
        }
    }
}
