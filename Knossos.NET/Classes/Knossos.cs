using Avalonia.Threading;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.ViewModels;
using Knossos.NET.Views;
using Microsoft.VisualBasic.FileIO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq;
using Avalonia.Platform;
using VP.NET;

namespace Knossos.NET
{
    public static class Knossos
    {
        public static readonly string AppVersion = "1.3.0";
        public readonly static string ToolRepoURL = "https://raw.githubusercontent.com/KnossosNET/Knet-Tool-Repo/main/knet_tools.json";
        public readonly static string GitHubUpdateRepoURL = "https://api.github.com/repos/KnossosNET/Knossos.NET";
        public readonly static string FAQURL = "https://raw.githubusercontent.com/KnossosNET/KNet-General-Resources-Repo/main/communityfaq.json";
        public readonly static string debugFilterURL = "https://raw.githubusercontent.com/KnossosNET/KNet-General-Resources-Repo/main/debug-filters.json";
        private static List<Mod> installedMods = new List<Mod>();
        private static List<FsoBuild> engineBuilds = new List<FsoBuild>();
        private static List<Tool> modTools = new List<Tool>();
        public static GlobalSettings globalSettings = new GlobalSettings();
        public static bool retailFs2RootFound = false;
        public static bool flagDataLoaded = false;
        private static object? ttsObject = null;
        private static bool forceUpdateDownload = false; //Only intended to test the update system!
        public static bool inPortableMode { get; private set; } = false;
        public static bool isKnDataFolderReadOnly { get; private set; } = false;
        public static bool inSingleTCMode { get; private set; } = false;
        public static bool initIsComplete {  get; private set; } = false;
        public static IDictionary<string, string> filterDisplayStrings { get; private set;} = new Dictionary<string, string>
        {
                ["total_conversion"] = "Standalone Game",
                ["retail_fs2"] = "Retail Freespace 2",
                ["fs2_mod"] = "Freespace 2 Mod",
                ["fs1_mod"] = "Freespace 1 Mod",
                ["tc_mod"] = "Standalone Game Mod",
                ["utility"] = "Utilities and Misc",
                ["dependency"] = "Dependency",
                ["asset_pack"] = "Asset Pack",
                ["demo"] = "Demo Mod",
                ["multiplayer"] = "Multiplayer Enabled",
                ["vr_mod"] = "VR",
                ["test"] = "For Testing",
        };

        /// <summary>
        /// Static constructor
        /// </summary>
        static Knossos()
        {
            try
            {
                //We are in portable mode? if so set everything up ahead of all else
                var pathToExec = KnUtils.KnetFolderPath;
                if (pathToExec != null)
                {
                    if (Directory.Exists(Path.Combine(pathToExec, "kn_portable")))
                    {
                        inPortableMode = true;
                    }
                }
            }
            catch (Exception ex)
            {
                //At this stage we can only log to console
                Log.WriteToConsole("Knossos() - " + ex.Message);
            }
            //Important!!! The first time it needs to be ran after checking if we are in portable mode or not
            //Due to the Knossos data folder path changing
            inSingleTCMode = CustomLauncher.IsCustomMode;
        }

        /// <summary>
        /// StartUp sequence
        /// </summary>
        /// <param name="isQuickLaunch"></param>
        public static async void StartUp(bool isQuickLaunch, bool forceUpdate)
        {
            forceUpdateDownload = forceUpdate;
            try
            {
                //See if the data folder works
                try
                {
                    if (!Directory.Exists(KnUtils.GetKnossosDataFolderPath()))
                    {
                        Directory.CreateDirectory(KnUtils.GetKnossosDataFolderPath());
                    }
                    else
                    {
                        // Test if we can write to the data directory
                        using (StreamWriter writer = new StreamWriter(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "test.txt"))
                        {
                            writer.WriteLine("test");
                        }
                        File.Delete(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "test.txt");
                    }
                }
                catch (Exception ex)
                {
                    isKnDataFolderReadOnly = true;
                    Log.Add(Log.LogSeverity.Error, "Knossos.StartUp()", ex);
                    if (MainWindow.instance != null)
                    {
                        await MessageBox.Show(MainWindow.instance, "Unable to write to KnossosNET data folder:\n'"+ KnUtils.GetKnossosDataFolderPath()+"'\nSome functions may not work correctly.", "KnossosNET Error", MessageBox.MessageBoxButtons.OK);
                    }
                }

                //Rename the old cache folder
                try
                {
                    var oldCachePath = Path.Combine(KnUtils.GetKnossosDataFolderPath(), "image_cache");
                    if (!Directory.Exists(KnUtils.GetCachePath()) && Directory.Exists(oldCachePath))
                    {
                        Directory.Move(oldCachePath, KnUtils.GetCachePath());
                    }
                }
                catch(Exception ex) 
                {
                    Log.Add(Log.LogSeverity.Error, "Knossos.Startup()", ex);
                }

                Log.Add(Log.LogSeverity.Information, "Knossos.StartUp()", "=== KnossosNET v" + AppVersion + " Start ===");

                if (inPortableMode)
                {
                    Log.Add(Log.LogSeverity.Information, "Knossos.StartUp()", "Running in PORTABLE MODE.");
                    try
                    {
                        Directory.CreateDirectory(KnUtils.GetFSODataFolderPath());
                        Directory.CreateDirectory(Path.Combine(KnUtils.KnetFolderPath!, "kn_portable", "Library"));
                    }
                    catch (Exception ex) 
                    {
                        Log.Add(Log.LogSeverity.Error, "Knossos.Startup()", ex);
                    }
                }

                //Load language files
                Lang.LoadFiles();

                //Load knossos config
                globalSettings.Load();
                MainWindowViewModel.Instance?.ApplySettings();


                //Print Decompressor Type
                Log.Add(Log.LogSeverity.Information, "Knossos.StartUp()", "The selected decompressor type is set to " + globalSettings.decompressor.ToString());

                //Check for updates
                if (globalSettings.checkUpdate && !isQuickLaunch)
                {
                    await CheckKnetUpdates().ConfigureAwait(false);
                    //Check for .old files and delete them
                    if (KnUtils.IsAppImage)
                    {
                        //Fire and forget
                        _ = Task.Factory.StartNew(() =>
                        {
                            CleanUpdateFiles();
                        });
                    }
                }

                //Load base path from knossos legacy
                if (globalSettings.basePath == null && !inSingleTCMode)
                {
                    globalSettings.basePath = KnUtils.GetBasePathFromKnossosLegacy();
                }

                LoadBasePath(isQuickLaunch);

                if (globalSettings.basePath == null && !isQuickLaunch && !inSingleTCMode)
                    OpenQuickSetup();
            }
            catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Knossos.StartUp", ex);
            }
        }

        /// <summary>
        /// Deletes .old files in the executable folder
        /// </summary>
        private static async void CleanUpdateFiles()
        {
            try
            {
                await Task.Delay(2000);
                if(KnUtils.IsAppImage && File.Exists(KnUtils.AppImagePath + ".old"))
                {
                    File.Delete(KnUtils.AppImagePath + "old");
                }

                /*No cleanup is needed for the other versions
                var appDirPath = KnUtils.KnetFolderPath;
                if (appDirPath != null)
                {
                    var oldFiles = System.IO.Directory.GetFiles(appDirPath, "*.old");
                    foreach (string f in oldFiles)
                    {
                        File.Delete(f);
                    }
                }
                */
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Knossos.CleanUpdateFiles()", ex);
            }
        }

        /// <summary>
        /// Launch the QuickLaunch mod and close
        /// </summary>
        private static async Task QuickLaunch()
        {
            string[] args = Environment.GetCommandLineArgs();
            string modid = string.Empty;
            string modver = string.Empty;
            string modExecType = string.Empty;
            string toolName = string.Empty;
            string buildid = string.Empty;
            string buildver = string.Empty;
            string? cmdline = null;
            bool saveID = false;
            bool saveVer = false;
            bool saveExecType = false;
            bool toolID = false;
            bool saveBuildID = false;
            bool saveBuildVer = false;
            bool saveCmdline = false;

            foreach (var arg in args)
            {
                Log.Add(Log.LogSeverity.Information, "Knossos.StartUp", "Knet Cmdline Arg: " + arg);
                if (toolID)
                {
                    toolID = false;
                    toolName = arg;
                }
                if (saveID)
                {
                    saveID = false;
                    modid = arg;
                }
                if (saveVer)
                {
                    saveVer = false;
                    modver = arg;
                }
                if (saveExecType)
                {
                    saveExecType = false;
                    modExecType = arg;
                }
                if (saveBuildID)
                {
                    saveBuildID = false;
                    buildid = arg;
                }
                if (saveBuildVer)
                {
                    saveBuildVer = false;
                    buildver = arg;
                }
                if (saveCmdline)
                {
                    saveCmdline = false;
                    cmdline = arg;
                }
                if (arg.ToLower() == "-playmod")
                {
                    saveID = true;
                }
                if (arg.ToLower() == "-tool")
                {
                    toolID = true;
                }
                if (arg.ToLower() == "-version")
                {
                    saveVer = true;
                }
                if (arg.ToLower() == "-exec")
                {
                    saveExecType = true;
                }
                if (arg.ToLower() == "-build")
                {
                    saveBuildID = true;
                }
                if (arg.ToLower() == "-build-version")
                {
                    saveBuildVer = true;
                }
                if (arg.ToLower() == "-cmdline")
                {
                    saveCmdline = true;
                }
            }

            if (toolName != string.Empty)
            {
                var tool = GetTools().FirstOrDefault(x=> x.name.ToLower() == toolName.ToLower());
                try
                {
                    if (tool != null)
                    {
                        _ = tool.Open();
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Error, "Knossos.QuickLaunch", "Quick launch was used but the tool was not found. Used tool name: " + toolName);
                    }
                }
                catch(Exception ex) 
                {
                    Log.Add(Log.LogSeverity.Error, "Knossos.QuickLaunch", ex);
                }
            }
            FsoBuild? fsoBuild = null;
            if (buildver != string.Empty)
            {
                //we have a version
                if (buildid == string.Empty)
                    buildid = "fso";
                switch (buildver.ToLower())
                {
                    case "newerstable":
                        fsoBuild = GetInstalledBuildsList().Where(x => x.id.ToLower() == buildid.ToLower() && x.stability == FsoStability.Stable)?.MaxBy(x => new SemanticVersion(x.version));
                        break;
                    case "newerrc":
                        fsoBuild = GetInstalledBuildsList().Where(x => x.id.ToLower() == buildid.ToLower() && x.stability == FsoStability.RC)?.MaxBy(x => new SemanticVersion(x.version));
                        break;
                    case "newernightly":
                        fsoBuild = GetInstalledBuildsList().Where(x => x.id.ToLower() == buildid.ToLower() && x.stability == FsoStability.Nightly)?.MaxBy(x => new SemanticVersion(x.version));
                        break;
                    default:
                        fsoBuild = GetInstalledBuildsList().FirstOrDefault(x => x.id.ToLower() == buildid.ToLower() && x.version == buildver);
                        break;
                }
            }
            else if(buildid != string.Empty)
            {
                //no version, pick whatever is newer for this id
                fsoBuild = GetInstalledBuildsList().Where(x => x.id.ToLower() == buildid.ToLower())?.MaxBy(x => new SemanticVersion(x.version));
            }

            if (buildid != string.Empty && fsoBuild == null)
            {
                Log.Add(Log.LogSeverity.Error, "Knossos.QuickLaunch", "Quick launch was used but we could not find a FSO build with this ID: " + buildid + " and version: " + buildver == null ? "Any" : buildver );
            }

            if(modid != string.Empty)
            {
                var execType = FsoExecType.Release;
                if (modExecType != string.Empty && modExecType.ToLower() != "default")
                {
                    execType = FsoBuild.GetExecType(modExecType);
                    if(execType == FsoExecType.Unknown)
                    {
                        Log.Add(Log.LogSeverity.Error, "Knossos.QuickLaunch", "Quick launch was used but the exec type used was invalid, reverted to Release. Used exec type: " + modExecType);
                        execType = FsoExecType.Release;
                    }
                }
                if (modver != string.Empty)
                {
                    var mod = GetInstalledMod(modid, modver);

                    if(mod != null)
                    {
                        PlayMod(mod, execType, false, 0, false, fsoBuild, cmdline);
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Error, "Knossos.QuickLaunch", "Quick launch was used but the modid and modver was not found. Used modid: "+modid + " modver: "+modver);
                    }
                }
                else
                {
                    var modlist = GetInstalledModList(modid);
                    if (modlist != null && modlist.Count() > 0)
                    {
                        Mod? mod = null;
                        if (modlist.Count() > 1)
                        {
                            foreach (var m in modlist)
                            {
                                if (mod == null || SemanticVersion.Compare(m.version, mod.version) > 0)
                                {
                                    mod = m;
                                }
                            }
                        }
                        else
                        {
                            mod = modlist[0];
                        }

                        if(mod != null)
                        {
                            PlayMod(mod, execType, false, 0, false, fsoBuild, cmdline);
                        }
                    }
                    else
                    {
                        Log.Add(Log.LogSeverity.Error, "Knossos.QuickLaunch", "Quick launch was used but the modid was not found. Used modid: " + modid);
                    }
                }
            }
            else
            {
                Log.Add(Log.LogSeverity.Error, "Knossos.QuickLaunch", "Quick launch was used but the modid was not detected.");
            }
            await Task.Delay(2000);
            if (MainWindow.instance != null)
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    MainWindow.instance.Close();
                });
            }
        }

        /// <summary>
        /// Connects to KNet repo and check for updates
        /// </summary>
        private static async Task CheckKnetUpdates()
        {
            try
            {
                if (forceUpdateDownload)
                    Console.WriteLine("Starting Update Process");
                var latest = await GitHubApi.GetLastRelease().ConfigureAwait(false);
                if (latest != null && latest.tag_name != null)
                {
                    if (forceUpdateDownload || SemanticVersion.Compare(AppVersion, latest.tag_name.ToLower().Replace("v", "").Trim()) <= -1)
                    {
                        if (latest.assets != null)
                        {
                            GitHubReleaseAsset? releaseAsset = null;
                            foreach (GitHubReleaseAsset a in latest.assets)
                            {
                                if (KnUtils.IsAppImage)
                                {
                                    if (a.name != null)
                                    {
                                        if ((KnUtils.CpuArch.ToLower() == "x64" && a.name.EndsWith("x86_64.AppImage")) || (KnUtils.CpuArch.ToLower() == "arm64" && a.name.EndsWith("aarch64.AppImage")))
                                        {
                                            releaseAsset = a;
                                            break;
                                        }
                                        if ((KnUtils.CpuArch.ToLower() == "riscv32" && a.name.EndsWith("riscv32.AppImage")) || (KnUtils.CpuArch.ToLower() == "riscv64" && a.name.EndsWith("riscv64.AppImage")))
                                        {
                                            releaseAsset = a;
                                            break;
                                        }
                                    }
                                }
                                else if (KnUtils.WasInstallerUsed())
                                {
                                    if (a.name != null)
                                    {
                                        if (KnUtils.IsWindows && a.name.ToLower().EndsWith(".exe"))
                                        {
                                            if (a.name.ToLower().Contains(KnUtils.CpuArch.ToLower()) && ( KnUtils.CpuArch != "Arm" || KnUtils.CpuArch == "Arm" && !a.name.ToLower().Contains("arm64")))
                                            {
                                                releaseAsset = a;
                                                break;
                                            }
                                        }

                                        if (KnUtils.IsMacOS && a.name.ToLower().EndsWith(".dmg"))
                                        {
                                            releaseAsset = a;
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    if(a.name != null && ( a.name.ToLower().Contains(".zip") || a.name.ToLower().Contains(".7z") || a.name.ToLower().Contains(".tar.gz")))
                                    {
                                        if(a.name.ToLower().Contains(KnUtils.GetOSNameString().ToLower()))
                                        {
                                            if (a.name.ToLower().Contains(KnUtils.CpuArch.ToLower()) && ( KnUtils.CpuArch != "Arm" || KnUtils.CpuArch == "Arm" && !a.name.ToLower().Contains("arm64")))
                                            {
                                                releaseAsset = a;
                                                continue;
                                            }
                                        }
                                    }
                                }
                            }

                            if (forceUpdateDownload)
                                Console.WriteLine("Found Release: " + latest.tag_name);

                            if (releaseAsset != null && releaseAsset.browser_download_url != null)
                            {
                                if(globalSettings.ignoredLauncherUpdates.Contains(latest.tag_name))
                                {
                                    Log.Add(Log.LogSeverity.Information, "Knossos.CheckKnetUpdates()", "Launcher update: " + latest.tag_name + " is available, but it was skipped because it is on the ignore list.");
                                    return;
                                }
                                if (!forceUpdateDownload && !globalSettings.autoUpdate)
                                {
                                    MessageBox.MessageBoxResult result = MessageBox.MessageBoxResult.Cancel;
                                    await Dispatcher.UIThread.Invoke(async () => {
                                        result = await MessageBox.Show(null, "Knossos.NET " + latest.tag_name + ":\n" + latest.body + "\n\n\nIf you continue Knossos.NET will be re-started after download.", "An update is available", MessageBox.MessageBoxButtons.ContinueCancelSkipVersion);
                                    }).ConfigureAwait(false);
                                    if(result == MessageBox.MessageBoxResult.SkipVersion)
                                    {
                                        globalSettings.ignoredLauncherUpdates.Add(latest.tag_name);
                                        globalSettings.Save();
                                    }
                                    if (result != MessageBox.MessageBoxResult.Continue)
                                    {
                                        return;
                                    }
                                }
                                else
                                {
                                    await Task.Delay(500).ConfigureAwait(false);
                                }
                                if (forceUpdateDownload)
                                    Console.WriteLine("Starting Download: " + releaseAsset.browser_download_url);
                                var extension = Path.GetExtension(releaseAsset.browser_download_url);
                                //little hack to pickup tar.gz
                                if(extension != null && extension.ToLower() == ".gz" && releaseAsset.browser_download_url.Contains(".tar.gz")) 
                                {
                                    extension = ".tar.gz";
                                }
                                var download = await Dispatcher.UIThread.InvokeAsync(async () => await TaskViewModel.Instance!.AddFileDownloadTask(releaseAsset.browser_download_url, KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "update"+ extension, "Downloading "+latest.tag_name+" "+releaseAsset.name, true, "This is a Knossos.NET update"), DispatcherPriority.Background).ConfigureAwait(false);
                                if (download != null && download == true)
                                {
                                    var appDirPath = Path.GetDirectoryName(Environment.ProcessPath);
                                    var execFullPath = Environment.ProcessPath;

                                    //Decompress new files / 2nd phase
                                    try
                                    {
                                        // no extraction needed for AppImage, just move exec over and restart
                                        // NOTE: exeName is already the full path to the AppImage
                                        if (KnUtils.IsAppImage)
                                        {
                                            // change exec name to be the AppImage itself (is full path!)
                                            execFullPath = KnUtils.AppImagePath;

                                            try
                                            {
                                                var appFolder = KnUtils.KnetFolderPath;
                                                if (appFolder == null)
                                                {
                                                    throw new ArgumentNullException(nameof(appFolder));
                                                }
                                                //Move Current Executables
                                                if (forceUpdateDownload)
                                                    Console.WriteLine("Renaming current executable files");

                                                File.Move(execFullPath!, execFullPath + ".old", true);

                                                // set new filename to match old one, in case it was renamed by the user
                                                var newFileFullPath = Path.Combine(appFolder, Path.GetFileName(execFullPath));
                                                File.Move(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "update" + extension, newFileFullPath);

                                                //Start again
                                                KnUtils.Chmod(newFileFullPath, "+x");

                                                if (forceUpdateDownload)
                                                    Console.WriteLine("Launching new version");

                                                Process p = new Process();
                                                p.StartInfo.FileName = newFileFullPath;
                                                p.Start();

                                                if (forceUpdateDownload)
                                                    Console.WriteLine("Closing...");

                                                //Close App
                                                Dispatcher.UIThread.Invoke(() =>
                                                {
                                                    MainWindow.instance!.Close();
                                                });
                                            }
                                            catch (Exception ex)
                                            {
                                                //Rollback
                                                try
                                                {
                                                    File.Move(execFullPath + ".old", execFullPath!, true);
                                                }
                                                catch { }
                                                Log.Add(Log.LogSeverity.Error, "Knossos.CheckKnetUpdates()", ex);
                                            }
                                        }
                                        else
                                        {
                                            var updateFilesFolder = Path.Combine(KnUtils.GetKnossosDataFolderPath(), "kn_update");

                                            Directory.CreateDirectory(updateFilesFolder);

                                            if (KnUtils.WasInstallerUsed())
                                            {
                                                File.Move(Path.Combine(KnUtils.GetKnossosDataFolderPath(), "update" + extension), Path.Combine(updateFilesFolder, "update" + extension), true);
                                            }
                                            else
                                            {
                                                //Regular Portable Format
                                                if (forceUpdateDownload)
                                                    Console.WriteLine("Decompressing update...");

                                                var result = false;

                                                await Dispatcher.UIThread.Invoke(async () =>
                                                {
                                                    result  = await TaskViewModel.Instance!.AddFileDecompressionTask( KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "update" + extension, updateFilesFolder, false);
                                                }).ConfigureAwait(false);

                                                if (!result)
                                                {
                                                    throw new Exception("Error while decompressing update file: " + KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "update" + extension);
                                                }

                                                if (forceUpdateDownload)
                                                    Console.WriteLine("Delete update file");

                                                //Cleanup file
                                                try
                                                {
                                                    if (File.Exists(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "update" + extension))
                                                        File.Delete(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "update" + extension);
                                                }
                                                catch { }
                                            }

                                            //Update Script
                                            if (forceUpdateDownload)
                                                Console.WriteLine("Exporting update script to " + KnUtils.GetKnossosDataFolderPath());

                                            Process process = new Process();
                                            process.StartInfo.WorkingDirectory = KnUtils.GetKnossosDataFolderPath();
                                            process.StartInfo.EnvironmentVariables["update_folder"] = updateFilesFolder;
                                            process.StartInfo.EnvironmentVariables["app_path"] = appDirPath;
                                            process.StartInfo.EnvironmentVariables["app_name"] = Path.GetFileName(execFullPath);

                                            if (KnUtils.WasInstallerUsed())
                                            {
                                                process.StartInfo.EnvironmentVariables["use_installer"] = "1";
                                            }

                                            if (KnUtils.IsWindows)
                                            {
                                                var scriptPath = Path.Combine(KnUtils.GetKnossosDataFolderPath(), "update_windows.cmd");
                                                using (var fileStream = File.Create(scriptPath))
                                                {
                                                    AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/scripts/update_windows.cmd")).CopyTo(fileStream);
                                                    fileStream.Close();
                                                }
                                                process.StartInfo.FileName = scriptPath;
                                                process.StartInfo.Arguments = "> update.log"; //Create logfile to catch script output in Knossos data folder path
                                            }

                                            if (KnUtils.IsLinux || (KnUtils.IsMacOS && !KnUtils.WasInstallerUsed()))
                                            {
                                                var scriptPath = Path.Combine(KnUtils.GetKnossosDataFolderPath(), "update_generic.sh");
                                                using (var fileStream = File.Create(scriptPath))
                                                {
                                                    AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/scripts/update_generic.sh")).CopyTo(fileStream);
                                                    fileStream.Close();
                                                }
                                                KnUtils.Chmod(scriptPath);
                                                process.StartInfo.FileName = scriptPath;
                                                process.StartInfo.Arguments = "> update.log"; //Create logfile to catch script output in Knossos data folder path
                                            }

                                            if (KnUtils.IsMacOS && KnUtils.WasInstallerUsed())
                                            {
                                                // these variables need to be modified to be correct for the app bundle
                                                var cutOff = execFullPath!.IndexOf(".app") + 4;
                                                var realName = execFullPath![..cutOff];
                                                process.StartInfo.EnvironmentVariables["app_path"] = Path.GetDirectoryName(realName);
                                                process.StartInfo.EnvironmentVariables["app_name"] = Path.GetFileName(realName);

                                                var scriptPath = Path.Combine(KnUtils.GetKnossosDataFolderPath(), "update_macapp.sh");
                                                using (var fileStream = File.Create(scriptPath))
                                                {
                                                    AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/scripts/update_macapp.sh")).CopyTo(fileStream);
                                                    fileStream.Close();
                                                }
                                                KnUtils.Chmod(scriptPath);
                                                process.StartInfo.FileName = scriptPath;
                                                process.StartInfo.Arguments = "> update.log"; //Create logfile to catch script output in Knossos data folder path
                                            }

                                            if (forceUpdateDownload)
                                                Console.WriteLine("Starting update script");

                                            process.StartInfo.CreateNoWindow = true;
                                            process.Start();
                                            
                                            if (forceUpdateDownload)
                                                Console.WriteLine("Closing...");

                                            //Close App
                                            Dispatcher.UIThread.Invoke(() =>
                                            {
                                                MainWindow.instance!.Close();
                                            });
                                        }
                                    }
                                    catch(Exception ex)
                                    {
                                        Dispatcher.UIThread.Invoke(() => { 
                                            MessageBox.Show(MainWindow.instance, "An error has ocurred during the 2nd phase of the update. The operation was cancelled.", "Update error", MessageBox.MessageBoxButtons.OK);
                                        });
                                        Log.Add(Log.LogSeverity.Error, "Knossos.CheckKnetUpdates()", ex);
                                    }
                                }
                            }
                            else
                            {
                                Log.Add(Log.LogSeverity.Warning, "Knossos.CheckKnetUpdates()", "Unable to find a matching OS and cpu arch build in the latest Github release, update has been skipped. CPU Arch:"+ KnUtils.CpuArch + " OS: "+ KnUtils.GetOSNameString());
                                if (forceUpdateDownload)
                                    Console.WriteLine("Update Error! No matching CPU / OS arch. Current: " + KnUtils.CpuArch + " " + KnUtils.GetOSNameString());
                            }
                        }
                        else
                        {
                            Log.Add(Log.LogSeverity.Error, "Knossos.CheckKnetUpdates()", "Last version from Github has a null asset array.");
                            if (forceUpdateDownload)
                                Console.WriteLine("Update Error! Knossos.CheckKnetUpdates()", "Last version from Github has a null asset array.");
                        }
                    }
                }
                else
                {
                    Log.Add(Log.LogSeverity.Error, "Knossos.CheckKnetUpdates()", "Get latest version from GitHub resulted in null or tag_name being null.");
                    if (forceUpdateDownload)
                        Console.WriteLine("Update Error! Get latest version from GitHub resulted in null or tag_name being null.");
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Knossos.CheckKnetUpdates()", ex);
                if (forceUpdateDownload)
                    Console.WriteLine("Update Error! " + ex.Message);
            }
        }

        /// <summary>
        /// Shows Quick Setup Guide
        /// </summary>
        public static void OpenQuickSetup()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var dialog = new QuickSetupView();
                dialog.DataContext = new QuickSetupViewModel(dialog);
                dialog.Show(MainWindow.instance!);
            });
        }

        /// <summary>
        /// Resets the current library path, clears all loaded data and mods
        /// </summary>
        public static void ResetBasePath()
        {
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                MainWindowViewModel.Instance?.ClearViews();
                installedMods.Clear();
                engineBuilds.Clear();
                retailFs2RootFound = false;
                TaskViewModel.Instance?.CancelAllRunningTasks();
                Nebula.CancelOperations();
                await Task.Delay(2000).ConfigureAwait(false);
                KnUtils.ClearDownloadedRemoteResourceList();
                LoadBasePath();
            });
        }

        /// <summary>
        /// Loads knossos library and connects to Nebula
        /// </summary>
        /// <param name="isQuickLaunch"></param>
        public async static void LoadBasePath(bool isQuickLaunch = false)
        {
            if (globalSettings.basePath != null)
            {
                //Clear Mod Tags
                ModTags.Clear();
                MainWindowViewModel.Instance?.tagFilter.Clear();

                await FolderSearchRecursive(globalSettings.basePath, isQuickLaunch).ConfigureAwait(false);

                Log.Add(Log.LogSeverity.Information, "Knossos.LoadBasePath()", "Loaded: " + installedMods.Count() +" installed mods or tcs and " + engineBuilds.Count() + " engine builds." );

                if (!isQuickLaunch)
                {
                    //Sort/Re-sort installed mods
                    MainWindowViewModel.Instance?.InstalledModsView?.ChangeSort(globalSettings.sortType);

                    //Red border for mod with missing deps
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        MainWindowViewModel.Instance?.RunModStatusChecks();
                    });

                    //Load config options to view, must be done after loading the fso builds due to flag data
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        MainWindowViewModel.Instance?.GlobalSettingsLoadData();
                    });

                    //Enter the nebula
                    //Note: this has to be done after scanning the local folder, fire and forget
                    await Task.Run(async () => { 
                        await Nebula.Trinity();
                        //Auto-Update FSO Builds function, has to run after the repo is loaded
                        _ = Task.Run(() => AutoUpdateBuilds());
                    }).ConfigureAwait(false);
                }
                else
                {
                    await QuickLaunch();
                }

                //generate mod tag buttons
                Dispatcher.UIThread.Invoke(() =>
                {
                    NebulaModListView.Instance?.GenerateFilterButtons();
                    ModListView.Instance?.GenerateFilterButtons();
                });

                initIsComplete = true;
            }
        }

        /// <summary>
        /// Check for updates if FSO Stable, RC and Nightly builds if needed
        /// Note: RCs are only installed IF they are newer than the newerest stable in nebula
        /// </summary>
        private static void AutoUpdateBuilds()
        {
            try
            {
                //Note: we are getting the data directly from the builds loaded into the UI as repo is already loaded at this point
                if (Nebula.repoLoaded && FsoBuildsViewModel.Instance != null)
                {
                    //Stables
                    if (globalSettings.autoUpdateBuilds.UpdateStable)
                    {
                        Log.Add(Log.LogSeverity.Information, "Knossos.AutoUpdateBuilds()", "Checking for new stable FSO builds.");

                        var bestInstalled = GetInstalledBuildsList("FSO", FsoStability.Stable).MaxBy(x => new SemanticVersion(x.version));
                        var bestNebula = FsoBuildsViewModel.Instance.StableItems.Where(x => !x.IsInstalled && x.build != null).MaxBy(x => new SemanticVersion(x.build!.version));

                        if ((bestInstalled == null && bestNebula != null) || (bestInstalled != null && bestNebula != null && 
                            new SemanticVersion(bestNebula!.build!.version) > new SemanticVersion(bestInstalled!.version)))
                        {
                            //Update
                            Log.Add(Log.LogSeverity.Information, "Knossos.AutoUpdateBuilds()", "Found a newer stable build, installing: " + bestNebula.build);
                            if (bestNebula.build!.modData != null)
                                bestNebula.DownloadBuildExternal(bestNebula.build.modData, globalSettings.autoUpdateBuilds.DeleteOlder);
                        }
                    }
                    //Nightly
                    if (globalSettings.autoUpdateBuilds.UpdateNightly)
                    {
                        Log.Add(Log.LogSeverity.Information, "Knossos.AutoUpdateBuilds()", "Checking for new nightly FSO builds.");

                        var bestInstalled = GetInstalledBuildsList("FSO", FsoStability.Nightly).MaxBy(x => new SemanticVersion(x.version));
                        var bestNebula = FsoBuildsViewModel.Instance.NightlyItems.Where(x => !x.IsInstalled && x.build != null).MaxBy(x => new SemanticVersion(x.build!.version));

                        if ((bestInstalled == null && bestNebula != null) || (bestInstalled != null && bestNebula != null &&
                            new SemanticVersion(bestNebula!.build!.version) > new SemanticVersion(bestInstalled!.version)))
                        {
                            //Update
                            Log.Add(Log.LogSeverity.Information, "Knossos.AutoUpdateBuilds()", "Found a newer nightly build, installing: " + bestNebula.build);
                            if (bestNebula.build!.modData != null)
                                bestNebula.DownloadBuildExternal(bestNebula.build.modData, globalSettings.autoUpdateBuilds.DeleteOlder);
                        }
                    }
                    //RC
                    if (globalSettings.autoUpdateBuilds.UpdateRC)
                    {
                        Log.Add(Log.LogSeverity.Information, "Knossos.AutoUpdateBuilds()", "Checking for new RC FSO builds.");

                        var bestInstalled = GetInstalledBuildsList("FSO", FsoStability.RC).MaxBy(x => new SemanticVersion(x.version));
                        var bestNebula = FsoBuildsViewModel.Instance.RcItems.Where(x => !x.IsInstalled && x.build != null).MaxBy(x => new SemanticVersion(x.build!.version));
                        var bestNebulaStable = FsoBuildsViewModel.Instance.StableItems.Where(x => x.build != null).MaxBy(x => new SemanticVersion(x.build!.version));

                        if (bestNebulaStable != null && bestInstalled != null && bestNebula != null && 
                            new SemanticVersion(bestNebula!.build!.version) > new SemanticVersion(bestInstalled!.version))
                        {
                            if (new SemanticVersion(bestNebula!.build!.version) > new SemanticVersion(bestNebulaStable.build!.version))
                            {
                                //Update
                                Log.Add(Log.LogSeverity.Information, "Knossos.AutoUpdateBuilds()", "Found a newer RC build, installing: " + bestNebula.build);
                                if (bestNebula.build!.modData != null)
                                    bestNebula.DownloadBuildExternal(bestNebula.build.modData, globalSettings.autoUpdateBuilds.DeleteOlder);
                            }
                            else
                            {
                                //Older than stable, skip
                                Log.Add(Log.LogSeverity.Information, "Knossos.AutoUpdateBuilds()", "The newer RC build: " + bestNebula.build + " Is older than the newer stable in nebula: " + bestNebulaStable.build + " . Skipping.");
                            }
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Knossos.AutoUpdateBuilds()", ex);
            }
        }

        /// <summary>
        /// Get fullpath to Knossos library from the settings
        /// </summary>
        /// <returns>fullpath or null if not set</returns>
        public static string? GetKnossosLibraryPath()
        {
            return globalSettings.basePath;
        }

        /// <summary>
        /// Plays a mod or starts a standalone server
        /// </summary>
        /// <param name="mod"></param>
        /// <param name="fsoExecType"></param>
        /// <param name="standaloneServer"></param>
        /// <param name="standalonePort"></param>
        public static async void PlayMod(Mod mod, FsoExecType fsoExecType, bool standaloneServer = false, int standalonePort = 0, bool vrMode = false, FsoBuild? specifiedBuild = null, string? additionalCmd = null)
        {
            if (TaskViewModel.Instance?.IsSafeState() == false)
            {
                var result = await MessageBox.Show(MainWindow.instance!, "Other important tasks are running, it is recommended that you wait until they finish before launching the game because it may cause them to fail.\nIf you are absolutely sure those tasks cannot interfere you can continue.", "Tasks are running", MessageBox.MessageBoxButtons.ContinueCancel);
                if(result != MessageBox.MessageBoxResult.Continue)
                    return;
            }

            Log.Add(Log.LogSeverity.Information, "Knossos.PlayMod()", "Launching Mod: " + mod.folderName);

            /* Check the dependencies and stop if there are unresolved ones */
            var missingDeps = mod.GetMissingDependenciesList(false,true);
            var errorMsg = string.Empty;

            foreach (var dependency in missingDeps.ToList())
            {
                //If we are missing the FSO dep and the user selected a custom build, ignore it
                if (mod.modSettings.customBuildId != null && dependency.id == "FSO")
                {
                    missingDeps.Remove(dependency);
                }
                else
                {
                    Log.Add(Log.LogSeverity.Warning, "Knossos.PlayMod()", "Unable to resolve dependency : " + dependency.id + " - " + dependency.version);
                    errorMsg += "\n" + "Unable to resolve dependency : " + dependency.id + " - " + dependency.version;
                    if (dependency.packages != null)
                    {
                        var selected = dependency.SelectMod();
                        foreach (string okg in dependency.packages)
                        {
                            if (selected != null)
                            {
                                Log.Add(Log.LogSeverity.Warning, "Knossos.PlayMod()", "Missing Package for : " + dependency.id + " - " + selected.version + " - PKG NAME: " + okg);
                                errorMsg += "\n" + "Missing Package for : " + dependency.id + " - " + selected.version + " - PKG NAME: " + okg;
                            }
                            else
                            {
                                Log.Add(Log.LogSeverity.Warning, "Knossos.PlayMod()", "Missing Package for : " + dependency.id + " - PKG NAME: " + okg);
                                errorMsg += "\n" + "Missing Package for : " + dependency.id + " - PKG NAME: " + okg;
                            }
                        }
                    }
                }
            }

            if(missingDeps.Count > 0)
            {
                if (MainWindow.instance != null)
                {
                    await MessageBox.Show(MainWindow.instance, errorMsg, "Unable to resolve dependencies", MessageBox.MessageBoxButtons.OK);
                }

                return;
            }


            /* Build cmdline */
            var cmdline = "-parse_cmdline_only";
            var modFlag = string.Empty;
            var modList = new List<Mod>();
            FsoBuild? fsoBuild = specifiedBuild;

            if(mod.modSettings.noIngameOptions)
            {
                cmdline += " -no_ingame_options";
            }

            if(additionalCmd != null)
            {
                cmdline += " " + additionalCmd;
            }

            /* VR Mode Stuff */
            if(vrMode)
            {
                cmdline += " -vr";
            }

            /* Resolve Dependencies should be all valid at this point */
            var dependencyList = mod.GetModDependencyList(false,true);
            bool hasBuildDependency = false;
            if (dependencyList != null)
            {
                foreach (var dep in dependencyList)
                {
                    var selectedMod = dep.SelectMod();
                    if (selectedMod != null)
                    {
                        modList.Add(selectedMod);
                    }
                    else
                    {
                        /* 
                            It has to be the engine dependency, "SelectMod" does not select engine builds
                            Do not do this if there is a user selected mod-especific build
                        */
                        if (fsoBuild == null && mod.modSettings.customBuildId == null)
                        {
                            hasBuildDependency = true;
                            fsoBuild = dep.SelectBuild(true);
                        }
                    }
                }

                /* Check if there is a dependency conflicts (if more than one dependency with the same ID exist at this point) */
                var queryConflict = dependencyList.GroupBy(x => x.id).Where(g => g.Count() > 1).ToList();
                if (queryConflict.Count() > 0)
                {
                    var outputString = "There is a dependency conflict for this mod: " + mod + " KnossosNET will try to adjust but the mod may present issues or not work at all. \nThis may be be resolved manually using custom dependencies for this mod.\n";
                    foreach (var conflictGroup in queryConflict)
                    {
                        foreach (var conflictDep in conflictGroup)
                        {
                            var pkg = mod.packages.FirstOrDefault(pkg => pkg.dependencies != null && pkg.dependencies.Contains(conflictDep));
                            outputString += "\n\nPackage: "+ (pkg != null? pkg.name : "") +"\nDependency Id: " + conflictDep.id + " Version: " + conflictDep.version; 
                        }
                    }
                    Log.Add(Log.LogSeverity.Warning, "Knossos.PlayMod()", outputString);
                    if (MainWindow.instance != null)
                    {
                        var result = await MessageBox.Show(MainWindow.instance, outputString, "Dependency Conflict!", MessageBox.MessageBoxButtons.ContinueCancel);
                        if (result != MessageBox.MessageBoxResult.Continue)
                            return;
                    }
                }
            }

            /* Search for the user-especified FSO build */
            if (fsoBuild == null && mod.modSettings.customBuildId != null && mod.modSettings.customBuildVersion != null)
            {
                if (mod.modSettings.customBuildExec == null)
                {
                    fsoBuild = engineBuilds.FirstOrDefault(builds => builds.id == mod.modSettings.customBuildId && builds.version == mod.modSettings.customBuildVersion);
                }
                else
                {
                    fsoBuild = new FsoBuild(mod.modSettings.customBuildExec);
                }

                if(fsoBuild == null)
                {
                    Log.Add(Log.LogSeverity.Warning, "Knossos.PlayMod()", "Unable to find the user selected build for mod " + mod.title + " " + mod.version + " requested build " + mod.modSettings.customBuildId + " " + mod.modSettings.customBuildVersion);
                }
            }

            /* Determine root folder */
            var rootPath = string.Empty;

            /* Others TCs: WCS, Solaris, etc */
            if (mod.type == ModType.tc && mod.parent == null)
            {
                rootPath = mod.fullPath.Replace(mod.folderName+Path.DirectorySeparatorChar,"");
            }
            else
            {
                /* Most likely FS2 Retail only */
                if (mod.type == ModType.tc && mod.parent != null || mod.parent == mod.id)
                {
                    rootPath = mod.fullPath;
                }
                else
                {
                    /* Regular mods */
                    if(mod.type == ModType.mod && mod.parent != null)
                    {
                        foreach(var installedMod in installedMods) 
                        { 
                            if(installedMod.type == ModType.tc && installedMod.id == mod.parent)
                            {
                                if (installedMod.parent != null)
                                {
                                    rootPath = installedMod.fullPath;
                                }
                                else
                                {
                                    rootPath = mod.fullPath.Replace(mod.folderName + Path.DirectorySeparatorChar, "");
                                }
                            }
                        }
                    }
                }
            }

            if(rootPath == string.Empty)
            {
                Log.Add(Log.LogSeverity.Error, "Knossos.PlayMod()", "Unable to determine working folder for mod: " + mod.folderName);
                if (MainWindow.instance != null)
                {
                    await MessageBox.Show(MainWindow.instance, "Unable to determine working folder for mod: " + mod.folderName, "Error launching mod", MessageBox.MessageBoxButtons.OK);
                }
                return;
            }
            else
            {
                Log.Add(Log.LogSeverity.Information, "Knossos.PlayMod()", "Used working folder : " + rootPath);
            }

            /* Build mod flag */
            foreach (var modid in mod.GetModFlagList())
            {
                /* Load this mod */
                if (modid == mod.id)
                {
                    /* Dev Mode ON */
                    if (mod.devMode)
                    {
                        foreach (var pkg in mod.packages.OrderBy(x => x.name))
                        {
                            if (pkg.isEnabled)
                            {
                                if (modFlag.Length > 0)
                                {
                                    modFlag += "," + Path.GetRelativePath(rootPath, mod.fullPath + Path.DirectorySeparatorChar + pkg.folder).TrimEnd('/').TrimEnd('\\');
                                }
                                else
                                {
                                    modFlag += Path.GetRelativePath(rootPath, mod.fullPath + Path.DirectorySeparatorChar + pkg.folder).TrimEnd('/').TrimEnd('\\');
                                }
                            }
                        }

                        //If standalone the multi.cfg will be on mod root\data to avoid uploading it to nebula
                        if (standaloneServer)
                        {
                            if (modFlag.Length > 0)
                            {
                                modFlag += "," + Path.GetRelativePath(rootPath, mod.fullPath).TrimEnd('/').TrimEnd('\\');
                            }
                            else
                            {
                                modFlag += Path.GetRelativePath(rootPath, mod.fullPath).TrimEnd('/').TrimEnd('\\');
                            }
                        }
                    }
                    else
                    {
                        //avoid passing FS2 in modflag because fs2retail runs off the working folder
                        if (mod.id != "FS2")
                        {
                            if (modFlag.Length > 0)
                            {
                                modFlag += "," + mod.folderName;
                            }
                            else
                            {
                                modFlag += mod.folderName;
                            }
                        }
                    }
                }
                else
                {
                    var depMod = modList.FirstOrDefault(d => d.id == modid);
                    //Found the modflag id in mod dependencies list
                    if (depMod != null)
                    {
                        /* Dev Mode ON */
                        if (depMod.devMode)
                        {
                            foreach (var pkg in depMod.packages)
                            {
                                if (modFlag.Length > 0)
                                {
                                    modFlag += "," + Path.GetRelativePath(rootPath, depMod.fullPath + Path.DirectorySeparatorChar + pkg.folder).TrimEnd('/').TrimEnd('\\');
                                }
                                else
                                {
                                    modFlag += Path.GetRelativePath(rootPath, depMod.fullPath + Path.DirectorySeparatorChar + pkg.folder).TrimEnd('/').TrimEnd('\\');
                                }
                            }
                        }
                        else
                        {
                            if (modFlag.Length > 0)
                            {
                                modFlag += "," + Path.GetRelativePath(rootPath, depMod.fullPath).TrimEnd('/').TrimEnd('\\');
                            }
                            else
                            {
                                modFlag += Path.GetRelativePath(rootPath, depMod.fullPath).TrimEnd('/').TrimEnd('\\');
                            }
                        }
                    }
                    else
                    {
                        //Unofficial feature
                        //https://github.com/KnossosNET/Knossos.NET/issues/195
                        //Try to load a mod thats not on the dependencies list, but it is on the modflag list, if found installed
                        try
                        {
                            var optionalDep = Knossos.GetInstalledModList(modid);
                            if (optionalDep != null && optionalDep.Count() > 0)
                            {
                                var newerOpt = optionalDep.MaxBy(o =>  new SemanticVersion(o.version));
                                if(newerOpt != null)
                                {
                                    Log.Add(Log.LogSeverity.Information, "Knossos.PlayMod()", "Loading optional dependency: " + newerOpt);
                                    /* Dev Mode ON */
                                    if (newerOpt.devMode)
                                    {
                                        foreach (var pkg in newerOpt.packages)
                                        {
                                            if (modFlag.Length > 0)
                                            {
                                                modFlag += "," + Path.GetRelativePath(rootPath, newerOpt.fullPath + Path.DirectorySeparatorChar + pkg.folder).TrimEnd('/').TrimEnd('\\');
                                            }
                                            else
                                            {
                                                modFlag += Path.GetRelativePath(rootPath, newerOpt.fullPath + Path.DirectorySeparatorChar + pkg.folder).TrimEnd('/').TrimEnd('\\');
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (modFlag.Length > 0)
                                        {
                                            modFlag += "," + Path.GetRelativePath(rootPath, newerOpt.fullPath).TrimEnd('/').TrimEnd('\\');
                                        }
                                        else
                                        {
                                            modFlag += Path.GetRelativePath(rootPath, newerOpt.fullPath).TrimEnd('/').TrimEnd('\\');
                                        }
                                    }
                                }
                            }
                            else
                            {
                                //Mod has a optional dependency but it is not found installed
                                Log.Add(Log.LogSeverity.Information, "Knossos.PlayMod()", "Mod requested a optional dependency that is not found: " + modid + ". This is not critical.");
                            }
                        }catch(Exception ex)
                        {
                            Log.Add(Log.LogSeverity.Error, "Knossos.PlayMod(optional dependency)", ex);
                        }
                    }
                }
            }

            if (fsoBuild == null)
            {
                Log.Add(Log.LogSeverity.Error, "Knossos.PlayMod()", "Unable to find a valid FSO build for this mod!");
                if (MainWindow.instance != null)
                {
                    if (hasBuildDependency)
                    {
                        await MessageBox.Show(MainWindow.instance!, "Unable to find a valid FSO build for this mod!", "Error launching mod", MessageBox.MessageBoxButtons.OK);
                    }
                    else
                    {
                        await MessageBox.Show(MainWindow.instance!, "This mod does not specify an engine build to use, you should select one in the mod settings.", "Error launching mod", MessageBox.MessageBoxButtons.OK);
                    }
                }
                return;
            }
            else
            {
                Log.Add(Log.LogSeverity.Information, "Knossos.PlayMod()", "Used FSO build : " + fsoBuild.version);
                try
                {
                    if (SemanticVersion.Compare(fsoBuild.version, VPCompression.MinimumFSOVersion) < 0)
                    {
                        if (mod.modSettings.isCompressed)
                        {
                            if (MainWindow.instance != null)
                            {
                                var result = await MessageBox.Show(MainWindow.instance!, "This mod currently resolves to FSO build: " + fsoBuild.version + " and it is compressed, the minimum to fully support all compression features is: " + VPCompression.MinimumFSOVersion + ".\n23.0.0 may work if the mod do not have loose files, older versions are not going to work. Use a newer FSO version or uncompress this mod.", "FSO Version below minimum for compression", MessageBox.MessageBoxButtons.ContinueCancel);
                                if (result != MessageBox.MessageBoxResult.Continue)
                                    return;
                            }
                        }
                        else
                        {
                            var compressedMods = string.Empty;
                            if (mod.parent == "FS2")
                            {
                                var fs2 = Knossos.GetInstalledMod("FS2", "1.20.0");
                                if (fs2 != null && fs2.modSettings.isCompressed)
                                {
                                    compressedMods += fs2 + ", ";
                                }
                            }
                            var depMods = mod.GetModDependencyList();
                            if (depMods != null)
                            {
                                foreach (var depMod in depMods)
                                {
                                    var m = depMod.SelectMod();
                                    if (m != null && m.modSettings.isCompressed)
                                    {
                                        compressedMods += m + ", ";
                                    }
                                }
                            }
                            if (compressedMods != string.Empty)
                            {
                                if (MainWindow.instance != null)
                                {
                                    var result = await MessageBox.Show(MainWindow.instance!, "This mod currently resolves to FSO build: " + fsoBuild.version + " and depends on mods: " + compressedMods + " that are currently compressed, the minimum to fully support all compression features is: " + VPCompression.MinimumFSOVersion + ".\n23.0.0 may work if the mod do not have loose files, older versions are not going to work. Use a newer FSO version or uncompress those mods.", "FSO Version below minimum for compression", MessageBox.MessageBoxButtons.ContinueCancel);
                                    if (result != MessageBox.MessageBoxResult.Continue)
                                        return;
                                }
                            }
                        }
                    }
                }catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "Knossos.PlayMod()", ex);
                }
            }

            /* Build the cmdline, take in consideration systemcmd, globalcmd, modcmd(with user changes if any) */
            var modCmd = mod.GetModCmdLine()?.Split('-').ToList();
            var systemCmd = Knossos.globalSettings.GetSystemCMD(fsoBuild)?.Split('-');
            var globalCmd = Knossos.globalSettings.globalCmdLine?.Split('-');

            if(!standaloneServer)
            {
                if (!mod.modSettings.ignoreGlobalCmd)
                {
                    if (modCmd != null && modCmd.Any() && !globalSettings.noSystemCMD)
                    {
                        foreach(var flag in modCmd.ToList())
                        {
                            if(GlobalSettings.SystemFlags.Contains(flag.ToLower().Trim().Split(' ')[0]))
                                modCmd.Remove(flag);
                        }
                    }
                    if (!globalSettings.noSystemCMD)
                        cmdline = KnUtils.CmdLineBuilder(cmdline, systemCmd);
                    cmdline = KnUtils.CmdLineBuilder(cmdline, globalCmd);
                }
                cmdline = KnUtils.CmdLineBuilder(cmdline, modCmd?.ToArray());
                if (inSingleTCMode && CustomLauncher.CustomCmdlineArray != null && CustomLauncher.CustomCmdlineArray.Any())
                {
                    cmdline = KnUtils.CmdLineBuilder(cmdline, CustomLauncher.CustomCmdlineArray);
                }
            }
            else
            {
                cmdline += " -standalone";
                if (standalonePort > 0)
                {
                    cmdline += " -port " + standalonePort;
                }
            }

            if (modFlag.Length > 0)
            {
                cmdline += " -mod " + modFlag;
            }

            

            if (MainWindow.instance != null && globalSettings.warnNewSettingsSystem && !CustomLauncher.IsCustomMode)
            {
                try
                {
                    var fsoVersion = new SemanticVersion(fsoBuild.version);
                    var newSettingsVersion = new SemanticVersion("24.2.0-RC");
                    if(fsoVersion >= newSettingsVersion)
                    {
                        await Dispatcher.UIThread.InvokeAsync(async () => {
                            try
                            {
                                var res = await MessageBox.Show(MainWindow.instance, "Starting from FSO version 24.2.0 a new in-game settings system was implemented.\n" +
                                    "Launcher settings, such as video, audio or input, and Command Line arguments of those categories no longer have an effect by default.\n" +
                                    "Settings for most mods using FSO version 24.2.0 or higher must be now changed in-game by going to the 'Options' menu, where you will see additional methods for accessing and setting these new in-game options.", "New FSO settings system", MessageBox.MessageBoxButtons.DontWarnAgainOK);

                                if (res == MessageBox.MessageBoxResult.DontWarnAgain)
                                {
                                    globalSettings.warnNewSettingsSystem = false;
                                    globalSettings.Save();
                                }
                            }
                            catch
                            {
                                //fail silently, this is not important
                            }
                        });
                    }
                }
                catch 
                {
                    //fail silently, this is not important
                }
            }

            //Portable mode and limitations in unsupported fso versions
            if (inPortableMode && globalSettings.portableFsoPreferences)
            {
                try
                {
                    var fsoVersion = new SemanticVersion(fsoBuild.version);
                    var newPortableModeVersion = new SemanticVersion("24.3.0-20241211");
                    if (fsoVersion < newPortableModeVersion)
                    {
                        cmdline = "-portable_mode " + cmdline;
                        //older portable mode uses working path to pickup the .ini and store pilots
                        globalSettings.WriteFS2IniValues(Path.Combine(rootPath, "fs2_open.ini"));
                    }
                }
                catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "Knossos.PlayMod()", ex);
                }
            }

            Log.Add(Log.LogSeverity.Information, "Knossos.PlayMod()", "Used cmdLine : " + cmdline);

            //Launch FSO!!!
            var fsoResult = await fsoBuild.RunFSO(fsoExecType, cmdline, rootPath, false);

            if (!fsoResult.IsSuccess)
            {
                Log.Add(Log.LogSeverity.Error, "Knossos.PlayMod()", fsoResult.ErrorMessage);
                if (MainWindow.instance != null)
                {
                    await MessageBox.Show(MainWindow.instance, fsoResult.ErrorMessage, "Error launching mod", MessageBox.MessageBoxButtons.OK);
                }
            }
        }

        /// <summary>
        /// Gets installed Mods or TC
        /// This does not includes FSO builds
        /// Optional ID to return all versions of a specific mod id
        /// </summary>
        /// <param name="id"></param>
        /// <returns>List of Mods or empty list</returns>
        public static List<Mod> GetInstalledModList(string? id)
        {
            if (id != null)
            {
                return installedMods.Where(m => m.id == id).ToList();
            }
            else
            {
                return installedMods;
            }
        }

        /// <summary>
        /// Gets a installed mod data
        /// </summary>
        /// <param name="id"></param>
        /// <param name="version"></param>
        /// <returns>Mod class or null if not found</returns>
        public static Mod? GetInstalledMod(string id, string version)
        {
            return installedMods.FirstOrDefault(m => m.id == id && m.version == version);
        }

        /// <summary>
        /// Gets a list of requested installed FsoBuild
        /// Optional id and FsoStability to narrow down the results
        /// </summary>
        /// <param name="id"></param>
        /// <param name="stability"></param>
        /// <returns>List of FsoBuild or a empty list</returns>
        public static List<FsoBuild> GetInstalledBuildsList(string? id=null, FsoStability? stability = null)
        {
            if (id != null)
            {
                if(stability != null)
                {
                    return engineBuilds.Where(build => build.id == id && build.stability == stability).ToList();
                }
                else
                {
                    return engineBuilds.Where(build => build.id == id).ToList();
                }
            }
            else
            {
                if(stability != null)
                {
                    return engineBuilds.Where(build => build.stability == stability).ToList();
                }
                else
                {
                    return engineBuilds;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param>
        public static FsoBuild? GetInstalledBuild (string id, string version)
        {
            return engineBuilds.FirstOrDefault(build => build.id == id && build.version == version);
        }

        /// <summary>
        /// Search and load mods, builds and tools on the library path
        /// </summary>
        /// <param name="path"></param>
        /// <param name="isQuickLaunch"></param>
        /// <param name="folderLevel"></param>
        private static async Task FolderSearchRecursive(string path, bool isQuickLaunch, int folderLevel=0)
        {
            try
            {
                DirectoryInfo di = new DirectoryInfo(@path);
                DirectoryInfo[] arrDir = di.GetDirectories();

                if (folderLevel <= 1)
                {
                    foreach (DirectoryInfo dir in arrDir)
                    {
                        await FolderSearchRecursive(dir.ToString() + Path.DirectorySeparatorChar, isQuickLaunch, folderLevel + 1);
                    }
                }

                if(File.Exists(path + Path.DirectorySeparatorChar + "knossos_net_download.token"))
                {
                    /* This is a incomplete download, delete the folder */
                    Log.Add(Log.LogSeverity.Warning, "Knossos.FolderSearchRecursive()", "Deleting incomplete download found at "+path);
                    Directory.Delete(path, true);
                }
                else if (File.Exists(path + Path.DirectorySeparatorChar + "tool.json"))
                {
                    Knossos.AddTool(new Tool(path));
                }
                else if (File.Exists(path + Path.DirectorySeparatorChar + "mod.json"))
                {
                    try
                    {
                        var modJson = new Mod(path,di.Name);
                        switch (modJson.type)
                        {
                            case ModType.tc:
                            case ModType.mod: 
                                installedMods.Add(modJson);
                                if (modJson.id == "FS2" && modJson.type == ModType.tc && modJson.parent == "FS2")
                                {
                                    var fs2RootFiles = Directory.GetFiles(modJson.fullPath);
                                    if (fs2RootFiles != null && fs2RootFiles.FirstOrDefault(f=>f.ToLower().Contains("root_fs2.vp")) != null)
                                    {
                                        retailFs2RootFound = true;
                                        Log.Add(Log.LogSeverity.Information, "Knossos.FolderSearchRecursive", "Found FS2 Root Pack!");
                                    }
                                }
                                if(!isQuickLaunch)
                                    await Dispatcher.UIThread.InvokeAsync(() => MainWindowViewModel.Instance?.AddInstalledMod(modJson), DispatcherPriority.Background);
                                break;

                            case ModType.engine:
                                var build = new FsoBuild(modJson);
                                engineBuilds.Add(build);
                                if(!isQuickLaunch)
                                    await Dispatcher.UIThread.InvokeAsync(() => FsoBuildsViewModel.Instance?.AddBuildToUi(build), DispatcherPriority.Background);
                                break;
                        }
                        if(modJson.devMode && !isQuickLaunch)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() => MainWindowViewModel.Instance?.AddDevMod(modJson), DispatcherPriority.Background);
                        }
                    }
                    catch (Exception ex)
                    {
                        /* Likely if json parsing fails */
                        Log.Add(Log.LogSeverity.Error, "Knossos.ModSearchRecursive", ex);
                    }
                }
                else if(File.Exists(path + Path.DirectorySeparatorChar + "mod.ini"))
                {
                    var modLegacy = new Mod(path, di.Name, ModType.modlegacy);
                    installedMods.Add(modLegacy);
                    await Dispatcher.UIThread.InvokeAsync(() => MainWindowViewModel.Instance?.AddInstalledMod(modLegacy), DispatcherPriority.Background);
                }
            }catch (Exception ex)
            {
                /* Likely file/folder permission issues */
                Log.Add(Log.LogSeverity.Error, "Knossos.ModSearchRecursive", ex);

                if (folderLevel == 0 )
                {
                    if (MainWindow.instance != null)
                    {
                        Dispatcher.UIThread.Invoke(() => {
                            MessageBox.Show(MainWindow.instance!, "KnossosNET either cannot find or does not have permission to read a previously selected library folder.\nEither select a new library folder in KnossosNET's settings, or check if the folder is now protected.", "Knossos.NET Library Folder Error" , MessageBox.MessageBoxButtons.OK);
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Writes an string ONLY to the Debug -> Console window
        /// This is only valid for the current session
        /// </summary>
        /// <param name="message"></param>
        public static void WriteToUIConsole(string message)
        {
            MainWindowViewModel.Instance?.WriteToUIConsole(message);
        }

        /// <summary>
        /// Removes a from the installed builds list
        /// This does not removes the physical files or the UI element
        /// </summary>
        /// <param name="build"></param>
        public static void RemoveBuild(FsoBuild build)
        {
            engineBuilds.Remove(build);
        }

        /// <summary>
        /// Add a builds to the installed builds list
        /// This does not creates the UI element
        /// </summary>
        /// <param name="build"></param>
        public static void AddBuild(FsoBuild build)
        {
            engineBuilds.Add(build);
        }

        /// <summary>
        /// Add a mod to the installed mod list
        /// This does not creates the UI element
        /// </summary>
        /// <param name="mod"></param>
        public static void AddMod(Mod mod)
        {
            installedMods.Add(mod);
        }

        /// <summary>
        /// Remove all isntalled versions of a MOD ID
        /// Also removes the UI element and physical files
        /// It does not re-add the UI element to the Nebula tab
        /// </summary>
        /// <param name="modId"></param>
        public static void RemoveMod(string modId)
        {
            try
            {
                var delete = installedMods.Where(m => m.id == modId).ToList();
                if (delete.Any())
                {
                    MainWindowViewModel.Instance?.RemoveInstalledMod(modId);
                    foreach (var mod in delete)
                    {
                        Log.Add(Log.LogSeverity.Information, "Knossos.RemoveMod()", "Deleting mod: "+mod.title + " " +mod.version);
                        try
                        {
                            // this might only work on Windows
                            FileSystem.DeleteDirectory(mod.fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        }
                        catch (PlatformNotSupportedException)
                        {
                            // this should work on all platforms but doesn't use the Recycle Bin / Trash
                            Directory.Delete(mod.fullPath, true);
                        }
                        installedMods.Remove(mod);
                    }
                }
            }
            catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Knossos.RemoveMod()", ex);
            }
        }

        /// <summary>
        /// Removes one mod version from the installed mod list
        /// Deletes Physical files
        /// Does not deletes the UI element in any case
        /// </summary>
        /// <param name="mod"></param>
        public static void RemoveMod(Mod mod)
        {
            try
            {
                Log.Add(Log.LogSeverity.Information, "Knossos.RemoveMod()", "Deleting mod: " + mod.title + " " + mod.version);
                try
                {
                    // this might only work on Windows
                    FileSystem.DeleteDirectory(mod.fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
                catch (PlatformNotSupportedException)
                {
                    // this should work on all platforms but doesn't use the Recycle Bin / Trash
                    Directory.Delete(mod.fullPath, true);
                }
                installedMods.Remove(mod);
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Knossos.RemoveMod()", ex);
            }
        }

        /// <summary>
        /// Text-to-speech using the same system and voice that FSO is currently set to use
        /// Uses the Ksapi utility in Windows, what is likely to not work properly on ARM64 due to SAPI registry keys
        /// </summary>
        /// <param name="text"></param>
        /// <param name="voice_index"></param>
        /// <param name="voice_name"></param>
        /// <param name="volume"></param>
        /// <param name="callBack"></param>
        public async static void Tts(string text, int? voice_index = null, string? voice_name = null, int? volume = null, Func<bool>? callBack = null)
        {
            try
            {
                if (globalSettings.enableTts)
                {
                    if (KnUtils.IsWindows)
                    {
                        if (ttsObject != null)
                        {
                            var sp = (Process)ttsObject;
                            sp.Kill();
                            ttsObject = null;
                        }
                        if (text != string.Empty)
                        {
                            if (!File.Exists(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "KSapi.exe"))
                            {
                                if (KnUtils.CpuArch == "X86")
                                {
                                    using (var fileStream = File.Create(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "KSapi.exe"))
                                    {
                                        AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/utils/win/KSapi_x86.exe")).CopyTo(fileStream);
                                        fileStream.Close();
                                    }
                                }
                                else
                                {
                                    using (var fileStream = File.Create(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "KSapi.exe"))
                                    {
                                        AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/utils/win/KSapi.exe")).CopyTo(fileStream);
                                        fileStream.Close();
                                    }
                                }
                            }
                            await Task.Run(async () =>
                            {
                                //Max cmdline lenght is 8192, lets limit the text to 7500
                                if(text.Length > 7500)
                                {
                                    text = text.Substring(0, 7500);
                                }
                                await Task.Delay(300);
                                var voice = globalSettings.ttsVoice;
                                var vol = globalSettings.ttsVolume;
                                if (voice_index.HasValue)
                                    voice = voice_index.Value;
                                if (volume.HasValue)
                                    vol = volume.Value;
                                using var ttsProcess = new Process();
                                ttsProcess.StartInfo.FileName = KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "KSapi.exe";
                                ttsProcess.StartInfo.Arguments = "-text \"" + text + "\" -voice " + voice + " -vol " + vol;
                                ttsProcess.StartInfo.UseShellExecute = false;
                                ttsProcess.StartInfo.CreateNoWindow = true;
                                ttsObject = ttsProcess;
                                ttsProcess.Start();
                                ttsProcess.WaitForExit();
                                ttsObject = null;
                                if (callBack != null)
                                    await Dispatcher.UIThread.InvokeAsync(() => callBack(), DispatcherPriority.Background);
                            });
                        }
                    }
                    if(KnUtils.IsLinux)
                    {
                        //unimplemented
                        if (callBack != null)
                            await Dispatcher.UIThread.InvokeAsync(() => callBack(), DispatcherPriority.Background);
                    }
                    if(KnUtils.IsMacOS)
                    {
                        if (ttsObject != null)
                        {
                            var sp = (Process)ttsObject;
                            sp.Kill();
                            ttsObject = null;
                        }
                        if (text != string.Empty)
                        {
                            await Task.Run(async () =>
                            {
                                if (text.Length > 7500)
                                {
                                    text = text.Substring(0, 7500);
                                }
                                await Task.Delay(300);
                                string args = "";
                                string? voice = globalSettings.ttsVoiceName;
                                if (voice_name != null)
                                {
                                    voice = voice_name;
                                }
                                if (voice != null && voice != string.Empty)
                                {
                                    args = $"-v \"{voice}\"";
                                }
                                var vol = globalSettings.ttsVolume;
                                if (volume.HasValue)
                                    vol = volume.Value;
                                using var ttsProcess = new Process();
                                ttsProcess.StartInfo.FileName = "say";
                                ttsProcess.StartInfo.Arguments = $"{args} \"[[volm {vol / 100.0}]] {text}\"";
                                ttsProcess.StartInfo.UseShellExecute = false;
                                ttsProcess.StartInfo.CreateNoWindow = true;
                                ttsObject = ttsProcess;
                                ttsProcess.Start();
                                ttsProcess.WaitForExit();
                                ttsObject = null;
                                if (callBack != null)
                                    await Dispatcher.UIThread.InvokeAsync(() => callBack(), DispatcherPriority.Background);
                            });
                        }
                    }
                }
                else
                {
                    if (callBack != null)
                        await Dispatcher.UIThread.InvokeAsync(() => callBack(), DispatcherPriority.Background);
                }
            }catch(Exception ex)
            {
                Log.Add(Log.LogSeverity.Error,"Knossos.TTS()",ex);
                if (callBack != null)
                    callBack();
            }
        }

        /// <summary>
        /// Removes a tool from the installed tool list
        /// Does not removes physical files
        /// </summary>
        /// <param name="tool"></param>
        public static void RemoveTool(Tool tool)
        {
            modTools.Remove(tool);
        }

        /// <summary>
        /// Add a tool to the installed tool list
        /// </summary>
        /// <param name="tool"></param>
        public static void AddTool(Tool tool)
        {
            modTools.Add(tool);
        }

        /// <summary>
        /// Get all installed Tool list
        /// </summary>
        /// <returns>List of tools or empty list</returns>
        public static List<Tool> GetTools()
        {
            return modTools;
        }
    }
}
