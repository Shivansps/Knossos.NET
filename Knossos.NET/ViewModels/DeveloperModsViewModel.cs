using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.Views;
using Knossos.NET.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using System.Globalization;
using System.Collections.Generic;

namespace Knossos.NET.ViewModels
{
    /// <summary>
    /// Develop Tab View Model
    /// </summary>
    public partial class DeveloperModsViewModel : ViewModelBase
    {
        private enum SortType
        {
            Default,
            Name,
            Mod_Type,
            Update_Date
        }

        public int sortSelectedIndex = 0;
        internal int SortSelectedIndex
        {
            get => sortSelectedIndex;
            set
            {
                if (value != sortSelectedIndex)
                {
                    this.SetProperty(ref sortSelectedIndex, value);
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        try
                        {
                            CloseEditor();
                            List<Mod>? tempList = null;
                            switch (value)
                            {
                                case (int)SortType.Default: tempList = Mods.OrderBy(x => x.folderName).ToList(); break;
                                case (int)SortType.Name: tempList = Mods.OrderBy(x => x.title).ToList(); break;
                                case (int)SortType.Update_Date: tempList = Mods.OrderByDescending(x => x.lastUpdate != null ? DateTime.Parse(x.lastUpdate, CultureInfo.InvariantCulture) : DateTime.MinValue)
                                                                .ThenBy(x => x.title).ToList(); break;
                                case (int)SortType.Mod_Type: tempList = Mods.OrderBy(x => x.type.ToString()).ThenBy(x => x.title).ToList(); break;
                            }
                            if (tempList != null)
                            {
                                for (int i = 0; i < tempList.Count(); i++)
                                {
                                    Mods.Move(Mods.IndexOf(tempList[i]), i);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Add(Log.LogSeverity.Error, "DevelopersModsViewModel.ChangeSort()", ex);
                        }
                    }, DispatcherPriority.Background);
                    Knossos.globalSettings.devModSort = value;
                    Knossos.globalSettings.Save(false);
                }
            }
        }

        [ObservableProperty]
        public ObservableCollection<ComboBoxItem> sortBoxItems = new ObservableCollection<ComboBoxItem>();

        public static DeveloperModsViewModel? Instance;
        [ObservableProperty]
        internal ObservableCollection<Mod> mods = new ObservableCollection<Mod>();
        [ObservableProperty]
        internal DevModEditorViewModel? modEditor;
        [ObservableProperty]
        internal NebulaLoginViewModel nebulaLoginVM = new NebulaLoginViewModel();
        [ObservableProperty]
        internal DevToolManagerViewModel devToolManager = new DevToolManagerViewModel();
        [ObservableProperty]
        public string latestStable = string.Empty;
        [ObservableProperty]
        public string latestNightly = string.Empty;

        [ObservableProperty]
        public bool nightlyInstalled = false;
        [ObservableProperty]
        public bool stableInstalled = false;

        internal int tabIndex = 0;
        internal int TabIndex
        {
            get => tabIndex;
            set
            {
                if (value != tabIndex)
                {
                    this.SetProperty(ref tabIndex, value);
                    if (tabIndex == 0) //MODS
                    {
                    }
                    if (tabIndex == 1) //Tools
                    {
                        DevToolManager.LoadTools();
                    }
                    if (tabIndex == 2) //Nebula Login
                    {
                        NebulaLoginVM.UpdateUI();
                    }
                }
            }
        }

        internal int selectedIndex = -1;
        internal int SelectedIndex
        {
            get
            {
                return selectedIndex;
            }
            set
            {
                if (selectedIndex != value || ModEditor == null)
                {
                    if (value >= 0)
                        this.SetProperty(ref selectedIndex, value);
                    else
                        selectedIndex = 0;

                    if (Mods.Count > selectedIndex)
                    {
                        if(ModEditor == null)
                            ModEditor = new DevModEditorViewModel();
                        ModEditor.StartModEditor(Mods[selectedIndex]);
                    }
                }
            }
        }

        /// <summary>
        /// Fill the mod sorting combobox
        /// </summary>
        private void FillSortBox()
        {
            foreach(var e in Enum.GetValues(typeof(SortType)))
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    var cmb = new ComboBoxItem();
                    cmb.Tag = e;
                    cmb.Content = e.ToString()?.Replace("_"," ");
                    SortBoxItems.Add(cmb);
                }, DispatcherPriority.Background);
            }
        }

        public DeveloperModsViewModel()
        {
            Instance = this;
            FillSortBox();
        }

        /// <summary>
        /// On entering devmodetab check the sorting is the last selected one
        /// </summary>
        public void MaybeChangeSorting()
        {
            if (TabIndex == 0 && Knossos.globalSettings.devModSort != SortSelectedIndex)
            {
                SortSelectedIndex = Knossos.globalSettings.devModSort;
            }
        }

        /// <summary>
        /// Close Mod Editor
        /// </summary>
        public void CloseEditor()
        {
            ModEditor = null;
            if (Mods.Count > 0)
            {
                SelectedIndex = 0;
            }
            else
            {
                SelectedIndex = -1;
            }
            GC.Collect();
        }


        /// <summary>
        /// Updates the Version Manager Version List
        /// If modid is passed it will be only be updated IF loaded mod id matches the modid
        /// </summary>
        /// <param name="modid"></param>
        public void UpdateVersionManager(string? modid = null)
        {
            ModEditor?.UpdateVersionManager(modid); 
        }

        /// <summary>
        /// If the mod editor is open this will refresh the listed build options in the Fso Settings tabs
        /// </summary>
        public void UpdateListedFsoBuildVersionsInEditor()
        {
            ModEditor?.UpdateFsoSettingsComboBox();
        }

        /// <summary>
        /// Resets the mod editor
        /// Reloads the currently loaded mod in editor if it matches the passed id, null for always
        /// </summary>
        /// <param name="modid"></param>
        public void ResetModEditor(string? modid = null)
        {
            if (ModEditor != null && ( modid == null || modid == ModEditor.ActiveVersion.id))
            {
                ModEditor = new DevModEditorViewModel();
                ModEditor.StartModEditor(Mods[selectedIndex]);
            }
        }

        /// <summary>
        /// Delete a mod from the Dev Mod list
        /// </summary>
        /// <param name="modid"></param>
        public void DeleteMod(string modid)
        {
            var mod = Mods.FirstOrDefault(m=>m.id == modid);
            if(mod != null)
                Mods.Remove(mod);
        }

        /// <summary>
        /// Add a mod to the Dev Mod list
        /// Only one version can exist in this list at any given time
        /// </summary>
        /// <param name="mod"></param>
        public void AddMod(Mod mod)
        {
            try
            {
                var exist = Mods.FirstOrDefault(m => m.id == mod.id);
                if (exist == null)
                {
                    if(mod.modSource == ModSource.nebula)
                        Mods.Add(mod);
                }
                else
                {
                    //Do not consider DevEnv versions here because they dont have a proper "last update" date for the sorting
                    //So always replace them if a normal version is found and ignore it if picked up later
                    if (exist.version == "999.0.0-DevEnv" || mod.version != "999.0.0-DevEnv" && SemanticVersion.Compare(exist.version, mod.version) < 1)
                    {
                        var index = Mods.IndexOf(exist);
                        if (index != -1)
                        {
                            Mods.RemoveAt(index);
                            Mods.Insert(index, mod);
                        }
                    }
                    if (ModEditor != null && ModEditor.ActiveVersion.id == mod.id)
                    {
                        ModEditor.AddModToList(mod);
                    }
                }
            }catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DeveloperModsViewModel.AddMod()", ex);
            }
        }

        /// <summary>
        /// Clear view
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        internal void ClearView()
        {
            Mods.Clear();
            CloseEditor();
        }

        /* Buttons */

        /// <summary>
        /// Open Create Mod dialog
        /// </summary>
        internal async void CreateMod()
        {
            var dialog = new DevModCreateNewView();
            dialog.DataContext = new DevModCreateNewViewModel(dialog);
            await dialog.ShowDialog<DevModCreateNewView?>(MainWindow.instance!);
        }

        public void UpdateBuildNames(string stableIn, string nightlyIn)
        {
            LatestStable = stableIn;
            LatestNightly = nightlyIn;
            UpdateBuildInstallButtons();
        }

        public void UpdateBuildInstallButtons(){
            if (LatestStable == "")
            {
                StableInstalled = true;
            }
            else 
            {
                var installed = Knossos.GetInstalledBuild("FSO", LatestStable);
                StableInstalled = (installed != null);
                if (StableInstalled)
                    LatestStable = "";
            }
            if (LatestNightly == "")
            {
                NightlyInstalled = true;
            }
            else 
            {
                var installed = Knossos.GetInstalledBuild("FSO", LatestNightly);
                NightlyInstalled = (installed != null);
                if (NightlyInstalled)
                    LatestNightly = "";
            }
        }

        public void InstallLatestNightly()
        {
            if (LatestNightly != ""){
                var nightly = new Mod();
                nightly.id = "FSO";
                nightly.version = LatestNightly;
                nightly.type = ModType.engine;
                nightly.stability = "nightly";

                MainViewModel.Instance!.FsoBuildsView!.RelayInstallBuild(nightly);

                var installed = Knossos.GetInstalledBuild("FSO", LatestNightly);
                if (installed != null){
                    LatestNightly = "";
                    UpdateBuildInstallButtons();
                }
            }
        }

        public void InstallLatestStable()
        {
            if (LatestStable != ""){
                var stable = new Mod();
                stable.id = "FSO";
                stable.version = LatestStable;
                stable.type = ModType.engine;
                stable.stability = "stable";

                MainViewModel.Instance!.FsoBuildsView!.RelayInstallBuild(stable);

                var installed = Knossos.GetInstalledBuild("FSO", LatestStable);
                if (installed != null){
                    LatestStable = "";
                    UpdateBuildInstallButtons();
                }
            }
        }

        internal async void OpenAddToolGuide()
        {
            try
            {
                var text = "If you want to add or update your tool to Knossos.NET tool section, you will need to make a PR in the tool repo: https://github.com/KnossosNET/Knet-Tool-Repo." +
                    "\nEditing the 'knet_tools.json' to add or modify your tool. The download must be in a direct-download storage (like a Github release), no google drive support." +
                    "\nIf you dont know how to do this, please create an 'issue' instead in that same repo, providing the tool data and download link." +
                    "\nIf you press continue you will be taken to the repo.";
                var resp = await MessageBox.Show(MainWindow.instance, text, "Adding tools to Knossos.NET", MessageBox.MessageBoxButtons.ContinueCancel);
                if (resp == MessageBox.MessageBoxResult.Continue)
                    KnUtils.OpenBrowserURL("https://github.com/KnossosNET/Knet-Tool-Repo");
            }
            catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DeveloperModsViewModel.OpenAddToolGuide", ex);
            }
        }
    }
}
