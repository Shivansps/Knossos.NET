using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Knossos.NET.Classes.Inno;
using Knossos.NET.Models;
using Knossos.NET.Views;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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

        [ObservableProperty]
        internal bool isInstalling = false;
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
        private int reqFilesFound = 0;
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
            if(Knossos.GetKnossosLibraryPath() == null)
            {
                await MessageBox.Show(MainWindow.instance, "The KnossosNET library path is not set, first set the library path in the settings tab before installing FS2 Retail.", "Library path is null", MessageBox.MessageBoxButtons.OK);
                return;
            }
            
            await Task.Run(async () => { 
                try
                {
                    IsInstalling = true;
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
                                file => File.Create(destPath[file]),
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
                                    using (var streamDst = new FileStream(Path.Combine(fs2Path, file.Name), FileMode.Create, FileAccess.Write))
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
                                        using (var streamDst = new FileStream(Path.Combine(playersPath, file.Name), FileMode.Create, FileAccess.Write))
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
                                        using (var streamDst = new FileStream(Path.Combine(moviesPath, file.Name), FileMode.Create, FileAccess.Write))
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
                    fs2Mod.SaveJson();
                    try
                    {
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_tile.png"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_tile.png")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_banner.png"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_banner.png")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_0.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_0.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_1.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_1.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_2.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_2.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_3.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_3.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_4.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_4.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_5.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_5.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_6.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_6.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_7.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_7.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_8.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_8.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_9.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_9.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_10.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_10.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                        using (var fileStream = File.Create(Knossos.GetKnossosLibraryPath() + Path.DirectorySeparatorChar + "FS2" + Path.DirectorySeparatorChar + "kn_screen_11.jpg"))
                        {
                            AssetLoader.Open(new Uri("avares://Knossos.NET/Assets/fs2_res/kn_screen_11.jpg")).CopyTo(fileStream);
                            fileStream.Close();
                        }
                    }
                    catch { }
                    Dispatcher.UIThread.Invoke(new Action(() => { InstallText = "Install Complete!, KnossosNET is reloading the library..."; }));
                    Knossos.ResetBasePath();
                    await Task.Delay(3000);
                    Dispatcher.UIThread.Invoke(new Action(() => { window?.Close(); }));
                }
                catch(Exception ex)
                {
                    Log.Add(Log.LogSeverity.Error, "Fs2InstallerViewModel.InstallFS2Command()",ex);
                    Dispatcher.UIThread.Invoke(new Action(() => { MessageBox.Show(MainWindow.instance, $"An error has ocurred during file copy: {ex.Message}.", "An error has ocurred", MessageBox.MessageBoxButtons.OK); }));
                }
            });
        }

        /// <summary>
        /// Open file dialog to select a gog exe file, it checks that this is a valid FS2 install file
        /// by counting the number of requiered and optional files present in it.
        /// </summary>
        internal async void LoadGoGExeCommand()
        {
            var top = KnUtils.GetTopLevel();
            // Sandboxed platforms (Android/Browser) have no usable file paths, so we
            // pick the FOLDER (which grants access to both the .exe and its .bin siblings).
            // Desktop keeps the familiar .exe file picker (path-based).
            bool sandboxed = KnUtils.IsAndroid || KnUtils.IsBrowser;
            try
            {
                CanInstall = false;
                gogExe = null;
                gogExeFile = null;
                gogBins.Clear();

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
                CanInstall = true;
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Fs2InstallerViewModel.LoadGoGExeCommand()", ex);
                gogExe = null;
                gogExeFile = null;
                gogBins.Clear();
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
            FolderPickerOpenOptions options = new FolderPickerOpenOptions();
            options.AllowMultiple = false;
            options.Title = "Select your Freespace 2 retail folder";
            var topmostWindow = KnUtils.GetTopLevel();
            var result = await topmostWindow.StorageProvider.OpenFolderPickerAsync(options);

            if (result != null && result.Count > 0)
            {
                CanInstall = false;
                gogExe = null;
                ProcessFolder(result[0]);
            }
        }

        /// <summary>
        /// Search the folder to find all files
        /// </summary>
        /// <param name="path"></param>
        private async void ProcessFolder(IStorageFolder? path, bool topLevel = true)
        {
            try
            {
                if(path == null)
                    throw new ArgumentNullException(nameof(path));
                if (topLevel)
                {
                    reqFilesFound = 0;
                    filePaths.Clear();
                }

                var items = path.GetItemsAsync();
                await foreach (var item in items)
                {
                    if (item is IStorageFile file)
                    {
                        var isImportant = required.FirstOrDefault(x=> x.ToLower() == file.Name.ToLower());
                        if(isImportant != null)
                        {
                            reqFilesFound ++;
                            filePaths.Add(file);
                        }
                        var isOptional = optional.FirstOrDefault(x => x.ToLower() == file.Name.ToLower());
                        if (isOptional != null)
                        {
                            filePaths.Add(file);
                        }
                    }
                    else if (item is IStorageFolder subfolder)
                    {
                        ProcessFolder(subfolder, false);
                    }
                }

                if(topLevel)
                {
                    if (reqFilesFound < 9)
                    {
                        //Missing files
                        await MessageBox.Show(MainWindow.instance, "Unable to find all the required Freespace 2 files in this directory.", "Files not found", MessageBox.MessageBoxButtons.OK);
                        return;
                    }
                    CanInstall = true;
                    ProgressMax = filePaths.Count();
                }
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "Fs2InstallerViewModel.ProcessFolder()", ex);
            }
        }
    }
}
