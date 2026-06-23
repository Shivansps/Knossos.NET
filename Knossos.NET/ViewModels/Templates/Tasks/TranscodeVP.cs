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

namespace Knossos.NET.ViewModels
{
    public partial class TaskItemViewModel : ViewModelBase
    {
        private async Task<bool> TranscodeVP(FileInfo vpFile, CancellationTokenSource? cancelSource = null, ConcurrentBag<string>? transcodedNames = null, HashSet<string>? forceList = null)
        {
            var workFolder = "";
            try
            {
                if (!TaskIsSet)
                {
                    TaskIsSet = true;
                    ProgressBarMax = 1;
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
                    Name = "Transcoding: " + vpFile.Name;

                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }

                    Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.TranscodeVP()", "Starting to transcode VP file: " + vpFile.Name);

                    workFolder = Path.Combine(Knossos.GetKnossosLibraryPath() ?? KnUtils.GetKnossosDataFolderPath() , "temp", Guid.NewGuid().ToString());
                    Directory.CreateDirectory(workFolder);
                    try
                    {
                        //make sure this folder is deleted at next startup if it remains
                        File.Create(Path.Combine(workFolder, "knossos_net_download.token")).Close(); 
                    }
                    catch { }

                    var vp = new VPContainer();
                    await vp.LoadVP(vpFile.FullName);
                    var changes = false;
                    if (vp.vpFiles != null && vp.vpFiles.Any())
                    {
                        var ddsFiles = vp.vpFiles[0].SearchForFileExtension(".dds");
                        if(ddsFiles.Any())
                        {
                            ProgressBarMax = ddsFiles.Count();
                            foreach (var ddsFile in ddsFiles)
                            {
                                var nameLower = ddsFile.info.name.ToLowerInvariant();
                                await Dispatcher.UIThread.InvokeAsync(() => Info = $"{ProgressCurrent} / {ProgressBarMax} {nameLower}");
                                ProgressCurrent++;
                                if (forceList != null && !forceList.Contains(nameLower)) continue;

                                byte[] ddsBytes;
                                using (var inputStream = new MemoryStream())
                                {
                                    await ddsFile.ReadToStream(inputStream);
                                    ddsBytes = inputStream.ToArray();
                                }

                                var filename = Path.GetFileNameWithoutExtension(ddsFile.info.name);
                                var outputFileName = Path.Combine(workFolder, filename + ".ktx");
                                bool forceRGBA8 = filename.ToLower().Contains("normal") || filename.ToLower().Contains("reflect"); //normal and reflect textures must use ETC2 rgba8
                                if (forceList != null) forceRGBA8 = true;

                                byte[]? ktx;
                                try
                                {
                                    ktx = await Task.Run(() => Etc2Transcoder.TranscodeToKtxBytes(ddsBytes, forceRgba8: forceRGBA8, 
                                        forceResize: true, quality: 10, jobs: 8, forceTranscodeUncompressed : forceList != null), cancellationTokenSource.Token);
                                }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception ex)
                                {
                                    Log.Add(Log.LogSeverity.Error, "TaskViewModel.TranscodeVP()", $"Error transcoding {filename}: {ex.Message}");
                                    continue;
                                }

                                if (ktx == null)
                                {
                                    Log.Add(Log.LogSeverity.Information, "TaskViewModel.TranscodeVP()", $"Skipping {filename} (DDS uncompressed).");
                                    continue;
                                }

                                await File.WriteAllBytesAsync(outputFileName, ktx, cancellationTokenSource.Token);
                                ddsFile.Delete();
                                changes = true;
                                ddsFile.parent!.AddFile(new FileInfo(outputFileName));
                                transcodedNames?.Add(nameLower);
                            }
                            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                        }
                    }
                    await Task.Delay(2000);
                    if (changes)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            ProgressCurrent = 0;
                            Info = "Saving...";
                        });
                        await vp.SaveAsAsync(vpFile.FullName, compressionCallback, cancellationTokenSource);
                    }

                    await Task.Delay(2000);
                    Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.TranscodeVP()", "Transcode VP finished: " + vpFile.Name + " Processed Files: " + ProgressBarMax);
                    Info = "";
                    IsCompleted = true;
                    ProgressCurrent = ProgressBarMax;
                    if (workFolder != "" && Directory.Exists(workFolder))
                    {
                        Directory.Delete(workFolder, true);
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
                if (workFolder != "" && Directory.Exists(workFolder))
                {
                    Directory.Delete(workFolder, true);
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
                if (workFolder != "" && Directory.Exists(workFolder))
                {
                    Directory.Delete(workFolder, true);
                }
                Log.Add(Log.LogSeverity.Warning, "TaskItemViewModel.TranscodeVP()", ex);
                return false;
            }
        }
    }
}
