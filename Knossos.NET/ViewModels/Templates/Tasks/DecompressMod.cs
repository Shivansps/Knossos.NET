using Avalonia.Threading;
using Knossos.NET.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Knossos.NET.ViewModels
{
    public partial class TaskItemViewModel : ViewModelBase
    {
        public async Task<bool> DecompressMod(Mod mod, CancellationTokenSource? cancelSource = null)
        {
            try
            {
                if (!TaskIsSet)
                {
                    TaskIsSet = true;
                    CancelButtonVisible = true;
                    Name = "Decompressing mod: " + mod.title + " " + mod.version;
                    ShowProgressText = false;
                    await Dispatcher.UIThread.InvokeAsync(() => TaskRoot.Add(this));
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
                    while (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() != this)
                    {
                        await Task.Delay(1000);
                        if (cancellationTokenSource.IsCancellationRequested)
                        {
                            throw new TaskCanceledException();
                        }
                    }

                    Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.DecompressMod()", "Starting to decompress Mod: " + mod.title);

                    var vpcFiles = Directory.GetFiles(mod.fullPath, "*.vpc").ToList();
                    ProgressBarMax = vpcFiles.Count() + 1;

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

                            if (file.IsReadOnly || file.Extension.ToLower() != ".lz41")
                            {
                                if (file.Extension.ToLower() == ".vpc")
                                {
                                    vpcFiles.Add(fileInData);
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
                            await Dispatcher.UIThread.InvokeAsync(() => { TaskList.Insert(0, fileTask); });
                            Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;

                            var result = await fileTask.DecompressLosseFiles(allFilesInDataFolder, skipped, cancellationTokenSource);
                            if (!result || cancellationTokenSource.IsCancellationRequested)
                            {
                                throw new TaskCanceledException();
                            }
                        }, DispatcherPriority.Background);
                    }
                    ProgressCurrent++;
                    Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;

                    //VPC Decompression
                    await Parallel.ForEachAsync(vpcFiles, new ParallelOptions { MaxDegreeOfParallelism = Knossos.globalSettings.compressionMaxParallelism }, async (file, token) =>
                    {
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            var vpTask = new TaskItemViewModel();
                            await Dispatcher.UIThread.InvokeAsync(() => { TaskList.Insert(0, vpTask); });
                            Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;
                            await vpTask.DecompressVP(new FileInfo(file), cancellationTokenSource);
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
                    mod.modSettings.isCompressed = false;
                    mod.modSettings.Save();

                    IsCompleted = true;
                    ProgressCurrent = ProgressBarMax;
                    Info = string.Empty;
                    CancelButtonVisible = false;

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
                Info = "Task Cancelled";
                IsCompleted = false;
                CancelButtonVisible = false;
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
                Info = "Task Failed";
                IsCompleted = false;
                CancelButtonVisible = false;
                cancellationTokenSource?.Cancel();
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
                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.CompressMod()", ex);
                return false;
            }
        }
    }
}
