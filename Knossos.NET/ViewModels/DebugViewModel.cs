using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Diagnostics;
using System.IO;
using Knossos.NET.Classes;

namespace Knossos.NET.ViewModels
{
    public partial class DebugViewModel : ViewModelBase
    {
        [ObservableProperty]
        internal string uiConsoleOutput = string.Empty;

        /// <summary>
        /// Write a string to UI console on debug tab
        /// </summary>
        /// <param name="message"></param>
        public void WriteToUIConsole(string message)
        {
            UiConsoleOutput += "\n" + message;
        }

        /* Debug Section */
        internal void OpenLog()
        {
            if (File.Exists(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "Knossos.log"))
            {
                KnUtils.OpenFileInOS(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "Knossos.log");
            }
            else
            {
                MessageBox.Show(MainWindow.instance, "Log File " + KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "Knossos.log not found.", "File not found", MessageBox.MessageBoxButtons.OK);
            }
        }

        internal void OpenSettings()
        {
            if (File.Exists(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "settings.json"))
            {
                KnUtils.OpenFileInOS(KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "settings.json");
            }
            else
            {
                MessageBox.Show(MainWindow.instance, "Log File " + KnUtils.GetKnossosDataFolderPath() + Path.DirectorySeparatorChar + "settings.json not found.", "File not found", MessageBox.MessageBoxButtons.OK);
            }
        }

        internal void OpenFS2Log()
        {
            if (File.Exists(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "fs2_open.log"))
            {
                KnUtils.OpenFileInOS(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "fs2_open.log");
            }
            else
            {
                MessageBox.Show(MainWindow.instance, "Log File " + KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "fs2_open.log not found.", "File not found", MessageBox.MessageBoxButtons.OK);
            }
        }

        internal void OpenFS2Ini()
        {
            if (File.Exists(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "fs2_open.ini"))
            {
                KnUtils.OpenFileInOS(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "fs2_open.ini");
            }
            else
            {
                MessageBox.Show(MainWindow.instance, "Log File " + KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "fs2_open.ini not found.", "File not found", MessageBox.MessageBoxButtons.OK);
            }
        }

        internal async void UploadFS2Log()
        {
            if (File.Exists(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "fs2_open.log"))
            {
                try
                {
                    var logString = File.ReadAllText(KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "fs2_open.log", System.Text.Encoding.UTF8);
                    if (logString.Trim() != string.Empty)
                    {
                        var status = await Nebula.UploadLog(logString);
                        if (!status)
                        {
                            await MessageBox.Show(MainWindow.instance, "An error has ocurred while uploading the log file, check the log below.", "Upload log error", MessageBox.MessageBoxButtons.OK);
                        }
                    }
                    else
                    {
                        await MessageBox.Show(MainWindow.instance, "The log file is empty.", "Error", MessageBox.MessageBoxButtons.OK);
                    }
                }
                catch (Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "MainWindowViewModel.UploadFS2Log", ex);
                }
            }
            else
            {
                await MessageBox.Show(MainWindow.instance, "Log File " + KnUtils.GetFSODataFolderPath() + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "fs2_open.log not found.", "File not found", MessageBox.MessageBoxButtons.OK);
            }
        }
        internal async void UploadKnossosConsole()
        {
            try
            {
                var status = await Nebula.UploadLog(UiConsoleOutput);
                if (!status)
                {
                    await MessageBox.Show(MainWindow.instance, "An error has ocurred while uploading the console output, check the log below.", "Upload error", MessageBox.MessageBoxButtons.OK);
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "MainWindowViewModel.UploadKnossosConsole", ex);
            }
        }

        /// <summary>
        /// Open Debug Filter Dialog
        /// </summary>
        internal async void OpenDebugFilterView()
        {
            var dialog = new Views.DebugFiltersView();
            dialog.DataContext = new DebugFiltersViewModel();
            await dialog.ShowDialog<DebugFiltersView?>(MainWindow.instance!);
        }
    }
}
