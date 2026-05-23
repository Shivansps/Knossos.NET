using Avalonia.Threading;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Threading;
using Knossos.NET.Classes;
using VP.NET;
using Avalonia.Media;
using System.Text.RegularExpressions;

namespace Knossos.NET.ViewModels
{
    public partial class TaskItemViewModel : ViewModelBase
    {
        public async Task<bool> InstallMod(Mod mod, CancellationTokenSource cancelSource, List<ModPackage>? reinstallPkgs = null, bool manualCompress = false, bool cleanupOldVersions = false, bool cleanInstall = false, bool allowHardlinks = true)
        {
            string? modPath = null;
            Mod? installed = null;

            try
            {
                if (!TaskIsSet)
                {
                    TaskIsSet = true;
                    installVersion = mod.version;
                    installID = mod.id;
                    if (cancelSource != null)
                    {
                        cancellationTokenSource = cancelSource;
                    }
                    else
                    {
                        cancellationTokenSource = new CancellationTokenSource();
                    }
                    CancelButtonVisible = true;
                    Name = "Downloading " + mod.ToString();
                    ShowProgressText = false;
                    await Dispatcher.UIThread.InvokeAsync(() => TaskRoot.Add(this));
                    Info = "In Queue";
                    bool compressMod = false;

                    //Set Mod card as "installing"
                    MainWindowViewModel.Instance?.SetInstalling(mod.id, cancellationTokenSource);

                    //Wait in Queue
                    while (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() != this)
                    {
                        await Task.Delay(1000);
                        if (cancellationTokenSource.IsCancellationRequested)
                        {
                            throw new TaskCanceledException();
                        }
                    }

                    if (!mod.devMode) //Do not compress dev mode mods
                    {
                        compressMod = manualCompress;
                        //Todo add mod fso version checking
                        if (!mod.devMode && Knossos.globalSettings.modCompression == CompressionSettings.Always)
                        {
                            compressMod = true;
                        }
                        if (!mod.devMode && Knossos.globalSettings.modCompression == CompressionSettings.ModSupport)
                        {
                            try
                            {
                                var fso = mod.GetDependency("FSO");
                                if (fso != null && (fso.version == null || SemanticVersion.Compare(SemanticVersion.GetLowerBound(fso.version) ?? "0.0.0", VPCompression.MinimumFSOVersion) > 0))
                                    compressMod = true;
                            }
                            catch (Exception ex)
                            {
                                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.InstallMod()", ex);
                            }
                        }
                    }

                    /*
                        Check if its installed, even on install task it could have been installed by another task that was in the queue
                    */
                    installed = Knossos.GetInstalledMod(mod.id, mod.version);
                    if (installed != null)
                    {
                        installed.ReLoadJson();
                        Name = "Modify " + mod.ToString();
                        compressMod = installed.modSettings.isCompressed;
                    }

                    Info = "Starting";

                    /*
                        -Parse all files we need to download
                        -Determine mod folder path and rootpack
                        -Add all "ModFile" to a new list for easy access
                        -Create all folders
                        -Create the download token on the root of the mod.
                        -Set all the data needed here, number of tasks, etc for the progress bar and info
                        -Main progress max value is calculated as follows: ( Number of files to download * 2 ) + 1
                         (Download, Decompression, Download banner/tile images)
                        -+1 task if we have to compress
                        -If the mod is installeds there is no need to download the baners and title image again so -2 to max tasks
                        -If devmode and file is a vp it needs to be decompressed +1 to max tasks
                    */

                    //Reject mod metadata with traversal sequences in path-component fields. Without this guard a
                    //malicious Nebula response (id="..\\..\\..", version="../etc"...) would poison modPath itself,
                    //causing the IsSubPath checks on file.dest / file.filename below to validate against the poisoned base.
                    if (!KnUtils.IsSafePathComponent(mod.id) ||
                        !KnUtils.IsSafePathComponent(mod.version) ||
                        (mod.parent != null && !KnUtils.IsSafePathComponent(mod.parent)))
                    {
                        Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.InstallMod()", "Refusing to install: mod has unsafe id/version/parent: id=" + mod.id + " version=" + mod.version + " parent=" + mod.parent);
                        CancelTaskCommand();
                        throw new TaskCanceledException();
                    }
                    foreach (var pkg in mod.packages)
                    {
                        if (pkg.folder != null && !KnUtils.IsSafePathComponent(pkg.folder))
                        {
                            Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.InstallMod()", "Refusing to install: mod " + mod.id + " has unsafe package folder: " + pkg.folder);
                            CancelTaskCommand();
                            throw new TaskCanceledException();
                        }
                    }

                    List<ModFile> files = new List<ModFile>();
                    string modFolder = mod.id + "-" + mod.version;
                    string rootPack = string.Empty;
                    if (mod.type == ModType.tc && mod.parent == null)
                    {
                        rootPack = mod.id;
                    }
                    else
                    {
                        if (mod.type == ModType.mod && mod.parent != null)
                        {
                            rootPack = mod.parent;
                        }
                        else
                        {
                            Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.InstallMod()", "Unable to determine mod root pack " + mod.ToString() + " Type: " + mod.type + " Parent: " + mod.parent);
                            throw new TaskCanceledException();
                        }
                    }

                    modPath = Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + rootPack + Path.DirectorySeparatorChar + modFolder;

                    /* Metadata update */
                    bool metaUpdate = false;
                    if (installed != null && Mod.IsMetaUpdate(mod, installed))
                    {
                        metaUpdate = true;
                        installed.lastUpdate = mod.lastUpdate;
                        installed.firstRelease = mod.firstRelease;
                        installed.releaseThread = mod.releaseThread;
                        installed.title = mod.title;
                        installed.description = mod.description;
                        installed.modFlag = mod.modFlag;
                        installed.videos = mod.videos;
                        foreach (var pkg in installed.packages)
                        {
                            var other = mod.packages.FirstOrDefault(p => p.name == pkg.name);
                            if (other != null)
                            {
                                pkg.dependencies = other.dependencies;
                            }
                        }
                        installed.SaveJson();
                        installed.tile = mod.tile;
                        installed.banner = mod.banner;
                        var msg = new TaskItemViewModel();
                        msg.ShowMsg("Metadata was updated", null);
                        await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, msg));
                    }

                    /* Delete pkgs */
                    if (installed != null)
                    {
                        bool save = false;
                        foreach (var modpkg in mod.packages.ToList())
                        {
                            var installedPkg = installed.packages.FirstOrDefault(p => p.name == modpkg.name);
                            if (modpkg.filelist != null && !modpkg.isSelected && installedPkg != null)
                            {
                                int delCount = 0;
                                var newTask = new TaskItemViewModel();
                                Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.InstallMod(delete mod file)", "Deleting package: " + modpkg.name + " MOD: " + mod);
                                Dispatcher.UIThread.Invoke(() =>
                                {
                                    newTask.ShowMsg("Deleting pkg: " + modpkg.name, null, Brushes.Red);
                                    TaskList.Add(newTask);
                                });
                                foreach (var file in modpkg.filelist)
                                {
                                    try
                                    {
                                        if (File.Exists(installed.fullPath + Path.DirectorySeparatorChar + file.filename))
                                        {
                                            File.Delete(installed.fullPath + Path.DirectorySeparatorChar + file.filename);
                                            delCount++;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.InstallMod(delete mod file)", ex);
                                    }
                                }
                                Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.InstallMod(delete mod file)", "Files deleted: " + delCount);
                                installed.packages.Remove(installedPkg);
                                mod.packages.Remove(modpkg);
                                save = true;
                            }
                        }
                        if (save)
                        {
                            installed.SaveJson();
                        }
                    }

                    int vPExtractionNeeded = 0;

                    for (int i = mod.packages.Count - 1; i >= 0; i--)
                    {
                        bool alreadyInstalled = false;
                        if (installed != null)
                        {
                            foreach (var pkg in installed.packages)
                            {
                                if (pkg.name == mod.packages[i].name)
                                {
                                    if (reinstallPkgs == null || !reinstallPkgs.Where(re => re.name == mod.packages[i].name).Any())
                                    {
                                        alreadyInstalled = true;
                                        continue;
                                    }
                                }
                            }
                        }

                        if (mod.packages[i].isSelected && !alreadyInstalled || !alreadyInstalled && mod.devMode)
                        {
                            if (mod.packages[i].files != null)
                            {
                                if (mod.devMode)
                                {
                                    foreach (var file in mod.packages[i].files!)
                                    {
                                        file.dest = mod.packages[i].folder + Path.DirectorySeparatorChar + file.dest;
                                        if (mod.packages[i].isVp)
                                            vPExtractionNeeded++;
                                    }
                                }
                                files.AddRange(mod.packages[i].files!);
                            }
                        }
                        else
                        {
                            mod.packages.RemoveAt(i);
                        }
                    }


                    /* Is there is nothing new to install just end the task */
                    if (files.Count == 0 && !metaUpdate)
                    {
                        Info = string.Empty;
                        IsCompleted = true;
                        CancelButtonVisible = false;
                        if (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() == this)
                        {
                            TaskViewModel.Instance!.taskQueue.Dequeue();
                        }
                        return true;
                    }

                    Directory.CreateDirectory(modPath);

                    foreach (var file in files)
                    {
                        if (file.dest != null && file.dest.Trim() != string.Empty)
                        {
                            if (!KnUtils.IsSubPath(modPath, file.dest))
                            {
                                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.InstallMod()", $"Unsafe dest path in mod '{mod.id}': {file.dest}");
                                CancelTaskCommand();
                                return false;
                            }
                            Directory.CreateDirectory(modPath + Path.DirectorySeparatorChar + file.dest);
                        }
                    }

                    ProgressBarMin = 0;
                    ProgressCurrent = 0;
                    ProgressBarMax = installed == null ? (files.Count * 2) + 1 : (files.Count * 2);
                    ProgressBarMax += vPExtractionNeeded;
                    if (compressMod)
                    {
                        ProgressBarMax += 1;
                    }
                    Info = "Tasks: 0/" + ProgressBarMax;

                    /* Do not create the token on mod updates */
                    if (installed == null)
                    {
                        try
                        {
                            File.Create(modPath + Path.DirectorySeparatorChar + "knossos_net_download.token").Close();
                        }
                        catch { }
                    }

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    //Do not load old versions if it is a clean install
                    var oldVersions = cleanInstall ? new List<Mod>() : Knossos.GetInstalledModList(mod.id);
                    //Reload checksum data, because by default it is unloaded after parsing
                    foreach (var oldVer in oldVersions)
                    {
                        oldVer.ReLoadJson();
                    }
                    /*
                        -Use parallel to process this new list, the max parallelism is the max number of concurrent downloads
                        -Always check canceltask before executing something
                        -Download File -> Verify Checksum -> Extract file
                        -Increase main progress when: 
                         File starts to download, File finishes downloading, Decompression starts, Decompression ends, Image download completed
                    */
                    mod.fullPath = modPath + Path.DirectorySeparatorChar;
                    await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = Knossos.globalSettings.maxConcurrentSubtasks, CancellationToken = cancellationTokenSource.Token }, async (file, token) =>
                    {
                        if (cancellationTokenSource.IsCancellationRequested)
                        {
                            throw new TaskCanceledException();
                        }

                        Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;

                        bool copiedFromOldVersion = false;

                        if (oldVersions.Any())
                        {
                            //Search for files in old versions
                            var copyTask = new TaskItemViewModel();
                            await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, copyTask));
                            copiedFromOldVersion = await copyTask.TryToCopyFilesFromOldVersions(mod, oldVersions, file, mod.packages.FirstOrDefault(p => p.files != null && p.files.Contains(file)), compressMod, allowHardlinks, cancellationTokenSource);
                            if (!copiedFromOldVersion)
                            {
                                await Dispatcher.UIThread.InvokeAsync(() => TaskList.Remove(copyTask));
                            }
                            else
                            {
                                //yay!, we skipped a download, don't you see me jumping in joy? We saved two steps!
                                ProgressCurrent += 2;
                            }
                        }

                        if (!copiedFromOldVersion)
                        {
                            //The good old way: download the file from nebula and extract
                            //Download
                            var fileTask = new TaskItemViewModel();
                            await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, fileTask));
                            if (file.dest == null)
                            {
                                file.dest = string.Empty;
                            }
                            if (string.IsNullOrEmpty(file.filename) || !KnUtils.IsSubPath(modPath, file.filename))
                            {
                                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.InstallMod()", $"Unsafe filename in mod '{mod.id}': {file.filename}");
                                CancelTaskCommand();
                                throw new TaskCanceledException();
                            }
                            var fileFullPath = modPath + Path.DirectorySeparatorChar + file.filename;
                            var result = await fileTask.DownloadFile(file.urls!, fileFullPath, "Downloading " + file.filename, false, null, cancellationTokenSource);

                            if (cancellationTokenSource.IsCancellationRequested)
                            {
                                throw new TaskCanceledException();
                            }

                            if (result.HasValue && result.Value)
                            {
                                ++ProgressCurrent;
                                Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;
                            }
                            else
                            {
                                throw new Exception("Error while downloading file: " + fileFullPath);
                            }

                            //Checksum
                            if (file.checksum != null && file.checksum.Count() > 0)
                            {
                                if (file.checksum[0].ToLower() == "sha256")
                                {
                                    using (FileStream? filehash = new FileStream(fileFullPath, FileMode.Open, FileAccess.Read))
                                    {
                                        using (SHA256 checksum = SHA256.Create())
                                        {
                                            filehash.Position = 0;
                                            var hashValue = BitConverter.ToString(await checksum.ComputeHashAsync(filehash)).Replace("-", String.Empty);
                                            filehash.Close();
                                            if (hashValue.ToLower() != file.checksum[1].ToLower())
                                            {
                                                throw new Exception("The downloaded file hash was incorrect, expected: " + file.checksum[1] + " Calculated Hash: " + hashValue);
                                            }
                                        }
                                        fileTask.Info = " Checksum OK!";
                                        await Dispatcher.UIThread.InvokeAsync(() => TaskList.Remove(fileTask));
                                    }
                                }
                                else
                                {
                                    Log.Add(Log.LogSeverity.Warning, "TaskItemViewModel.InstallMod()", "Cryptographic methods besides sha256 are not supported, skipping checksum check :" + file.checksum[0]);
                                }
                            }

                            if (cancellationTokenSource.IsCancellationRequested)
                            {
                                throw new TaskCanceledException();
                            }

                            //Decompress
                            var decompressTask = new TaskItemViewModel();
                            await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, decompressTask));
                            var decompResult = await decompressTask.DecompressNebulaFile(fileFullPath, file.filename, modPath + Path.DirectorySeparatorChar + file.dest, cancellationTokenSource);
                            if (!decompResult)
                            {
                                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.InstallMod()", "Error while decompressing the file " + fileFullPath);
                                CancelTaskCommand();
                                throw new TaskCanceledException();
                            }
                            File.Delete(fileFullPath);
                            ++ProgressCurrent;
                        }

                        Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;

                        if (cancellationTokenSource.IsCancellationRequested)
                        {
                            throw new TaskCanceledException();
                        }
                    });
                    files.Clear();

                    //Unload the checksum data we loaded for old versions
                    foreach (var oldVer in oldVersions)
                    {
                        oldVer.ClearUnusedData();
                    }

                    /*
                        -Delete the download token.
                        -Download tile and banner images, update those file names on json, increase progress
                        -Add folder name and full path to the modJson before creating the fsobuild
                        -Set modJson installed to true before saving json
                        -Serialize json to folder
                        -Create the FsoBuild object and add it to the main list
                        -Return the same FsoObject so it can be updated on the FsoBuildView
                        -Compress Mod if we had to
                    */

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    //Download Tile image
                    if (!string.IsNullOrEmpty(mod.tile) && (installed == null || metaUpdate))
                    {
                        Directory.CreateDirectory(modPath + Path.DirectorySeparatorChar + "kn_images");
                        var uri = new Uri(mod.tile);
                        using (var fs = await KnUtils.GetRemoteResourceStream(mod.tile))
                        {
                            var tileTask = new TaskItemViewModel();
                            await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, tileTask));
                            tileTask.ShowMsg("Getting tile image", null);
                            if (fs != null)
                            {
                                using (var destImg = new FileStream(modPath + Path.DirectorySeparatorChar + "kn_images" + Path.DirectorySeparatorChar + Path.GetFileName(uri.LocalPath), FileMode.Create, FileAccess.Write))
                                {
                                    await fs.CopyToAsync(destImg);
                                    fs.Close();
                                    destImg.Close();
                                }
                            }
                        }
                        mod.tile = "kn_images" + Path.DirectorySeparatorChar + Path.GetFileName(uri.LocalPath);
                    }

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    //Download Banner
                    if (!string.IsNullOrEmpty(mod.banner) && (installed == null || metaUpdate))
                    {
                        Directory.CreateDirectory(modPath + Path.DirectorySeparatorChar + "kn_images");
                        Directory.CreateDirectory(modPath + Path.DirectorySeparatorChar + "kn_images");
                        var uri = new Uri(mod.banner);
                        using (var fs = await KnUtils.GetRemoteResourceStream(mod.banner))
                        {
                            var bannerTask = new TaskItemViewModel();
                            await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, bannerTask));
                            bannerTask.ShowMsg("Getting banner image", null);
                            if (fs != null)
                            {
                                using (var destImg = new FileStream(modPath + Path.DirectorySeparatorChar + "kn_images" + Path.DirectorySeparatorChar + Path.GetFileName(uri.LocalPath), FileMode.Create, FileAccess.Write))
                                {
                                    await fs.CopyToAsync(destImg);
                                    fs.Close();
                                    destImg.Close();
                                }
                            }
                        }
                        mod.banner = "kn_images" + Path.DirectorySeparatorChar + Path.GetFileName(uri.LocalPath);
                    }

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    //Download Screenshots
                    if (mod.screenshots != null && mod.screenshots.Any() && installed == null)
                    {
                        Directory.CreateDirectory(modPath + Path.DirectorySeparatorChar + "kn_images");
                        var scList = new List<string>();
                        foreach (var sc in mod.screenshots)
                        {
                            if (cancellationTokenSource.IsCancellationRequested)
                            {
                                throw new TaskCanceledException();
                            }
                            var uri = new Uri(sc);
                            using (var fs = await KnUtils.GetRemoteResourceStream(sc))
                            {
                                var scTask = new TaskItemViewModel();
                                await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, scTask));
                                scTask.ShowMsg("Getting screenshot #" + scList.Count() + " image", null);
                                if (fs != null)
                                {
                                    using (var destImg = new FileStream(modPath + Path.DirectorySeparatorChar + "kn_images" + Path.DirectorySeparatorChar + Path.GetFileName(uri.LocalPath), FileMode.Create, FileAccess.Write))
                                    {
                                        await fs.CopyToAsync(destImg);
                                        fs.Close();
                                        destImg.Close();
                                    }
                                }
                            }
                            scList.Add("kn_images" + Path.DirectorySeparatorChar + Path.GetFileName(uri.LocalPath));
                        }
                        mod.screenshots = scList.ToArray();
                    }

                    ++ProgressCurrent;
                    Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;
                    mod.fullPath = modPath + Path.DirectorySeparatorChar;
                    mod.folderName = modFolder;
                    mod.installed = true;
                    mod.inNebula = true;

                    if (installed == null)
                    {
                        //mark all pkgs as enabled
                        mod.packages?.ForEach(pkg => pkg.isEnabled = true);
                        mod.SaveJson();
                    }
                    else
                    {
                        installed.ReLoadJson();
                        if (reinstallPkgs == null)
                        {
                            installed.packages.AddRange(mod.packages);
                        }
                        //mark all pkgs as enabled
                        mod.packages?.ForEach(pkg => pkg.isEnabled = true);
                        installed.SaveJson();
                        mod.ClearUnusedData();
                    }

                    mod.modSettings.SetInitialFilePath(mod.fullPath);

                    //We have to compress?
                    if (compressMod)
                    {
                        if (cancellationTokenSource.IsCancellationRequested)
                        {
                            throw new TaskCanceledException();
                        }

                        var cpTask = new TaskItemViewModel();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            TaskList.Insert(0, cpTask);
                        });
                        await cpTask.CompressMod(mod, cancellationTokenSource, true);
                        ProgressCurrent++;
                        Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;
                    }

                    //ExtractVPs if needed
                    if (mod.devMode)
                    {
                        var vpFiles = Directory.GetFiles(modPath, "*.*", SearchOption.AllDirectories).Where(file => Regex.IsMatch(file.ToLower(), @"^.+\.(vp|vpc)$"));
                        await Parallel.ForEachAsync(vpFiles, new ParallelOptions { MaxDegreeOfParallelism = Knossos.globalSettings.maxConcurrentSubtasks }, async (vp, token) =>
                        {
                            var extractTask = new TaskItemViewModel();
                            await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, extractTask));
                            var extractResult = await extractTask.ExtractVP(new FileInfo(vp), cancellationTokenSource);
                            if (!extractResult)
                            {
                                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.InstallMod()", "Error while extracting vp file " + vp);
                            }
                            ++ProgressCurrent;
                            Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;
                        });
                    }

                    try
                    {
                        File.Delete(mod.fullPath + Path.DirectorySeparatorChar + "knossos_net_download.token");
                    }
                    catch { }

                    //Remove Mod card, unmark update available, re-run dependencies checks
                    if (installed == null)
                    {
                        MainWindowViewModel.Instance?.NebulaModsView?.RemoveMod(mod.id);
                        FreespaceViewModel.Instance?.SetInstalling(mod.id, false);
                        Knossos.AddMod(mod);
                        await Dispatcher.UIThread.InvokeAsync(() => MainWindowViewModel.Instance?.AddInstalledMod(mod), DispatcherPriority.Background);
                        //We cant determine if the version we are installing is the newer one at this point, but this will determine if it is newer than anything was was installed previously, what is good enoght
                        var newer = Knossos.GetInstalledModList(mod.id)?.MaxBy(x => new SemanticVersion(x.version));
                        if (newer == mod)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() => MainWindowViewModel.Instance?.MarkAsUpdateAvailable(mod.id, false), DispatcherPriority.Background);
                        }
                        if (mod.devMode)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() => MainWindowViewModel.Instance!.AddDevMod(mod), DispatcherPriority.Background);
                            //Reload version editor if needed
                            DeveloperModsViewModel.Instance?.UpdateVersionManager(mod.id);
                        }
                        MainWindowViewModel.Instance?.RunModStatusChecks();
                    }

                    // Clean old versions
                    if (cleanupOldVersions)
                    {
                        try
                        {
                            var versions = Knossos.GetInstalledModList(mod.id);
                            if (versions != null)
                            {
                                versions.Remove(mod);
                                if (versions.Any())
                                {
                                    foreach (var version in versions.ToList())
                                    {
                                        //Check if it is inferior to the one we just installed
                                        if (SemanticVersion.Compare(mod.version, version.version) >= 1)
                                        {
                                            bool inUse = false;
                                            string inUseMods = "";
                                            foreach (var m in Knossos.GetInstalledModList(null))
                                            {
                                                if (m != null && m.id != mod.id)
                                                {
                                                    var deps = m.GetModDependencyList();
                                                    if (deps != null)
                                                    {
                                                        foreach (var dep in deps)
                                                        {
                                                            var depMod = dep.SelectMod();
                                                            if (depMod == version)
                                                            {
                                                                inUse = true;
                                                                inUseMods += m + ", ";
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            if (inUse)
                                            {
                                                Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.InstallMod()", "Cleanup: " + version + " is in use by these mods: " + inUseMods + ". Skipping.");
                                            }
                                            else
                                            {
                                                //Safe to delete
                                                Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.InstallMod()", "Cleanup: " + version + " is not in use, deleting...");
                                                var msgtask = new TaskItemViewModel();
                                                msgtask.ShowMsg("Cleanup: Deleting " + version.version, null);
                                                await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, msgtask));
                                                //Remove the mod version from Knossos and physical files
                                                await Task.Run(() => Knossos.RemoveMod(version));
                                                //Remove mod version from UI mod versions list
                                                await Dispatcher.UIThread.InvokeAsync(() => MainWindowViewModel.Instance?.RemoveInstalledModVersion(version));
                                                //If the dev editor is open and loaded this mod id, reset it
                                                await Dispatcher.UIThread.InvokeAsync(() => DeveloperModsViewModel.Instance?.ResetModEditor(mod.id));
                                            }
                                        }
                                        else
                                        {
                                            Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.InstallMod()", "Cleanup: " + version + " is newer than " + mod + ". Skipping.");
                                        }
                                    }
                                    MainWindowViewModel.Instance?.RunModStatusChecks();
                                }
                                else
                                {
                                    //Nothing to cleanup
                                    var msgtask = new TaskItemViewModel();
                                    msgtask.ShowMsg("Cleanup: Nothing to cleanup.", null);
                                    await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, msgtask));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            var msgtask = new TaskItemViewModel();
                            msgtask.ShowMsg("Cleanup: An error has ocurred, check logs.", null);
                            await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, msgtask));
                            Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.InstallMod()", ex);
                        }
                    }

                    /*
                        Always Dequeue, always check for check size and verify that the first is this TaskItemViewModel object
                    */
                    if (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() == this)
                    {
                        TaskViewModel.Instance!.taskQueue.Dequeue();
                    }

                    Info = string.Empty;
                    IsCompleted = true;
                    CancelButtonVisible = false;

                    return true;
                }
                else
                {
                    throw new Exception("The task is already set, it cant be changed or re-assigned.");
                }
            }
            catch (TaskCanceledException)
            {
                /*
                    Task cancel requested by user
                */
                IsCompleted = false;
                IsCancelled = true;
                CancelButtonVisible = false;
                Info = "Cancel Requested";
                while (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() != this)
                {
                    await Task.Delay(500);
                }
                if (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() == this)
                {
                    TaskViewModel.Instance!.taskQueue.Dequeue();
                }
                await Task.Delay(2000); //give time for child tasks to cancel first
                Info = "Task Cancelled";
                try
                {
                    /* If a error ocurred while updating do not delete the whole mod */
                    if (modPath != null && installed == null)
                    {
                        Directory.Delete(modPath, true);
                    }
                }
                catch { }
                await Dispatcher.UIThread.InvokeAsync(() => MainWindowViewModel.Instance?.CancelModInstall(mod.id), DispatcherPriority.Background);
                //Only dispose the token if it was created locally
                if (cancelSource == null)
                {
                    cancellationTokenSource?.Dispose();
                }
                return false;
            }
            catch (Exception ex)
            {
                /*
                    Task cancel forced due to a error
                */
                IsCompleted = false;
                IsCancelled = true;
                CancelButtonVisible = false;
                cancellationTokenSource?.Cancel();
                Info = "Cancel Requested";
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    //Messagebox is not thread safe!
                    await MessageBox.Show(MainWindow.instance!, "An error was ocurred during the download of the mod: " + mod.ToString() + ". Error: " + ex.Message, "Error", MessageBox.MessageBoxButtons.OK);
                });
                while (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() != this)
                {
                    await Task.Delay(500);
                }
                if (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() == this)
                {
                    TaskViewModel.Instance!.taskQueue.Dequeue();
                }

                await Task.Delay(2000); //give time for child tasks to cancel first
                Info = "Task Failed";
                try
                {
                    /* If a error ocurred while updating do not delete the whole mod */
                    if (modPath != null && installed == null)
                    {
                        Directory.Delete(modPath, true);
                    }
                }
                catch { }
                await Dispatcher.UIThread.InvokeAsync(() => MainWindowViewModel.Instance?.CancelModInstall(mod.id), DispatcherPriority.Background);
                //Only dispose the token if it was created locally
                if (cancelSource == null)
                {
                    cancellationTokenSource?.Dispose();
                }
                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.InstallMod()", ex);
                return false;
            }
        }
    }
}
