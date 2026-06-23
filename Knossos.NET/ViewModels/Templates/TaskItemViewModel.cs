using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Threading;
using Knossos.NET.Classes;
using VP.NET;
using System.Text;
using Avalonia.Media;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace Knossos.NET.ViewModels
{
    /*
        //New Task Method Example
        public async Task<bool> NewTaskExample(CancellationTokenSource? cancelSource = null)
        {
            try
            {
                if (!TaskIsSet)
                {
                    TaskIsSet = true; //a task can only do one thing, when a method is called this defines what this task will do, this ensures no other operation is done by mistake
                    ProgressBarMax = 1; // 0 = no progress bar, anything else to display progress bar, you can set this at any time
                    ProgressCurrent = 0; //if you need a progress bar you can set this at any time
                    ShowProgressText = false; //If the progress bar must display ProgressCurrent / ProgressBarMax on top of the bar
                    CancelButtonVisible = true; //you want the user to be able to manually cancel this task?
                    IsTextTask = false; // Set to True if you want simple "show my text" task. This will not display the "task completed" text.
                    IsFileDownloadTask = true; //This enables pause/resume buttons for file download on this task
                    Name = ""; //Display name

                    //We need a cancel token, if it is not provided one must be created
                    if (cancelSource != null)
                        cancellationTokenSource = cancelSource;
                    else
                        cancellationTokenSource = new CancellationTokenSource();

                    if (cancellationTokenSource.IsCancellationRequested) //this task was cancelled? you may want to check this multiple times
                        throw new TaskCanceledException();

                    //Wait in Queue. Important!!! Only for main tasks that need to wait in queue that are created in TaskViewModel.cs
                    //And only if it needs to actually wait in the queue, some you dont need to do this for, ex: show a text msg.
                    //Do not do this for internal/subtasks as it will loop here forever
                    Info = "In Queue";
                    while (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() != this)
                    {
                        await Task.Delay(1000);
                        if (cancellationTokenSource.IsCancellationRequested)
                        {
                            throw new TaskCanceledException();
                        }
                    }

                    Info = ""; //This display Text on the TaskView, if the task has a progress bar this text is display to the right of it, otherwise to the right of "name".
                    TextColor =  Brushes.White; //Info text color

                    //If to need your task to create subtasks
                    //Add this task to the task root (you can do it at any point, only do it once)
                    Dispatcher.UIThread.InvokeAsync(() => TaskRoot.Add(this)); //important! do it from the ui thread

                    //Create and add all subtask to the task list
                    var newTask = new TaskItemViewModel();
                    newTask.NewTaskExample(cancellationTokenSource); //pass the cancel token
                    Dispatcher.UIThread.InvokeAsync(() => TaskList.Add(newTask)); //important! do it from the ui thread

                    IsCompleted = true; //once we completed the task this must be set to true
                    CancelButtonVisible = false; //once we completed the task this must be set to true
                    ProgressCurrent = ProgressBarMax; //a recomended step at the end in case your task had a progress bar

                    //Dequeue. Important: Only if this is not a Subtask!!! If this task did not wait in a queue dont do this it will loop here forever
                    if (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() == this)
                    {
                        TaskViewModel.Instance!.taskQueue.Dequeue();
                    }

                    return true; //on task completion return true
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
                //If you need to do a task specific stuff on cancel resquest do it here
                //Dequeue. Important: Only if this is not a Subtask!!! If this task did not wait in a queue dont do this it will loop here forever
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
                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.NewTaskExample()", ex);
                //If you need to do a task specific stuff do it here
                //Dequeue. Important: Only if this is not a Subtask!!! If this task did not wait in a queue dont do this it will loop here forever
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
    */


    /*
        Core file for TaskItemViewModel, variables and methods used by all tasks will be here
        Individual Task code is splitted into individual files in the Tasks subfolder for clarity 
    */
    public partial class TaskItemViewModel : ViewModelBase
    {
        [ObservableProperty]
        internal bool taskIsSet = false;
        [ObservableProperty]
        internal bool cancelButtonVisible = false;
        [ObservableProperty]
        internal bool tooltipVisible = false;
        [ObservableProperty]
        internal string? tooltip = null;
        [ObservableProperty]
        internal string info = string.Empty;
        [ObservableProperty]
        internal int progressBarMin = 0;
        [ObservableProperty]
        internal int progressBarMax = 0;
        [ObservableProperty]
        internal int progressCurrent = 0;
        [ObservableProperty]
        internal string name = string.Empty;
        [ObservableProperty]
        internal bool isCompleted = false;
        [ObservableProperty]
        internal bool isCancelled = false;
        [ObservableProperty]
        internal bool isFileDownloadTask = false;
        [ObservableProperty]
        internal bool showProgressText = true;
        [ObservableProperty]
        internal string currentMirror = string.Empty;
        [ObservableProperty]
        internal IBrush textColor = Brushes.White;
        [ObservableProperty]
        internal bool isTextTask = false;
        [ObservableProperty]
        internal bool resumeButtonVisible = false;

        /* If this task contains subtasks, the subtasks must be added here, from the UIthread */
        [ObservableProperty]
        public ObservableCollection<TaskItemViewModel> taskList = new ObservableCollection<TaskItemViewModel>();
        /* If this task contains subtasks (this) object must be added to this single item list, from the UIthread */
        [ObservableProperty]
        public ObservableCollection<TaskItemViewModel> taskRoot = new ObservableCollection<TaskItemViewModel>();

        private CancellationTokenSource? cancellationTokenSource = null;
        public string? installID = null;
        public string? installVersion = null;
        private bool restartDownload = false;
        private bool pauseDownload = false;

        public TaskItemViewModel() 
        {
        }

        /* Progress Callbacks */
        private async void multiuploaderCallback(string text, int currentPart, int maxParts)
        {
            await Dispatcher.UIThread.InvokeAsync(() => {
                try
                {
                    ProgressBarMax = maxParts;
                    ProgressCurrent = currentPart;
                    Info = text;
                }
                catch { }
            });
        }

        private async void sevenZipCallback(int percentage)
        {
            await Dispatcher.UIThread.InvokeAsync(() => {
                try
                {
                    ProgressCurrent = percentage;
                }
                catch { }
            });
        }

        private async void copyCallback(string filename)
        {
            if (filename != string.Empty)
            {
                ProgressCurrent++;
            }
            else
            {
                ProgressCurrent = 0;
            }
            await Dispatcher.UIThread.InvokeAsync(() => {
                try
                {
                    Info = ProgressCurrent.ToString() + " / " + ProgressBarMax.ToString() + "  -  " + filename;
                }
                catch { }
            });
        }

        private async void compressionCallback(string filename, int maxFiles)
        {
            if (filename != string.Empty)
            {
                ProgressCurrent++;
            }
            else
            {
                ProgressCurrent = 0;
            }
            await Dispatcher.UIThread.InvokeAsync(() => {
                try
                {
                    ProgressBarMax = maxFiles;
                    Info = ProgressCurrent.ToString() + " / " + ProgressBarMax.ToString() + "  -  " + filename;
                }
                catch { }
            });
        }

        private async void extractCallback(string filename, int _, int maxFiles)
        {
            if (filename != string.Empty)
            {
                ProgressCurrent++;
            }
            else
            {
                ProgressCurrent = 0;
            }
            await Dispatcher.UIThread.InvokeAsync(() => {
                try
                {
                    ProgressBarMax = maxFiles;
                    Info = ProgressCurrent.ToString() + " / " + ProgressBarMax.ToString() + "  -  " + filename;
                }
                catch { }
            });
        }

        private void deCompressionCallback(int progress)
        {
            Dispatcher.UIThread.InvokeAsync(() => {
                ProgressCurrent = progress;
            });
        }

        /* Button Commands */
        public void CancelTaskCommand()
        {
            if (!IsCompleted)
            {
                cancellationTokenSource?.Cancel();
                IsCancelled = true;
            }
        }

        internal void PauseDownloadCommand()
        {
            pauseDownload = true;
            ResumeButtonVisible = true;
        }

        internal void ResumeDownloadCommand()
        {
            pauseDownload = false;
            ResumeButtonVisible = false;
        }

        internal void RestartDownloadCommand()
        {
            restartDownload = true;
            pauseDownload = false;
            ResumeButtonVisible = false;
        }
    }
}
