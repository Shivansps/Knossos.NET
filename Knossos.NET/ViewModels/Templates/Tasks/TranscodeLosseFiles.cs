using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using VP.NET;
using Knossos.NET.Classes;
using System.Collections.Concurrent;

namespace Knossos.NET.ViewModels
{
    public partial class TaskItemViewModel : ViewModelBase
    {
        /// <summary>
        /// Transcode .dds BCn files to .ktx ETC2
        /// </summary>
        /// <param name="filePaths"></param>
        /// <param name="alreadySkipped"></param>
        /// <param name="cancelSource"></param>
        /// <returns></returns>
        private async Task<bool> TranscodeLosseFiles(List<string> filePaths, int alreadySkipped, CancellationTokenSource? cancelSource = null, ConcurrentBag<string>? transcodedNames = null, HashSet<string>? forceList = null)
        {
            try
            {
                if (!TaskIsSet)
                {
                    TaskIsSet = true;
                    ProgressBarMax = filePaths.Count();
                    ProgressCurrent = 0;
                    ShowProgressText = false;
                    if (cancelSource != null)
                    {
                        cancellationTokenSource = cancelSource;
                    }
                    else
                    {
                        cancellationTokenSource = new CancellationTokenSource();
                    }
                    CancelButtonVisible = false;
                    Name = "Transcoding loose files";

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    int skippedCount = alreadySkipped;
                    int compressedCount = 0;

                    var cmpxVersion = CmpxTranscoder.NativeVersion();

                    if (cmpxVersion != "")
                    {
                        Log.Add(Log.LogSeverity.Information, "TaskViewModel.TranscodeLosseFiles()", $"Cmpx loaded: v{cmpxVersion}");
                    }
                    else
                    {
                        throw new TaskCanceledException("Unable to load Cmpx library!");
                    }

                    Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.TranscodeLosseFiles()", "Starting to transcode loose files");

                    await Parallel.ForEachAsync(filePaths, new ParallelOptions { MaxDegreeOfParallelism = Knossos.globalSettings.compressionMaxParallelism }, async (file, token) =>
                    {
                        if(Path.GetExtension(file.ToLower()) == ".dds")
                        {
                            var nameLower = Path.GetFileName(file).ToLowerInvariant();
                            if (forceList != null && !forceList.Contains(nameLower))
                            {
                                skippedCount++;
                            }
                            else
                            {
                                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                                if (!input.CanRead)
                                {
                                    throw new TaskCanceledException();
                                }
                                var filename = Path.GetFileNameWithoutExtension(file).ToLower();
                                var outputFileName = Path.Combine(Path.GetDirectoryName(file) ?? "", filename + ".ktx");
                                bool forceRGBA8 = filename.Contains("normal") || filename.Contains("reflect"); // normal and reflect must be ETC2 RGBA8
                                if (forceList != null) forceRGBA8 = true; //on the 2nd pass i need to force-transcode of all files on the list

                                using var output = new FileStream(outputFileName, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                                if (!output.CanWrite)
                                {
                                    throw new TaskCanceledException();
                                }

                                input.Seek(0, SeekOrigin.Begin);
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    Info = ProgressCurrent + " / " + ProgressBarMax + " " + filename;
                                });

                                var result = CmpxTranscoder.Transcode(input, output, forceRGBA8);
                                input.Close();
                                output.Close();

                                if (result == CmpxTranscodeStatus.Transcoded)
                                {
                                    //Delete original
                                    File.Delete(file);
                                    compressedCount++;
                                    transcodedNames?.Add(nameLower);
                                    //Log.Add(Log.LogSeverity.Information, "TaskViewModel.TranscodeLosseFiles()", $"Transcoded {filename} to {outputFileName}");
                                }
                                else
                                {
                                    //Roll back
                                    File.Delete(outputFileName);
                                    skippedCount++;
                                    if (result == CmpxTranscodeStatus.NotCompressed)
                                    {
                                        Log.Add(Log.LogSeverity.Information, "TaskViewModel.TranscodeLosseFiles()", $"Skipping {filename} because it is DDS Uncompressed.");
                                    }
                                    else
                                    {
                                        Log.Add(Log.LogSeverity.Error, "TaskViewModel.TranscodeLosseFiles()", $"Error while transcoding {filename} : {result.ToString()}");
                                    }
                                }
                            }
                        }
                        else
                        {
                            skippedCount++;
                        }

                        ProgressCurrent++;

                        if (cancellationTokenSource.IsCancellationRequested)
                        {
                            throw new TaskCanceledException();
                        }
                    });
                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    IsCompleted = true;
                    ProgressCurrent = ProgressBarMax;
                    Info = "Compressed: " + compressedCount + " Skipped: " + skippedCount;
                    Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.TranscodeLosseFiles()", "Transcoding Loose files finished: " + Info);
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
                Log.Add(Log.LogSeverity.Warning, "TaskItemViewModel.TranscodeLosseFiles()", ex);
                return false;
            }
        }
    }
}
