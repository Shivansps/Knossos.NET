using Avalonia.Markup.Xaml.Templates;
using Avalonia.Threading;
using Etc2;
using Knossos.NET.Classes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using VP.NET;
using static Etc2.Dds;

namespace Knossos.NET.ViewModels
{
    public partial class TaskItemViewModel : ViewModelBase
    {
        private int _progressCounter = 0;
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

                    Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.TranscodeLosseFiles()", "Starting to transcode loose files");
                    var config = Knossos.globalSettings.modEtc2TranscodeConfig ?? new Models.GlobalSettings.Etc2Config();
                    var hardwareSupport = AndroidHelper.GpuSupportsBCnTexturesOpenGL();
                    if (config.ForceBC7) hardwareSupport.bc7 = false;
                    if (config.ForceS3TC) hardwareSupport.s3tc = false;

                    await Parallel.ForEachAsync(filePaths, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = cancellationTokenSource.Token },
                        async (file, token) =>
                        {
                            if (Path.GetExtension(file).ToLowerInvariant() != ".dds") { Interlocked.Increment(ref skippedCount); return; }

                            var nameLower = Path.GetFileName(file).ToLowerInvariant();
                            if (forceList != null && !forceList.Contains(nameLower)) { Interlocked.Increment(ref skippedCount); return; }

                            var filename = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                            var outputFileName = Path.Combine(Path.GetDirectoryName(file) ?? "", filename + ".ktx");
                            bool forceRGBA8 = filename.ToLower().Contains("normal") || filename.ToLower().Contains("reflect"); //normal and reflect textures must use ETC2 rgba8
                            if (hardwareSupport.s3tc && hardwareSupport.bc7) forceRGBA8 = false;
                            if (forceList != null) forceRGBA8 = true;
                          
                            int done = Interlocked.Increment(ref _progressCounter);
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                ProgressCurrent = done;
                                Info = $"{done} / {ProgressBarMax} {filename}";
                            });

                            Etc2Status result;
                            try
                            {
                                result = await Task.Run(() => Etc2Transcoder.TranscodeFile(file, outputFileName,
                                            forceRgba8: forceRGBA8, forceResize: config.Resize, quality: config.Quality, jobs: config.Jobs,
                                            forceTranscodeUncompressed: forceList != null, hasS3TC : hardwareSupport.s3tc, hasBC7: hardwareSupport.bc7), token);
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                Interlocked.Increment(ref skippedCount);
                                Log.Add(Log.LogSeverity.Error, "TranscodeLosseFiles()", $"Error transcoding {filename}: {ex.Message}");
                                return;
                            }

                            if (result == Etc2Status.Transcoded)
                            {
                                File.Delete(file);  
                                Interlocked.Increment(ref compressedCount);
                                lock (transcodedNames!) transcodedNames.Add(nameLower);
                            }
                            else
                            {
                                Interlocked.Increment(ref skippedCount);
                                if (result == Etc2Status.NotCompressed) { 
                                    //Log.Add(Log.LogSeverity.Information, "TranscodeLosseFiles()", $"Skipping {filename} (DDS uncompressed).");
                                }
                                else if (result == Etc2Status.Skipped) { 
                                    /* silent skip */ 
                                }
                                else
                                    Log.Add(Log.LogSeverity.Error, "TranscodeLosseFiles()", $"Error transcoding {filename}: {result}");
                            }

                            token.ThrowIfCancellationRequested();
                        });
                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    GC.Collect();

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
