using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using Avalonia.Threading;
using System;
using System.IO;
using VP.NET;
using System.Diagnostics;

namespace Knossos.NET.ViewModels
{
    /// <summary>
    /// Mod Settings Window View Model
    /// </summary>
    public partial class ModSettingsViewModel : ViewModelBase
    {
        private Mod? modJson;
        private ModCardViewModel? modCardViewModel;

        internal ObservableCollection<DependencyItemViewModel> DepItems { get; set; } = new ObservableCollection<DependencyItemViewModel>();

        /* UI Variables with ObervableProperty*/
        [ObservableProperty]
        internal bool shortcutEnabled = KnUtils.IsWindows;
        [ObservableProperty]
        internal bool configureBuildOpen = false;
        [ObservableProperty]
        internal string title = string.Empty;
        [ObservableProperty]
        internal bool customDependencies = false;
        [ObservableProperty]
        internal FsoBuildPickerViewModel fsoPicker;
        [ObservableProperty]
        internal FsoFlagsViewModel? fsoFlags = null;
        [ObservableProperty]
        internal string modSize = "0GB";
        [ObservableProperty]
        internal bool compressionAvailable = false;
        [ObservableProperty]
        internal bool compressed = false;
        [ObservableProperty]
        internal bool isDevMode = false;
        [ObservableProperty]
        internal string quickLaunch = "";
        [ObservableProperty]
        internal bool ignoreGlobalCmd = false;
        [ObservableProperty]
        internal string buildMissingWarning = string.Empty;
        [ObservableProperty]
        internal bool noIngameOptions = false;
        [ObservableProperty]
        internal bool ingameOptionsFileExist = false;

        public ModSettingsViewModel()
        {
            fsoPicker = new FsoBuildPickerViewModel();
        }

        /// <summary>
        /// Open Mod Settings of a certain, installed, Mod.
        /// If modcard is passed it will force a status icons update on Save()
        /// Reads compression settings, it checks mod folder size, reads mods the dependency list
        /// </summary>
        /// <param name="modJson"></param>
        /// <param name="modCard"></param>
        public ModSettingsViewModel(Mod modJson, ModCardViewModel? modCard=null)
        {
            try
            {
                if(!KnUtils.IsAppImage)
                {
                    QuickLaunch = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName + " -playmod " + modJson.id + " -version " + modJson.version + " -exec Default";
                }
                else
                {
                    QuickLaunch = KnUtils.AppImagePath + " -playmod " + modJson.id + " -version " + modJson.version + " -exec Default";
                }
            }catch { }
            this.modJson = modJson;
            modCardViewModel = modCard;
            isDevMode = modJson.devMode;
            compressed = modJson.modSettings.isCompressed;
            ignoreGlobalCmd = modJson.modSettings.ignoreGlobalCmd;
            noIngameOptions = modJson.modSettings.noIngameOptions;
            if (Knossos.globalSettings.modCompression != CompressionSettings.Disabled && !modJson.modSettings.isCompressed)
            {
                compressionAvailable = true;
            }

            Title = modJson.title + " " + modJson.version + " Mod " + "Settings";
            CreateDependencyItems();

            if (modJson.modSettings.customDependencies != null)
            {
                CustomDependencies = true;
                CustomDependenciesClick();
            }
            
            if(modJson.modSettings.customBuildId == null)
            {
                fsoPicker = new FsoBuildPickerViewModel(null);
            }
            else
            {
                if (modJson.modSettings.customBuildExec != null)
                {
                    fsoPicker = new FsoBuildPickerViewModel(new FsoBuild(modJson.modSettings.customBuildExec));
                }
                else
                {
                    var matchingBuilds = Knossos.GetInstalledBuildsList(modJson.modSettings.customBuildId);
                    if (matchingBuilds.Any())
                    {
                        var theBuild = matchingBuilds.FirstOrDefault(build => build.version == modJson.modSettings.customBuildVersion);
                        if (theBuild != null)
                        {
                            fsoPicker = new FsoBuildPickerViewModel(theBuild);
                        }
                        else
                        {
                            Log.Add(Log.LogSeverity.Warning, "ModSettingsViewModel.Constructor()", "Missing user-saved build version for mod: " + modJson.title + " - " + modJson.version + " requested build id: " + modJson.modSettings.customBuildId + " and version: " + modJson.modSettings.customBuildVersion);
                            BuildMissingWarning = "Missing user-saved build version for mod: " + modJson.title + " - " + modJson.version + " requested build id: " + modJson.modSettings.customBuildId + " and version: " + modJson.modSettings.customBuildVersion;
                            fsoPicker = new FsoBuildPickerViewModel(null);
                        }
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Warning, "ModSettingsViewModel.Constructor()", "Missing user-saved build id for mod: " + modJson.title + " - " + modJson.version + " requested build id: " + modJson.modSettings.customBuildId);
                        BuildMissingWarning = "Missing user-saved build id for mod: " + modJson.title + " - " + modJson.version + " requested build id: " + modJson.modSettings.customBuildId;
                        fsoPicker = new FsoBuildPickerViewModel(null);
                    }
                }
            }
            CheckIfOptionsFileExist();
            //Size
            Task.Factory.StartNew(() => UpdateModSize());
        }

        /// <summary>
        /// Generate mod shortcut on windows
        /// </summary>
        internal void Shortcut()
        {
            var knPath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
            if (knPath != null)
            {
                if (KnUtils.IsAppImage)
                {
                    knPath = KnUtils.AppImagePath;
                }
                KnUtils.CreateDesktopShortcut(modJson!.title, knPath, "-playmod " + modJson.id + " -version " + modJson.version + " -exec Default");
            }
        }

        /// <summary>
        /// Update Mod Folder size value on UI
        /// </summary>
        private async void UpdateModSize()
        {
            if(modJson == null || modJson.fullPath == string.Empty)
            {
                return;
            }

            try
            {
                long byteCount = 0;
                // with retail all regular mods are included in the size, so handle it a little different
                // to get the correct value
                if (modJson.id == "FS2")
                {
                    byteCount = await KnUtils.GetSizeOfFolderInBytes(modJson.fullPath, false);
                    byteCount += await KnUtils.GetSizeOfFolderInBytes(Path.Combine(modJson.fullPath, "data"));
                }
                else
                {
                    byteCount = await KnUtils.GetSizeOfFolderInBytes(modJson.fullPath);
                }
                ModSize = KnUtils.FormatBytes(byteCount);
                if (modJson.modSettings.isCompressed)
                    ModSize += " (Compressed)";
            }catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "ModSettingsViewModel.UpdateModSize()", ex);
            }
        }

        /// <summary>
        /// Load the Dependency List
        /// Pass override settings = true to force to load mod defined dependencies and ignore user settings
        /// </summary>
        /// <param name="overrideSettings"></param>
        private void CreateDependencyItems(bool overrideSettings = false)
        {
            if (modJson != null)
            {
                var allDeps = modJson.GetModDependencyList(overrideSettings)?.ToList();
                foreach (var modid in modJson.GetModFlagList(overrideSettings))
                {
                    /* This mod */
                    if (modid == modJson.id)
                    {
                        DepItems.Add(new DependencyItemViewModel(modJson.id, this));
                    }
                    else
                    {
                        var dep = modJson.GetDependency(modid, overrideSettings);
                        if (dep != null)
                        {
                            if (allDeps != null && allDeps.IndexOf(dep) != -1)
                            {
                                allDeps.Remove(dep);
                            }
                            DepItems.Add(new DependencyItemViewModel(dep, this));
                        }
                    }
                }
                if (allDeps != null)
                {
                    /* Only the ones outside modflag should be left, what should be a FSO build*/
                    foreach (var dep in allDeps)
                    {
                        DepItems.Add(new DependencyItemViewModel(dep, this));
                    }
                }
            }
        }

        /* External Commands */
        /// <summary>
        /// Dependency Item Control: move dep one place up
        /// </summary>
        /// <param name="item"></param>
        public void DepUP(DependencyItemViewModel item)
        {
            int index = DepItems.IndexOf(item);
            if(index > 0)
            {
                DepItems.Move(index, index-1);
            }
        }

        /// <summary>
        /// Dependency Item Control: move dep one place down
        /// </summary>
        /// <param name="item"></param>
        public void DepDOW(DependencyItemViewModel item)
        {
            int index = DepItems.IndexOf(item);
            if (index < DepItems.Count-1)
            {
                DepItems.Move(index, index + 1);
            }
        }

        /// <summary>
        /// Dependency Item Control: delete dependecy
        /// </summary>
        /// <param name="item"></param>
        public void DepDEL(DependencyItemViewModel item)
        {
            DepItems.Remove(item);
        }


        /* UI */
        internal void SaveSettingsCommand()
        {
            if(modJson!= null)
            {
                BuildMissingWarning = string.Empty;
                //Dependencies
                if (CustomDependencies)
                {
                    modJson.modSettings.customDependencies = new List<ModDependency>();
                    modJson.modSettings.customModFlags = new List<string>();

                    foreach (var item in DepItems)
                    {
                        var dep = item.GetModDependency();

                        if(dep != null)
                        {
                            if (dep.id != modJson.id)
                            {
                                modJson.modSettings.customDependencies.Add(dep);
                            }

                            /* Check if the ID is a FSO build, if not add to modflag */
                            var isBuild = Knossos.GetInstalledBuildsList(dep.id);
                            if(isBuild.Count==0)
                            {
                                if(modJson.modSettings.customModFlags.IndexOf(dep.id) == -1)
                                    modJson.modSettings.customModFlags.Add(dep.id);
                            }
                        }
                    }
                }
                else
                {
                    modJson.modSettings.customDependencies = null;
                    modJson.modSettings.customModFlags = null;
                }

                //FSO Build
                var customBuild = FsoPicker.GetSelectedFsoBuild();
                if (customBuild != null)
                {
                    modJson.modSettings.customBuildId = customBuild.id;
                    modJson.modSettings.customBuildVersion = customBuild.version;
                    modJson.modSettings.customBuildExec = customBuild.directExec;
                }
                else
                {
                    modJson.modSettings.customBuildId = null;
                    modJson.modSettings.customBuildVersion = null;
                    modJson.modSettings.customBuildExec = null;
                }

                //FSO Flags
                if(FsoFlags != null)
                {

                    if(FsoFlags.GetCmdLine() == modJson.GetModCmdLine(true))
                    {
                        modJson.modSettings.customCmdLine = null;
                    }
                    else
                    {
                        modJson.modSettings.customCmdLine = FsoFlags.GetCmdLine();
                    }
                }

                modJson.modSettings.ignoreGlobalCmd = IgnoreGlobalCmd;
                modJson.modSettings.noIngameOptions = NoIngameOptions;

                modJson.modSettings.Save();
                if(modCardViewModel != null)
                {
                    modCardViewModel.CheckDependencyActiveVersion();
                    modCardViewModel.RefreshSpecialIcons();
                }
            }
        }

        internal void ConfigureBuildCommand()
        {
            ConfigureBuild(false);
        }

        /// <summary>
        /// Display Fso Build Flag list
        /// Also allows to edit the mod cmdline
        /// </summary>
        /// <param name="ignoreUserSettings"></param>
        private void ConfigureBuild(bool ignoreUserSettings)
        {
            if (modJson == null)
                return;
            ConfigureBuildOpen = true;
            FsoBuild? fsoBuild = FsoPicker.GetSelectedFsoBuild();

            /* If set to "mod select" pick the one from the current dependency list displayed in this dialog */
            if (fsoBuild == null)
            {
                foreach (var item in DepItems)
                {
                    var dep = item.GetModDependency();

                    if (dep != null && Knossos.GetInstalledBuildsList(dep.id).Any())
                    {
                        fsoBuild = dep.SelectBuild();
                        break;
                    }
                }
            }

            if(fsoBuild != null)
            {
                var flagsV1=fsoBuild.GetFlagsV1();
                if(flagsV1 != null)
                {
                    FsoFlags = new FsoFlagsViewModel(flagsV1,modJson.GetModCmdLine(ignoreUserSettings));
                }
                else
                {
                    Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await MessageBox.Show(MainWindow.instance!, "Unable to get flag data from build " + fsoBuild + " It might be below the minimal version supported (3.8.1) or some other error ocurred.", "Invalid flag data", MessageBox.MessageBoxButtons.OK);
                    });
                    
                }
            }
            else
            {
                /* No valid build found, send message */
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await MessageBox.Show(MainWindow.instance!, "Unable to resolve FSO build dependency, download the correct one or manually select a FSO version. ", "Not engine build found", MessageBox.MessageBoxButtons.OK);
                });
            }
        }

        /// <summary>
        /// Reset settings to DEFAULT on UI
        /// </summary>
        internal void ResetSettingsCommand()
        {
            CustomDependencies = false;
            BuildMissingWarning = string.Empty;
            FsoPicker = new FsoBuildPickerViewModel(null);
            FsoFlags = null;
            ConfigureBuild(true);
            DepItems.Clear();
            CreateDependencyItems(true);
            IgnoreGlobalCmd = false;
        }

        /// <summary>
        /// Toggle custom dependecies on/off
        /// </summary>
        internal void CustomDependenciesClick()
        {

            if (CustomDependencies)
            {
                foreach(var items in DepItems)
                {
                    items.SetReadOnly(false);
                }
            }
            else
            {
                DepItems.Clear();
                CreateDependencyItems(true);
            }
        }

        /// <summary>
        /// Add new dependency to list
        /// Custom dependecy should be ON at this point
        /// </summary>
        internal void AddDependencyCommand()
        {
            DepItems.Add(new DependencyItemViewModel(this));
        }

        /// <summary>
        /// Start task to Compress Mod, does all standard checks, mod setting save is done on the task method
        /// Mod cant be Dev Mode
        /// Warns if FSO version that is resolved via user or mod dependencies is below the minimum needed to run compressed files (23.2.0) 
        /// </summary>
        internal async void CompressCommand()
        {
            var cancel = false;
            if(modJson!.devMode)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var result = await MessageBox.Show(MainWindow.instance!, "This mod is in devmode, do not compress a mod that have not been uploaded to nebula yet! This increases upload size and disk space on nebula. It is ok if you compress old versions that are already uploaded.", "Mod is in devmode", MessageBox.MessageBoxButtons.ContinueCancel);
                    if (result != MessageBox.MessageBoxResult.Continue)
                        cancel = true;
                });
            }

            if (cancel)
                return;

            FsoBuild? fsoBuild = FsoPicker.GetSelectedFsoBuild();
            if (fsoBuild == null)
            {
                foreach (var item in DepItems)
                {
                    var dep = item.GetModDependency();

                    if (dep != null && Knossos.GetInstalledBuildsList(dep.id).Any())
                    {
                        fsoBuild = dep.SelectBuild();
                        break;
                    }
                }
            }

            if (fsoBuild != null && SemanticVersion.Compare(fsoBuild.version,VPCompression.MinimumFSOVersion) < 0)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var result = await MessageBox.Show(MainWindow.instance!, "This mod currently resolves to FSO build: "+fsoBuild.version+" the minimum to fully support all features is: "+VPCompression.MinimumFSOVersion + ".\n 23.0.0 may work if the mod do not have loose files, older versions are not going to work.", "FSO Version below minimum", MessageBox.MessageBoxButtons.ContinueCancel);
                    if (result != MessageBox.MessageBoxResult.Continue)
                        cancel = true;
                });
            }

            if (cancel)
                return;

            if (modJson != null && modJson.fullPath != string.Empty)
            {
                await TaskViewModel.Instance?.CompressMod(modJson)!;
                if (modJson.modSettings.isCompressed)
                {
                    CompressionAvailable = false;
                    Compressed = true;
                }
                await Task.Factory.StartNew(() => UpdateModSize());
            }
        }

        /// <summary>
        /// Create the task to decompress the mod, mod setting save is done on the task method
        /// </summary>
        internal async void DecompressCommand()
        {
            if (modJson != null && modJson.fullPath != string.Empty)
            {
                await TaskViewModel.Instance?.DecompressMod(modJson)!;
                if (!modJson.modSettings.isCompressed)
                {
                    CompressionAvailable = true;
                    Compressed = false;
                }
                await Task.Factory.StartNew(() => UpdateModSize());
            }
        }

        internal void DeleteIngameOptions()
        {
            try
            {
                if (IngameOptionsFileExist)
                {
                    File.Delete(Path.Combine(KnUtils.GetFSODataFolderPath(), "data", modJson!.id + "_settings.ini"));
                }
                CheckIfOptionsFileExist();
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "ModSettingsViewModel.DeleteIngameOptions()", ex);
            }
        }

        internal void ViewIngameOptions()
        {
            if (File.Exists(Path.Combine(KnUtils.GetFSODataFolderPath(), "data", modJson!.id + "_settings.ini")))
            {
                try
                {
                    var cmd = new Process();
                    cmd.StartInfo.FileName = Path.Combine(KnUtils.GetFSODataFolderPath(), "data", modJson!.id + "_settings.ini");
                    cmd.StartInfo.UseShellExecute = true;
                    cmd.Start();
                    cmd.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "MainWindowViewModel.ViewIngameOptions", ex);
                }
            }
        }

        private void CheckIfOptionsFileExist()
        {
            try
            {
                IngameOptionsFileExist = File.Exists(Path.Combine(KnUtils.GetFSODataFolderPath(), "data", modJson!.id + "_settings.ini"));
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "ModSettingsViewModel.CheckIfOptionsFileExist()", ex);
            }
        }
    }
}
