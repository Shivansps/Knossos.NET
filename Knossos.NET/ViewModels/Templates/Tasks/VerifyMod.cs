using Avalonia.Threading;
using Knossos.NET.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Threading;

namespace Knossos.NET.ViewModels
{
    public partial class TaskItemViewModel : ViewModelBase
    {
        public async Task<bool> VerifyMod(Mod mod, CancellationTokenSource cancelSource)
        {
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
                    Name = "Verifying " + mod.ToString();
                    ShowProgressText = false;
                    await Dispatcher.UIThread.InvokeAsync(() => TaskRoot.Add(this));
                    Info = "In Queue";

                    //Wait in Queue
                    while (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() != this)
                    {
                        await Task.Delay(1000);
                        if (cancellationTokenSource.IsCancellationRequested)
                        {
                            throw new TaskCanceledException();
                        }
                    }

                    Info = "Starting";

                    ProgressCurrent = 0;
                    ProgressBarMax = 0;
                    foreach (var pkg in mod.packages)
                    {
                        if (pkg.filelist != null)
                        {
                            ProgressBarMax += pkg.filelist.Count();
                        }
                    }

                    Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.VerifyMod()", "Start verify for :" + mod);
                    mod.ReLoadJson();
                    List<ModPackage> reinstall = new List<ModPackage>();
                    List<string> fileArray = Directory.GetFiles(mod.fullPath, "*.*", SearchOption.AllDirectories).ToList();
                    for (int i = fileArray.Count() - 1; i >= 0; i--)
                    {
                        if (fileArray[i].ToLower().Contains(".json") || fileArray[i].ToLower().Contains(".ini") || mod.tile != null && fileArray[i].ToLower().Contains(mod.tile) || mod.banner != null && fileArray[i].ToLower().Contains(mod.banner) || fileArray[i].ToLower().Contains("kn_screen"))
                            fileArray.RemoveAt(i);
                    }
                    foreach (var pkg in mod.packages)
                    {
                        bool pkgPassed = true;
                        if (pkg.filelist != null)
                        {
                            foreach (var file in pkg.filelist)
                            {
                                if (cancellationTokenSource!.IsCancellationRequested)
                                {
                                    throw new TaskCanceledException();
                                }
                                for (int i = fileArray.Count() - 1; i >= 0; i--)
                                {
                                    if (fileArray[i].ToLower().Replace(Path.DirectorySeparatorChar.ToString(), "").Contains(file.filename!.ToLower().Replace(@"./", "").Replace(@"\", "").Replace(@"/", "")))
                                        fileArray.RemoveAt(i);
                                }
                                ProgressCurrent++;
                                Info = "Files: " + ProgressCurrent + "/" + ProgressBarMax + " Current File: " + file.filename;
                                //Checksum
                                if (file.checksum != null && file.checksum.Count() > 0)
                                {
                                    if (file.checksum[0].ToLower() == "sha256")
                                    {
                                        try
                                        {
                                            using (FileStream? filehash = new FileStream(Path.Combine(mod.fullPath, file.filename ?? ""), FileMode.Open, FileAccess.Read))
                                            {
                                                using (SHA256 checksum = SHA256.Create())
                                                {
                                                    filehash.Position = 0;
                                                    var hashValue = BitConverter.ToString(await checksum.ComputeHashAsync(filehash)).Replace("-", String.Empty);
                                                    filehash.Close();
                                                    if (hashValue.ToLower() != file.checksum[1].ToLower())
                                                    {
                                                        pkgPassed = false;
                                                        Log.Add(Log.LogSeverity.Warning, "TaskItemViewModel.VerifyMod()", file.filename + " failed checksum check! Mod: " + mod);
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            //Filenotfound most likely
                                            pkgPassed = false;
                                            Log.Add(Log.LogSeverity.Warning, "TaskItemViewModel.VerifyMod()", ex);
                                        }
                                    }
                                    else
                                    {
                                        Log.Add(Log.LogSeverity.Warning, "TaskItemViewModel.VerifyMod()", "Unsupported checksum crypto: " + file.checksum[0]);
                                    }
                                }
                                if (!pkgPassed)
                                {
                                    continue;
                                }
                            }
                        }
                        if (pkgPassed)
                        {
                            Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.VerifyMod()", "Pkg Verify OK: " + pkg.name + "Mod: " + mod);
                        }
                        else
                        {
                            pkg.isSelected = true;
                            reinstall.Add(pkg);
                            Log.Add(Log.LogSeverity.Warning, "TaskItemViewModel.VerifyMod()", "Pkg Verify FAIL: " + pkg.name + "Mod: " + mod);
                        }
                    }

                    if (cancellationTokenSource!.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    if (TaskViewModel.Instance!.taskQueue.Count > 0 && TaskViewModel.Instance!.taskQueue.Peek() == this)
                    {
                        TaskViewModel.Instance!.taskQueue.Dequeue();
                    }

                    IsCompleted = true;
                    CancelButtonVisible = false;
                    ProgressCurrent = ProgressBarMax;

                    if (!reinstall.Any())
                    {
                        Info = "PASSED";
                        mod.ClearUnusedData();
                    }
                    else
                    {
                        Info = "FAIL";
                        mod.packages?.ForEach(p => p.isSelected = true); //mark all installed pkgs as selected so they are not deleted!
                        TaskViewModel.Instance?.InstallMod(mod, reinstall);
                    }

                    if (fileArray.Any())
                    {
                        foreach (var file in fileArray)
                        {
                            Log.Add(Log.LogSeverity.Warning, "TaskItemViewModel.VerifyMod()", "Unknown file detected during verify: " + file);
                        }
                        Info += " - " + fileArray.Count() + " Unknown files detected, check log or debug console";
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
                mod.ClearUnusedData();
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
                mod.ClearUnusedData();
                //Only dispose the token if it was created locally
                if (cancelSource == null)
                {
                    cancellationTokenSource?.Dispose();
                }
                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.VerifyMod()", ex);
                return false;
            }
        }
    }
}
