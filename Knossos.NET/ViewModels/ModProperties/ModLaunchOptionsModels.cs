using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    public enum LaunchOptionsSection { QuickLaunch, EngineOptions, Executable, CommandLine, Flags, Dependencies, QuickHelp }

    public class ModLaunchOptionsServices
    {
        public virtual bool IsAndroid => KnUtils.IsAndroid;
        public virtual bool IsWindows => KnUtils.IsWindows;
        public virtual string LauncherPath => KnUtils.IsAppImage ? KnUtils.AppImagePath ?? "" : Environment.ProcessPath ?? "";
        public virtual List<FsoBuild> GetBuilds() => Knossos.GetInstalledBuildsList();
        public virtual List<Mod> GetMods() => Knossos.GetInstalledModList(null);
        public virtual FsoBuild? Resolve(ModDependency dependency) => dependency.SelectBuild();
        public virtual Task<FlagsJsonV1?> GetFlagsAsync(FsoBuild build, CancellationToken token) => build.GetFlagsV1Async(token, false);
        public virtual string OptionsPath(Mod mod) => Path.Combine(KnUtils.GetFSODataFolderPath(), "data", mod.id + "_settings.ini");
        public virtual void SaveBuildFilters(bool rc, bool nightly, bool custom)
        {
            Knossos.globalSettings.hideBuildRC = rc;
            Knossos.globalSettings.hideBuildNightly = nightly;
            Knossos.globalSettings.hideBuildCustom = custom;
            Knossos.globalSettings.Save();
        }
        public virtual bool IsSectionExpanded(LaunchOptionsSection section) => Knossos.globalSettings.expandedLaunchSections.Contains(section.ToString());
        public virtual void SetSectionExpanded(LaunchOptionsSection section, bool expanded)
        {
            var sections = Knossos.globalSettings.expandedLaunchSections;
            if (expanded) sections.Add(section.ToString()); else sections.Remove(section.ToString());
        }
        public virtual bool IsFlagCategoryExpanded(string category) => Knossos.globalSettings.expandedLaunchFlagCategories.Contains(category);
        public virtual void SetFlagCategoryExpanded(string category, bool expanded)
        {
            var categories = Knossos.globalSettings.expandedLaunchFlagCategories;
            if (expanded) categories.Add(category); else categories.Remove(category);
        }
        public virtual void SaveExpansionPreferences() => Knossos.globalSettings.Save(false);
        public virtual string CreateShortcut(string title, string executable, string arguments)
        {
            var name = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            KnUtils.CreateDesktopShortcut(name, executable, arguments);
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name + ".lnk");
            if (!File.Exists(path)) throw new IOException("The desktop shortcut could not be created.");
            return path;
        }
    }

    public sealed class LaunchBuildChoice
    {
        public FsoBuild? Build { get; }
        public string Label => Build == null ? "Default (recommended)" : Build.directExec != null ? "Direct executable · " + Build.directExec : Build.stability + " · " + Build;
        public string Color => Build?.stability switch { FsoStability.RC => "#E9B66F", FsoStability.Nightly => "#BBABED", FsoStability.Custom => "#89C8FF", _ => "#F3F6FA" };
        public LaunchBuildChoice(FsoBuild? build) => Build = build;
    }

    public partial class LaunchFlagViewModel : ViewModelBase
    {
        private readonly Action<LaunchFlagViewModel, bool> changed;
        private bool updating;
        public Flag Flag { get; }
        public string Name => Flag.name;
        public string Description => string.IsNullOrWhiteSpace(Flag.description) ? Flag.name : Flag.description;
        [ObservableProperty] private bool isChecked;
        [ObservableProperty] private bool isInherited;
        public bool CanToggle => !IsInherited;
        public LaunchFlagViewModel(Flag flag, Action<LaunchFlagViewModel, bool> changed) { Flag = flag; this.changed = changed; }
        partial void OnIsCheckedChanged(bool value) { if (!updating) changed(this, value); }
        partial void OnIsInheritedChanged(bool value) => OnPropertyChanged(nameof(CanToggle));
        public void Update(bool local, bool inherited)
        {
            updating = true; IsInherited = inherited; IsChecked = local || inherited; updating = false;
        }
        [RelayCommand] private void Help() { if (!string.IsNullOrWhiteSpace(Flag.web_url)) KnUtils.OpenBrowserURL(Flag.web_url); }
    }
    public sealed partial class LaunchFlagGroupViewModel : ViewModelBase
    {
        private readonly Action<bool> expansionChanged;
        [ObservableProperty] private bool isExpanded;
        public string Name { get; }
        public IReadOnlyList<LaunchFlagViewModel> Items { get; }
        public LaunchFlagGroupViewModel(string name, IReadOnlyList<LaunchFlagViewModel> items, bool expanded, Action<bool> expansionChanged)
        {
            Name = name; Items = items; isExpanded = expanded; this.expansionChanged = expansionChanged;
        }
        partial void OnIsExpandedChanged(bool value) => expansionChanged(value);
    }

    public partial class LaunchDependencyViewModel : ViewModelBase
    {
        private readonly ModLaunchOptionsViewModel owner;
        private readonly ModDependency? original;
        private bool filling;
        public bool IsRoot { get; }
        public ObservableCollection<string> IdChoices { get; } = new();
        public ObservableCollection<string> VersionChoices { get; } = new();
        [ObservableProperty] private string selectedId;
        [ObservableProperty] private string selectedVersion;
        public bool CanEdit => owner.CustomDependencies && !IsRoot;
        public bool CanMoveUp => owner.CustomDependencies && owner.Dependencies.IndexOf(this) > 0;
        public bool CanMoveDown => owner.CustomDependencies && owner.Dependencies.IndexOf(this) < owner.Dependencies.Count - 1;
        public LaunchDependencyViewModel(ModLaunchOptionsViewModel owner, string id, ModDependency? dependency, bool root = false)
        {
            this.owner = owner; original = dependency; IsRoot = root; selectedId = id;
            selectedVersion = dependency?.version ?? "Newest";
            foreach (var choice in owner.DependencyIds.Append(id).Distinct().OrderBy(x => x)) IdChoices.Add(choice);
            FillVersions();
        }
        private void FillVersions()
        {
            var selected = SelectedVersion ?? "Newest";
            VersionChoices.Clear(); VersionChoices.Add("Newest");
            foreach (var version in owner.DependencyVersions(SelectedId))
                foreach (var prefix in new[] { "", ">=", "~", "<=" }) VersionChoices.Add(prefix + version);
            if (!VersionChoices.Contains(selected)) VersionChoices.Insert(1, selected);
            SelectedVersion = selected;
        }
        partial void OnSelectedIdChanged(string value)
        {
            filling = true;
            SelectedVersion = "Newest"; FillVersions();
            filling = false; owner.DependencyChanged();
        }
        partial void OnSelectedVersionChanged(string value) { if (!filling) owner.DependencyChanged(); }
        public ModDependency? ToDependency() => IsRoot || string.IsNullOrWhiteSpace(SelectedId) ? null : new ModDependency
        {
            id = SelectedId, version = SelectedVersion == "Newest" ? null : SelectedVersion,
            packages = original?.id == SelectedId ? original.packages?.ToList() : null
        };
        public void RefreshState()
        {
            OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanMoveUp)); OnPropertyChanged(nameof(CanMoveDown));
            MoveUpCommand.NotifyCanExecuteChanged(); MoveDownCommand.NotifyCanExecuteChanged(); RemoveCommand.NotifyCanExecuteChanged();
        }
        [RelayCommand(CanExecute = nameof(CanMoveUp))] private void MoveUp() => owner.MoveDependency(this, -1);
        [RelayCommand(CanExecute = nameof(CanMoveDown))] private void MoveDown() => owner.MoveDependency(this, 1);
        [RelayCommand(CanExecute = nameof(CanEdit))] private void Remove() => owner.RemoveDependency(this);
    }
}
