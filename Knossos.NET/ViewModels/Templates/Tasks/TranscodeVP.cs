using Avalonia.Threading;
using Knossos.NET.Classes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

                    var cmpxVersion = CmpxTranscoder.NativeVersion();

                    if (cmpxVersion != "")
                    {
                        Log.Add(Log.LogSeverity.Information, "TaskViewModel.TranscodeLosseFiles()", $"Cmpx loaded: v{cmpxVersion}");
                    }
                    else
                    {
                        throw new TaskCanceledException("Unable to load Cmpx library!");
                    }

                    Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.TranscodeVP()", "Starting to transcode VP file: " + vpFile.Name);

                    workFolder = Path.Combine(KnUtils.GetFSODataFolderPath(), "transcodeTemp");
                    Directory.CreateDirectory(workFolder);

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
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    Info = ProgressCurrent + " / " + ProgressBarMax + " " + nameLower;
                                });
                                ProgressCurrent++;
                                if (forceList != null && !forceList.Contains(nameLower)) continue; //on the 2nd pass only process files on the forced list

                                using var inputStream = new MemoryStream();
                                await ddsFile.ReadToStream(inputStream);
                                inputStream.Position = 0;
                                var filename = Path.GetFileNameWithoutExtension(ddsFile.info.name);
                                var outputFileName = Path.Combine(workFolder, filename+ ".ktx");
                                bool forceRGBA8 = filename.Contains("normal") || filename.Contains("reflect"); // normal and reflect must be ETC2 RGBA8
                                if (forceList != null) forceRGBA8 = true; //on the 2nd pass i need to force-transcode of all files on the list

                                using var output = new FileStream(outputFileName, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                                var result = CmpxTranscoder.Transcode(inputStream, output, forceRGBA8);
                                inputStream.Close();
                                output.Close();

                                if (result == CmpxTranscodeStatus.Transcoded)
                                {
                                    //Delete original
                                    ddsFile.Delete();
                                    changes = true;
                                    ddsFile.parent!.AddFile(new FileInfo(outputFileName));
                                    transcodedNames?.Add(nameLower);
                                    //Log.Add(Log.LogSeverity.Information, "TaskViewModel.TranscodeVP()", $"Transcoded {filename}");
                                }
                                else
                                {
                                    //Roll back
                                    File.Delete(outputFileName);
                                    if (result == CmpxTranscodeStatus.NotCompressed)
                                    {
                                        Log.Add(Log.LogSeverity.Information, "TaskViewModel.TranscodeVP()", $"Skipping {filename} because it is DDS Uncompressed.");
                                    }
                                    else
                                    {
                                        Log.Add(Log.LogSeverity.Error, "TaskViewModel.TranscodeVP()", $"Error while transcoding {filename} : {result.ToString()}");
                                    }
                                }
                            }
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
