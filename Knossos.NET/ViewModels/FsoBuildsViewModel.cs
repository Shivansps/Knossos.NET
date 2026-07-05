using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using Knossos.NET.Views;
using Knossos.NET.Views.Windows;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Threading;

namespace Knossos.NET.ViewModels
{
    /// <summary>
    /// Fso builds list view model
    /// </summary>
    public partial class FsoBuildsViewModel : ViewModelBase
    {
        public static FsoBuildsViewModel? Instance;
        private static readonly string nightlyDateLimit = DateTime.Today.AddMonths(-5).ToString("yyyy-MM-dd");
        private List<Mod> unloadedNightlies = new List<Mod>();
        [ObservableProperty]
        internal bool allNightliesLoaded = false;
        [ObservableProperty]
        internal ObservableCollection<FsoBuildItemViewModel> stableItems = new ObservableCollection<FsoBuildItemViewModel>();
        [ObservableProperty]
        internal ObservableCollection<FsoBuildItemViewModel> rcItems = new ObservableCollection<FsoBuildItemViewModel>();
        [ObservableProperty]
        internal ObservableCollection<FsoBuildItemViewModel> nightlyItems = new ObservableCollection<FsoBuildItemViewModel>();
        [ObservableProperty]
        internal ObservableCollection<FsoBuildItemViewModel> customItems = new ObservableCollection<FsoBuildItemViewModel>();

        public FsoBuildsViewModel()
        {
            Instance = this;
        }

        /// <summary>
        /// Clear ALL lists of FSO Builds in the UI
        /// </summary>
        public void ClearView()
        {
            StableItems.Clear();
            RcItems.Clear();
            NightlyItems.Clear();
            CustomItems.Clear();
        }

        /// <summary>
        /// External call to start download a FSO build
        /// This is equivalent of clicking the "download" button
        /// Used to start engine build installs when a build is referenced as a dependency of a mod
        /// </summary>
        /// <param name="mod"></param>
        public void RelayInstallBuild(Mod mod)
        {
            var buildStability = FsoBuild.GetFsoStability(mod.stability, mod.id);
            ObservableCollection<FsoBuildItemViewModel>? list = null;
            switch (buildStability)
            {
                case FsoStability.Stable: list = StableItems; break;
                case FsoStability.RC: list = RcItems; break;
                case FsoStability.Nightly: list = NightlyItems; break;
                case FsoStability.Custom: list = CustomItems; break;
            }

            if(list != null)
            {
                var uiItem = list.FirstOrDefault(b => b.CompareIdAndVersionToMod(mod) == true);
                if (uiItem != null)
                {
                    uiItem.DownloadBuildExternal(mod);
                }
                else
                {
                    //If we are searching for a nightly and it was not found, load them all
                    if(buildStability == FsoStability.Nightly && !AllNightliesLoaded)
                    {
                        LoadAllNightlies();
                        RelayInstallBuild(mod);
                        return;
                    }
                    Log.Add(Log.LogSeverity.Error, "FsoBuildsViewModel.RelayInstallBuild()", "Unable to find the build UI item with these parameters. Build install cancelled. Version: " + mod.version + " ID: " + mod.id);
                }
            }
            else
            {
                Log.Add(Log.LogSeverity.Error, "FsoBuildsViewModel.RelayInstallBuild()", "Unable to select the correct build list with these parameters. Build install cancelled. Stability: "+mod.stability + " ID: "+mod.id);
            }
        }

        /// <summary>
        /// This "updates" a FSO build info on UI, what it actually does is to delete the old version and insert the new one
        /// Used to update data on sser devmode builds
        /// </summary>
        /// <param name="build"></param>
        public void UpdateBuildUI(FsoBuild build)
        {
            switch (build.stability)
            {
                case FsoStability.Stable:
                    var previous = StableItems.FirstOrDefault(b => b.build != null && b.build.id == build.id && b.build.version == build.version);
                    if(previous != null)
                        StableItems.Remove(previous);
                    break;
                case FsoStability.RC:
                    var previousRc = RcItems.FirstOrDefault(b => b.build != null && b.build.id == build.id && b.build.version == build.version);
                    if(previousRc != null)
                        RcItems.Remove(previousRc);
                    break;
                case FsoStability.Nightly:
                    var previousNL = NightlyItems.FirstOrDefault(b => b.build != null && b.build.id == build.id && b.build.version == build.version);
                    if(previousNL != null)
                        NightlyItems.Remove(previousNL);
                    break;
                case FsoStability.Custom:
                    var previousCu = CustomItems.FirstOrDefault(b => b.build != null && b.build.id == build.id && b.build.version == build.version);
                    if(previousCu != null)
                        CustomItems.Remove(previousCu);
                    break;
            }
            AddBuildToUi(build);
        }

        /// <summary>
        /// Add a new FSO build to UI, depending on stability
        /// It calls ClearUnusedData() if not devmode or private
        /// </summary>
        /// <param name="build"></param>
        public void AddBuildToUi(FsoBuild build)
        {
            switch(build.stability)
            {
                case FsoStability.Stable:
                    var previous = StableItems.FirstOrDefault(b=> string.Compare(b.Date,build.date) < 0);
                    if (previous != null)
                    {
                        int index = StableItems.IndexOf(previous);
                        if (index == -1)
                            index = 0;
                        StableItems.Insert(index, new FsoBuildItemViewModel(build));
                    }
                    else
                    {
                        StableItems.Add(new FsoBuildItemViewModel(build));
                    }
                    break;
                case FsoStability.RC:
                    var previousRc = RcItems.FirstOrDefault(b => string.Compare(b.Date, build.date) < 0);
                    if (previousRc != null)
                    {
                        int index = RcItems.IndexOf(previousRc);
                        if (index == -1)
                            index = 0;
                        RcItems.Insert(index, new FsoBuildItemViewModel(build));
                    }
                    else
                    {
                        RcItems.Add(new FsoBuildItemViewModel(build));
                    }
                    break;
                case FsoStability.Nightly:
                    var previousNL = NightlyItems.FirstOrDefault(b => string.Compare(b.Date, build.date) < 0);
                    if (previousNL != null)
                    {
                        int index = NightlyItems.IndexOf(previousNL);
                        if (index == -1)
                            index = 0;
                        NightlyItems.Insert(index, new FsoBuildItemViewModel(build));
                    }
                    else
                    {
                        NightlyItems.Add(new FsoBuildItemViewModel(build));
                    }
                    break;
                case FsoStability.Custom:
                    var previousCu = CustomItems.FirstOrDefault(b => string.Compare(b.Date, build.date) < 0);
                    if (previousCu != null)
                    {
                        int index = CustomItems.IndexOf(previousCu);
                        if (index == -1)
                            index = 0;
                        CustomItems.Insert(index, new FsoBuildItemViewModel(build));
                    }
                    else
                    {
                        CustomItems.Add(new FsoBuildItemViewModel(build));
                    }
                    break;
            }
            if (build.modData != null && !build.modData.isPrivate && !build.devMode)
            {
                build.modData!.ClearUnusedData();
            }
        }

        /// <summary>
        /// Load an lists of FSO builds into ui in a more efficient way that doing it one by one
        /// It deletes all displayed elements, except for fso builds that are installed locally
        /// </summary>
        /// <param name="modsJson"></param>
        public void BulkLoadNebulaBuilds(List<Mod> modsJson)
        {
            var newStable = new ObservableCollection<FsoBuildItemViewModel>();
            var newRc = new ObservableCollection<FsoBuildItemViewModel>();
            var newNightly = new ObservableCollection<FsoBuildItemViewModel>();
            var newCustom = new ObservableCollection<FsoBuildItemViewModel>();
            StableItems.ForEach(s => { if (s.IsInstalled) newStable.Add(s); });
            RcItems.ForEach(s => { if (s.IsInstalled) newRc.Add(s); });
            NightlyItems.ForEach(s => { if (s.IsInstalled) newNightly.Add(s); });
            CustomItems.ForEach(s => { if (s.IsInstalled) newCustom.Add(s); });

            AllNightliesLoaded = false;
            unloadedNightlies.Clear();

            foreach (var mod in modsJson)
            {
                var build = new FsoBuild(mod);
                switch (build.stability)
                {
                    case FsoStability.Stable:
                        //do not add this un-usable build in db
                        if (build.id == "FSO" && build.version == "3.8.0-2")
                             continue;
                        var previous = newStable.FirstOrDefault(b => string.Compare(b.Date, build.date) < 0);
                        if (previous != null)
                        {
                            int index = newStable.IndexOf(previous);
                            if (index == -1)
                                index = 0;
                            newStable.Insert(index, new FsoBuildItemViewModel(build));
                        }
                        else
                        {
                            newStable.Add(new FsoBuildItemViewModel(build));
                        }
                        break;
                    case FsoStability.RC:
                        var previousRc = newRc.FirstOrDefault(b => string.Compare(b.Date, build.date) < 0);
                        if (previousRc != null)
                        {
                            int index = newRc.IndexOf(previousRc);
                            if (index == -1)
                                index = 0;
                            newRc.Insert(index, new FsoBuildItemViewModel(build));
                        }
                        else
                        {
                            newRc.Add(new FsoBuildItemViewModel(build));
                        }
                        break;
                    case FsoStability.Nightly:
                        if (AllNightliesLoaded || !AllNightliesLoaded && string.Compare(nightlyDateLimit, build.date) < 0)
                        {
                            var previousNL = newNightly.FirstOrDefault(b => string.Compare(b.Date, mod.lastUpdate) < 0);
                            if (previousNL != null)
                            {
                                int index = newNightly.IndexOf(previousNL);
                                if (index == -1)
                                    index = 0;
                                newNightly.Insert(index, new FsoBuildItemViewModel(new FsoBuild(mod)));
                            }
                            else
                            {
                                newNightly.Add(new FsoBuildItemViewModel(new FsoBuild(mod)));
                            }
                        }
                        else
                        {
                            unloadedNightlies.Add(mod);
                        }
                        break;
                    case FsoStability.Custom:
                        var previousCu = newCustom.FirstOrDefault(b => string.Compare(b.Date, build.date) < 0);
                        if (previousCu != null)
                        {
                            int index = newCustom.IndexOf(previousCu);
                            if (index == -1)
                                index = 0;
                            newCustom.Insert(index, new FsoBuildItemViewModel(build));
                        }
                        else
                        {
                            newCustom.Add(new FsoBuildItemViewModel(build));
                        }
                        break;
                }
                if (!mod.isPrivate && !mod.devMode)
                {
                    mod.ClearUnusedData();
                }
            }

            StableItems = newStable;
            RcItems = newRc;
            NightlyItems = newNightly;
            CustomItems = newCustom;
        }

        /// <summary>
        /// Completely deletes an FSO Build, from UI, from Knossos internal list and also clears physical files
        /// It optionally runs mod status checks, default on
        /// </summary>
        /// <param name="build"></param>
        /// <param name="item"></param>
        public void DeleteBuild(FsoBuild build, FsoBuildItemViewModel? item, bool runModstatusChecks = true)
        {
            try
            {
                Directory.Delete(build.folderPath,true);
                if(item != null) 
                { 
                    if(!StableItems.Remove(item))
                    {
                        if (!RcItems.Remove(item))
                        {
                            if (!NightlyItems.Remove(item))
                            {
                                CustomItems.Remove(item);
                            }
                        }
                    }
                }
                Knossos.RemoveBuild(build);
                DeveloperModsViewModel.Instance?.UpdateListedFsoBuildVersionsInEditor();
                if (runModstatusChecks)
                {
                    MainViewModel.Instance?.RunModStatusChecks();
                }
            }
            catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "FsoBuildsViewModel.DeleteBuild()",ex);
            }
        }

        /// <summary>
        /// Opens view to add custom user fso build
        /// </summary>
        internal async void CommandAddUserBuild()
        {
            var dialog = new AddUserBuildView();
            dialog.DataContext = new AddUserBuildViewModel(dialog);

            await dialog.ShowDialog<AddUserBuildView?>(MainWindow.instance);
        }

        /// <summary>
        /// Loads all nightlies into view that arent loaded due to being too old
        /// </summary>
        internal void LoadAllNightlies()
        {
            if (!AllNightliesLoaded)
            {
                AllNightliesLoaded = true;
                foreach (var item in unloadedNightlies)
                {
                    AddBuildToUi(new FsoBuild(item));
                }
                GC.Collect();
            }
        }

        /// <summary>
        /// Delete all installed Nightlies and re add their UI items
        /// </summary>
        internal void CleanNightlies()
        {
            //Get all nightlies
            var installedNL = Knossos.GetInstalledBuildsList("FSO", FsoStability.Nightly);
            if(installedNL != null && installedNL.Any())
            {
                var fsoVersionString = "";
                //dump all version info to a string
                installedNL.ForEach( x => fsoVersionString += "\n" + x.ToString() );
                Dispatcher.UIThread.Invoke(async() =>
                {
                    var reply = await MessageBox.Show(MainWindow.instance!, "You are about to delete the following FSO builds:" + fsoVersionString, "Deleting nightlies", MessageBox.MessageBoxButtons.ContinueCancel);
                    if(reply == MessageBox.MessageBoxResult.Continue)
                    {
                        foreach (var build in installedNL)
                        {
                            Log.Add(Log.LogSeverity.Information, "FsoBuildItemViewModel.DeleteBuildCommand()", "Deleting FSO build " + build.ToString());
                            //Find their UI item
                            var uiItem = NightlyItems.FirstOrDefault(i => i.build == build);
                            TaskViewModel.Instance!.AddMessageTask("Deleting:" + build);
                            //Delete build
                            DeleteBuild(build, uiItem, false);
                            if (uiItem != null)
                            {
                                //Re add the item to the UI
                                var result = await Nebula.GetModData(build.id, build.version).ConfigureAwait(false);
                                if(result != null)
                                {
                                    Dispatcher.UIThread.Invoke(() =>
                                    {
                                        AddBuildToUi(new FsoBuild(result));
                                    });
                                }
                            }
                        }
                    }
                    MainViewModel.Instance?.RunModStatusChecks();
                });
            }
            else
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    MessageBox.Show(MainWindow.instance!, "There are not installed FSO nightlies to delete", "Error deleting nightlies", MessageBox.MessageBoxButtons.OK);
                });
            }
        }
    }
}
