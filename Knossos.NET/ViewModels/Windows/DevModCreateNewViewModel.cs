using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    /// <summary>
    /// Create New Model Windows View Model
    /// For DevMode
    /// </summary>
    public partial class DevModCreateNewViewModel : ViewModelBase
    {
        internal string modId = string.Empty;
        internal string ModId
        {
            get { return modId; }
            set 
            {
                SetProperty(ref modId, Regex.Replace(value.Replace(" ", "_"), "[^a-zA-Z0-9_]+", "", RegexOptions.Compiled));

                if (modName == lastmodName){
                    modIDManual = true;
                }
            }
        }

        // These two variables help track if the user is manually setting the mod ID.
        internal string lastmodName = string.Empty;
        internal bool modIDManual = false;

        internal string modName = string.Empty;
        internal string ModName
        {
            get { return modName; }
            set
            {
                SetProperty(ref modName, value);

                if (modName != lastmodName && !modIDManual){
                    ModId = modName;
                }

                lastmodName = modName;
            }
        }
        [ObservableProperty]
        internal string modVersion = "1.0.0";
        [ObservableProperty]
        internal int typeSelectedIndex = -1;
        [ObservableProperty]
        internal ObservableCollection<ComboBoxItem> parentComboBoxItems = new ObservableCollection<ComboBoxItem>();
        [ObservableProperty]
        internal int parentSelectedIndex = -1;
        private KnossosWindow? dialog;
        internal bool LoggedInNebula
        {
            get { return Nebula.userIsLoggedIn; }
        }

        public DevModCreateNewViewModel()
        { }

        public DevModCreateNewViewModel(KnossosWindow dialog) 
        {
            this.dialog = dialog;
            var tcs = Knossos.GetInstalledModList(null).Where(x=>x.type == Models.ModType.tc);

            if(tcs != null && tcs.Any())
            {
                foreach(var tc in tcs)
                {
                    var item = new ComboBoxItem();
                    item.Content = tc.title + " [" + tc.id + "]";
                    item.Tag = tc.id;
                    item.DataContext = tc;
                    var exist = ParentComboBoxItems.FirstOrDefault(x => x.Tag != null && x.Tag.ToString() == tc.id);
                    if(exist != null)
                    {
                        //Update in case the name is diferent, they SHOULD be ordered by version already.
                        exist.Content = item.Content;
                        exist.DataContext = tc;
                    }
                    else
                    {
                        parentComboBoxItems.Add(item);
                    }
                }
            }
            ParentSelectedIndex = 0;
            TypeSelectedIndex = 0;
        }

        private async Task<bool> Verify()
        {
            bool displayWarningID = false;
            //Is library set?
            if(Knossos.GetKnossosLibraryPath() == null )
            {
                await MessageBox.Show(MainWindow.instance, "KnossosNET library path is not set. Go to the settings tab and set the KnossosNET library location.", "Validation error", MessageBox.MessageBoxButtons.OK);
                return false;
            }
            //Version
            var sm = new SemanticVersion(ModVersion);
            if(sm == null || sm.ToString() == "0.0.0")
            {
                await MessageBox.Show(MainWindow.instance, ModVersion + " is not a valid semantic version", "Validation error",MessageBox.MessageBoxButtons.OK);
                return false;
            }
            //Name
            if(ModName.Replace(" ", "").Length <= 1)
            {
                await MessageBox.Show(MainWindow.instance, "Mod name cant be empty or a single character: "+ModName, "Validation error", MessageBox.MessageBoxButtons.OK);
                return false;
            }
            //ID
            if (ModId.Replace(" ", "").Length <= 2)
            {
                await MessageBox.Show(MainWindow.instance, "Mod ID cant be empty or be less than 3 characters: " + ModId, "Validation error", MessageBox.MessageBoxButtons.OK);
                return false;
            }
            if (ModId.ToLower() == "tools" || ModId.ToLower() == "fso")
            {
                await MessageBox.Show(MainWindow.instance, "Mod ID: " + ModId+" is a reserved value", "Validation error", MessageBox.MessageBoxButtons.OK);
                return false;
            }
            if (await Nebula.IsModIdInNebula(ModId))
            {
                var editableIds = await Nebula.GetEditableModIDs();
                if (editableIds != null && editableIds.Contains(ModId))
                {
                    displayWarningID = true;
                }
                else
                {
                    await MessageBox.Show(MainWindow.instance, "Mod id already exist in Nebula: " + ModId, "Validation error", MessageBox.MessageBoxButtons.OK);
                    return false;
                }
            }
            if (Knossos.GetInstalledModList(ModId).Any() || Knossos.GetInstalledBuildsList(ModId).Any())
            {
                await MessageBox.Show(MainWindow.instance, "Mod ID already exist locally: " + ModId, "Validation error", MessageBox.MessageBoxButtons.OK);
                return false;
            }
            //If modtype = Mod it has to have a parent mod
            if (TypeSelectedIndex == 0)
            {
                if(ParentSelectedIndex >= 0 && ParentComboBoxItems.Count() > ParentSelectedIndex)
                {
                    if (ParentComboBoxItems[ParentSelectedIndex].DataContext == null)
                    {
                        await MessageBox.Show(MainWindow.instance, "Mod type: MOD requires to select a parent mod.", "Validation error", MessageBox.MessageBoxButtons.OK);
                        return false;
                    }
                }
                else
                {
                    await MessageBox.Show(MainWindow.instance, "Mod type: MOD requires to select a parent mod.", "Validation error", MessageBox.MessageBoxButtons.OK);
                    return false;
                }
            }
            //Folder
            if(TypeSelectedIndex == 0) //Mod
            {
                var parent = ParentComboBoxItems[ParentSelectedIndex].DataContext as Mod;
                // Retail FS2 mods are stored on that same folder
                if (parent!.id.ToLower() == "fs2")
                {
                    if(Directory.Exists(parent.fullPath+Path.DirectorySeparatorChar+ModId+"-"+ModVersion) || File.Exists(parent.fullPath + Path.DirectorySeparatorChar + ModId + "-" + ModVersion))
                    {
                        await MessageBox.Show(MainWindow.instance, "Folder or File already exists: "+ parent.fullPath + Path.DirectorySeparatorChar + ModId + "-" + ModVersion, "Validation error", MessageBox.MessageBoxButtons.OK);
                        return false;
                    }
                }
                else
                {
                    var parentParentFolder = new DirectoryInfo(parent.fullPath).Parent;
                    if (parentParentFolder != null)
                    {
                        if (Directory.Exists(parentParentFolder.FullName + Path.DirectorySeparatorChar + ModId + "-" + ModVersion) || File.Exists(parentParentFolder.FullName + Path.DirectorySeparatorChar + ModId + "-" + ModVersion))
                        {
                            await MessageBox.Show(MainWindow.instance, "Folder or File already exists: " + parentParentFolder.FullName + Path.DirectorySeparatorChar + ModId + "-" + ModVersion, "Validation error", MessageBox.MessageBoxButtons.OK);
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            else
            {
                if (TypeSelectedIndex == 1) //Total Conversion
                {
                    if(Directory.Exists(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + ModId ) || File.Exists(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + ModId))
                    {
                        await MessageBox.Show(MainWindow.instance, "Folder or File already exists: " + Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + ModId, "Validation error", MessageBox.MessageBoxButtons.OK);
                        return false;
                    }
                }
                else
                {
                    //FSO Build
                    if (Directory.Exists(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar + ModId + "-" + ModVersion) || File.Exists(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar + ModId + "-" + ModVersion))
                    {
                        await MessageBox.Show(MainWindow.instance, "Folder or File already exists: " + Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar + ModId + "-" + ModVersion, "Validation error", MessageBox.MessageBoxButtons.OK);
                        return false;
                    }
                }
            }

            if(displayWarningID)
            {
                var res = await MessageBox.Show(MainWindow.instance, "This mod id already exists in Nebula, if there are uploaded versions of this mod you WILL override metadata for those versions if you try to upload something.", "ModID exists and you have write access to it", MessageBox.MessageBoxButtons.ContinueCancel);
                return res == MessageBox.MessageBoxResult.Continue;
            }

            return true;
        }

        internal async void CreateMod()
        {
            try
            {
                if (!await Verify())
                    return;

                //Create folder
                var folderPath = string.Empty;
                if (TypeSelectedIndex == 0) //Mod
                {
                    var parent = ParentComboBoxItems[ParentSelectedIndex].DataContext as Mod;
                    // Retail FS2 mods are stored on that same folder
                    if (parent!.id.ToLower() == "fs2")
                    {
                        folderPath = parent.fullPath + Path.DirectorySeparatorChar + ModId + "-" + ModVersion;
                    }
                    else
                    {
                        var parentParentFolder = new DirectoryInfo(parent.fullPath).Parent;
                        if (parentParentFolder != null)
                        {
                            folderPath = parentParentFolder.FullName + Path.DirectorySeparatorChar + ModId + "-" + ModVersion;
                        }
                    }
                }
                else
                {
                    if (TypeSelectedIndex == 1) //Total Conversion
                    {
                        folderPath = Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + ModId + Path.DirectorySeparatorChar + ModId + "-" + ModVersion;
                    }
                    else
                    {
                        //FSO Build
                        folderPath = Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar + ModId + "-" + ModVersion;
                    }
                }
                Directory.CreateDirectory(folderPath);

                //Create Json
                var mod = new Mod();
                mod.id = ModId;
                mod.fullPath = folderPath;
                mod.title = ModName;
                mod.version = ModVersion;
                mod.folderName = ModId + "-" + ModVersion;
                mod.devMode = true;
                mod.isPrivate = true;
                mod.installed = true;
                mod.modSource = ModSource.nebula;
                switch (TypeSelectedIndex)
                {
                    case 0: //Mod
                        var parentMod = ParentComboBoxItems[ParentSelectedIndex].DataContext as Mod;
                        mod.type = ModType.mod;
                        mod.parent = parentMod!.id;
                        break;
                    case 1: //TC
                        mod.type = ModType.tc;
                        break;
                    case 2: //Build
                        mod.type = ModType.engine;
                        mod.stability = "stable";
                        break;
                }
                mod.modFlag.Add(mod.id);
                mod.SaveJson();
                if(mod.type == ModType.engine)
                {
                    var build = new FsoBuild(mod);
                    Knossos.AddBuild(build);
                    FsoBuildsViewModel.Instance!.AddBuildToUi(build);
                }
                else
                {
                    Knossos.AddMod(mod);
                    MainViewModel.Instance!.AddInstalledMod(mod);
                }
                MainViewModel.Instance!.AddDevMod(mod);
                if (dialog != null)
                    dialog.Close();
            }
            catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModCreateNewViewModel.CreateMod", ex);
            }
        }
    }
}
