using Avalonia.Threading;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Knossos.NET.Classes;
using System.Diagnostics;

namespace Knossos.NET.ViewModels
{
    public partial class TaskItemViewModel : ViewModelBase
    {
        public async Task<bool> InstallTool(Tool tool, Tool? updateFrom, Action<bool> finishedCallback, CancellationTokenSource? cancelSource = null)
        {
            try
            {
                if (!TaskIsSet)
                {
                    TaskIsSet = true;
                    ProgressBarMax = 2;
                    ProgressCurrent = 0;
                    ShowProgressText = false;
                    CancelButtonVisible = true;
                    IsTextTask = false;
                    IsFileDownloadTask = true;
                    Name = "Install Tool: " + tool.name;
                    if (updateFrom != null)
                    {
                        Name = "Update Tool: " + tool.name;
                    }

                    if (cancelSource != null)
                        cancellationTokenSource = cancelSource;
                    else
                        cancellationTokenSource = new CancellationTokenSource();

                    if (cancellationTokenSource.IsCancellationRequested)
                        throw new TaskCanceledException();

                    Info = "In Queue";
                    while (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() != this)
                    {
                        await Task.Delay(1000);
                        if (cancellationTokenSource.IsCancellationRequested)
                        {
                            throw new TaskCanceledException();
                        }
                    }

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    var libPath = Knossos.GetKnossosLibraryPath();

                    if (string.IsNullOrEmpty(libPath))
                        throw new TaskCanceledException("KnossosNET library path is empty!");

                    var toolPath = Path.Combine(libPath, "tools", tool.name);
                    if (updateFrom != null)
                    {
                        toolPath += "_tool_update";
                    }

                    Directory.CreateDirectory(toolPath);

                    try
                    {
                        File.Create(Path.Combine(toolPath, "knossos_net_download.token")).Close();
                    }
                    catch { }

                    await Dispatcher.UIThread.InvokeAsync(() => TaskRoot.Add(this));

                    //Download
                    var fileTask = new TaskItemViewModel();
                    await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, fileTask));

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;

                    var url = tool.GetDownloadURL();

                    if (url == null)
                        throw new TaskCanceledException("Tool download URL was null.");

                    var fileName = Path.GetFileName(url);
                    var fileFullPath = Path.Combine(toolPath, fileName);
                    var result = await fileTask.DownloadFile(url, fileFullPath, "Downloading " + fileName, false, null, cancellationTokenSource);

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    ProgressCurrent++;

                    Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;

                    //Decompress
                    var decompressTask = new TaskItemViewModel();
                    await Dispatcher.UIThread.InvokeAsync(() => TaskList.Insert(0, decompressTask));
                    var decompResult = await decompressTask.DecompressNebulaFile(fileFullPath, fileName, toolPath, cancellationTokenSource);
                    if (!decompResult)
                    {
                        Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.InstallTool()", "Error while decompressing the file " + fileFullPath);
                        CancelTaskCommand();
                        throw new TaskCanceledException();
                    }

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    File.Delete(fileFullPath);

                    Knossos.AddTool(tool);

                    try
                    {
                        File.Delete(Path.Combine(toolPath, "knossos_net_download.token"));
                    }
                    catch { }

                    if (updateFrom != null)
                    {
                        updateFrom.Delete();
                        await Task.Delay(300);
                        var newPath = toolPath.Replace("_tool_update", "");
                        Directory.Move(toolPath, newPath);
                        tool.isFavorite = updateFrom.isFavorite;
                        tool.SaveJson(newPath);
                    }
                    else
                    {
                        tool.SaveJson(toolPath);
                    }

                    if (KnUtils.IsMacOS)
                    {
                        // Binaries on macOS must be signed as of BigSur (11.0) in order to run
                        // on Apple Silicon. So make sure that at least the main executable is
                        // signed ad-hoc after install.
                        //
                        // NOTE: This will *not* replace an existing signature.
                        // NOTE: This will *not* sign libraries or frameworks! The assumption
                        //       is that more complicated tools will already be signed.

                        var executablePath = tool.GetBestPackage()?.executablePath;

                        if (!string.IsNullOrEmpty(executablePath))
                        {
                            var execPath = Path.Combine(toolPath, executablePath);

                            try
                            {
                                using var process = new Process();
                                process.StartInfo.FileName = "codesign";
                                process.StartInfo.Arguments = $"-s - \"{execPath}\"";
                                process.StartInfo.CreateNoWindow = true;
                                process.Start();
                            }
                            catch (Exception ex)
                            {
                                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.InstallTool()", ex);
                            }
                        }
                    }

                    ProgressCurrent++;

                    Info = "Tasks: " + ProgressCurrent + "/" + ProgressBarMax;

                    IsCompleted = true;
                    CancelButtonVisible = false;
                    ProgressCurrent = ProgressBarMax;

                    if (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() == this)
                    {
                        TaskViewModel.Instance!.taskQueue.Dequeue();
                    }

                    finishedCallback.Invoke(true);
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
                finishedCallback.Invoke(true);
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
                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.InstallTool()", ex);
                while (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() != this)
                {
                    await Task.Delay(500);
                }
                if (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() == this)
                {
                    TaskViewModel.Instance!.taskQueue.Dequeue();
                }
                finishedCallback.Invoke(true);
                return false;
            }
        }
    }
}
