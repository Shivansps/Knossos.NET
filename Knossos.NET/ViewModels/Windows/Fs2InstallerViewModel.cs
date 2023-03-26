﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Knossos.NET.ViewModels
{
    public partial class Fs2InstallerViewModel : ViewModelBase
    {
        private readonly string[] required =
        {
            "root_fs2.vp", "smarty_fs2.vp", "sparky_fs2.vp",
            "sparky_hi_fs2.vp", "stu_fs2.vp", "tango1_fs2.vp",
            "tango2_fs2.vp", "tango3_fs2.vp", "warble_fs2.vp"
        };

        private readonly string[] optional =
        {
            "movies_fs2.vp" , "multi-mission-pack.vp", "multi-voice-pack.vp", "bastion.ogg", "colossus.ogg",
            "endpart1.ogg", "endprt2a.ogg", "endprt2b.ogg", "intro.ogg", "mono1.ogg", "mono2.ogg", "mono3.ogg", "mono4.ogg", "bastion.mve",
            "colossus.mve", "endpart1.mve", "endprt2a.mve", "endprt2b.mve", "intro.mve", "mono1.mve", "mono2.mve", "mono3.mve", "mono4.mve"
        };

        private List<string> filePaths = new List<string>();

        [ObservableProperty]
        private bool isInstalling = false;
        [ObservableProperty]
        private bool canInstall = false;
        [ObservableProperty]
        private int progressMax = 100;
        [ObservableProperty]
        private int progressCurrent = 0;
        [ObservableProperty]
        private string installText = string.Empty;
        [ObservableProperty]
        private bool innoExtractIsAvalible = false;
        private string? gogExe = null;

        public Fs2InstallerViewModel() 
        { 
            if(SysInfo.IsWindows || SysInfo.IsLinux && ( SysInfo.CpuArch == "X64" || SysInfo.CpuArch == "X86"))
            {
                InnoExtractIsAvalible = true;
            }
        }

        private async void InstallFS2Command()
        {
            if(Knossos.GetKnossosLibraryPath() == null)
            {
                await MessageBox.Show(MainWindow.instance!, "The Knossos library path is not set, first set the library path in the settings tab before installing FS2 Retail.", "Library path is null", MessageBox.MessageBoxButtons.OK);
                return;
            }
            
            //If gog exe first extract to a temp folder and process it first
            if(gogExe != null)
            {
                InstallText = "Running innoextract";
                IsInstalling = true;
                try
                {
                    string innoPath = SysInfo.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar;
                    if (SysInfo.IsWindows)
                    {
                        innoPath += "innoextract.exe";
                    }
                    else
                    {
                        if(SysInfo.IsLinux)
                        {
                            if(SysInfo.CpuArch == "X64")
                            {
                                innoPath += "innoextract.x64";
                            }
                            if (SysInfo.CpuArch == "X86")
                            {
                                innoPath += "innoextract.x86";
                            }
                        }
                    }

                    await Task.Run(() =>
                    {
                        var cmd = new Process();
                        Directory.CreateDirectory(SysInfo.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "gog");
                        cmd.StartInfo.FileName = innoPath;
                        cmd.StartInfo.Arguments = gogExe + " -L -g -d " + SysInfo.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "gog";
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
                            });
                        }
                        cmd.WaitForExit();
                        cmd.Dispose();
                    });
                    /*
                        there is an older gog installer that had all the data inside an /app folder, current version it just on the root
                        ProccessFolder need to be pointed to the folder with all the vps and the datas folder
                    */
                    if (File.Exists(SysInfo.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "gog" + Path.DirectorySeparatorChar + "root_fs2.vp"))
                    {
                        ProcessFolder(SysInfo.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "gog");
                    }
                    else
                    {
                        ProcessFolder(SysInfo.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "gog" + Path.DirectorySeparatorChar + "app");
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
                        Directory.Delete(SysInfo.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "gog", true);
                    }
                    catch { }
                }
                return;
            }

            await Task.Run(() => { 
                try
                {
                    IsInstalling = true; 
                    ProgressMax = filePaths.Count();
                    Directory.CreateDirectory(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "movies");
                    foreach (string file in filePaths)
                    {
                        var parts = file.Split(Path.DirectorySeparatorChar);
                        InstallText = "Copying " + parts[parts.Count() - 1];
                        if (parts[parts.Count() - 1].ToLower().Contains(".vp"))
                        { 
                            File.Copy(file, Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + parts[parts.Count() - 1]);
                        }
                        else
                        {
                            File.Copy(file, Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "movies" + Path.DirectorySeparatorChar + parts[parts.Count() - 1]);
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
                    fs2Mod.type = "tc";
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
                    fs2Mod.SaveJson();
                    try
                    {
                        var assets = Avalonia.AvaloniaLocator.Current.GetService<IAssetLoader>();
                        if (assets != null)
                        {
                            using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_tile.png"))
                            {
                                assets.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_tile.png")).CopyTo(fileStream);
                                fileStream.Close();
                            }
                            using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_banner.png"))
                            {
                                assets.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_banner.png")).CopyTo(fileStream);
                                fileStream.Close();
                            }
                            using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_0.jpg"))
                            {
                                assets.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_0.jpg")).CopyTo(fileStream);
                                fileStream.Close();
                            }
                            using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_1.jpg"))
                            {
                                assets.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_1.jpg")).CopyTo(fileStream);
                                fileStream.Close();
                            }
                            using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_2.jpg"))
                            {
                                assets.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_2.jpg")).CopyTo(fileStream);
                                fileStream.Close();
                            }
                            using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_3.jpg"))
                            {
                                assets.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_3.jpg")).CopyTo(fileStream);
                                fileStream.Close();
                            }
                            using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_4.jpg"))
                            {
                                assets.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_4.jpg")).CopyTo(fileStream);
                                fileStream.Close();
                            }
                            using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_5.jpg"))
                            {
                                assets.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_5.jpg")).CopyTo(fileStream);
                                fileStream.Close();
                            }
                            using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_6.jpg"))
                            {
                                assets.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_6.jpg")).CopyTo(fileStream);
                                fileStream.Close();
                            }
                            using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_7.jpg"))
                            {
                                assets.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_7.jpg")).CopyTo(fileStream);
                                fileStream.Close();
                            }
                            using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_8.jpg"))
                            {
                                assets.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_8.jpg")).CopyTo(fileStream);
                                fileStream.Close();
                            }
                            using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_9.jpg"))
                            {
                                assets.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_9.jpg")).CopyTo(fileStream);
                                fileStream.Close();
                            }
                            using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_10.jpg"))
                            {
                                assets.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_10.jpg")).CopyTo(fileStream);
                                fileStream.Close();
                            }
                            using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_11.jpg"))
                            {
                                assets.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_11.jpg")).CopyTo(fileStream);
                                fileStream.Close();
                            }
                        }
                    }
                    catch { }
                    InstallText = "Install Complete!, Knossos is reloading the library...";
                    Knossos.ResetBasePath();
                    if(gogExe != null)
                    {
                        try
                        {
                            Directory.Delete(SysInfo.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "gog",true);
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

        private async void LoadGoGExeCommand()
        {
            var dialog = new OpenFileDialog();
            dialog.AllowMultiple = false;
            var result = await dialog.ShowAsync(MainWindow.instance!);

            if (result != null)
            {
                CanInstall = false;
                gogExe = null;
                try
                {
                    var assets = Avalonia.AvaloniaLocator.Current.GetService<IAssetLoader>();
                    if (assets != null)
                    {
                        string innoPath = SysInfo.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar;
                        if (SysInfo.IsWindows)
                        {
                            innoPath += "innoextract.exe";
                            using (var fileStream = File.Create(innoPath))
                            {
                                assets.Open(new Uri("avares://Knossos.NET/Assets/utils/innoextract.exe")).CopyTo(fileStream);
                                fileStream.Close();
                            }
                        }
                        else
                        {
                            if (SysInfo.IsLinux)
                            {
                                if (SysInfo.CpuArch == "X64")
                                {
                                    innoPath += "innoextract.x64";
                                    using (var fileStream = File.Create(innoPath))
                                    {
                                        assets.Open(new Uri("avares://Knossos.NET/Assets/utils/innoextract.x64")).CopyTo(fileStream);
                                        fileStream.Close();
                                        SysInfo.Chmod(innoPath,"+x");
                                    }
                                }
                                if (SysInfo.CpuArch == "X86")
                                {
                                    innoPath += "innoextract.x86";
                                    using (var fileStream = File.Create(innoPath))
                                    {
                                        assets.Open(new Uri("avares://Knossos.NET/Assets/utils/innoextract.x86")).CopyTo(fileStream);
                                        fileStream.Close();
                                        SysInfo.Chmod(innoPath, "+x");
                                    }
                                }
                            }
                        }

                        var cmd = new Process();
                        var file = new FileInfo(result[0]);
                        gogExe = file.FullName;
                        cmd.StartInfo.FileName = innoPath;
                        cmd.StartInfo.Arguments = file.FullName + " -l -g";
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

                        if (count != 9)
                        {
                            //Missing files
                            gogExe = null;
                            await MessageBox.Show(MainWindow.instance!, "Unable to find all the required Freespace 2 files in gog exe.", "Files not found", MessageBox.MessageBoxButtons.OK);
                            return;
                        }
                        CanInstall = true;
                    }
                }catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "Fs2InstallerViewModel.LoadGoGExeCommand()", ex);
                }
            }
        }

        private async void LoadFolderCommand()
        {
            var dialog = new OpenFolderDialog();
            var result = await dialog.ShowAsync(MainWindow.instance!);

            if (result != null)
            {
                CanInstall = false;
                gogExe = null;
                ProcessFolder(result);
            }
        }

        private async void ProcessFolder(string path)
        {
            var fileArray = Directory.GetFiles(path, "*.vp").ToList();
            filePaths.Clear();

            if (fileArray.Any())
            {
                try
                {
                    fileArray.AddRange(Directory.GetFiles(path + Path.DirectorySeparatorChar + "data", "*.*", SearchOption.AllDirectories).ToList());
                    fileArray.AddRange(Directory.GetFiles(path + Path.DirectorySeparatorChar + "data2", "*.*", SearchOption.AllDirectories).ToList());
                    fileArray.AddRange(Directory.GetFiles(path + Path.DirectorySeparatorChar + "data3", "*.*", SearchOption.AllDirectories).ToList());
                }
                catch { }

                foreach (var reqFileName in required)
                {
                    var file = fileArray.FirstOrDefault(f => f.ToLower().Contains(reqFileName));
                    if (file != null)
                    {
                        filePaths.Add(file);
                    }
                }

                if (filePaths.Count() != 9)
                {
                    //Missing files
                    await MessageBox.Show(MainWindow.instance!, "Unable to find all the required Freespace 2 files in this directory.", "Files not found", MessageBox.MessageBoxButtons.OK);
                    return;
                }

                foreach (var otnFileName in optional)
                {
                    var file = fileArray.FirstOrDefault(f => f.ToLower().Contains(otnFileName));
                    if (file != null)
                    {
                        filePaths.Add(file);
                    }
                }

                CanInstall = true;
            }
        }
    }
}