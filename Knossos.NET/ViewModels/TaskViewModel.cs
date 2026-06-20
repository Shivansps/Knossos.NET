using Avalonia.Threading;
using Knossos.NET.Classes;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Controls;
using System.Linq;

namespace Knossos.NET.ViewModels
{
    /// <summary>
    /// This class is intended to be the gateway used by the rest of the code to create and display tasks in the UI
    /// </summary>
    public partial class TaskViewModel : ViewModelBase
    {
        public static TaskViewModel? Instance { get; private set; }
        /// <summary>
        /// Tasks currently displayed in UI. Internal use only, must be modified from the UIThread
        /// </summary>
        internal ObservableCollection<TaskItemViewModel> TaskList { get; set; } = new ObservableCollection<TaskItemViewModel>();
        /// <summary>
        /// Task Execute Queue. Internal use only, must be modified from the UIThread
        /// </summary>
        internal Queue<TaskItemViewModel> taskQueue { get; set; } = new Queue<TaskItemViewModel>();

        [ObservableProperty]
        internal bool buttonsVisible = true;

        public TaskViewModel() 
        {
            Instance = this;
        }

        /// <summary>
        /// Calls CancelTaskCommand() on all tasks
        /// </summary>
        public void CancelAllRunningTasks()
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                foreach (var task in TaskList)
                {
                    task.CancelTaskCommand();
                }
            });
        }

        /// <summary>
        /// Cancel a ModInstall or Verify mod by mod ID and version
        /// Null version will cancel all tasks with the same id
        /// </summary>
        /// <param name="id"></param>
        /// <param name="version"></param>
        public void CancelAllInstallTaskWithID(string id, string? version)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                foreach (var task in TaskList.ToList())
                {
                    if (!task.IsCompleted && task.installID == id && (version == null || task.installVersion == version))
                    {
                        task.CancelTaskCommand();
                    }
                }
            });
        }

        /// <summary>
        /// SHow or hide buttons on taskview
        /// </summary>
        /// <param name="state"></param>
        public void ShowButtons(bool state)
        {
            ButtonsVisible = state;
        }

        /// <summary>
        /// Checks if all tasks in queue are mark as cancelled or completed
        /// </summary>
        /// <returns>true if all completed or cancelled, false if there is running tasks</returns>
        public bool IsSafeState()
        {
            foreach (var task in TaskList.ToList())
            {
                if (!task.IsCancelled && !task.IsCompleted)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Download a file from a url and display download progress
        /// </summary>
        /// <param name="url"></param>
        /// <param name="dest"></param>
        /// <param name="msg"></param>
        /// <param name="showStopButton"></param>
        /// <param name="tooltip"></param>
        /// <returns>true on successfull download, false otherwise</returns>
        public async Task<bool?> AddFileDownloadTask(string url, string dest, string msg, bool showStopButton, string? tooltip = null)
        {
            var newTask = new TaskItemViewModel();
            Dispatcher.UIThread.Invoke(() =>
            {
                TaskList.Add(newTask);
            });
            return await newTask.DownloadFile(url, dest, msg, showStopButton, tooltip).ConfigureAwait(false);
        }

        /// <summary>
        /// Extracts a file to a folder showing a progress bar
        /// This task does not wait in queue
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="destFolder"></param>
        /// <returns>true if the extraction was completed, false otherwise</returns>
        public async Task<bool> AddFileDecompressionTask(string filePath, string destFolder, bool extractFullPath = true)
        {
            var newTask = new TaskItemViewModel();
            Dispatcher.UIThread.Invoke(() =>
            {
                TaskList.Add(newTask);
            });

            if(!File.Exists(filePath))
            {
                Log.Add(Log.LogSeverity.Error, "TaskViewModel.AddFileDecompressionTask()", "File: " + filePath + " dosent exist!");
                return false;
            }

            if (!Directory.Exists(destFolder))
            {
                Log.Add(Log.LogSeverity.Error, "TaskViewModel.AddFileDecompressionTask()", "Dest folder: " + destFolder + " dosent exist!");
                return false;
            }

            return await newTask.DecompressNebulaFile(filePath, Path.GetFileName(filePath), destFolder, null, extractFullPath).ConfigureAwait(false);
        }

        /// <summary>
        /// Displays a simple message on task list
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="tooltip"></param>
        public void AddMessageTask(string msg, string? tooltip = null)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                var newTask = new TaskItemViewModel();
                TaskList.Add(newTask);
                newTask.ShowMsg(msg, tooltip);
            });
        }

        /// <summary>
        /// Displays the list of updated mod news
        /// </summary>
        /// <param name="updatedMods"></param>
        public void AddDisplayUpdatesTask(List<Mod> updatedMods)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                var newTask = new TaskItemViewModel();
                TaskList.Add(newTask);
                newTask.DisplayUpdates(updatedMods);
            });
        }

        /// <summary>
        /// Remove all tasks from view except for the ones currently running or waiting in the execute queue
        /// </summary>
        public void CleanCommand()
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                for (int i = TaskList.Count - 1; i >= 0; i--)
                {
                    if (TaskList[i].CancelButtonVisible == false)
                    {
                        TaskList.RemoveAt(i);
                    }
                }
            });
        }

        /// <summary>
        /// Gets string with currently running and pending taks to use as a tooltip
        /// </summary>
        public string GetRunningTaskString()
        {
            try
            {
                var active = "Running Task:\n";
                var finished = "Finished Tasks:";
                var first = true;
                Dispatcher.UIThread.Invoke(() =>
                {
                    try
                    {
                        foreach (var task in TaskList)
                        {
                            if (!task.IsCancelled && !task.IsCompleted)
                            {
                                active += task.Name + "\n";
                                if (first)
                                {
                                    active += "\n\nPending Tasks:\n";
                                    first = false;
                                }
                            }
                            else
                            {
                                finished += "\n" + task.Name;
                            }
                        }
                    } catch
                    {
                        //task failed successfully
                        //usually TaskCancelledException when closing Knet
                    }
                });
                return active + "\n" + finished;
            }
            catch 
            {
                return "";
            }
        }

        /// <summary>
        /// Return number of tasks in list
        /// </summary>
        /// <returns></returns>
        public int NumberOfTasks()
        {
            return TaskList.Count;
        }

        /// <summary>
        /// Downloads a new FSO build from Nebula
        /// </summary>
        /// <param name="build"></param>
        /// <param name="sender"></param>
        /// <param name="modJson"></param>
        /// <returns>FsoBuild class of the installed build or null if failed or cancelled</returns>
        public async Task<FsoBuild?> InstallBuild(FsoBuild build, FsoBuildItemViewModel sender, Mod? modJson=null, List<ModPackage>? modifyPkgs = null, bool cleanupOldVersions = false)
        {
            if(Knossos.GetKnossosLibraryPath() == null)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await MessageBox.Show(MainWindow.instance!, "KnossosNET Library Folder path is not set! Before installing mods go to settings and select a Library Folder.", "Error", MessageBox.MessageBoxButtons.OK);
                });
                return null;
            }
            var newTask = new TaskItemViewModel();
            Dispatcher.UIThread.Invoke(() =>
            {
                TaskList.Add(newTask);
                taskQueue.Enqueue(newTask);
            });
            var res = await newTask.InstallBuild(build, sender,sender.cancellationTokenSource,modJson, modifyPkgs, cleanupOldVersions).ConfigureAwait(false);
            if (res != null && Knossos.inSingleTCMode)
            {
                try
                {
                    TaskList.Remove(newTask);
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "TaskViewModel.InstallBuild()", ex);
                }
                Dispatcher.UIThread.Invoke(() => TaskViewModel.Instance?.AddMessageTask("Completed: " + newTask.Name), DispatcherPriority.Background);
            }
            return res;
        }

        /// <summary>
        /// Install a mod from Nebula, the reinstallPkgs list can be used to reinstall installed packages or install new ones
        /// if manualCompress = true a mod compress task will be started after this one finishes
        /// </summary>
        /// <param name="mod"></param>
        /// <param name="reinstallPkgs"></param>
        /// <param name="manualCompress"></param>
        public async void InstallMod(Mod mod, List<ModPackage>? reinstallPkgs = null, bool manualCompress = false, bool cleanupOldVersions = false, bool cleanInstall = false, bool allowHardlinks = true)
        {
            try
            {
                if (Knossos.GetKnossosLibraryPath() == null)
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await MessageBox.Show(MainWindow.instance!, "KnossosNET Library Folder path is not set! Before installing mods go to settings and select a Library Folder.", "Error", MessageBox.MessageBoxButtons.OK);
                    });
                    return;
                }

                if (mod.type == ModType.engine)
                {
                    //If this is an engine build then call the UI element to do the build install process instead
                    FsoBuildsViewModel.Instance?.RelayInstallBuild(mod);
                }
                else
                {
                    using (var cancelSource = new CancellationTokenSource())
                    {
                        var newTask = new TaskItemViewModel();
                        Dispatcher.UIThread.Invoke(() =>
                        {
                            TaskList.Add(newTask);
                            taskQueue.Enqueue(newTask);
                        });
                        var res = await newTask.InstallMod(mod, cancelSource, reinstallPkgs, manualCompress, cleanupOldVersions, cleanInstall, allowHardlinks).ConfigureAwait(false);
                        if(res && Knossos.inSingleTCMode)
                        {
                            try
                            {
                                TaskList.Remove(newTask);
                            }
                            catch (Exception ex)
                            {
                                Log.Add(Log.LogSeverity.Error, "TaskViewModel.InstallMod()", ex);
                            }
                            Dispatcher.UIThread.Invoke(() => TaskViewModel.Instance?.AddMessageTask("Completed: " + newTask.Name), DispatcherPriority.Background);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "TaskViewModel.InstallMod()", ex);
            }
        }

        /// <summary>
        /// Compress a mod for FSO using LZ41
        /// Reemplaces .vp for .vpc and compress losse files inside the data folder
        /// Do not do this for Devmode mods
        /// </summary>
        /// <param name="mod"></param>
        public async Task CompressMod(Mod mod)
        {
            if (mod.type == ModType.engine)
            {
                //If this is an engine build then do nothing
            }
            else
            {
                using (var cancelSource = new CancellationTokenSource())
                {
                    var newTask = new TaskItemViewModel();
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        TaskList.Add(newTask);
                        taskQueue.Enqueue(newTask);
                    });
                    var res = await newTask.CompressMod(mod, cancelSource).ConfigureAwait(false);
                    if (res && Knossos.inSingleTCMode)
                    {
                        try
                        {
                            TaskList.Remove(newTask);
                        }
                        catch (Exception ex)
                        {
                            Log.Add(Log.LogSeverity.Error, "TaskViewModel.CompressMod()", ex);
                        }
                        Dispatcher.UIThread.Invoke(() => TaskViewModel.Instance?.AddMessageTask("Completed: " + newTask.Name), DispatcherPriority.Background);
                    }
                }
            }
        }

        /// <summary>
        /// Transcode all DDS BCn files in a mod to KTX ETC2
        /// </summary>
        /// <param name="mod"></param>
        /// <returns></returns>
        public async Task TranscodeMod(Mod mod)
        {
            if (mod.type == ModType.engine)
            {
                //If this is an engine build then do nothing
            }
            else
            {
                using (var cancelSource = new CancellationTokenSource())
                {
                    var newTask = new TaskItemViewModel();
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        TaskList.Add(newTask);
                        taskQueue.Enqueue(newTask);
                    });
                    var res = await newTask.TranscodeMod(mod, cancelSource).ConfigureAwait(false);
                    if (res && Knossos.inSingleTCMode)
                    {
                        try
                        {
                            TaskList.Remove(newTask);
                        }
                        catch (Exception ex)
                        {
                            Log.Add(Log.LogSeverity.Error, "TaskViewModel.TranscodeMod()", ex);
                        }
                        Dispatcher.UIThread.Invoke(() => TaskViewModel.Instance?.AddMessageTask("Completed: " + newTask.Name), DispatcherPriority.Background);
                    }
                }
            }
        }

        /// <summary>
        /// Decompresses a mod from LZ41 into regular files
        /// Reemplaces .vpc for .vp and decompress .lz41 files inside the data folder
        /// </summary>
        /// <param name="mod"></param>
        public async Task DecompressMod(Mod mod)
        {
            if (mod.type == ModType.engine)
            {
                //If this is an engine build then do nothing
            }
            else
            {
                using (var cancelSource = new CancellationTokenSource())
                {
                    var newTask = new TaskItemViewModel();
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        TaskList.Add(newTask);
                        taskQueue.Enqueue(newTask);
                    });
                    var res = await newTask.DecompressMod(mod, cancelSource).ConfigureAwait(false);
                    if (res && Knossos.inSingleTCMode)
                    {
                        try
                        {
                            TaskList.Remove(newTask);
                        }
                        catch (Exception ex)
                        {
                            Log.Add(Log.LogSeverity.Error, "TaskViewModel.DecompressMod()", ex);
                        }
                        Dispatcher.UIThread.Invoke(() => TaskViewModel.Instance?.AddMessageTask("Completed: " + newTask.Name), DispatcherPriority.Background);
                    }
                }
            }
        }

        /// <summary>
        /// Verefies a MOD files using their checksum info
        /// Also checks and informs if there is extra files that should not be there.
        /// Extra files are not considered as a fail
        /// </summary>
        /// <param name="mod"></param>
        public async void VerifyMod(Mod mod)
        {
            try
            {
                using (var cancelSource = new CancellationTokenSource())
                {
                    var newTask = new TaskItemViewModel();
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        TaskList.Add(newTask);
                        taskQueue.Enqueue(newTask);
                    });
                    await newTask.VerifyMod(mod, cancelSource).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "TaskViewModel.VerifyMod()", ex);
            }
        }

        /// <summary>
        /// For Devmode
        /// Creates a copy of the mod, on the same folder were it is located, using the new passed version string
        /// </summary>
        /// <param name="oldMod"></param>
        /// <param name="newVersion"></param>
        /// <param name="hackCallback"></param>
        public async void CreateModVersion(Mod oldMod, string newVersion, Action hackCallback)
        {
            try
            {
                using (var cancelSource = new CancellationTokenSource())
                {
                    var newTask = new TaskItemViewModel();
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        TaskList.Add(newTask);
                        taskQueue.Enqueue(newTask);
                    });
                    await newTask.CreateModVersion(oldMod, newVersion, cancelSource).ConfigureAwait(false);
                    await Task.Delay(1000).ConfigureAwait(false);
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        hackCallback?.Invoke();
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "TaskViewModel.CreateModVersion()", ex);
            }
        }

        /// <summary>
        /// For devmode
        /// Upload a mod to nebula
        /// </summary>
        /// <param name="mod"></param>
        /// <param name="isNewMod"></param>
        /// <param name="metadataonly"></param>
        /// <param name="advData"></param>
        /// <param name="parallelCompression"></param>
        /// <param name="parallelUploads"></param>
        public async void UploadModVersion(Mod mod, bool isNewMod, bool metadataonly = false, int parallelCompression = 1, int parallelUploads = 1, List<DevModAdvancedUploadData>? advData = null )
        {
            try
            {
                using (var cancelSource = new CancellationTokenSource())
                {
                    var newTask = new TaskItemViewModel();
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        TaskList.Add(newTask);
                        taskQueue.Enqueue(newTask);
                    });
                    await newTask.UploadModVersion(mod, isNewMod, metadataonly, cancelSource, parallelCompression, parallelUploads, advData).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "TaskViewModel.UploadModVersion()", ex);
            }
        }

        /// <summary>
        /// Downloads and extract a new tool from the tool repo, also handles updates
        /// </summary>
        /// <param name="tool"></param>
        /// <param name="updateFrom"></param>
        /// <param name="finishedCallback"></param>
        public async Task<bool> DownloadTool(Tool tool, Tool? updateFrom, Action<bool> finishedCallback)
        {
            using (var cancelSource = new CancellationTokenSource())
            {
                var newTask = new TaskItemViewModel();
                Dispatcher.UIThread.Invoke(() =>
                {
                    TaskList.Add(newTask);
                    taskQueue.Enqueue(newTask);
                });
                return await newTask.InstallTool(tool, updateFrom, finishedCallback, cancelSource).ConfigureAwait(false);
            }
        }
    }
}
