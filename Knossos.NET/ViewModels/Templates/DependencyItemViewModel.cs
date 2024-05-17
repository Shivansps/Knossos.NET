using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Knossos.NET.ViewModels
{
    public partial class DependencyItemViewModel : ViewModelBase, INotifyPropertyChanged
    {
        internal ObservableCollection<ComboBoxItem> VersionItems { get; set; } = new ObservableCollection<ComboBoxItem>();

        internal ObservableCollection<ComboBoxItem> ModItems { get; set; } = new ObservableCollection<ComboBoxItem>();

        private bool rootMod  = false;

        private ModSettingsViewModel? modSettingsViewModel;

        [ObservableProperty]
        internal bool readOnly = true;
        [ObservableProperty]
        internal bool arrowsReadOnly = true;
        [ObservableProperty]
        internal int versionSelectedIndex = 0;

        internal int modSelectedIndex = 0;
        internal int ModSelectedIndex
        {
            get
            {
                return modSelectedIndex;
            }
            set
            {
                if (modSelectedIndex != value)
                {
                    modSelectedIndex = value;
                    VersionItems.Clear();
                    FillAllVersions();
                    VersionSelectedIndex = 1;
                }
            }
        }


        public DependencyItemViewModel()
        {
        }

        public DependencyItemViewModel(ModSettingsViewModel? settingsVM=null)
        {
            FillModList();
            SetReadOnly(false);
            modSettingsViewModel = settingsVM;
        }

        public DependencyItemViewModel(string title, ModSettingsViewModel settingsVM)
        {
            var itemMod = new ComboBoxItem();
            itemMod.Content = title;
            ModItems.Add(itemMod);
            rootMod = true;
            modSettingsViewModel = settingsVM;
        }

        public DependencyItemViewModel(ModDependency dep, ModSettingsViewModel settingsVM) 
        {
            var itemMod = new ComboBoxItem();
            itemMod.Content = dep.id;
            ModItems.Add(itemMod);
            modSelectedIndex = 0;

            var itemVer = new ComboBoxItem();
            if (dep.version != null)
            {
                itemVer.Content = dep.version;
            }
            else
            {
                itemVer.Content = "Newest";
            }
            VersionItems.Add(itemVer);
            VersionSelectedIndex = 0;
            FillModList();
            FillAllVersions();
            modSettingsViewModel = settingsVM;
        }

        public void SetReadOnly(bool value)
        {
            ArrowsReadOnly = value;
            if (!rootMod)
            {
                ReadOnly = value;
            }
        }

        public ModDependency? GetModDependency()
        {
            var modID = ModItems[ModSelectedIndex].Content?.ToString();
            if (ModItems[ModSelectedIndex].IsEnabled && modID != null)
            {
                var dep = new ModDependency();
                dep.id = modID;
                if (VersionItems.Count > 0)
                {
                    dep.version = VersionItems[VersionSelectedIndex].Content?.ToString();
                }

                if (dep.version == "Newest")
                {
                    dep.version = null;
                }        
                return dep;
            }
            return null;
        }

        internal void ArrowUPCommand()
        {
            if(modSettingsViewModel != null)
            {
                modSettingsViewModel.DepUP(this);
            }
        }

        internal void ArrowDOWNCommand()
        {
            if (modSettingsViewModel != null)
            {
                modSettingsViewModel.DepDOW(this);
            }
        }

        internal void DeleteCommand() 
        {
            if (modSettingsViewModel != null)
            {
                modSettingsViewModel.DepDEL(this);
            }
        }

        private void FillModList()
        {
            var mods = Knossos.GetInstalledModList(null);
            var builds = Knossos.GetInstalledBuildsList(null);
            var usedIds = new List<string>();

            var separator = new ComboBoxItem();
            separator.Content = "--- Mods ---";
            separator.IsEnabled = false;
            ModItems.Add(separator);

            foreach (var mod in mods)
            {
                if (usedIds.IndexOf(mod.id) == -1 && (ModItems.Count == 0 || mod.id != ModItems[0].Content!.ToString()))
                {
                    var itemMod = new ComboBoxItem();
                    itemMod.Content = mod.id;
                    ModItems.Add(itemMod);
                    usedIds.Add(mod.id);
                }
            }

            separator = new ComboBoxItem();
            separator.Content = "--- Engine Builds ---";
            separator.IsEnabled = false;
            ModItems.Add(separator);

            foreach (var build in builds)
            {
                if (usedIds.IndexOf(build.id) == -1 && (ModItems.Count == 0 || build.id != ModItems[0].Content!.ToString()))
                {
                    var itemMod = new ComboBoxItem();
                    itemMod.Content = build.id;
                    ModItems.Add(itemMod);
                    usedIds.Add(build.id);
                }
            }
    }

        private void FillAllVersions()
        {
            if(ModItems.Count > 0)
            {
                var id = ModItems[ModSelectedIndex].Content!.ToString();
                var mods = Knossos.GetInstalledModList(id);

                if(VersionItems.Count == 0 || VersionItems[0].Content!.ToString() != "Newest")
                {
                    var separator = new ComboBoxItem();
                    separator.Content = "----------";
                    separator.IsEnabled = false;
                    VersionItems.Add(separator);
                    var itemVer = new ComboBoxItem();
                    itemVer.Content = "Newest";
                    VersionItems.Add(itemVer);
                }

                if (mods.Count > 0)
                {
                    mods.Reverse();
                    var separator = new ComboBoxItem();
                    separator.Content = "--- == ---";
                    separator.IsEnabled = false;
                    VersionItems.Add(separator);

                    foreach (var mod in mods)
                    {
                        var itemVer = new ComboBoxItem();
                        itemVer.Content = mod.version;
                        VersionItems.Add(itemVer);
                    }

                    separator = new ComboBoxItem();
                    separator.Content = "--- >= ---";
                    separator.IsEnabled = false;
                    VersionItems.Add(separator);

                    foreach (var mod in mods)
                    {
                        var itemVer = new ComboBoxItem();
                        itemVer.Content = ">=" + mod.version;
                        VersionItems.Add(itemVer);
                    }

                    separator = new ComboBoxItem();
                    separator.Content = "--- <= ---";
                    separator.IsEnabled = false;
                    VersionItems.Add(separator);

                    foreach (var mod in mods)
                    {
                        var itemVer = new ComboBoxItem();
                        itemVer.Content = "<=" + mod.version;
                        VersionItems.Add(itemVer);
                    }

                    separator = new ComboBoxItem();
                    separator.Content = "--- ~ ---";
                    separator.IsEnabled = false;
                    VersionItems.Add(separator);

                    foreach (var mod in mods)
                    {
                        var itemVer = new ComboBoxItem();
                        itemVer.Content = "~" + mod.version;
                        VersionItems.Add(itemVer);
                    }
                }
                else
                {
                    var builds = Knossos.GetInstalledBuildsList(id);
                    if (builds.Count > 0)
                    {
                        builds.Reverse();
                        var separator = new ComboBoxItem();
                        separator.Content = "--- == ---";
                        separator.IsEnabled = false;
                        VersionItems.Add(separator);

                        foreach (var build in builds)
                        {
                            var itemVer = new ComboBoxItem();
                            itemVer.Content = build.version;
                            VersionItems.Add(itemVer);
                        }

                        separator = new ComboBoxItem();
                        separator.Content = "--- >= ---";
                        separator.IsEnabled = false;
                        VersionItems.Add(separator);

                        foreach (var build in builds)
                        {
                            var itemVer = new ComboBoxItem();
                            itemVer.Content = ">=" + build.version;
                            VersionItems.Add(itemVer);
                        }

                        separator = new ComboBoxItem();
                        separator.Content = "--- <= ---";
                        separator.IsEnabled = false;
                        VersionItems.Add(separator);

                        foreach (var build in builds)
                        {
                            var itemVer = new ComboBoxItem();
                            itemVer.Content = "<=" + build.version;
                            VersionItems.Add(itemVer);
                        }
                    }
                }
            }
        }
    }
}
