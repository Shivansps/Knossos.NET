using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace Knossos.NET.ViewModels
{
    public partial class DevModAdvancedUploadData : ObservableObject
    {
        [ObservableProperty]
        internal List<ComboBoxItem> otherVersions = new List<ComboBoxItem>();

        internal int otherVersionsSelectedIndex = 0;
        internal int OtherVersionsSelectedIndex
        {
            get { return otherVersionsSelectedIndex; }
            set
            {
                if (otherVersionsSelectedIndex != value)
                {
                    this.SetProperty(ref otherVersionsSelectedIndex, value);
                    if (value == 0 ) // Auto
                    {
                        CustomHash = "";
                    }
                    else
                    {
                        if(package == null)
                        {
                            CustomHash = "Error: Local package was null, this should not happen.";
                            return;
                        }
                        try
                        {
                            //Get package data from nebula and let the cache do its thing
                            if (packageInNebula == null)
                            {
                                if (OtherVersions[value].DataContext == null)
                                {
                                    CustomHash = "Error: Datacontext was null";
                                    return;
                                }
                                CustomHash = "Loading...";
                                var m = (Mod)OtherVersions[value].DataContext!;
                                Task.Run(async () =>
                                {
                                    try
                                    {
                                        var modTask = await Nebula.GetModData(mod!.id, m.version);
                                        if (modTask == null)
                                        {
                                            CustomHash = "Error: Unable to get data from Nebula";
                                            return;
                                        }
                                        packageInNebula = modTask.packages.FirstOrDefault(p => p.name == package.name);
                                        if (packageInNebula == null)
                                        {
                                            CustomHash = "Error: Package was not found on selected version, or Nebula error.";
                                            return;
                                        }
                                        CustomHash = packageInNebula!.files!.FirstOrDefault()!.checksum![1].ToString();
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Add(Log.LogSeverity.Error, "DevModAdvancedUploadData(OtherVersionsSelectedIndex.Setter2)", ex);
                                        CustomHash = "Error in getting data from Nebula";
                                    };
                                });
                            }
                            else
                            {
                                CustomHash = packageInNebula!.files!.FirstOrDefault()!.checksum![1].ToString();
                            }
                        }
                        catch (Exception ex)
                        {
                            CustomHash = "Error getting hash";
                            Log.Add(Log.LogSeverity.Error, "DevModAdvancedUploadData(OtherVersionsSelectedIndex.Setter3)", ex);
                        }
                    }
                }
            }
        }

        internal bool upload = true;
        internal bool Upload
        {
            get { return upload; }
            set
            {
                if (upload != value)
                {
                    this.SetProperty(ref upload, value);
                    try
                    {
                        if (value == false)
                        {
                            //Enable all Versions listed in the combobox
                            OtherVersions.ForEach(o => o.IsEnabled = true);
                            OtherVersions[0].IsEnabled = false; // Disable "auto"
                            CustomHash = "";
                            //Select what should be the latest version of a mod id
                            if(OtherVersions.Count() >= 2)
                                OtherVersionsSelectedIndex = 1;
                            else
                                OtherVersionsSelectedIndex = 0;
                        }
                        else
                        {
                            //Disable all versions listed in the combobox
                            OtherVersions.ForEach(o => o.IsEnabled = false);
                            OtherVersions[0].IsEnabled = true; //Enable Auto
                            //Select Auto
                            OtherVersionsSelectedIndex = 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Add(Log.LogSeverity.Error, "DevModAdvancedUploadData(Upload.Setter)", ex);
                    }
                }
            }
        }
        
        [ObservableProperty]
        internal string customHash = "";

        public ModPackage? package;
        public ModPackage? packageInNebula = null;
        private Mod? mod;

        public string PackageName
        {
            get
            {
                if (package != null)
                    return package.name;
                else
                    return "package is null";
            }
        }


        public DevModAdvancedUploadData(ModPackage package, Mod mod, List<Mod> allVersionsOfMod)
        {
            this.package = package;
            this.mod = mod;

            //Fill Get Hash Combobox
            var auto = new ComboBoxItem();
            auto.Content = "Auto";
            auto.IsEnabled = true;
            OtherVersions.Add(auto);

            foreach (var o in allVersionsOfMod)
            {
                if (o.version != mod.version)
                {
                    //List version in the combobox
                    var item = new ComboBoxItem();
                    item.DataContext = o;
                    item.Content = o.version.ToString();
                    item.IsEnabled = false;
                    item.Foreground = o.isPrivate ? Brushes.Red : Brushes.Cyan;
                    OtherVersions.Add(item);
                }
            }
            OtherVersionsSelectedIndex = 0;
        }
    }

    /********************************************************************************/

    public partial class DevModAdvancedUploadViewModel : ViewModelBase
    {
        [ObservableProperty]
        internal string title = string.Empty;
        [ObservableProperty]
        internal bool loading = true;
        [ObservableProperty]
        internal int parallelCompressing = 1;
        [ObservableProperty]
        internal int parallelUploads = 1;
        [ObservableProperty]
        internal List<DevModAdvancedUploadData> modPackagesData  = new List<DevModAdvancedUploadData>();

        private Mod? uploadMod = null;
        private DevModAdvancedUploadView? dialog = null;
        private DevModVersionsViewModel? versionsViewModel = null;
        public DevModAdvancedUploadViewModel() 
        {
        }

        public DevModAdvancedUploadViewModel(Mod mod, DevModAdvancedUploadView? dialog, DevModVersionsViewModel? versionsViewModel)
        {
            this.dialog = dialog;
            this.versionsViewModel = versionsViewModel;
            Title = "Advanced Nebula Upload: " + mod;
            uploadMod = mod;
            _ = Task.Run(() => { LazyLoading(); });
            this.versionsViewModel = versionsViewModel;
        }

        private async void LazyLoading()
        {
            if (uploadMod != null && uploadMod.packages != null)
            {
                var allVersionsOfThisMod = await Nebula.GetAllModsWithID(uploadMod.id);
                allVersionsOfThisMod.Sort(Mod.CompareVersionNewerToOlder);
                foreach (var package in uploadMod.packages)
                {
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        var data = new DevModAdvancedUploadData(package, uploadMod, allVersionsOfThisMod);
                        ModPackagesData.Add(data);
                    });
                }
            }
            Loading = false;
        }

        internal async void UploadMod()
        {
            //We have to check that all packages we arent going to upload has a valid sha256 and that they had been uploaded to nebula
            //Basic check
            foreach (var data in ModPackagesData)
            {
                if (!data.upload)
                {
                    if (String.IsNullOrWhiteSpace(data.CustomHash) || data.CustomHash.Length == 0)
                    {
                        _ = MessageBox.Show(MainWindow.instance, "Package: " + data.PackageName + " is not set to upload but it lacks a defined sha256. Operation cancelled.", "Missing hash data", MessageBox.MessageBoxButtons.OK);
                        return;
                    }
                    else
                    {
                        //ensure it is in lowercase
                        data.CustomHash = data.CustomHash.ToLower();
                    }

                    if(data.packageInNebula == null)
                    {
                        _ = MessageBox.Show(MainWindow.instance, "Package: " + data.PackageName + " is not set to upload but it lacks the package data from Nebula. Operation cancelled.", "Missing package data", MessageBox.MessageBoxButtons.OK);
                        return;
                    }
                }
            }

            //Check with nebula if the file is uploaded
            //Disabled, no need since we are getting the file hash from nebula the file HAS to be uploaded.
            /*
            foreach (var data in ModPackagesData)
            {
                if (!data.upload)
                {
                    if(await Nebula.IsFileUploaded(data.CustomHash) == false)
                    {
                        _ = MessageBox.Show(MainWindow.instance, "The provided sha256 hash for package: " + data.PackageName + " is not valid or not uploaded to Nebula, operation cancelled. Passed hash:" + data.CustomHash, "File hash is not uploaded to nebula (or it is incorrect)", MessageBox.MessageBoxButtons.OK);
                        Log.Add(Log.LogSeverity.Error,"DevModAdvancedUploadViewModel.UploadMod", "The provided sha256 hash for package: " + data.PackageName + " is not valid or not uploaded to Nebula, operation cancelled. Passed hash:"+data.CustomHash);
                        return;
                    }
                    await Task.Delay(1000);
                }
            }
            */

            //Send to upload and close window
            await Dispatcher.UIThread.InvokeAsync( ()=> { 
                versionsViewModel?.AdvancedUpload(ModPackagesData, ParallelCompressing, ParallelUploads);
                dialog?.Close();
            });
        }
    }
}
