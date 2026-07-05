using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Platform.Storage;
using Avalonia.Controls;

namespace Knossos.NET.ViewModels
{
    /// <summary>
    /// Fs2 Retail Window View Model
    /// </summary>
    public partial class Fs2InstallerViewModel : ViewModelBase
    {
        /// <summary>
        /// Must have file list
        /// </summary>
        private readonly string[] required =
        {
            "root_fs2.vp", "smarty_fs2.vp", "sparky_fs2.vp",
            "sparky_hi_fs2.vp", "stu_fs2.vp", "tango1_fs2.vp",
            "tango2_fs2.vp", "tango3_fs2.vp", "warble_fs2.vp"
        };

        /// <summary>
        /// Additional files to find and copy
        /// </summary>
        private readonly string[] optional =
        {
            "hud_1.hcf", "hud_2.hcf", "hud_3.hcf", "movies_fs2.vp", "multi-mission-pack.vp", "multi-voice-pack.vp", "bastion.ogg", "colossus.ogg",
            "endpart1.ogg", "endprt2a.ogg", "endprt2b.ogg", "intro.ogg", "mono1.ogg", "mono2.ogg", "mono3.ogg", "mono4.ogg", "bastion.mve",
            "colossus.mve", "endpart1.mve", "endprt2a.mve", "endprt2b.mve", "intro.mve", "mono1.mve", "mono2.mve", "mono3.mve", "mono4.mve"
        };

        private List<IStorageFile> filePaths = new List<IStorageFile>();

        [ObservableProperty]
        internal bool isInstalling = false;
        [ObservableProperty]
        internal bool canInstall = false;
        [ObservableProperty]
        internal int progressMax = 100;
        [ObservableProperty]
        internal int progressCurrent = 0;
        [ObservableProperty]
        internal string installText = string.Empty;
        [ObservableProperty]
        internal bool innoExtractIsAvailable = false;
        private string? gogExe = null;
        private KnossosWindow? window;
        private int reqFilesFound = 0;

        public Fs2InstallerViewModel() 
        { 
            if(KnUtils.IsWindows || KnUtils.IsMacOS || KnUtils.IsLinux && ( KnUtils.CpuArch == "X64" || KnUtils.CpuArch == "X86" || KnUtils.CpuArch == "Arm64" || KnUtils.CpuArch == "RiscV64"))
            {
                InnoExtractIsAvailable = true;
            }
        }

        public Fs2InstallerViewModel(KnossosWindow window)
        {
            if (KnUtils.IsWindows || KnUtils.IsMacOS || KnUtils.IsLinux && (KnUtils.CpuArch == "X64" || KnUtils.CpuArch == "X86" || KnUtils.CpuArch == "Arm64" || KnUtils.CpuArch == "RiscV64"))
            {
                InnoExtractIsAvailable = true;
            }
            this.window = window;
        }

        /// <summary>
        /// The main install process
        /// </summary>
        internal async void InstallFS2Command()
        {
            if(Knossos.GetKnossosLibraryPath() == null)
            {
                await MessageBox.Show(MainWindow.instance!, "The KnossosNET library path is not set, first set the library path in the settings tab before installing FS2 Retail.", "Library path is null", MessageBox.MessageBoxButtons.OK);
                return;
            }
            
            //If gog exe first extract to a temp folder and process it first
            if(gogExe != null)
            {
                InstallText = "Running innoextract";
                IsInstalling = true;
                try
                {
                    string innoPath = KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar;
                    if (KnUtils.IsWindows)
                    {
                        innoPath += "innoextract.exe";
                    }
                    else
                    {
                        if(KnUtils.IsLinux)
                        {
                            if(KnUtils.CpuArch == "X64")
                            {
                                innoPath += "innoextract.x64";
                            }
                            if (KnUtils.CpuArch == "X86")
                            {
                                innoPath += "innoextract.x86";
                            }
                            if (KnUtils.CpuArch == "Arm64")
                            {
                                innoPath += "innoextract.arm64";
                            }
                            if (KnUtils.CpuArch == "RiscV64")
                            {
                                innoPath += "innoextract.riscv64";
                            }
                        }
                        else
                        {
                            if(KnUtils.IsMacOS)
                            {
                                innoPath += "innoextract.mac64";
                            }
                        }
                    }

                    await Task.Run(() =>
                    {
                        var cmd = new Process();
                        Directory.CreateDirectory(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "gog");
                        cmd.StartInfo.FileName = innoPath;
                        cmd.StartInfo.Arguments = gogExe + " -L -g -d \"" + KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "gog\"";
                        cmd.StartInfo.UseShellExecute = false;
                        cmd.StartInfo.CreateNoWindow = true;
                        cmd.StartInfo.RedirectStandardOutput = true;
                        cmd.StartInfo.StandardOutputEncoding = new UTF8Encoding(false);
                        cmd.Start();
                        string? output;
                        while ((output = cmd.StandardOutput.ReadLine()) != null)
                        {
                            Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                InstallText = "Running innoextract" + output;
                                ProgressCurrent++;
                            });
                        }
                        cmd.WaitForExit();
                        cmd.Dispose();
                    });
                    /*
                        there is an older gog installer that had all the data inside an /app folder, current version it just on the root
                        ProccessFolder need to be pointed to the folder with all the vps and the datas folder
                    */
                    var sp = KnUtils.GetTopLevel().StorageProvider;
                    if (File.Exists(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "gog" + Path.DirectorySeparatorChar + "root_fs2.vp"))
                    {
                        ProcessFolder( await sp.TryGetFolderFromPathAsync(Path.Combine(KnUtils.GetKnossosDataFolderPath(),"gog")) );
                    }
                    else
                    {
                        ProcessFolder(await sp.TryGetFolderFromPathAsync(Path.Combine(KnUtils.GetKnossosDataFolderPath(), "gog", "app")));
                    }
                }
                catch(Exception ex) 
                {
                    Log.Add(Log.LogSeverity.Error, "Fs2InstallerViewModel.InstallFS2Command()", ex);
                    return;
                }
            }

            if (!filePaths.Any())
            {
                await MessageBox.Show(MainWindow.instance!, "Filepaths list is empty, something happened, if you are reading a gog exe it may be because its internal folder structure is different than the expected.", "Error", MessageBox.MessageBoxButtons.OK);
                if (gogExe != null)
                {
                    try
                    {
                        Directory.Delete(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "gog", true);
                    }
                    catch { }
                }
                return;
            }

            await Task.Run(async () => { 
                try
                {
                    IsInstalling = true;
                    ProgressMax += filePaths.Count();
                    ProgressCurrent = ProgressMax - filePaths.Count();
                    Directory.CreateDirectory(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "movies");
                    Directory.CreateDirectory(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "players");
                    foreach (var file in filePaths)
                    {
                        InstallText = "Copying " + file.Name;
                        if (file.Name.ToLower().Contains(".vp"))
                        {
                            using (var streamOrg = await file.OpenReadAsync())
                            {
                                using (var streamDst = new FileStream(Path.Combine(Knossos.GetKnossosLibraryPath()!, "FS2", file.Name), FileMode.Create, FileAccess.Write))
                                {
                                    await streamOrg.CopyToAsync(streamDst);
                                }
                            }
                        }
                        else
                        {
                            if (file.Name.ToLower().Contains(".hcf"))
                            {
                                using (var streamOrg = await file.OpenReadAsync())
                                {
                                    using (var streamDst = new FileStream(Path.Combine(Knossos.GetKnossosLibraryPath()!, "FS2", "data", "players", file.Name), FileMode.Create, FileAccess.Write))
                                    {
                                        await streamOrg.CopyToAsync(streamDst);
                                    }
                                }
                            }
                            else
                            {
                                using (var streamOrg = await file.OpenReadAsync())
                                {
                                    using (var streamDst = new FileStream(Path.Combine(Knossos.GetKnossosLibraryPath()!, "FS2", "data", "movies", file.Name), FileMode.Create, FileAccess.Write))
                                    {
                                        await streamOrg.CopyToAsync(streamDst);
                                    }
                                }
                            }
                        }
                        ProgressCurrent++;
                    }
                    InstallText = "Finishing tasks...";
                    var fs2Mod = new Mod();
                    fs2Mod.fullPath = Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2";
                    fs2Mod.folderName = "FS2";
                    fs2Mod.installed = true;
                    fs2Mod.id = "FS2";
                    fs2Mod.title = "Retail FS2";
                    fs2Mod.type = ModType.tc;
                    fs2Mod.parent = "FS2";
                    fs2Mod.version = "1.20.0";
                    fs2Mod.stability = "stable"; //wut?
                    fs2Mod.description = "[b][i]The year is 2367, thirty two years after the Great War. Or at least that is what YOU thought was the Great War. The endless line of Shivan capital ships, bombers and fighters with super advanced technology was nearly overwhelming.\n\nAs the Terran and Vasudan races finish rebuilding their decimated societies, a disturbance lurks in the not-so-far reaches of the Gamma Draconis system.\n\nYour nemeses have arrived... and they are wondering what happened to their scouting party.[/i][/b]\n\n[hr]FreeSpace 2 is a 1999 space combat simulation computer game developed by Volition as the sequel to Descent: FreeSpace \u2013 The Great War. It was completed ahead of schedule in less than a year, and released to very positive reviews.\n\nThe game continues on the story from Descent: FreeSpace, once again thrusting the player into the role of a pilot fighting against the mysterious aliens, the Shivans. While defending the human race and its alien Vasudan allies, the player also gets involved in putting down a rebellion. The game features large numbers of fighters alongside gigantic capital ships in a battlefield fraught with beams, shells and missiles in detailed star systems and nebulae.";
                    fs2Mod.tile = "kn_tile.png";
                    fs2Mod.banner = "kn_banner.png";
                    fs2Mod.releaseThread = "http://www.hard-light.net/forums/index.php";
                    fs2Mod.videos = new string[] { "https://www.youtube.com/watch?v=ufViyhrXzTE" };
                    fs2Mod.screenshots = new string[] { "kn_screen_0.jpg", "kn_screen_1.jpg", "kn_screen_2.jpg", "kn_screen_3.jpg", "kn_screen_4.jpg", "kn_screen_5.jpg", "kn_screen_6.jpg", "kn_screen_7.jpg", "kn_screen_8.jpg", "kn_screen_9.jpg", "kn_screen_10.jpg", "kn_screen_11.jpg" };
                    fs2Mod.firstRelease = "1999-09-30";
                    fs2Mod.lastUpdate = "1999-12-03";
                    fs2Mod.notes = string.Empty;
                    fs2Mod.cmdline = string.Empty;
                    fs2Mod.attachments = new string[] { };
                    fs2Mod.modFlag.Add("FS2");
                    var fs2Pkg= new ModPackage();
                    fs2Pkg.name = "Content";
                    fs2Pkg.status = "required";
                    var fs2Dep = new ModDependency();
                    fs2Dep.id = "FSO";
                    fs2Dep.version = ">=3.8.1";
                    fs2Pkg.dependencies = new ModDependency[] { fs2Dep };
                    fs2Mod.packages.Add(fs2Pkg);
                    fs2Mod.modSource = ModSource.local;
                    fs2Mod.SaveJson();
                    try
                    {
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_tile.png"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_tile.png")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_banner.png"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_banner.png")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_0.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_0.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_1.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_1.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_2.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_2.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_3.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_3.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_4.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_4.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_5.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_5.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_6.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_6.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_7.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_7.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_8.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_8.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_9.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_9.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_10.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_10.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_11.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_11.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                    }
                    catch { }
                    InstallText = "Install Complete!, KnossosNET is reloading the library...";
                    Knossos.ResetBasePath();
                    if(gogExe != null)
                    {
                        try
                        {
                            Directory.Delete(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "gog",true);
                        }
                        catch { }
                    }
                }
                catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "Fs2InstallerViewModel.InstallFS2Command()",ex);
                }
            });
        }

        /// <summary>
        /// Open file dialog to select a gog exe file, it checks that this is a valid FS2 install file
        /// </summary>
        internal async void LoadGoGExeCommand()
        {
            FilePickerOpenOptions options = new FilePickerOpenOptions();
            options.AllowMultiple = false;
            options.Title = "Select your Freespace 2 gog .exe installer file";

            var topmostWindow = KnUtils.GetTopLevel();
            var result = await topmostWindow.StorageProvider.OpenFilePickerAsync(options);

            if (result != null && result.Count > 0)
            {
                CanInstall = false;
                gogExe = null;
                try
                {
                    string innoPath = KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar;

                    /*Copy Innoextract License file*/
                    using (var fileStream = File.Create(innoPath + Path.DirectorySeparatorChar + "innoextract.license"))
                    {
                        AssetLoader.Open(new Uri("avares://Knossos.NET.Desktop/Assets/utils/innoextract.license")).CopyTo(fileStream);
                        fileStream.Close();
                    }

                    if (KnUtils.IsWindows)
                    {
                        innoPath += "innoextract.exe";
                        using (var fileStream = File.Create(innoPath))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET.Desktop/Assets/utils/win/innoextract.exe")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                    }
                    else
                    {
                        if (KnUtils.IsLinux)
                        {
                            if (KnUtils.CpuArch == "X64")
                            {
                                innoPath += "innoextract.x64";
                                using (var fileStream = File.Create(innoPath))
                                {
                                    AssetLoader.Open(new Uri("avares://Knossos.NET.Desktop/Assets/utils/linux-x64/innoextract.x64")).CopyTo(fileStream);
                                    fileStream.Close();
                                    KnUtils.Chmod(innoPath,"+x");
                                }
                            }
                            if (KnUtils.CpuArch == "Arm64")
                            {
                                innoPath += "innoextract.arm64";
                                using (var fileStream = File.Create(innoPath))
                                {
                                    AssetLoader.Open(new Uri("avares://Knossos.NET.Desktop/Assets/utils/linux-arm64/innoextract.arm64")).CopyTo(fileStream);
                                    fileStream.Close();
                                    KnUtils.Chmod(innoPath, "+x");
                                }
                            }
                            if (KnUtils.CpuArch == "RiscV64")
                            {
                                innoPath += "innoextract.riscv64";
                                using (var fileStream = File.Create(innoPath))
                                {
                                    AssetLoader.Open(new Uri("avares://Knossos.NET.Desktop/Assets/utils/linux-riscv64/innoextract.riscv64")).CopyTo(fileStream);
                                    fileStream.Close();
                                    KnUtils.Chmod(innoPath, "+x");
                                }
                            }
                        }
                        else
                        {
                            if(KnUtils.IsMacOS)
                            {
                                innoPath += "innoextract.mac64";
                                using (var fileStream = File.Create(innoPath))
                                {
                                    AssetLoader.Open(new Uri("avares://Knossos.NET.Desktop/Assets/utils/osx/innoextract.mac64")).CopyTo(fileStream);
                                    fileStream.Close();
                                    KnUtils.Chmod(innoPath, "+x");
                                }
                            }
                        }
                    }

                    var cmd = new Process();
                    var file = new FileInfo(result[0].Path.LocalPath.ToString());
                    gogExe = "\"" + file.FullName + "\"";
                    cmd.StartInfo.FileName = innoPath;
                    cmd.StartInfo.Arguments = gogExe + " -l -g";
                    cmd.StartInfo.UseShellExecute = false;
                    cmd.StartInfo.CreateNoWindow = true;
                    cmd.StartInfo.RedirectStandardOutput = true;
                    cmd.StartInfo.RedirectStandardInput = true;
                    cmd.StartInfo.StandardOutputEncoding = new UTF8Encoding(false);
                    cmd.Start();
                    var innoOutput = cmd.StandardOutput.ReadToEnd().ToLower();
                    cmd.WaitForExit();
                    cmd.Dispose();
                    int count = 0;
                    foreach (var reqFileName in required)
                    {
                        if (innoOutput.Contains(reqFileName))
                            count++;
                    }

                    if (count != required.Count())
                    {
                        //Missing files
                        gogExe = null;
                        await MessageBox.Show(MainWindow.instance!, "Unable to find all the required Freespace 2 files in gog exe.", "Files not found", MessageBox.MessageBoxButtons.OK);
                        return;
                    }
                    ProgressMax = innoOutput.Split('\n').Length-2;
                    ProgressMax += required.Count() + optional.Count();
                    CanInstall = true;
                }catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "Fs2InstallerViewModel.LoadGoGExeCommand()", ex);
                }
            }
        }

        /// <summary>
        /// Select a fs2retail folder
        /// </summary>
        internal async void LoadFolderCommand()
        {
            FolderPickerOpenOptions options = new FolderPickerOpenOptions();
            options.AllowMultiple = false;
            options.Title = "Select your Freespace 2 retail folder";
            var topmostWindow = KnUtils.GetTopLevel();
            var result = await topmostWindow.StorageProvider.OpenFolderPickerAsync(options);

            if (result != null && result.Count > 0)
            {
                CanInstall = false;
                gogExe = null;
                ProcessFolder(result[0]);
            }
        }

        /// <summary>
        /// Search the folder to find all files
        /// </summary>
        /// <param name="path"></param>
        private async void ProcessFolder(IStorageFolder? path, bool topLevel = true)
        {
            try
            {
                if(path == null)
                    throw new ArgumentNullException(nameof(path));
                if (topLevel)
                {
                    reqFilesFound = 0;
                    filePaths.Clear();
                }

                var items = path.GetItemsAsync();
                await foreach (var item in items)
                {
                    if (item is IStorageFile file)
                    {
                        var isImportant = required.FirstOrDefault(x=> x.ToLower() == file.Name.ToLower());
                        if(isImportant != null)
                        {
                            reqFilesFound ++;
                            filePaths.Add(file);
                        }
                        var isOptional = optional.FirstOrDefault(x => x.ToLower() == file.Name.ToLower());
                        if (isOptional != null)
                        {
                            filePaths.Add(file);
                        }
                    }
                    else if (item is IStorageFolder subfolder)
                    {
                        ProcessFolder(subfolder, false);
                    }
                }

                if(topLevel)
                {
                    if (reqFilesFound < 9)
                    {
                        //Missing files
                        await MessageBox.Show(MainWindow.instance!, "Unable to find all the required Freespace 2 files in this directory.", "Files not found", MessageBox.MessageBoxButtons.OK);
                        return;
                    }
                    CanInstall = true;
                }
            }catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Fs2InstallerViewModel.ProcessFolder()", ex);
            }
        }
    }
}
