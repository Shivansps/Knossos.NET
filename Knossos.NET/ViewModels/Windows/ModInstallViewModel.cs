using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    /// <summary>
    /// View Model for the Mod Install Window
    /// </summary>
    public partial class ModInstallViewModel : ViewModelBase
    {
        private KnossosWindow? dialogWindow;

        [ObservableProperty]
        internal List<Mod> modVersions = new List<Mod>();
        [ObservableProperty]
        internal string title = string.Empty;
        [ObservableProperty]
        internal string version = string.Empty;
        [ObservableProperty]
        internal string releaseDate = string.Empty;
        [ObservableProperty]
        internal string name = string.Empty;
        [ObservableProperty]
        internal int selectedIndex = -1;
        [ObservableProperty]
        internal bool dataLoaded = false;
        [ObservableProperty]
        internal ObservableCollection<Mod> modInstallList = new ObservableCollection<Mod>();
        [ObservableProperty]
        internal bool isInstalled = false;
        [ObservableProperty]
        internal bool compress = false;
        [ObservableProperty]
        internal bool compressVisible = Knossos.globalSettings.modCompression == CompressionSettings.Manual? true : false;
        [ObservableProperty]
        internal bool canSelectDevMode = true;
        [ObservableProperty]
        internal bool installInDevMode = false;
        [ObservableProperty]
        internal bool hasWriteAccess = false;
        [ObservableProperty]
        internal bool forceDevMode = false;
        [ObservableProperty]
        internal string installSize = string.Empty;
        [ObservableProperty]
        internal string freeSpace = string.Empty;
        [ObservableProperty]
        internal bool cleanupVisible = false;
        [ObservableProperty]
        internal bool cleanupEnabled = true;
        [ObservableProperty]
        internal bool isMetaUpdate = false;
        [ObservableProperty]
        internal bool cleanInstall = false;
        [ObservableProperty]
        internal bool allowHardlink = true;
        private List<Mod> modCache = new List<Mod>();


        internal Mod? selectedMod;
        internal Mod? SelectedMod
        {
            get { return selectedMod; }
            set
            {
                if (selectedMod != value)
                {
                    this.SetProperty(ref selectedMod, value);
                    UpdateSelectedVersion();
                }
            }
        }

        public ModInstallViewModel() 
        {
        }

        /// <summary>
        /// Mod Install View Model
        /// Dialog is the window this datacontext is attached to, used to close the window when user clicks "install"
        /// </summary>
        /// <param name="modJson"></param>
        /// <param name="dialog"></param>
        /// <param name="preSelectedVersion"></param>
        /// <param name="forceDevModeOn"></param>
        public ModInstallViewModel(Mod modJson, KnossosWindow dialog, string? preSelectedVersion = null, bool forceDevModeOn = false)
        {
            dialogWindow = dialog;
            Title = "Installing: " + modJson.title;
            CanSelectDevMode = !forceDevModeOn;
            InstallInDevMode = forceDevModeOn;
            forceDevMode = forceDevModeOn;
            InitialLoad(modJson.id, preSelectedVersion);
        }

        /// <summary>
        /// Initial Load, if preselected version is pass, that would be the displayed version when the UI is show
        /// </summary>
        /// <param name="id"></param>
        /// <param name="preSelectedVersion"></param>
        private async void InitialLoad(string id, string? preSelectedVersion = null)
        {
            if (Nebula.userIsLoggedIn && !Knossos.inSingleTCMode || Nebula.userIsLoggedIn && Knossos.inSingleTCMode && CustomLauncher.MenuDisplayNebulaLoginEntry)
            {
                var ids = await Nebula.GetEditableModIDs();
                if (ids != null && ids.Any() && ids.FirstOrDefault(x => x == id) != null)
                {
                    HasWriteAccess = true;
                    InstallInDevMode = true;
                }
            }
            ModVersions = await Nebula.GetAllModsWithID(id);
            if(ModVersions.Any())
            {
                ModVersions.Sort(Mod.CompareVersion);
                if (preSelectedVersion == null)
                {
                    SelectedIndex = ModVersions.Count() - 1;
                }
                else
                {
                    var preSelect = ModVersions.FirstOrDefault(m=>m.version == preSelectedVersion);
                    if (preSelect != null)
                    {
                        SelectedIndex = ModVersions.IndexOf(preSelect);
                    }
                    else
                    {
                        /* Version not found? maybe deleted from nebula Select the newerest */
                        SelectedIndex = ModVersions.Count() - 1;
                    }
                }
            }
            else
            {
                Log.Add(Log.LogSeverity.Warning, "ModInstallViewModel.InitialLoad()", "Unable to find this mod id: "+ id +" on repo.json");
                await MessageBox.Show(MainWindow.instance!,"Unable to find this mod ID on nebula repo, maybe the mod was removed from nebula.","Unable to find mod on repo",MessageBox.MessageBoxButtons.OK);
            }
        }

        /// <summary>
        /// Load to UI all mod packages (and dependencies) for the current selected mod version
        /// </summary>
        private async void UpdateSelectedVersion()
        {
            ModInstallList.Clear();
            IsMetaUpdate = false;
            DataLoaded = false;
            CleanupVisible = false;
            Compress = false;
            List <Mod> allMods;
            if(SelectedMod != null)
            {
                var successfull = await SelectedMod.LoadFulLNebulaData();
                if (!successfull)
                    _ = MessageBox.Show(MainWindow.instance, "An error has ocurred while fetching data from nebula for mod " + SelectedMod, "Error fetching data", MessageBox.MessageBoxButtons.OK);
            }
            //load all dependencies mods in singletc mode so they can be modified too at any time
            if(SelectedMod != null && !SelectedMod.GetMissingDependenciesList().Any() && !Knossos.inSingleTCMode)
            {
                allMods = ModVersions.ToList();
            }
            else
            {
                allMods = await Nebula.GetAllModsWithID(null);
            }
            if (SelectedMod != null && allMods.Any())
            {
                var installed=Knossos.GetInstalledMod(SelectedMod.id,SelectedMod.version);
                if(installed != null)
                {
                    /* If its installed just mark installed packages as required, and optional the non installed ones */
                    IsInstalled = true;
                    SelectedMod.fullPath = installed.fullPath;
                    foreach (var displayPkg in SelectedMod.packages)
                    {
                        var installedPkg = installed.packages.FirstOrDefault(ip => ip.name == displayPkg.name);
                        if(installedPkg != null)
                        {
                            //pkg is installed
                            if (displayPkg.status == "required")
                            {
                                displayPkg.isEnabled = false;
                            }
                            else
                            {
                                displayPkg.isEnabled = true;
                            }
                            displayPkg.isSelected = true;
                        }
                        else
                        {
                            //pkg not installed
                            if(displayPkg.status == "required")
                            {
                                //rare case
                                displayPkg.isEnabled = false;
                                displayPkg.isSelected = true;
                            }
                            else
                            {
                                displayPkg.isEnabled = true;
                                displayPkg.isSelected = false;
                            }
                        }
                    }
                    SelectedMod.installed = true;
                    InstallInDevMode = installed.devMode;
                    CanSelectDevMode = false;
                    IsMetaUpdate = Mod.IsMetaUpdate(SelectedMod,installed);
                }
                else
                {
                    //If this mod-version is not installed, do we have any other version of it installed?
                    var otherVersionInstalled = Knossos.GetInstalledModList(SelectedMod.id)?.MaxBy(m => new SemanticVersion(m.version));
   
                    foreach(var pkg in SelectedMod.packages)
                    {
                        switch (pkg.status)
                        {
                            case "required":
                                pkg.isEnabled = false;
                                pkg.isSelected = true;
                                break;
                            case "recommended":
                                pkg.isEnabled = true;
                                pkg.isSelected = true;
                                break;
                            case "optional":
                                pkg.isEnabled = true;
                                pkg.isSelected = false;
                                break;
                        }

                        //Copy pkg selection from the installed version if we can
                        if (otherVersionInstalled != null && pkg.status != "required")
                        {
                            var pkgExist = otherVersionInstalled.packages.FirstOrDefault(p=>p.name == pkg.name);
                            pkg.tooltip = "\n\nNote: Selection status was copied from installed version: " + otherVersionInstalled.version;
                            if (pkgExist != null)
                            {
                                pkg.isSelected = true;
                            }
                            else
                            {
                                pkg.isSelected = false;
                                //Check if this is a new mod pkg
                                if(pkg.status == "recommended")
                                {
                                    var inCache = modCache.FirstOrDefault(mc => mc.id == otherVersionInstalled.id && mc.version == otherVersionInstalled.version);
                                    if(inCache == null)
                                    {
                                        inCache = await Nebula.GetModData(otherVersionInstalled.id, otherVersionInstalled.version);
                                        if(inCache != null )
                                        {
                                            modCache.Add(inCache);
                                        }
                                    }
                                    if(inCache != null)
                                    {
                                        var isNewPkg = inCache.packages.FirstOrDefault(p => p.name == pkg.name);
                                        if(isNewPkg == null)
                                        {
                                            //pkg did not existed in the version we are comparing to
                                            pkg.isSelected = true;
                                            pkg.tooltip = "\n\nNote: This is a newly added mod pkg.";
                                        }
                                    }
                                }
                            }
                            

                        }
                    }

                    IsInstalled = false;

                    if(ForceDevMode)
                    {
                        CanSelectDevMode = false;
                        InstallInDevMode = true;
                    }
                    else
                    {
                        CanSelectDevMode = true;
                        InstallInDevMode = HasWriteAccess;
                    }
                }
                SelectedMod.isEnabled=false;
                SelectedMod.isSelected = true;
                Compress=SelectedMod.modSettings.isCompressed;
                Name = SelectedMod.title;
                Version = SelectedMod.version;
                if (SelectedMod.lastUpdate != null){                
                    ReleaseDate = SelectedMod.lastUpdate;
                } else {
                    ReleaseDate = "Was not listed in repository";
                }
                await ProcessMod(SelectedMod,allMods);
            }
            if (!IsInstalled && SelectedMod != null && ModVersions.Count > 1 && ModVersions.IndexOf(SelectedMod) > 0)
            {
                CleanupVisible = true;
            }
            DataLoaded = true;
            allMods.Clear();
            GC.Collect();
        }

        /// <summary>
        /// Add a mod to the install list/tree
        /// Loads full data from nebula if not already loaded for the relevant mods
        /// Detects any missing dependency of this mod and add them to install too
        /// Then it recursively calls this function again on all dependencies
        /// Making a full mod tree dependency resolution.
        /// </summary>
        /// <param name="mod"></param>
        /// <param name="allMods"></param>
        /// <param name="processed"></param>
        private async Task ProcessMod(Mod mod, List<Mod> allMods, List<Mod>? processed = null)
        {
            if (processed == null)
                processed = new List<Mod>();
            //load all dependencies mods in singletc mode so they can be modified too at any time
            var dependencies = Knossos.inSingleTCMode ? mod.GetModDependencyList(false, true) : mod.GetMissingDependenciesList(false, true);
            //Display this mod on install list
            AddModToList(mod);
            //Add this mod here to avoid possible looping
            processed.Add(mod);
            if (dependencies != null)
            {
                foreach (var dep in dependencies)
                {
                    var modDep = await dep.SelectModNebula(allMods);
                    if (modDep != null)
                    {
                        //Is this dependecy mod already is installed?
                        var modInstalled = Knossos.GetInstalledMod(modDep.id, modDep.version);

                        //Check Cache
                        var modInCache = modCache.FirstOrDefault(x => x.id == modDep.id && x.version == modDep.version);
                        if (modInCache != null)
                        {
                            modDep = modInCache;
                        }
                        else
                        {
                            //Load Nebula data first to check the packages and add to cache
                            var successfull = await modDep.LoadFulLNebulaData();
                            if (!successfull)
                                _ = MessageBox.Show(MainWindow.instance, "An error has ocurred while fetching data from nebula for mod " + modDep, "Error fetching data", MessageBox.MessageBoxButtons.OK);
                            modCache.Add(modDep);
                        }

                        //If this is an engine build then check if contains valid executables
                        if (modDep.type == ModType.engine)
                        {
                            //Set a max amount of attempts to get an alternative version in case we need an alternative version
                            //This is because if user request "FSO" builds with an an incompatible cpu arch this is going to try
                            //with every FSO build in nebula that sastifies the dependency, incluiding nightlies.
                            var attempt = 0;
                            var maxAttempts = 10;
                            while (modDep != null && ++attempt < maxAttempts && modDep.packages.Any(x => FsoBuild.IsEnviromentStringValidInstall(x.environment)) == false)
                            {
                                //This build is not valid for this pc, delete from allmods list and resend to process
                                var remove = allMods.FirstOrDefault(x => x.id == modDep.id && x.version == modDep.version);
                                if (remove != null)
                                {
                                    allMods.Remove(remove);
                                    var alternativeVersion = await dep.SelectModNebula(allMods);
                                    if (alternativeVersion != null)
                                    {
                                        //Check Cache
                                        modInCache = modCache.FirstOrDefault(x => x.id == alternativeVersion.id && x.version == alternativeVersion.version);
                                        if (modInCache != null)
                                        {
                                            alternativeVersion = modInCache;
                                        }
                                        else
                                        {
                                            //Load Nebula data first to check the packages and add to cache
                                            var successfull = await alternativeVersion.LoadFulLNebulaData();
                                            if (!successfull)
                                                _ = MessageBox.Show(MainWindow.instance, "An error has ocurred while fetching data from nebula for mod " + alternativeVersion, "Error fetching data", MessageBox.MessageBoxButtons.OK);
                                            modCache.Add(alternativeVersion);
                                        }
                                    }
                                    modDep = alternativeVersion;
                                }
                                else
                                {
                                    //if for some reason we cant find modDep on allMods (it should never happen) we have to break or we are going to loop
                                    break;
                                }
                            }
                            //if we cant find a alternative version in nebula, we have to skip the rest.
                            if (modDep == null || attempt == maxAttempts)
                                continue;
                        }

                        //Make sure to mark all needed pkgs this mod need as required
                        modDep.isEnabled = true;
                        modDep.isSelected = true;

                        foreach (var pkg in modDep.packages)
                        {
                            if (dep != null && dep.packages != null)
                            {
                                //Auto select needed packages and inform via tooltip, updating the foreground
                                var depPkg = dep.packages.FirstOrDefault(dp => dp == pkg.name);
                                if (depPkg != null && pkg.status != "required")
                                {
                                    pkg.isEnabled = true;
                                    pkg.isSelected = true;
                                    pkg.isRequired = true;

                                    var originalPkg = mod.FindPackageWithDependency(dep.originalDependency);
                                    if (originalPkg != null)
                                    {
                                        if (!pkg.tooltip.Contains(mod + "\nPKG: " + originalPkg.name))
                                        {
                                            pkg.tooltip += "\n\nRequired by MOD: " + mod + "\nPKG: " + originalPkg.name;
                                        }
                                    }
                                    else
                                    {
                                        if (!pkg.tooltip.Contains(mod.ToString()))
                                        {
                                            pkg.tooltip += "\n\nRequired by MOD: " + mod;
                                        }
                                    }
                                }
                                else
                                {
                                    switch (pkg.status)
                                    {
                                        case "required":
                                            pkg.isEnabled = false;
                                            pkg.isSelected = true;
                                            break;
                                        case "recommended":
                                            pkg.isSelected = true;
                                            break;
                                        case "optional":
                                            //No need to do anything here
                                            break;
                                    }
                                }
                            }

                            //If mod is already installed, non-installed pkgs are all unselected
                            //and all installed ones are selected
                            if (modInstalled != null && modInstalled.packages != null)
                            {
                                var packageIsInstalled = modInstalled.packages.FirstOrDefault(m => m.name == pkg.name);
                                if (packageIsInstalled != null)
                                {
                                    //Pkg is installed
                                    if (pkg.status == "required")
                                    {
                                        pkg.isEnabled = false;
                                    }
                                    else
                                    {
                                        pkg.isEnabled = true;
                                    }
                                    pkg.isSelected = true;
                                }
                                else
                                {
                                    //Pkg is not installed
                                    //ONLY if the currently selected mod is also installed
                                    //For new mod or new mod version installs only if the package is not needed
                                    if (IsInstalled || !IsInstalled && !pkg.isRequired)
                                    {
                                        pkg.isEnabled = true;
                                        pkg.isSelected = false;
                                    }
                                }
                            }
                        }

                        //If process this depmod own dependencies if we havent done already
                        //Otherwise re-add it to the list to enabled any potential new pkg needed
                        if (processed.IndexOf(modDep) == -1)
                        {
                            await ProcessMod(modDep, allMods, processed);
                        }
                        else
                        {
                            AddModToList(modDep);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Add mod to list, it cheks if the mod is already added and if thats the case
        /// then it checks the packages of that mod to make sure recommended and requiered packages are properly marked
        /// </summary>
        /// <param name="mod"></param>
        private void AddModToList(Mod mod)
        {
            var modInList = ModInstallList.FirstOrDefault(m=>m.id == mod.id && m.version == mod.version);

            if (modInList == null)
            {
                /* This mod was not added to the install list yet */
                //Copy pkg notes to tooltip
                foreach (var pkg in mod.packages)
                {
                    if (!string.IsNullOrEmpty(pkg.notes) && !pkg.tooltip.Contains(pkg.notes))
                    {
                        pkg.tooltip = pkg.notes + pkg.tooltip;
                    }
                }
                ModInstallList.Add(mod);
            }
            else
            {
                foreach(var pkgInList in modInList.packages)
                {
                    /* If the mod is already added make sure all needed packages are properly marked */
                    var pkg = mod.packages.FirstOrDefault(mp => mp.name == pkgInList.name);
                    if(pkg != null)
                    {
                        if (pkgInList.isEnabled && !pkg.isEnabled || pkg.status!.ToLower() == "required")
                        {
                            pkgInList.isEnabled = false;
                            pkgInList.isSelected = true;
                        }
                        if(!pkgInList.isRequired && pkg.isRequired)
                        {
                            pkgInList.isRequired = true;
                        }
                        if (!pkgInList.tooltip.Contains(pkg.tooltip))
                        {
                            pkgInList.tooltip += pkg.tooltip;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Starts Mod Verify Task
        /// </summary>
        internal void VerifyCommand()
        {
            if(SelectedMod != null)
            {
                SelectedMod.modSettings.Load(SelectedMod.fullPath);
                if (SelectedMod.modSettings.isCompressed)
                {
                    MessageBox.Show(MainWindow.instance!, "This mod is compressed, mod verify is not available for compressed mods, uncompress it and try again.", "Verify error", MessageBox.MessageBoxButtons.OK);
                    return;
                }
                MessageBox.Show(MainWindow.instance!, "The verify process has started.", "Verify", MessageBox.MessageBoxButtons.OK);
                TaskViewModel.Instance!.VerifyMod(SelectedMod);
            }
        }

        /// <summary>
        /// Calls install mod on all mods in install list
        /// And closes the window
        /// </summary>
        internal void InstallMod()
        {
            if(Knossos.inSingleTCMode)
                TaskViewModel.Instance?.CleanCommand();
            foreach (var mod in ModInstallList)
            {
                var cleanOldVersions = false;
                if (mod == SelectedMod)
                {
                    mod.devMode = InstallInDevMode;
                    //Only for the mod we are installing, we need to check if the option is visible AND enabled
                    if(CleanupVisible && CleanupEnabled)
                        cleanOldVersions = true;
                }
                if (mod.isSelected)
                    TaskViewModel.Instance!.InstallMod(mod, null, Compress, cleanOldVersions, CleanInstall, AllowHardlink && !CleanInstall);
            }
            ModInstallList.Clear();

            if (dialogWindow != null)
            { 
                dialogWindow.Close(); 
            }      
        }

        internal async void UpdateSpace()
        {
            try
            {
                await Task.Delay(500);
                long size = 0;
                foreach (var mod in ModInstallList)
                {
                    if(mod.isSelected)
                    {
                        if (mod.packages != null)
                        {
                            Mod? installed = null;
                            if (mod.installed)
                            {
                                installed = Knossos.GetInstalledMod(mod.id, mod.version);
                            }
                            foreach (var pkg in mod.packages)
                            {
                                if (pkg.isSelected && pkg.files != null)
                                {
                                    if(installed != null)
                                    {
                                        if (installed.IsPackageInstalled(pkg.name))
                                            continue;
                                    }
                                    foreach (var file in pkg.files)
                                    {
                                        size += file.filesize;
                                    }
                                }
                            }
                        }
                    }
                }
                await Dispatcher.UIThread.InvokeAsync(() => {
                    InstallSize = "Download Size: " + KnUtils.FormatBytes(size) + " (*)";
                    if(Knossos.GetKnossosLibraryPath() != null)
                       FreeSpace = "Free space in library: " + KnUtils.FormatBytes(KnUtils.CheckDiskSpace(Knossos.GetKnossosLibraryPath()!));
                });
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "ModInstallViewModel.UpdateSpace()", ex);
            }
        }
    }
}
