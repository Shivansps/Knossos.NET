using Avalonia.Threading;
using Knossos.NET.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using VP.NET;

namespace Knossos.NET.ViewModels
{
    public partial class TaskItemViewModel : ViewModelBase
    {
        public async Task<bool> CompressMod(Mod mod, CancellationTokenSource? cancelSource = null, bool isSubTask = false)
        {
            try
            {
                if (!TaskIsSet)
                {
                    TaskIsSet = true;
                    if (!isSubTask)
                    {
                        CancelButtonVisible = true;
                        Name = "Compressing mod: " + mod.title + " " + mod.version;
                    }
                    else
                    {
                        Name = "Compressing mod";
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

                    Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.CompressMod()", "Starting to compress Mod: " + mod.title);

                    var vpFiles = Directory.GetFiles(mod.fullPath, "*.vp").ToList();
                    ProgressBarMax = vpFiles.Count() + 1;

                    //Loose Files Compression
                    if (Directory.Exists(Path.Combine(mod.fullPath, "data")) || mod.devMode)
                    {
                        var searchDir = mod.devMode ? mod.fullPath : Path.Combine(mod.fullPath, "data");
                        var allFilesInDataFolder = Directory.GetFiles(searchDir, "*.*", SearchOption.AllDirectories).ToList();
                        int skipped = 0;
                        //Filter
                        foreach (var fileInData in allFilesInDataFolder.ToList())
                        {
                            var file = new FileInfo(fileInData);

                            if (file.IsReadOnly || file.Length < VPCompression.MinimumSize || VPCompression.ExtensionIgnoreList.Contains(file.Extension.ToLower()) || file.Extension.ToLower() == ".lz41")
                            {
                                if (file.Extension.ToLower() == ".vp")
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

                            var result = await fileTask.CompressLosseFiles(allFilesInDataFolder, skipped, cancellationTokenSource);
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
                            await vpTask.CompressVP(new FileInfo(file), cancellationTokenSource);
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
                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.CompressMod()", ex);
                return false;
            }
        }
    }
}
