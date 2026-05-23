using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    public partial class DevModPkgMgrViewModel : ViewModelBase
    {
        /*********************************************DEPENDENCY ITEM************************************************************/
        public partial class EditorDependencyItem : ObservableObject
        {
            public ModDependency Dependency { get; set; }

            [ObservableProperty]
            internal List<ModPackage> packages = new List<ModPackage>();

            [ObservableProperty]
            internal bool displayPackages = false;

            //True when the dep was loaded with a range/complex constraint that the dropdowns can't represent.
            //Cleared on any user interaction with the mod, version, or operator dropdowns. While set,
            //GetDependency() returns the original Dependency unchanged so the constraint round-trips intact.
            private bool preserveOriginalConstraint = false;

            internal int versionSelectedIndex = 0;
            internal int VersionSelectedIndex
            {
                get
                {
                    return versionSelectedIndex;
                }
                set
                {
                    if(versionSelectedIndex != value)
                    {
                        SetProperty(ref versionSelectedIndex, value);
                        preserveOriginalConstraint = false;
                        FillPackages();
                    }
                }
            }

            [ObservableProperty]
            internal int versionTypeIndex = 0;

            partial void OnVersionTypeIndexChanged(int value)
            {
                if (preserveOriginalConstraint)
                {
                    //User edited the operator while a complex constraint was preserved — exit preserve mode
                    //and rebuild the version dropdown so the placeholder is gone and a real version is selected.
                    preserveOriginalConstraint = false;
                    VersionItems.Clear();
                    FillAllVersions();
                    VersionSelectedIndex = 1;
                }
            }

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
                        SetProperty(ref modSelectedIndex, value);
                        preserveOriginalConstraint = false;
                        VersionItems.Clear();
                        FillAllVersions();
                        VersionSelectedIndex = 1;
                    }
                }
            }

            internal ObservableCollection<ComboBoxItem> VersionItems { get; set; } = new ObservableCollection<ComboBoxItem>();

            internal ObservableCollection<ComboBoxItem> ModItems { get; set; } = new ObservableCollection<ComboBoxItem>();

            internal EditorModPackageItem EditorPackageItem { get; set; }

            private string modID = string.Empty;
            private string? modParent = null;

            public EditorDependencyItem(ModDependency dep, EditorModPackageItem pkgItem, string modId, string? modParent)
            {
                Dependency = dep;
                EditorPackageItem = pkgItem;
                this.modID = modId;
                this.modParent = modParent;

                FillModList();

                var currentMod=ModItems.FirstOrDefault(x => x.Tag != null && x.Tag.ToString() == dep.id);
                if(currentMod != null)
                {
                    modSelectedIndex=ModItems.IndexOf(currentMod);

                    FillAllVersions();

                    if (SemanticVersion.IsComplexConstraint(dep.version))
                    {
                        //Range syntax cannot be expressed via the operator+version dropdowns; show the original
                        //string in a placeholder item and enter preserve mode so GetDependency() round-trips
                        //the dep unchanged unless the user edits one of the dropdowns.
                        preserveOriginalConstraint = true;
                        var complexItem = new ComboBoxItem { Content = dep.version };
                        VersionItems.Add(complexItem);
                        versionSelectedIndex = VersionItems.Count - 1;
                        versionTypeIndex = 0;
                    }
                    else
                    {
                        versionTypeIndex = OperatorTypeIndexFromVersion(dep.version);

                        var bareVersion = dep.version != null ? StripVersionOperators(dep.version) : null;
                        var currentVersion = VersionItems.FirstOrDefault(x => x.Content != null && bareVersion != null && x.Content.ToString() == bareVersion);
                        if (currentVersion != null)
                        {
                            versionSelectedIndex = VersionItems.IndexOf(currentVersion);
                        }
                        else if (!string.IsNullOrEmpty(bareVersion))
                        {
                            //Requested version isn't installed — surface it as its own entry so the UI matches the JSON.
                            var itemVer = new ComboBoxItem();
                            itemVer.Content = bareVersion;
                            VersionItems.Add(itemVer);
                            versionSelectedIndex = VersionItems.Count - 1;
                        }
                        else
                        {
                            VersionSelectedIndex = 0;
                        }
                    }

                    FillPackages();
                }
                else
                {
                    //Dependency Mod id not found
                    var itemMod = new ComboBoxItem();
                    itemMod.Tag = dep.id;
                    itemMod.Content = dep.id + " (Missing)";
                    itemMod.IsEnabled = false; //Important! This signals that on writing to return the original depedency data to avoid possible loss of dep data
                    ModItems.Insert(0, itemMod);
                    ModSelectedIndex = 0;
                    VersionTypeIndex = OperatorTypeIndexFromVersion(dep.version);
                    var itemVer = new ComboBoxItem();
                    itemVer.Content = dep.version != null ? StripVersionOperators(dep.version) : "Any";
                    VersionItems.Add(itemVer);
                }
            }

            private void FillModList()
            {
                var mods = Knossos.GetInstalledModList(null);
                var builds = Knossos.GetInstalledBuildsList(null);
                var usedIds = new List<string>() { modID };

                var separator = new ComboBoxItem();
                separator.Content = "--- Mods ---";
                separator.IsEnabled = false;
                separator.Tag = "-1";
                ModItems.Add(separator);

                foreach (var mod in mods)
                {
                    //I have disabled the comparison that disallows TCs to depend on mods, and mods to depend on a mod from other TC
                    if (usedIds.IndexOf(mod.id) == -1 /*&& modParent != null && modParent == mod.parent*/)
                    {
                        var itemMod = new ComboBoxItem();
                        itemMod.Content = mod.title + "  [ "+ mod.id+" ]";
                        itemMod.Tag = mod.id;
                        ModItems.Add(itemMod);
                        usedIds.Add(mod.id);
                    }
                }

                separator = new ComboBoxItem();
                separator.Tag = "-1";
                separator.Content = "--- Engine Builds ---";
                separator.IsEnabled = false;
                ModItems.Add(separator);

                foreach (var build in builds)
                {
                    if (usedIds.IndexOf(build.id) == -1 )
                    {
                        var itemMod = new ComboBoxItem();
                        itemMod.Content = build.title + "  [ " + build.id + " ]";
                        itemMod.Tag = build.id;
                        ModItems.Add(itemMod);
                        usedIds.Add(build.id);
                    }
                }
            }

            private void FillAllVersions()
            {
                if (ModItems.Count > 0)
                {
                    var id = ModItems[ModSelectedIndex].Tag!.ToString();
                    var mods = Knossos.GetInstalledModList(id);

                    var anyVer = new ComboBoxItem();
                    anyVer.Content = "Any";
                    anyVer.DataContext = null;
                    VersionItems.Add(anyVer);


                    if (mods.Any())
                    {
                        mods.Reverse();
                        foreach (var mod in mods)
                        {
                            var itemVer = new ComboBoxItem();
                            itemVer.Content = mod.version;
                            itemVer.DataContext = mod;
                            VersionItems.Add(itemVer);
                        }
                    }
                    else
                    {
                        var builds = Knossos.GetInstalledBuildsList(id);
                        if (builds.Any())
                        {
                            builds.Reverse();
                            foreach (var build in builds)
                            {
                                var itemVer = new ComboBoxItem();
                                itemVer.Content = build.version;
                                itemVer.DataContext = build;
                                VersionItems.Add(itemVer);
                            }
                        }
                    }
                }
            }

            private void FillPackages()
            {
                try
                {
                    Packages.Clear();
                    DisplayPackages = false;
                    if (VersionItems.Count > VersionSelectedIndex && VersionSelectedIndex != -1)
                    {
                        var moditem = VersionItems[VersionSelectedIndex].DataContext as Mod;
                        if (moditem != null && (moditem.type == ModType.mod || moditem.type == ModType.tc))
                        {
                            foreach (var pkg in moditem.packages)
                            {
                                var displayPkg = new ModPackage();
                                displayPkg.name = pkg.name;
                                //Pre-selected for required 
                                displayPkg.isSelected = pkg.status == "required" ? true : false;
                                //Only allow to select/deselect non required packages
                                displayPkg.isEnabled = !displayPkg.isSelected;
                                //Mark previusly enabled packages as enabled
                                if (Dependency.packages != null && Dependency.id == moditem.id && Dependency.packages.IndexOf(pkg.name) != -1)
                                {
                                    displayPkg.isSelected = true;
                                }
                                Packages.Add(displayPkg);
                                DisplayPackages = true;
                            }
                        }
                    }
                    if(Packages.Count() > 1)
                        Packages = Packages.OrderByDescending(x => x.isEnabled).ToList();
                }catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "EditorDependencyItem.FillPackages()", ex);
                }
            }

            //Maps a dependency version string to the matching index in the version-type combobox
            //(0 == exact, 1 >=, 2 ~, 3 <=, 4 >, 5 <).
            private static int OperatorTypeIndexFromVersion(string? version)
            {
                if (version == null) return 0;
                if (version.Contains("~")) return 2;
                if (version.Contains(">=")) return 1;
                if (version.Contains("<=")) return 3;
                if (version.Contains(">")) return 4;
                if (version.Contains("<")) return 5;
                return 0;
            }

            private static string StripVersionOperators(string version)
            {
                return version.Trim().Replace(">=", "").Replace("<=", "")
                                     .Replace(">", "").Replace("<", "").Replace("~", "");
            }

            internal void DeleteDependency()
            {
                EditorPackageItem.DeleteDependency(this);
            }

            internal void ReloadDependency()
            {
                EditorPackageItem.ReloadDependency(this);
            }

            public ModDependency? GetDependency()
            {
                try
                {
                    //If we are reading a missing dep, return original data
                    if (!ModItems[ModSelectedIndex].IsEnabled)
                    {
                        return Dependency;
                    }

                    //If the version is a preserved complex constraint and the user hasn't touched anything, round-trip it unchanged
                    if (preserveOriginalConstraint)
                    {
                        return Dependency;
                    }

                    var depId = ModItems[ModSelectedIndex].Tag as string;
                    var depVersion = VersionItems[VersionSelectedIndex].Content as string;

                    if(depId != "-1" && depId != null)
                    {
                        Dependency.id = depId;
                        var versionType = VersionTypeIndex switch
                        {
                            1 => ">=",
                            2 => "~",
                            3 => "<=",
                            4 => ">",
                            5 => "<",
                            _ => string.Empty
                        };
                        Dependency.version = depVersion != "Any" ? versionType+depVersion : null;
                        var newPkgs = new List<string>();
                        foreach (var pkg in Packages)
                        {
                            if (pkg.isSelected && pkg.isEnabled)
                                newPkgs.Add(pkg.name);
                        }
                        Dependency.packages = newPkgs;
                        return Dependency;
                    }
                    else
                    {
                        return null;
                    }
                }catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "EditorDependencyItem.GetDependency()", ex);
                    return Dependency;
                }
            }
        }

        /*************************************************** PACKAGE ITEM ***********************************************************/
        public partial class EditorModPackageItem : ObservableObject
        {
            public ModPackage Package { get; set; }

            [ObservableProperty]
            internal int packageStatusIndex = 0;

            [ObservableProperty]
            internal string packageNotes = string.Empty;

            [ObservableProperty]
            internal bool isEnabled = true;

            [ObservableProperty]
            internal bool packVP = false;

            [ObservableProperty]
            internal string diskSpace = string.Empty;

            [ObservableProperty]
            internal ObservableCollection<EditorDependencyItem> dependencyItems = new ObservableCollection<EditorDependencyItem>();

            public DevModPkgMgrViewModel PkgMgr { get; set; }

            public EditorModPackageItem(ModPackage pkg, DevModPkgMgrViewModel pkgmgr) 
            {
                Package = pkg;
                PkgMgr = pkgmgr;
                IsEnabled = pkg.isEnabled;
                PackVP = pkg.isVp;
                PackageNotes = pkg.notes != null? pkg.notes : string.Empty;
                if (pkg.dependencies != null && PkgMgr.editor != null)
                {
                    foreach(var dep in pkg.dependencies)
                    {
                        DependencyItems.Add(new EditorDependencyItem(dep, this, PkgMgr.editor.ActiveVersion.id, PkgMgr.editor.ActiveVersion.parent));
                    }
                }
                switch(pkg.status)
                {
                    case "required": PackageStatusIndex = 0;
                        break;
                    case "recommended": PackageStatusIndex = 1;
                        break; ;
                    case "optional": PackageStatusIndex = 2; 
                        break;
                }

                UpdateFolderSize();
            }

            private void UpdateFolderSize()
            {
                DiskSpace = "";
                Task.Run(() => {
                    try
                    {
                        if (PkgMgr.editor != null)
                        {
                            if (Directory.Exists(PkgMgr.editor.ActiveVersion.fullPath + Path.DirectorySeparatorChar + Package.folder))
                            {
                                long sizeInBytes = Directory.EnumerateFiles(PkgMgr.editor.ActiveVersion.fullPath + Path.DirectorySeparatorChar + Package.folder, "*", SearchOption.AllDirectories).Sum(fileInfo => new FileInfo(fileInfo).Length);
                                DiskSpace = KnUtils.FormatBytes(sizeInBytes);
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        Log.Add(Log.LogSeverity.Warning, "EditorModPackageItem.UpdateFolderSize", ex);
                    }
                });
            }

            public ModPackage GetPackage() 
            {
                UpdateFolderSize();
                Package.isEnabled = IsEnabled;
                Package.isVp = PackVP;
                Package.notes = PackageNotes;
                switch (PackageStatusIndex)
                {
                    case 0:
                        Package.status = "required";
                        break;
                    case 1:
                        Package.status = "recommended";
                        break; ;
                    case 2:
                        Package.status = "optional";
                        break;
                }
                if(DependencyItems.Any())
                {
                    var deps = new List<ModDependency>();
                    foreach (var dep in DependencyItems)
                    {
                        var newDep = dep.GetDependency();
                        if(newDep != null)
                            deps.Add(newDep);
                    }
                    Package.dependencies = deps.ToArray();
                }
                else
                {
                    Package.dependencies = null;
                }
                return Package;
            }

            public void DeleteDependency(EditorDependencyItem dependency)
            {
                try
                {
                    DependencyItems.Remove(dependency);
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "EditorModPackageItem.DeleteDependency()", ex);
                }
            }

            public void ReloadDependency(EditorDependencyItem dependency)
            {
                int oldIndex = DependencyItems.IndexOf(dependency);
                if (oldIndex != -1)
                {
                    DeleteDependency(dependency);
                    try
                    {
                        if (PkgMgr.editor != null && oldIndex <= DependencyItems.Count())
                            DependencyItems.Insert(oldIndex, new EditorDependencyItem(dependency.Dependency, this, PkgMgr.editor.ActiveVersion.id, PkgMgr.editor.ActiveVersion.parent));
                    }
                    catch (Exception ex)
                    {
                        Log.Add(Log.LogSeverity.Error, "EditorModPackageItem.ReloadDependency()", ex);
                    }
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "EditorModPackageItem.ReloadDependency()", "Got -1 on DeprendencyItems.IndexOf(dependency) for some reason.");
                }

            }

            internal void DeleteModPkg()
            {
                try
                {
                    PkgMgr.DeletePkg(this);
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "EditorModPackageItem.DeletePkg()", ex);
                }
            }

            internal void AddModDependency()
            {
                try
                {
                    if(PkgMgr.editor != null)
                        DependencyItems.Add(new EditorDependencyItem(new ModDependency(), this, PkgMgr.editor.ActiveVersion.id, PkgMgr.editor.ActiveVersion.parent));
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "EditorModPackageItem.AddDependency()", ex);
                }
            }

            internal void OpenModPackageFolder()
            {
                try
                {
                    if(PkgMgr.editor != null)
                        KnUtils.OpenFolder(PkgMgr.editor.ActiveVersion.fullPath + Path.DirectorySeparatorChar + Package.folder);
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "EditorModPackageItem.OpenPackageFolder()", ex);
                }
            }
        }


        /************************************************************ FLAG ITEM *********************************************************************/
        public class EditorFlagItem
        {
            public string Flag { get; set; }

            public string FlagName { get; set; }

            internal DevModPkgMgrViewModel PkgMgr { get; set; }

            public bool IsThisMod { get; set; }

            public EditorFlagItem(string flag, DevModPkgMgrViewModel pkgmgr, bool thisMod)
            {
                Flag = flag;
                PkgMgr = pkgmgr;
                IsThisMod = thisMod;
                var mod = Knossos.GetInstalledModList(flag);
                if(mod.Any())
                {
                    FlagName = mod[0].title + "  [ " + flag + " ]";
                }
                else
                {
                    var build = Knossos.GetInstalledBuildsList(flag);
                    if (build.Any())
                    {
                        FlagName = build[0].title + "  [ " + flag + " ]";
                    }
                    else
                    {
                        FlagName = flag;
                    }
                }
            }

            internal void FlagUP()
            {
                PkgMgr.FlagUP(this);
            }
            internal void FlagDown()
            {
                PkgMgr.FlagDown(this);
            }
        }

        /**************************************************** PACKAGE MANAGER *************************************************************/

        public DevModEditorViewModel? editor;
        [ObservableProperty]
        internal ObservableCollection<EditorFlagItem> editorFlagItems = new ObservableCollection<EditorFlagItem>();
        [ObservableProperty]
        internal ObservableCollection<EditorModPackageItem> editorPackageItems = new ObservableCollection<EditorModPackageItem>();

        internal string newPackageName = string.Empty;

        internal string NewPackageName
        {
            get
            {
                return newPackageName;
            }
            set
            {
                if (value != newPackageName)
                {
                    SetProperty(ref newPackageName, Regex.Replace(value, "[^a-zA-Z0-9-__ ]+", "", RegexOptions.Compiled));
                    NewPackageFolder = newPackageName.Replace(" ","_");
                }
            }
        }

        [ObservableProperty]
        internal string newPackageFolder = string.Empty;

        public DevModPkgMgrViewModel()
        {
        }

        public DevModPkgMgrViewModel(DevModEditorViewModel editor)
        {
            this.editor = editor;
            foreach (var item in editor.ActiveVersion.modFlag)
            {
                EditorFlagItems.Add(new EditorFlagItem(item, this, item == editor.ActiveVersion.id));
            }

            foreach (var item in editor.ActiveVersion.packages)
            {
                EditorPackageItems.Add(new EditorModPackageItem(item, this));
            }
        }

        /* Button Commands */
        internal void Save()
        {
            if (editor == null)
                return;
            var newPkgs = new List<ModPackage>();
            //Get all listed packages with updated data
            foreach (EditorModPackageItem pkg in EditorPackageItems)
            {
                var newPkg = pkg.GetPackage();
                newPkgs.Add(newPkg);
                if (newPkg.dependencies != null)
                {
                    //Check if we need to add new mods ids to the flag list
                    foreach (ModDependency dep in newPkg.dependencies)
                    {
                        //Discard FSO builds
                        var deps = Knossos.GetInstalledBuildsList(dep.id);
                        if (!deps.Any())
                        {
                            var isAdded = EditorFlagItems.FirstOrDefault(f => f.Flag == dep.id);
                            if (isAdded == null)
                            {
                                EditorFlagItems.Add(new EditorFlagItem(dep.id, this, false));
                            }
                        }
                    }
                }
            }
            //Check all mod flags to see if we need to remove one and re-create array
            var flagList = new List<string>();
            foreach (EditorFlagItem item in EditorFlagItems.ToList())
            {
                if (!item.IsThisMod)
                {
                    bool found = false;
                    foreach (var pkg in newPkgs)
                    {
                        if (pkg.dependencies != null)
                        {
                            foreach (var dep in pkg.dependencies)
                            {
                                if (dep.id == item.Flag)
                                    found = true;
                            }
                        }
                    }
                    if (!found)
                    {
                        EditorFlagItems.Remove(item);
                    }
                    else
                    {
                        flagList.Add(item.Flag);
                    }
                }
                else
                {
                    flagList.Add(item.Flag);
                }
            }
            //Update mod
            editor.ActiveVersion.packages = newPkgs;
            editor.ActiveVersion.modFlag = flagList;
            editor.ActiveVersion.SaveJson();
        }
        internal void CreatePackage()
        {
            try
            {
                if (NewPackageFolder.Trim() != string.Empty && NewPackageName.Trim() != string.Empty)
                {
                    if(NewPackageFolder.ToLower() == "kn_images" || NewPackageFolder.ToLower() == "kn_upload")
                    {
                        MessageBox.Show(MainWindow.instance, NewPackageName + " is a reserved package folder name and cant be used", "Error creating new package", MessageBox.MessageBoxButtons.OK);
                        return;
                    }
                    Directory.CreateDirectory(editor!.ActiveVersion.fullPath + Path.DirectorySeparatorChar + NewPackageFolder + Path.DirectorySeparatorChar + "data");
                    var newPkg = new ModPackage();
                    newPkg.folder = NewPackageFolder;
                    newPkg.name = NewPackageName;
                    EditorPackageItems.Add(new EditorModPackageItem(newPkg, this));
                    NewPackageName = string.Empty;
                    NewPackageFolder = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModPkgMgrViewModel.CreatePackage()", ex);
            }
        }

        internal async void DeletePkg(EditorModPackageItem editorPackageItem)
        {
            try
            {
                if (editor != null)
                {
                    var folderPath = editor.ActiveVersion.fullPath + Path.DirectorySeparatorChar + editorPackageItem.Package.folder;
                    var resp = await MessageBox.Show(MainWindow.instance!, "This will delete the package: " + editorPackageItem.Package.name + " and ALL FILES on this folder: " + folderPath + " of the mod and version " + editor.ActiveVersion + "\n Do you really want to do this? This action cannot be undone.","Confirm package deletion",MessageBox.MessageBoxButtons.YesNo);
                    if(resp == MessageBox.MessageBoxResult.Yes)
                    {
                        try
                        {
                            Directory.Delete(folderPath, true);
                        }
                        catch (Exception ex)
                        {
                            Log.Add(Log.LogSeverity.Warning, "DevModPkgMgrViewModel.DeletePkg()", ex);
                        }
                        EditorPackageItems.Remove(editorPackageItem);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModPkgMgrViewModel.DeletePkg()", ex);
            }
        }

        internal void FlagUP(EditorFlagItem flagItem)
        {
            int index = EditorFlagItems.IndexOf(flagItem);
            if (index > 0)
            {
                EditorFlagItems.Move(index, index - 1);
            }
        }

        internal void FlagDown(EditorFlagItem flagItem)
        {
            int index = EditorFlagItems.IndexOf(flagItem);
            if (index + 1  < EditorFlagItems.Count())
            {
                EditorFlagItems.Move(index, index + 1);
            }
        }
    }
}
