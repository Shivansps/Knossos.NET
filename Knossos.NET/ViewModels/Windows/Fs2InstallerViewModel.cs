using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes;
using Knossos.NET.Classes.Inno;
using Knossos.NET.Models;
using Knossos.NET.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace Knossos.NET.ViewModels
{
    /// <summary>
    /// Fs2 Retail Window View Model
    /// </summary>
    public partial class Fs2InstallerViewModel : ViewModelBase
    {
        /// <summary>
        /// Must have file list
        /// </summary>
        private readonly string[] required =
        {
            "root_fs2.vp", "smarty_fs2.vp", "sparky_fs2.vp",
            "sparky_hi_fs2.vp", "stu_fs2.vp", "tango1_fs2.vp",
            "tango2_fs2.vp", "tango3_fs2.vp", "warble_fs2.vp"
        };

        /// <summary>
        /// Additional files to find and copy
        /// </summary>
        private readonly string[] optional =
        {
            "hud_1.hcf", "hud_2.hcf", "hud_3.hcf", "movies_fs2.vp", "multi-mission-pack.vp", "multi-voice-pack.vp", "bastion.ogg", "colossus.ogg",
            "endpart1.ogg", "endprt2a.ogg", "endprt2b.ogg", "intro.ogg", "mono1.ogg", "mono2.ogg", "mono3.ogg", "mono4.ogg", "bastion.mve",
            "colossus.mve", "endpart1.mve", "endprt2a.mve", "endprt2b.mve", "intro.mve", "mono1.mve", "mono2.mve", "mono3.mve", "mono4.mve"
        };

        private List<IStorageFile> filePaths = new List<IStorageFile>();
        private IStorageFile? compressedFile;
        private HashSet<string> compressedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSelectSource))]
        internal bool isInstalling = false;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSelectSource))]
        internal bool isSelectingSource = false;
        public bool CanSelectSource => !IsInstalling && !IsSelectingSource;
        [ObservableProperty]
        internal bool canInstall = false;
        [ObservableProperty]
        internal int progressMax = 100;
        [ObservableProperty]
        internal int progressCurrent = 0;
        [ObservableProperty]
        internal string installText = string.Empty;
        private string? gogExe = null;
        private KnossosWindow? window;
        // GOG installer sources. Desktop: a real path. Sandboxed (Android/iOS): storage
        // handles for the .exe and its .bin slice(s), accessed as streams (no file paths).
        private IStorageFile? gogExeFile = null;
        private readonly Dictionary<string, IStorageFile> gogBins =
            new Dictionary<string, IStorageFile>(StringComparer.OrdinalIgnoreCase);

        public Fs2InstallerViewModel() 
        { 
        }

        public Fs2InstallerViewModel(KnossosWindow window)
        {
            this.window = window;
        }

        /// <summary>
        /// The main install process
        /// </summary>
        internal async void InstallFS2Command()
        {
            if (!CanSelectSource || !CanInstall)
                return;
            if(Knossos.GetKnossosLibraryPath() == null)
            {
                await MessageBox.Show(MainWindow.instance, "The KnossosNET library path is not set, first set the library path in the settings tab before installing FS2 Retail.", "Library path is null", MessageBox.MessageBoxButtons.OK);
                return;
            }
            
            IsInstalling = true;
            await Task.Run(async () => { 
                var createdFiles = new List<string>();
                bool installationComplete = false;
                FileStream CreateInstallFile(string path)
                {
                    // This is a fresh installation. Only register files we create,
                    // so a failed attempt can remove them without touching others.
                    var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                    createdFiles.Add(path);
                    return stream;
                }

                try
                {
                    Dispatcher.UIThread.Invoke(new Action(() => { ProgressCurrent = 0; }));
                    var fs2Path = Path.Combine(Knossos.GetKnossosLibraryPath()!, "FS2");
                    var moviesPath = Path.Combine(fs2Path, "data", "movies");
                    var playersPath = Path.Combine(fs2Path, "data", "players");
                    Directory.CreateDirectory(fs2Path);
                    Directory.CreateDirectory(moviesPath);
                    Directory.CreateDirectory(playersPath);

                    //GoG (single .exe or .exe + .bin, GOG Galaxy multi-part)
                    if (gogExe != null || gogExeFile != null)
                    {
                        var archive = OpenGogArchive(out var cleanup);
                        try
                        {
                            // Gather the files we want (required + any present optional) and
                            // their destination paths, using the same routing as before.
                            var wanted = new List<InnoFile>();
                            var destPath = new Dictionary<InnoFile, string>();
                            foreach (var name in required.Concat(optional))
                            {
                                var f = archive.FindFile(name);
                                if (f == null)
                                    continue;
                                var dir = RouteDir(name, fs2Path, playersPath, moviesPath);
                                if (dir == "")
                                    continue;
                                wanted.Add(f);
                                destPath[f] = Path.Combine(dir, name);
                            }

                            Dispatcher.UIThread.Invoke(new Action(() => { InstallText = "Extracting FreeSpace 2 data..."; }));

                            // Single forward pass over the .bin (works on Android forward-only
                            // streams). ExtractFiles verifies each file's checksum internally.
                            archive.ExtractFiles(
                                wanted,
                                file => CreateInstallFile(destPath[file]),
                                file =>
                                {
                                    Dispatcher.UIThread.Invoke(new Action(() =>
                                    {
                                        InstallText = $"Extracted: {file.Name}";
                                        ProgressCurrent++;
                                    }));
                                });
                        }
                        finally
                        {
                            cleanup();
                            archive.Dispose();
                        }
                    }

                    if (compressedFile != null)
                    {
                        using var source = await OpenCompressedStreamAsync(compressedFile);
                        var extracted = Fs2RetailArchive.ReadFiles(source, compressedFiles, (name, data) =>
                        {
                            Dispatcher.UIThread.Invoke(() => { InstallText = $"Extracting: {name}"; });
                            var dir = RouteDir(name, fs2Path, playersPath, moviesPath);
                            using (var destination = CreateInstallFile(Path.Combine(dir, name)))
                                data.CopyTo(destination);
                            Dispatcher.UIThread.Invoke(() => { ProgressCurrent++; });
                        });
                        if (!extracted.SetEquals(compressedFiles))
                            throw new InvalidDataException("The compressed file has changed. Select it again before installing.");
                    }

                    //Folder Copy
                    if (filePaths.Any())
                    {
                        foreach (var file in filePaths)
                        {
                            Dispatcher.UIThread.Invoke(new Action(() => { InstallText = $"Copying: {file.Name}"; }));
                            /* VPs */
                            if (file.Name.ToLower().Contains(".vp"))
                            {
                                using (var streamOrg = await file.OpenReadAsync())
                                {
                                    using (var streamDst = CreateInstallFile(Path.Combine(fs2Path, file.Name)))
                                    {
                                        await streamOrg.CopyToAsync(streamDst);
                                    }
                                }
                            }
                            else
                            {
                                /* Player Profiles */
                                if (file.Name.ToLower().Contains(".hcf"))
                                {
                                    using (var streamOrg = await file.OpenReadAsync())
                                    {
                                        using (var streamDst = CreateInstallFile(Path.Combine(playersPath, file.Name)))
                                        {
                                            await streamOrg.CopyToAsync(streamDst);
                                        }
                                    }
                                }
                                else
                                {
                                    /* Movies */
                                    using (var streamOrg = await file.OpenReadAsync())
                                    {
                                        using (var streamDst = CreateInstallFile(Path.Combine(moviesPath, file.Name)))
                                        {
                                            await streamOrg.CopyToAsync(streamDst);
                                        }
                                    }
                                }
                            }
                            Dispatcher.UIThread.Invoke(new Action(() => { ProgressCurrent++; }));
                        }
                    }

                    /* FINISH */
                    Dispatcher.UIThread.Invoke(new Action(() => { ProgressCurrent = ProgressMax;  InstallText = "Finishing tasks..."; }));
                    var fs2Mod = new Mod();
                    fs2Mod.fullPath = fs2Path;
                    fs2Mod.folderName = "FS2";
                    fs2Mod.installed = true;
                    fs2Mod.id = "FS2";
                    fs2Mod.title = "Retail FS2";
                    fs2Mod.type = ModType.tc;
                    fs2Mod.parent = "FS2";
                    fs2Mod.version = "1.20.0";
                    fs2Mod.stability = "stable"; //wut?
                    fs2Mod.description = "[b][i]The year is 2367, thirty two years after the Great War. Or at least that is what YOU thought was the Great War. The endless line of Shivan capital ships, bombers and fighters with super advanced technology was nearly overwhelming.\n\nAs the Terran and Vasudan races finish rebuilding their decimated societies, a disturbance lurks in the not-so-far reaches of the Gamma Draconis system.\n\nYour nemeses have arrived... and they are wondering what happened to their scouting party.[/i][/b]\n\n[hr]FreeSpace 2 is a 1999 space combat simulation computer game developed by Volition as the sequel to Descent: FreeSpace \u2013 The Great War. It was completed ahead of schedule in less than a year, and released to very positive reviews.\n\nThe game continues on the story from Descent: FreeSpace, once again thrusting the player into the role of a pilot fighting against the mysterious aliens, the Shivans. While defending the human race and its alien Vasudan allies, the player also gets involved in putting down a rebellion. The game features large numbers of fighters alongside gigantic capital ships in a battlefield fraught with beams, shells and missiles in detailed star systems and nebulae.";
                    fs2Mod.tile = "kn_tile.png";
                    fs2Mod.banner = "kn_banner.png";
                    fs2Mod.releaseThread = "http://www.hard-light.net/forums/index.php";
                    fs2Mod.videos = new string[] { "https://www.youtube.com/watch?v=ufViyhrXzTE" };
                    fs2Mod.screenshots = new string[] { "kn_screen_0.jpg", "kn_screen_1.jpg", "kn_screen_2.jpg", "kn_screen_3.jpg", "kn_screen_4.jpg", "kn_screen_5.jpg", "kn_screen_6.jpg", "kn_screen_7.jpg", "kn_screen_8.jpg", "kn_screen_9.jpg", "kn_screen_10.jpg", "kn_screen_11.jpg" };
                    fs2Mod.firstRelease = "1999-09-30";
                    fs2Mod.lastUpdate = "1999-12-03";
                    fs2Mod.notes = string.Empty;
                    fs2Mod.cmdline = string.Empty;
                    fs2Mod.attachments = new string[] { };
                    fs2Mod.modFlag.Add("FS2");
                    var fs2Pkg= new ModPackage();
                    fs2Pkg.name = "Content";
                    fs2Pkg.status = "required";
                    var fs2Dep = new ModDependency();
                    fs2Dep.id = "FSO";
                    fs2Dep.version = ">=3.8.1";
                    fs2Pkg.dependencies = new ModDependency[] { fs2Dep };
                    fs2Mod.packages.Add(fs2Pkg);
                    fs2Mod.modSource = ModSource.local;
                    foreach (var name in new[] { "kn_tile.png", "kn_banner.png" }.Concat(fs2Mod.screenshots))
                    {
                        using var source = AssetLoader.Open(new Uri($"avares://Knossos.NET/Assets/fs2_res/{name}"));
                        using var destination = CreateInstallFile(Path.Combine(fs2Path, name));
                        source.CopyTo(destination);
                    }

                    // Metadata marks a completed installation. Write it only after
                    // every data and asset stream has closed successfully. SaveJson()
                    // swallows write errors, so serialize here and let them abort.
                    using (var metadata = CreateInstallFile(Path.Combine(fs2Path, "mod.json")))
                    {
                        JsonSerializer.Serialize(metadata, fs2Mod, new JsonSerializerOptions
                        {
                            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                            WriteIndented = true
                        });
                    }
                    installationComplete = true;
                    Dispatcher.UIThread.Invoke(new Action(() => { InstallText = "Install Complete!, KnossosNET is reloading the library..."; }));
                    Knossos.ResetBasePath();
                    await Task.Delay(3000);
                    Dispatcher.UIThread.Invoke(new Action(() => { window?.Close(); }));
                }
                catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "Fs2InstallerViewModel.InstallFS2Command()",ex);
                    bool cleanupFailed = false;
                    if (!installationComplete)
                    {
                        foreach (var path in createdFiles)
                        {
                            try { File.Delete(path); }
                            catch (Exception cleanupError)
                            {
                                cleanupFailed = true;
                                Log.Add(Log.LogSeverity.Error, "Fs2InstallerViewModel.InstallFS2Command.Cleanup", cleanupError);
                            }
                        }
                    }
                    Dispatcher.UIThread.Invoke(new Action(() =>
                    {
                        InstallText = "Installation failed.";
                        var cleanupMessage = cleanupFailed ? " Some incomplete files could not be removed. See the log for details." : "";
                        MessageBox.Show(MainWindow.instance, $"Installation failed: {ex.Message}.{cleanupMessage}", "FS2 installation failed", MessageBox.MessageBoxButtons.OK);
                    }));
                }
                finally
                {
                    Dispatcher.UIThread.Invoke(new Action(() => { IsInstalling = false; }));
                }
            });
        }

        /// <summary>
        /// Open file dialog to select a gog exe file, it checks that this is a valid FS2 install file
        /// by counting the number of requiered and optional files present in it.
        /// </summary>
        internal async void LoadGoGExeCommand()
        {
            if (!CanSelectSource)
                return;
            var top = KnUtils.GetTopLevel();
            // Sandboxed platforms (Android/Browser) have no usable file paths, so we
            // pick the FOLDER (which grants access to both the .exe and its .bin siblings).
            // Desktop keeps the familiar .exe file picker (path-based).
            bool sandboxed = KnUtils.IsAndroid || KnUtils.IsBrowser;
            try
            {
                IsSelectingSource = true;
                ResetSource();
                InstallText = "Checking GOG installer...";

                if (!sandboxed)
                {
                    var options = new FilePickerOpenOptions();
                    options.AllowMultiple = false;
                    options.Title = "Select your Freespace 2 gog .exe installer file";
                    var result = await top.StorageProvider.OpenFilePickerAsync(options);
                    if (result == null || result.Count == 0)
                        return;
                    // If the platform gives us a real path, use it; otherwise fall back to folder mode.
                    var localPath = result[0].TryGetLocalPath();
                    if (localPath != null)
                        gogExe = localPath;
                    else
                        sandboxed = true;
                }

                if (sandboxed && gogExe == null)
                {
                    var fopts = new FolderPickerOpenOptions();
                    fopts.AllowMultiple = false;
                    fopts.Title = "Select the folder with your Freespace 2 gog installer (.exe + .bin)";
                    var folders = await top.StorageProvider.OpenFolderPickerAsync(fopts);
                    if (folders == null || folders.Count == 0)
                        return;
                    await FindGogFilesInFolder(folders[0]);
                    if (gogExeFile == null)
                    {
                        await MessageBox.Show(MainWindow.instance, "No .exe installer was found in that folder.", "Installer not found", MessageBox.MessageBoxButtons.OK);
                        return;
                    }
                }

                // Validate on a background thread: opening storage streams blocks, and doing
                // that on the UI thread can deadlock on Android.
                int total = 0;
                bool allRequired = false;
                await Task.Run(() =>
                {
                    var archive = OpenGogArchive(out var cleanup);
                    try
                    {
                        int req = required.Count(r => archive.FindFile(r) != null);
                        allRequired = req == required.Length;
                        total = req + optional.Count(o => archive.FindFile(o) != null);
                    }
                    finally
                    {
                        cleanup();
                        archive.Dispose();
                    }
                });

                if (!allRequired)
                {
                    gogExe = null;
                    gogExeFile = null;
                    gogBins.Clear();
                    await MessageBox.Show(MainWindow.instance, "Unable to find all the required Freespace 2 files in gog installer.", "Files not found", MessageBox.MessageBoxButtons.OK);
                    return;
                }
                ProgressMax = total;
                InstallText = string.Empty;
                CanInstall = true;
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Fs2InstallerViewModel.LoadGoGExeCommand()", ex);
                gogExe = null;
                gogExeFile = null;
                gogBins.Clear();
            }
            finally
            {
                IsSelectingSource = false;
            }
        }

        private void ResetSource()
        {
            CanInstall = false;
            filePaths.Clear();
            gogExe = null;
            gogExeFile = null;
            gogBins.Clear();
            compressedFile = null;
            compressedFiles.Clear();
            ProgressCurrent = 0;
            InstallText = string.Empty;
        }

        /// <summary>
        /// Select and validate any archive format supported by SharpCompress.
        /// Detection uses the contents rather than restricting file extensions.
        /// </summary>
        internal async void LoadCompressedFileCommand()
        {
            if (!CanSelectSource)
                return;
            try
            {
                IsSelectingSource = true;
                var result = await KnUtils.GetTopLevel().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    Title = "Select a compressed file containing FreeSpace 2 retail data"
                });
                if (result == null || result.Count == 0)
                    return;

                ResetSource();
                InstallText = "Checking compressed file...";
                var selected = result[0];
                var found = await Task.Run(async () =>
                {
                    using var source = await OpenCompressedStreamAsync(selected);
                    return Fs2RetailArchive.ReadFiles(source, required.Concat(optional));
                });
                var missing = required.Where(name => !found.Contains(name)).ToArray();
                if (missing.Length > 0)
                {
                    await MessageBox.Show(MainWindow.instance,
                        $"Unable to find all the required FreeSpace 2 files in this archive. Missing: {string.Join(", ", missing)}",
                        "Files not found", MessageBox.MessageBoxButtons.OK);
                    return;
                }
                compressedFile = selected;
                compressedFiles = found;
                ProgressMax = found.Count;
                CanInstall = true;
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Fs2InstallerViewModel.LoadCompressedFileCommand()", ex);
                ResetSource();
                await MessageBox.Show(MainWindow.instance,
                    $"Unable to read the compressed file. It may be damaged, encrypted, or use an unsupported format or compression method. {ex.Message}",
                    "Cannot open compressed file", MessageBox.MessageBoxButtons.OK);
            }
            finally
            {
                InstallText = string.Empty;
                IsSelectingSource = false;
            }
        }

        private static async Task<Stream> OpenCompressedStreamAsync(IStorageFile file)
        {
            var source = await file.OpenReadAsync();
            if (source.CanSeek)
                return source;

            // Storage providers can return forward-only streams. Use a temporary
            // file rather than buffering a multi-gigabyte archive in memory.
            FileStream? buffered = null;
            try
            {
                buffered = new FileStream(Path.Combine(Path.GetTempPath(), $"knossos-retail-{Guid.NewGuid():N}.tmp"),
                    FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
                await source.CopyToAsync(buffered);
                buffered.Position = 0;
                return buffered;
            }
            catch
            {
                buffered?.Dispose();
                throw;
            }
            finally
            {
                source.Dispose();
            }
        }

        /// <summary>
        /// Finds the GOG installer .exe and its .bin slice(s) inside a picked folder,
        /// storing them as storage handles (streams are opened later, at install time).
        /// </summary>
        private async Task FindGogFilesInFolder(IStorageFolder folder)
        {
            IStorageFile? anyExe = null;
            await foreach (var item in folder.GetItemsAsync())
            {
                if (item is not IStorageFile f)
                    continue;
                var lname = f.Name.ToLowerInvariant();
                if (lname.EndsWith(".exe"))
                {
                    anyExe ??= f;
                    if (lname.Contains("setup"))
                        gogExeFile ??= f;   // prefer a setup*.exe
                }
                else if (lname.EndsWith(".bin"))
                {
                    gogBins[f.Name] = f;
                }
            }
            gogExeFile ??= anyExe;
        }

        /// <summary>
        /// Opens the selected GOG installer as an InnoArchive, from a path (desktop) or from
        /// storage streams (sandboxed). The returned cleanup action releases opened streams.
        /// </summary>
        private InnoArchive OpenGogArchive(out Action cleanup)
        {
            if (gogExe != null)
            {
                cleanup = () => { };
                return new InnoArchive(gogExe);
            }
            if (gogExeFile != null)
            {
                var opened = new List<Stream>();
                InnoArchive.SliceOpener opener = (idx, expectedName) =>
                {
                    if (gogBins.TryGetValue(expectedName, out var bf))
                    {
                        var bs = bf.OpenReadAsync().GetAwaiter().GetResult();
                        opened.Add(bs);
                        return bs;
                    }
                    return null;
                };
                var exeStream = gogExeFile.OpenReadAsync().GetAwaiter().GetResult();
                var archive = new InnoArchive(exeStream, gogExeFile.Name, opener, leaveOpen: false);
                cleanup = () => { foreach (var st in opened) { try { st.Dispose(); } catch { } } };
                return archive;
            }
            throw new InvalidOperationException("No GOG installer selected.");
        }

        /// <summary>
        /// Destination directory for a file name (same routing as the folder-copy path).
        /// </summary>
        private static string RouteDir(string name, string fs2Path, string playersPath, string moviesPath)
        {
            switch (Path.GetExtension(name).ToLowerInvariant())
            {
                case ".vp":
                case ".vpc": return fs2Path;
                case ".hcf": return playersPath;
                case ".ogg":
                case ".mve": return moviesPath;
                default: return "";
            }
        }

        /// <summary>
        /// Select a fs2retail folder
        /// </summary>
        internal async void LoadFolderCommand()
        {
            if (!CanSelectSource)
                return;
            try
            {
                IsSelectingSource = true;
                var result = await KnUtils.GetTopLevel().StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    AllowMultiple = false,
                    Title = "Select your Freespace 2 retail folder"
                });
                if (result == null || result.Count == 0)
                    return;

                ResetSource();
                InstallText = "Checking retail folder...";
                await ProcessFolder(result[0]);
                if (!required.All(name => filePaths.Any(file => string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase))))
                {
                    await MessageBox.Show(MainWindow.instance, "Unable to find all the required Freespace 2 files in this directory.", "Files not found", MessageBox.MessageBoxButtons.OK);
                    return;
                }
                ProgressMax = filePaths.Count;
                CanInstall = true;
            }
            catch (Exception ex)
            {
                ResetSource();
                Log.Add(Log.LogSeverity.Error, "Fs2InstallerViewModel.LoadFolderCommand()", ex);
            }
            finally
            {
                InstallText = string.Empty;
                IsSelectingSource = false;
            }
        }

        /// <summary>
        /// Search the folder to find all files
        /// </summary>
        /// <param name="path"></param>
        private async Task ProcessFolder(IStorageFolder path)
        {
            await foreach (var item in path.GetItemsAsync())
            {
                if (item is IStorageFile file)
                {
                    if (required.Concat(optional).Contains(file.Name, StringComparer.OrdinalIgnoreCase)
                        && !filePaths.Any(existing => string.Equals(existing.Name, file.Name, StringComparison.OrdinalIgnoreCase)))
                        filePaths.Add(file);
                }
                else if (item is IStorageFolder subfolder)
                {
                    await ProcessFolder(subfolder);
                }
            }
        }
    }
}
