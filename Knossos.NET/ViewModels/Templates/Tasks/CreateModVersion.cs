using Avalonia.Threading;
using Knossos.NET.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace Knossos.NET.ViewModels
{
    public partial class TaskItemViewModel : ViewModelBase
    {
        public async Task<bool> CreateModVersion(Mod oldMod, string newVersion, CancellationTokenSource? cancelSource = null)
        {
            var newDir = string.Empty;
            try
            {
                if (!TaskIsSet)
                {
                    TaskIsSet = true;
                    ProgressBarMax = 1;
                    ProgressCurrent = 0;
                    ShowProgressText = false;
                    CancelButtonVisible = true;
                    Name = "Creating Mod Version: " + oldMod.title + " " + newVersion;
                    var currentDir = new DirectoryInfo(oldMod.fullPath);
                    var parentDir = currentDir.Parent;
                    newDir = parentDir!.FullName + Path.DirectorySeparatorChar + oldMod.id + "-" + newVersion;

                    if (cancelSource != null)
                    {
                        cancellationTokenSource = cancelSource;
                    }
                    else
                    {
                        cancellationTokenSource = new CancellationTokenSource();
                    }

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
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

                    ProgressBarMax = Directory.GetFiles(currentDir.FullName, "*", SearchOption.AllDirectories).Length;

                    Directory.CreateDirectory(newDir);

                    using (StreamWriter writer = new StreamWriter(newDir + Path.DirectorySeparatorChar + "knossos_net_download.token"))
                    {
                        writer.WriteLine("Warning: This token indicates an incomplete folder copy. If this token is present on the next KnossosNET startup this folder WILL BE DELETED.");
                    }

                    await KnUtils.CopyDirectoryAsync(
                        currentDir.FullName, 
                        newDir, 
                        true, 
                        cancellationTokenSource, 
                        copyCallback,
                        Knossos.globalSettings.skipExtensionsModFilecopy != null && Knossos.globalSettings.skipExtensionsModFilecopy.Any() ? Knossos.globalSettings.skipExtensionsModFilecopy.ToArray() : null);

                    File.Delete(newDir + Path.DirectorySeparatorChar + "knossos_net_download.token");

                    var newMod = new Mod(newDir, oldMod.id + "-" + newVersion);
                    newMod.version = newVersion;
                    newMod.SaveJson();

                    if (newMod.type == ModType.engine)
                    {
                        var build = new FsoBuild(newMod);
                        await Dispatcher.UIThread.InvokeAsync(() => Knossos.AddBuild(build));
                        await Dispatcher.UIThread.InvokeAsync(() => FsoBuildsViewModel.Instance!.AddBuildToUi(build));
                    }
                    else
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => Knossos.AddMod(newMod));
                        await Dispatcher.UIThread.InvokeAsync(() => MainViewModel.Instance!.AddInstalledMod(newMod));

                    }
                    await Dispatcher.UIThread.InvokeAsync(() => MainViewModel.Instance!.AddDevMod(newMod));

                    Info = "";
                    IsCompleted = true;
                    CancelButtonVisible = false;
                    ProgressCurrent = ProgressBarMax;

                    while (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() != this)
                    {
                        await Task.Delay(500);
                    }
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
                /*
                    Task cancel requested by user
                */
                IsCompleted = false;
                IsCancelled = true;
                CancelButtonVisible = false;
                Info = "Task Cancelled";
                //Only dispose the token if it was created locally
                if (cancelSource == null)
                {
                    cancellationTokenSource?.Dispose();
                }
                try
                {
                    Directory.Delete(newDir, true);
                }
                catch { }

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
                IsCompleted = false;
                CancelButtonVisible = false;
                IsCancelled = true;
                Info = "Task Failed";
                //Only dispose the token if it was created locally
                if (cancelSource == null)
                {
                    cancellationTokenSource?.Dispose();
                }
                Log.Add(Log.LogSeverity.Warning, "TaskItemViewModel.CreateModVersion()", ex);
                try
                {
                    Directory.Delete(newDir, true);
                }
                catch { }

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
        }
    }
}
