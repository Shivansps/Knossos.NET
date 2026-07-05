using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{

    public partial class FsoBuildPickerViewModel : ViewModelBase
    {
        private bool directSeparatorAdded = false;
        private Window? window = null;
        [ObservableProperty]
        internal ObservableCollection<ComboBoxItem> buildItems = new ObservableCollection<ComboBoxItem>();

        internal bool hideRC = Knossos.globalSettings.hideBuildRC;
        internal bool HideRC
        {
            get
            {
                return hideRC;
            }
            set
            {
                if (hideRC != value)
                {
                    this.SetProperty(ref hideRC, value);
                    Knossos.globalSettings.hideBuildRC = value;
                    BuildItems.ForEach(buildItem => { 
                        if(buildItem.Tag != null && buildItem.Tag?.ToString() == "rc")
                            buildItem.IsVisible = !value;
                    });
                }
            }
        }

        internal bool hideCustom = Knossos.globalSettings.hideBuildCustom;
        internal bool HideCustom
        {
            get
            {
                return hideCustom;
            }
            set
            {
                if (hideCustom != value)
                {
                    Knossos.globalSettings.hideBuildCustom = value;
                    this.SetProperty(ref hideCustom, value);
                    BuildItems.ForEach(buildItem => {
                        if (buildItem.Tag != null && buildItem.Tag?.ToString() == "custom")
                            buildItem.IsVisible = !value;
                    });
                }
            }
        }

        internal bool hideNightly = Knossos.globalSettings.hideBuildNightly;
        internal bool HideNightly
        {
            get
            {
                return hideNightly;
            }
            set
            {
                if (hideNightly != value)
                {
                    this.SetProperty(ref hideNightly, value);
                    Knossos.globalSettings.hideBuildNightly = value;
                    BuildItems.ForEach(buildItem => {
                        if (buildItem.Tag != null && buildItem.Tag?.ToString() == "nightly")
                            buildItem.IsVisible = !value;
                    });
                }
            }
        }

        [ObservableProperty]
        internal int buildSelectedIndex = 0;

        public FsoBuildPickerViewModel()
        {
        }

        public FsoBuildPickerViewModel(Window window)
        {
            this.window = window;
        }

        public FsoBuildPickerViewModel(FsoBuild? preSelected, Window? window)
        {
            FillBuildsItems(preSelected);
            this.window = window;
        }

        /*
            Gets the selected FSO build in the list
            Null if none (Mod Default).
        */
        public FsoBuild? GetSelectedFsoBuild()
        {
            if(BuildSelectedIndex == 0)
            {
                return null;
            }
            else
            {
                return (FsoBuild)BuildItems[BuildSelectedIndex].Content!;
            }
        }

        private void FillBuildsItems(FsoBuild? preSelected)
        {
            ComboBoxItem? selectedItem = null;
            ComboBoxItem modDefault = new ComboBoxItem();
            modDefault.Content = "Mod Default";
            modDefault.Tag = "default";
            BuildItems.Add(modDefault);


            ComboBoxItem separator = new ComboBoxItem();
            separator.Content = "--- Stable Builds ---";
            separator.IsEnabled = false;
            separator.Tag = "stable";
            BuildItems.Add(separator);

            var stable = Knossos.GetInstalledBuildsList(null, FsoStability.Stable);
            stable.Sort(FsoBuild.CompareDatesAsTimestamp);
            foreach (var build in stable)
            {
                ComboBoxItem item = new ComboBoxItem();
                item.Content = build;
                item.Tag = "stable";
                item.FontWeight = FontWeight.Bold;
                BuildItems.Add(item);
                if (preSelected != null)
                {
                    if(preSelected == build)
                    {
                        selectedItem = item;
                    }
                }
            }

            var custom = Knossos.GetInstalledBuildsList(null, FsoStability.Custom);
            if (custom.Count > 0)
            {
                separator = new ComboBoxItem();
                separator.Content = "--- Custom Builds ---";
                separator.IsEnabled = false;
                separator.Tag = "custom";
                BuildItems.Add(separator);
                custom.Sort(FsoBuild.CompareDatesAsTimestamp);
                foreach (var build in custom)
                {
                    ComboBoxItem item = new ComboBoxItem();
                    item.Content = build;
                    item.Tag = "custom";
                    item.FontWeight = FontWeight.Bold;
                    item.IsVisible = !HideCustom;
                    item.Foreground = KnUtils.GetResourceColor("PrimaryColorMouseOver");
                    BuildItems.Add(item);
                    if (preSelected != null)
                    {
                        if (preSelected == build)
                        {
                            selectedItem = item;
                        }
                    }
                }
            }

            var rc = Knossos.GetInstalledBuildsList(null, FsoStability.RC);
            if (rc.Count > 0)
            {
                separator = new ComboBoxItem();
                separator.Content = "--- Release Candidate Builds ---";
                separator.IsEnabled = false;
                separator.Tag = "rc";
                BuildItems.Add(separator);
                rc.Sort(FsoBuild.CompareDatesAsTimestamp);
                foreach (var build in rc)
                {
                    ComboBoxItem item = new ComboBoxItem();
                    item.Content = build;
                    item.Tag = "rc";
                    item.IsVisible = !HideRC;
                    item.FontWeight = FontWeight.Bold;
                    item.Foreground = KnUtils.GetResourceColor("SecondaryColorMouseOver");
                    BuildItems.Add(item);
                    if (preSelected != null)
                    {
                        if (preSelected == build)
                        {
                            selectedItem = item;
                        }
                    }
                }
            }

            var nightly = Knossos.GetInstalledBuildsList(null, FsoStability.Nightly);
            if (nightly.Count > 0)
            {
                separator = new ComboBoxItem();
                separator.Content = "--- Nightly Builds ---";
                separator.IsEnabled = false;
                separator.Tag = "nightly";
                BuildItems.Add(separator);
                nightly.Sort(FsoBuild.CompareDatesAsTimestamp);
                foreach (var build in nightly)
                {
                    ComboBoxItem item = new ComboBoxItem();
                    item.Content = build;
                    item.Tag = "nightly";
                    item.IsVisible = !HideNightly;
                    item.FontWeight = FontWeight.Bold;
                    item.Foreground = KnUtils.GetResourceColor("TertiaryColor");
                    BuildItems.Add(item);
                    if (preSelected != null)
                    {
                        if (preSelected == build)
                        {
                            selectedItem = item;
                        }
                    }
                }
            }

            if (preSelected != null && preSelected.directExec != null)
            {
                if (!directSeparatorAdded)
                {
                    separator = new ComboBoxItem();
                    separator.Content = "--- Direct Exec ---";
                    separator.IsEnabled = false;
                    separator.Tag = "dexec";
                    BuildItems.Add(separator);
                    directSeparatorAdded = true;
                }
                ComboBoxItem item = new ComboBoxItem();
                item.Content = preSelected;
                item.Tag = "directexec";
                item.FontWeight = FontWeight.Bold;
                item.Foreground = KnUtils.GetResourceColor("QuaternaryColorMouseOver");
                BuildItems.Add(item);
                selectedItem = item;
            }

            if (selectedItem != null)
            {
                BuildSelectedIndex = BuildItems.IndexOf(selectedItem);
            }
            else
            {
                if(preSelected != null)
                {
                    Log.Add(Log.LogSeverity.Warning, "FsoBuildPickerViewModel.FillBuildsItems()", "The previously selected fso build "+preSelected.title + " " + preSelected.version + " not longer exist.");
                }
            }
        }

        internal async void OpenFileCommand()
        {
            var path = await GetPath();
            if(path != null)
            {
                if (!directSeparatorAdded)
                {
                    ComboBoxItem separator = new ComboBoxItem();
                    separator.Content = "--- Direct Exec ---";
                    separator.IsEnabled = false;
                    separator.Tag = "dexec";
                    BuildItems.Add(separator);
                    directSeparatorAdded = true;
                }
                ComboBoxItem item = new ComboBoxItem();
                item.Content = new FsoBuild(path);
                item.Tag = "directexec";
                item.IsSelected = true;
                item.FontWeight = FontWeight.Bold;
                item.Foreground = KnUtils.GetResourceColor("QuaternaryColorMouseOver");
                BuildItems.Add(item);
                BuildSelectedIndex = BuildItems.IndexOf(item);  
            }
        }

        private async Task<string?> GetPath()
        {
            FilePickerOpenOptions options = new FilePickerOpenOptions();
            options.AllowMultiple = false;
            options.Title = "Select the FSO executable file";
            var topmostWindow = KnUtils.GetTopLevel();
            var result = await topmostWindow.StorageProvider.OpenFilePickerAsync(options);

            if (result != null && result.Count > 0)
            {
                var file = new FileInfo(result[0].Path.LocalPath.ToString());
                return file.FullName;
            }
            return null;
        }
    }
}
