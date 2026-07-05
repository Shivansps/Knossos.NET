using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Knossos.NET.ViewModels
{
    public partial class DevBuildPkgMgrViewModel : ViewModelBase
    {
        /*************************************************** PACKAGE EXECUTABLE *****************************************************/
        public partial class PackageExecItem : ObservableObject
        {
            private EditorPackageItem editorPackageItem;
            private ModExecutable modExecutable;
            [ObservableProperty]
            internal string file = string.Empty;
            [ObservableProperty]
            internal int labelSelectedIndex = 0;
            [ObservableProperty]
            internal ObservableCollection<ComboBoxItem> labels = new ObservableCollection<ComboBoxItem>();

            public PackageExecItem(ModExecutable modExec, EditorPackageItem editorPackageItem) 
            { 
                this.editorPackageItem = editorPackageItem;
                modExecutable = modExec;
                File = modExec.file != null ? modExec.file : string.Empty;
                FillLabels();
            }

            public ModExecutable? GetExec()
            {
                try
                {
                    modExecutable.file = File;
                    modExecutable.label = FsoBuild.GetLabelString((FsoExecType)Labels[LabelSelectedIndex].Content!);
                    return modExecutable;
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "PackageExecItem.GetExec()", ex);
                    return modExecutable;
                }
            }

            internal void DeleteExecutable()
            {
                editorPackageItem.DeleteExecutable(this);
            }

            internal async void ChangeExecutable()
            {
                try
                {
                    FilePickerOpenOptions options = new FilePickerOpenOptions();
                    if (editorPackageItem.PkgMgr.editor != null)
                    {
                        options.SuggestedStartLocation = await KnUtils.GetTopLevel().StorageProvider.TryGetFolderFromPathAsync(Path.Combine(editorPackageItem.PkgMgr.editor.ActiveVersion.fullPath, editorPackageItem.Package.folder != null ? editorPackageItem.Package.folder : string.Empty));
                    }
                    options.AllowMultiple = false;

                    var result = await KnUtils.GetTopLevel().StorageProvider.OpenFilePickerAsync(options);

                    if (result != null && result.Count > 0)
                    {
                        File = Path.GetRelativePath(Path.Combine(editorPackageItem.PkgMgr.editor!.ActiveVersion.fullPath, editorPackageItem.Package.folder != null ? editorPackageItem.Package.folder : ""), result[0].Path.LocalPath.ToString()).Replace(@"\", @"/");
                    }
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "PackageExecItem.ChangeExecutable()", ex);
                }
            }

            private void FillLabels()
            {
                try
                {
                    foreach (var label in Enum.GetValues(typeof(FsoExecType)))
                    {
                        if ((FsoExecType)label != FsoExecType.Unknown) //Default unsupported
                        {
                            var item = new ComboBoxItem();
                            item.Content = label;
                            Labels.Add(item);
                        }
                    }
                    //Select
                    if (modExecutable.label != null)
                    {
                        var label = FsoBuild.GetExecType(modExecutable.label);
                        foreach (var item in Labels)
                        {
                            if (label == (FsoExecType)item.Content!)
                            {
                                LabelSelectedIndex = Labels.IndexOf(item);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "PackageExecItem.FillLabels()", ex);
                }
            }
        }

        /*************************************************** PACKAGE ITEM ***********************************************************/
        public partial class EditorPackageItem : ObservableObject
        {
            public ModPackage Package { get; set; }

            [ObservableProperty]
            internal int packageArchIndex = 0;

            [ObservableProperty]
            internal int packageOsIndex = 0;

            [ObservableProperty]
            internal string packageNotes = string.Empty;

            [ObservableProperty]
            internal ObservableCollection<PackageExecItem> executables = new ObservableCollection<PackageExecItem>();

            [ObservableProperty]
            internal ObservableCollection<ComboBoxItem> arch = new ObservableCollection<ComboBoxItem>();

            [ObservableProperty]
            internal ObservableCollection<ComboBoxItem> os = new ObservableCollection<ComboBoxItem>();

            public DevBuildPkgMgrViewModel PkgMgr { get; set; }

            public EditorPackageItem(ModPackage pkg, DevBuildPkgMgrViewModel pkgmgr)
            {
                Package = pkg;
                PkgMgr = pkgmgr;
                PackageNotes = pkg.notes != null ? pkg.notes : string.Empty;
                FillComboBoxes();
                if(pkg.executables != null)
                {
                    foreach(var exec in pkg.executables)
                    {
                        executables.Add(new PackageExecItem(exec,this));
                    }
                }
            }

            internal void DeleteExecutable(PackageExecItem packageExecItem)
            {
                Executables.Remove(packageExecItem);
            }

            private void FillComboBoxes()
            {
                try
                {
                    //CPU Arch
                    foreach (var cpuArch in Enum.GetValues(typeof(FsoExecArch)))
                    {
                        if ((FsoExecArch)cpuArch != FsoExecArch.other) //Default unsupported
                        {
                            var item = new ComboBoxItem();
                            item.Content = cpuArch;
                            Arch.Add(item);
                        }
                    }
                    //OS
                    foreach (var os in Enum.GetValues(typeof(FsoExecEnvironment)))
                    {
                        if ((FsoExecEnvironment)os != FsoExecEnvironment.Unknown) //Default unsupported
                        {
                            var item = new ComboBoxItem();
                            item.Content = os;
                            Os.Add(item);
                        }
                    }
                    //Select
                    if (Package.environment != null)
                    {
                        var props = FsoBuild.FillProperties(Package.environment);
                        var arch = FsoBuild.GetExecArch(props);
                        var os = FsoBuild.GetExecEnvironment(Package.environment);
                        foreach (var item in Arch)
                        {
                            if (arch == (FsoExecArch)item.Content!)
                            {
                                PackageArchIndex = Arch.IndexOf(item);
                            }
                        }
                        foreach (var item in Os)
                        {
                            if (os == (FsoExecEnvironment)item.Content!)
                            {
                                PackageOsIndex = Os.IndexOf(item);
                            }
                        }
                    }
                }catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "EditorPackageItem.FillComboBoxes()", ex);
                }
            }

            public ModPackage GetPackage()
            {
                try
                {
                    Package.notes = PackageNotes;
                    Package.status = "required";
                    Package.dependencies = null;
                    Package.isVp = false;
                    Package.checkNotes = null;
                    Package.environment = FsoBuild.GetEnviromentString((FsoExecArch)Arch[PackageArchIndex].Content!, (FsoExecEnvironment)Os[PackageOsIndex].Content!);
                    var newExecs = new List<ModExecutable>();
                    foreach (var e in Executables)
                    {
                        var exec = e.GetExec();
                        exec!.properties = FsoBuild.FillProperties(Package.environment);
                        newExecs.Add(exec!);
                    }
                    Package.executables = newExecs;
                }catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "EditorPackageItem.GetPackage()", ex);
                }
                return Package;
            }

            internal void DeletePkg()
            {
                try
                {
                    PkgMgr.DeletePkg(this);
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "EditorPackageItem.DeletePkg()", ex);
                }
            }

            internal void OpenPackageFolder()
            {
                try
                {
                    if (PkgMgr.editor != null)
                        KnUtils.OpenFolder(Path.Combine(PkgMgr.editor.ActiveVersion.fullPath, Package.folder != null ? Package.folder : string.Empty));
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "EditorPackageItem.OpenPackageFolder()", ex);
                }
            }

            internal async void AddExecutable()
            {
                try
                {
                    FilePickerOpenOptions options = new FilePickerOpenOptions();
                    if (PkgMgr.editor != null)
                    {
                        options.SuggestedStartLocation = await KnUtils.GetTopLevel().StorageProvider.TryGetFolderFromPathAsync(Path.Combine(PkgMgr.editor.ActiveVersion.fullPath, Package.folder != null ? Package.folder : string.Empty));
                    }
                    options.AllowMultiple = false;

                    var result = await KnUtils.GetTopLevel().StorageProvider.OpenFilePickerAsync(options);

                    if (result != null && result.Count > 0)
                    {
                        var fullPath = result[0].Path.LocalPath.ToString();
                        var newFile = Path.Combine(Path.GetDirectoryName(fullPath)!, FsoBuild.GetRealExeName(Path.GetDirectoryName(fullPath)!, Path.GetFileName(fullPath)));
                        var newExec = new ModExecutable();
                        newExec.file = Path.GetRelativePath(Path.Combine(PkgMgr.editor!.ActiveVersion.fullPath, Package.folder != null ? Package.folder : "" ), newFile).Replace(@"\",@"/");
                        Executables.Add(new PackageExecItem(newExec, this));
                    }
                }catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "EditorPackageItem.AddExecutable()", ex);
                }
            }
        }

        public DevModEditorViewModel? editor;
        [ObservableProperty]
        internal ObservableCollection<EditorPackageItem> editorPackageItems = new ObservableCollection<EditorPackageItem>();
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
                    NewPackageFolder = newPackageName.Replace(" ", "_");
                }
            }
        }

        [ObservableProperty]
        internal string newPackageFolder = string.Empty;

        public DevBuildPkgMgrViewModel()
        {

        }

        public DevBuildPkgMgrViewModel(DevModEditorViewModel devModEditorViewModel)
        {
            editor = devModEditorViewModel;
            foreach (var item in editor.ActiveVersion.packages)
            {
                EditorPackageItems.Add(new EditorPackageItem(item, this));
            }
        }

        /* Button Commands */
        internal async void Save()
        {
            try
            {
                if (editor == null)
                    return;
                var newPkgs = new List<ModPackage>();
                //Get all listed packages with updated data
                foreach (EditorPackageItem pkg in EditorPackageItems)
                {
                    var newPkg = pkg.GetPackage();
                    newPkgs.Add(newPkg);
                }
                //Update mod
                editor.ActiveVersion.packages = newPkgs;
                editor.ActiveVersion.SaveJson();
                //Update Build
                var list = Knossos.GetInstalledBuildsList(editor.ActiveVersion.id);
                if (list != null && list.Any())
                {
                    var build = list.FirstOrDefault(b => b.version == editor.ActiveVersion.version);
                    if (build != null)
                    {
                        build.UpdateBuildData(editor.ActiveVersion);
                        await Dispatcher.UIThread.InvokeAsync(() => FsoBuildsViewModel.Instance?.UpdateBuildUI(build), DispatcherPriority.Background);
                    }
                }
            }catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevBuildEditorViewModel.Save()", ex);
            }
        }

        internal void CreatePackage()
        {
            try
            {
                if (NewPackageFolder.Trim() != string.Empty && NewPackageName.Trim() != string.Empty)
                {
                    Directory.CreateDirectory(editor!.ActiveVersion.fullPath + Path.DirectorySeparatorChar + NewPackageFolder + Path.DirectorySeparatorChar + NewPackageFolder);
                    File.Create(editor!.ActiveVersion.fullPath + Path.DirectorySeparatorChar + NewPackageFolder + Path.DirectorySeparatorChar + "do_not_copy_the_build_files_here").Close();
                    File.Create(editor!.ActiveVersion.fullPath + Path.DirectorySeparatorChar + NewPackageFolder + Path.DirectorySeparatorChar + NewPackageFolder + Path.DirectorySeparatorChar + "copy_the_build_files_here").Close();
                    var newPkg = new ModPackage();
                    newPkg.folder = NewPackageFolder;
                    newPkg.name = NewPackageName;
                    EditorPackageItems.Add(new EditorPackageItem(newPkg, this));
                    NewPackageName = string.Empty;
                    NewPackageFolder = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevBuildEditorViewModel.CreatePackage()", ex);
            }
        }

        internal async void DeletePkg(EditorPackageItem editorPackageItem)
        {
            try
            {
                if (editor != null)
                {
                    var folderPath = Path.Combine(editor.ActiveVersion.fullPath, editorPackageItem.Package.folder != null ? editorPackageItem.Package.folder : string.Empty);
                    var resp = await MessageBox.Show(MainWindow.instance!, "This will delete the package: " + editorPackageItem.Package.name + " and ALL FILES on this folder: " + folderPath + " of the build and version " + editor.ActiveVersion + "\n Do you really want to do this? This action cannot be undone.", "Confirm package deletion", MessageBox.MessageBoxButtons.YesNo);
                    if (resp == MessageBox.MessageBoxResult.Yes)
                    {
                        Directory.Delete(folderPath, true);
                        EditorPackageItems.Remove(editorPackageItem);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevBuildEditorViewModel.DeletePkg()", ex);
            }
        }
    }
}
