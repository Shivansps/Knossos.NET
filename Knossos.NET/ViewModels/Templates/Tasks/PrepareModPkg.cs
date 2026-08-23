using Knossos.NET.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Threading;
using Knossos.NET.Views;
using VP.NET;

namespace Knossos.NET.ViewModels
{
    public partial class TaskItemViewModel : ViewModelBase
    {
        private static readonly SemaphoreSlim uploadRetryPromptLock = new(1, 1);

        private async Task ConfirmPackageCompressionRetry(string archivePath, string modFullPath)
        {
            await uploadRetryPromptLock.WaitAsync(cancellationTokenSource!.Token);
            try
            {
                if (cancellationTokenSource.IsCancellationRequested)
                    throw new TaskCanceledException();

                var libraryPath = Knossos.GetKnossosLibraryPath() ?? modFullPath;
                var result = MessageBox.MessageBoxResult.Cancel;
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    result = await MessageBox.Show(MainWindow.instance,
                        "The package archive could not be created or failed its integrity check. " +
                        "This may mean that the drive containing the Knossos.NET library has run out of free space.\n\n" +
                        "Free some space in the library, then select Continue to retry.\n\n" +
                        "Library path:\n" + libraryPath + "\n\n" +
                        "Archive path:\n" + archivePath,
                        "Package creation failed",
                        MessageBox.MessageBoxButtons.ContinueCancel);
                });

                if (result != MessageBox.MessageBoxResult.Continue)
                    throw new TaskCanceledException();
            }
            finally
            {
                uploadRetryPromptLock.Release();
            }
        }

        private async Task<bool> PrepareModPkg(ModPackage pkg, string modFullPath, CancellationTokenSource? cancelSource = null)
        {
            try
            {
                if (!TaskIsSet)
                {
                    TaskIsSet = true;
                    ProgressBarMax = 0;
                    ProgressCurrent = 0;
                    ShowProgressText = false;
                    CancelButtonVisible = false;
                    IsTextTask = false;
                    IsFileDownloadTask = false;
                    Name = "Prepare Pkg: " + pkg.name;
                    var maxCrcAttempts = 25; //How many times try to compress a pkg with 7z in case of CRC error

                    if (cancelSource != null)
                        cancellationTokenSource = cancelSource;
                    else
                        cancellationTokenSource = new CancellationTokenSource();

                    if (cancellationTokenSource.IsCancellationRequested)
                        throw new TaskCanceledException();

                    //Create VP if needed
                    //Create filelist
                    //Filename, archive(7z), orig_name, checksum
                    //Compress with 7z
                    //Clear files.urls
                    //Fill file.filename, file.checksum, file.dest, file.filesize
                    //Note: MacOSX builds must be compressed as tar.gz keeping symblinks as links

                    if (!Directory.Exists(modFullPath + Path.DirectorySeparatorChar + pkg.folder))
                    {
                        Info = "Fail - No Dir";
                        IsCompleted = true;
                        CancelButtonVisible = false;
                        ProgressCurrent = ProgressBarMax;
                        Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.PrepareModPkg()", "Package folder: " + modFullPath + Path.DirectorySeparatorChar + pkg.folder + " does not exist.");
                        throw new TaskCanceledException();
                    }

                    var allfiles = Directory.GetFiles(modFullPath + Path.DirectorySeparatorChar + pkg.folder, "*.*", SearchOption.AllDirectories);
                    if (!allfiles.Any())
                    {
                        Info = "Fail - No Files";
                        IsCompleted = true;
                        CancelButtonVisible = false;
                        ProgressCurrent = ProgressBarMax;
                        Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.PrepareModPkg()", "Package folder: " + modFullPath + Path.DirectorySeparatorChar + pkg.folder + " is empty.");
                        throw new TaskCanceledException();
                    }


                    var zipPath = modFullPath + Path.DirectorySeparatorChar + "kn_upload" + Path.DirectorySeparatorChar + pkg.folder + ".7z";
                    if (pkg.environment != null && pkg.environment.ToLower().Contains("macos"))
                    {
                        zipPath = modFullPath + Path.DirectorySeparatorChar + "kn_upload" + Path.DirectorySeparatorChar + pkg.folder;
                    }
                    if (File.Exists(zipPath))
                    {
                        File.Delete(zipPath);
                    }

                    var filelist = new List<ModFilelist>();
                    var pkgFile = new ModFile();
                    var files = new List<ModFile>() { pkgFile };

                    if (pkg.isVp)
                    {
                        Info = "Creating VP";
                        ProgressBarMax = 100;
                        ProgressCurrent = 0;
                        var vpPath = modFullPath + Path.DirectorySeparatorChar + "kn_upload" + Path.DirectorySeparatorChar + "vps" + Path.DirectorySeparatorChar + pkg.name + ".vp";
                        Directory.CreateDirectory(modFullPath + Path.DirectorySeparatorChar + "kn_upload" + Path.DirectorySeparatorChar + "vps");
                        if (File.Exists(vpPath))
                        {
                            File.Delete(vpPath);
                        }
                        var vp = new VPContainer();
                        vp.AddFolderToRoot(modFullPath + Path.DirectorySeparatorChar + pkg.folder);
                        vp.DisableCompression();
                        await vp.SaveAsAsync(vpPath, compressionCallback, cancellationTokenSource);
                        Info = "Get VP Checksum";
                        var checksumVP = await KnUtils.GetFileHash(vpPath);
                        if (checksumVP != null)
                        {
                            Info = "Compressing (7z)";
                            ProgressBarMax = 100;
                            ProgressCurrent = 0;
                            using (var compressor = new SevenZipConsoleWrapper(sevenZipCallback, cancellationTokenSource))
                            {
                                var crcAttempt = 0;
                                var crcResult = false;
                                do
                                {
                                    if (!await compressor.CompressFile(vpPath, modFullPath + Path.DirectorySeparatorChar + "kn_upload" + Path.DirectorySeparatorChar + "vps", zipPath, true))
                                    {
                                        Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.PrepareModPkg()", "Error while compressing the package");
                                        //Disable failing and instead delete the file if it exists
                                        //throw new TaskCanceledException();
                                        await KnUtils.DeleteFileSafe(zipPath, cancellationTokenSource);
                                        await ConfirmPackageCompressionRetry(zipPath, modFullPath);
                                    }
                                    else
                                    {
                                        //CRC CHECK
                                        Info = "CRC Check";
                                        crcResult = await compressor.VerifyFile(zipPath);
                                        if (!crcResult)
                                        {
                                            if(crcAttempt >= maxCrcAttempts)
                                            {
                                                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.PrepareModPkg()", "CRC error on file: " + zipPath + ". Max attempts reached, cancelling upload...");
                                                throw new TaskCanceledException();
                                            }
                                            Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.PrepareModPkg()", "CRC error on file: " + zipPath + ". Retrying...");
                                            ProgressBarMax = 100;
                                            ProgressCurrent = 0;
                                            Info = "Retry: Compressing (7z)";
                                            await KnUtils.DeleteFileSafe(zipPath, cancellationTokenSource);
                                            crcAttempt++;
                                            await ConfirmPackageCompressionRetry(zipPath, modFullPath);
                                        }
                                    }
                                    if (cancellationTokenSource.IsCancellationRequested)
                                        throw new TaskCanceledException();
                                } while (!crcResult);
                                Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.PrepareModPkg()", "CRC Verify OK on File: " + zipPath);
                            }
                            var fl = new ModFilelist();
                            fl.archive = pkg.folder + ".7z";
                            fl.filename = fl.origName = pkg.folder + ".vp";
                            fl.checksum = new string[2] { "sha256", checksumVP };
                            filelist.Add(fl);
                        }
                        else
                        {
                            throw new TaskCanceledException();
                        }

                    }
                    else
                    {
                        Info = "Adding files";
                        foreach (var file in allfiles)
                        {
                            //Do not add symblinks
                            var fi = new FileInfo(file);
                            if (fi.LinkTarget == null)
                            {
                                var relativePath = Path.GetRelativePath(modFullPath + Path.DirectorySeparatorChar + pkg.folder, file).Replace(@"\", @"/");
                                var checksum = await KnUtils.GetFileHash(file);
                                if (checksum != null)
                                {
                                    var fl = new ModFilelist();
                                    fl.archive = pkg.folder + ".7z";
                                    fl.filename = fl.origName = relativePath;
                                    fl.checksum = new string[2] { "sha256", checksum };
                                    filelist.Add(fl);
                                }
                                else
                                {
                                    throw new TaskCanceledException();
                                }
                            }
                        }

                        ProgressBarMax = 100;
                        ProgressCurrent = 0;
                        using (var compressor = new SevenZipConsoleWrapper(sevenZipCallback, cancellationTokenSource))
                        {
                            if (pkg.environment != null && pkg.environment.ToLower().Contains("macos"))
                            {
                                Info = "Compressing (.tar.gz)";
                                var crcAttempt = 0;
                                var crcResult = false;
                                do
                                {
                                    if (!await compressor.CompressFolderTarGz(modFullPath + Path.DirectorySeparatorChar + pkg.folder, zipPath))
                                    {
                                        Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.PrepareModPkg()", "Error while compressing the package");
                                        //Disable failing and instead delete the file if it exists
                                        //throw new TaskCanceledException();
                                        await KnUtils.DeleteFileSafe(zipPath + ".tar.gz", cancellationTokenSource);
                                        await ConfirmPackageCompressionRetry(zipPath + ".tar.gz", modFullPath);
                                    }
                                    else
                                    {
                                        //CRC CHECK
                                        Info = "CRC Check";
                                        crcResult = await compressor.VerifyFile(zipPath + ".tar.gz");
                                        if (!crcResult)
                                        {
                                            /*
                                            if (crcAttempt >= maxCrcAttempts)
                                            {
                                                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.PrepareModPkg()", "CRC error on file: " + zipPath + ".tar.gz. Max attempts reached, cancelling upload...");
                                                throw new TaskCanceledException();
                                            }
                                            */
                                            Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.PrepareModPkg()", "CRC error on file: " + zipPath + ".tar.gz. Retrying...");
                                            ProgressBarMax = 100;
                                            ProgressCurrent = 0;
                                            Info = "Retry: Compressing (.tar.gz)";
                                            await KnUtils.DeleteFileSafe(zipPath + ".tar.gz", cancellationTokenSource);
                                            crcAttempt++;
                                            await ConfirmPackageCompressionRetry(zipPath + ".tar.gz", modFullPath);
                                        }
                                    }
                                } while (!crcResult);
                                zipPath += ".tar.gz";
                            }
                            else
                            {
                                Info = "Compressing (7z)";
                                var crcAttempt = 0;
                                var crcResult = false;
                                do
                                {
                                    if (!await compressor.CompressFolder(modFullPath + Path.DirectorySeparatorChar + pkg.folder, zipPath))
                                    {
                                        Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.PrepareModPkg()", "Error while compressing the package");
                                        //Disable failing and instead delete the file if it exists
                                        //throw new TaskCanceledException();
                                        await KnUtils.DeleteFileSafe(zipPath, cancellationTokenSource);
                                        await ConfirmPackageCompressionRetry(zipPath, modFullPath);
                                    }
                                    else
                                    {
                                        //CRC CHECK
                                        Info = "CRC Check";
                                        crcResult = await compressor.VerifyFile(zipPath);
                                        if (!crcResult)
                                        {
                                            /*
                                            if (crcAttempt >= maxCrcAttempts)
                                            {
                                                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.PrepareModPkg()", "CRC error on file: " + zipPath + ". Max attempts reached, cancelling upload...");
                                                throw new TaskCanceledException();
                                            }
                                            */
                                            Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.PrepareModPkg()", "CRC error on file: " + zipPath + ". Retrying...");
                                            ProgressBarMax = 100;
                                            ProgressCurrent = 0;
                                            Info = "Retry: Compressing (7z)";
                                            await KnUtils.DeleteFileSafe(zipPath, cancellationTokenSource);
                                            crcAttempt++;
                                            await ConfirmPackageCompressionRetry(zipPath, modFullPath);
                                        }
                                    }
                                } while (!crcResult);
                            }
                            Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.PrepareModPkg()", "CRC Verify OK on File: " + zipPath);
                        }
                    }

                    //Wait for file to be closed
                    while (KnUtils.IsFileInUse(zipPath))
                    {
                        Info = "Waiting for file to be closed";
                        Log.Add(Log.LogSeverity.Information, "TaskItemViewModel.PrepareModPkg()", "Waiting for file to be closed: " + zipPath);
                        await Task.Delay(100);
                        if (cancellationTokenSource.IsCancellationRequested)
                            throw new TaskCanceledException();
                    }

                    Info = "Getting Hash";
                    /*
                     * TODO: it is unclear to me, at this moment, why this would be needed since 7z should extract with fullpath.
                     * Using the pkg as work folder. 
                     * FSO builds seems to use it.
                     *
                    */
                    pkgFile.dest = "";
                    var checksumZip = await KnUtils.GetFileHash(zipPath);
                    if (checksumZip != null)
                    {
                        var fi = new FileInfo(zipPath);
                        pkgFile.filesize = fi.Length;
                        pkgFile.filename = pkg.folder + ".7z";
                        pkgFile.checksum = new string[2] { "sha256", checksumZip };
                    }
                    else
                    {
                        throw new TaskCanceledException();
                    }
                    pkgFile.urls = null;
                    pkg.files = files.ToArray();
                    pkg.filelist = filelist.ToArray();
                    if (pkg.executables == null)
                    {
                        pkg.executables = new List<ModExecutable>();
                    }
                    if (pkg.dependencies == null)
                    {
                        pkg.dependencies = new ModDependency[0];
                    }
                    Info = "OK";
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
                Log.Add(Log.LogSeverity.Error, "TaskItemViewModel.PrepareModPkg()", ex);
                return false;
            }
        }
    }
}
