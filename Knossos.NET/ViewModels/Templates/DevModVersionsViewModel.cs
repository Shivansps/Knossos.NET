using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    public partial class DevModVersionsViewModel : ViewModelBase
    {
        [ObservableProperty]
        internal List<Mod> mods = new List<Mod>();
        [ObservableProperty]
        internal bool buttonsEnabled = true;
        [ObservableProperty]
        internal string visibilityButtonText = "Make it Public";
        [ObservableProperty]
        internal bool isDevEnvVersion = false;
        [ObservableProperty]
        internal bool modHasDevEnvVersion = false;

        internal int selectedIndex = -1;
        internal int SelectedIndex
        {
            get
            {
                return selectedIndex;
            }
            set
            {
                if (selectedIndex != value)
                {
                    SetProperty(ref selectedIndex, value);
                    selectedIndex = value;
                    newVersion = string.Empty;
                    if (selectedIndex < Mods.Count && selectedIndex >= 0 && editor != null)
                    {
                        editor.ChangeActiveVersion(Mods[value]);
                        if (Mods[value].version.Contains("-devenv",StringComparison.OrdinalIgnoreCase))
                        {
                            IsDevEnvVersion = true;
                        }
                        else
                        { 
                            IsDevEnvVersion = false; 
                        }
                    }
                }
            }
        }

        private DevModEditorViewModel? editor;

        internal string CurrentVersion 
        {
            get 
            {
                if (editor != null)
                    return editor.ActiveVersion.version;
                else
                    return "1.0.0-Default";
            }
        }

        internal string newVersion = string.Empty;
        internal string NewVersion
        {
            get 
            {
                if (newVersion != string.Empty)
                    return newVersion;
                else
                {
                    if (!IsDevEnvVersion || Mods.Count() <= 1 || Mods.Count() <= (selectedIndex - 1))
                        return CurrentVersion;
                    else
                        return Mods[SelectedIndex - 1].version;
                }
            }
            set
            {
                if(newVersion != value)
                {
                    newVersion = value;
                }
            }
        }


        public DevModVersionsViewModel() 
        {
        }

        public DevModVersionsViewModel(DevModEditorViewModel editor)
        {
            this.editor = editor;
            Mods = editor.GetModList();
            foreach (var m in Mods)
            {
                if (editor.ActiveVersion == m)
                {
                    selectedIndex = Mods.IndexOf(m);
                    if (m.version.Contains("-devenv", StringComparison.OrdinalIgnoreCase))
                    {
                        IsDevEnvVersion = true;
                        ModHasDevEnvVersion = true;
                    }
                    else
                    {
                        IsDevEnvVersion = false;
                    }
                }
                else
                {
                    if (m.version.Contains("-devenv", StringComparison.OrdinalIgnoreCase))
                    {
                        ModHasDevEnvVersion = true;
                    }
                }
            }
            if (editor.ActiveVersion.isPrivate)
            {
                VisibilityButtonText = "Make it Public";
            }
            else
            {
                VisibilityButtonText = "Make it Private";
            }
        }

        private bool VerifyNewVersion()
        {
            //Verify Version String
            try
            {
                var sm = new SemanticVersion(NewVersion);
                if (sm != null)
                {
                    if (sm.ToString() == "0.0.0")
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Warning, "DevModVersionsViewModel.VerifyNewVersion()", ex);
                return false;
            }
            return true;
        }

        /* Button Commands */

        internal async void LoadVersionsFromNebula()
        {
            try
            {
                if (editor != null)
                {
                    var dialog = new ModInstallView();
                    dialog.DataContext = new ModInstallViewModel(editor.ActiveVersion, dialog, editor.ActiveVersion.version, true);
                    await dialog.ShowDialog<ModInstallView?>(MainWindow.instance!);
                }
            }
            catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "DevModVersionsViewModel.LoadVersionsFromNebula()", ex);
            }
        }

        internal async void CreateNewVersion(object control) 
        {
            if (editor == null)
                return;
            //Validate new version string
            if(!VerifyNewVersion())
            {
                await MessageBox.Show(MainWindow.instance!, "'" + NewVersion + "' is not a valid semantic version.", "Validation error", MessageBox.MessageBoxButtons.OK);
                return;
            }
            if (Mods.FirstOrDefault(m => m.version == NewVersion) != null)
            {
                await MessageBox.Show(MainWindow.instance!, "'" + NewVersion + "' already exists.", "Validation error", MessageBox.MessageBoxButtons.OK);
                return;
            }
            var parentDir = new DirectoryInfo(editor.ActiveVersion.fullPath).Parent;
            if(parentDir == null)
            {
                Log.Add(Log.LogSeverity.Error, "DevModVersionsViewModel.CreateNewVersion()", editor.ActiveVersion.fullPath+" get parent folder was null");
                return;
            }
            var newDir = Path.Combine(parentDir.FullName, editor.ActiveVersion.id) + "-" + NewVersion;
            if (Directory.Exists(newDir))
            {
                await MessageBox.Show(MainWindow.instance!, "The directory '"+ newDir + "' already exists.", "Validation error", MessageBox.MessageBoxButtons.OK);
                return;
            }

            //Version string should be valid
            var button = (Button)control;
            if (button.Flyout != null)
                button.Flyout.Hide();

            TaskViewModel.Instance!.CreateModVersion(editor.ActiveVersion, NewVersion, HackUpdateModList);
        }

        /// <summary>
        /// Hack to force to update the list in the ui, since Mod class dosent implement observable object
        /// </summary>
        public void HackUpdateModList()
        {
            Mods = Mods.ToList();
        }

        internal async void UploadModAdvanced()
        {
            var mod = editor?.ActiveVersion;
            if (mod != null)
            {
                if (!mod.inNebula)
                {
                    if (mod.type == ModType.mod || mod.type == ModType.tc)
                    {
                        var dialog = new DevModAdvancedUploadView();
                        dialog.DataContext = new DevModAdvancedUploadViewModel(mod, dialog, this);
                        await dialog.ShowDialog<DevModAdvancedUploadView?>(MainWindow.instance!);
                    }
                    else
                    {
                        _ = MessageBox.Show(MainWindow.instance!, "Advanced uploading is only available for Mods and TCs.", "Unsupported for type: " + mod.type, MessageBox.MessageBoxButtons.OK);
                    }
                }
                else
                {
                    _ = MessageBox.Show(MainWindow.instance!, "This mod version is already uploaded to nebula, if you want to update the metadata use the basic upload", "Mod already uploaded", MessageBox.MessageBoxButtons.OK);
                }
            }
        }

        //For UI button
        internal async void UploadMod()
        {
            await UploadProcess();
        }

        //for the advanced upload window
        public void AdvancedUpload(List<DevModAdvancedUploadData> advData, int parallelCompression, int parallelUploads)
        {
            _ = UploadProcess(advData, parallelCompression, parallelUploads);
        }

        private async Task UploadProcess(List<DevModAdvancedUploadData>? advData = null, int parallelCompression = 1, int parallelUploads = 1)
        {
            /* 
             *  Pre-Upload Checks:
             *  NO-GO:
             *  1) At least one package must exist. --done
             *  2) One of the packages must be marked as requiered. --done
             *  3) If type = TC check it must have no mod dependencies of other mod parent or a TC --done
             *  4) If type = Engine check if the enviroment string is valid and the execs are accesible. --TODO
             *  5) If type = mod and parent is not "FS2" it must have a dependency pointing to a TC/Version mod. --done
             *  6) if type == mod or tc, it must have a FSO engine as dependency, and it must be already uploaded --TODO
             *  7) User must be logged in nebula. --done
             *  8) Check modid to see if it exist, and if does the user must have write permisions to it. --done
             *  9) All dependencies must be released mods, either public or private
             *  10) If mod is public it cant depend on a private dependency
             *  11) if the tile image size is over the maximum allowed (300kb) --done
             *  
             *  WARNINGS:
             *  -(Always) Warn about the mod visibility before upload, for both private and public, and what it means. --done
             *  -if there is no tile image (for type != engine) or description. --done
             *  -Metadata update only if already uploaded --done
             *  
             *  UPLOAD PROCESS:
             *  A) Do pre_flight API call, im guessing that if the mod version is already uploaded Nebula will report that here somehow. YES: "duplicated version"
             *  B) Upload mod tile image(check if already uploaded), get checksum and import it on modjson.
             *  C) Upload banner image and screenshots(check if already uploaded), get checksum and import it on modjson.
             *  D)  1) If new mod do a create_mod api call, insert first release date (yyyy-mm-dd) and fields: id, title, type, parent, logo, tile, first_release
             *      2) If mod update do a update_mod api call, fields: id(REALLY?), title, logo, tile, first_release (for what?). 
             *         Im petty sure this step is a left behind of an initial implementation and it must NOT be done. ngld never told me about it. TODO: Confirm it! 
             *  E) If package = vp create a vp in mod\kn_upload\vps\{packagename}.vp (No Compression)
             *  F) 7z all packages folders and vp file and place them in kn_upload\{packagename}.7z
             *  G) Wipe and re-generate data in package.files and filelist. "files" is for the 7z file we are uploading to nebula. "filelist" is for all files inside the package folder (folder or vp)
             *  H) Use multipartuploader to upload all packages (will auto-skip if already uploaded)
             *  I) Api Call to "mod/release" with the mod meta (full json)
            */

            if (editor != null)
            {
                ButtonsEnabled = false;
                try
                {
                    if(!TaskViewModel.Instance!.IsSafeState())
                    {
                        await MessageBox.Show(MainWindow.instance!, "You must wait for other tasks to finish before uploading a new mod.", "Basic Check Fail", MessageBox.MessageBoxButtons.OK);
                        ButtonsEnabled = true;
                        return;
                    }

                    var mod = editor.ActiveVersion;
                    if (mod != null)
                    {
                        //Packages exist? && One requiered Package
                        if (mod.packages == null || !mod.packages.Any() || mod.packages.FirstOrDefault(x => x.status == "required") == null)
                        {
                            await MessageBox.Show(MainWindow.instance!, "The mod must include at least one required package.", "Basic Check Fail", MessageBox.MessageBoxButtons.OK);
                            ButtonsEnabled = true;
                            return;
                        }

                        //If type = TC check it must have no mod dependencies of other mod parent. Update: This was disabled
                        /*
                        if (mod.type == ModType.tc)
                        {
                            foreach(var pkg in mod.packages)
                            {
                                if(pkg.dependencies != null && pkg.dependencies.Any())
                                {
                                    foreach (var dep in pkg.dependencies)
                                    {
                                        var depMod = dep.SelectMod();
                                        if(depMod != null && (depMod.type == ModType.mod || depMod.type == ModType.tc) && depMod.parent != mod.id)
                                        {
                                            if (mod.type == ModType.mod)
                                            {
                                                await MessageBox.Show(MainWindow.instance!, "This mod depends on: " + depMod + ". That mod has a diferent parent mod: "
                                                    + depMod.parent + ". And it is not intended to be used with your TC mod: " + mod.id, "Basic Check Fail", MessageBox.MessageBoxButtons.OK);
                                            }
                                            if (mod.type == ModType.tc)
                                            {
                                                await MessageBox.Show(MainWindow.instance!, "This mod depends on: " + depMod + ". Thats a different TC mod. Your TC mod cant depend on another TC mod."
                                                    , "Basic Check Fail", MessageBox.MessageBoxButtons.OK);
                                            }
                                            return;
                                        }
                                    }
                                }
                            }
                        }
                        */

                        //If type = mod and parent is not "FS2" it must have a dependency pointing to a TC/Version mod.
                        if(mod.type == ModType.mod && mod.parent != "FS2")
                        {
                            var found = false;
                            foreach (var pkg in mod.packages)
                            {
                                if (pkg.dependencies != null && pkg.dependencies.Any())
                                {
                                    var result = pkg.dependencies.FirstOrDefault(x => x.SelectMod()?.type == ModType.tc); 
                                    if(result != null)
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                            }
                            if(!found)
                            {
                                await MessageBox.Show(MainWindow.instance!, "Mods for TC other than 'FS2' must also include a dependency to that TC mod.", "Basic Check Fail", MessageBox.MessageBoxButtons.OK);
                                ButtonsEnabled = true;
                                return;
                            }
                        }

                        //if the tile image size is over the maximum allowed (300kb)
                        if (!string.IsNullOrEmpty(mod.tile) && File.Exists(Path.Combine(mod.fullPath,mod.tile)) && new FileInfo(Path.Combine(mod.fullPath, mod.tile)).Length > 307200)
                        {
                            await MessageBox.Show(MainWindow.instance!, "The mod tile image is over the maximum of 300kb allowed.", "Basic Check Fail", MessageBox.MessageBoxButtons.OK);
                            ButtonsEnabled = true;
                            return;
                        }

                        //If type = Engine check if the enviroment string is valid and the execs are accesible.
                        if(mod.type == ModType.engine)
                        {
                            foreach (var pkg in mod.packages)
                            {
                                if(string.IsNullOrEmpty(pkg.environment))
                                {
                                    await MessageBox.Show(MainWindow.instance!, "Package " + pkg.name + " has a empty environment string.", "Basic Check Fail", MessageBox.MessageBoxButtons.OK);
                                    ButtonsEnabled = true;
                                    return;
                                }
                            }

                            //rest is unimplemented
                        }

                        //If type == mod or tc, it must have a FSO engine as dependency, and it must be already uploaded
                        if(mod.type == ModType.mod || mod.type == ModType.tc)
                        {
                            var found = false;
                            foreach (var pkg in mod.packages)
                            {
                                if (pkg.status == "required" && pkg.dependencies != null && pkg.dependencies.Any())
                                {
                                    var result = pkg.dependencies.FirstOrDefault(x => x.SelectBuild() != null);
                                    if (result != null)
                                    {
                                        //if released will be checked in the next step
                                        found = true;
                                        break;
                                    }
                                }
                            }
                            if (!found)
                            {
                                await MessageBox.Show(MainWindow.instance!, "Mods and TCs must include a FSO engine dependency in one of the required packages.", "Basic Check Fail", MessageBox.MessageBoxButtons.OK);
                                ButtonsEnabled = true;
                                return;
                            }
                        }

                        //All dependencies must be released mods, either public or private
                        //If mod is public it cant depend on a private dependency
                        if (mod.type == ModType.mod || mod.type == ModType.tc)
                        {
                            foreach (var pkg in mod.packages)
                            {
                                if (pkg.dependencies != null && pkg.dependencies.Any())
                                {
                                    foreach (var dep in pkg.dependencies)
                                    {
                                        var modlist = await Nebula.GetAllModsWithID(dep.id);
                                        if(modlist == null || !modlist.Any())
                                        {
                                            await MessageBox.Show(MainWindow.instance!, "Your mod depends on mod id: " + dep.id + " that was not found in Nebula.", "Basic Check Fail", MessageBox.MessageBoxButtons.OK);
                                            ButtonsEnabled = true;
                                            return;
                                        }
                                        bool found = false;
                                        foreach(var m in modlist)
                                        {
                                            if ((!m.isPrivate || m.isPrivate && mod.isPrivate) && SemanticVersion.SastifiesDependency(dep.version, m.version))
                                            {
                                                found = true;
                                                break;
                                            }
                                        }
                                        if(!found)
                                        {
                                            await MessageBox.Show(MainWindow.instance!, "Your mod depends on mod id: " + dep.id + " Condition: " + dep.version + " we cant find " +
                                                "a candidate in Nebula that satisfies the condition with a compatible mod visibility.", "Basic Check Fail", MessageBox.MessageBoxButtons.OK);
                                            ButtonsEnabled = true;
                                            return;
                                        }
                                    }
                                }
                            }
                        }

                        //User must be logged in nebula.
                        if (!await Nebula.Login())
                        {
                            await MessageBox.Show(MainWindow.instance!, "You must be logged in to nebula in order to upload mods.", "Basic Check Fail", MessageBox.MessageBoxButtons.OK);
                            ButtonsEnabled = true;
                            return;
                        }

                        //Check modid to see if it exist, and if does the user must have write permisions to it.
                        var isNewMod = await Nebula.CheckIDAvailable(mod.id);
                        if (!isNewMod)
                        {
                            if(!await Nebula.IsModEditable(mod.id))
                            {
                                await MessageBox.Show(MainWindow.instance!, "You do not have write permissions to this mod.", "Basic Check Fail", MessageBox.MessageBoxButtons.OK);
                                ButtonsEnabled = true;
                                return;
                            }
                        }

                        //Warnings
                        //if there is no tile image (for type != engine) or description.
                        if (string.IsNullOrEmpty(mod.description) && mod.type != ModType.engine)
                        {
                            if(await MessageBox.Show(MainWindow.instance!, "Your mod does not include a description, it is recomended you set a description for users. " +
                                "This is only a warning and you can continue the upload if you want.", "Basic Check Warning", MessageBox.MessageBoxButtons.ContinueCancel) != MessageBox.MessageBoxResult.Continue)
                            {
                                ButtonsEnabled = true;
                                return;
                            }  
                        }
                        if (string.IsNullOrEmpty(mod.tile) && mod.type != ModType.engine)
                        {
                            if (await MessageBox.Show(MainWindow.instance!, "Your mod does not include a tile image, it is recomended you set a tile image for users. " +
                                "This is only a warning and you can continue the upload if you want.", "Basic Check Warning", MessageBox.MessageBoxButtons.ContinueCancel) != MessageBox.MessageBoxResult.Continue)
                            {
                                ButtonsEnabled = true;
                                return;
                            }
                        }

                        //Warn about the mod visibility before upload, for both private and public, and what it means.
                        if(mod.isPrivate)
                        {
                            if (await MessageBox.Show(MainWindow.instance!, "The mod version you are uploading is set to 'PRIVATE'. " +
                                "This means only the mod members will be able to see it. Do you want to continue?", "Basic Check Warning", MessageBox.MessageBoxButtons.ContinueCancel) != MessageBox.MessageBoxResult.Continue)
                            {
                                ButtonsEnabled = true;
                                return;
                            }
                        }
                        else
                        {
                            if (await MessageBox.Show(MainWindow.instance!, "The mod version you are uploading is set to 'PUBLIC'. " +
                                "This means the mod will be available for everyone. Do you want to continue?", "Basic Check Warning", MessageBox.MessageBoxButtons.ContinueCancel) != MessageBox.MessageBoxResult.Continue)
                            {
                                ButtonsEnabled = true;
                                return;
                            }
                        }

                        //Metadata update only if mod/version is already uploaded
                        var metaUpdOnly = false;
                        if(await Nebula.GetModData(mod.id,mod.version) != null)
                        {
                            metaUpdOnly = true;
                            var res = await MessageBox.Show(MainWindow.instance!, "This mod and version '" + mod + "' is already uploaded to Nebula, if you continue only the metadata will be updated.", "Version already uploaded", MessageBox.MessageBoxButtons.ContinueCancel);
                            if(res == MessageBox.MessageBoxResult.Cancel)
                            {
                                ButtonsEnabled = true;
                                return;
                            }
                        }


                        //Add release date if new mod and last update
                        if (mod.firstRelease == null)
                        {
                            mod.firstRelease = DateTime.Now.Year.ToString() + "-" + DateTime.Now.Month.ToString("00") + "-" + DateTime.Now.Day.ToString("00");
                        }
                        mod.lastUpdate = DateTime.Now.Year.ToString() + "-" + DateTime.Now.Month.ToString("00") + "-" + DateTime.Now.Day.ToString("00");

                        //Basic check finish, create task
                        ButtonsEnabled = true;
                        TaskViewModel.Instance!.UploadModVersion(mod, isNewMod, metaUpdOnly, parallelCompression, parallelUploads, advData);
                    }
                }
                catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "DevModVersionsViewModel.UploadMod()", ex);
                }
            }
        }

        internal async void DeleteAll()
        {
            if (editor != null)
            {
                var response = await MessageBox.Show(MainWindow.instance!, "This is going to complete delete the mod " + editor.ActiveVersion.title + " from your library." +
                    " All versions will be deleted. This is not going to affect any version already uploaded to Nebula.", "Delete ALL mod versions", MessageBox.MessageBoxButtons.OKCancel);
                if (response == MessageBox.MessageBoxResult.OK)
                {
                    editor.DeleteAllVersions();
                }
            }
        }

        internal async void DeleteNebula()
        {
            if(editor != null)
            {
                if(editor.ActiveVersion.inNebula)
                {
                    var response = await MessageBox.Show(MainWindow.instance!, "Do you really want to remove mod " + editor.ActiveVersion + " from Nebula? " +
                        "this is not going to affect local files. ", "Delete mod from Nebula", MessageBox.MessageBoxButtons.OKCancel);
                    if (response == MessageBox.MessageBoxResult.OK)
                    {
                        ButtonsEnabled = false;
                        var result = await Nebula.DeleteModVersion(editor.ActiveVersion);

                        if (result != null)
                        {
                            if (result == "ok")
                            {
                                editor.ActiveVersion.inNebula = false;
                                HackUpdateModList();
                                await MessageBox.Show(MainWindow.instance!, "The mod " + editor.ActiveVersion + " was deleted from Nebula.", "Mod deleted", MessageBox.MessageBoxButtons.OK);
                            }
                            else
                            {
                                await MessageBox.Show(MainWindow.instance!, "An error has ocurred while trying to remove mod " + editor.ActiveVersion + " from Nebula. Reason: " + result, "Mod delete error", MessageBox.MessageBoxButtons.OK);
                            }
                        }
                        else
                        {
                            await MessageBox.Show(MainWindow.instance!, "An error has ocurred while trying to remove mod " + editor.ActiveVersion + " from Nebula. Reason: unknown error", "Mod delete error", MessageBox.MessageBoxButtons.OK);
                        }
                        ButtonsEnabled = true;
                    }
                }
            }
        }

        internal async void DeleteLocally()
        {
            if (editor != null)
            {
                var response = await MessageBox.Show(MainWindow.instance!, "This is going to delete the mod/version " + editor.ActiveVersion + " from your library." +
                    " This is not going to affect anything uploaded to Nebula.", "Delete mod version", MessageBox.MessageBoxButtons.OKCancel);
                if (response == MessageBox.MessageBoxResult.OK)
                {
                    editor.DeleteActiveVersion();
                }
                
            }
        }

        internal void ChangeVisibility()
        {
            if (editor != null)
            {
                ButtonsEnabled = false;
                editor.ActiveVersion.isPrivate = !editor.ActiveVersion.isPrivate;
                if (editor.ActiveVersion.isPrivate)
                {
                    VisibilityButtonText = "Make it Public";
                }
                else
                {
                    VisibilityButtonText = "Make it Private";
                }
                editor.ActiveVersion.SaveJson();
                HackUpdateModList();
                ButtonsEnabled = true;
            }
        }

        /// <summary>
        /// DevEnv is just a version tagged "999.0.0-DevEnv" this version cant be uploaded to Nebula and only used for continuous local development process
        /// If a Mod has this version the create devenv button is disabled as well as the upload, visibility and delete from nebula buttons
        /// </summary>
        internal async void CreateDevEnv()
        {
            if (editor == null)
                return;
            string devVersion = "999.0.0-DevEnv";
            string explanation = "A DevEnv version is a mod version intended to be used for continuous local development process, and it can't be uploaded to Nebula.\nThis version will always be the default active version every time you start Knet, and" +
                            " provides a static folder you can always work on.\nWhen you want to release a new version you can create a new version from the DevEnv one and release it.";

            if(await MessageBox.Show(MainWindow.instance!, explanation, "Creating Dev Environment version", MessageBox.MessageBoxButtons.ContinueCancel) != MessageBox.MessageBoxResult.Continue)
            {
                return;
            }
            if (Mods.FirstOrDefault(m => m.version == devVersion) != null)
            {
                await MessageBox.Show(MainWindow.instance!, "'" + devVersion + "' already exists.", "Validation error", MessageBox.MessageBoxButtons.OK);
                return;
            }
            var parentDir = new DirectoryInfo(editor.ActiveVersion.fullPath).Parent;
            if (parentDir == null)
            {
                Log.Add(Log.LogSeverity.Error, "DevModVersionsViewModel.CreateNewVersion()", editor.ActiveVersion.fullPath + " get parent folder was null");
                return;
            }
            var newDir = Path.Combine(parentDir.FullName, editor.ActiveVersion.id) + "-" + devVersion;
            if (Directory.Exists(newDir))
            {
                await MessageBox.Show(MainWindow.instance!, "The directory '" + newDir + "' already exists.", "Validation error", MessageBox.MessageBoxButtons.OK);
                return;
            }

            ModHasDevEnvVersion = true;

            TaskViewModel.Instance!.CreateModVersion(editor.ActiveVersion, devVersion, HackUpdateModList);
        }
    }
}
