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
        private async Task<bool> UploadModImages(Mod mod, CancellationTokenSource? cancelSource = null)
        {
            try
            {
                if (!TaskIsSet)
                {
                    TaskIsSet = true;
                    ProgressBarMax = 2;
                    ProgressCurrent = 0;
                    ShowProgressText = false;
                    CancelButtonVisible = false;
                    IsTextTask = false;
                    IsFileDownloadTask = false;
                    Name = "Uploading Mod Images";

                    if (cancelSource != null)
                        cancellationTokenSource = cancelSource;
                    else
                        cancellationTokenSource = new CancellationTokenSource();

                    if (cancellationTokenSource.IsCancellationRequested)
                        throw new TaskCanceledException();

                    //Screenshots
                    if (mod.screenshots != null && mod.screenshots.Any())
                    {
                        ProgressBarMax += mod.screenshots.Length;

                        var uploadedScreenshots = new List<string>(mod.screenshots.Length);
                        var i = 1;

                        foreach (var sc in mod.screenshots)
                        {
                            Info = $"Screenshot Image {i} / {mod.screenshots.Length}";
                            ProgressCurrent++;
                            var checksum = await Nebula.UploadImage(Path.Combine(mod.fullPath, sc));

                            if (checksum != null)
                            {
                                uploadedScreenshots.Add(checksum);
                            }
                            else
                            {
                                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.UploadModImages", $"Failed to upload screenshot: {sc}. Upload will be cancelled.");
                                Info = $"Failed to upload screenshot: {sc}";
                                throw new Exception($"Could not upload screenshot '{sc}'.");
                            }
                            i++;
                        }

                        mod.screenshots = uploadedScreenshots.ToArray();
                    }

                    //Tile
                    if (!string.IsNullOrEmpty(mod.tile))
                    {
                        Info = "Tile Image";
                        var checksum = await Nebula.UploadImage(Path.Combine(mod.fullPath, mod.tile));

                        if (checksum != null)
                        {
                            mod.tile = checksum;
                        }
                        else
                        {
                            Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.UploadModImages", "Failed to upload tile image.");
                            throw new Exception("Could not upload tile image.");
                        }
                    }
                    ProgressCurrent++;
                    //Banner
                    if (!string.IsNullOrEmpty(mod.banner))
                    {
                        Info = "Banner Image";
                        var checksum = await Nebula.UploadImage(Path.Combine(mod.fullPath, mod.banner));

                        if (checksum != null)
                        {
                            mod.banner = checksum;
                        }
                        else
                        {
                            Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.UploadModImages", "Failed to upload banner image.");
                            throw new Exception("Could not upload banner image.");
                        }
                    }
                    ProgressCurrent++;
                    Info = "All images uploaded successfully";
                    IsCompleted = true;
                    CancelButtonVisible = false;
                    ProgressCurrent = ProgressBarMax;
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
                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.UploadModImages()", ex);
                return false;
            }
        }
    }
}
