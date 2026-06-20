using Avalonia.Threading;
using Knossos.NET.Models;
using System;
using System.Collections.Concurrent;   // EFF
using System.Collections.Generic;       // EFF
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using VP.NET;

namespace Knossos.NET.ViewModels
{
    public partial class TaskItemViewModel : ViewModelBase
    {
        public async Task<bool> TranscodeMod(Mod mod, CancellationTokenSource? cancelSource = null, bool isSubTask = false)
        {
            try
            {
                if (!TaskIsSet)
                {
                    TaskIsSet = true;
                    if (!isSubTask)
                    {
                        CancelButtonVisible = true;
                        Name = "Transcoding mod: " + mod.title + " " + mod.version;
                    }
                    else
                    {
                        Name = "Transcoding mod";
                    }

                    ShowProgressText = false;
                    await Dispatcher.UIThread.InvokeAsync(() => {
                        TaskRoot.Add(this);
                    });
                    ProgressBarMin = 0;
                    ProgressCurrent = 0;
                    Info = "In Queue";

                    if (cancelSource != null)
                    {
                        cancellationTokenSource = cancelSource;
                    }
                    else
                    {
                        cancellationTokenSource = new CancellationTokenSource();
                    }

                    //Wait in Queue
                    if (!isSubTask)
                    {
                        while (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() != this)
                        {
                            await Task.Delay(1000);
                            if (cancellationTokenSource.IsCancellationRequested)
                            {
                                throw new TaskCanceledException();
                            }
                        }
                    }

                    Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.TranscodeMod()", "Starting to transcode Mod: " + mod.title);

                    //get all .vp / .vpc
                    var vpFiles = Directory.GetFiles(mod.fullPath, "*.vp").Concat(Directory.GetFiles(mod.fullPath, "*.vpc")).ToList();
                    ProgressBarMax = vpFiles.Count() + 2;

                    // list of transcoded dds files
                    var transcodedNames = new ConcurrentBag<string>();

                    //Loose Files Compression
                    if (Directory.Exists(mod.fullPath + Path.DirectorySeparatorChar + "data") || mod.devMode)
                    {
                        var searchDir = mod.devMode ? mod.fullPath : mod.fullPath + Path.DirectorySeparatorChar + "data";
                        var allFilesInDataFolder = Directory.GetFiles(searchDir, "*.*", SearchOption.AllDirectories).ToList();
                        int skipped = 0;
                        //Filter
                        foreach (var fileInData in allFilesInDataFolder.ToList())
                        {
                            var file = new FileInfo(fileInData);

                            if (file.IsReadOnly || file.Extension.ToLower() == ".ktx" || file.Extension.ToLower() == ".vp" || file.Extension.ToLower() == ".vpc")
                            {
                                if (file.Extension.ToLower() == ".vp" || file.Extension.ToLower() == ".vpc")
                                {
                                    vpFiles.Add(fileInData);
                                    ProgressBarMax++;
                                }
                                allFilesInDataFolder.Remove(fileInData);
                                skipped++;
                            }
                        }
                        //Process
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            var fileTask = new TaskItemViewModel();
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                TaskList.Insert(0, fileTask);
                            });

                            Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;

                            var result = await fileTask.TranscodeLosseFiles(allFilesInDataFolder, skipped, cancellationTokenSource, transcodedNames);
                            if (cancellationTokenSource.IsCancellationRequested)
                            {
                                throw new TaskCanceledException();
                            }
                        }, DispatcherPriority.Background);
                    }
                    ProgressCurrent++;
                    Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;

                    //VP Compression
                    await Parallel.ForEachAsync(vpFiles, new ParallelOptions { MaxDegreeOfParallelism = Knossos.globalSettings.compressionMaxParallelism }, async (file, token) =>
                    {
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            var vpTask = new TaskItemViewModel();
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                TaskList.Insert(0, vpTask);
                            });
                            Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;

                            await vpTask.TranscodeVP(new FileInfo(file), cancellationTokenSource, transcodedNames);
                            ProgressCurrent++;
                            Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;
                            if (cancellationTokenSource.IsCancellationRequested)
                            {
                                throw new TaskCanceledException();
                            }
                        }, DispatcherPriority.Background);
                    });

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    // Patch effs
                    var transcodedSet = new HashSet<string>(transcodedNames, StringComparer.OrdinalIgnoreCase);
                    HashSet<string> forceList;
                    {
                        var effTask = new TaskItemViewModel();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            TaskList.Insert(0, effTask);
                        });
                        forceList = await effTask.PatchEffFiles(mod, transcodedSet, cancellationTokenSource);
                    }
                    ProgressCurrent++;
                    Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    // EFF: 2nd pass (force)
                    // If PatchEffFiles report files that have not been transcoded (like when a .eff contain both
                    // uncompressed and compressed .dds files) make a 2nd pass to force transcode remaining files
                    if (forceList.Count > 0)
                    {
                        var forced = new ConcurrentBag<string>();
                        var dataDir = mod.devMode ? mod.fullPath : mod.fullPath + Path.DirectorySeparatorChar + "data";

                        if (Directory.Exists(dataDir))
                        {
                            var loose2 = Directory.GetFiles(dataDir, "*.dds", SearchOption.AllDirectories)
                                .Where(f => forceList.Contains(Path.GetFileName(f).ToLowerInvariant())).ToList();
                            if (loose2.Count > 0)
                            {
                                ProgressBarMax++;
                                await Dispatcher.UIThread.InvokeAsync(async () =>
                                {
                                    var t = new TaskItemViewModel();
                                    await Dispatcher.UIThread.InvokeAsync(() =>
                                    {
                                        TaskList.Insert(0, t);
                                    });
                                    await t.TranscodeLosseFiles(loose2, 0, cancellationTokenSource, forced, forceList);
                                    ProgressCurrent++;
                                    if (cancellationTokenSource.IsCancellationRequested)
                                    {
                                        throw new TaskCanceledException();
                                    }
                                }, DispatcherPriority.Background);
                            }
                        }
                        // VP
                        var archives2 = Directory.GetFiles(mod.fullPath, "*.vp").Concat(Directory.GetFiles(mod.fullPath, "*.vpc")).ToList();
                        if (Directory.Exists(dataDir) && !mod.devMode)
                        {
                            archives2.AddRange(Directory.GetFiles(dataDir, "*.vp", SearchOption.AllDirectories));
                            archives2.AddRange(Directory.GetFiles(dataDir, "*.vpc", SearchOption.AllDirectories));
                        }
                        archives2 = archives2.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        ProgressBarMax += archives2.Count;

                        await Parallel.ForEachAsync(archives2, new ParallelOptions { MaxDegreeOfParallelism = Knossos.globalSettings.compressionMaxParallelism }, async (file, token) =>
                        {
                            await Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                var t = new TaskItemViewModel();
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    TaskList.Insert(0, t);
                                });
                                await t.TranscodeVP(new FileInfo(file), cancellationTokenSource, forced, forceList);
                                ProgressCurrent++;
                                Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;
                                if (cancellationTokenSource.IsCancellationRequested)
                                {
                                    throw new TaskCanceledException();
                                }
                            }, DispatcherPriority.Background);
                        });

                        // Warn about frames that failed to convert
                        foreach (var still in forceList)
                        {
                            if (!forced.Contains(still))
                            {
                                Log.Add(Log.LogSeverity.Warning, "TaskItemViewModel.TranscodeMod()",
                                    $"Frame '{still}' is referenced by a patched .eff file to type=ktx but it failed to transcode.");
                            }
                        }
                    }

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    //Update settings json
                    mod.modSettings.Load(mod.fullPath);
                    mod.modSettings.isCompressed = true;
                    mod.modSettings.Save();

                    IsCompleted = true;
                    ProgressCurrent = ProgressBarMax;
                    Info = string.Empty;
                    CancelButtonVisible = false;

                    if (!isSubTask && TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() == this)
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
                Info = "Task Cancelled";
                IsCompleted = false;
                CancelButtonVisible = false;
                //Only dispose the token if it was created locally
                if (cancelSource == null)
                {
                    cancellationTokenSource?.Dispose();
                }
                if (!isSubTask)
                {
                    while (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() != this)
                    {
                        await Task.Delay(500);
                    }
                    if (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() == this)
                    {
                        TaskViewModel.Instance!.taskQueue.Dequeue();
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Info = "Task Failed";
                IsCompleted = false;
                CancelButtonVisible = false;
                cancellationTokenSource?.Cancel();
                if (cancelSource == null)
                {
                    cancellationTokenSource?.Dispose();
                }
                if (!isSubTask)
                {
                    while (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() != this)
                    {
                        await Task.Delay(500);
                    }
                    if (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() == this)
                    {
                        TaskViewModel.Instance!.taskQueue.Dequeue();
                    }
                }
                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.TranscodeMod()", ex);
                return false;
            }
        }
    }
}
