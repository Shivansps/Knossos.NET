using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Knossos.NET.ViewModels
{
    public partial class DevModEditorViewModel : ViewModelBase
    {
        public DevModEditorViewModel() 
        {

        }

        [ObservableProperty]
        public DevModVersionsViewModel? versionsView;
        [ObservableProperty]
        public object? pkgMgrView;
        [ObservableProperty]
        public DevModFsoSettingsViewModel? fsoSettingsView;
        [ObservableProperty]
        public DevModDetailsViewModel? detailsView;
        [ObservableProperty]
        public DevModMembersMgrViewModel? membersView;
        [ObservableProperty]
        internal ObservableCollection<ComboBoxItem> toolItems = new ObservableCollection<ComboBoxItem>();
        [ObservableProperty]
        internal int toolIndex = 0;
        internal bool ToolsAvailable { get; } = !KnUtils.IsAndroid;

        internal int tabIndex = 0;
        internal int TabIndex
        {
            get => tabIndex;
            set
            {
                if (value != tabIndex)
                {
                    this.SetProperty(ref tabIndex, value);
                    if (tabIndex == 4) //Members
                    {
                        if(MembersView != null)
                        {
                            MembersView.UpdateUI();
                        }
                    }
                }
            }
        }

        [ObservableProperty]
        public string name = string.Empty;
        [ObservableProperty]
        public string version = string.Empty;
        [ObservableProperty]
        public bool isEngineBuild = false;

        [ObservableProperty]
        internal Bitmap? modImage;

        private List<Mod> mods = new List<Mod>();
        private int index = 0;

        public Mod ActiveVersion
        {
            get
            {
                return mods[index];
            }
        }

        public List<Mod> GetModList()
        {
            return mods;
        }

        public void AddModToList(Mod newMod)
        {
            var currentActive = ActiveVersion;
            mods.Add(newMod);
            mods.Sort(Mod.CompareVersion);
            ChangeActiveVersion(currentActive);
        }

        public void ChangeActiveVersion(Mod mod)
        {
            if (mod != ActiveVersion)
            {
                index = mods.IndexOf(mod);
                LoadActiveVersion();
            }
        }

        public void DeleteActiveVersion()
        {
            int currentIndex = index;
            string id = ActiveVersion.id;
            Knossos.RemoveMod(ActiveVersion);
            mods.Remove(ActiveVersion);
            if (mods.Any())
            {
                if (currentIndex > 0)
                {
                    index--;
                }
                else
                {
                    index = 0;
                }
                LoadActiveVersion();
            }
            else
            {
                //Delete mod from view and close editor
                DeveloperModsViewModel.Instance!.DeleteMod(id);
                DeveloperModsViewModel.Instance!.CloseEditor();
            }
        }

        public void DeleteAllVersions()
        {
            //Delete mod from view and close editor
            Knossos.RemoveMod(ActiveVersion.id);
            DeveloperModsViewModel.Instance!.DeleteMod(ActiveVersion.id);
            DeveloperModsViewModel.Instance!.CloseEditor();
        }

        public void StartModEditor(Mod mod)
        {
            try
            {
                //Clean old data
                Name = Version = string.Empty;
                mods.Clear();
                ModImage?.Dispose();
                VersionsView = null;
                PkgMgrView = null;
                FsoSettingsView = null;
                DetailsView = null;
                MembersView = null;
                IsEngineBuild = false;
                if (ToolsAvailable)
                    LoadTools();
                ModImage = new Bitmap(AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/general/NebulaDefault.png")));
                index = 0;
                //Get all installed mods with this ID
                switch (mod.type)
                {
                    case ModType.tc:
                    case ModType.mod:
                        mods = Knossos.GetInstalledModList(mod.id);
                        break;
                    case ModType.engine:
                        var builds = Knossos.GetInstalledBuildsList(mod.id);
                        foreach(var b in builds)
                        {
                            if(b.modData != null)
                                mods.Add(b.modData);
                        }
                        IsEngineBuild = true;
                        break;
                    case ModType.tool:
                        break;
                }
                //Sort by version
                mods.Sort(Mod.CompareVersion);
                //Filter and determine best active version
                foreach (var m in mods.ToList())
                {
                    if (m.modSource != ModSource.nebula)
                    {
                        mods.Remove(m);
                    }
                    else
                    {
                        if (SemanticVersion.Compare(ActiveVersion.version, m.version) < 1)
                        {
                            ChangeActiveVersion(m);
                        }
                    }
                }
                //Load Mod to UI
                LoadActiveVersion();
            }
            catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModEditorViewModel.StartModEditor()", ex);
            }
        }

        private void LoadActiveVersion()
        {
            try
            {
                var mod = ActiveVersion;
                Name = mod.title;
                Version = mod.version;
                if (!string.IsNullOrEmpty(mod.tile) && File.Exists(mod.fullPath + Path.DirectorySeparatorChar + mod.tile))
                {
                    ModImage?.Dispose();
                    ModImage = new Bitmap(mod.fullPath + Path.DirectorySeparatorChar + mod.tile);
                }
                //Templated Elements
                if (IsEngineBuild)
                {
                    PkgMgrView = new DevBuildPkgMgrViewModel(this);
                }
                else
                {
                    PkgMgrView = new DevModPkgMgrViewModel(this);
                    FsoSettingsView = new DevModFsoSettingsViewModel(this);
                }
                VersionsView = new DevModVersionsViewModel(this);
                DetailsView = new DevModDetailsViewModel(this);
                MembersView = new DevModMembersMgrViewModel(this);
                if(TabIndex == 4) //members tab is open?
                {
                    MembersView.UpdateUI();
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModEditorViewModel.LoadActiveVersion()", ex);
            }
        }

        /* Button Commands */
        internal void PlayMod(object type)
        {
            try
            {
                switch ((string)type)
                {
                    case "release": Knossos.PlayMod(ActiveVersion,FsoExecType.Release); break;
                    case "fred2": Knossos.PlayMod(ActiveVersion, FsoExecType.Fred2); break;
                    case "qtfred": Knossos.PlayMod(ActiveVersion, FsoExecType.QtFred); break;
                    case "qtfreddebug": Knossos.PlayMod(ActiveVersion, FsoExecType.QtFredDebug); break;
                    case "debug": Knossos.PlayMod(ActiveVersion, FsoExecType.Debug); break;
                    case "fred2debug": Knossos.PlayMod(ActiveVersion, FsoExecType.Fred2Debug); break;
                }
            }catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModEditorViewModel.PlayMod()", ex);
            }
        }

        internal async void OpenTool()
        {
            try
            {
                if (ToolIndex != -1 && ToolItems.Count() > ToolIndex)
                {
                    var selected = ToolItems[ToolIndex];
                    if (selected != null)
                    {
                        var tool = selected.DataContext as Tool;
                        if (tool != null)
                        {
                            await tool.Open(ActiveVersion.fullPath);
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModEditorViewModel.OpenTool()", ex);
            }
        }

        internal void OpenModFolder()
        {
            try
            {
                KnUtils.OpenFolder(ActiveVersion.fullPath);
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModEditorViewModel.OpenModFolder()", ex);
            }
        }

        private void LoadTools()
        {
            try
            {
                ToolItems.Clear();
                ToolIndex = -1;
                var tools = Knossos.GetTools();
                foreach (var tool in tools)
                {
                    var item = new ComboBoxItem();
                    item.DataContext = tool;
                    item.Content = tool.name;
                    InsertToolInOrder(item);
                }
                if (ToolItems.Any())
                {
                    ToolIndex = 0;
                }
            }catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModEditorViewModel.LoadTools", ex);
            }
        }

        private void InsertToolInOrder(ComboBoxItem item)
        {
            try
            {
                int i;
                for (i = 0; i < ToolItems.Count; i++)
                {
                    var toolInList = ToolItems[i].DataContext as Tool;
                    var tool = item.DataContext as Tool;

                    if (toolInList!.isFavorite == tool!.isFavorite && String.Compare(toolInList!.name, tool.name) > 0)
                    {
                        break;
                    }
                    if (!toolInList!.isFavorite && tool.isFavorite)
                    {
                        break;
                    }
                }
                ToolItems.Insert(i, item);
            }catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModEditorViewModel.InsertToolInOrder", ex);
            }
        }


        /// <summary>
        /// Updates the Version Manager Version List
        /// If modid is passed it will be only be updated IF loaded mod id matches the modid
        /// </summary>
        /// <param name="modid"></param>
        public void UpdateVersionManager(string? modid = null)
        {
            if(VersionsView != null && (modid == null || ActiveVersion.id == modid) )
                VersionsView?.HackUpdateModList();
        }

        /// <summary>
        /// If the mod editor is open this will refresh the listed build options in the Fso Settings tabs
        /// </summary>
        public void UpdateFsoSettingsComboBox()
        {
            if (FsoSettingsView != null)
            {
                FsoSettingsView.UpdateFsoPicker();
            }
        }
    }
}
