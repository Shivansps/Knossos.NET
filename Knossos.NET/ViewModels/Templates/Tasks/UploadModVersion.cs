using Avalonia.Threading;
using Knossos.NET.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Knossos.NET.ViewModels
{
    public partial class TaskItemViewModel : ViewModelBase
    {
        public async Task<bool> UploadModVersion(Mod mod, bool isNewMod, bool metaOnly, CancellationTokenSource? cancelSource = null, int parallelCompression = 1, int parallelUploads = 1, List<DevModAdvancedUploadData>? advData = null)
        {
            try
            {
                if (!TaskIsSet)
                {
                    TaskIsSet = true;
                    ProgressBarMax = 5;
                    ProgressCurrent = 0;
                    ShowProgressText = false;
                    CancelButtonVisible = true;
                    IsTextTask = false;
                    IsFileDownloadTask = false;
                    Info = "In Queue";
                    Name = "Uploading " + mod.ToString();
                    await Dispatcher.UIThread.InvokeAsync(() => TaskRoot.Add(this));

                    if (cancelSource != null)
                        cancellationTokenSource = cancelSource;
                    else
                        cancellationTokenSource = new CancellationTokenSource();

                    //Wait in Queue
                    while (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() != this)
                    {
                        await Task.Delay(1000);
                        if (cancellationTokenSource.IsCancellationRequested)
                        {
                            throw new TaskCanceledException();
                        }
                    }
                    Info = "";
                    //If we are doing only meta skip to the end
                    if (!metaOnly)
                    {
                        ProgressBarMax += mod.packages.Count() * 2;

                        if (isNewMod)
                        {
                            Info = "Create Mod";
                            if (cancellationTokenSource.IsCancellationRequested)
                                throw new TaskCanceledException();
                            var create = new TaskItemViewModel();
                            await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, create));
                            await create.CreateModNebula(mod, cancellationTokenSource);
                            //If fails it should trigger cancel no need to check the return
                        }

                        ProgressCurrent++;

                        if (cancellationTokenSource.IsCancellationRequested)
                            throw new TaskCanceledException();

                        //At this point the mod id exist and we should have write access

                        /*
                            UPLOAD PROCESS:
                            1) Do pre_flight API call, im guessing that if the mod version is already uploaded Nebula will report that here somehow. YES: "duplicated version"
                            2) Upload mod tile image(check if already uploaded), get checksum and import it on modjson.
                            3) Upload banner image and screenshots(check if already uploaded), get checksum and import it on modjson.
                            4) If package = vp create a vp in mod\kn_upload\vps\{ packagename}.vp(No Compression)
                            5) 7z all packages folders and vp file and place them in kn_upload\{ packagename}.7z
                            6) Wipe and re - generate data in package.files and filelist. "files" is for the 7z file we are uploading to nebula. "filelist" is for all files inside the package folder(folder or vp)
                            7) Use multipartuploader to upload all packages(will auto-skip if already uploaded)
                            8) Api Call to "mod/release" with the mod meta(full json)
                        */

                        //Preflight check
                        Info = "PreFlight Check";
                        var newTask = new TaskItemViewModel();
                        await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, newTask));
                        var preFlightCheck = await newTask.PreFlightCheck(mod, cancellationTokenSource);

                        ProgressCurrent++;

                        if (cancellationTokenSource.IsCancellationRequested)
                            throw new TaskCanceledException();

                        //At this point preflight check was valid otherwise it would trigger a cancel, only check if it is a duplicated version, if it is skip to meta
                        if (preFlightCheck == "ok")
                        {
                            //We are good. Im leaving image upload for meta stage
                            Info = "Prepare Packages";
                            Directory.CreateDirectory(Path.Combine(mod.fullPath, "kn_upload"));
                            //Prepare packages, update data on mod
                            await Parallel.ForEachAsync(mod.packages, new ParallelOptions { MaxDegreeOfParallelism = parallelCompression }, async (pkg, token) =>
                            {
                                bool skipPkg = false;
                                //We should skip this?
                                if (advData != null)
                                {
                                    var advDataPkg = advData.FirstOrDefault(p => p.packageInNebula != null && p.packageInNebula!.name == pkg.name);
                                    if (advDataPkg != null && !advDataPkg.Upload)
                                    {
                                        var uploadedPkg = advDataPkg.packageInNebula;
                                        if (uploadedPkg != null)
                                        {
                                            pkg.notes = uploadedPkg.notes;
                                            pkg.isVp = uploadedPkg.isVp;
                                            pkg.status = uploadedPkg.status;
                                            pkg.filelist = uploadedPkg.filelist;
                                            pkg.files = uploadedPkg.files;
                                            pkg.dependencies = uploadedPkg.dependencies;
                                            pkg.environment = uploadedPkg.environment;
                                            pkg.executables = uploadedPkg.executables;
                                            pkg.folder = uploadedPkg.folder;
                                            pkg.checkNotes = uploadedPkg.checkNotes;
                                            pkg.files?.ForEach(f => f.urls = null); //Cant send urls to Nebula or it gets rejected
                                            skipPkg = true;
                                            Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.UploadModVersion()", "Skipping package preparation for :" + pkg.name + ". Data was loaded from Nebula.");
                                        }
                                    }
                                }
                                if (!skipPkg)
                                {
                                    if (mod.type != ModType.mod && mod.type != ModType.tc) //Just to be sure
                                        pkg.isVp = false;
                                    var newTask = new TaskItemViewModel();
                                    await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, newTask));
                                    await newTask.PrepareModPkg(pkg, mod.fullPath, cancellationTokenSource);
                                }
                                ProgressCurrent++;
                                if (cancellationTokenSource.IsCancellationRequested)
                                    throw new TaskCanceledException();
                            });

                            Info = "Upload Packages";
                            //Upload Packages
                            await Parallel.ForEachAsync(mod.packages, new ParallelOptions { MaxDegreeOfParallelism = parallelUploads }, async (pkg, token) =>
                            {
                                bool skipPkg = false;
                                //We should skip this?
                                if (advData != null)
                                {
                                    var advDataPkg = advData.FirstOrDefault(p => p.packageInNebula != null && p.packageInNebula!.name == pkg.name);
                                    if (advDataPkg != null && !advDataPkg.Upload)
                                    {
                                        var uploadedPkg = advDataPkg.packageInNebula;
                                        if (uploadedPkg != null)
                                        {
                                            skipPkg = true;
                                            Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.UploadModVersion()", "Skipping package upload for :" + pkg.name + ". Used the one in Nebula instead, file hash: " + advDataPkg.CustomHash);
                                        }
                                    }
                                }
                                if (!skipPkg)
                                {
                                    var newTask = new TaskItemViewModel();
                                    await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, newTask));
                                    await newTask.UploadModPkg(pkg, mod.fullPath, cancellationTokenSource);
                                }
                                ProgressCurrent++;
                                if (cancellationTokenSource.IsCancellationRequested)
                                    throw new TaskCanceledException();
                            });
                        }
                        else
                        {
                            if (preFlightCheck == "duplicated version")
                            {
                                ProgressBarMax -= mod.packages.Count() * 2;
                                metaOnly = true;
                            }
                        }
                    }
                    else
                    {
                        ProgressBarMax--;
                    }

                    if (cancellationTokenSource.IsCancellationRequested)
                        throw new TaskCanceledException();

                    //Meta Stage
                    Info = "Upload Images";
                    //I need to save the original data for tile, banner and screenshots in order not to override local paths
                    var origTile = mod.tile;
                    var origBanner = mod.banner;
                    var origScreenshots = mod.screenshots != null ? mod.screenshots.ToArray() : null;

                    var imgs = new TaskItemViewModel();
                    await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, imgs));
                    await imgs.UploadModImages(mod, cancellationTokenSource);

                    ProgressCurrent++;

                    if (cancellationTokenSource.IsCancellationRequested)
                        throw new TaskCanceledException();

                    //Meta
                    Info = "Upload Metadata";
                    var meta = new TaskItemViewModel();
                    await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, meta));
                    await meta.ReleaseMod(mod, metaOnly, cancellationTokenSource);

                    ProgressCurrent++;

                    if (cancellationTokenSource.IsCancellationRequested)
                        throw new TaskCanceledException();

                    //Restore paths & save
                    mod.tile = origTile;
                    mod.banner = origBanner;
                    mod.screenshots = origScreenshots;
                    //mark all pkgs as enabled
                    mod.packages?.ForEach(pkg => pkg.isEnabled = true);
                    mod.SaveJson();

                    ProgressCurrent++;

                    if (!metaOnly)
                        Info = "Upload Complete!";
                    else
                        Info = "Metadata Updated!";

                    //Completed
                    mod.inNebula = true;
                    IsCompleted = true;
                    CancelButtonVisible = false;
                    ProgressCurrent = ProgressBarMax;

                    //Delete kn_upload folder?
                    if (Knossos.globalSettings.deleteUploadedFiles && Directory.Exists(Path.Combine(mod.fullPath, "kn_upload")))
                    {
                        try
                        {
                            Directory.Delete(Path.Combine(mod.fullPath, "kn_upload"), true);
                        }
                        catch (Exception ex)
                        {
                            Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.UploadModVersion()", ex);
                        }
                    }

                    //Reload version editor if needed
                    if (!metaOnly)
                        DeveloperModsViewModel.Instance?.UpdateVersionManager(mod.id);

                    if (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() == this)
                    {
                        TaskViewModel.Instance!.taskQueue.Dequeue();
                    }

                    return true;
                }
                else
                {
                    throw new Exception("The task is already set, it cant be changed or re-assigned.");
                }
            }
            catch (TaskCanceledException)
            {
                //Task cancel requested by user
                IsCompleted = false;
                IsCancelled = true;
                CancelButtonVisible = false;
                Info = "Task Cancelled";
                //Only dispose the token if it was created locally
                if (cancelSource == null)
                {
                    cancellationTokenSource?.Dispose();
                }
                while (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() != this)
                {
                    await Task.Delay(500);
                }
                if (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() == this)
                {
                    TaskViewModel.Instance!.taskQueue.Dequeue();
                }
                return false;
            }
            catch (Exception ex)
            {
                //An exception has happened during task run
                IsCompleted = false;
                CancelButtonVisible = false;
                IsCancelled = true;
                Info = "Task Failed";
                //Only dispose the token if it was created locally
                if (cancelSource == null)
                {
                    cancellationTokenSource?.Dispose();
                }
                else
                {
                    //Call cancel task on the parent object
                    cancellationTokenSource?.Cancel();
                }
                while (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() != this)
                {
                    await Task.Delay(500);
                }
                if (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() == this)
                {
                    TaskViewModel.Instance!.taskQueue.Dequeue();
                }
                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.UploadModVersion()", ex);
                return false;
            }
        }
    }
}
